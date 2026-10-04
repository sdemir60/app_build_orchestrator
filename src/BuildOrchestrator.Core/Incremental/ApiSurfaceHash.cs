using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [cycle rounds — API kısa devresi] Bir derleme çıktısının (DLL/EXE) DIŞA GÖRÜNÜR yüzeyinin özeti: assembly
/// kimliği + private OLMAYAN tipler ve üyeler (imzaları ÇÖZÜLMÜŞ hâlde — ham blob DEĞİL, çünkü blob'lar
/// TypeRef satır numarası taşır ve gövde değişimi o tabloyu yeniden numaralandırabilir) + öznitelikleri
/// (parametre, dönüş değeri ve generic parametre öznitelikleri dahil: <c>params</c>, <c>decimal</c> varsayılanı,
/// <c>Caller*</c> gibi olanlar çağrı biçimini belirler) + açık tip layout'u (<c>StructLayout</c> Size/Pack).
/// GÖVDE (IL), MVID, timestamp ve tablo sırası özete GİRMEZ — yalnız gövdesi değişen bir yeniden derleme AYNI
/// özeti üretir. SCC tur döngüsü bunu "bu üyenin bağlandığı API sonradan değişti mi" sorusuna kanıt yapar
/// (<see cref="Core.Planning.CycleRoundPolicy"/>'nin <c>staleNow</c> girdisi).
///
/// <para><b>Sürüm kuralı:</b> assembly SÜRÜMÜ özete yalnız assembly STRONG-NAMED ise girer (public key blob'u
/// varsa). Strong-name'siz assembly'lerde loader sürümü zaten simple-name ile çözer; wildcard
/// <c>AssemblyVersion</c> kullanan eski projelerde sürümü saymak kısa devreyi bedavaya öldürürdü.
/// Strong-named'de ise sürüm bağlamanın parçasıdır ve değişimi yüzey değişimidir.</para>
///
/// <para><b>Internal üyeler DAHİLDİR</b> (muhafazakâr yön): <c>InternalsVisibleTo</c> ile bir kardeş, internal
/// yüzeye bağlanabilir. Private üyeler (değer tipinin alanları hariç — aşağıdaki paragraf) ve derleyici üretimi
/// adlar (<c>&lt;</c> içeren) hariçtir — gövde
/// değişiminde derleyicinin ürettiği state-machine/closure adları kayar ve özet boşuna oynardı; aynı nedenle
/// üretilmiş tip ADI taşıyan <c>AsyncStateMachine</c>/<c>IteratorStateMachine</c> öznitelikleri ile
/// <c>CompilerGenerated</c> ve (derleme kipine bağlı) <c>Debuggable</c> da sayılmaz. WPF işaretleme derleyicisinin
/// ürettiği XAML yükleyici yardımcı tipi (tam adı <c>XamlGeneratedNamespace.GeneratedInternalTypeHelper</c>) da
/// hariçtir: kaynağa değil <c>obj</c>'deki artımlı işaretleme durumuna bağlı olarak bir derlemede belirir, ötekinde
/// kaybolur — sayılsaydı içerik aynıyken yüzey "değişti" görünürdü. Dışlama YALNIZ bu tam addır
/// (<c>XamlGeneratedNamespace.*</c> değil).</para>
///
/// <para><b>Değer tipinin private alanları DAHİLDİR:</b> kesin atama ve <c>unmanaged</c> kuralı struct'ın BÜTÜN
/// alanlarına bakar (Roslyn reference assembly'leri de bu yüzden struct alanlarını atmaz); alanların tipi
/// <c>sizeof</c>'u ve layout'u belirler. SINIFTA private alan yüzey değildir — ona kimse bağlanamaz.</para>
///
/// <para>Yanılma yönü BİLİNÇLİDİR: kuşkuda "değişti" demek fazladan bir tur satın alır (doğruluk bozulmaz),
/// "değişmedi" demek ise eski API'ye bağlı bir çıktıyı persist ettirirdi — bu yüzden dahil etme kuralları
/// geniş, hariç tutma kuralları dardır.</para>
/// </summary>
public static class ApiSurfaceHash
{
    /// <summary>Dosya diskte YOKKEN <see cref="OfFile"/>'ın döndürdüğü kararlı işaret — "yok" da bir
    /// durumdur ve iki tur üst üste "yok" değişmemiş demektir (okunamayan dosya ise <c>null</c>'dur).</summary>
    public const string Absent = "absent";

