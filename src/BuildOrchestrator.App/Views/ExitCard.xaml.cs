using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [Exit] Title bar'daki Exit butonunun onay kartı: iş sürerken Exit uygulamayı kapatmaz, bu kart neyin duracağını ve neyin kalacağını
/// söyler. Popover iskeleti <see cref="PopoverBase"/>'tir (IsOpen, açılışta tazeleme + giriş + odak içeri, Esc →
/// <see cref="PopoverBase.CloseRequested"/>); kartın kendine özgü farkları giriş animasyonu (<see cref="PopIn.PlayDropInFromRight"/> —
/// yukarıdan, sağ üst köşeden: kart butonun sağ kenarından sarkar), odağın <c>Cancel</c>'a gitmesi ve iki isteğidir: <c>Cancel</c>
/// kapanma, birincil düğme çıkış ister (<see cref="ActionRequested"/>) — ikisine de kabuk cevap verir (<c>MainWindow.Exit.cs</c>).
///
/// <para><b>Veri VM'dedir:</b> iş <see cref="RunViewModel.ExitWork"/>, ilerleme <see cref="RunViewModel.ExitProgress"/>, uçuştaki proje
/// sayısı <see cref="RunCounters.Building"/>; metinler <see cref="ExitText"/>'ten. Kart açıkken iş biterse VM <c>ExitWork</c>'ü duyurur ve
/// kart kendini "Nothing is running." hâline günceller — uçuşta iş yokken süreç bloğu, ilerleme ve sonuç satırı yoktur.</para>
///
/// <para><b>Süreçler kabuktandır</b> (<see cref="Processes"/>): outer job'un sayımı (<see cref="JobProcessCensus"/>) yalnız kart açıkken
/// alınır. Satırlar liste değişince yeniden kurulur; aynı liste yeniden verildiğinde ağaç dokunulmaz (sayım saniyede bir gelir).</para>
/// </summary>
public partial class ExitCard : PopoverBase
{
    /// <summary>Süreç satırları arasındaki boşluk.</summary>
    private const double ProcessRowGap = 4;

    /// <summary>Sonuç satırı ile düğmeler arasındaki boşluk — sonuç satırı yokken düğmeler bloğun üstündedir.</summary>
    private static readonly Thickness ActionsBelowOutcome = new(0, 12, 0, 0);

    /// <summary>Kartın tazelendiği VM özellikleri: iş, ilerlemenin iki sayacı ve koşu durumu, uçuştaki proje sayısı.</summary>
    private static readonly HashSet<string> WatchedProperties =
    [
        nameof(RunViewModel.ExitWork), nameof(RunViewModel.FinishedOfWillBuild), nameof(RunViewModel.WillBuildCount),
        nameof(RunViewModel.IsRunning), nameof(RunViewModel.Counters),
    ];

    private IReadOnlyList<ProcessTally> _processes = [];

    /// <summary>Son çizilen süreç satırları — aynı liste ağacı yeniden kurmaz.</summary>
    private IReadOnlyList<ProcessTally> _drawnProcesses = [];

    public ExitCard()
    {
        InitializeComponent();
        // Cancel kartı kapatır — Esc ile AYNI istek; kapanış ve odağın butona dönüşü kabuğun cevabındadır.
        PART_Cancel.Click += (_, _) => RequestClose();
        PART_Action.Click += (_, _) => ActionRequested?.Invoke();
    }

    /// <summary>Birincil düğmeye basıldı: kabuk kartı kapatır ve çıkışı ister (iş varsa kapanış ekranıyla).</summary>
    public event Action? ActionRequested;

    /// <summary>"ENDS ON EXIT" listesi — kabuğun sayımı (<see cref="JobProcessCensus.Take"/>). Boşsa blok çizilmez.</summary>
    internal IReadOnlyList<ProcessTally> Processes
    {
        get => _processes;
        set
        {
            _processes = value ?? [];
            RefreshContent();
        }
    }

    /// <summary>Açılışta odak <c>Cancel</c>'a gider — tasarımın kuralı ve kartın yıkıcı olmayan seçeneği.</summary>
    protected override UIElement InitialFocusTarget => PART_Cancel;

    protected override void PlayEntrance() => PopIn.PlayDropInFromRight(this);

    protected override void SubscribeVm(RunViewModel vm) => vm.PropertyChanged += OnVmPropertyChanged;

    protected override void UnsubscribeVm(RunViewModel vm) => vm.PropertyChanged -= OnVmPropertyChanged;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && WatchedProperties.Contains(name)) RefreshContent();
    }

    /// <summary>İşi, ilerlemeyi, süreçleri ve kararı karta yazar. Uçuşta iş yokken (<see cref="ExitWork.None"/>) yalnız etiket, cümle ve
    /// <c>Exit</c> düğmesi kalır.</summary>
    protected override void RefreshContent()
    {
        var vm = Vm;
        var work = vm?.ExitWork ?? ExitWork.None;
        bool idle = work == ExitWork.None;

        PART_Heading.Text = ExitText.Heading(work);
        string? progress = idle ? null : vm?.ExitProgress;
        PART_Progress.Text = progress ?? "";
        PART_Progress.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        PART_Sentence.Text = ExitText.Sentence(work);

        var processes = idle ? [] : _processes;
        PART_ProcessesBlock.Visibility = processes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DrawProcesses(processes);

        string? outcome = ExitText.Outcome(work, vm?.Counters.Building ?? 0);
        PART_Outcome.Text = outcome ?? "";
        PART_Outcome.Visibility = outcome is null ? Visibility.Collapsed : Visibility.Visible;
        PART_Actions.Margin = outcome is null ? new Thickness(0) : ActionsBelowOutcome;

        string action = ExitText.Action(work);
        PART_ActionLabel.Text = action;
        AutomationProperties.SetName(PART_Action, action);
    }

    /// <summary>Süreç satırlarını kurar — liste son çizilenle aynıysa hiçbir şey yapmaz.</summary>
    private void DrawProcesses(IReadOnlyList<ProcessTally> processes)
    {
        if (processes.SequenceEqual(_drawnProcesses)) return;
        _drawnProcesses = processes;
        PART_Processes.Children.Clear();
        for (int i = 0; i < processes.Count; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : ProcessRowGap, 0, 0) };
            var count = ProcessText(ExitText.ProcessCount(processes[i].Count), "Brush.TextDim");
            DockPanel.SetDock(count, Dock.Right);
            count.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(count);
            row.Children.Add(ProcessText(processes[i].Name, "Brush.TextSecondary"));
            PART_Processes.Children.Add(row);
        }
    }

    /// <summary>Bir süreç satırının metni: mono 11px, verilen renk token'ı.</summary>
    private static TextBlock ProcessText(string text, string brushKey)
    {
        var block = new TextBlock { Text = text, FontFamily = AppFonts.Mono, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.2xs");
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }
}
