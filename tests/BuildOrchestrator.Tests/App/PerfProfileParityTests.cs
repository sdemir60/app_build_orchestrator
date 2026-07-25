using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T20-a/T20-b] App'in perf yüzeyinin Core'daki <see cref="PerfProfile"/> tablosuyla uyumu.
///
/// <para>[Fix round 1 — minor 4] P2'den sonra App'in KENDİ tablosu yok (<c>ParallelismFor</c> kaldırıldı), bu
/// yüzden buradaki iddia artık "iki tablo eşit mi" DEĞİLDİR (o soru totolojiye dönüştü). Pinlenen şey App'in
/// İKİ giriş yolunun o tablodan doğru satırı çekmesidir: kalıcı <b>seed</b> (<see cref="RunViewModel.SetPerfMode"/>,
/// UiState'ten) ve <b>chip döngüsü</b> (<see cref="RunViewModel.CyclePerfAsync"/>). Seed yolunun geçersiz-değer
/// guard'ı da burada pinlenir — o guard P2'de değişti ve testsiz kalmıştı.</para>
///
/// <para>App VM'ini (dolayısıyla <c>EngineHost</c> harness'ını) sürdüğü için Core'un saf
/// <c>ProcessControl</c> testlerinin yanında değil, <b>App tarafında</b> durur — saf tablo testleri
/// <see cref="BuildOrchestrator.Tests.ProcessControl.PerfProfileTests"/>'tedir.</para>
/// </summary>
public class PerfProfileParityTests
{
    // Başlatılmaz — VM harness'ı (RunViewModelStateTests deseni).
    private static RunViewModel NewVm(EngineHost engine) =>
        new(engine, new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1");

    // [D6 persistence] Seed yolu: UiState'ten okunan her profil adı Core tablosunun O satırını uygular.
    [Theory]
    [InlineData("Full")]
    [InlineData("Balanced")]
    [InlineData("Light")]
    public async Task The_persisted_seed_path_applies_the_core_table_row_for_every_profile_name(string mode)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.SetPerfMode(mode);

        Assert.Equal(mode, vm.PerfMode);
        Assert.Equal(PerfProfile.TryParse(mode)!.Value.Parallelism, vm.Parallelism);
    }

    // [Fix round 1 — minor 3] Seed guard'ı: tanınmayan değer (bayat/bozuk UiState) NO-OP'tur — son geçerli
    // profil korunur. Bu, <see cref="RunViewModel.ProfileFor"/>'un Balanced fallback'inden BİLEREK farklı bir
    // karardır: bir seed sessizce BAŞKA bir profile kaymamalıdır (kullanıcı öyle bir şey seçmedi).
    [Fact]
    public async Task An_invalid_persisted_seed_is_ignored_instead_of_falling_back()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.SetPerfMode("Light");

        vm.SetPerfMode("Turbo");

        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(PerfProfile.For(PerfMode.Light).Parallelism, vm.Parallelism);
    }

    [Fact]
    public async Task The_perf_chip_cycles_balanced_light_full_and_tracks_the_table_at_every_step()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        var seen = new List<string>();
        var parallelisms = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            seen.Add(vm.PerfMode);
            parallelisms.Add(vm.Parallelism);
            await vm.CyclePerfAsync();
        }

        Assert.Equal(["Balanced", "Light", "Full"], seen);
        // Beklenen paralellikler TABLODAN türetilir (literal 4/2/6 yazılmaz): chip her adımda o satırı çekmeli.
        Assert.Equal([.. seen.Select(m => PerfProfile.TryParse(m)!.Value.Parallelism)], parallelisms);
    }
}
