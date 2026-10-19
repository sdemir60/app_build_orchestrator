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
/// payı, ortadaki sayısı, sınırı, mod sözcüğü ve tooltip'i (motor olayları VM'e oynatılarak — ekran modeli, pencere yok).
///
/// <para>Hücre KOŞUYU anlatır (R2): <c>runStarted</c> → koşu sonu arası, koşu BAŞINDAKİ mod, motorun koşu başında belirlediği paralellik
/// (<c>runStarted.Parallelism</c> — ortadaki sayı ve tooltip'in <c>of N</c>'i) ve modun sınırıyla; çip koşu ortasında dönse hücre kıpırdamaz,
/// koşu bitince çipi izler. Koşu yokken ortadaki sayı yoktur (<c>/—</c>). CPU tooltip'inin cap'i hücrenin moduyla aynıdır (R4). Koşu
/// <see cref="StartRunCommand"/>'la, üretimdeki yoldan başlar (komut gönderimi kabul edilir: <see cref="MainWindowHost.AcceptSends"/>);
/// motorun cevabını (<c>runStarted</c> …) test verir. Ürün adı hiçbir beklentide literal yazılmaz: <see cref="AppIdentity.Product"/>'tan
/// gelir.</para>
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19]</b> ESKİ: hücre <c>pay/payda</c>'ydı, payda modun TABLO değeriydi ve tek proje
/// koşusu (R3, <c>RunTargetId</c> dolu) çipi CANLI izleyip "reduced" demezdi. YENİ: pay ile sınırın arasında koşunun kendi sayısı durur ve
/// tek proje koşusu da tam koşu gibi koşu başındaki cevapla anlatılır — gerekçe ilgili testlerin özetinde.</para>
/// </summary>
public class ResourceStripViewModelTests
{
    private static readonly string[] ProjectNames = ["A", "B", "C"];

    /// <summary>Koşu yokken ortadaki sayının metni: değerin yokluğu.</summary>
    private const string NoRun = "/—";

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

    /// <summary>Paralellik hücresinin dört metni, ekrandaki sırayla: pay · ortadaki sayı · sınır · mod sözcüğü.</summary>
    private static (string Building, string Run, string Limit, string Mode) Cell(RunViewModel vm) =>
        (vm.StripBuildingText, vm.StripRunParallelismText, vm.StripAllowedText, vm.StripModeText);

    // ---------------------------------------------------------------- boşta: hücre çipi izler (anında)

    [Fact]
    public async Task An_idle_view_model_describes_the_chip_in_the_parallel_cell_and_the_chips_cap_in_the_cpu_tooltip()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        Assert.Equal(("0", NoRun, "/4", "Balanced"), Cell(vm));
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

