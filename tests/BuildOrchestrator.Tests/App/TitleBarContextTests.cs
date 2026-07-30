using System.Windows.Controls;
using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.1] Title bar'ın mono bağlam metni — <b>design-v1 §2.1 otoritesi</b>:
///
/// <para><i>"Sol: Delta logosu (dark varyant, 15px) + &quot;Build Orchestrator&quot; başlığı; ardından mono 11px
/// <c>text-dim</c> bağlam: <c>OSYS · main</c> — worktree aktifse <c>· main-2</c> eklenir (<c>text-faint</c>).
/// Repo yokken: <c>no repository</c>."</i></para>
///
/// <para><b>Ölçülen kusur (T2 envanteri):</b> <c>rg ContextText src tests docs</c> TEK isabet veriyordu —
/// <c>MainWindow.xaml:161</c>'deki XAML literali. Hiçbir C# dosyası <c>ContextText.Text</c>'e YAZMIYORDU, yani
/// başlık her koşulda <c>no repository</c>'de donmuştu: repo seçimi de, branch seçimi de, worktree de oraya
/// HİÇ ulaşmıyordu.</para>
///
/// <para><b>Tetikleyici kuralı (A12 dersi):</b> metin üretimdeki yoldan sürülür — VM'in gerçek
/// <c>RootPath</c>/<c>Branch</c>/<c>UseWorktree</c>/<c>WorktreeName</c> özellikleri set edilir ve kablo
/// (<c>PropertyChanged</c> → <c>RefreshTitleContext</c>) çalışsın diye pencere ÖNCE realize edilir, veri SONRA
/// akar. Kablo koparsa metin XAML varsayılanında kalır → kırmızı.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class TitleBarContextTests
{
    /// <summary>Title bar'daki TÜM metin blokları — bağlam gövdesi + (varsa) worktree eki bunların içindedir.
    /// Ek, gövdeden AYRI bir <see cref="TextBlock"/> olmak ZORUNDADIR: iki farklı ton (text-dim gövde /
    /// text-faint ek) tek bir <c>Text</c> literaliyle verilemez.</summary>
    private static IReadOnlyList<TextBlock> TitleBarTexts(MainWindow window) =>
        [.. DsResources.Descendants(window.RootShell).OfType<TextBlock>()];

    private static TextBlock? WorktreeSuffix(MainWindow window) =>
        TitleBarTexts(window).FirstOrDefault(t => t.Text.StartsWith('·'));

    [StaFact]
    public void With_no_repository_the_title_context_reads_the_verbatim_empty_marker()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal("no repository", window.ContextText.Text);
        Assert.Null(WorktreeSuffix(window)); // repo yokken worktree eki de OLMAZ
        GC.KeepAlive(window);
    }

    /// <summary>design-v1 §2.1'in ana iddiası: repo seçilince bağlam <c>&lt;repo-adı&gt; · &lt;branch&gt;</c> olur.
    /// Repo adı kökün KLASÖR adıdır (prototipteki sabit <c>'OSYS'</c> literalinin gerçek karşılığı) — T2
    /// envanterinde ölçüldü ki bu kavram üretimde hiç HESAPLANMIYORDU.</summary>
    [StaFact]
    public void Choosing_a_repository_and_branch_writes_them_into_the_title_context()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window); // kabuk ÖNCE realize — veri SONRA akar (üretim sırası)

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";

        Assert.Equal("OSYS · main", window.ContextText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>Branch daha bilinmiyorken (envanter gelmeden) sallantıda bir ayraç bırakılmaz — yalnız repo adı.</summary>
    [StaFact]
    public void Before_the_branch_is_known_the_context_shows_the_repository_alone()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"D:\Projects\Delta\OSYS\";  // sondaki ayraç repo adını BOZMAMALI

        Assert.Equal("OSYS", window.ContextText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Worktree eki AYRI ve DAHA SOLUK: gövde <c>Brush.TextDim</c>, ek <c>Brush.TextFaint</c> (design-v1 §2.1).
    /// Tek bir TextBlock'a iki ton verilemeyeceği için ek ayrı bir öğedir; ikisi de mono 11px
    /// (<c>FontSize.2xs</c>) kalır.
    /// </summary>
    [StaFact]
    public void An_active_worktree_appends_a_fainter_suffix_next_to_the_context()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var host = DsResources.NewHost(); // token karşılaştırması için AYNI merge zinciri
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = "main-2";

        var suffix = WorktreeSuffix(window);
        Assert.NotNull(suffix);
        Assert.Equal("· main-2", suffix.Text);
        Assert.Equal("OSYS · main", window.ContextText.Text); // gövde EK'İ İÇERMEZ (iki ayrı ton)

        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(suffix.Foreground));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(window.ContextText.Foreground));
        Assert.Equal((double)host.FindResource("FontSize.2xs"), suffix.FontSize); // mono 11px
        GC.KeepAlive(window);
    }

    /// <summary>Worktree kapatılınca ek KAYBOLUR (yapışıp kalmaz).</summary>
    [StaFact]
    public void Turning_the_worktree_off_removes_the_suffix_again()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.RootPath = @"C:\src\OSYS";
        vm.Branch = "main";
        vm.UseWorktree = true;
        vm.WorktreeName = "main-2";
        Assert.NotNull(WorktreeSuffix(window)); // ön-koşul

        vm.UseWorktree = false;

        Assert.Null(WorktreeSuffix(window));
        GC.KeepAlive(window);
    }
}
