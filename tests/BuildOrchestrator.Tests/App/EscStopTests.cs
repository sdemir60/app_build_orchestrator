using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Esc zincirinin koşu katmanı, pencerenin GERÇEK Esc bağlamasından sürülür
/// (<see cref="MainWindowInputTests"/> deseni): hiçbir katman açık değilken Esc çalışan Build/Rebuild/Clean'i durdurur;
/// durdurma zaten sürerken tekrar Esc ikinci bir stop GÖNDERMEZ, şerit satırı kısa bir vurguyla "duyuldu" der;
/// durdurulamayan bir iş (Sync, Deep Clean, Optimize, checkout, pull) sürerken konsola tek satır düşer — aynı işte
/// yalnız ilk basışta. Görünmeyen sessiz Sync'te Esc bir şey söylemez.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EscStopTests
{
    private static void PressEscape(MainWindow window)
    {
        var escape = window.InputBindings.OfType<KeyBinding>()
            .Single(k => k.Key == Key.Escape && k.Modifiers == ModifierKeys.None);
        if (escape.Command.CanExecute(null)) escape.Command.Execute(null);
    }

    private static (MainWindow window, RunViewModel vm, List<IpcCommand> sent) NewWindowWithSends(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        vm.RootPath = @"C:\src\OSYS";
        MainWindowHost.AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        return (window, vm, sent);
    }

    private static int Occurrences(string text, string line) =>
        text.Split('\n').Count(l => l.Contains(line, StringComparison.Ordinal));

    // ---------------------------------------------------------------- durdur

    [StaFact]
    public void Escape_with_nothing_open_stops_a_running_build()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = NewWindowWithSends(temp);
        MainWindowHost.StartBuild(vm);

        PressEscape(window);

        Assert.Equal(AppPhase.Stopping, vm.Phase);
        Assert.Single(sent.OfType<StopRunCommand>(), s => s.Kind == StopKind.Graceful);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_clears_a_selection_before_it_stops_the_build()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = NewWindowWithSends(temp);
        MainWindowHost.StartBuild(vm);
        vm.SelectProject(@"C:\p\a.csproj");

        PressEscape(window);
        Assert.Null(vm.SelectedProjectId);
        Assert.Empty(sent.OfType<StopRunCommand>()); // ilk Esc yalnız seçimi bıraktı

        PressEscape(window);
        Assert.Single(sent.OfType<StopRunCommand>());
        GC.KeepAlive(window);
    }

    /// <summary>Durdurma sürerken tekrar Esc: ikinci bir stop GİTMEZ ve konsola satır EKLENMEZ; VM şeride "duyuldu"
    /// sinyalini verir.</summary>
    [StaFact]
    public void A_second_escape_while_stopping_sends_nothing_and_acknowledges_on_the_ribbon()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = NewWindowWithSends(temp);
        MainWindowHost.StartBuild(vm);
        PressEscape(window);
        Assert.Equal(AppPhase.Stopping, vm.Phase); // ön-koşul
        int acknowledged = 0;
        vm.StopRequestAcknowledged += (_, _) => acknowledged++;
        string console = vm.GetRunDocumentText();

        PressEscape(window);

        Assert.Single(sent.OfType<StopRunCommand>());
        Assert.Equal(1, acknowledged);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- durdurulamaz işler

    [StaFact]
    public async Task Escape_during_a_sync_says_once_that_it_cannot_be_stopped()
    {
        using var temp = new TempDir();
        var (window, vm, _) = NewWindowWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);
        Assert.True(vm.SyncBusy); // ön-koşul: motorun cevabı bekleniyor

        PressEscape(window);
        PressEscape(window);

        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.Sync)));
        GC.KeepAlive(window);
    }

    /// <summary>"Aynı işte yalnız ilk basışta" kuralının öbür yüzü: iş bitip YENİ bir iş başlayınca satır yeniden
    /// düşer (bu kez o işin adıyla).</summary>
    [StaFact]
    public async Task A_new_operation_gets_its_own_cannot_be_stopped_line()
    {
        using var temp = new TempDir();
        var (window, vm, _) = NewWindowWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);
        PressEscape(window);
        MainWindowHost.ReplySync(vm, ("A", null)); // Sync bitti — meşguliyet kalktı

        await vm.CleanCommand.ExecuteAsync(null); // derin Clean: motorun cevabı bekleniyor
        Assert.True(vm.CleanBusy); // ön-koşul
        PressEscape(window);

        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.DeepClean)));
        GC.KeepAlive(window);
    }

    /// <summary>Sessiz Sync kullanıcıya görünmez (pill yazılmaz, transkript akmaz) — Esc onun hakkında konuşmaz;
    /// konuşsaydı kullanıcının başlatmadığı bir işi anlatırdı.</summary>
    [StaFact]
    public async Task Escape_during_a_silent_sync_says_nothing()
    {
        using var temp = new TempDir();
        var (window, vm, _) = NewWindowWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Silent);
        Assert.True(vm.SyncBusy); // ön-koşul
        string console = vm.GetRunDocumentText();

        PressEscape(window);

        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- şerit vurgusu

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1");

    /// <summary>Vurgu şerit satırının opaklığında kısa bir saattir; motion açıkken GERÇEKTEN kurulur (karşıtı
    /// aşağıda: reduced-motion'da hiç kurulmaz — ikisi birlikte kapının iki yönlü çalıştığını söyler).</summary>
    [StaFact]
    public void The_ribbon_line_pulses_when_a_stop_request_is_acknowledged_with_motion_on()
    {
        var vm = NewVm();
        var ribbon = new StickyRibbon { DataContext = vm, AnimationsEnabledProvider = () => true };
        var window = DsResources.Realize(DsResources.NewHost(), ribbon);
        Assert.False(ribbon.PhaseText.HasAnimatedProperties); // ön-koşul

        vm.AcknowledgeStopRequest();

        Assert.True(ribbon.PhaseText.HasAnimatedProperties, "şerit satırının vurgu saati kurulmadı");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_ribbon_line_does_not_pulse_under_reduced_motion()
    {
        var vm = NewVm();
        var ribbon = new StickyRibbon { DataContext = vm, AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), ribbon);

        vm.AcknowledgeStopRequest();

        Assert.False(ribbon.PhaseText.HasAnimatedProperties);
        GC.KeepAlive(window);
    }
}
