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
}
