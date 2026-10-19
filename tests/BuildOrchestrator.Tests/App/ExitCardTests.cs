using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.DsResources;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Exit'in onay kartı: iş sürerken Exit (buton, Ctrl+Q) uygulamayı kapatmaz, butonun altında bu kartı açar. Kart mevcut popover
/// altyapısıdır (<see cref="PopoverBase"/> + WPF <see cref="Popup"/> + <see cref="PopoverToggle"/> + <c>Ds.Popover</c> kabuğu, dolgu 0):
/// 320px, butonun 9px altında, SAĞ kenarı butonun sağ kenarında; 140ms drop-in (4px yukarıdan, sağ üst köşeden). Üç blok, aralarında 1px
/// <c>border</c>: (1) durum — caps etiket (11px <c>text-faint</c>) + sağda mono ilerleme, altında tek cümle (13px/500 <c>text-primary</c>);
/// (2) "ENDS ON EXIT" — kapanacak süreçler, mono 11px: ad <c>text-secondary</c>, <c>×N</c> <c>text-dim</c>; gösterilecek süreç yoksa blok
/// yok; (3) karar — 12px <c>text-dim</c> sonuç satırı, altında sağa yaslı <c>Cancel</c> (secondary sm) + <c>Stop and exit</c> (danger sm,
/// 12px power ikonlu). İş biterse kart kendini günceller ("Nothing is running.", buton <c>Exit</c>). Kapanış: Cancel, butona tekrar
/// basış, dışarı tık, Esc — Esc önce bu kartı kapatır (açık bir dialogdan da önce). Açılınca odak Cancel'dadır. Kart dialogların da
/// üstündedir: dialog açılınca kapanmaz.
///
/// <para>Kartın açık durumunun kaynağı Exit butonunun <see cref="DsChrome.IsActiveProperty"/>'sidir (güncelleme hapının <c>IsChecked</c>'i
/// gibi): gösterilmeyen bir pencerede <see cref="Popup.IsOpen"/> yüklenmeye dek false'a zorlanır (ölçüldü), bu yüzden kabuk testleri durumu
/// butondan okur; kartın içi gerçek bir pencerede ayrıca realize edilir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ExitCardTests
{
    /// <summary>Tasarımın kart genişliği.</summary>
    private const double CardWidth = 320;

    private static readonly IReadOnlyList<ProcessTally> TwoProcesses =
        [new("MSBuild.exe", 4), new("VBCSCompiler.exe", 1)];

    private static Color Token(FrameworkElement host, string key) => DsResources.TokenColor(host, key);

    private static RunViewModel NewVm()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
        };
        MainWindowHost.AcceptSends(vm);
        return vm;
    }

    /// <summary>Dört projesi derlenen bir Build — ekran görüntüsündeki kart: <c>4/22</c>, dört proje uçuşta.</summary>
    private static RunViewModel BuildingVm()
    {
        var vm = NewVm();
        string[] names = ["A", "B", "C", "D", "E"];
        VmTopology.Seed(vm, [.. names.Select(MainWindowHost.IdOf)]);
        MainWindowHost.StartBuild(vm, names);
        foreach (string name in names[..4]) MainWindowHost.StartProject(vm, name);
        vm.WillBuildCount = 22;
        vm.FinishedOfWillBuild = 4;
        return vm;
    }

    /// <summary>Kartı üretimdeki kabuğunun (<c>Ds.Popover</c>, 320px, dolgu 0) içinde, AÇIK ve ekran dışı gerçek bir pencerede realize
    /// eder (<c>UpdateCardTests</c>'in deseni).</summary>
    private static (ExitCard card, Window window) OpenRealized(RunViewModel vm, IReadOnlyList<ProcessTally>? processes = null)
    {
        var host = DsResources.NewHost();
        var shell = new Border { Width = CardWidth };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Ds.Popover");
        shell.Padding = new Thickness(0);
        var card = new ExitCard { DataContext = vm, Processes = processes ?? [] };
        shell.Child = card;
        var window = DsResources.Realize(host, shell, 500, 600);
        card.IsOpen = true;
        card.UpdateLayout();
        return (card, window);
    }

    // ================================================================ içerik

    [StaFact]
    public void The_status_block_reads_the_work_its_progress_and_one_sentence()
    {
        var (card, window) = OpenRealized(BuildingVm(), TwoProcesses);
        var status = card.PART_Status;

        Assert.Equal(new Thickness(16, 13, 16, 14), status.Padding);
        Assert.Equal(new Thickness(0), status.BorderThickness);

        var heading = card.PART_Heading;
        Assert.Equal("Build in progress", heading.Text);
        Assert.True(heading.Uppercase);
        Assert.Equal(11.0, heading.FontSize);
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(heading.Foreground));

        var progress = card.PART_Progress;
        Assert.Equal("4/22", progress.Text);
        Assert.Equal(AppFonts.Mono, progress.FontFamily);
        Assert.Equal(11.0, progress.FontSize);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(progress));
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(progress.Foreground));
        Assert.Equal(status.ActualWidth - 16, BoundsIn(progress, status).Right, precision: 1); // sağa yaslı

        var sentence = card.PART_Sentence;
        Assert.Equal("Exit stops the build, then quits.", sentence.Text);
        Assert.Equal(13.0, sentence.FontSize);
        Assert.Equal(FontWeights.Medium, sentence.FontWeight);
        Assert.Equal(Token(card, "Brush.TextPrimary"), DsResources.ColorOf(sentence.Foreground));
        Assert.Equal(8.0, BoundsIn(sentence, status).Top - BoundsIn(card.PART_HeadingRow, status).Bottom, precision: 1);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(card));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_ends_on_exit_block_lists_each_process_in_mono_with_its_count()
    {
        var (card, window) = OpenRealized(BuildingVm(), TwoProcesses);
        var block = card.PART_ProcessesBlock;

        Assert.Equal(Visibility.Visible, block.Visibility);
        Assert.Equal(new Thickness(16, 12, 16, 13), block.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), block.BorderThickness);
        Assert.Equal(Token(card, "Brush.Border"), DsResources.ColorOf(block.BorderBrush));

        var heading = card.PART_ProcessesHeading;
        Assert.Equal(ExitText.EndsOnExit, heading.Text);
        Assert.True(heading.Uppercase);
        Assert.Equal(11.0, heading.FontSize);
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(heading.Foreground));

        var rows = card.PART_Processes.Children.Cast<DockPanel>().ToList();
        Assert.Equal(2, rows.Count);
        var texts = rows.Select(r => r.Children.OfType<TextBlock>().ToList()).ToList();
        Assert.Equal([("MSBuild.exe", "×4"), ("VBCSCompiler.exe", "×1")],
            texts.Select(t => (t.Single(b => DockPanel.GetDock(b) != Dock.Right).Text, t.Single(b => DockPanel.GetDock(b) == Dock.Right).Text)));
        foreach (var row in texts)
        {
            var name = row.Single(b => DockPanel.GetDock(b) != Dock.Right);
            var count = row.Single(b => DockPanel.GetDock(b) == Dock.Right);
            Assert.All(row, b => Assert.Equal((AppFonts.Mono, 11.0), (b.FontFamily, b.FontSize)));
            Assert.Equal(Token(card, "Brush.TextSecondary"), DsResources.ColorOf(name.Foreground));
            Assert.Equal(Token(card, "Brush.TextDim"), DsResources.ColorOf(count.Foreground));
        }
        Assert.Equal(block.ActualWidth - 16, BoundsIn(texts[0].Single(b => DockPanel.GetDock(b) == Dock.Right), block).Right,
            precision: 1); // sayı sağa yaslı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Without_processes_to_show_the_block_is_not_drawn()
    {
        var (card, window) = OpenRealized(BuildingVm(), processes: []);

        Assert.Equal(Visibility.Collapsed, card.PART_ProcessesBlock.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_decision_block_says_what_stays_and_offers_cancel_and_stop_and_exit()
    {
        var (card, window) = OpenRealized(BuildingVm(), TwoProcesses);
        var decision = card.PART_Decision;

        Assert.Equal(new Thickness(16, 12, 16, 14), decision.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), decision.BorderThickness);

        var outcome = card.PART_Outcome;
        Assert.Equal(
            "4 projects in flight stay unbuilt. Finished projects keep their results; the next Build picks up the rest.",
            outcome.Text);
        Assert.Equal(12.0, outcome.FontSize);
        Assert.Equal(Token(card, "Brush.TextDim"), DsResources.ColorOf(outcome.Foreground));
        Assert.Equal(TextWrapping.Wrap, outcome.TextWrapping);

        Assert.Same(card.FindResource("Ds.Button.Secondary.Sm"), card.PART_Cancel.Style);
        Assert.Equal(ExitText.Cancel, card.PART_Cancel.Content);
        Assert.Same(card.FindResource("Ds.Button.Danger.Sm"), card.PART_Action.Style);
        Assert.Equal("Stop and exit", card.PART_ActionLabel.Text);
        Assert.Equal("Stop and exit", AutomationProperties.GetName(card.PART_Action));
        var icon = DsResources.Descendants(card.PART_Action).OfType<Viewbox>().Single();
        Assert.Equal((12.0, 12.0), (icon.Width, icon.Height));
        Assert.Same(card.FindResource("Icon.Power"), DsResources.Descendants(icon).OfType<Path>().Single().Data);

        Assert.Equal(HorizontalAlignment.Right, card.PART_Actions.HorizontalAlignment);
        Assert.Equal(8.0, BoundsIn(card.PART_Action, decision).Left - BoundsIn(card.PART_Cancel, decision).Right, precision: 1);
        Assert.Equal(12.0, BoundsIn(card.PART_Actions, decision).Top - BoundsIn(outcome, decision).Bottom, precision: 1);
        GC.KeepAlive(window);
    }

    /// <summary>Kart açıkken iş biterse kart kendini günceller: etiket <c>EXIT</c>, cümle "Nothing is running. Exit quits right away.",
    /// düğme <c>Exit</c>; ilerleme, süreç bloğu ve sonuç satırı kalkar.</summary>
    [StaFact]
    public void When_the_work_ends_the_open_card_says_nothing_is_running()
    {
        var vm = BuildingVm();
        var (card, window) = OpenRealized(vm, TwoProcesses);

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 400));
        card.UpdateLayout();

        Assert.Equal("Exit", card.PART_Heading.Text);
        Assert.Equal("Nothing is running. Exit quits right away.", card.PART_Sentence.Text);
        Assert.Equal(Visibility.Collapsed, card.PART_Progress.Visibility);
        Assert.Equal(Visibility.Collapsed, card.PART_ProcessesBlock.Visibility);
        Assert.Equal(Visibility.Collapsed, card.PART_Outcome.Visibility);
        Assert.Equal("Exit", card.PART_ActionLabel.Text);
        Assert.Equal(new Thickness(0), card.PART_Actions.Margin); // sonuç satırı yokken düğmeler bloğun üstündedir
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Cancel_asks_the_card_to_close_and_the_action_asks_to_exit()
    {
        var (card, window) = OpenRealized(BuildingVm(), TwoProcesses);
        int closes = 0, exits = 0;
        card.CloseRequested += () => closes++;
        card.ActionRequested += () => exits++;

        CommandPress.Click(card.PART_Cancel);
        Assert.Equal((1, 0), (closes, exits));

        CommandPress.Click(card.PART_Action);
        Assert.Equal((1, 1), (closes, exits));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Opening_puts_the_keyboard_focus_on_cancel()
    {
        var (card, window) = OpenRealized(BuildingVm(), TwoProcesses);

        DispatcherPump.DrainToIdle();

        Assert.Same(card.PART_Cancel, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(card)));
        GC.KeepAlive(window);
    }

    /// <summary>140ms drop-in: opaklık 0 + 4px YUKARIDAN + ölçek .985 → düz; ölçeğin merkezi SAĞ ÜST köşe (kart butonun sağ kenarından
    /// sarkar — güncelleme kartı soldan sarkar ve sol üstten büyür).</summary>
    [StaFact]
    public void The_card_drops_in_from_4px_above_scaling_from_its_top_right_corner()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var host = DsResources.NewHost();
        var card = new ExitCard { DataContext = BuildingVm() };
        var window = DsResources.Realize(host, card, 400, 500);

        card.IsOpen = true;

        Assert.Equal(new Point(1, 0), card.RenderTransformOrigin);
        var group = Assert.IsType<TransformGroup>(card.RenderTransform);
        var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        var translate = Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal((0.985, 0.985), (scale.ScaleX, scale.ScaleY));
        Assert.Equal(-4.0, translate.Y);
        Assert.True(card.HasAnimatedProperties, "drop-in GERÇEKTEN animasyonlu değil");
        GC.KeepAlive(window);
    }

    // ================================================================ kabuk

    /// <summary>Kabuğu iş sürerken kurar: sahte süreç sayımı ve zamanlayıcı (gerçek job ya da saat yok), sayaçlı kapanış.</summary>
    private sealed class Rig
    {
        public MainWindow Window { get; }
        public RunViewModel Vm { get; }
        public List<IpcCommand> Sent { get; } = [];
        public FakePollTimer Timer { get; } = new();
        public int Shutdowns { get; private set; }
        public int Census { get; private set; }
        public IReadOnlyList<ProcessTally> Processes { get; set; } = TwoProcesses;

        public Rig(TempDir temp, bool busy = true)
        {
            (Window, Vm) = MainWindowHost.NewRealized(temp);
            Window.ShutdownApplication = () => Shutdowns++;
            Window.ExitCardTimer = Timer;
            Window.TakeProcessCensus = () =>
            {
                Census++;
                return Processes;
            };
            MainWindowHost.AcceptSends(Vm);
            Vm.DebugOnCommandSent = Sent.Add;
            if (busy) MainWindowHost.StartBuild(Vm);
        }

        public bool CardOpen => DsChrome.GetIsActive(Window.ExitButton);
    }

    [StaFact]
    public void Exit_with_work_in_flight_opens_the_card_instead_of_quitting()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);

        CommandPress.Click(s.Window.ExitButton);

        Assert.True(s.CardOpen);
        Assert.Equal(0, s.Shutdowns);
        Assert.False(s.Vm.ExitPending);
        Assert.Empty(s.Sent);
        Assert.Equal(TwoProcesses, s.Window.ExitCardView.Processes); // süreçler açılışta sayıldı
        GC.KeepAlive(s.Window);
    }

    [StaFact]
    public void A_second_press_on_the_button_and_ctrl_q_close_the_card()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);

        Assert.True(PopoverToggle.IsBound(s.Window.ExitButton)); // dışarı tıkın aynı basışı yeniden açamaz
        CommandPress.Click(s.Window.ExitButton);
        CommandPress.Click(s.Window.ExitButton);
        Assert.False(s.CardOpen);

        MainWindowHost.PressKeyBinding(s.Window, Key.Q, ModifierKeys.Control);
        Assert.True(s.CardOpen);
        MainWindowHost.PressKeyBinding(s.Window, Key.Q, ModifierKeys.Control);
        Assert.False(s.CardOpen);
        Assert.Equal(0, s.Shutdowns);
        GC.KeepAlive(s.Window);
    }

    /// <summary>Kart butonun 9px altında, SAĞ kenarı butonun sağ kenarında, 320px'lik <c>Ds.Popover</c> kabuğunda, dolgusuz durur; dışarı
    /// tık onu kapatır (<c>StaysOpen=False</c>). Yer, WPF'in çağıracağı geri çağrıyla sorulur (<c>UpdateCardTests</c>'in gerekçesi).
    /// Açık/kapalı durumu butonun <see cref="DsChrome.IsActiveProperty"/>'siyle iki yönlü bağlıdır: dışarı tıkla kapanan popup butonu da
    /// söndürür.</summary>
    [StaFact]
    public void The_card_hangs_9px_below_the_button_right_edge_in_a_320px_popover_shell()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        var popup = s.Window.ExitPopup;

        Assert.Same(s.Window.ExitButton, popup.PlacementTarget);
        Assert.Equal(PlacementMode.Custom, popup.Placement);
        Assert.Equal((0.0, 0.0), (popup.HorizontalOffset, popup.VerticalOffset));
        var button = new Size(s.Window.ExitButton.ActualWidth, s.Window.ExitButton.ActualHeight);
        var placement = Assert.Single(popup.CustomPopupPlacementCallback(new Size(CardWidth, 230), button, default));
        Assert.Equal(new Point(button.Width - CardWidth, button.Height + 9), placement.Point);
        Assert.False(popup.StaysOpen);
        Assert.True(popup.AllowsTransparency);
        Assert.Equal(PopupAnimation.None, popup.PopupAnimation);

        var shell = Assert.IsType<Border>(popup.Child);
        Assert.Same(s.Window.FindResource("Ds.Popover"), shell.Style);
        Assert.Equal(CardWidth, shell.Width);
        Assert.Equal(new Thickness(0), shell.Padding);
        Assert.Same(s.Window.ExitCardView, shell.Child);
        Assert.Same(s.Vm, s.Window.ExitCardView.DataContext);

        var popupOpen = BindingOperations.GetBinding(popup, Popup.IsOpenProperty)!;
        Assert.Equal((nameof(s.Window.ExitButton), BindingMode.TwoWay), (popupOpen.ElementName, popupOpen.Mode));
        Assert.Same(DsChrome.IsActiveProperty, popupOpen.Path.PathParameters.Single());
        var cardOpen = BindingOperations.GetBinding(s.Window.ExitCardView, PopoverBase.IsOpenProperty)!;
        Assert.Equal((nameof(s.Window.ExitPopup), nameof(Popup.IsOpen)), (cardOpen.ElementName, cardOpen.Path.Path));
        GC.KeepAlive(s.Window);
    }

    [StaFact]
    public void Cancel_closes_the_card_and_nothing_stops()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        CommandPress.Click(s.Window.ExitButton);

        CommandPress.Click(s.Window.ExitCardView.PART_Cancel);

        Assert.False(s.CardOpen);
        Assert.Empty(s.Sent);
        Assert.Equal(0, s.Shutdowns);
        Assert.False(s.Timer.IsRunning); // kapalı kart süreç saymaz
        GC.KeepAlive(s.Window);
    }

    /// <summary>Esc önce bu kartı kapatır — açık bir dialogdan da önce: kart dialogların üstündedir. Dialog ikinci Esc'e kalır.</summary>
    [StaFact]
    public void The_window_escape_closes_the_card_before_an_open_dialog()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        CommandPress.Click(s.Window.InfoButton);
        Assert.Equal(Visibility.Visible, s.Window.AboutOverlay.Visibility); // ön-koşul: About açık
        MainWindowHost.PressKeyBinding(s.Window, Key.Q, ModifierKeys.Control);
        Assert.True(s.CardOpen); // dialog açıkken de açılır

        MainWindowHost.PressEscape(s.Window);
        Assert.False(s.CardOpen);
        Assert.Equal(Visibility.Visible, s.Window.AboutOverlay.Visibility);

        MainWindowHost.PressEscape(s.Window);
        Assert.NotEqual(Visibility.Visible, s.Window.AboutOverlay.Visibility);
        GC.KeepAlive(s.Window);
    }

    /// <summary>Kart açıkken bir dialog açılırsa kart KAPANMAZ — dialogların üstündedir (güncelleme kartının aksine).</summary>
    [StaFact]
    public void Opening_a_dialog_leaves_the_card_open()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        CommandPress.Click(s.Window.ExitButton);

        MainWindowHost.PressKeyBinding(s.Window, Key.F1);

        Assert.Equal(Visibility.Visible, s.Window.AboutOverlay.Visibility);
        Assert.True(s.CardOpen);
        GC.KeepAlive(s.Window);
    }

    [StaFact]
    public void Stop_and_exit_closes_the_card_and_stops_the_work()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        CommandPress.Click(s.Window.ExitButton);
        Assert.True(s.CardOpen); // ön-koşul: buton kapatmadı, kartı açtı
        Assert.Empty(s.Sent);

        CommandPress.Click(s.Window.ExitCardView.PART_Action);

        Assert.False(s.CardOpen);
        Assert.True(s.Vm.ExitPending);
        Assert.Equal(StopKind.Hard, Assert.Single(s.Sent.OfType<StopRunCommand>()).Kind);
        GC.KeepAlive(s.Window);
    }

    /// <summary>Kart açıkken süreç listesi tazelenir (derlemeler başlar ve biter) — yalnız kart açıkken: kapanınca sayım durur. Boşta
    /// hiçbir şey sorgulanmaz.</summary>
    [StaFact]
    public void The_card_recounts_its_processes_only_while_it_is_open()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);
        Assert.Equal(0, s.Census); // kapalı kart saymaz
        CommandPress.Click(s.Window.ExitButton);
        Assert.Equal(1, s.Census);
        Assert.True(s.Timer.IsRunning);

        s.Processes = [new("MSBuild.exe", 2)];
        s.Timer.Tick();
        Assert.Equal([new ProcessTally("MSBuild.exe", 2)], s.Window.ExitCardView.Processes);

        DsChrome.SetIsActive(s.Window.ExitButton, false); // dışarı tık — popup'ın kapanışı butonu söndürür
        s.Timer.Tick();
        Assert.False(s.Timer.IsRunning);
        Assert.Equal(2, s.Census);
        GC.KeepAlive(s.Window);
    }

    [StaFact]
    public void The_card_inside_the_window_resolves_its_tokens()
    {
        using var temp = new TempDir();
        var s = new Rig(temp);

        Assert.Empty(DsResources.DynamicResourceTypeMismatches(s.Window.ExitCardView));
        GC.KeepAlive(s.Window);
    }
}
