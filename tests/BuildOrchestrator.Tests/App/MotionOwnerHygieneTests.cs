using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E3 fold'ları] Motion sahibi hijyeni: (1) BuildingSpinner'ın 900ms/270° dönüşünü sayısal PİNLER (C-2 kararı —
/// bundle'ın 900ms'i, README'nin 1.4s'i DEĞİL — sessizce kaymasın); (2) motion-signal aboneliğinin idempotent
/// (subscribe-once) guard'ını kanıtlar — Loaded iki kez ateşlense de sahip TEK abonelik tutar (çift Refresh/
/// ApplyBreathing birikmez). Aynı <c>-= sonra +=</c> idiomu ProjectRow/StickyRibbon/BuildingSpinner/StatusGlyph'te
/// paylaşılır; burada seam'li ProjectRow üstünden pinlenir.
///
/// <para><b>[W2]</b> Kablaj artık <see cref="MotionGate"/>'tedir. Bu sınıf onun İKİ KİPİNİ de ayrı ayrı pinler —
/// GraphView'ın <b>latch-first</b> sapması (ilk kaynaktan sonra atama yok sayılır; MainWindow buna dayanır) ve
/// diğerlerinin <b>latch'siz</b> "her Loaded'da yeniden oku" davranışı. Ayrıca BuildingSpinner/StatusGlyph'in
/// W2'de kazandığı seam'in ÖLÜ KOD olmadığı (enjekte edilen sinyalde saatlerin gerçekten kurulduğu) kanıtlanır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class MotionOwnerHygieneTests
{
    [Fact]
    public void The_building_spinner_rotates_a_full_turn_over_900ms_at_30fps()
    {
        // C-2: 900ms (bundle) — README'nin 1.4s'i DEĞİL. 270°'lik YAY görseli 0→360° tam tur döner.
        Assert.Equal(900.0, BuildingSpinner.RotationMs);
        var spin = BuildingSpinner.BuildSpinAnimation();
        Assert.Equal(0.0, spin.From);
        Assert.Equal(360.0, spin.To);
        Assert.Equal(TimeSpan.FromMilliseconds(900), spin.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, spin.RepeatBehavior);
        Assert.Equal(30, Timeline.GetDesiredFrameRate(spin)); // dekoratif sonsuz → 30fps tavanı (feasibility §3.4)
    }

    // [KALDIRILDI — design v1.7.0 §2.5] Konsolun daktilosu, saat sütunu ve satır-bazlı kaskadı kaldırıldı;
    // bu iddiaların konusu artık yok. Yerlerine gelen davranış: satırlar anında basılır, prompt satırı yalnız
    // imleç + "ready" taşır, panel geçişi tek parça tilt-in'dir.

    /// <summary>
    /// [A13/final · lensB Ö1] Hold'un <b>DEĞERİ</b> yukarıda pinliydi ama <b>ÜRETİMDE TÜKETİLDİĞİ</b> hiçbir
    /// testle pinli değildi: lens B üç tüketim noktasının ÜÇÜNÜ BİRDEN sildi ve 1649 testlik süit tamamen yeşil
    /// kaldı (M5 + M5b). Bu test o boşluğun KAYNAK yarısını kapatır — üç sahibin üçü de hold'u zamanlayıcısının
    /// süresine EKLEMEK zorundadır; biri silinirse sayı düşer ve buradan kırmızı gelir.
    ///
    /// <para><b>Neden davranışsal değil (ÖLÇÜLDÜ):</b> davranışsal ayrım yalnız <c>ConsoleView</c> için
    /// yazılabilir — orada hold, imleç fade'inin BAŞLAMA anını geciktirir, yani gözlenebilir bir etkisi vardır
    /// (<see cref="ConsoleMotionPathTests.The_active_line_cursor_holds_steady_for_420ms_before_it_starts_to_fade"/>).
    /// <c>EventStreamView</c>'ın iki tüketimi ise <b>render yüzeyinden AYIRT EDİLEMEZ</b> (bu, "etkisi yok"tan
    /// daha dar bir iddiadır — A13/final lens B düzeltmesi): <c>TypewriterScheduler.RevealedAt</c> zaten
    /// <c>Duration</c>'da tam uzunluğa doyar, dolayısıyla metin, imleç ve satır durumu iki hâlde de BİREBİR
    /// aynıdır. Terim yine de ölü DEĞİLDİR — hold, <c>StopTypewriter()</c>'ın <c>_scheduler = null</c> atamasını
    /// geciktirir ve bunu gerçek bir üretim dalı okur — ama kullanıcıya giden sonuç değişmediği için o iki
    /// noktanın tek mümkün pini kaynak düzeyindedir; sınırı gizlemek yerine burada AÇIKÇA yazılır.</para>
    /// </summary>
    [Fact]
    public void Every_typewriter_owner_adds_the_cursor_hold_to_its_scheduler_duration()
    {
        var usages = SourceGuard.ScanApp("*.cs",
            new Regex(@"\.Duration\s*\+\s*TimeSpan\.FromMilliseconds\(CursorHoldMs\)", RegexOptions.Compiled),
            skipCommentLines: true);

        // Vakum kapısı: tarama boş bir dosya kümesi görseydi aşağıdaki sayım anlamsız olurdu.
        Assert.Contains(Path.Combine("Views", "EventStreamView.xaml.cs"), SourceGuard.ScannedAppFiles("*.cs"));

        // [DEĞİŞEN KURAL — design v1.7.0 §2.5] Konsolun daktilosu kaldırıldı; geriye TEK daktilo sahibi kaldı
        // (event stream). Eski iddia üç kullanım sayıyordu, biri ConsoleView'ındı.
        Assert.Equal(2, usages.Count);
        Assert.All(usages, u =>
            Assert.StartsWith(Path.Combine("Views", "EventStreamView.xaml.cs"), u, StringComparison.Ordinal));
    }

    [StaFact]
    public void A_motion_owner_subscribes_to_the_signal_exactly_once_across_repeated_loads()
    {
        // [E3 fold — subscribe-once] -= sonra += idempotent guard'ı: Loaded iki kez ateşlense de (unload olmadan)
        // tek abonelik kalır. Guard olmasa SubscriberCount 2 olurdu → her sinyalde çift ApplyBreathing.
        var motion = new CountingMotion();
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, MotionSettings = motion, DataContext = vm };

        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(1, motion.SubscriberCount);
    }

    /// <summary>
    /// [W2 pin — BİLİNÇLİ SAPMA] <see cref="GraphView"/>'ın abonelik guard'ı diğer sahiplerinkinden FARKLIDIR:
    /// ilk abonelikten SONRA <c>MotionSettings</c> ataması YOK SAYILIR ("latch-first"). MainWindow bu sözleşmeye
    /// açıkça dayanır (<c>MainWindow.xaml.cs:79-80</c>: "GraphView'ın MotionSettings'i Loaded'dan ÖNCE atanmalı").
    /// Bu test sapmayı pinler — bir seam-fold sırasında sessizce <c>-=</c>/<c>+=</c> idiomuna dönüştürülemesin.
    /// </summary>
    [StaFact]
    public void The_graph_view_latches_its_first_motion_source_and_ignores_later_assignments()
    {
        var first = new CountingMotion();
        var second = new CountingMotion();
        var view = new GraphView { AnimationsEnabledProvider = () => false, MotionSettings = first };

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(1, first.SubscriberCount);

        view.MotionSettings = second;                                        // ilk abonelikten SONRA → YOK SAYILIR
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(1, first.SubscriberCount);
        Assert.Equal(0, second.SubscriberCount);

        // Latch yalnız Unloaded'da açılır: sonraki Loaded YENİ kaynağa abone olur (eski kaynak bırakılmış olur).
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.Equal(0, first.SubscriberCount);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(1, second.SubscriberCount);
    }

    /// <summary>
    /// [W2 pin] Latch'siz sahipler (ProjectRow/StickyRibbon) her <c>Loaded</c>'da kaynağı YENİDEN OKUR — GraphView'ın
    /// tam TERSİ. Bugünkü davranışın tamamı pinlenir, kuyruğundaki tuhaflık dahil: yeni kaynağa abone olunur ama
    /// ESKİ abonelik çözülmez (sahip yalnız en son kaynağı hatırlar). Fold bu asimetriyi korumalıdır.
    /// </summary>
    [StaFact]
    public void A_latchless_motion_owner_re_reads_its_source_on_every_load()
    {
        var first = new CountingMotion();
        var second = new CountingMotion();
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, MotionSettings = first, DataContext = vm };

        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(1, first.SubscriberCount);

        row.MotionSettings = second;
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(1, second.SubscriberCount); // YENİ kaynağa abone olundu (GraphView bunu yapmaz)
        Assert.Equal(1, first.SubscriberCount);  // eski abonelik ÇÖZÜLMEZ — bugünkü davranış, bilinçli pin
    }

    [StaFact]
    public void The_building_spinner_subscribes_to_the_static_signal_exactly_once_across_repeated_loads()
        => AssertSubscribesOnce(new BuildingSpinner());

    [StaFact]
    public void The_status_glyph_subscribes_to_the_static_signal_exactly_once_across_repeated_loads()
        => AssertSubscribesOnce(new StatusGlyph());

    // ---------------------------------------------------------------- [W2] seam genişletmesi

    /// <summary>
    /// [W2] <see cref="BuildingSpinner"/> ve <see cref="StatusGlyph"/> artık statik <c>App.Motion</c>'a ÇİVİLENMİŞ
    /// değildir: enjekte edilen sinyal (a) abonelikte ve (b) TAZE okumada gerçekten kullanılır. Bu, aşağıdaki
    /// statik set/restore testinin (ve <c>ReducedMotionCoverageTests</c>'in) vacuous PASS'a düşmediğinin de kanıtı —
    /// enjekte edilen AÇIK sinyalde saatler GERÇEKTEN kurulur.
    /// </summary>
    [StaFact]
    public void The_spinner_and_the_glyph_honour_an_injected_motion_signal_instead_of_the_static_one()
    {
        Assert.Null(BuildOrchestrator.App.App.Motion); // statik kapalı: aşağıdaki saatler YALNIZ seam'den doğabilir

        var spinnerMotion = new CountingMotion();
        var glyphMotion = new CountingMotion();
        var host = DsResources.NewHost();
        var spinner = new BuildingSpinner { MotionSettings = spinnerMotion, AnimationsEnabledProvider = () => true };
        var glyph = new StatusGlyph
        {
            Status = GraphStatus.Building, MotionSettings = glyphMotion, AnimationsEnabledProvider = () => true,
        };
        var panel = new StackPanel { Children = { spinner, glyph } };
        var window = DsResources.Realize(host, panel);

        Assert.True(spinner.IsRotating);          // enjekte edilen AÇIK sinyal → dönüş saati kuruldu
        Assert.True(glyph.HasAnimatedProperties); // enjekte edilen AÇIK sinyal → nabız saati kuruldu
        Assert.Equal(1, spinnerMotion.SubscriberCount); // abonelik de seam'e gitti (statiğe değil)
        Assert.Equal(1, glyphMotion.SubscriberCount);
        GC.KeepAlive(window);
    }

    /// <summary>[W2] Seam'li yoldan subscribe-once: <c>Loaded</c> iki kez ateşlense de TEK abonelik kalır. Statik
    /// set/restore gerektiren aşağıdaki ikizi KALDIRILMADI — o, üretim varsayılanının (<c>App.Motion</c>) aynı
    /// guard'dan geçtiğini ayrıca kanıtlar.</summary>
    [StaTheory]
    [InlineData(typeof(BuildingSpinner))]
    [InlineData(typeof(StatusGlyph))]
    public void A_seam_fed_motion_owner_subscribes_exactly_once_across_repeated_loads(Type ownerType)
    {
        var motion = new CountingMotion();
        var owner = (FrameworkElement)Activator.CreateInstance(ownerType)!;
        switch (owner)
        {
            case BuildingSpinner s: s.MotionSettings = motion; break;
            case StatusGlyph g: g.MotionSettings = motion; break;
            default: throw new ArgumentOutOfRangeException(nameof(ownerType));
        }

        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(1, motion.SubscriberCount);
    }

    /// <summary>[fix — #3/#5] BuildingSpinner/StatusGlyph seam'li DEĞİL: motion sinyalini statik <c>App.Motion</c>'dan
    /// DOĞRUDAN okur → subscribe-once guard'ının gövdesi yalnız <c>App.Motion</c> null DEĞİLKEN koşar. Headless'ta
    /// null olduğundan guard hiç çalışmaz ve plain <c>+=</c>'e geri dönmek HİÇBİR testi düşürmezdi. Bu yüzden
    /// static'i geçici set/restore et (Console UI serial collection → mutasyon serileştirilir) ve Loaded'ı iki kez
    /// ateşle: guard varsa abonelik TEK kalır, olmasa 2 olurdu.</summary>
    private static void AssertSubscribesOnce(FrameworkElement owner)
    {
        var motion = new CountingMotion();
        using var _ = MotionScope.Enable(motion); // [A13/T4 fix-1 · C4] tek yer — restore Dispose'da (headless varsayılanı null)

        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(1, motion.SubscriberCount);
    }

    /// <summary>Abone olan delege SAYISINI (guard'ı) gözlemleyen IMotionSettings — çift-abonelik burada görünür.</summary>
    private sealed class CountingMotion : IMotionSettings
    {
        private EventHandler? _handlers;
        public bool AnimationsEnabled => false;
        public TimeSpan Effective(TimeSpan token) => TimeSpan.Zero;
        public event EventHandler? AnimationsEnabledChanged
        {
            add => _handlers += value;
            remove => _handlers -= value;
        }
        public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;
    }
}
