using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · maddeler 1.11 + 1.12] Proje kartının GERÇEK girdi kablajı.
///
/// <para><b>1.11 ölçülmüş boşluk:</b> <c>ProjectRow.xaml.cs:508-515</c> Enter/Space ile seçim yapar; ama
/// <c>Key.Enter</c>/<c>Key.Space</c> repo genelinde süitte HİÇ geçmiyordu (grep: 0 isabet) — klavye erişimi
/// tamamen testsizdi.</para>
///
/// <para><b>1.12 ölçülmüş boşluk:</b> <c>ProjectRowTests</c> hover'ı <c>row.SimulateHover(...)</c> test seam'i
/// ile sürüyor; üretimde AYNI <c>SetHover</c> <c>MouseEnter</c>/<c>MouseLeave</c>'e kablıdır
/// (<c>ProjectRow.xaml.cs:82-83</c>) ama gerçek olay hiç yükseltilmiyordu — iki <c>+=</c> silinse suite yeşil
/// kalır, kartlar fareyle hiç tepki vermezdi.</para>
///
/// <para><b>Kurulum üretimle aynı:</b> kartın seçimi <c>RunViewModel</c>'de yaşar ve kart onu görsel ağaçta
/// YUKARI yürüyerek bulur (<c>FindRunViewModel</c>) — bu yüzden kart, DataContext'i <see cref="RunViewModel"/>
/// olan bir kabın içinde realize edilir (listede <c>StickyLayerList</c>/<c>ShellRoot</c> aynı rolü oynar).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ProjectRowInputTests
{
    private const string RowId = @"C:\p\a.csproj";

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewRunVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectRow Realize(RunViewModel runVm, ProjectRowViewModel rowVm, out Window window)
    {
        var row = new ProjectRow { DataContext = rowVm };
        // Üretimdeki ata zinciri: satır VM'i kartta, run VM'i ÜSTTEKİ kapta (FindRunViewModel yukarı yürür).
        var shell = new Border { DataContext = runVm, Child = row };
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, shell);
        return row;
    }

    /// <summary>
    /// [fix-1 · I-G] Tuş, satır GERÇEKTEN klavye odağındayken basılır.
    ///
    /// <para>Odak adımı süs değil: kart klavyeyle ancak <c>ProjectRow.xaml:5</c>'teki
    /// <c>Focusable="True" IsTabStop="True"</c> sayesinde ulaşılabilir. O regresyona uğrarsa üretimde
    /// Enter/Space tuşu karta HİÇ ULAŞMAZ; odak kurulmadan ham <c>RaiseEvent</c> yapan bir test ise bunu
    /// göremez ve yeşil kalırdı. <c>Assert.True(IsKeyboardFocused)</c> o kapıyı da pinler.</para></summary>
    private static KeyEventArgs Press(ProjectRow row, Key key)
    {
        Assert.True(row.Focus(), "kart klavye odağını ALAMADI — Focusable/IsTabStop regresyonu");
        Assert.True(row.IsKeyboardFocused);

        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(row)!, 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        row.RaiseEvent(args);
        return args;
    }

    private static void RaiseMouse(ProjectRow row, RoutedEvent mouseEvent) =>
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = mouseEvent });

    // ---------------------------------------------------------------- 1.11 Enter / Space

    [StaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void Pressing_enter_or_space_on_a_row_selects_it_and_pressing_again_clears_the_selection(Key key)
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);
        Assert.Null(runVm.SelectedProjectId); // ön-koşul

        var first = Press(row, key);
        Assert.Equal(RowId, runVm.SelectedProjectId);
        Assert.True(first.Handled, "tuş yutulmadı — pencere Esc/kısayol zincirine sızar");

        Press(row, key); // aynı satır → SelectProject toggle
        Assert.Null(runVm.SelectedProjectId);
        GC.KeepAlive(window);
    }

    /// <summary>Ayırt edici: kart YALNIZ Enter/Space'e tepki verir. Kapı (<c>e.Key is Key.Enter or Key.Space</c>)
    /// gevşetilse ok tuşuyla gezinme her satırda seçim yapardı.</summary>
    [StaFact]
    public void Arrow_keys_do_not_select_the_row_and_are_left_for_navigation()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);

        var args = Press(row, Key.Down);

        Assert.Null(runVm.SelectedProjectId);
        Assert.False(args.Handled); // gezinmeye bırakılır
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 1.12 gerçek MouseEnter / MouseLeave

    /// <summary>
    /// Gerçek <see cref="Mouse.MouseEnterEvent"/> lazy hover bloğunu KURAR ve sha ↔ ikon takasını yapar;
    /// <see cref="Mouse.MouseLeaveEvent"/> geri alır. <c>ProjectRowTests</c> aynı sonucu <c>SimulateHover</c>
    /// seam'iyle pinler — burada kanıtlanan, o seam'in üretimde GERÇEK fare olaylarına bağlı olduğudur.
    /// </summary>
    [StaFact]
    public void Real_mouse_enter_builds_the_hover_icons_and_swaps_them_with_the_sha_pair()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending)
        {
            WillBuild = true, CurrentSha = "a3f81c2",
        };
        var row = Realize(runVm, rowVm, out var window);

        // [L1] Hover ikonları TEMBEL: ilk hover'a kadar HİÇ kurulmaz.
        Assert.Null(row.HoverIcons);
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);

        RaiseMouse(row, Mouse.MouseEnterEvent);

        Assert.NotNull(row.HoverIcons);
        Assert.Equal(Visibility.Visible, row.HoverIcons!.Visibility);
        Assert.Equal(Visibility.Collapsed, row.ShaText.Visibility);

        RaiseMouse(row, Mouse.MouseLeaveEvent);

        Assert.Equal(Visibility.Collapsed, row.HoverIcons!.Visibility);
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);
        GC.KeepAlive(window);
    }

    // ================================================================ [design v1.11.0 §2.4-4 · §9-6] satır aksiyonları

    /// <summary>Hover bloğu DÖRT ikon taşır ve sırası sabittir: <b>play · ⋯ · Explorer · VS</b>. Play birincil
    /// eylemdir ve bir tık büyüktür (14px, diğerleri 13px — §3.8).</summary>
    [StaFact]
    public void The_hover_block_orders_play_more_explorer_and_visual_studio()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);

        RaiseMouse(row, Mouse.MouseEnterEvent);
        var actions = row.Actions!;
        var strip = (Panel)actions.HoverIcons;

        Assert.Equal(
            new UIElement[] { actions.BuildButton, actions.MoreButton, actions.RevealButton, actions.VsButton },
            strip.Children.Cast<UIElement>());
        Assert.Equal(14.0, ((FrameworkElement)actions.BuildButton.Content).Width);   // birincil eylem: bir tık büyük
        Assert.Equal(13.0, ((FrameworkElement)actions.MoreButton.Content).Width);
        GC.KeepAlive(window);
    }

    /// <summary>[§3.8] Tek-proje koşusunun arka ucu henüz yazılmadı: play PASİFTİR ve tooltip nedenini
    /// söyler (bakım kutusundaki Clean/Optimize ile AYNI karar).</summary>
    [StaFact]
    public void The_play_button_is_disabled_with_a_reason_until_its_engine_exists()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);

        RaiseMouse(row, Mouse.MouseEnterEvent);

        Assert.False(row.Actions!.BuildButton.IsEnabled);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.RowActionsTooltip, row.Actions!.BuildButton.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(row.Actions!.BuildButton));
        GC.KeepAlive(window);
    }

    /// <summary>[§9-6] Satıra SAĞ TIK, ⋯ ile AYNI menüyü açar; menü başlığı projenin kısa adıdır ve maddeleri
    /// Build · Rebuild · Clean'dir (arka uç yokken pasif).</summary>
    [StaFact]
    public void Right_clicking_the_row_opens_the_same_menu_the_ellipsis_opens()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "OSYS.Sales.Core", ProjectRowState.Pending) { NamePrefix = "OSYS." };
        var row = Realize(runVm, rowVm, out var window);

        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        { RoutedEvent = UIElement.MouseRightButtonUpEvent });

        var actions = row.Actions!;
        Assert.True(actions.MoreButton.IsChecked);          // ⋯ ile AYNI kapı (popup IsOpen ona bağlıdır)
        Assert.Equal("Sales.Core", actions.RowMenuContent.Title);   // kısa ad (ortak önek atılmış)
        // Sağ tık bir SEÇİM jesti DEĞİLDİR.
        Assert.Null(runVm.SelectedProjectId);

        GC.KeepAlive(window);
    }

    /// <summary>[§9-6] Menünün maddeleri: Build · Rebuild · Clean — Build split-button'la AYNI üçlü ve AYNI
    /// ikon ailesi. Arka uç yokken üçü de pasiftir ve tooltip nedeni söyler.
    /// <para>Menü kabuğu, KAPALI bir popup içinde realize olmadığı için burada TEK BAŞINA kurulur
    /// (BuildMenuTests deseni) — satırın kablajı yukarıdaki testte, içeriği burada pinlenir.</para></summary>
    [StaFact]
    public void The_row_menu_offers_build_rebuild_and_clean_disabled_until_their_engine_exists()
    {
        var host = DsResources.NewHost();
        var menu = new ProjectRowMenu();
        var window = DsResources.Realize(host, menu);

        Assert.Equal(["build", "rebuild", "clean"], ProjectRowMenu.Items.Select(i => i.Kind));
        var rows = menu.Rows.ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.False(r.IsEnabled));
        Assert.All(rows, r => Assert.Equal(BuildOrchestrator.App.AccessibilityNames.RowActionsTooltip, r.ToolTip));
        GC.KeepAlive(window);
    }

    /// <summary>Menü AÇIKKEN hover ikonları görünür kalır — menü satırın çapasına bağlıdır; çapa kaybolursa
    /// menü havada asılı kalırdı (prototipte de <c>hover || menuOpen</c>).</summary>
    [StaFact]
    public void The_hover_icons_stay_visible_while_the_row_menu_is_open()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);

        RaiseMouse(row, Mouse.MouseEnterEvent);
        row.Actions!.MoreButton.IsChecked = true;
        RaiseMouse(row, Mouse.MouseLeaveEvent);   // fare satırdan çıktı ama menü AÇIK

        Assert.Equal(Visibility.Visible, row.HoverIcons!.Visibility);

        row.Actions!.MoreButton.IsChecked = false; // menü kapandı → hover kuralı geri işler
        Assert.Equal(Visibility.Collapsed, row.HoverIcons!.Visibility);
        GC.KeepAlive(window);
    }
}
