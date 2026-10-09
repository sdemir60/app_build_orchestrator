using System.Reflection;
using System.Windows.Threading;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Motor olaylarının pencereye inişi, üretim kablajından geçerek: bir olay PATLAMASI UI thread'ini tek blokta tutmaz (arkasında
/// bekleyen girdi patlama bitmeden sıra alır), olaylar geliş sırasıyla uygulanır ve motorun çıkışı, ondan ÖNCE gönderilmiş
/// olaylardan sonra uygulanır. Pompanın kendi sözleşmesi (dilim, istisna, çok üretici) <see cref="EngineEventPumpTests"/>'te.
///
/// <para><b>Neden (ÖLÇÜLDÜ, 2026-10-10):</b> koşu başında motor <c>runStarted</c> + <c>buildPreview</c> + her atlanan proje
/// için bir <c>projectSkipped</c> gönderir (OSYS'te değişmemiş bir Build: 186 olay, aynı anda). Pencere her olayı ayrı bir
/// <c>Dispatcher.InvokeAsync</c> ile Normal öncelikte taşıyordu; Normal, çizimin ve girdinin ÜSTÜNDE olduğu için hepsi tek
/// blokta koştu — iz: UI thread'i 312 ms aralıksız, gerçek koşuda her koşunun başında 115–145 ms girdi gecikmesi ve 210–260
/// ms'lik kare boşluğu.</para>
///
/// <para>Olaylar motorun <c>EventReceived</c>'ından (okuma thread'ini taklit eden bir arka plan görevinden) pencerenin kendi
/// aboneliğine iner; <c>EventReceived</c>/<c>EngineExited</c> alan-benzeri olaylar olduğu için çağrı, derleyicinin ürettiği
/// temsilci alanı yansımayla okunarak yapılır (alan adı değişirse <see cref="Require"/> açık bir mesajla düşer) — üretim koduna
/// test yüzeyi eklenmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EngineEventBurstTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Require<T>(object? value, string what) where T : class =>
        value as T ?? throw new InvalidOperationException($"yansıma hedefi bulunamadı: {what} — üretim kodunda adı değişti mi?");

    private static EngineHost EngineOf(MainWindow window) =>
        Require<EngineHost>(typeof(MainWindow).GetField("_engine", Hidden)?.GetValue(window), "MainWindow._engine");

    /// <summary>Olayları motorun okuma thread'i gibi, arka planda ve sırayla yayınlar.</summary>
    private static void RaiseFromReaderThread(MainWindow window, IReadOnlyList<IpcEvent> events)
    {
        var received = Require<Action<IpcEvent>>(
            typeof(EngineHost).GetField(nameof(EngineHost.EventReceived), Hidden)?.GetValue(EngineOf(window)), "EngineHost.EventReceived");
        Task.Run(() => { foreach (var ev in events) received(ev); }).Wait();
    }

    private static string[] Names => MainWindowHost.ProjectNames(MainWindowHost.OsysProjectCount);

    private static int RowsIn(RunViewModel vm, ProjectRowState state) => vm.Projects.Count(p => p.State == state);

    [StaFact]
    public void Input_queued_behind_the_run_start_burst_is_served_before_the_burst_ends()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, Names);
        var burst = new List<IpcEvent>
        {
            new RunStartedEvent("r1", RunMode.Build, Names.Length, 4, "Debug", null),
            new BuildPreviewEvent([.. Names.Select(n => new BuildPreviewItem(MainWindowHost.IdOf(n), n, false, "abc1234"))]),
        };
        burst.AddRange(Names.Select(n => new ProjectSkippedEvent("r1", MainWindowHost.IdOf(n), SkipReasons.UpToDate)));

        RaiseFromReaderThread(window, burst);
        int skippedWhenInputRan = -1;
        window.Dispatcher.InvokeAsync(() => skippedWhenInputRan = RowsIn(vm, ProjectRowState.Skipped), DispatcherPriority.Input);
        DispatcherPump.PumpUntil(() => skippedWhenInputRan >= 0 && RowsIn(vm, ProjectRowState.Skipped) == Names.Length,
            TimeSpan.FromSeconds(20));

        Assert.Equal(Names.Length, RowsIn(vm, ProjectRowState.Skipped)); // non-vacuous: patlamanın tamamı işlendi
        // Ön kabul: bu patlama tek bir dilimden (EngineEventPump.SliceBudgetMs) uzun sürer — 177 atlanma bugün ~70 ms.
        Assert.True(skippedWhenInputRan is >= 0 and < MainWindowHost.OsysProjectCount,
            $"patlamanın arkasındaki girdi {skippedWhenInputRan}/{Names.Length} atlanan olaydan sonra koştu — UI thread'i patlama " +
            "boyunca hiç bırakılmadı (ya da patlama tek bir dilime sığacak kadar hızlandı; o durumda pompa birim testleri bakar)");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_sliced_burst_still_applies_events_in_arrival_order()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, Names);
        var burst = new List<IpcEvent> { new RunStartedEvent("r1", RunMode.Build, Names.Length, 4, "Debug", null) };
        foreach (string n in Names)
        {
            burst.Add(new ProjectStartedEvent("r1", MainWindowHost.IdOf(n), n));
            burst.Add(new ProjectSucceededEvent("r1", MainWindowHost.IdOf(n), 120));
        }

        RaiseFromReaderThread(window, burst);
        DispatcherPump.PumpUntil(() => RowsIn(vm, ProjectRowState.Succeeded) == Names.Length, TimeSpan.FromSeconds(20));

        // Sıra bozulsaydı bir projenin "started"ı "succeeded"ından SONRA işlenir ve satır derleniyor durumda kalırdı.
        Assert.Equal(Names.Length, RowsIn(vm, ProjectRowState.Succeeded));
        Assert.Equal(0, RowsIn(vm, ProjectRowState.Started));
        GC.KeepAlive(window);
    }

    /// <summary>Motor çıkışı (<c>EngineExited</c>, pencerede <c>Dispatcher.Invoke</c> — Send önceliği) kuyrukta bekleyen olayların
    /// önüne geçmemeli: motorun ölmeden önce gönderdiği <c>runStarted</c> çıkıştan SONRA uygulanırsa koşu yeniden açılır,
    /// "engine stopped" mesajı silinir ve UI ölü bir motorla "Running"de kalır.</summary>
    [StaFact]
    public void An_engine_exit_is_applied_after_the_events_the_engine_sent_before_it()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, Names);
        RaiseFromReaderThread(window, [new RunStartedEvent("r1", RunMode.Build, Names.Length, 4, "Debug", null)]);

        var exited = Require<Action<int?>>(
            typeof(EngineHost).GetField(nameof(EngineHost.EngineExited), Hidden)?.GetValue(EngineOf(window)), "EngineHost.EngineExited");
        var exit = Task.Run(() => exited(1)); // çıkış izleyicisinin thread'i: Dispatcher.Invoke ile UI'ı bekler
        DispatcherPump.PumpUntil(() => exit.IsCompleted, TimeSpan.FromSeconds(10));
        DispatcherPump.DrainToIdle();

        Assert.True(exit.IsCompleted);
        Assert.False(vm.IsRunning, "motor öldükten sonra uygulanan runStarted koşuyu yeniden açtı");
        Assert.NotNull(vm.EngineDiedMessage);
        GC.KeepAlive(window);
    }
}
