using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [cycle rounds — API kısa devresi] <see cref="ApiSurfaceHash"/>: bir yönetilen PE'nin DIŞA GÖRÜNÜR yüzey
/// özeti. Kritik iddia (1): yalnız GÖVDESİ değişen iki derleme AYNI özeti üretir (MVID/timestamp/IL özete
/// girmez) — SCC tur döngüsünün "ikinci tur gereksiz" kanıtı buna dayanır. Kritik iddia (2): görünür yüzeye
/// (public/internal üye, öznitelik, strong-name'li sürüm) dokunan her değişim özeti DEĞİŞTİRİR — kısa devre
/// asla gerçek bir API değişimini yutmaz. Assembler gerçek PE üretir (<see cref="PersistedAssemblyBuilder"/>),
/// sahte byte dizisi YOK; WPF/process/sleep YOK [D8].
/// </summary>
public class ApiSurfaceHashTests
{
    private static byte[] Assembly(Action<TypeBuilder>? shape = null, Version? version = null,
        byte[]? publicKey = null, CustomAttributeBuilder[]? typeAttributes = null,
        bool valueType = false, int size = 0)
    {
        var name = new AssemblyName("Surface.Probe");
        if (version is not null) name.Version = version;
        if (publicKey is not null) name.SetPublicKey(publicKey);
        var builder = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        // Struct'ın tabanı ValueType'tır; size > 0 ise ClassLayout tablosuna yazılır (StructLayout(Size = …)).
        var type = builder.DefineDynamicModule("M").DefineType("N.C",
            valueType
                ? TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout
                : TypeAttributes.Public | TypeAttributes.Class,
            valueType ? typeof(ValueType) : null);
        foreach (var attribute in typeAttributes ?? []) type.SetCustomAttribute(attribute);
        shape?.Invoke(type);
        type.CreateType();
        // DefineType(packsize, typesize) persisted çıktıda YOK SAYILIR (ölçüldü: ClassLayout satırı yazılmaz).
        if (size > 0) return SaveWithClassLayout(builder, size);
        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();
    }

    /// <summary><see cref="PersistedAssemblyBuilder"/>'ın ürettiği metadata'ya <c>N.C</c> için elle bir ClassLayout
    /// satırı (<c>StructLayout(Size = …)</c>) ekleyip PE'yi yeniden kurar. <c>N.C</c> ikinci TypeDef'tir
    /// (<c>&lt;Module&gt;</c> birincidir).</summary>
    private static byte[] SaveWithClassLayout(PersistedAssemblyBuilder builder, int size)
    {
        var metadata = builder.GenerateMetadata(out System.Reflection.Metadata.BlobBuilder il,
            out System.Reflection.Metadata.BlobBuilder fieldData);
        metadata.AddTypeLayout(System.Reflection.Metadata.Ecma335.MetadataTokens.TypeDefinitionHandle(2), 0, (uint)size);
        var pe = new System.Reflection.PortableExecutable.ManagedPEBuilder(
            System.Reflection.PortableExecutable.PEHeaderBuilder.CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(metadata), il, mappedFieldData: fieldData);
        var blob = new System.Reflection.Metadata.BlobBuilder();
        pe.Serialize(blob);
        return blob.ToArray();
    }

    /// <summary>Public bir int alanı olan struct (<c>N.C</c>); <paramref name="shape"/> ek üye ekler,
    /// <paramref name="size"/> sıfırdan büyükse açık layout boyutudur. Struct testlerinin TEK kaynağı.</summary>
    private static byte[] ValueTypeAssembly(Action<TypeBuilder>? shape = null, int size = 0) =>
        Assembly(t =>
        {
            t.DefineField("Amount", typeof(int), FieldAttributes.Public);
            shape?.Invoke(t);
        }, valueType: true, size: size);

    /// <summary>Sabit dönen bir metot — gövde, döndürdüğü sabitten ibarettir: aynı imza + farklı sabit =
    /// "yalnız gövdesi değişti"nin en küçük gerçek örneği.</summary>
    private static void Method(TypeBuilder type, string name, int returnedConstant,
        MethodAttributes attributes = MethodAttributes.Public)
    {
        var method = type.DefineMethod(name, attributes, typeof(int), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, returnedConstant);
        il.Emit(OpCodes.Ret);
    }

