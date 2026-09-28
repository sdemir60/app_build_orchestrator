using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §2.7-11 · §9-7] Build split-button menüsü ÜÇ maddedir: <b>Build · Rebuild · Clean</b>.
/// İkon ailesi tek grid/stroke'ta okunur — play · rotate-cw · brush — ve AYNI aile satır menüsünde de
/// kullanılır.
///
/// <para><b>[DEĞİŞEN KURAL]</b> v1.7.0'da menü KOŞULSUZ İKİ maddeydi (Build + Rebuild) ve Rebuild'in ikonu
/// <c>Icon.Rot</c>'tu (Sync/Redo ailesinden). v1.11.0 üçüncü maddeyi (VS <i>Clean Solution</i>) ekledi ve
/// Rebuild'i kendi ailesinin rotate-cw'sine (<c>Icon.Rebuild</c>) taşıdı — eski iddiayı pinleyen testler bu
/// dosyada YENİ kurala göre yeniden yazıldı.</para>
///
/// <para><b>Clean</b> Visual Studio'nun <i>Clean Solution</i>'ıdır: satır menüsündeki Clean bir proje için ne
/// yapıyorsa bunu grafın TÜM projeleri için yapar — kapsamsız bir <c>RunMode.Clean</c> koşusu. Madde Build ve
/// Rebuild gibi çizilir ve aynı kapıdan geçer.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class BuildMenuTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };

    private static (BuildMenu menu, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var menu = new BuildMenu { DataContext = vm };
        return (menu, DsResources.Realize(host, menu));
    }

    [Fact]
    public void The_menu_is_build_rebuild_then_clean()
    {
        var items = BuildMenu.ComposeItems(total: 36);

        Assert.Equal(["build", "rebuild", "clean"], items.Select(i => i.Kind));
        Assert.Equal("Clean", items[2].Title);
        Assert.Equal("Remove build outputs — next build is full", items[2].Desc);
        Assert.Null(items[2].Kbd); // Clean'in kısayolu YOK (F5/Ctrl+F5 Build ve Rebuild'indir)
    }

    /// <summary>[§9-7] "İkon ailesi tek grid/stroke'ta: play · rotate-cw · brush." Rebuild ARTIK Sync'in
    /// <c>Icon.Rot</c>'unu kullanmaz.</summary>
    [Fact]
    public void The_three_items_share_one_icon_family_play_rotate_cw_brush()
    {
        Assert.Equal("Icon.Play", BuildMenu.IconKeyFor("build"));
        Assert.Equal("Icon.Rebuild", BuildMenu.IconKeyFor("rebuild"));
        Assert.Equal("Icon.Brush", BuildMenu.IconKeyFor("clean"));
    }

    /// <summary>
    /// Clean, kardeşleri gibi CANLI çizilir: etkin, el imleci, tam opaklık ve aynı hover zemini. Tooltip işi
    /// adlandırır — motor proje başına koşar, bu yüzden "every project" der (solution düzeyinde bir MSBuild
    /// izlenimi verilmez, ARCHITECTURE §13.2).
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-28]</b> Eski ad/iddia:
    /// <c>Clean_is_drawn_disabled_with_a_reason_in_its_tooltip</c> — madde pasifti (0.45 opaklık, hover yok) ve
    /// tooltip <c>"… on every solution; caches are untouched — not available yet"</c> diyordu, çünkü arka ucu
    /// yazılmamıştı. Değişme gerekçesi: motor yazıldı — madde kapsamsız bir <c>RunMode.Clean</c> koşusu
    /// gönderir (<see cref="Picking_clean_sends_an_unscoped_clean_run_and_closes_the_menu"/>); pasif çizmek artık
    /// var olan bir işi gizlerdi.</para>
    /// </summary>
    [StaFact]
    public void Clean_is_drawn_live_like_its_siblings_and_its_tooltip_names_the_job()
    {
        var vm = NewVm();
        var (menu, window) = Realize(vm);

        var rows = menu.Rows.ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsEnabled));
        Assert.All(rows, r => Assert.Equal(System.Windows.Input.Cursors.Hand, r.Cursor));
        Assert.All(rows, r => Assert.Equal(1.0, r.Opacity));
        Assert.All(rows, r => Assert.IsType<System.Windows.Media.SolidColorBrush>(r.Background)); // hover zemini takılı
        string tooltip = Assert.IsType<string>(rows[2].ToolTip);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.CleanSolutionTooltip, tooltip);
        Assert.Equal("Clean — msbuild /t:Clean on every project; caches are untouched", tooltip);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Clean maddesi kapsamsız bir <c>RunMode.Clean</c> koşusu gönderir (<c>ScopeProjectId = null</c> — grafın
    /// tamamı) ve menüyü kapatır. Kablaj GERÇEK fare olayıyla sınanır. Pill <c>CLEAN</c> yazar; konsol notu
    /// hedef adı TAŞIMAZ (tam koşu).
    /// </summary>
    [StaFact]
    public void Picking_clean_sends_an_unscoped_clean_run_and_closes_the_menu()
    {
        var vm = NewVm();
        VmTopology.Seed(vm, @"C:\p\a.csproj", @"C:\p\b.csproj");
        var (menu, window) = Realize(vm);
        bool closed = false;
        menu.ItemInvoked += () => closed = true;
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        menu.Rows.ToList()[2].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

        Assert.True(closed);
        Assert.NotNull(sent);
        Assert.Equal((RunMode.Clean, (string?)null), (sent!.Mode, sent.ScopeProjectId));
        Assert.Equal(OperationLabel.Clean, vm.CurrentOperation);
        string console = vm.GetRunDocumentText();
        Assert.Contains("clean requested", console, StringComparison.Ordinal);
        Assert.DoesNotContain("single project", console, StringComparison.Ordinal);
        GC.KeepAlive(window);
    }
}
