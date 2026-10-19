using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;
using static BuildOrchestrator.Tests.ByteUnits;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1 · karar K3-K8 · plan R2-R5] <see cref="RunViewModel"/>'in şerit özellikleri: CPU ve RAM hücresinin
/// metinleri/tooltip'leri (örnekleyicinin <see cref="RunViewModel.ApplyResourceSample"/> ile ittiği sayılardan) ve paralellik hücresinin
/// payı, paydası, mod sözcüğü ve tooltip'i (motor olayları VM'e oynatılarak — ekran modeli, pencere yok).
///
/// <para>Hücre KOŞUYU anlatır (R2): <c>runStarted</c> → koşu sonu arası, tam koşuda, koşu BAŞINDAKİ mod ve paydayla; çip koşu ortasında
/// dönse hücre kıpırdamaz, koşu bitince çipi izler. Tek proje koşusunda (R3, <c>RunTargetId</c> dolu) hücre çipi CANLI izler. CPU
/// tooltip'inin cap'i hücrenin moduyla aynıdır (R4). Koşu <see cref="StartRunCommand"/>'la, üretimdeki yoldan başlar (komut gönderimi
/// kabul edilir: <see cref="MainWindowHost.AcceptSends"/>); motorun cevabını (<c>runStarted</c> …) test verir. Ürün adı hiçbir beklentide
/// literal yazılmaz: <see cref="AppIdentity.Product"/>'tan gelir.</para>
/// </summary>
public class ResourceStripViewModelTests
{
    private static readonly string[] ProjectNames = ["A", "B", "C"];

    /// <summary>Üç projeli, çipi <paramref name="perfMode"/> olan VM. Komutlar motora "gider" (cevabı test verir).</summary>
    private static RunViewModel NewVm(EngineHost engine, string perfMode = "Balanced")
    {
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", PerfMode = perfMode };
        VmTopology.Seed(vm, [.. ProjectNames.Select(IdOf)]);
        AcceptSends(vm);
        return vm;
    }

    /// <summary>Motorun <c>runStarted</c> cevabı. <paramref name="parallelism"/> motorun FİİLİ (kırpılmış olabilir) işçi sayısıdır.</summary>
    private static RunStartedEvent Started(int total = 3, int parallelism = 4, string? reducedReason = null) =>
        new("r1", RunMode.Build, total, parallelism, "Debug", WorkersReducedReason: reducedReason);

    /// <summary>CPU tooltip'inin beklenen metni; <paramref name="cap"/> modun cap eki (<c>" · cap 70%"</c>) ya da cap'siz modda boş.</summary>
    private static string CpuTooltip(string cap) => $"CPU used by {AppIdentity.Product} — app, engine and compilers{cap}";

    private static string MemoryTooltip(string app, string engine, string compilers) =>
        $"Memory used by {AppIdentity.Product} · app {app} · engine {engine} · compilers {compilers}";

    // ---------------------------------------------------------------- boşta: hücre çipi izler (anında)

