using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a] K11 perf tablosu: Full(6, cap yok, Normal) · Balanced(4, %70, BelowNormal) · Light(2, %40, Idle).
/// <see cref="PerfProfile"/> SAF'tır — Win32/WPF türü taşımaz, priority kendi enum'uyla
/// (<see cref="ProcessPriorityClassKind"/>) ifade edilir. Paralellik değerleri bugün App'te ayrı duran
/// tabloyla (<c>RunViewModel.ParallelismFor</c>) aynı olmak ZORUNDA; tek doğruluk kaynağına geçiş P2'de
/// yapılacağı için bu test o güne kadar iki tablonun sessizce ayrışmasını yakalar.
/// </summary>
public class PerfProfileTests
{
    [Fact]
    public void Perf_table_matches_the_K11_decision_for_all_three_modes()
    {
        Assert.Equal(new PerfProfile(6, null, ProcessPriorityClassKind.Normal), PerfProfile.For(PerfMode.Full));
        Assert.Equal(new PerfProfile(4, 70, ProcessPriorityClassKind.BelowNormal), PerfProfile.For(PerfMode.Balanced));
        Assert.Equal(new PerfProfile(2, 40, ProcessPriorityClassKind.Idle), PerfProfile.For(PerfMode.Light));
    }

    [Theory]
    [InlineData("Full")]
    [InlineData("Balanced")]
    [InlineData("Light")]
    public void Try_parse_resolves_the_three_perf_mode_strings_the_app_uses(string text)
        => Assert.Equal(PerfProfile.For(Enum.Parse<PerfMode>(text)), PerfProfile.TryParse(text));

    [Theory]
    [InlineData("full")]   // eşleşme ordinal — App'in yazdığı string birebir gelir
    [InlineData("")]
    [InlineData("Turbo")]
    public void Try_parse_returns_null_for_an_unknown_perf_mode_text(string text)
        => Assert.Null(PerfProfile.TryParse(text));

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
