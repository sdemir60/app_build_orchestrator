using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [RESOLVE Faz 4 / karar 11 — kullanıcı onayı] Settings → General → BUILD: <c>Resolve cycles at full priority</c>.
/// Açıkken (varsayılan) Resolve cycles koşusu profilin işçi sayısıyla ama cap'siz ve Normal öncelikte koşar; kapalıyken
/// profili izler. Burada ayara özgü halka durur: değer <see cref="StartRunCommand.ResolveAtFullPriority"/> ile motora gider. Ayarın
/// zinciri — katalog → taslak → ayar dosyası → Save (UiState + <see cref="RunViewModel.ResolveAtFullPriority"/>) → kabuğun açılış tohumu
/// ve kalıcılığı — öteki General ayarlarıyla birlikte <see cref="GeneralSettingChainTests"/>'tedir. Motorun bayrakla ne yaptığı
/// <c>PerfProfileTests</c> ve <c>RunCoordinatorTests</c>'tedir.
/// </summary>
public class ResolveAtFullPrioritySettingsTests
{
    [Fact]
    public async Task The_run_command_carries_the_setting()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        run.ResolveAtFullPriority = false;
        await run.BuildCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(sent.OfType<StartRunCommand>()).ResolveAtFullPriority);
    }
}
