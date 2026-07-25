using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T20-a] Core'daki <see cref="PerfProfile"/> tablosu ile App'in perf chip'inin ürettiği paralellik
/// DEĞERLERİNİN aynı olduğunu doğrular. Bugün iki tablo ayrı duruyor (<c>RunViewModel.ParallelismFor</c>);
/// tek doğruluk kaynağına geçiş P2'de yapılacağı için bu test o güne kadar sessiz ayrışmayı yakalar.
///
/// <para>App VM'ini (dolayısıyla <c>EngineHost</c> harness'ını) sürdüğü için Core'un saf
/// <c>ProcessControl</c> testlerinin yanında değil, <b>App tarafında</b> durur — saf tablo testleri
/// <see cref="BuildOrchestrator.Tests.ProcessControl.PerfProfileTests"/>'tedir.</para>
/// </summary>
public class PerfProfileParityTests
{
    [Fact]
    public async Task Parallelism_table_matches_the_perf_chip_cycle_in_the_app()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // başlatılmaz — VM harness'ı (RunViewModelStateTests deseni)
        var vm = new RunViewModel(engine, new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1");

        // Chip döngüsü Balanced → Light → Full → Balanced: üç modun da App-tarafı paralelliği okunur.
        var seen = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var profile = PerfProfile.TryParse(vm.PerfMode);
            Assert.NotNull(profile);
            Assert.Equal(profile.Value.Parallelism, vm.Parallelism);
            seen.Add(vm.PerfMode);
            vm.CyclePerf();
        }
        Assert.Equal(["Balanced", "Light", "Full"], seen);
    }
}