    private static string? HashOf(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly);
        return ApiSurfaceHash.OfStream(stream);
    }

    // Strong-name işareti: içerik değil VARLIĞI önemli — metadata'ya public key blob'u olarak yazılır.
    private static byte[] FakePublicKey() => [.. Enumerable.Range(1, 160).Select(i => (byte)i)];

    /// <summary>Gövdesi yalnız <c>ret</c> olan public static void metot; çağıran dönen builder'a parametre ya da
    /// generic parametre ekleyip öznitelik yazabilir.</summary>
    private static MethodBuilder StaticVoid(TypeBuilder type, string name, params Type[] parameterTypes)
    {
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), parameterTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Tek parametreli metot: <paramref name="decorate"/> parametreye (SequenceNumber 1),
    /// <paramref name="decorateReturnValue"/> dönüş değerine (SequenceNumber 0) öznitelik yazar. Dönüş değeri satırı
    /// HER ZAMAN tanımlanır: iki varyantın tek farkı öznitelik olsun — satırın varlığı (zaten özetlenen ad/bayrak
    /// metni) olmasın, yoksa test düzeltmeden ÖNCE de geçerdi.</summary>
    private static void MethodWithParameter(TypeBuilder type, string name, Type parameterType,
        Action<ParameterBuilder>? decorate = null, Action<ParameterBuilder>? decorateReturnValue = null)
    {
        var method = StaticVoid(type, name, parameterType);
        var returnValue = method.DefineParameter(0, ParameterAttributes.None, null);
        var parameter = method.DefineParameter(1, ParameterAttributes.None, "value");
        decorateReturnValue?.Invoke(returnValue);
        decorate?.Invoke(parameter);
    }

    private static CustomAttributeBuilder Attribute<T>(params object[] args) where T : Attribute =>
        new(typeof(T).GetConstructor(args.Select(a => a.GetType()).ToArray())!, args);

    /// <summary>Generic parametrede gerçekte görülen bir öznitelik (<c>[DynamicallyAccessedMembers]</c> T).</summary>
    private static CustomAttributeBuilder GenericParameterAnnotation() =>
        Attribute<DynamicallyAccessedMembersAttribute>(DynamicallyAccessedMemberTypes.PublicConstructors);

    [Fact]
    public void same_declarations_with_different_bodies_hash_identically()
    {
        // İki AYRI emit: MVID ve timestamp kesin farklı — özet yine aynı olmalı, yoksa her yeniden derleme
        // "API değişti" okunur ve kısa devre hiç çalışmaz.
        string? first = HashOf(Assembly(t => Method(t, "M", returnedConstant: 1)));
        string? second = HashOf(Assembly(t => Method(t, "M", returnedConstant: 2)));

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void a_new_public_method_changes_the_hash()
    {
        string? one = HashOf(Assembly(t => Method(t, "M", 1)));
        string? two = HashOf(Assembly(t => { Method(t, "M", 1); Method(t, "Extra", 1); }));

        Assert.NotEqual(one, two);
    }

    [Fact] // InternalsVisibleTo ile bir kardeş internal yüzeye bağlanabilir — internal üye YÜZEYDİR.
    public void an_internal_method_changes_the_hash()
    {
        string? without = HashOf(Assembly(t => Method(t, "M", 1)));
        string? with = HashOf(Assembly(t => { Method(t, "M", 1); Method(t, "Hidden", 1, MethodAttributes.Assembly); }));

        Assert.NotEqual(without, with);
    }

    [Fact] // Private üyeye kimse bağlanamaz — yüzey DEĞİLDİR; sayılsaydı özel yardımcı eklemek turları şişirirdi.
    public void a_private_method_does_not_change_the_hash()
    {
        string? without = HashOf(Assembly(t => Method(t, "M", 1)));
        string? with = HashOf(Assembly(t => { Method(t, "M", 1); Method(t, "Helper", 1, MethodAttributes.Private); }));

        Assert.Equal(without, with);
    }

    [Fact] // Derleyici üretimi adlar ('<' önekli) gövdeyle birlikte kayar — internal görünürlükte bile sayılmaz.
    public void a_compiler_generated_member_does_not_change_the_hash()
    {
        string? without = HashOf(Assembly(t => Method(t, "M", 1)));
        string? with = HashOf(Assembly(t => { Method(t, "M", 1); Method(t, "<M>b__0_0", 1, MethodAttributes.Assembly); }));

        Assert.Equal(without, with);
    }

    [Fact] // Strong-name YOKKEN loader sürüme bakmaz; wildcard AssemblyVersion kısa devreyi öldürmemeli.
    public void an_assembly_version_bump_without_a_strong_name_does_not_change_the_hash()
    {
        string? v1 = HashOf(Assembly(t => Method(t, "M", 1), version: new Version(1, 0, 0, 0)));
        string? v2 = HashOf(Assembly(t => Method(t, "M", 1), version: new Version(2, 0, 0, 0)));

        Assert.Equal(v1, v2);
    }

    [Fact] // Strong-name VARKEN sürüm bağlamanın parçasıdır: değişimi yüzey değişimidir.
    public void an_assembly_version_bump_with_a_strong_name_changes_the_hash()
    {
        string? v1 = HashOf(Assembly(t => Method(t, "M", 1), version: new Version(1, 0, 0, 0), publicKey: FakePublicKey()));
        string? v2 = HashOf(Assembly(t => Method(t, "M", 1), version: new Version(2, 0, 0, 0), publicKey: FakePublicKey()));

        Assert.NotEqual(v1, v2);
    }

    [Fact] // Öznitelik bağlanmayı değiştirebilir ([Obsolete] as error, [Extension]) — yüzeydir.
    public void a_type_attribute_changes_the_hash()
    {
        var obsolete = new CustomAttributeBuilder(
            typeof(ObsoleteAttribute).GetConstructor([typeof(string)])!, ["gone"]);
        string? without = HashOf(Assembly(t => Method(t, "M", 1)));
        string? with = HashOf(Assembly(t => Method(t, "M", 1), typeAttributes: [obsolete]));

        Assert.NotEqual(without, with);
    }

    [Fact] // params → ParamArrayAttribute: çağrı biçimi (genişletilmiş form) değişir.
    public void a_params_modifier_changes_the_hash()
    {
        string? plain = HashOf(Assembly(t => MethodWithParameter(t, "Sum", typeof(int[]))));
        string? withParams = HashOf(Assembly(t => MethodWithParameter(t, "Sum", typeof(int[]),
            p => p.SetCustomAttribute(Attribute<ParamArrayAttribute>()))));

        Assert.NotEqual(plain, withParams);
    }

    [Fact] // decimal varsayılanı Constant tablosunda DEĞİL, parametredeki DecimalConstantAttribute'tadır.
    public void a_decimal_default_value_changes_the_hash()
    {
        static byte[] Rate(uint low) => Assembly(t => MethodWithParameter(t, "Rate", typeof(decimal),
            p => p.SetCustomAttribute(Attribute<DecimalConstantAttribute>((byte)2, (byte)0, (uint)0, (uint)0, low))));

        Assert.NotEqual(HashOf(Rate(18)), HashOf(Rate(20)));
        // Aynı varsayılan iki AYRI emit'te aynı özeti verir: yeni satırlar MVID/sıra gibi oynak bir şey taşımaz.
        Assert.Equal(HashOf(Rate(18)), HashOf(Rate(18)));
    }

    [Fact] // [CallerMemberName] çağıranın derleyicisine ne dolduracağını söyler — yüzeydir.
    public void a_caller_info_attribute_changes_the_hash()
    {
        string? plain = HashOf(Assembly(t => MethodWithParameter(t, "Who", typeof(string))));
        string? caller = HashOf(Assembly(t => MethodWithParameter(t, "Who", typeof(string),
            p => p.SetCustomAttribute(Attribute<CallerMemberNameAttribute>()))));

        Assert.NotEqual(plain, caller);
    }

    [Fact] // Tip generic parametresi: [DynamicallyAccessedMembers] T gibi — RenderType yolu.
    public void a_generic_parameter_attribute_changes_the_hash()
    {
        static byte[] Generic(bool annotated) => Assembly(t =>
        {
            var parameter = t.DefineGenericParameters("T")[0];
            if (annotated) parameter.SetCustomAttribute(GenericParameterAnnotation());
        });

        Assert.NotEqual(HashOf(Generic(annotated: false)), HashOf(Generic(annotated: true)));
    }

    [Fact] // Metot generic parametresi ayrı yoldan (RenderMethod) yazılır — tip yolundan bağımsız sınanır.
    public void a_method_generic_parameter_attribute_changes_the_hash()
    {
        static byte[] Generic(bool annotated) => Assembly(t =>
        {
            var parameter = StaticVoid(t, "Create").DefineGenericParameters("T")[0];
            if (annotated) parameter.SetCustomAttribute(GenericParameterAnnotation());
        });

        Assert.NotEqual(HashOf(Generic(annotated: false)), HashOf(Generic(annotated: true)));
    }

    [Fact] // [return: ...] parametre tablosunda SequenceNumber 0 satırına yazılır; satır iki varyantta da var.
    public void a_return_value_attribute_changes_the_hash()
    {
        string? plain = HashOf(Assembly(t => MethodWithParameter(t, "Get", typeof(int))));
        string? annotated = HashOf(Assembly(t => MethodWithParameter(t, "Get", typeof(int),
            decorateReturnValue: r => r.SetCustomAttribute(Attribute<NotNullAttribute>()))));

        Assert.NotEqual(plain, annotated);
    }

    [Fact]
    public void a_private_field_of_a_struct_changes_the_hash()
    {
        // Kesin atama ve `unmanaged` kuralı struct'ın BÜTÜN alanlarına bakar — Roslyn reference assembly'leri de bu
        // yüzden struct alanlarını atmaz.
        static byte[] Money(Type padType) =>
            ValueTypeAssembly(t => t.DefineField("_pad", padType, FieldAttributes.Private));

        Assert.NotEqual(HashOf(Money(typeof(int))), HashOf(Money(typeof(object))));
    }

    [Fact] // Sınıfta private alana kimse bağlanamaz: değer tipi genişlemesi sınıfa SIZMAMALI.
    public void a_private_field_of_a_class_still_does_not_change_the_hash()
    {
        string? a = HashOf(Assembly(t => t.DefineField("_pad", typeof(int), FieldAttributes.Private)));
        string? b = HashOf(Assembly(t => t.DefineField("_pad", typeof(object), FieldAttributes.Private)));

        Assert.Equal(a, b);
    }

    [Fact] // StructLayout(Size = …) ClassLayout tablosuna yazılır; unsafe tüketicide sizeof değişir.
    public void an_explicit_struct_layout_size_changes_the_hash()
    {
        Assert.NotEqual(HashOf(ValueTypeAssembly(size: 8)), HashOf(ValueTypeAssembly(size: 16)));
    }

    [Fact]
    public void a_missing_file_reads_absent_and_a_non_pe_file_reads_null()
    {
        string root = Directory.CreateTempSubdirectory("bo-surface-").FullName;
        try
        {
            // "Yok" kararlı bir DURUMDUR (iki tur üst üste "yok" = değişmedi); okunamayan ise kanıt DEĞİLDİR.
            Assert.Equal(ApiSurfaceHash.Absent, ApiSurfaceHash.OfFile(Path.Combine(root, "missing.dll")));

            string garbage = Path.Combine(root, "garbage.dll");
            File.WriteAllText(garbage, "this is not a portable executable");
            Assert.Null(ApiSurfaceHash.OfFile(garbage));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact] // OfFile ile OfStream aynı özeti üretir — dosya yolu yalnız bir taşıyıcıdır.
    public void of_file_matches_of_stream_for_a_real_assembly()
    {
        byte[] assembly = Assembly(t => Method(t, "M", 1));
        string root = Directory.CreateTempSubdirectory("bo-surface-").FullName;
        try
        {
            string path = Path.Combine(root, "probe.dll");
            File.WriteAllBytes(path, assembly);
            Assert.Equal(HashOf(assembly), ApiSurfaceHash.OfFile(path));
            Assert.NotNull(ApiSurfaceHash.OfFile(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
