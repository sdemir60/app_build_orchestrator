using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-03 · perf Faz B · B3] "Stop now": koşu <see cref="AppPhase.Stopping"/>'teyken (graceful stop
/// gitti, uçuştakiler bitiyor) İKİNCİ Stop basışı hard stop gönderir (<see cref="StopKind.Hard"/> — inner job terminate;
/// uçuştakiler <c>failed("stopped")</c> olur, sonraki Build onları baştan derler). Üçüncü basış hiçbir şey göndermez.
/// Esc yolu <see cref="EscStopTests"/>'te, düğmenin üç hâli (<c>ActionBarTests</c>) ve tepsi maddesi (<c>TrayMenuTests</c>)
/// kendi süitlerinde pinlidir; burada komutun kendisi, kapısı, Esc zincirinin girdisi ve konsol izi sürülür.
///
/// <para>Harness pencerelidir ama motorsuzdur (<see cref="MainWindowHost.NewWithSends"/>): gönderim kabul edilir, giden
/// komutlar toplanır, motorun cevabı <c>vm.OnEvent(...)</c> ile verilir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class StopNowTests
{
    private static StopRunCommand[] StopsOf(List<IpcCommand> sent) => [.. sent.OfType<StopRunCommand>()];

    /// <summary>İlk basış graceful, ikincisi hard gider — ikisi de KOŞAN run'ın kimliğiyle; üçüncü basış hiçbir şey göndermez
    /// (hard geri alınmaz ve ikinci kez gitmez).</summary>
    [StaFact]
    public async Task A_second_stop_while_stopping_sends_a_hard_stop_once()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(AppPhase.Stopping, vm.Phase); // ön-koşul: ilk basış graceful gitti
        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        StopRunCommand[] expected = [new("r1", StopKind.Graceful), new("r1", StopKind.Hard)];
        Assert.Equal(expected, StopsOf(sent));
        GC.KeepAlive(window);
    }

    /// <summary>Komutun kapısı Stopping'de AÇIK kalır (ikinci basış hard'dır) ve hard gidince kapanır — düğme, tepsi maddesi
    /// ve satır Stop'u aynı <c>StopCommand</c>'ı okur, yani üçünün de durumu bu kapıdan gelir.</summary>
    [StaFact]
    public async Task The_stop_command_stays_executable_while_stopping_until_the_hard_stop_is_sent()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        Assert.True(vm.StopCommand.CanExecute(null)); // ön-koşul
        Assert.False(vm.HardStopRequested);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(AppPhase.Stopping, vm.Phase);
        Assert.True(vm.StopCommand.CanExecute(null), "graceful stop istenmiş koşuda ikinci basış açık olmalı");

        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.HardStopRequested);
        Assert.False(vm.StopCommand.CanExecute(null), "hard gittikten sonra kapı kapanmalı");
        GC.KeepAlive(window);
    }

    /// <summary>Hard stop konsola TEK satır bırakır (tıklamanın kalıcı kaydı); graceful basış onu yazmaz, üçüncü basış
    /// ikinci kez yazmaz.</summary>
    [StaFact]
    public async Task The_hard_stop_request_writes_one_console_line()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.DoesNotContain(RunViewModel.StopNowRequestedLine, vm.GetRunDocumentText()); // ön-koşul: graceful basış bunu yazmaz

        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        Assert.Single(vm.GetRunDocumentText().Split('\n'),
            line => line.Contains(RunViewModel.StopNowRequestedLine, StringComparison.Ordinal));
        GC.KeepAlive(window);
    }

    /// <summary>Hard stop bitince konsol KAÇ derlemenin sonlandırıldığını söyler. Sayı hard stop'un İSTENDİĞİ andaki uçuş
    /// sayısıdır: motor uçuştakileri <c>failed("stopped")</c> raporladıktan SONRA <c>runStopped</c> yazar (gerçek sıra burada
    /// sürülür), yani <c>runStopped</c> geldiğinde <c>Counters.Building</c> çoktan 0'dır — sayıyı orada okumak "0 compiles
    /// terminated" derdi.</summary>
    [StaFact]
    public async Task A_hard_stop_ending_says_how_many_in_flight_compiles_it_terminated()
    {
        using var temp = new TempDir();
        var names = MainWindowHost.ProjectNames(3);
        var (window, vm, _) = MainWindowHost.NewWithSends(temp, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, names[0]);
        MainWindowHost.StartProject(vm, names[1]);
        Assert.Equal(2, vm.Counters.Building); // ön-koşul: iki derleme uçuşta

        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf(names[0]), 100, "stopped"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf(names[1]), 100, "stopped"));
        Assert.Equal(0, vm.Counters.Building); // ön-koşul: motor sırası — runStopped'a kadar uçuş sayacı boşalmıştır
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));

        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.Contains(RunViewModel.HardStoppedLine(2), vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>Graceful bitiş konsola "sonlandırıldı" satırı eklemez: o satır yalnız hard stop'a aittir.</summary>
    [StaFact]
    public async Task A_graceful_stop_ending_says_nothing_about_terminated_compiles()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        await vm.StopCommand.ExecuteAsync(null);

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));

        Assert.Equal(AppPhase.Stopped, vm.Phase); // ön-koşul
        Assert.DoesNotContain("terminated", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>Hard bayrağı koşuya aittir: sonraki koşu başlarken düşer. Düşmezse hard stop kullanılmış bir oturumda sonraki
    /// Build'in Stop'u sonsuza dek pasif kalırdı.</summary>
    [StaFact]
    public async Task The_hard_stop_gate_reopens_for_the_next_run()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.HardStopRequested); // ön-koşul: hard gitti
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));
        VmTopology.Seed(vm);

        await vm.BuildCommand.ExecuteAsync(null); // yeni koşu

        Assert.True(vm.IsStarting); // ön-koşul: startRun gitti, runStarted gelmedi
        Assert.False(vm.HardStopRequested);
        Assert.True(vm.StopCommand.CanExecute(null));
        GC.KeepAlive(window);
    }

    /// <summary>Esc zincirinin girdisi faza bakar, kapıya DEĞİL: graceful gittikten sonra Stop'un kapısı hâlâ açıktır ama
    /// durum "Stoppable" değil "Stopping"tir — ikincisi hard'dır, birincisi graceful. Hard gittikten sonra da Stopping
    /// kalır (üçüncü Esc komutun kapısında yutulur).</summary>
    [StaFact]
    public async Task The_esc_chain_reads_Stopping_through_the_whole_stop()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        Assert.Equal(EscRunState.Stoppable, vm.EscRunState); // ön-koşul

        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.StopCommand.CanExecute(null)); // ön-koşul: hard için kapı HÂLÂ açık
        Assert.Equal(EscRunState.Stopping, vm.EscRunState);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(EscRunState.Stopping, vm.EscRunState);
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now · M1] Hard stop istendi ama motor hiçbir şeyi sonlandırmadı: uçuştakiler talep ile sonlandırma
    /// arasında kendi başarısıyla bitti (koordinatör zaten kapanıyorsa host ikinci bir <c>runStopped(WasHard)</c> yazar).
    /// Konsol "terminated" iddia ETMEZ — <c>runStopped</c> konsola hiçbir satır eklemez.</summary>
    [StaFact]
    public async Task A_hard_stop_that_found_nothing_to_terminate_adds_no_terminated_line()
    {
        using var temp = new TempDir();
        var names = MainWindowHost.ProjectNames(2);
        var (window, vm, _) = MainWindowHost.NewWithSends(temp, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, names[0]);
        MainWindowHost.StartProject(vm, names[1]);
        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(vm.HardStopRequested); // ön-koşul: hard gitti
        MainWindowHost.SucceedProject(vm, names[0]); // uçuştakiler kendi başarısıyla döndü — hiçbiri sonlandırılmadı
        MainWindowHost.SucceedProject(vm, names[1]);
        string beforeEnd = vm.GetRunDocumentText();

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));

        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.Equal(beforeEnd, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now · M1] Sayı hard stop İSTENDİKTEN sonra motorun <c>failed("stopped")</c> raporladığı projelerdir; talep
    /// anındaki uçuş sayısı değil. Dört uçuştan biri kendi başarısıyla, biri kendi hatasıyla ("exit 1") döndü — ikisi de
    /// sonlandırılmadı; yalnız iki proje sonlandırıldı.</summary>
    [StaFact]
    public async Task A_hard_stop_counts_only_the_projects_the_engine_reports_as_stopped()
    {
        using var temp = new TempDir();
        var names = MainWindowHost.ProjectNames(4);
        var (window, vm, _) = MainWindowHost.NewWithSends(temp, names);
        MainWindowHost.StartBuild(vm, names);
        foreach (var name in names) MainWindowHost.StartProject(vm, name);
        Assert.Equal(4, vm.Counters.Building); // ön-koşul: dört derleme uçuşta
        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);

        MainWindowHost.SucceedProject(vm, names[0]);
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf(names[1]), 100, "exit 1"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf(names[2]), 100, "stopped"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf(names[3]), 100, "stopped"));
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));

        Assert.Contains(RunViewModel.HardStoppedLine(2), vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now · M2] Bitiş satırı sayıyı doğru çekimle söyler (<c>StreamText.Counted</c> kuralı): tekil sayıda
    /// "1 in-flight compiles" kopya-yapıştır kokusudur.</summary>
    [Fact]
    public void The_hard_stopped_line_uses_the_right_grammar_for_one_and_many()
    {
        Assert.Equal("stopped — 1 in-flight compile terminated", RunViewModel.HardStoppedLine(1));
        Assert.Equal("stopped — 2 in-flight compiles terminated", RunViewModel.HardStoppedLine(2));
    }

    /// <summary>[Stop now · M5] Hard gönderimi senkron düşerse (pipe koptu) istek geri alınır: kapı yeniden açılır, düğme
    /// "Terminating…" demez ("Stop now" kalır) ve kullanıcı yeniden deneyebilir — graceful'daki hata yolunun deseni.</summary>
    [StaFact]
    public async Task A_hard_stop_that_cannot_be_sent_is_taken_back_and_can_be_retried()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        await vm.StopCommand.ExecuteAsync(null); // graceful gitti
        vm.DebugSendOverride = cmd => cmd is StopRunCommand { Kind: StopKind.Hard }
            ? Task.FromException(new InvalidOperationException("pipe is broken"))
            : Task.CompletedTask;

        await vm.StopCommand.ExecuteAsync(null);

        Assert.False(vm.HardStopRequested);
        Assert.Equal(StopStage.StopNow, vm.StopStage);
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.Contains("[error] failed to send stop: pipe is broken", vm.GetRunDocumentText());

        vm.DebugSendOverride = _ => Task.CompletedTask; // pipe düzeldi
        await vm.StopCommand.ExecuteAsync(null);

        StopRunCommand[] expected = [new("r1", StopKind.Graceful), new("r1", StopKind.Hard), new("r1", StopKind.Hard)];
        Assert.Equal(expected, StopsOf(sent));
        Assert.Equal(StopStage.Terminating, vm.StopStage);
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now] Üç yüzün (düğme, tepsi, satır ikonu) ve komutun/Esc zincirinin/çıkışın ortak durumu: istenmedi →
    /// graceful gitti → hard gitti → koşu bitti. Değişim yalnız gerçekten değiştiğinde duyurulur (fazın Running'e geçişi
    /// aşamayı değiştirmez).</summary>
    [StaFact]
    public async Task The_stop_stage_walks_Stop_StopNow_Terminating_and_back_to_Stop_when_the_run_ends()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        var seen = new List<StopStage>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RunViewModel.StopStage)) seen.Add(vm.StopStage); };
        MainWindowHost.StartBuild(vm);
        Assert.Equal(StopStage.Stop, vm.StopStage); // ön-koşul: koşu sürüyor, durdurma istenmedi
        Assert.Empty(seen);

        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));

        StopStage[] expected = [StopStage.StopNow, StopStage.Terminating, StopStage.Stop];
        Assert.Equal(expected, seen);
        Assert.Equal(StopText.Label(StopStage.Stop), vm.StopLabel);
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now] Üç aşamanın SÖZCÜKLERİ burada bir kez pinlenir; yüzler ve diğer testler metni <see cref="StopText"/>'ten
    /// okur (tek kaynak).</summary>
    [Fact]
    public void The_stop_stages_read_Stop_then_Stop_now_then_Terminating()
    {
        Assert.Equal("Stop", StopText.Label(StopStage.Stop));
        Assert.Equal("Stop now", StopText.Label(StopStage.StopNow));
        Assert.Equal("Terminating…", StopText.Label(StopStage.Terminating));
    }
}
