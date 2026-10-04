using System.IO;
using Xunit;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [Faz 2 · WPF geçici assembly] Targets dosyasının içeriği ve motorun onu önbellek köküne yazışı. İçerik ve dosya
/// adları TEK yerdedir (<see cref="WpfTemporaryAssemblyTargets"/>); testler onu okur, ikinci bir kopya tutmaz.
/// Gerçek MSBuild ile çıktı eşdeğerliği bu sınıfın değil ayrı bir Acceptance testinin işidir.
/// </summary>
public class WpfTemporaryAssemblyTargetsTests
{
    /// <summary>
    /// İki dosya <c>&lt;önbellek kökü&gt;\msbuild\</c> altına yazılır (kullanıcının çalışma ağacına DEĞİL: OutDir ve
    /// obj düzenine dokunulmaz) ve dönen yol targets dosyasının tam yoludur — <c>-p:CustomBeforeMicrosoftCommonTargets=</c>
    /// argümanına olduğu gibi girer. Klasör adı bilerek literal: diskteki konum belgelenmiş bir sözleşmedir.
    /// </summary>
    [Fact]
    public void writes_both_files_under_msbuild_and_returns_the_targets_path()
    {
        using var dir = new TempDir();

        string path = WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "msbuild", WpfTemporaryAssemblyTargets.TargetsFileName), path);
        Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(path));
        Assert.Equal(WpfTemporaryAssemblyTargets.FriendContent,
            File.ReadAllText(Path.Combine(dir.Path, "msbuild", WpfTemporaryAssemblyTargets.FriendFileName)));
    }

    /// <summary>
    /// Üç öğe BİRLİKTE gerekir ve YALNIZ proje adı <c>_wpftmp</c> ile biten geçici projede etki eder: gövdesiz derleme
    /// (<c>ProduceOnlyReferenceAssembly</c>), SDK-style projede <c>/refout</c>+<c>/refonly</c> çakışmasını (CS8308)
    /// kapatan <c>ProduceReferenceAssembly=false</c> ve internal üyeye bağlanan XAML'ın (MC3072) düşmemesi için
    /// geçici assembly'ye eklenen <c>InternalsVisibleTo</c> dosyası. Global <c>CustomBeforeMicrosoftCommonTargets</c>
    /// MSBuild'in varsayılan Custom.Before dosyasının yerine geçtiği için o dosya zincirde KALIR.
    /// </summary>
    [Fact]
    public void the_targets_only_act_on_wpftmp_projects_and_carry_the_three_elements()
    {
        string t = WpfTemporaryAssemblyTargets.TargetsContent;

        Assert.Contains("$(MSBuildProjectName.EndsWith('_wpftmp'))", t);
        Assert.Contains("<ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>", t);
        Assert.Contains("<ProduceReferenceAssembly>false</ProduceReferenceAssembly>", t);
        Assert.Contains("<Compile Include=\"$(MSBuildThisFileDirectory)" + WpfTemporaryAssemblyTargets.FriendFileName + "\"", t);
        Assert.Contains("InternalsVisibleTo(\"" + WpfTemporaryAssemblyTargets.FriendAssemblyName + "\")",
            WpfTemporaryAssemblyTargets.FriendContent);
        // Varsayılan Custom.Before dosyası zincirde kalır [W6]
        Assert.Contains("Custom.Before.Microsoft.Common.targets", t);
    }

    /// <summary>
    /// Göreli bir önbellek kökü (ör. göreli <c>--logs</c>) MSBuild'in ÇALIŞMA DİZİNİNE göre çözülürdü: <c>-p:</c> yolu
    /// proje klasöründen bakılarak <c>Exists</c> ile sınanır, dosya bulunamayınca optimizasyon HİÇBİR UYARI vermeden
    /// düşerdi. Bu yüzden dönen yol her zaman tam yoldur. (Göreli kök, test çalışma dizini altında benzersiz bir
    /// klasördür ve iş bitince silinir.)
    /// </summary>
    [Fact]
    public void a_relative_cache_root_yields_an_absolute_targets_path()
    {
        string relativeRoot = "bo-relative-root-" + Guid.NewGuid().ToString("N");
        try
        {
            string path = WpfTemporaryAssemblyTargets.EnsureWritten(relativeRoot);

            Assert.True(Path.IsPathRooted(path));
            Assert.Equal(Path.GetFullPath(
                Path.Combine(relativeRoot, "msbuild", WpfTemporaryAssemblyTargets.TargetsFileName)), path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            string full = Path.GetFullPath(relativeRoot);
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }

    /// <summary>
    /// "Targets var ⇒ friend var": friend dosyası targets'tan ÖNCE yazılır. Targets yazımı patlarsa (burada yolunu bir
    /// klasör işgal eder) friend yerindedir; ters sırada, targets'ı gören bir MSBuild olmayan bir kaynak dosyasına
    /// bağlanır ve geçici assembly derlemesi dosya bulunamadı hatasıyla düşerdi.
    /// </summary>
    [Fact]
    public void the_friend_file_is_written_before_the_targets_file()
    {
        using var dir = new TempDir();
        string msbuildDir = Path.Combine(dir.Path, "msbuild");
        Directory.CreateDirectory(Path.Combine(msbuildDir, WpfTemporaryAssemblyTargets.TargetsFileName)); // targets yazımı patlar

        Assert.ThrowsAny<Exception>(() => WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path));

        Assert.Equal(WpfTemporaryAssemblyTargets.FriendContent,
            File.ReadAllText(Path.Combine(msbuildDir, WpfTemporaryAssemblyTargets.FriendFileName)));
    }

    /// <summary>
    /// Motor her açılışta yazar: içerik aynıysa dosyayı YENİDEN YAZMAZ, bozulmuş ya da eski bir içerik onarılır. Atomik
    /// yazım yoktur — motor tek yazıcıdır.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eskiden bu test "aynı içerik = dosyaya dokunulmaz" iddiasını <c>mtime</c>'ın
    /// değişmemesiyle sınıyordu. İki dosyanın tarihi artık HER durumda sabit eski tarihe çekildiği için
    /// (<see cref="WpfTemporaryAssemblyTargets.FixedTimestampUtc"/>) o karşılaştırma bir yeniden yazımı ayırt edemez — yeniden
    /// yazım da aynı tarihle biterdi ve iddia boşalırdı. "Yeniden yazılmadı" artık baytla sınanır: dosya BOM'lu yerleştirilir
    /// (<c>ReadAllText</c> BOM'u soyar, motorun içerik karşılaştırması için içerik AYNIDIR) ve BOM'suz bir yeniden yazım onu
    /// silerdi.</para>
    /// </summary>
    [Fact]
    public void rewriting_is_idempotent_and_does_not_rewrite_an_identical_file()
    {
        using var dir = new TempDir();
        var (targets, friend) = Plant(dir.Path, WpfTemporaryAssemblyTargets.TargetsContent,
            WpfTemporaryAssemblyTargets.FriendContent, withBom: true);
        File.SetLastWriteTimeUtc(targets, WpfTemporaryAssemblyTargets.FixedTimestampUtc); // tarih zaten sabit: yalnız yeniden yazım sınanır
        File.SetLastWriteTimeUtc(friend, WpfTemporaryAssemblyTargets.FixedTimestampUtc);

        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
        Assert.True(StartsWithBom(targets) && StartsWithBom(friend), "an identical file is not rewritten");

        File.WriteAllText(targets, "<Project />");
        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
        Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(targets)); // bozulan içerik onarılır
    }

    /// <summary>
    /// [PERF Faz C/C6] İki dosya ilk yazımdan sonra sabit, ESKİ tarihi taşır — "şimdi"yi değil. MSBuild bu import'u her
    /// projenin girdileri arasında sayar; yeni tarihli bir targets dosyası motorun çağırdığı HER projeyi bir kez baştan
    /// derletirdi (ölçüldü: Rebuild 46 → 78 sn), oysa import nihai çıktıyı değiştirmez.
    /// </summary>
    [Fact]
    public void both_files_carry_the_fixed_old_timestamp_after_the_first_write()
    {
        using var dir = new TempDir();

        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);

        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc,
            File.GetLastWriteTimeUtc(FileOf(dir.Path, WpfTemporaryAssemblyTargets.TargetsFileName)));
        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc,
            File.GetLastWriteTimeUtc(FileOf(dir.Path, WpfTemporaryAssemblyTargets.FriendFileName)));
    }

    /// <summary>Sabit tarih UTC ve "eski"dir: her çıktıdan çok önce — yakın bir tarihe taşınırsa çekmenin anlamı kalmaz.</summary>
    [Fact]
    public void the_fixed_timestamp_is_utc_and_far_in_the_past()
    {
        Assert.Equal(DateTimeKind.Utc, WpfTemporaryAssemblyTargets.FixedTimestampUtc.Kind);
        Assert.True(WpfTemporaryAssemblyTargets.FixedTimestampUtc < DateTime.UtcNow.AddYears(-10));
    }

    /// <summary>
    /// Önceki sürümün dosyayı YENİ bir tarihle yazdığı makine: içerik AYNI (yeniden yazılmaz — BOM baytı kalır) ama tarih
    /// yeni; <c>EnsureWritten</c> tarihi sabit eski tarihe geri çeker.
    /// </summary>
    [Fact]
    public void an_identical_file_with_a_newer_timestamp_is_pulled_back_without_a_rewrite()
    {
        using var dir = new TempDir();
        var (targets, friend) = Plant(dir.Path, WpfTemporaryAssemblyTargets.TargetsContent,
            WpfTemporaryAssemblyTargets.FriendContent, withBom: true);
        Assert.NotEqual(WpfTemporaryAssemblyTargets.FixedTimestampUtc, File.GetLastWriteTimeUtc(targets)); // önkoşul: tarih yeni

        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);

        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc, File.GetLastWriteTimeUtc(targets));
        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc, File.GetLastWriteTimeUtc(friend));
        Assert.True(StartsWithBom(targets) && StartsWithBom(friend), "identical content is not rewritten");
    }

    /// <summary>İçerik farklıysa dosya yazılır VE yeni yazılmış olmasına rağmen tarihi sabit eski tarihe çekilir.</summary>
    [Fact]
    public void a_changed_file_is_rewritten_and_carries_the_fixed_timestamp()
    {
        using var dir = new TempDir();
        var (targets, friend) = Plant(dir.Path, "<Project />", "// stale", withBom: false);

        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);

        Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(targets));
        Assert.Equal(WpfTemporaryAssemblyTargets.FriendContent, File.ReadAllText(friend));
        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc, File.GetLastWriteTimeUtc(targets));
        Assert.Equal(WpfTemporaryAssemblyTargets.FixedTimestampUtc, File.GetLastWriteTimeUtc(friend));
    }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Önbellek kökü altındaki dosya (klasör adı bilerek literal: diskteki konum belgelenmiş bir sözleşmedir).</summary>
    private static string FileOf(string cacheRoot, string name) => Path.Combine(cacheRoot, "msbuild", name);

    /// <summary>İki dosyayı elle, verilen içerikle ve YENİ bir tarihle yerleştirir — önceki bir sürümün bıraktığı hâl.</summary>
    private static (string Targets, string Friend) Plant(string cacheRoot, string targetsContent, string friendContent, bool withBom)
    {
        Directory.CreateDirectory(Path.Combine(cacheRoot, "msbuild"));
        string targets = FileOf(cacheRoot, WpfTemporaryAssemblyTargets.TargetsFileName);
        string friend = FileOf(cacheRoot, WpfTemporaryAssemblyTargets.FriendFileName);
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom);
        File.WriteAllText(targets, targetsContent, encoding);
        File.WriteAllText(friend, friendContent, encoding);
        return (targets, friend); // yeni yazıldı: tarih "şimdi"
    }

    private static bool StartsWithBom(string path) => File.ReadAllBytes(path).Take(Utf8Bom.Length).SequenceEqual(Utf8Bom);
}