    /// <summary>Dosyanın yüzey özeti; dosya yoksa <see cref="Absent"/>, okunamıyorsa ya da geçerli bir
    /// yönetilen PE değilse <c>null</c>. Hiç fırlatmaz — okunamayan dosya kanıt değildir, karar çağıranındır.</summary>
    public static string? OfFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (!File.Exists(path)) return Absent;
            // ReadWrite|Delete paylaşımı: dosyanın SAHİBİ derleme/kopya süreçleridir; kilitli bir kopyayı
            // okuyamamak normaldir ve null'a düşer (çağıran "değişti" sayar — güvenli yön).
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return OfStream(stream);
        }
        catch { return null; }
    }

    /// <summary>Akıştaki yönetilen PE'nin yüzey özeti (hex SHA-256); metadata okunamıyorsa <c>null</c>.
    /// Saf: akışı yalnız okur.</summary>
    public static string? OfStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return null;
            var reader = pe.GetMetadataReader();

            var text = new StringBuilder();
            AppendAssembly(reader, text);
            AppendExportedTypes(reader, text);
            AppendTypes(reader, text);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
        catch (BadImageFormatException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // ---------------------------------------------------------------- assembly kimliği

    private static void AppendAssembly(MetadataReader reader, StringBuilder text)
    {
        if (!reader.IsAssembly) return;
        var assembly = reader.GetAssemblyDefinition();
        text.Append("assembly ").Append(reader.GetString(assembly.Name));
        // Sürüm YALNIZ strong-name varken kimliğin parçasıdır (tip özetindeki gerekçe).
        if (!assembly.PublicKey.IsNil && reader.GetBlobBytes(assembly.PublicKey).Length > 0)
            text.Append(" sn=").Append(Convert.ToHexString(reader.GetBlobBytes(assembly.PublicKey)))
                .Append(" v=").Append(assembly.Version);
        text.Append('\n');
        AppendAttributes(reader, assembly.GetCustomAttributes(), text);
    }

    /// <summary>Type forwarder'lar tüketicinin bağlandığı adları taşır — yüzeydir.</summary>
    private static void AppendExportedTypes(MetadataReader reader, StringBuilder text)
    {
        var lines = new List<string>();
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            lines.Add("exported " + reader.GetString(exported.Namespace) + "." + reader.GetString(exported.Name));
        }
        lines.Sort(StringComparer.Ordinal);
        foreach (string line in lines) text.Append(line).Append('\n');
    }

    // ---------------------------------------------------------------- tipler ve üyeler

    private static void AppendTypes(MetadataReader reader, StringBuilder text)
    {
        // Tip sırası METİN üzerinden sabitlenir (tablo sırası derleyicinin işidir, yüzeyin değil).
        var rendered = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IncludeType(reader, type)) continue;
            rendered.Add(RenderType(reader, type));
        }
        rendered.Sort(StringComparer.Ordinal);
        foreach (string line in rendered) text.Append(line);
    }

    /// <summary>WPF işaretleme derleyicisinin ürettiği XAML yükleyici yardımcı tipinin tam adı;
    /// <see cref="IncludeType"/> bu tipi özetin dışında tutar. Adın gerçek WPF çıktısıyla eşleştiği birim testle değil
    /// gerçek derlemeyle doğrulandı (OSYS'te aynı kaynaktan iki derleme, bu tip dışlanınca aynı özet).</summary>
    internal const string XamlLoaderHelperType = "XamlGeneratedNamespace.GeneratedInternalTypeHelper";

    private static bool IncludeType(MetadataReader reader, TypeDefinition type)
    {
        string fullName = FullNameOf(reader, type);
        // '<Module>', '<PrivateImplementationDetails>', closure/state-machine tipleri: üretilmiş adlar.
        if (fullName.Contains('<')) return false;
        // WPF'in XAML yükleyici yardımcısı: hiçbir kardeş ona bağlanamaz (aynı tam ad her WPF derlemesinde AYRI
        // üretilir; yükleyici yansımayla YEREL derlemedekini kullanır) ve varlığı kaynağa değil obj'deki artımlı
        // işaretleme durumuna bağlıdır — sayılırsa içerik aynıyken yüzey "değişti" görünür ve okuyan herkes
        // boşuna yeniden derlenir. YALNIZ bu tam ad: "XamlGeneratedNamespace.*" gibi geniş bir kural, aynı
        // ad alanındaki gerçek bir tipi de yutardı.
        if (fullName == XamlLoaderHelperType) return false;
        return (type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.NestedPrivate;
    }

    /// <summary>Struct <c>System.ValueType</c>'tan, enum <c>System.Enum</c>'dan türer.</summary>
    private static readonly string[] ValueTypeBases = ["System.ValueType", "System.Enum"];

    /// <summary>Taban tip adı (<see cref="RenderTypeHandle"/> çıktısı) değer tipi tabanı mı? Ad ya olduğu gibi
    /// (core assembly'nin kendi tipleri) ya da <c>[assembly]ad</c> biçiminde (TypeRef) gelir; köşeli parantezli sonek
    /// eşlemesi <c>MySystem.ValueType</c> gibi bir adı yanlışlıkla yakalamaz.</summary>
    private static bool IsValueTypeBase(string baseName)
    {
        foreach (string known in ValueTypeBases)
            if (baseName == known || baseName.EndsWith("]" + known, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string RenderType(MetadataReader reader, TypeDefinition type)
    {
        var provider = new NameProvider();
        var text = new StringBuilder();
        string baseName = RenderTypeHandle(reader, type.BaseType, provider);
        text.Append("type ").Append(FullNameOf(reader, type))
            .Append(" attrs=").Append((int)type.Attributes)
            .Append(" base=").Append(baseName);

        var interfaces = new List<string>();
        foreach (var handle in type.GetInterfaceImplementations())
            interfaces.Add(RenderTypeHandle(reader, reader.GetInterfaceImplementation(handle).Interface, provider));
        interfaces.Sort(StringComparer.Ordinal);
        if (interfaces.Count > 0) text.Append(" impl=").Append(string.Join(",", interfaces));
        // Açık layout (StructLayout Size/Pack) ClassLayout tablosundadır; unsafe tüketicide sizeof'u değiştirir.
        var layout = type.GetLayout();
        if (!layout.IsDefault) text.Append(" layout=").Append(layout.Size).Append('/').Append(layout.PackingSize);

        // Generic parametre öznitelikleri ([DynamicallyAccessedMembers], nullable...) ana satırdan SONRA, ayrı satırlardır.
        var genericParameterAttributes = new StringBuilder();
        AppendGenericParameters(reader, type.GetGenericParameters(), provider, text, genericParameterAttributes,
            indent: "");
        text.Append('\n').Append(genericParameterAttributes);
        AppendAttributes(reader, type.GetCustomAttributes(), text);

        var members = new List<string>();
        bool isValueType = IsValueTypeBase(baseName);
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            string name = reader.GetString(field.Name);
            if (name.Contains('<')) continue;
            var access = field.Attributes & FieldAttributes.FieldAccessMask;
            // Değer tipinde private alan da yüzeydir (kesin atama, unmanaged, layout); sınıfta değildir.
            if (access == FieldAttributes.Private && !isValueType) continue;
            var line = new StringBuilder();
            line.Append("  field ").Append(name)
                .Append(':').Append(field.DecodeSignature(provider, null))
                .Append(" attrs=").Append((int)field.Attributes)
                .Append(RenderConstant(reader, field.GetDefaultValue()))
                .Append('\n');
            AppendAttributes(reader, field.GetCustomAttributes(), line, indent: "  ");
            members.Add(line.ToString());
        }

        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!IncludeMethod(reader, method)) continue;
            members.Add(RenderMethod(reader, method, provider));
        }

        // Property/event tabloları accessor'ların ÜSTÜNDE ayrı öznitelik taşır ([Obsolete] property'e yazılır,
        // get_/set_'e değil) — görünür accessor'ı olanlar dahil edilir.
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            var accessors = property.GetAccessors();
            if (!AnyVisibleAccessor(reader, accessors.Getter, accessors.Setter)) continue;
            var line = new StringBuilder();
            var signature = property.DecodeSignature(provider, null);
            line.Append("  property ").Append(reader.GetString(property.Name))
                .Append('(').Append(string.Join(",", signature.ParameterTypes)).Append("):")
                .Append(signature.ReturnType).Append('\n');
            AppendAttributes(reader, property.GetCustomAttributes(), line, indent: "  ");
            members.Add(line.ToString());
        }

        foreach (var handle in type.GetEvents())
        {
            var @event = reader.GetEventDefinition(handle);
            var accessors = @event.GetAccessors();
            if (!AnyVisibleAccessor(reader, accessors.Adder, accessors.Remover)) continue;
            var line = new StringBuilder();
            line.Append("  event ").Append(reader.GetString(@event.Name))
                .Append(':').Append(RenderTypeHandle(reader, @event.Type, provider)).Append('\n');
            AppendAttributes(reader, @event.GetCustomAttributes(), line, indent: "  ");
            members.Add(line.ToString());
        }

        members.Sort(StringComparer.Ordinal);
        foreach (string member in members) text.Append(member);
        return text.ToString();
    }

    private static bool IncludeMethod(MetadataReader reader, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        if (name.Contains('<')) return false; // derleyici üretimi (lambda/local function gövdeleri)
        // .cctor zaten private'tır ve düşer; public/protected/internal/protected-internal kalır.
        return (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Private;
    }

    private static string RenderMethod(MetadataReader reader, MethodDefinition method, NameProvider provider)
    {
        var signature = method.DecodeSignature(provider, null);
        var line = new StringBuilder();
        line.Append("  method ").Append(reader.GetString(method.Name))
            .Append('`').Append(signature.GenericParameterCount)
            .Append('(').Append(string.Join(",", signature.ParameterTypes)).Append("):")
            .Append(signature.ReturnType)
            .Append(" attrs=").Append((int)method.Attributes);

        // Parametre ADLARI ve varsayılanları yüzeydir: named argument ve optional çağrılar onlara bağlanır.
        // Öznitelikleri de (params, decimal/DateTime varsayılanı — Constant tablosunda DEĞİL öznitelikte durur —,
        // Caller*, Dynamic, tuple adları, nullable...) çağrı biçimini belirler; ana satırdan SONRA ayrı satırlardır.
        // Dönüş değeri SequenceNumber 0'lı satırdır ve aynı döngüden geçer.
        var parameterAttributes = new StringBuilder();
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            line.Append(" p").Append(parameter.SequenceNumber).Append('=')
                .Append(reader.GetString(parameter.Name))
                .Append('/').Append((int)parameter.Attributes)
                .Append(RenderConstant(reader, parameter.GetDefaultValue()));
            AppendAttributes(reader, parameter.GetCustomAttributes(), parameterAttributes,
                indent: "  p" + parameter.SequenceNumber.ToString(CultureInfo.InvariantCulture) + " ");
        }
        var genericParameterAttributes = new StringBuilder();
        AppendGenericParameters(reader, method.GetGenericParameters(), provider, line, genericParameterAttributes,
            indent: "  ");
        line.Append('\n').Append(parameterAttributes).Append(genericParameterAttributes);
        AppendAttributes(reader, method.GetCustomAttributes(), line, indent: "  ");
        return line.ToString();
    }

    private static bool AnyVisibleAccessor(MetadataReader reader, params MethodDefinitionHandle[] accessors)
    {
        foreach (var handle in accessors)
        {
            if (handle.IsNil) continue;
            if (IncludeMethod(reader, reader.GetMethodDefinition(handle))) return true;
        }
        return false;
    }

    /// <summary>Generic parametrenin adı, bayrakları ve kısıtları <paramref name="text"/>'e (üyenin ana satırına)
    /// yazılır; ÖZNİTELİKLERİ <paramref name="attributeLines"/>'a ayrı satırlar olarak — çağıran bunları ana satırın
    /// satır sonundan SONRA ekler. <paramref name="indent"/> o üyenin öznitelik satırı girintisidir.</summary>
    private static void AppendGenericParameters(MetadataReader reader, GenericParameterHandleCollection handles,
        NameProvider provider, StringBuilder text, StringBuilder attributeLines, string indent)
    {
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            text.Append(" gp").Append(parameter.Index).Append('=').Append(reader.GetString(parameter.Name))
                .Append('/').Append((int)parameter.Attributes);
            var constraints = new List<string>();
            foreach (var constraintHandle in parameter.GetConstraints())
                constraints.Add(RenderTypeHandle(reader,
                    reader.GetGenericParameterConstraint(constraintHandle).Type, provider));
            constraints.Sort(StringComparer.Ordinal);
            if (constraints.Count > 0) text.Append(':').Append(string.Join("&", constraints));
            AppendAttributes(reader, parameter.GetCustomAttributes(), attributeLines,
                indent: indent + "gp" + parameter.Index.ToString(CultureInfo.InvariantCulture) + " ");
        }
    }

    // ---------------------------------------------------------------- öznitelikler

    /// <summary>Üretilmiş tip ADI taşıyan ya da derleme kipiyle oynayan öznitelikler — bunlar yüzey değil,
    /// derleyicinin kendi kendine notudur ve gövde değişiminde kayarlar.</summary>
    private static readonly string[] ExcludedAttributes =
    [
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Diagnostics.DebuggableAttribute",
    ];

    private static void AppendAttributes(MetadataReader reader, CustomAttributeHandleCollection handles,
        StringBuilder text, string indent = "")
    {
        var lines = new List<string>();
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            string typeName = AttributeTypeName(reader, attribute.Constructor);
            if (Array.IndexOf(ExcludedAttributes, typeName) >= 0) continue;
            // Değer blob'u token TAŞIMAZ (yalnız serileştirilmiş sabitler ve tip ADLARI) — ham hex güvenlidir.
            lines.Add(indent + "attr " + typeName + "=" + Convert.ToHexString(reader.GetBlobBytes(attribute.Value)));
        }
        lines.Sort(StringComparer.Ordinal);
        foreach (string line in lines) text.Append(line).Append('\n');
    }

    private static string AttributeTypeName(MetadataReader reader, EntityHandle constructor) =>
        constructor.Kind switch
        {
            HandleKind.MethodDefinition => FullNameOf(reader,
                reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)constructor)
                    .GetDeclaringType())),
            HandleKind.MemberReference => RenderTypeHandle(reader,
                reader.GetMemberReference((MemberReferenceHandle)constructor).Parent, new NameProvider()),
            _ => constructor.Kind.ToString(),
        };

    // ---------------------------------------------------------------- ad çözümleme

    private static string FullNameOf(MetadataReader reader, TypeDefinition type)
    {
        string name = reader.GetString(type.Name);
        if (type.IsNested)
            return FullNameOf(reader, reader.GetTypeDefinition(type.GetDeclaringType())) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string FullNameOf(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);
        string ns = reader.GetString(reference.Namespace);
        string local = ns.Length == 0 ? name : ns + "." + name;
        return reference.ResolutionScope.Kind switch
        {
            // İç içe TypeRef: kapsam, DIŞ tipin kendisidir.
            HandleKind.TypeReference => FullNameOf(reader, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name,
            // Tip kimliğine assembly SIMPLE adı girer (sürüm değil — sürüm kuralı assembly kimliğinde).
            HandleKind.AssemblyReference => "[" + reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name) + "]" + local,
            _ => local,
        };
    }

    private static string RenderTypeHandle(MetadataReader reader, EntityHandle handle, NameProvider provider) =>
        handle.IsNil ? "-" : handle.Kind switch
        {
            HandleKind.TypeDefinition => FullNameOf(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            HandleKind.TypeReference => FullNameOf(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(provider, null),
            _ => handle.Kind.ToString(),
        };

    private static string RenderConstant(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil) return "";
        var constant = reader.GetConstant(handle);
        return " const=" + constant.TypeCode + ":" + Convert.ToHexString(reader.GetBlobBytes(constant.Value));
    }

    /// <summary>İmzaları TİP ADLARINA çözen sağlayıcı — ham imza blob'undaki TypeRef/TypeDef token'ları tablo
    /// SATIR NUMARASIDIR ve gövde değişimi tabloları yeniden numaralandırabilir; adlar bundan etkilenmez.
    /// Sınıf durumsuz olduğundan <c>genericContext</c> kullanılmaz (parametreler indeksle yazılır).</summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            FullNameOf(reader, reader.GetTypeDefinition(handle));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            FullNameOf(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[r" + shape.Rank + "]";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetByReferenceType(string elementType) => "ref " + elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
            (isRequired ? "modreq(" : "modopt(") + modifier + ")" + unmodifiedType;
        public string GetPinnedType(string elementType) => elementType + " pinned";
        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            "fnptr(" + string.Join(",", signature.ParameterTypes) + "):" + signature.ReturnType;
    }
}