    [Fact]
    public async Task An_idle_view_model_describes_the_chip_in_the_parallel_cell_and_the_chips_cap_in_the_cpu_tooltip()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        Assert.Equal("0", vm.StripBuildingText);
        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);
    }

    /// <summary>Hiçbir olay gelmemiş, çip dokunulmamış ilk karedeki VM de çipi (varsayılan Balanced) anlatır — hücre ilk olayı/ilk çip değişimini
    /// beklemez (kurucu hücreyi kurar).</summary>
    [Fact]
    public async Task A_brand_new_view_model_already_describes_the_default_chip()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal("0", vm.StripBuildingText);
        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);
    }

    /// <summary>Çip her basışta dönerken hücre AYNI ANDA değişir (örnek beklenmez): payda modun tablo paralelliği, mod sözcüğü modun adı, CPU
    /// tooltip'inin cap'i modun cap'i (Full'de ek yok).</summary>
    [Theory]
    [InlineData(1, "Light", "/2", "Light — up to 2 projects build in parallel", " · cap 40%")]
    [InlineData(2, "Full", "/6", "Full — up to 6 projects build in parallel", "")]
    [InlineData(3, "Balanced", "/4", "Balanced — up to 4 projects build in parallel", " · cap 70%")]
    public async Task Turning_the_chip_while_idle_changes_the_parallel_cell_and_the_cap_at_once(
        int presses, string mode, string allowed, string tooltip, string cap)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        for (int i = 0; i < presses; i++) await vm.CyclePerfAsync(); // Balanced → Light → Full → Balanced

        Assert.Equal(mode, vm.PerfMode);
        Assert.Equal("0", vm.StripBuildingText);
        Assert.Equal(allowed, vm.StripAllowedText);
        Assert.Equal(mode, vm.StripModeText);
        Assert.Equal(tooltip, vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(cap), vm.StripCpuTooltip);
    }

    /// <summary>Çip kalıcı durumdan (<see cref="RunViewModel.SetPerfMode"/>, açılışta) ya da doğrudan atanınca da hücre değişir.</summary>
    [Fact]
    public async Task Seeding_the_chip_from_the_saved_state_changes_the_cell_too()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.SetPerfMode("Light");

        Assert.Equal("/2", vm.StripAllowedText);
        Assert.Equal("Light", vm.StripModeText);
    }

    // ---------------------------------------------------------------- CPU ve RAM hücreleri

    /// <summary>Hiç örnek gelmeden (ilk örnek etkinleşmeden 0,5 sn sonra gelir): CPU ve RAM okunamayan değerdir, tooltip parçaları da.</summary>
    [Fact]
    public async Task Before_the_first_sample_the_cpu_and_ram_cells_are_unreadable()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        Assert.Equal(ResourceStripText.Unreadable, vm.StripCpuText);
        Assert.Equal(ResourceStripText.Unreadable, vm.StripMemoryText);
        string dash = ResourceStripText.Unreadable;
        Assert.Equal(MemoryTooltip(dash, dash, dash), vm.StripMemoryTooltip);
    }

    /// <summary>Motor uyurken (job boş) örnek: App 300 MiB, motor ve derleyiciler 0 (ölçüm). RAM hücresi yalnız App'tir, tooltip motor ve
    /// derleyicileri <c>0 MB</c> yazar — <c>—</c> değil.</summary>
    [Fact]
    public async Task A_sample_from_a_sleeping_engine_shows_the_app_alone_and_zero_for_the_rest()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.ApplyResourceSample(new ResourceSample(7, 300 * Mb, 0, 0));

        Assert.Equal("7%", vm.StripCpuText);
        Assert.Equal("300 MB", vm.StripMemoryText);
        Assert.Equal(MemoryTooltip("300 MB", "0 MB", "0 MB"), vm.StripMemoryTooltip);
    }

    /// <summary>RAM hücresi App + motor + derleyicilerin toplamıdır; 1 GB üstü tek ondalıklı GB. Tooltip parçaları ayrı ayrı yazılır.</summary>
    [Fact]
    public async Task The_ram_cell_is_the_sum_of_the_three_parts_and_the_tooltip_lists_them()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.ApplyResourceSample(new ResourceSample(69, 310 * Mb, 85 * Mb, 4505 * Mb));

        Assert.Equal("69%", vm.StripCpuText);
        Assert.Equal("4.8 GB", vm.StripMemoryText); // 4900 MB
        Assert.Equal(MemoryTooltip("310 MB", "85 MB", "4.4 GB"), vm.StripMemoryTooltip);
    }

    /// <summary>Job sorgusu düştü (Win32 hatası): CPU ve RAM hücreleri ve tooltip parçaları <c>—</c>; App'in kendi okuması tooltip'te durur.
    /// Bir sonraki başarılı örnek hücreleri düzeltir.</summary>
    [Fact]
    public async Task An_unreadable_job_blanks_the_cpu_and_ram_cells_and_the_next_good_sample_recovers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        string dash = ResourceStripText.Unreadable;

        vm.ApplyResourceSample(new ResourceSample(null, 300 * Mb, null, null));

        Assert.Equal(dash, vm.StripCpuText);
        Assert.Equal(dash, vm.StripMemoryText);
        Assert.Equal(MemoryTooltip("300 MB", dash, dash), vm.StripMemoryTooltip);

        vm.ApplyResourceSample(new ResourceSample(2, 300 * Mb, 0, 0));

        Assert.Equal("2%", vm.StripCpuText);
        Assert.Equal("300 MB", vm.StripMemoryText);
    }

    // ---------------------------------------------------------------- koşuda: hücre KOŞUYU anlatır (R2, K4, K8)

    /// <summary>
    /// Tam koşu koşu başındaki modla anlatılır: Balanced'ta başlayan koşuda çip Full'e çevrilse bile hücre <c>/4</c> ve <c>Balanced</c> kalır
    /// (işçi sayısı koşu başında sabittir) ve CPU tooltip'i Balanced'ın cap'ini söyler (R4); koşu bitince hücre çipi izler: <c>/6</c>,
    /// <c>Full</c>, cap'siz.
    /// </summary>
    [Fact]
    public async Task A_full_run_keeps_the_mode_it_started_in_while_the_chip_moves_and_the_cell_follows_the_chip_again_when_it_ends()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null); // StartRunCommand(PerfMode = "Balanced") gider
        vm.OnEvent(Started());

        await vm.CyclePerfAsync(); // Balanced → Light
        await vm.CyclePerfAsync(); // Light → Full

        Assert.Equal("Full", vm.PerfMode);
        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);

        CompleteRun(vm, 3);

        Assert.False(vm.IsRunning);
        Assert.Equal("/6", vm.StripAllowedText);
        Assert.Equal("Full", vm.StripModeText);
        Assert.Equal("Full — up to 6 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(""), vm.StripCpuTooltip);
    }

    /// <summary>Yakalanan mod GÖNDERİLEN moddur: komut gittikten sonra, <c>runStarted</c> gelmeden önce (planlama penceresi, çip canlıdır)
    /// çip dönse de koşu komutla giden modla başlar. Pencere boyunca hücre henüz boşta metnidir ve çipi izler.</summary>
    [Fact]
    public async Task The_run_is_described_by_the_mode_its_command_carried_even_if_the_chip_moved_while_it_was_planning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null); // Balanced gitti; runStarted henüz yok

        await vm.CyclePerfAsync(); // planlama penceresinde çip → Light

        Assert.False(vm.IsRunning);
        Assert.Equal("/2", vm.StripAllowedText); // koşu başlamadı: hücre çipi izliyor
        Assert.Equal("Light", vm.StripModeText);

        vm.OnEvent(Started());

        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);
    }

    /// <summary>VM'in kendi komutu olmadan başlayan bir koşu (üretimde yok) çipin <c>runStarted</c> anındaki modunu alır.</summary>
    [Fact]
    public async Task A_run_started_without_the_view_models_own_command_takes_the_chips_mode_at_that_moment()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine, perfMode: "Light");

        vm.OnEvent(Started(parallelism: 2));
        await vm.CyclePerfAsync(); // Light → Full

        Assert.Equal("/2", vm.StripAllowedText);
        Assert.Equal("Light", vm.StripModeText);
    }

    /// <summary>Pay o an derlenen proje sayısıdır (<see cref="RunViewModel.Counters"/>.Building ile aynı): koşu başlar başlamaz <c>0</c> ve
    /// tooltip KOŞU metnidir (<c>0 of 4 …</c>, boşta metni değil); iki proje derlenirken <c>2</c>; biri bitince <c>1</c>.</summary>
    [Fact]
    public async Task The_numerator_is_the_live_count_of_projects_building_and_the_tooltip_tells_the_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started());

        Assert.Equal("0", vm.StripBuildingText);
        Assert.Equal("0 of 4 projects building in parallel", vm.StripParallelTooltip);

        StartProject(vm, "A");
        StartProject(vm, "B");

        Assert.Equal(2, vm.Counters.Building);
        Assert.Equal(vm.Counters.Building.ToString(), vm.StripBuildingText);
        Assert.Equal("2", vm.StripBuildingText);
        Assert.Equal("2 of 4 projects building in parallel", vm.StripParallelTooltip);

        SucceedProject(vm, "A");

        Assert.Equal("1", vm.StripBuildingText);
        Assert.Equal("1 of 4 projects building in parallel", vm.StripParallelTooltip);
    }

    /// <summary>Motor işçi sayısını makineye göre kıstıysa (<c>runStarted.Parallelism</c> &lt; tablo, gerekçe dolu) payda modun tablo değeri
    /// kalır (<c>/4</c>) — pay zaten kırpılmış sayıyı aşamaz; tooltip nedeni söyler (motorun yazdığı gerekçe aynen). Koşu bitince kırpma
    /// hücreden düşer.</summary>
    [Fact]
    public async Task A_reduced_run_says_why_in_the_tooltip_while_the_denominator_stays_the_modes()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started(parallelism: 2, reducedReason: "3 GB free memory"));
        StartProject(vm, "A");
        StartProject(vm, "B");

        Assert.Equal("2", vm.StripBuildingText);
        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("2 of 4 — reduced: 3 GB free memory", vm.StripParallelTooltip);

        CompleteRun(vm, 2);

        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);
    }

    /// <summary>
    /// Tek proje koşusunda (satırdan Build/Rebuild/Clean — <c>RunTargetId</c> dolu, R3) hücre çipi CANLI izler: koşu ortasında çip Light'a
    /// dönünce payda, mod sözcüğü ve CPU cap'i ANINDA değişir. Kırpma gerekçesi gelse de tooltip "reduced" demez (bir proje derlenirken işçi
    /// sayısı koşuyu tarif etmez — konsol notunun kapısıyla aynı).
    /// </summary>
    [Fact]
    public async Task A_single_project_run_follows_the_chip_live_and_never_says_reduced()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildProjectCommand.ExecuteAsync(IdOf("A"));
        Assert.Equal(IdOf("A"), vm.RunTargetId);
        vm.OnEvent(Started(total: 1, parallelism: 2, reducedReason: "1 logical processor"));
        StartProject(vm, "A");

        Assert.Equal("1", vm.StripBuildingText);
        Assert.Equal("/4", vm.StripAllowedText);
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal("1 of 4 projects building in parallel", vm.StripParallelTooltip);

        await vm.CyclePerfAsync(); // koşu ortasında Balanced → Light

        Assert.Equal("1", vm.StripBuildingText);
        Assert.Equal("/2", vm.StripAllowedText);
        Assert.Equal("Light", vm.StripModeText);
        Assert.Equal("1 of 2 projects building in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(" · cap 40%"), vm.StripCpuTooltip);
    }

    /// <summary>Hedef koşu ORTASINDA değişirse (üretimde yalnız tıklama anında yazılır; hücre yine de girdilerinin saf bir işlevidir) hücre ANINDA
    /// yeni duruma geçer: hedef dolunca çipi izler, boşalınca koşu başındaki moda döner.</summary>
    [Fact]
    public async Task The_cell_follows_a_change_of_the_run_target_at_once()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started());
        await vm.CyclePerfAsync();
        await vm.CyclePerfAsync(); // çip Full; tam koşunun hücresi Balanced'ta
        Assert.Equal("Balanced", vm.StripModeText);

        vm.RunTargetId = IdOf("A");

        Assert.Equal("Full", vm.StripModeText);
        Assert.Equal("/6", vm.StripAllowedText);

        vm.RunTargetId = null;

        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal("/4", vm.StripAllowedText);
    }

    /// <summary>
    /// Koşunun HER bitiş yolu (tamamlandı, durduruldu, motor hatası, motor ölümü) hücreyi çipe geri verir: koşu başındaki mod ve kırpma
    /// notu bırakılır ve pay <c>0</c>'a döner — projeler <c>Started</c> kalmış olsa bile (koşu bitişinde kimse derlemiyor; motor ölünce
    /// satırlar hiç bitirilmez, <see cref="RunViewModel.Counters"/>.Building bu yüzden sıfıra inmez).
    /// </summary>
    [Theory]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("failed")]
    [InlineData("engine-died")]
    public async Task Every_way_a_run_ends_hands_the_cell_back_to_the_chip(string ending)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started(parallelism: 2, reducedReason: "3 GB free memory"));
        StartProject(vm, "A");
        StartProject(vm, "B");
        await vm.CyclePerfAsync();
        await vm.CyclePerfAsync(); // çip Full
        Assert.Equal("Balanced", vm.StripModeText);
        Assert.Equal(2, vm.Counters.Building);

        switch (ending)
        {
            case "completed": CompleteRun(vm, 0); break;
            case "stopped": vm.OnEvent(new RunStoppedEvent("r1", WasHard: false)); break;
            case "failed": vm.OnEvent(new ErrorEvent("runFailed", "boom")); break;
            default: vm.OnEngineExited(1); break;
        }

        Assert.False(vm.IsRunning);
        Assert.Equal("0", vm.StripBuildingText);
        Assert.Equal("/6", vm.StripAllowedText);
        Assert.Equal("Full", vm.StripModeText);
        Assert.Equal("Full — up to 6 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(""), vm.StripCpuTooltip);
    }

    /// <summary>Bir sonraki koşu kendi modunu yakalar: önceki koşunun modu ve kırpma notu yeni koşuya taşınmaz.</summary>
    [Fact]
    public async Task The_next_run_captures_its_own_mode_and_carries_nothing_over()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started(parallelism: 2, reducedReason: "3 GB free memory"));
        CompleteRun(vm, 0);
        vm.SetPerfMode("Light");

        await vm.BuildCommand.ExecuteAsync(null); // yeni koşu Light'ın komutuyla gider
        vm.OnEvent(Started(parallelism: 2));

        Assert.Equal("/2", vm.StripAllowedText);
        Assert.Equal("Light", vm.StripModeText);
        Assert.Equal("0 of 2 projects building in parallel", vm.StripParallelTooltip); // "reduced" eki yok
    }
}
