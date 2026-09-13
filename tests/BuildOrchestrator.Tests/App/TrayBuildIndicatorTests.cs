using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/T3] Tepsideki animasyonlu marka göstergesi — logo-animasyonu deliverable'ının (v1.3,
/// sayaçlı sürüm) uygulamaya portu.
///
/// <para><b>Bu sınıfın koruduğu şey iki katmanlıdır.</b> Biri SANAT ESERİ: 3 saniyelik zaman çizelgesi
/// tasarımcının verdiği hâliyle taşınır ve şevron ile silme kaplamasının senkronu ("şevron şeritleri seriyor"
/// etkisinin tamamı) tek taraflı değiştirilemez. Diğeri UYGULAMANIN DİSİPLİNİ: dolgular token'dan gelir,
/// döngü sonsuz değildir (bitişi iterasyon sınırında karar verilebilsin diye), görünmeyen bir gösterge saat
/// döndürmez ve sayaç değişmeden yazılmaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public sealed class TrayBuildIndicatorTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    private static (TrayBuildIndicator Indicator, Window Window, System.Windows.Controls.Border Host) Realize()
    {
        var host = DsResources.NewHost();
        var indicator = new TrayBuildIndicator();
        var window = DsResources.Realize(host, indicator);
        return (indicator, window, host);
    }

    // ---------------------------------------------------------------- realize (K-13)

    [StaFact]
    public void Indicator_realizes_without_a_hwnd()
    {
        // Headless süit XAML'i RUNTIME'da çözmez: yeni bir XAML kökü ancak gerçekten kurulup ölçülürse
        // doğrulanır (eksik anahtar / yanlış tipli token ancak burada patlar).
        var (indicator, window, host) = Realize();

        Assert.NotNull(indicator.Content);
        Assert.True(indicator.ActualWidth > 0);
        Assert.True(indicator.ActualHeight > 0);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(indicator));
        GC.KeepAlive(window);
        GC.KeepAlive(host);
    }

    // ---------------------------------------------------------------- sanat eseri: senkron ve zaman çizelgesi

    /// <summary>
    /// Deliverable'ın BAĞLAYICI kuralı (README, "Entegrasyon notları"): <c>ChevronShift.X</c> ile
    /// <c>SweepShift.X</c> aynı keyframe ve KeySpline değerlerini taşımalı — "şevron şeritleri siliyor" etkisi
    /// tamamen bu senkrona bağlıdır. Bu test, zamanlamayı tek taraflı değiştireni yakalar.
    ///
    /// <para>TEK bilinçli ayrım ÇIKIŞ MESAFESİDİR: kaplama şevrondan daha uzağa gider (260 &gt; 110), yoksa
    /// uzun yol alan bir şerit kaplamanın kenarına takılıp kesilirdi. Zamanlama (KeyTime) ve eğri (KeySpline)
    /// yine birebir aynıdır — ayrım yalnız son karenin DEĞERİNDEDİR.</para></summary>
    [StaFact]
    public void Sweep_and_chevron_share_the_exact_same_keyframes()
    {
        var (indicator, window, _) = Realize();

        var chevron = KeyFramesOf(indicator.Loop, "ChevronShift");
        var sweep = KeyFramesOf(indicator.Loop, "SweepShift");

        Assert.Equal(chevron.Count, sweep.Count);
        for (int i = 0; i < chevron.Count; i++)
        {
            Assert.Equal(chevron[i].KeyTime, sweep[i].KeyTime);
            Assert.Equal(SplineOf(chevron[i]), SplineOf(sweep[i]));
        }

        // Değerler son kare HARİÇ birebir aynı; son karede kaplama daha uzağa gider (bilinçli).
        for (int i = 0; i < chevron.Count - 1; i++)
            Assert.Equal(chevron[i].Value, sweep[i].Value);
        Assert.True(sweep[^1].Value > chevron[^1].Value,
            "kaplama çıkışta şevrondan daha uzağa gitmeli — yoksa uzun yol alan şerit kenara takılır");

        GC.KeepAlive(window);
    }

    /// <summary>
    /// [K-10] Döngü <c>RepeatBehavior=Forever</c> DEĞİLDİR — ne storyboard'da ne bir çocuğunda.
    ///
    /// <para><b>Neden yapısal bir pin:</b> "koşu bitince gösterge mevcut turunu tamamlasın" gereksinimi ancak
    /// iterasyon SINIRINDA karar verilerek sağlanabilir. Sonsuz bir storyboard'un sınırı yoktur; onu durdurmak
    /// yalnız yarıda kesmek (ya da hız/seek oyunları) olurdu. Forever geri gelirse "çıkışı tamamla" davranışı
    /// sessizce ölür — bu test o kapıyı tutar.</para></summary>
    [StaFact]
    public void The_loop_is_a_single_iteration_not_forever()
    {
        var (indicator, window, _) = Realize();

        Assert.NotEqual(RepeatBehavior.Forever, indicator.Loop.RepeatBehavior);
        Assert.All(indicator.Loop.Children, child => Assert.NotEqual(RepeatBehavior.Forever, child.RepeatBehavior));

        GC.KeepAlive(window);
    }

    [StaFact]
    public void Desired_frame_rate_is_capped_for_the_decorative_loop()
    {
        var (indicator, window, _) = Realize();

        // Dekoratif animasyonlar tam kare hızına ihtiyaç duymaz (feasibility §3.4). Beklenen değer paylaşılan
        // sabitten OKUNUR — 30 ikinci kez yazılmaz.
        Assert.Equal(MotionTokens.DecorativeFrameRate, Timeline.GetDesiredFrameRate(indicator.Loop));

        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- döngü / bitiş mekaniği

    [StaFact]
    public void The_loop_starts_a_new_iteration_when_one_ends()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();
        Assert.Equal(1, indicator.IterationCount);

        indicator.Loop.SkipToFill(indicator);  // 3.000 s karesi — gerçek bekleme YOK (D8)
        DispatcherPump.PumpUntil(() => indicator.IterationCount > 1, PumpTimeout);

        Assert.Equal(2, indicator.IterationCount);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_requested_finish_lands_at_the_end_of_the_iteration_not_in_the_middle()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();

        bool finished = false;
        indicator.RequestFinish(() => finished = true);

        Assert.False(finished);                 // istek YARIM kesmez — yalnız bir bayraktır
        Assert.Equal(1, indicator.IterationCount);

        indicator.Loop.SkipToFill(indicator);
        DispatcherPump.PumpUntil(() => finished, PumpTimeout);

        Assert.True(finished);
        Assert.Equal(1, indicator.IterationCount);   // ve yeni tur BAŞLAMADI
        GC.KeepAlive(window);
    }

    /// <summary>[§14.5] "Sonsuz bir animasyon, koşmayı bırakmadan önce görünmez olmalıdır" — gösterge
    /// görünmez olduğu anda saati sökülür (tepsi göstergesi bir kez kurulup pencereyle birlikte
    /// gizlendiğinde arka planda dönmeye devam ederdi).</summary>
    [StaFact]
    public void Hiding_the_control_stops_the_clock()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();
        Assert.True(indicator.ChevronFigure.HasAnimatedProperties);   // non-vacuous: saat GERÇEKTEN kuruldu

        indicator.Visibility = Visibility.Collapsed;

        AssertClockTornDown(indicator);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_static_frame_starts_no_clock_and_rests_every_part_in_place()
    {
        var (indicator, window, _) = Realize();

        indicator.ShowStaticFrame();

        Assert.False(indicator.ChevronFigure.HasAnimatedProperties);
        // Duruş evresi kompozisyonu = öğelerin TABAN değerleri: her parça yerinde, hepsi görünür.
        Assert.Equal(1.0, indicator.ChevronFigure.Opacity);
        Assert.Equal(0.0, indicator.ChevronShiftTransform.X);
        Assert.Equal(0.0, indicator.SweepShiftTransform.X);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Stopping_tears_the_clock_down()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();
        Assert.True(indicator.ChevronFigure.HasAnimatedProperties);

        indicator.StopNow();

        AssertClockTornDown(indicator);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [K-4] Çıkış evresi koşarken gösterge ANINDA gizlenebilir (kullanıcı pencereyi geri getirdi). Gösterge
    /// susar, ama "bitti" haberi düşmez: taahhüt terminal anında doğmuştur ve bir kez verilmelidir.
    ///
    /// <para><b>Neden ayrı bir pin:</b> saf controller testi bunu göremez — orada view bir sahtedir ve bitiş
    /// callback'ini kendi elinde tutar. Gerçek kontrolde callback storyboard'un <c>Completed</c>'ına bağlıdır
    /// ve saat sökülünce o olay BİR DAHA GELMEZ; taahhüt burada yerine getirilmezse koşu sessizce
    /// bildirimsiz kapanır.</para></summary>
    [StaFact]
    public void Stopping_while_a_finish_is_pending_still_reports_it()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();
        bool finished = false;
        indicator.RequestFinish(() => finished = true);

        indicator.StopNow();

        Assert.True(finished);
        AssertClockTornDown(indicator);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Stopping_reports_a_pending_finish_only_once()
    {
        var (indicator, window, _) = Realize();
        indicator.BeginLoop();
        int reports = 0;
        indicator.RequestFinish(() => reports++);

        indicator.StopNow();
        indicator.StopNow();

        Assert.Equal(1, reports);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- boya (K-8)

    /// <summary>[K-8] Kaynak asset'in beş-dokuz duraklı SVG gradyanları PORT EDİLMEZ: uygulama içindeki marka
    /// arayüz paletiyle konuşur. Her dolgu, <c>AppMark</c>'ın kullandığı TOKEN fırçanın TA KENDİSİDİR
    /// (referans eşitliği) — böylece iki yüzey aynı rengi iki ayrı yerden çözemez.</summary>
    [StaFact]
    public void All_fills_come_from_tokens()
    {
        var (indicator, window, host) = Realize();

        var expected = new (string Part, string TokenKey)[]
        {
            ("StripTopDarkRect", "Brush.Brand.StripDim"),
            ("StripAmberRect",   "Brush.Amber"),
            ("StripMidDarkRect", "Brush.Neutral700"),
            ("StripWhiteRect",   "Brush.TextPrimary"),
            ("StripSilverRect",  "Brush.TextSecondary"),
            ("Chevron",          "Brush.Brand.Chevron"),
        };

        foreach (var (part, tokenKey) in expected)
        {
            var figure = Assert.IsType<ShapePath>(indicator.FindName(part));
            Assert.Same(host.FindResource(tokenKey), figure.Fill);
        }

        Assert.Same(host.FindResource("Brush.Brand.CounterText"), indicator.Counter.Foreground);
        GC.KeepAlive(window);
    }

    /// <summary>[K-7] Gösterge geometriyi ÇİZMEZ, paylaşılan sözlükten TÜKETİR — <c>AppMark</c> ile aynı
    /// kaynaktan. Beyaz şerit tek istisnadır: sayaç için genişletilmiş varyantı ister, ama o varyant da AYNI
    /// dosyada tanımlıdır.</summary>
    [Fact]
    public void The_indicator_consumes_the_shared_brand_geometry_by_key()
    {
        string markup = File.ReadAllText(
            IoPath.Combine(RepoPaths.AppSrcRoot, "Controls", "TrayBuildIndicator.xaml"));

        foreach (string key in new[]
                 {
                     "Brand.Pill.TopDark", "Brand.Pill.Amber", "Brand.Pill.MidDark",
                     "Brand.Pill.WhiteCounter", "Brand.Pill.Silver", "Brand.Chevron",
                 })
            Assert.Contains($"{{DynamicResource {key}}}", markup, StringComparison.Ordinal);

        // …ve markanın kendi orantısındaki beyaz pill'i İSTEMEZ (o AppMark'ındır).
        Assert.DoesNotContain("{DynamicResource Brand.Pill.White}", markup, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- sayaç (K-6 / K-14)

    [StaFact]
    public void Counter_text_renders_done_over_total()
    {
        var (indicator, window, _) = Realize();

        indicator.SetCounter(139, 248);

        Assert.Equal("139/248", indicator.Counter.Text);
        GC.KeepAlive(window);
    }

    /// <summary>[§14.5] Aynı string'i yeniden atamak bile measure/draw'ı boşa kirletir — ve bir sayaç,
    /// koşu boyunca saniyede birçok kez AYNI değerle beslenebilir.</summary>
    [StaFact]
    public void Counter_ignores_a_write_with_the_same_value()
    {
        var (indicator, window, _) = Realize();

        indicator.SetCounter(139, 248);
        Assert.Equal(1, indicator.CounterWrites);

        indicator.SetCounter(139, 248);
        indicator.SetCounter(139, 248);

        Assert.Equal(1, indicator.CounterWrites);
        GC.KeepAlive(window);
    }

    /// <summary>[K-14] Rakam değişimi SERT bir metin takası değildir: metin kısılır, yeni değer yazılır, geri
    /// açılır. Süre kod tarafına yazılmaz — <c>Duration.Fast</c> token'ı animasyon BAŞLANGICINDA taze okunur.</summary>
    [StaFact]
    public void Counter_change_runs_a_soft_swap_when_motion_is_on()
    {
        using var motion = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var (indicator, window, _) = Realize();

        indicator.SetCounter(139, 248);                     // ilk yazım: geçilecek bir değer yok, doğrudan
        Assert.False(indicator.Counter.HasAnimatedProperties);

        indicator.SetCounter(140, 248);

        Assert.True(indicator.Counter.HasAnimatedProperties);        // geçiş saati kuruldu
        DispatcherPump.PumpUntil(() => indicator.Counter.Text == "140/248", PumpTimeout);
        Assert.Equal("140/248", indicator.Counter.Text);             // ve yeni değere indi
        DispatcherPump.PumpUntil(() => indicator.Counter.Opacity >= 1.0, PumpTimeout);
        Assert.Equal(1.0, indicator.Counter.Opacity);                // sonra geri açıldı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Counter_change_snaps_with_no_clock_under_reduced_motion()
    {
        // Seam KASTEN enjekte EDİLMEZ: headless'ta App.Motion null'dur (= reduced) ve üretim VARSAYILANI sınanır.
        Assert.Null(BuildOrchestrator.App.App.Motion);
        var (indicator, window, _) = Realize();

        indicator.SetCounter(139, 248);
        indicator.SetCounter(140, 248);

        Assert.False(indicator.Counter.HasAnimatedProperties);
        Assert.Equal("140/248", indicator.Counter.Text);
        Assert.Equal(1.0, indicator.Counter.Opacity);
        GC.KeepAlive(window);
    }

    /// <summary>[K-14] Geçiş YALNIZ opaklıktır: <c>139/248</c> → <c>140/248</c> şeridi genişletmez, sayaç
    /// yuvasını oynatmaz. Şerit sabit genişliktedir ve yuva onu paylaşılan geometriden okur.</summary>
    [StaFact]
    public void The_counter_slot_never_moves_when_the_digits_change()
    {
        var (indicator, window, _) = Realize();
        indicator.SetCounter(139, 248);
        indicator.UpdateLayout();
        double width = indicator.CounterSlot.ActualWidth;
        double left = System.Windows.Controls.Canvas.GetLeft(indicator.CounterSlot);

        indicator.SetCounter(7, 9);          // en dar çift
        indicator.UpdateLayout();

        Assert.Equal(width, indicator.CounterSlot.ActualWidth, precision: 3);
        Assert.Equal(left, System.Windows.Controls.Canvas.GetLeft(indicator.CounterSlot), precision: 3);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- yardımcılar

    /// <summary>
    /// Saatin GERÇEKTEN söküldüğünü doğrular.
    ///
    /// <para><b>Neden pompalanır (ÖLÇÜLDÜ):</b> <c>Storyboard.Remove</c> storyboard'u aynı çağrıda çözer
    /// (<c>GetCurrentState</c> hemen ardından "bu nesneye uygulanmadı" der), ama
    /// <see cref="UIElement.HasAnimatedProperties"/> bayrağı ancak bir sonraki dispatcher tick'inde temizlenir.
    /// Beklemeden okunan bayrak, sökme DOĞRU yapılmış olsa bile true görünür. Çıkış bir KOŞULA bağlıdır
    /// (sabit bir uyku değil) — D8.</para>
    ///
    /// <para>Not: <c>Stop()</c> bu iş için YETMEZ; ölçümde animasyonlar öğelere bağlı kalmaya devam etti
    /// (ve üstelik <c>Stop()</c>'un kendisi <c>Completed</c> tetikleyip döngüyü diriltiyordu).</para></summary>
    private static void AssertClockTornDown(TrayBuildIndicator indicator)
    {
        DispatcherPump.PumpUntil(() => !indicator.ChevronFigure.HasAnimatedProperties, PumpTimeout);
        Assert.False(indicator.ChevronFigure.HasAnimatedProperties);
        Assert.False(indicator.ChevronShiftTransform.HasAnimatedProperties);
    }

    private static IReadOnlyList<DoubleKeyFrame> KeyFramesOf(Storyboard loop, string targetName)
    {
        var animation = loop.Children.OfType<DoubleAnimationUsingKeyFrames>()
            .Single(a => Storyboard.GetTargetName(a) == targetName
                      && Storyboard.GetTargetProperty(a).Path == "X");
        return [.. animation.KeyFrames.Cast<DoubleKeyFrame>()];
    }

    /// <summary>Eğrinin dört kontrol noktası. <see cref="KeySpline"/> bir <see cref="Freezable"/>'dır ve DEĞER
    /// eşitliği taşımaz — iki özdeş eğri <c>Assert.Equal</c> ile ayrı çıkar; karşılaştırma sayılar üzerinden
    /// yapılmalı. Spline taşımayan bir kare için <c>null</c> (o da bir eşleşme ölçütüdür: iki animasyonun
    /// AYNI karelerinde spline olmalı).</summary>
    private static (double, double, double, double)? SplineOf(DoubleKeyFrame frame) =>
        frame is SplineDoubleKeyFrame { KeySpline: { } k }
            ? (k.ControlPoint1.X, k.ControlPoint1.Y, k.ControlPoint2.X, k.ControlPoint2.Y)
            : null;
}
