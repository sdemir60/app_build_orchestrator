using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §2.7-5a] <b>Workspace adı alt bara taşındı.</b> Mono 11px (<c>FontSize.2xs</c>),
/// <c>text-dim</c>, <b>branch chip'inin solunda</b>; tooltip repository root'un kendisidir.
///
/// <para><b>Neden:</b> title bar'daki <c>OSYS · main · main-2</c> bağlamının branch ve worktree yarısı alt
/// bardaki chip'lerin AYNISINI söylüyordu. v1.11.0 title bar'ı markaya indirdi; geriye kalan tek yeni bilgi —
/// hangi workspace açık — chip'lerin yanına, aynı okuma satırına geçti.</para>
///
/// <para>Etiket workspace YOKKEN hiç çizilmez: adı olmayan bir workspace'in etiketi de olmaz (prototipte
/// <c>{workspace && …}</c>, BuildApp.jsx:2380).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class WorkspaceLabelTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1");

    private static (ActionBar bar, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        return (bar, DsResources.Realize(host, bar));
    }

    [StaFact]
    public void The_workspace_label_shows_the_repository_folder_name_and_roots_its_tooltip()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        vm.RootPath = @"D:\src\osys";

        Assert.Equal("osys", bar.WorkspaceLabel.Text);
        Assert.Equal(@"D:\src\osys", bar.WorkspaceLabel.ToolTip);
        Assert.Equal(Visibility.Visible, bar.WorkspaceLabel.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void With_no_workspace_the_label_is_not_drawn_at_all()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.Equal(Visibility.Collapsed, bar.WorkspaceLabel.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Mono 11px + <c>text-dim</c> (§2.7-5a) ve branch chip'inin SOLUNDA — sıra da iddianın parçası.</summary>
    [StaFact]
    public void The_label_is_mono_11px_text_dim_and_sits_left_of_the_branch_chip()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var (bar, window) = Realize(vm);
        vm.RootPath = @"D:\src\osys";
        bar.UpdateLayout(); // etiket YENİ göründü — yerleşim iddiaları taze bir pass ister

        Assert.Equal(AppFonts.Mono, bar.WorkspaceLabel.FontFamily);
        Assert.Equal((double)host.FindResource("FontSize.2xs"), bar.WorkspaceLabel.FontSize);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(bar.WorkspaceLabel.Foreground));

        double labelX = bar.WorkspaceLabel.TranslatePoint(new Point(0, 0), bar).X;
        double branchX = bar.BranchChip.TranslatePoint(new Point(0, 0), bar).X;
        Assert.True(labelX < branchX, $"workspace etiketi branch chip'inin solunda değil ({labelX} ≥ {branchX})");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Etiket ile branch chip'i arasında barın KENDİ öğe-arası boşluğu durur — ikisi birleşik OKUNMAZ.
    ///
    /// <para>Prototipte bar bir flex satırıdır ve <c>gap: 8</c> taşır (BuildApp.jsx:2325); etiketin span'i
    /// buna EK OLARAK <c>marginRight: 2</c> ekler (BuildApp.jsx:2394) → toplam 10px.</para>
    ///
    /// <para><b>Ölçülen kusur:</b> aralık 2px'ti — barın 8'lik öğe-arası boşluğu WPF'te her öğeye ELLE
    /// yazılıyor ve branch chip'inin kabı onu almamıştı. Kullanıcı bunu "OSYS branch seçimi ile birleşik
    /// olmuş" diye tarif etti.</para>
    /// </summary>
    [StaFact]
    public void The_label_keeps_the_bars_own_gap_between_itself_and_the_branch_chip()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);
        vm.RootPath = @"D:\src\osys";
        bar.UpdateLayout(); // etiket YENİ göründü — yerleşim iddiaları taze bir pass ister

        var label = bar.WorkspaceLabel;
        double labelRight = label.TranslatePoint(new Point(label.ActualWidth, 0), bar).X;
        double branchLeft = bar.BranchChip.TranslatePoint(new Point(0, 0), bar).X;

        Assert.True(label.ActualWidth > 0, "ön-koşul: workspace etiketi hiç yerleşmedi");
        Assert.Equal(10.0, branchLeft - labelRight, 3);
        GC.KeepAlive(window);
    }

    /// <summary>Uzun bir kök klasör adı barı ITMEZ: etiket kendi sınırında ellipsis'e düşer (title bar'daki
    /// eski bağlam metninin kırpma kuralı buraya taşındı).</summary>
    [StaFact]
    public void An_over_long_workspace_name_is_ellipsised_instead_of_pushing_the_bar()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);
        vm.RootPath = @"C:\src\" + new string('R', 120);
        bar.UpdateLayout(); // etiket YENİ göründü — yerleşim iddiaları taze bir pass ister

        var label = bar.WorkspaceLabel;
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        Assert.True(label.ActualWidth > 0, "workspace etiketi hiç yerleşmedi");
        Assert.True(label.ActualWidth <= label.MaxWidth,
            $"workspace etiketi MaxWidth={label.MaxWidth}'i aştı: {label.ActualWidth}px");

        var probe = new TextBlock
        { Text = label.Text, FontFamily = DsResources.MonoFontFamily, FontSize = label.FontSize };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(probe.DesiredSize.Width > label.ActualWidth,
            $"kırpma HİÇ olmadı: ham genişlik {probe.DesiredSize.Width}px, yerleşen {label.ActualWidth}px");
        GC.KeepAlive(window);
    }
}
