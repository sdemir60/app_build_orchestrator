using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29 · 2026-11-05] Sistem seçicileri (klasör, açılacak dosya, kaydedilecek dosya) pencerenin üzerinde,
/// dolayısıyla Settings diyaloğunun tam üstünde ortalı açılır. Ortalamayı <c>CenteredDialog</c> kapsamı yapar; kapsamsız açılan seçiciyi
/// Windows sahibinin sol üst köşesine koyar.
///
/// <para><b>Neden yapısal pin:</b> ortalama bir kompozisyon olayıdır — ekran dışı testler seçiciyi göremez, gerçek seçiciyi açan sonda
/// (<see cref="FileDialogCenteringProbeTests"/>) ortam değişkeniyle kapılıdır. Bu yüzden kural kaynakta çitlenir: her sistem seçicisi
/// TEK yardımcıda kurulur (<c>Shell/SystemPicker.cs</c>) ve o yardımcı her seçiciyi kapsamın içinde açar.</para>
///
/// <para><b>[ölçülen kusur 2026-11-05]</b> Yalnız Import seçicisi ortalanıyordu. Repo kökünün Browse'u (<c>MainWindow.PickFolder</c>)
/// ve Export'un kayıt seçicisi kapsamsız açılıyordu; kullanılmayan ikinci bir klasör seçici kopyası da <c>OsActions</c>'ta duruyordu.</para>
/// </summary>
public sealed class CenteredPickerGuardTests
{
    private const string Picker = @"Shell\SystemPicker.cs";

    /// <summary>Bir sistem seçicisinin kurulması — tam adla ya da kısa adla.</summary>
    private static readonly Regex NewSystemPicker =
        new(@"new\s+(?:Microsoft\.Win32\.)?(?:OpenFolderDialog|OpenFileDialog|SaveFileDialog)\b", RegexOptions.Compiled);

    [Fact]
    public void Every_system_picker_is_built_in_the_one_centred_helper()
    {
        var offenders = SourceGuard.ScanApp("*.cs", NewSystemPicker, [Picker], skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "Sistem seçicisi SystemPicker dışında kuruluyor (ortalanmadan açılır):\n  " + string.Join("\n  ", offenders));
        Assert.NotEmpty(SourceGuard.ScanApp("*.cs", NewSystemPicker, skipCommentLines: true)); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_helper_opens_every_picker_inside_the_centring_scope()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, Picker));

        Assert.Single(Regex.Matches(source, @"\.ShowDialog\s*\("));       // tek gösterim noktası…
        Assert.Matches(@"using\s*\(\s*CenteredDialog\.Over\s*\(", source); // …ve o nokta kapsamın içinde
    }

    [Fact]
    public void The_rule_recognises_a_picker_built_elsewhere()
    {
        Assert.Single(SourceGuard.ScanText("MainWindow.xaml.cs",
            "var dialog = new Microsoft.Win32.OpenFolderDialog { Title = \"Select repository root\" };", NewSystemPicker));
        Assert.Single(SourceGuard.ScanText("Services/OsActions.cs", "var d = new SaveFileDialog();", NewSystemPicker));
    }
}
