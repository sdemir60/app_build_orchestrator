using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using IoPath = System.IO.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E3/T36] REDUCED-MOTION TAM KAPSAM: motion envanterindeki (brief §0) HER sahibi dolaşıp
/// <c>AnimationsEnabled=false</c> iken (a) efektif sürelerin SIFIR olduğunu ve (b) dekoratif sonsuz
/// animasyonların (spinner dönüşü, nefes/nabız, graf dash-flow, imleç blink, belirsiz sweep) DURDUĞUNU /
/// hiç kurulmadığını kanıtlar. Bu test-suite'in amacı EKSİKSİZLİK: bir sahibin reduced-motion kablajı
/// düşerse (ya da yeni bir dekoratif animasyon eklenip bağlanmazsa) burada yakalanır.
///
/// <para><b>Sinyal kaynağı:</b> seam'li sahipler (GraphView/ProjectRow/StickyRibbon/EventStreamView) için
/// AÇIK bir kapalı <see cref="IMotionSettings"/> (<see cref="Off"/>) + <c>AnimationsEnabledProvider=()=&gt;false</c>
/// enjekte edilir (AnimationsEnabled=FALSE yolu kanıtlanır). Seam'siz sahipler (BuildingSpinner/StatusGlyph/
/// ConsoleView/PopIn/HoverBackground/LatestPill) doğrudan <c>App.Motion</c> okur — headless'ta o null'dur
/// (AnimationsEnabled=false), yani realize etmek zaten reduced-motion durumudur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ReducedMotionCoverageTests
{
    /// <summary>Kapalı reduced-motion sinyali — gerçek <see cref="MotionSettings"/>, kapalı bir sinyalle sarılı
    /// (sahte bir IMotionSettings gerekmez; Effective/AnimationsEnabled gerçek koddan gelir).</summary>
    private static IMotionSettings Off() => new MotionSettings(new FakeMotionSignal { AnimationsEnabled = false });

    // ================================================================ 0) TEK SÜRE KAPISI
    // Tüm kod-tarafı animasyonlar süreyi bu kapıdan (Effective) TAZE okur — kapalıyken her token 0'a çöker.

    [Fact]
    public void The_single_duration_gate_collapses_every_token_to_zero_when_animations_are_off()
    {
        var m = Off();
        Assert.False(m.AnimationsEnabled);
        foreach (var ms in new[] { 80.0, 120.0, 180.0, 280.0, 900.0 }) // Instant/Fast/Base/Slow + spinner
            Assert.Equal(TimeSpan.Zero, m.Effective(TimeSpan.FromMilliseconds(ms)));
    }

    // ================================================================ 1) BuildingSpinner (900ms/270° dönüş)

    [StaFact]
    public void BuildingSpinner_never_starts_its_rotation_clock_under_reduced_motion()
    {
        Assert.Null(BuildOrchestrator.App.App.Motion); // seam'siz sahip: headless null (reduced) — sızıntı vacuous PASS'a dönüşmesin
        var host = DsResources.NewHost();
        var spinner = new BuildingSpinner();
        var window = DsResources.Realize(host, spinner); // headless App.Motion null → reduced

        Assert.False(spinner.IsRotating); // dönüş saati HİÇ kurulmaz
        GC.KeepAlive(window);
    }

    // ================================================================ 2) StatusGlyph (1.6s nabız)

    [StaFact]
    public void StatusGlyph_never_starts_its_building_pulse_under_reduced_motion()
    {
        Assert.Null(BuildOrchestrator.App.App.Motion); // seam'siz sahip: headless null (reduced) — sızıntı vacuous PASS'a dönüşmesin
        var host = DsResources.NewHost();
        var glyph = new StatusGlyph { Status = GraphStatus.Building };
        var window = DsResources.Realize(host, glyph);

        // GERÇEK saat (BuildingSpinner.IsRotating kardeşi): nabız Opacity clock'u glyph'te HİÇ kurulmaz. İç
        // _isPulsing bayrağı yalan söyleyebilir (bookkeeping) — asıl kanıt HasAnimatedProperties'tir.
        Assert.False(glyph.HasAnimatedProperties); // nabız Opacity saati YOK
        Assert.False(glyph.IsPulsing);             // ikincil: iç bayrak da kapalı
        GC.KeepAlive(window);
    }

    // ================================================================ 3) GraphView (building animasyonu + açılış dalgası)

    /// <summary>[quiet] <b>Eski iddia:</b> bu test ayrıca <c>view.SharedDashClock</c>'un null olduğunu
    /// pinliyordu (kalıcı kenar ağının akan dash saati). v1.3.0 §2.3 kalıcı ağı kaldırdı — kenarlar yalnız
    /// seçimde çizilir — dolayısıyla o saatin sahibi de yok oldu.</summary>
    [StaFact]
    public void GraphView_keeps_no_building_animation_and_an_instant_reveal_under_reduced_motion()
    {
        var view = NewGraphView();
        view.SetGraph(
            [new("OSYS.Base", 0, GraphStatus.Building),
             new("OSYS.Data", 1, GraphStatus.Discovered)],
            [new("OSYS.Base", "OSYS.Data")]);

        Assert.Null(view.NodeVisuals["OSYS.Base"].Beads);                            // beads yörüngesi HİÇ kurulmaz
        Assert.Null(view.BeadsClock);                                                // paylaşımlı saat doğmaz
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Cell.Opacity)); // reveal ANİ (dalga yok)
    }

    // ================================================================ 4) ProjectRow (3.8s nefes + mount reveal)

    [StaFact]
    public void ProjectRow_stops_breathing_and_reveals_instantly_under_reduced_motion()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started); // building
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, MotionSettings = Off(), DataContext = vm };
        var window = DsResources.Realize(host, row);

        Assert.False(row.BreathLayer.HasAnimatedProperties); // nefes saati yok

        row.PlayReveal(9);
        Assert.Equal(1.0, row.Root.Opacity);                 // reveal ANİ (opacity 1)
        Assert.False(row.Root.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    // ============================================ 4b) StickyLayerList reveal wiring (E3 fold — liste-frontier reveal CANLI)

    [StaFact]
    public void StickyLayerList_reveal_wiring_holds_no_hero_and_snaps_rows_under_reduced_motion()
    {
        // [E4/T48] Liste reveal-hero wiring'i reduced-motion'da: HERO TUTULMAZ (graf reveal deseni — GraphView'ın
        // reduced yolu da hero almaz), release timer kurulmaz, satırlar ANİ (opacity 1). ProjectRow.PlayReveal'ın
        // reduced yolu §4'te ayrıca; bu, StickyLayerList SUBSYSTEM'inin (E3'ün buraya ertelediği) wiring'ini kanıtlar.
        var coordinator = new MotionCoordinator();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false, HeroCoordinator = coordinator };
        list.SetGroups([new StickyLayerList.LayerGroup("",
        [
            new ProjectRowViewModel("a", "A", ProjectRowState.Pending),
            new ProjectRowViewModel("b", "B", ProjectRowState.Pending),
        ])]);
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);
        DispatcherPump.PumpUntil(() => list.RevealRows.Count == 2, TimeSpan.FromSeconds(2));

        // [E4 fix — FIX 3, İZOLASYON] İlk satıra KENDİ sinyali `() => true` ver: bu satır kendi başına animasyona
        // GİRERDİ (opacity 0'da başlar). Opacity 1 assertion'ı ancak LİSTE kararının (animate=false) satırın sinyalini
        // EZDİĞİNİ kanıtlarsa anlamlıdır — aksi halde her satırın kendi headless-reduced varsayılanı (App.Motion null)
        // opacity'yi zaten 1'e çeker ve assertion bir wiring regresyonundan ASLA kırılamaz (non-isolating). Wiring
        // düşer de PlayRevealStagger satır kararı yerine satırın kendi sinyalini okursa, bu satır opacity 0'da kalır → RED.
        var rows = list.RevealRows;
        rows[0].AnimationsEnabledProvider = () => true;

        list.PlayRevealStagger();

        Assert.False(coordinator.IsHeroActive);                             // reduced → hero TUTULMAZ
        Assert.False(list.HasPendingRevealRelease);                         // release timer YOK
        Assert.All(rows, r => Assert.Equal(1.0, r.Root.Opacity));          // LİSTE kararı satır sinyalini EZER → hepsi ANİ (opacity 1)
        GC.KeepAlive(window);
    }

    // ================================================================ 5) StickyRibbon (1.4s belirsiz sweep)

    [StaFact]
    public void StickyRibbon_keeps_indeterminate_mode_but_never_sweeps_under_reduced_motion()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { AnimationsEnabledProvider = () => false, MotionSettings = Off(), DataContext = vm };
        var window = DsResources.Realize(host, ribbon);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // → Syncing (belirsiz mod)
        Assert.True(ribbon.IsIndeterminate);                  // MOD hâlâ belirsiz (statik %35 bar)
        Assert.False(ribbon.IndicatorTranslate.HasAnimatedProperties); // ama sweep saati YOK
        GC.KeepAlive(window);
    }

    // ================================================================ 6) EventStreamView (aktif satır daktilosu + imleç)

    [StaFact]
    public void EventStreamView_types_the_active_line_instantly_under_reduced_motion()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(host, view);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B")); // aktif satır "B building…"

        Assert.True(view.ActiveLineInstant); // daktilo INSTANT (harf-harf YOK)
        // [fix] ActiveLineInstant yalnız daktilo zamanlayıcısını gözler (scheduler yoksa vacuously true). İmlecin
        // blink saatinin GERÇEKTEN kurulmadığını ayrı kanıtla (ConsoleView.ActiveCursorGlyph deseni).
        Assert.False(view.ActiveCursorGlyph.HasAnimatedProperties); // imleç blink saati YOK
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [W2 fix-1] EventStreamView artık motion sinyalini CANLI izler. Fold'dan önce bu görünüm diğer sahiplerden
    /// asimetrikti: yalnız provider'ı vardı, <c>AnimationsEnabledChanged</c> aboneliği YOKtu — OS ayarı koşu
    /// SIRASINDA kapatılsa imlecin sonsuz blink saati dönmeye devam ederdi. Test önce blink'in GERÇEKTEN döndüğünü
    /// pinler (aksi halde ikinci assertion vacuous PASS olurdu), sonra sinyali kapatıp saatin söküldüğünü doğrular.
    /// </summary>
    [StaFact]
    public void EventStreamView_stops_the_cursor_blink_when_the_motion_signal_turns_off_while_running()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = true };
        var motion = new MotionSettings(signal);
        var vm = NewVm();
        var host = DsResources.NewHost();
        var view = new EventStreamView
        {
            AnimationsEnabledProvider = () => motion.AnimationsEnabled, MotionSettings = motion, DataContext = vm,
        };
        var window = DsResources.Realize(host, view);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A")); // aktif satır → imleç blink'i başlar
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties);        // non-vacuous: saat GERÇEKTEN dönüyor

        signal.AnimationsEnabled = false;
        signal.Raise();                                                  // OS ayarı koşu SIRASINDA kapandı

        Assert.False(view.ActiveCursorGlyph.HasAnimatedProperties);      // blink saati SÖKÜLDÜ
        Assert.Equal(1.0, view.ActiveCursorGlyph.Opacity);               // imleç steady
        GC.KeepAlive(window);
    }

    // ================================================================ 7) ConsoleView (idle "ready" imleç blink)

    [StaFact]
    public void ConsoleView_ready_cursor_does_not_blink_under_reduced_motion()
    {
        // [A13/T1 fix-1 · S6] ConsoleView ARTIK seam'lidir (yorumun "seam'siz sahip" hâli bayattı). Burada seam
        // KASTEN enjekte EDİLMEZ ki üretim VARSAYILANI (statik App.Motion) sınansın — headless'ta null = reduced.
        // Pozitif ikizi: ConsoleMotionPathTests.The_idle_ready_cursor_really_blinks_when_motion_is_on.
        Assert.Null(BuildOrchestrator.App.App.Motion);
        var view = new ConsoleView();

        view.ShowReady(); // headless App.Motion null → StopBlink

        Assert.False(view.ActiveCursorGlyph.HasAnimatedProperties); // imleç blink saati yok
    }

    // ================================================================ 8) Typewriter / cascade tempo (ConsoleView + EventStreamView ORTAK gate)

    [Fact]
    public void The_typewriter_and_cascade_schedulers_are_instant_with_zero_duration_when_off()
    {
        // ConsoleView aktif-satır daktilosu + EventStreamView daktilosu bu SAF zamanlayıcıyla gate'lenir.
        var typewriter = new TypewriterScheduler(textLength: 120, animationsEnabled: false);
        Assert.True(typewriter.Instant);
        Assert.Equal(TimeSpan.Zero, typewriter.Duration);

        // ConsoleView proje-log kaskatı bu SAF zamanlayıcıyla gate'lenir.
        var cascade = new CascadeScheduler(totalLines: 200, animationsEnabled: false);
        Assert.True(cascade.Instant);
        Assert.Equal(TimeSpan.Zero, cascade.Duration);
        Assert.Equal(200, cascade.RevealedAt(TimeSpan.Zero)); // her satır t=0'da tam açık
    }

    // ================================================================ 9) Paylaşılan geçiş kapıları (HoverBackground/LatestPill/DS chip + PopIn + scroll)

    [StaFact]
    public void The_shared_colour_transition_snaps_with_no_clock_when_off()
    {
        // MotionTokens.TransitionColor — HoverBackground, LatestPill ve TÜM DS 120ms renk geçişlerinin TEK yolu.
        Assert.Null(BuildOrchestrator.App.App.Motion); // seam'siz kapı: headless null (reduced) — sızıntı vacuous PASS'a dönüşmesin
        var host = DsResources.NewHost();
        var brush = new SolidColorBrush(Colors.Black);
        MotionTokens.TransitionColor(host, brush, Colors.Red); // headless App.Motion null → snap

        Assert.False(brush.HasAnimatedProperties);
        Assert.Equal(Colors.Red, brush.Color);
        GC.KeepAlive(host);
    }

    [StaFact]
    public void PopIn_snaps_the_element_to_its_final_state_with_no_clock_when_off()
    {
        Assert.Null(BuildOrchestrator.App.App.Motion); // seam'siz kapı: headless null (reduced) — sızıntı vacuous PASS'a dönüşmesin
        var el = new Border();

        PopIn.Play(el); // headless App.Motion null → snap

        Assert.Equal(1.0, el.Opacity);
        Assert.False(el.HasAnimatedProperties);
    }

    [StaFact]
    public void The_scroll_animator_snaps_instead_of_animating_when_off()
    {
        // ScrollAnimator — frontier-follow / alta-yapışma / seçim-scroll'unun ortak motoru; çağıran kapalı sinyali
        // TAZE geçirir → animasyon BAŞLATILMAZ (false döner), offset anında oturur.
        var scroll = new ScrollViewer();
        bool started = ScrollAnimator.AnimateTo(
            scroll, currentOffset: 0, targetOffset: 500,
            animationsEnabled: false, effectiveDuration: TimeSpan.Zero, keySpline: new System.Windows.Media.Animation.KeySpline(0.65, 0, 0.35, 1));

        Assert.False(started); // anlık atlandı — hiç clock kurulmadı
    }

    // ---------------------------------------------------------------- helpers

    // [A13/T1 fix-1 · S1] Sözlük merge'i artık GraphTestView'da (TEK yer) — altı kopyanın biriydi.
    private static GraphView NewGraphView()
        => GraphTestView.Sized(new Size(600, 400), () => false, Off());

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
}
