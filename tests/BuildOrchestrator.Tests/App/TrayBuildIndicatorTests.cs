using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/T3] Tepsideki animasyonlu marka göstergesi — logo-animasyonu deliverable'ının uygulamaya
/// portu.
///
/// <para><b>Bu sınıfın koruduğu şey iki katmanlıdır.</b> Biri SANAT ESERİ: 3 saniyelik zaman çizelgesi
/// tasarımcının verdiği hâliyle taşınır ve şevron ile silme kaplamasının senkronu ("şevron şeritleri seriyor"
/// etkisinin tamamı) tek taraflı değiştirilemez. Diğeri UYGULAMANIN DİSİPLİNİ: dolgular token'dan gelir,
/// döngü sonsuz değildir (bitişi iterasyon sınırında karar verilebilsin diye) ve görünmeyen bir gösterge saat
/// döndürmez.</para>
///
/// <para><b>[DEĞİŞEN KURAL] Gösterge artık SAYAÇ TAŞIMIYOR.</b></para>
///
/// <para><b>Eski iddia:</b> beyaz şerit canlı bir <c>done/total</c> sayacı taşırdı; değer değişmeden metin
/// yazılmaz, değişince rakamlar sert takas edilmez (<c>Duration.Fast</c> boyunca opaklık takası) ve yuva
/// kımıldamazdı. Şerit de bunun için markanın 60 birimlik pill'i değil 66 birimlik <c>WhiteCounter</c>
/// varyantıydı; çıkışta 82 giderdi. Beş test bunu pinliyordu
/// (<c>Counter_text_renders_done_over_total</c>, <c>Counter_ignores_a_write_with_the_same_value</c>,
/// <c>Counter_change_runs_a_soft_swap_when_motion_is_on</c>,
/// <c>Counter_change_snaps_with_no_clock_under_reduced_motion</c>,
/// <c>The_counter_slot_never_moves_when_the_digits_change</c>).</para>
///
/// <para><b>Değişme gerekçesi:</b> kullanıcının GÖRSEL TESTİ — overlay ölçüsünde küçülen sahnede rakamlar
/// okunmuyordu, yani sayacın var olma gerekçesi (okunur bir ilerleme) gerçekleşmiyordu. Okunmayan bir sayaç
/// bilgi değil gürültüdür ve logoyu bir etikete çevirir. Esas alınan asset artık deliverable'ın SADE sürümüdür
/// (<c>BuildOrchestratorIcon.xaml</c>): şerit markanın kendi 60 birimlik pill'i, çıkış mesafesi README'nin
/// zamanlama tablosundaki 88. Yerlerine iki pin geçti — şeridin markanın KENDİ şekli olduğu ve kontrolde
/// hiç yazı kalmadığı, bir de çıkış mesafesi.</para>
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

    // ---------------------------------------------------------------- sahne (T3): tasarımcının bandı

    /// <summary>
    /// [tray indicator/T3] Sahne artık tasarımcının BANDIDIR: dış Canvas (<c>Stage</c>) tasarımcının
    /// önizlemesindeki <c>viewBox="-30 76 375 134"</c>'ün ta kendisi
    /// (<c>.claude/outputs/2026-08-05-05-06-logo-animation-v1.3.0/Build Orchestrator Tray Indicator.dc.html</c>).
    ///
    /// <para>Bu test üç şeyi birden pinler: dış Canvas'ın ölçüsü <see cref="TrayBuildIndicator.StageWidth"/>/
    /// <see cref="TrayBuildIndicator.StageHeight"/>'tan gelir (ikinci bir yerde sayı yazılmaz), iç 286×286
    /// canvas bandın içine <c>Canvas.Left="30" Canvas.Top="-76"</c> ile oturur (iç koordinatın (-30,76)
    /// noktası bandın (0,0)'ına denk gelir), ve <b>375/134 sayılarının TA KENDİLERİ yalnız BURADA</b>
    /// görünür — tasarımcının viewBox'ının pinidir, başka hiçbir testte tekrarlanmaz.</para></summary>
    [StaFact]
    public void The_stage_is_the_designers_band()
    {
        var (indicator, window, _) = Realize();

        Assert.Equal(TrayBuildIndicator.StageWidth, indicator.StageCanvas.Width);
        Assert.Equal(TrayBuildIndicator.StageHeight, indicator.StageCanvas.Height);
        Assert.Equal(30.0, System.Windows.Controls.Canvas.GetLeft(indicator.InnerCanvas));
        Assert.Equal(-76.0, System.Windows.Controls.Canvas.GetTop(indicator.InnerCanvas));
        Assert.Equal(375.0, TrayBuildIndicator.StageWidth);
        Assert.Equal(134.0, TrayBuildIndicator.StageHeight);

        GC.KeepAlive(window);
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

        GC.KeepAlive(window);
    }

    /// <summary>[K-7] Gösterge geometriyi ÇİZMEZ, paylaşılan sözlükten TÜKETİR — <c>AppMark</c> ile TAM AYNI
    /// altı anahtarı ister. Beklenen liste ikinci kez YAZILMAZ: işaretin listesi neyse göstergeninki odur
    /// (<see cref="AppMarkTests.BrandGeometryKeys"/>) — iki kopya, iki tüketicinin sessizce ayrışmasına
    /// izin verirdi.</summary>
    [Fact]
    public void The_indicator_consumes_the_shared_brand_geometry_by_key()
    {
        string markup = File.ReadAllText(
            IoPath.Combine(RepoPaths.AppSrcRoot, "Controls", "TrayBuildIndicator.xaml"));

        foreach (string key in AppMarkTests.BrandGeometryKeys)
            Assert.Contains($"{{DynamicResource {key}}}", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Beyaz şerit, markanın KENDİ pill'idir: diğer dördü gibi düz bir figür, üzerinde HİÇBİR yazı yok.
    ///
    /// <para>İki iddia bilerek tek testte durur, çünkü tek bir karardır: şeridin genişletilmiş varyantı yalnız
    /// sayaç yüzünden vardı. Yazı gidince varyantın gerekçesi de kalmaz ve şerit markanın orantısına döner.
    /// İddia anahtar ADIYLA değil REFERANS EŞİTLİĞİYLE kurulur: gösterge ile <c>AppMark</c> aynı instance'ı
    /// çizmeli, yoksa marka iki yüzeyde sessizce iki şekle ayrışır.</para></summary>
    [StaFact]
    public void The_white_strip_is_the_marks_own_pill_and_carries_no_text()
    {
        var (indicator, window, host) = Realize();

        var strip = Assert.IsType<ShapePath>(indicator.FindName("StripWhiteRect"));
        Assert.Same(host.FindResource("Brand.Pill.White"), strip.Data);

        Assert.Empty(DsResources.Descendants(indicator).OfType<System.Windows.Controls.TextBlock>());

        GC.KeepAlive(window);
    }

    /// <summary>
    /// Beyaz şerit ÇIKIŞTA sade asset'in mesafesini alır: son karesi 88'dir.
    ///
    /// <para>Mesafe şeridin GENİŞLİĞİNE bağlıdır — tüm parçaların sağ ucu aynı noktada (x=250.5) yok olmalıdır
    /// (deliverable README'sinin zamanlama tablosu). 60 birimlik şerit 88 gider; 82, sayaç için 66'ya
    /// genişletilmiş varyantın değeriydi. Şerit sadeye döndüğü hâlde mesafe 82'de kalsaydı beyaz şerit
    /// diğerlerinden 6 birim geride solardı.</para></summary>
    [StaFact]
    public void The_white_strip_leaves_on_the_plain_assets_distance()
    {
        var (indicator, window, _) = Realize();

        var white = KeyFramesOf(indicator.Loop, "WhiteShift");

        Assert.Equal(88.0, white[^1].Value);
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
