using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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
        byte[]? publicKey = null, CustomAttributeBuilder[]? typeAttributes = null)
    {
        var name = new AssemblyName("Surface.Probe");
        if (version is not null) name.Version = version;
        if (publicKey is not null) name.SetPublicKey(publicKey);
        var builder = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var type = builder.DefineDynamicModule("M").DefineType("N.C", TypeAttributes.Public | TypeAttributes.Class);
        foreach (var attribute in typeAttributes ?? []) type.SetCustomAttribute(attribute);
        shape?.Invoke(type);
        type.CreateType();
        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();
    }

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
