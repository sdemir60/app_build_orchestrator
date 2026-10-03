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
    /// Motor her açılışta yazar: içerik aynıysa dosyaya DOKUNMAZ (mtime korunur), bozulmuş ya da eski bir içerik
    /// onarılır. Atomik yazım yoktur — motor tek yazıcıdır.
    /// </summary>
    [Fact]
    public void rewriting_is_idempotent_and_does_not_touch_an_identical_file()
    {
        using var dir = new TempDir();
        string path = WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
        var stamp = File.GetLastWriteTimeUtc(path);

        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

        File.WriteAllText(path, "<Project />");
        WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
        Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(path)); // bozulan içerik onarılır
    }
}
