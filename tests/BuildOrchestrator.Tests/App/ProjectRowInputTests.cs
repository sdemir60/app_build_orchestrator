using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;
using System.Windows.Automation;

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

    /// <summary>Hover bloğu DÖRT yuva taşır ve sırası sabittir: <b>play/Stop · ⋯ · Explorer · VS</b>. İlk
    /// yuvanın iki kiracısı vardır ve hiçbir zaman birlikte görünmez: play, koşunun hedefi olan satırda
    /// Stop'a döner (§3.8). Play birincil eylemdir ve bir tık büyüktür (14px, diğerleri 13px).</summary>
    [StaFact]
    public void The_hover_block_orders_play_or_stop_more_explorer_and_visual_studio()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);

        RaiseMouse(row, Mouse.MouseEnterEvent);
        var actions = row.Actions!;
        var strip = (Panel)actions.HoverIcons;

        Assert.Equal(
            new UIElement[] { actions.BuildButton, actions.StopButton, actions.MoreButton, actions.RevealButton, actions.VsButton },
            strip.Children.Cast<UIElement>());
        Assert.Equal(Visibility.Visible, actions.BuildButton.Visibility);   // hedef değil → play
        Assert.Equal(Visibility.Collapsed, actions.StopButton.Visibility);
        Assert.Equal(14.0, ((FrameworkElement)actions.BuildButton.Content).Width);   // birincil eylem: bir tık büyük
        Assert.Equal(13.0, ((FrameworkElement)actions.MoreButton.Content).Width);
        GC.KeepAlive(window);
    }

    /// <summary>[§3.8] Play, satırı YALNIZ o projeyi derleyen komuta bağlar (parametre satırın kimliği) ve
    /// tooltip'i kilide göre değişir: boşta <c>Build this project</c>; bir koşu uçuştayken (planlama penceresi
    /// DAHİL) düğme pasiftir ve tooltip nedenini söyler — <c>Build in progress — wait or stop it first</c>.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: motoru yazılmadığı için play pasifti ve "not available yet"
    /// derdi. Tek proje koşusunun motoru yazıldı (<see cref="StartRunCommand.ScopeProjectId"/>); pasiflik
    /// artık yalnız kilidin sonucudur ve kilit kalkınca düğme geri gelir.</para></summary>
    [StaFact]
    public void The_play_button_runs_the_row_through_the_build_project_command_and_locks_with_a_reason_mid_run()
    {
        var runVm = NewRunVm();
        VmTopology.Seed(runVm, RowId);
        var row = Realize(runVm, runVm.Projects.Single(), out var window);

        RaiseMouse(row, Mouse.MouseEnterEvent);
        var play = row.Actions!.BuildButton;

        Assert.Same(runVm.BuildProjectCommand, play.Command);
        Assert.Equal(RowId, play.CommandParameter);
        Assert.True(play.IsEnabled);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.BuildThisProject, play.ToolTip);

        runVm.IsStarting = true; // bir koşu İSTENDİ — daha planlama penceresinde kilit iner
        Assert.False(play.IsEnabled);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.BuildBusyTooltip, play.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(play));

        runVm.IsStarting = false;
        Assert.True(play.IsEnabled);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.BuildThisProject, play.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>[§3.8] Koşunun HEDEFİ olan satırda play kırmızı <b>Stop</b>'a döner ve hover olmadan da görünür
    /// kalır (Stop, play ile aynı boyda — birincil eylem); koşu bitince satır hover kuralına geri döner. Stop
    /// düğmesi ana Stop ile AYNI komuta bağlıdır — ikinci bir durdurma yolu yazılmaz.</summary>
    [StaFact]
    public void The_target_row_shows_a_red_stop_button_without_hover_until_the_run_ends()
    {
        var runVm = NewRunVm();
        VmTopology.Seed(runVm, RowId);
        var row = Realize(runVm, runVm.Projects.Single(), out var window);
        Assert.Null(row.HoverIcons); // hover yok → ikon bloğu henüz kurulmadı

        runVm.RunTargetId = RowId;   // satırdan Build'e basıldı (BeginRunAsync bunu tıklama anında yazar)
        runVm.IsStarting = true;

        var actions = row.Actions!;
        Assert.Equal(Visibility.Visible, actions.HoverIcons.Visibility);   // hover'sız görünür
        Assert.Equal(Visibility.Visible, actions.StopButton.Visibility);
        Assert.Equal(Visibility.Collapsed, actions.BuildButton.Visibility);
        Assert.Same(runVm.StopCommand, actions.StopButton.Command);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.StopThisBuild, AutomationProperties.GetName(actions.StopButton));
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.StopBuildTooltip, actions.StopButton.ToolTip);
        Assert.Equal(14.0, ((FrameworkElement)actions.StopButton.Content).Width);
        Assert.Same(row.FindResource("Brush.StatusFailText"), actions.StopIcon.Fill);

        runVm.IsStarting = false; // koşu bitti — kilit düşer, hedef bırakılır
        Assert.Null(runVm.RunTargetId);
        Assert.Equal(Visibility.Collapsed, actions.StopButton.Visibility);
        Assert.Equal(Visibility.Visible, actions.BuildButton.Visibility);
        Assert.Equal(Visibility.Collapsed, actions.HoverIcons.Visibility); // hover yok → sha'ya döner
        GC.KeepAlive(window);
    }

    /// <summary>[§9-6] Satıra SAĞ TIK, ⋯ ile AYNI menüyü açar; menü başlığı projenin kısa adıdır ve maddeleri
    /// Build · Rebuild · Clean'dir (Build/Rebuild tek proje koşusuna bağlı, Clean motoru bekliyor).</summary>
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

    /// <summary>[§9-6] Menünün maddeleri: Build · Rebuild · Clean — Build split-button ile AYNI üçlü ve AYNI
    /// ikon ailesi. ÜÇÜ DE tıklanabilir (arka uçları tek proje koşusudur) ve bir koşu uçuştayken üçü de
    /// pasifleşip nedenini söyler.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: üçü de "not available yet" ile pasifti; sonra Build/Rebuild
    /// açıldı, Clean pasif kaldı. Clean da yazıldı — satırdan Clean, Visual Studio&apos;nun proje Clean&apos;idir
    /// (<c>msbuild /t:Clean</c>). Bakım kutusundaki DERİN Clean ile Build split menüsünün Clean&apos;i AYRI
    /// yüzeylerdir ve hâlâ motorlarını bekler.</para>
    /// <para>Menü kabuğu, KAPALI bir popup içinde realize olmadığı için burada TEK BAŞINA kurulur
    /// (BuildMenuTests deseni) — satırın kablajı aşağıdaki testte, içeriği burada pinlenir.</para></summary>
    [StaFact]
    public void The_row_menu_offers_build_rebuild_and_clean_and_locks_all_three_while_a_run_is_in_flight()
    {
        var host = DsResources.NewHost();
        var menu = new ProjectRowMenu();
        var window = DsResources.Realize(host, menu);

        Assert.Equal(["build", "rebuild", "clean"], ProjectRowMenu.Items.Select(i => i.Kind));
        var rows = menu.Rows.ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsEnabled));
        Assert.All(rows, r => Assert.Equal(Cursors.Hand, r.Cursor));
        Assert.All(rows, r => Assert.Null(r.ToolTip));

        menu.SetRunActionsEnabled(false); // bir koşu uçuşta: menü açılır ama maddeler pasiftir
        Assert.All(rows, r => Assert.False(r.IsEnabled));
        Assert.All(rows, r => Assert.Equal(BuildOrchestrator.App.AccessibilityNames.BuildBusyTooltip, r.ToolTip));
        Assert.All(rows, r => Assert.Equal(BuildMenu.DisabledOpacity, r.Opacity));
        Assert.True(ToolTipService.GetShowOnDisabled(rows[2]));

        menu.SetRunActionsEnabled(true);
        Assert.All(rows, r => Assert.True(r.IsEnabled));
        Assert.All(rows, r => Assert.Equal(1.0, r.Opacity));
        GC.KeepAlive(window);
    }

    /// <summary>[§9-6] Menüden Build seçmek satırın projesini derler (kapsamlı komut) ve menüyü kapatır;
    /// Rebuild aynı yoldan kendi modunu gönderir. Kablaj GERÇEK fare olayıyla sınanır: menü satırı
    /// tıklanır, komut satır VM'inin kimliğiyle gider.</summary>
    [StaFact]
    public void Picking_a_row_menu_item_runs_that_project_in_its_own_mode_and_closes_the_menu()
    {
        var runVm = NewRunVm();
        VmTopology.Seed(runVm, RowId);
        var row = Realize(runVm, runVm.Projects.Single(), out var window);
        var sent = new List<StartRunCommand>();
        runVm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent.Add(s); };

        RaiseMouse(row, Mouse.MouseEnterEvent); // ikon bloğu kurulsun ki Opened'a abone olunabilsin
        var actions = row.Actions!;
        bool opened = false;
        actions.RowMenu.Opened += (_, _) => opened = true;
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        { RoutedEvent = UIElement.MouseRightButtonUpEvent });
        DispatcherPump.PumpUntil(() => opened, TimeSpan.FromSeconds(2));
        Assert.True(opened, "ön-koşul: satır menüsü açılmadı");
        actions.RowMenuContent.UpdateLayout();
        var rows = actions.RowMenuContent.Rows.ToList();

        rows[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

        Assert.False(actions.MoreButton.IsChecked);            // menü kapandı
        var build = Assert.Single(sent);
        Assert.Equal((RunMode.Build, RowId), (build.Mode, build.ScopeProjectId));
        Assert.Null(runVm.SelectedProjectId);                  // satırdan tetiklemek satıra tıklamak DEĞİLDİR

        // Üç maddenin üçü de kendi modunu, AYNI hedefle gönderir.
        foreach (var (index, mode) in new[] { (1, RunMode.Rebuild), (2, RunMode.Clean) })
        {
            actions.MoreButton.IsChecked = true;
            rows[index].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Assert.False(actions.MoreButton.IsChecked);
            Assert.Equal((mode, RowId), (sent[^1].Mode, sent[^1].ScopeProjectId));
        }
        Assert.Equal(3, sent.Count);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [design §2.4-4 · ölçüldü] <b>Satırdaki ikon butonların hover zemini, satırın KENDİ hover zemininden
    /// bir kademe yukarıda olmalıdır.</b> İkonlar yalnız satır hover'dayken görünür — yani buton her zaman
    /// zaten boyanmış bir zeminin üstündedir. Paylaşılan <c>Ds.IconButton</c> hover'ı
    /// <c>Brush.SurfaceRaised</c>'dır ve satırın hover'ı <c>Brush.SurfaceHover</c>'dır; ikisi de <c>#1a1a1e</c>
    /// (tasarım token'larında da öyle: <c>--surface-hover</c> ve <c>--surface-raised</c> ikisi de
    /// <c>neutral-850</c>) — sonuç: fare ikonun üstündeyken HİÇBİR zemin değişimi görünmez ve kullanıcı
    /// hangi ikonun üstünde olduğunu bilemez.
    ///
    /// <para><b>KAYITLI SAPMA:</b> satırdaki butonlar bir kademe üstteki <c>Brush.SurfaceOverlay</c>'e hover
    /// eder. Bu, action bar'daki butonların aldığı GÖRELİ kontrastın aynısıdır (orada buton
    /// <c>Brush.Surface</c> üstünde durur ve <c>SurfaceRaised</c>'e çıkar) — kural "hover zemini, üstünde
    /// durduğun yüzeyden bir kademe yukarısı"dır.</para>
    /// </summary>
    [StaFact]
    public void Row_icon_buttons_hover_one_step_above_the_hovered_row_surface()
    {
        var runVm = NewRunVm();
        var rowVm = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending);
        var row = Realize(runVm, rowVm, out var window);
        RaiseMouse(row, Mouse.MouseEnterEvent);
        var actions = row.Actions!;

        var rowHover = (SolidColorBrush)row.FindResource("Brush.SurfaceHover");
        foreach (var button in new ButtonBase[] { actions.BuildButton, actions.StopButton, actions.MoreButton,
                                                 actions.RevealButton, actions.VsButton })
        {
            object? key = HoverBackgroundKeyOf(button);
            Assert.Equal("Brush.SurfaceOverlay", key);                  // bir kademe yukarısı
            var hover = (SolidColorBrush)row.FindResource(key!);
            Assert.NotEqual(rowHover.Color, hover.Color);               // aynı renkse hover GÖRÜNMEZ
        }
        GC.KeepAlive(window);
    }

    /// <summary>Bir düğmenin stilindeki <c>IsMouseOver</c> tetikleyicisinin yazdığı zemin fırçasının KAYNAK
    /// ANAHTARI. Değerin kendisi bir fırça DEĞİLDİR: setter <c>{DynamicResource …}</c> taşır, yani anahtar
    /// çalışma anında çözülür (motion sözleşmesi: token'lar dinamik okunur). BasedOn zincirinde en TÜRETİLMİŞ
    /// eşleşme kazanır — WPF de aynı sırayı uygular.</summary>
    private static object? HoverBackgroundKeyOf(ButtonBase button)
    {
        for (var style = button.Style; style is not null; style = style.BasedOn)
            foreach (var trigger in style.Triggers.OfType<Trigger>())
            {
                if (trigger.Property != UIElement.IsMouseOverProperty || !Equals(trigger.Value, true)) continue;
                foreach (var setter in trigger.Setters.OfType<Setter>())
                    if (setter.Property == BuildOrchestrator.App.Controls.DsTransition.AnimatedBackgroundProperty)
                        return (setter.Value as DynamicResourceExtension)?.ResourceKey ?? setter.Value;
            }
        return null;
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
