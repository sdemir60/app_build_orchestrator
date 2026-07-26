using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
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
/// [T44] "Success flourish" SÖZLEŞMESİNİN KİLİDİ. Başarı anındaki kutlama TEK bir şeydir: event stream'in
/// <c>done</c> satırı BİR KEZ parlar (<see cref="EventStreamRow"/>, <c>Brush.StatusSuccessSoft</c> → şeffaf,
/// 1.1s). Liste (<see cref="ProjectRow"/>) ve graf (<see cref="GraphView"/>) başarıda HİÇBİR dalga/ripple
/// oynatmaz. Efektin KENDİSİ It-4b/D3'te teslim edildi; buradaki testler onu DRIFT'e karşı korur:
///
/// <list type="number">
/// <item><b>tek satır</b> — bütün koşuda parlayan satır sayısı 1'dir ve o satır <c>done</c> satırıdır;</item>
/// <item><b>bir kez</b> — satır listesi yeniden kurulsa/container recycle olsa bile tekrar oynamaz
/// (guard VM'dedir: <see cref="StreamEventViewModel.GlowPlayed"/>);</item>
/// <item><b>liste/graf dalgası yok</b> — başarılı satır/düğüm hiçbir saat tutmaz; ayrıca kaynak taraması
/// (<see cref="SourceGuard"/>) success tint'in animasyon hedefi olmasını ve kutlama sözlüğünü
/// (<c>Glow/Ripple/Confetti/…</c>) event stream sahibinin DIŞINDA yasaklar.</item>
/// </list>
///
/// <para>Hatalı koşuda parıltı YOKtur ve reduced-motion'da saat HİÇ kurulmaz (yalnız "oynandı" işaretlenir —
/// sinyal sonradan açılsa bile geriye dönük oynatma olmaz).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SuccessFlourishTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    // ================================================================ 1) TEK SATIR · BİR KEZ (stream)

    [StaFact]
    public void A_clean_run_flourishes_exactly_one_row_and_that_row_is_the_done_row()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm, animations: true);

        DriveCleanRun(vm);

        var rows = view.Rows;
        DispatcherPump.PumpUntil(() => rows.Sum(r => r.GlowPlayCount) >= 1, PumpTimeout);

        Assert.True(rows.Count >= 3);                                       // non-vacuous: ok satırları + done satırı var
        Assert.Equal(1, rows.Sum(r => r.GlowPlayCount));                    // TEK parıltı — koşunun tamamında
        Assert.Equal(1, rows.Count(r => r.ViewModel!.GlowEligible));        // uygunluk da TEK satırda (VM kuralı)
        var glowing = rows.Single(r => r.GlowPlayCount == 1);
        Assert.Same(rows[^1], glowing);                                     // ve o satır SON satır…
        Assert.Equal(StreamKind.Done, glowing.ViewModel!.Kind);             // …yani "Completed …" done satırı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Rebuilding_the_stream_rows_never_replays_the_flourish()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm, animations: true);

        DriveCleanRun(vm);
        DispatcherPump.PumpUntil(() => view.Rows.Sum(r => r.GlowPlayCount) >= 1, PumpTimeout);
        var doneVm = view.Rows[^1].ViewModel!;
        Assert.True(doneVm.GlowPlayed);

        // (a) container recycle: AYNI satır nesnesine aynı VM yeniden bağlanır.
        view.Rows[^1].SimulateContainerRecycle();
        Assert.Equal(1, view.Rows.Sum(r => r.GlowPlayCount));

        // (b) TAM yeniden kurulum: DataContext reset → RebuildRows TAZE EventStreamRow'lar üretir (sayaçları 0).
        // Guard view-lokal olsaydı bu taze satırlar parıltıyı YENİDEN oynatırdı; VM'deki GlowPlayed engeller.
        view.DataContext = null;
        view.DataContext = vm;
        var fresh = view.Rows;
        Assert.NotEmpty(fresh);
        DispatcherPump.PumpUntil(() => fresh.All(r => r.IsLoaded), PumpTimeout);

        Assert.All(fresh, r => Assert.Equal(0, r.GlowPlayCount));           // hiçbir taze satır parlamadı
        Assert.Same(doneVm, fresh[^1].ViewModel);                           // aynı done VM'i — guard hâlâ onda
        Assert.True(doneVm.GlowPlayed);
        GC.KeepAlive(window);
    }

    /// <summary>[fix round 1 · Important 4] "BİR KEZ" iddiasının ŞEKLİ. Sayaç (<c>GlowPlayCount</c>) yalnız kaç kez
    /// BAŞLATILDIĞINI ölçer; biri animasyona <c>RepeatBehavior.Forever</c> ya da <c>FillBehavior.HoldEnd</c> eklese
    /// sayaç 1 kalır ve diğer testlerin hepsi yeşil kalırdı — oysa parıltı ya sonsuza dek yanıp sönerdi ya da
    /// yeşil zemini üzerinde TUTARDI. Burada davranış pinlenir: parıltı KENDİ KENDİNE biter, saat bırakılır ve
    /// zemin şeffafa döner. (Şeklin kaynak-tarafı ikinci kilidi:
    /// <see cref="The_flourish_animation_declares_no_repeat_and_stops_filling"/>.)</summary>
    [StaFact]
    public void The_flourish_ends_by_itself_and_leaves_the_row_background_transparent()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm, animations: true);

        DriveCleanRun(vm);
        DispatcherPump.PumpUntil(() => view.Rows.Sum(r => r.GlowPlayCount) >= 1, PumpTimeout);
        var done = view.Rows[^1];
        var brush = (SolidColorBrush)done.Background;

        Assert.Equal(1, done.GlowPlayCount);
        Assert.True(brush.HasAnimatedProperties);                           // non-vacuous: saat GERÇEKTEN dönüyor
        Assert.NotEqual(Colors.Transparent, brush.Color);                   // ve zemin GERÇEKTEN yeşil (parıltı görünür)

        // Parıltı KENDİ KENDİNE bitmeli: 1.1s sonunda efektif renk tabana (şeffaf) döner. Koşul-tabanlı bekleme
        // (D8): sabit uyku yok — renk şeffafa dönünce çıkılır, timeout yalnız güvenlik ağıdır. Drift senaryosu:
        // son keyframe'in ya da FillBehavior'ın değişip zemini YEŞİLDE tutması → pump timeout'a düşer, assert RED.
        // (Sonsuz tekrar — RepeatBehavior — bu ölçümle yakalanamaz; onun kilidi kaynak tarafındadır:
        // The_flourish_animation_declares_no_repeat_and_stops_filling.)
        DispatcherPump.PumpUntil(() => brush.Color == Colors.Transparent, TimeSpan.FromSeconds(5));

        Assert.Equal(Colors.Transparent, brush.Color);                      // taban renge dönüldü (yeşilde asılı kalmaz)
        Assert.Equal(1, done.GlowPlayCount);                                // ve yeniden başlatılmadı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_failed_run_never_flourishes_any_row()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm, animations: true);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 1200, "error CS0103"));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 1, 0, 0, 100)); // Failed=1 → done KIRMIZI

        var rows = view.Rows;
        DispatcherPump.PumpUntil(() => rows.All(r => r.IsLoaded), PumpTimeout);

        // [fix round 1 · Important 3] POZİTİF KONTROL: parıltı TETİKLEYİCİSİ Loaded'dır (EventStreamRow.OnLoaded →
        // ApplyGlow). Satırlar hiç yüklenmeseydi aşağıdaki "0 parıltı" assertion'ı doğru ama ANLAMSIZ olurdu
        // (PumpUntil sessizce timeout'a düşer). Önce tetikleyicinin GERÇEKTEN ateşlendiğini kanıtla.
        Assert.All(rows, r => Assert.True(r.IsLoaded, "satır yüklenmedi — 'parıltı yok' sonucu vacuous olurdu"));

        Assert.Equal(StreamKind.Done, rows[^1].ViewModel!.Kind);            // non-vacuous: done satırı GERÇEKTEN var
        Assert.False(rows[^1].ViewModel!.GlowEligible);                     // ama hatalı koşuda uygun DEĞİL
        Assert.Equal(0, rows.Sum(r => r.GlowPlayCount));                    // hiçbir satır parlamaz
        Assert.False(rows[^1].ViewModel!.GlowPlayed);                       // uygun olmadığı için latch'e bile girmedi
        GC.KeepAlive(window);
    }

    // ================================================================ 2) REDUCED MOTION

    [StaFact]
    public void Reduced_motion_marks_the_flourish_played_without_ever_starting_a_clock()
    {
        // Non-vacuity: AYNI senaryo animasyon AÇIKken 1 parıltı üretiyor (bkz. yukarıdaki ilk test) — buradaki
        // 0, sinyalin KAPALI olmasından gelir, senaryonun eksikliğinden değil.
        bool animate = false;
        var vm = NewVm();
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => animate, DataContext = vm };
        var window = DsResources.Realize(host, view);

        DriveCleanRun(vm);
        var done = view.Rows[^1];
        DispatcherPump.PumpUntil(() => done.IsLoaded, PumpTimeout);

        // [fix round 1 · Important 3] POZİTİF KONTROL: tetikleyici (Loaded → ApplyGlow) gerçekten ateşlendi.
        // Aksi halde aşağıdaki "saat kurulmadı" sonucu, reduced-motion'dan değil satırın hiç yüklenmemesinden
        // gelirdi. GlowPlayed'in true olması da ApplyGlow'un KOŞTUĞUNUN ikinci kanıtıdır (reduced yol onu set eder).
        Assert.True(done.IsLoaded, "satır yüklenmedi — 'saat kurulmadı' sonucu vacuous olurdu");

        Assert.True(done.ViewModel!.GlowEligible);                          // satır UYGUN…
        Assert.Equal(0, done.GlowPlayCount);                                // …ama saat HİÇ kurulmadı
        Assert.False(((SolidColorBrush)done.Background).HasAnimatedProperties);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)done.Background).Color); // zemin şeffaf kaldı
        Assert.True(done.ViewModel!.GlowPlayed);                            // tek-yönlü latch işaretlendi

        // Sinyal SONRADAN açılırsa geriye dönük oynatma YOK (MotionSettings sözleşmesi: bir kereye mahsus
        // efektler yeniden oynatılmaz) — recycle yolu ApplyGlow'u yeniden çağırır, guard yine tutar.
        animate = true;
        done.SimulateContainerRecycle();
        Assert.Equal(0, done.GlowPlayCount);
        GC.KeepAlive(window);
    }

    // ================================================================ 3) LİSTE VE GRAFTA DALGA YOK (davranış)

    [StaFact]
    public void A_succeeded_list_row_holds_no_clock_while_a_building_row_breathes()
    {
        var host = DsResources.NewHost();
        var building = new ProjectRow
        {
            AnimationsEnabledProvider = () => true,
            DataContext = new ProjectRowViewModel("a", "A", ProjectRowState.Started),
        };
        var succeeded = new ProjectRow
        {
            AnimationsEnabledProvider = () => true,
            DataContext = new ProjectRowViewModel("b", "B", ProjectRowState.Succeeded),
        };
        var panel = new System.Windows.Controls.StackPanel { Children = { building, succeeded } };
        var window = DsResources.Realize(host, panel);

        // [fix round 1 · Important 2] Zemin brush'ı BU testte ölçülmez: MotionTokens.TransitionColor sinyali
        // STATİK kapıdan (MotionGate.StaticAnimationsEnabled → App.Motion) okur ve headless'ta App.Motion null'dur,
        // yani "zemin animasyonlu değil" assertion'ı satırın kendi provider'ından BAĞIMSIZ olarak YAPISAL biçimde
        // hep doğrudur — bir drift'ten ASLA kırılamazdı. Zemin yüzeyi bunun yerine kaynak guard'ıyla korunur
        // (The_success_tint_is_animated_only_by_the_event_stream_row: TransitionColor de anahtar kelimedir).
        Assert.True(building.BreathLayer.HasAnimatedProperties);            // non-vacuous: building satırı nefes ALIR
        Assert.False(succeeded.BreathLayer.HasAnimatedProperties);          // başarı satırında kutlama saati YOK
        Assert.False(succeeded.Root.HasAnimatedProperties);                 // satırın KENDİ provider'ı açık — ölçüm gerçek
        Assert.False(succeeded.Glyph.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Graph_nodes_release_their_clocks_when_they_succeed_instead_of_celebrating()
    {
        var view = NewGraphView();
        view.SetGraph(
            [new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, GraphStatus.Queued)],
            [new("OSYS.Base", "OSYS.Data")]);

        Assert.True(view.NodeVisuals["OSYS.Base"].IsPulsing);               // non-vacuous: building düğüm nabzı DÖNER

        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded), new("OSYS.Data", 1, GraphStatus.Succeeded)]);

        foreach (var visual in view.NodeVisuals.Values)
        {
            Assert.False(visual.IsPulsing);                                 // nabız bırakıldı…
            Assert.False(visual.PulseHost.HasAnimatedProperties);           // …ve yerine KUTLAMA saati geçmedi
            Assert.False(visual.Square.HasAnimatedProperties);
            Assert.Equal(1.0, visual.PulseHost.Opacity);
        }
    }

    // ================================================================ 4) KAYNAK GUARD'LARI (drift kapısı)

    /// <summary>[fix round 1 · Important 1] Bir success tint'ini (<c>Brush.StatusSuccess*</c>) animasyona
    /// bağlamak yalnız event stream satırının hakkıdır. Anahtar kelime listesi WPF API'siyle SINIRLI DEĞİLDİR:
    /// bu kod tabanında animasyonların ÇOĞU projenin kendi kapılarından geçer (<c>MotionTokens.TransitionColor</c>,
    /// <c>TransitionDouble</c>, <c>SplineTo</c>, <c>AnimateSlowEaseInOut</c>, <c>CreateBlinkAnimation</c>,
    /// <c>PopIn.Play</c>, <c>PlayReveal*</c>) — yalnız <c>BeginAnimation|Storyboard</c> aransaydı
    /// <c>MotionTokens.TransitionColor(this, _bgBrush, successColor)</c> ile eklenmiş bir dalga GÖRÜNMEZ olurdu.
    /// Pencere ±6 satırdır: uzaktaki meşru kullanım (GraphView/ProjectRow statü tabloları, Tokens.xaml tanımları)
    /// tetiklemez, animasyonun dibindeki kullanım tetikler.</summary>
    private const string AnimationEntryPoints =
        "BeginAnimation|Storyboard|KeyFrame|ColorAnimation|DoubleAnimation|Timeline\\.|AnimationClock"
        + "|MotionTokens\\.|TransitionColor|TransitionDouble|SplineTo|AnimateSlowEaseInOut|CreateBlinkAnimation"
        + "|PopIn\\.Play|PlayReveal|ScrollAnimator\\.";

    private const string SuccessTint = "Brush\\.StatusSuccess\\w*";

    private static readonly Regex SuccessTintAnimated = new(
        SuccessTint + "(?:[^\n]*\n){0,6}[^\n]*(?:" + AnimationEntryPoints + ")"
        + "|(?:" + AnimationEntryPoints + ")(?:[^\n]*\n){0,6}[^\n]*" + SuccessTint,
        RegexOptions.Compiled);

    /// <summary>Kutlama sözlüğü — bir "glow/ripple/confetti" başka bir sahipte belirirse ikinci bir kutlama
    /// efekti doğmuş demektir.</summary>
    private static readonly Regex CelebrationVocabulary =
        new("Glow|Ripple|Confetti|Celebrat|Flourish|Sparkle|Firework", RegexOptions.Compiled);

    private static readonly string[] FlourishOwners =
    [
        IoPath.Combine("Views", "EventStreamView.xaml.cs"),   // efektin KENDİSİ (EventStreamRow.ApplyGlow)
        IoPath.Combine("ViewModels", "StreamEventViewModel.cs"), // uygunluk + tek-yönlü guard
    ];

    [Fact]
    public void The_success_tint_is_animated_only_by_the_event_stream_row()
    {
        var offenders = SourceGuard.ScanApp("*.cs", SuccessTintAnimated, FlourishOwners)
            .Concat(SourceGuard.ScanApp("*.xaml", SuccessTintAnimated, FlourishOwners)).ToList();
        Assert.Empty(offenders);
    }

    /// <summary>[fix round 1 · Important 4] Şeklin kaynak-tarafı kilidi: parıltının sahibi dosyada tekrar eden bir
    /// animasyon BEYANI olamaz (<c>RepeatBehavior</c>) ve parıltı dolgusunu bırakmak zorundadır
    /// (<c>FillBehavior.Stop</c>). Çalışma-zamanı eşi:
    /// <see cref="The_flourish_ends_by_itself_and_leaves_the_row_background_transparent"/>.</summary>
    [Fact]
    public void The_flourish_animation_declares_no_repeat_and_stops_filling()
    {
        string owner = File.ReadAllText(IoPath.Combine(RepoPaths.AppSrcRoot, FlourishOwners[0]));

        Assert.Contains("FillBehavior.Stop", owner, StringComparison.Ordinal);      // dolgu bırakılır → yeşilde asılı kalmaz
        Assert.Single(Regex.Matches(owner, "ColorAnimationUsingKeyFrames"));        // TEK keyframe seti — ikinci bir parıltı yok

        // Sonsuz/tekrarlı parıltı YASAK. Yorum satırları elenir (dosyanın §5 notu imlecin
        // RepeatBehavior.Forever blink'inden BAHSEDER; beyan MotionTokens.CreateBlinkAnimation'dadır, burada değil).
        var repeats = SourceGuard.ScanText(
            FlourishOwners[0], owner, new Regex("RepeatBehavior", RegexOptions.Compiled), skipCommentLines: true);
        Assert.Empty(repeats);
    }

    [Fact]
    public void Celebration_vocabulary_lives_only_in_the_event_stream_owner()
    {
        var offenders = SourceGuard.ScanApp("*.cs", CelebrationVocabulary, FlourishOwners)
            .Concat(SourceGuard.ScanApp("*.xaml", CelebrationVocabulary, FlourishOwners)).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_flourish_guards_go_red_on_a_drifted_owner_but_not_on_a_distant_legitimate_use()
    {
        // (a) Listeye kopyalanmış bir parıltı bloğu → YAKALANIR.
        string driftedRow = string.Join('\n',
        [
            "private void ApplySuccessWave()",
            "{",
            "    Color from = ResolveColor(\"Brush.StatusSuccessSoft\");",
            "    var anim = new ColorAnimationUsingKeyFrames();",
            "    _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);",
            "}",
        ]);
        Assert.NotEmpty(SourceGuard.ScanText(IoPath.Combine("Views", "ProjectRow.xaml.cs"), driftedRow, SuccessTintAnimated));

        // (a2) [fix round 1 · Important 1] Projenin KENDİ animasyon kapısıyla eklenmiş bir yeşil dalga — tek satır,
        // WPF API'si hiç geçmiyor. Eski anahtar kelime listesi (BeginAnimation|Storyboard|…) bunu KAÇIRIRDI.
        Assert.NotEmpty(SourceGuard.ScanText(
            IoPath.Combine("Controls", "StickyLayerList.cs"),
            "MotionTokens.TransitionColor(this, _bgBrush, ResolveColor(\"Brush.StatusSuccess\"));",
            SuccessTintAnimated));

        // (b) Grafa eklenmiş bir "ripple" → sözlük guard'ı YAKALAR.
        Assert.NotEmpty(SourceGuard.ScanText(
            IoPath.Combine("Graph", "GraphView.xaml.cs"), "private void PlaySuccessRipple() { }", CelebrationVocabulary));

        // (c) Aynı dosyada UZAKTAKİ meşru kullanım (statü tablosu ile nabız arasında 8 satır) → temiz kalır;
        // guard "aynı dosyada geçiyor" demiyor, "animasyonun DİBİNDE" diyor.
        string legitimate = string.Join('\n',
            ["GraphStatus.Succeeded => (\"Brush.StatusSuccess\", \"Brush.StatusSuccessSoft\", \"…\", false),", .. Enumerable.Repeat("// ara satır", 8), "visual.PulseHost.BeginAnimation(OpacityProperty, pulse);"]);
        Assert.Empty(SourceGuard.ScanText(IoPath.Combine("Graph", "GraphView.xaml.cs"), legitimate, SuccessTintAnimated));
    }

    [Fact]
    public void The_flourish_guards_actually_scan_the_owners_they_exempt()
    {
        // Tarama boş dönerse (yol/filtre bozulması) iki guard da sessizce yeşil kalırdı.
        var scanned = SourceGuard.ScannedAppFiles("*.cs");
        Assert.All(FlourishOwners, owner => Assert.Contains(owner, scanned));
        Assert.Contains(IoPath.Combine("Views", "ProjectRow.xaml.cs"), scanned);
        Assert.Contains(IoPath.Combine("Graph", "GraphView.xaml.cs"), scanned);
        Assert.Contains(IoPath.Combine("Resources", "Tokens.xaml"), SourceGuard.ScannedAppFiles("*.xaml"));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Hatasız bir koşu: iki proje derlenir ve <c>Completed …</c> ile biter (done satırı UYGUN).</summary>
    private static void DriveCleanRun(RunViewModel vm)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 0, 0, 2100));
    }

    private static (EventStreamView view, Window window) Realize(RunViewModel vm, bool animations)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => animations, DataContext = vm };
        return (view, DsResources.Realize(host, view));
    }

    /// <summary>Animasyonu AÇIK bir GraphView (ReducedMotionCoverageTests.NewGraphView'ın açık-sinyal eşi):
    /// pack:// headless'ta çözülmez, sözlükler TestAssets'ten yüklenir.</summary>
    private static GraphView NewGraphView()
    {
        var view = new GraphView { AnimationsEnabledProvider = () => true };
        foreach (string name in new[] { "Tokens.xaml", "Motion.xaml", "Icons.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        view.Measure(new Size(600, 400));
        view.Arrange(new Rect(0, 0, 600, 400));
        return view;
    }

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
}