        Assert.Equal(("0", NoRun, "/4", "Balanced"), Cell(vm));
        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);
    }

    /// <summary>Çip her basışta dönerken hücre AYNI ANDA değişir (örnek beklenmez): sınır modun tablo paralelliği, mod sözcüğü modun adı, CPU
    /// tooltip'inin cap'i modun cap'i (Full'de ek yok). Ortadaki sayı boşta yoktur — çip onu yaratmaz.</summary>
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
        Assert.Equal(("0", NoRun, allowed, mode), Cell(vm));
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

        Assert.Equal(("0", NoRun, "/2", "Light"), Cell(vm));
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
    /// Tam koşu koşu başındaki cevapla anlatılır: Balanced'ta başlayan koşuda çip Full'e çevrilse bile hücre <c>0/4/4 Balanced</c> kalır
    /// (işçi sayısı koşu başında sabittir) ve CPU tooltip'i Balanced'ın cap'ini söyler (R4); koşu bitince hücre çipi izler: <c>0/—/6 Full</c>,
    /// cap'siz.
    /// </summary>
    [Fact]
    public async Task A_full_run_keeps_the_mode_it_started_in_while_the_chip_moves_and_the_cell_follows_the_chip_again_when_it_ends()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null); // StartRunCommand(PerfMode = "Balanced") gider
        vm.OnEvent(Started());

        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));

        await vm.CyclePerfAsync(); // Balanced → Light
        await vm.CyclePerfAsync(); // Light → Full

        Assert.Equal("Full", vm.PerfMode);
        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);

        CompleteRun(vm, 3);

        Assert.False(vm.IsRunning);
        Assert.Equal(("0", NoRun, "/6", "Full"), Cell(vm));
        Assert.Equal("Full — up to 6 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(""), vm.StripCpuTooltip);
    }

    /// <summary>Yakalanan mod GÖNDERİLEN moddur: komut gittikten sonra, <c>runStarted</c> gelmeden önce (planlama penceresi, çip canlıdır)
    /// çip dönse de koşu komutla giden modla başlar. Pencere boyunca hücre henüz boşta metnidir, çipi izler ve ortadaki sayı yoktur — motor
    /// sayıyı ancak koşuyu başlatınca belirler.</summary>
    [Fact]
    public async Task The_run_is_described_by_the_mode_its_command_carried_even_if_the_chip_moved_while_it_was_planning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null); // Balanced gitti; runStarted henüz yok

        await vm.CyclePerfAsync(); // planlama penceresinde çip → Light

        Assert.False(vm.IsRunning);
        Assert.Equal(("0", NoRun, "/2", "Light"), Cell(vm)); // koşu başlamadı: hücre çipi izliyor

        vm.OnEvent(Started());

        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));
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

        Assert.Equal(("0", "/2", "/2", "Light"), Cell(vm));
    }

    /// <summary>Pay o an derlenen proje sayısıdır (<see cref="RunViewModel.Counters"/>.Building ile aynı): koşu başlar başlamaz <c>0</c> ve
    /// tooltip KOŞU metnidir (<c>0 of 4 …</c>, boşta metni değil); iki proje derlenirken <c>2</c>; biri bitince <c>1</c>. Ortadaki sayı koşu
    /// boyunca kıpırdamaz.</summary>
    [Fact]
    public async Task The_numerator_is_the_live_count_of_projects_building_and_the_tooltip_tells_the_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started());

        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal("0 of 4 projects building in parallel", vm.StripParallelTooltip);

        StartProject(vm, "A");
        StartProject(vm, "B");

        Assert.Equal(2, vm.Counters.Building);
        Assert.Equal(vm.Counters.Building.ToString(), vm.StripBuildingText);
        Assert.Equal(("2", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal("2 of 4 projects building in parallel", vm.StripParallelTooltip);

        SucceedProject(vm, "A");

        Assert.Equal(("1", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal("1 of 4 projects building in parallel", vm.StripParallelTooltip);
    }

    /// <summary>
    /// Motor işçi sayısını makineye göre kıstıysa (<c>runStarted.Parallelism</c> &lt; tablo, gerekçe dolu) ortadaki sayı ve tooltip'in
    /// <c>of N</c>'i motorun sayısıdır, sınır modun tablo değeri kalır: <c>2/3/4 Balanced</c> — üç sayı üç ayrı şeyi söyler. Tooltip nedeni de
    /// söyler (motorun yazdığı gerekçe aynen). Koşu bitince kırpma hücreden düşer.
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19]</b> ESKİ: hücre <c>2/4</c>'tü, tooltip <c>2 of 4 — reduced: …</c>; kırpılmış sayı
    /// şeritte hiçbir yerde yazmıyordu ve <c>2/4</c> "o an yalnız 2 proje hazırdı"dan ayırt edilemiyordu.</para>
    /// </summary>
    [Fact]
    public async Task A_reduced_run_shows_the_engines_count_in_the_middle_and_in_the_tooltip_while_the_limit_stays_the_modes()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started(parallelism: 3, reducedReason: "3 GB free memory"));
        StartProject(vm, "A");
        StartProject(vm, "B");

        Assert.Equal(("2", "/3", "/4", "Balanced"), Cell(vm));
        Assert.Equal("2 of 3 — reduced: 3 GB free memory", vm.StripParallelTooltip);

        CompleteRun(vm, 2);

        Assert.Equal(("0", NoRun, "/4", "Balanced"), Cell(vm));
        Assert.Equal("Balanced — up to 4 projects build in parallel", vm.StripParallelTooltip);
    }

    /// <summary>
    /// Tek proje koşusu (satırdan Build/Rebuild/Clean — <c>RunTargetId</c> dolu) tam koşu gibi koşu başındaki cevapla anlatılır: koşu ortasında
    /// çip Light'a dönse de hücre <c>1/4/4 Balanced</c> kalır ve CPU tooltip'i Balanced'ın cap'ini söyler.
    /// <para><b>[DEĞİŞEN KURAL — 2026-10-19]</b> ESKİ (R3): tek proje koşusunda hücre çipi CANLI izlerdi (payda, mod, cap). Gerekçe: ortadaki
    /// sayı koşunun kendi sayısıdır ve koşu boyunca değişmez; hücre çipi izleseydi bu koşu Light'a dönüşte <c>1/4/2 Light</c> okunurdu — koşunun
    /// sayısı sınırın üstünde.</para>
    /// </summary>
    [Fact]
    public async Task A_single_project_run_is_described_like_a_full_run_by_the_answer_at_its_start()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildProjectCommand.ExecuteAsync(IdOf("A"));
        Assert.Equal(IdOf("A"), vm.RunTargetId);
        vm.OnEvent(Started(total: 1));
        StartProject(vm, "A");

        Assert.Equal(("1", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal("1 of 4 projects building in parallel", vm.StripParallelTooltip);

        await vm.CyclePerfAsync(); // koşu ortasında Balanced → Light

        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(("1", "/4", "/4", "Balanced"), Cell(vm));
        Assert.Equal("1 of 4 projects building in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(" · cap 70%"), vm.StripCpuTooltip);
    }

    /// <summary>
    /// Kırpılmış tek proje koşusu da nedenini söyler: ortadaki sayı motorun sayısıdır ve tooltip onu açıklar.
    /// <para><b>[DEĞİŞEN KURAL — 2026-10-19]</b> ESKİ (R3): tek proje koşusunda tooltip "reduced" demezdi (konsol notunun kapısıyla aynı) — hücre
    /// koşunun sayısını göstermiyordu. Ortadaki sayı gösterildiğine göre açıklaması da gösterilir; konsol notunun kapısı değişmez
    /// (<c>WorkersReducedNote</c>).</para>
    /// </summary>
    [Fact]
    public async Task A_reduced_single_project_run_says_why_too()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildProjectCommand.ExecuteAsync(IdOf("A"));
        vm.OnEvent(Started(total: 1, parallelism: 2, reducedReason: "1 logical processor"));

        Assert.Equal(("0", "/2", "/4", "Balanced"), Cell(vm));
        Assert.Equal("0 of 2 — reduced: 1 logical processor", vm.StripParallelTooltip);
    }

    /// <summary>
    /// Koşu hedefi hücrenin girdisi DEĞİLDİR: hedef koşu ortasında dolsa ya da boşalsa (üretimde yalnız tıklama anında yazılır) hücre koşu
    /// başındaki cevabı anlatmaya devam eder.
    /// <para><b>[DEĞİŞEN KURAL — 2026-10-19]</b> ESKİ: hedef dolunca hücre çipi izlerdi, boşalınca koşu başındaki moda dönerdi (R3). Tek proje
    /// koşusu artık tam koşu gibi anlatıldığı için hedefin hücreye söyleyeceği bir şey kalmadı.</para>
    /// </summary>
    [Fact]
    public async Task A_change_of_the_run_target_does_not_move_the_cell()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started());
        await vm.CyclePerfAsync();
        await vm.CyclePerfAsync(); // çip Full; koşunun hücresi Balanced'ta
        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));

        vm.RunTargetId = IdOf("A");

        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));

        vm.RunTargetId = null;

        Assert.Equal(("0", "/4", "/4", "Balanced"), Cell(vm));
    }

    /// <summary>
    /// Koşunun HER bitiş yolu (tamamlandı, durduruldu, motor hatası, motor ölümü) hücreyi çipe geri verir: koşu başındaki mod, koşunun sayısı ve
    /// kırpma notu bırakılır, ortadaki sayı <c>/—</c>'ya ve pay <c>0</c>'a döner — projeler <c>Started</c> kalmış olsa bile (koşu bitişinde kimse
    /// derlemiyor; motor ölünce satırlar hiç bitirilmez, <see cref="RunViewModel.Counters"/>.Building bu yüzden sıfıra inmez).
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
        Assert.Equal(("2", "/2", "/4", "Balanced"), Cell(vm));
        Assert.Equal(2, vm.Counters.Building);

        switch (ending)
        {
            case "completed": CompleteRun(vm, 0); break;
            case "stopped": vm.OnEvent(new RunStoppedEvent("r1", WasHard: false)); break;
            case "failed": vm.OnEvent(new ErrorEvent("runFailed", "boom")); break;
            default: vm.OnEngineExited(1); break;
        }

        Assert.False(vm.IsRunning);
        Assert.Equal(("0", NoRun, "/6", "Full"), Cell(vm));
        Assert.Equal("Full — up to 6 projects build in parallel", vm.StripParallelTooltip);
        Assert.Equal(CpuTooltip(""), vm.StripCpuTooltip);
    }

    /// <summary>Bir sonraki koşu kendi modunu ve sayısını yakalar: önceki koşunun modu, sayısı ve kırpma notu yeni koşuya taşınmaz.</summary>
    [Fact]
    public async Task The_next_run_captures_its_own_mode_and_carries_nothing_over()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(Started(parallelism: 3, reducedReason: "3 GB free memory"));
        CompleteRun(vm, 0);
        vm.SetPerfMode("Light");

        await vm.BuildCommand.ExecuteAsync(null); // yeni koşu Light'ın komutuyla gider
        vm.OnEvent(Started(parallelism: 2));

        Assert.Equal(("0", "/2", "/2", "Light"), Cell(vm));
        Assert.Equal("0 of 2 projects building in parallel", vm.StripParallelTooltip); // "reduced" eki yok
    }
}
