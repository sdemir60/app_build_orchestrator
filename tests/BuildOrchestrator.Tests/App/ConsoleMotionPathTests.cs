using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · maddeler 1.8 + 1.9 + 1.10] <see cref="ConsoleView"/>'ın daktilosu/gate'i/imleci — <b>ÜRETİM
/// APPEND YOLUNDAN</b>, animasyon AÇIKKEN.
///
/// <para><b>Ölçülmüş boşluklar:</b>
/// <list type="bullet">
///   <item><b>1.8</b> — <c>TypewriterSchedulerTests</c> yalnız SAF zamanlayıcıyı çağırıyordu; <see cref="ConsoleView"/>'ın
///   KENDİ daktilosu animasyon açıkken hiç koşturulamıyordu (<c>ConsoleViewTests.cs:288</c> yalnız reduced-motion
///   instant kolunu sürüyor). Sebep <b>üretim kodundaydı</b>: bu görünüm motion sinyalini statik
///   <c>MotionGate.StaticAnimationsEnabled</c>'dan doğrudan okuyordu ve headless'ta o hep <c>false</c>'tur.
///   Kardeş kontrollerin (ProjectRow/GraphView/StickyLayerList/EventStreamView) <see cref="ConsoleView.AnimationsEnabledProvider"/>
///   seam'i bu görünüme de eklendi (davranış-nötr: varsayılan provider AYNI statik ifadedir).</item>
///   <item><b>1.9</b> — <c>TypingDegradationTests</c> <see cref="ConsoleTypingGate"/>'i DOĞRUDAN çağırıyordu;
///   <see cref="ConsoleView.AppendNarrativeBatch"/>'in gate'i GERÇEKTEN tükettiği hiçbir testte doğrulanmıyordu.</item>
///   <item><b>1.10</b> — imleç blink'i için yalnız NEGATİF kanıt vardı (<c>ReducedMotionCoverageTests.cs:222</c>);
///   saatin GERÇEKTEN döndüğü POZİTİF kanıt yalnız <c>EventStreamView</c> için mevcuttu (<c>:209</c>).</item>
/// </list></para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleMotionPathTests
{
    // Anlatı satırı: HH:MM:SS damgası TAŞIR → ConsoleLineParser.Layout(...).Clock != null → "ham MSBuild" DEĞİL.
    private const string NarrativeLine = "git fetch origin main — resolving deltas and refreshing refs";
    // Ham MSBuild çıktısı: damga YOK → gate ASLA daktilolamaz (DD2).
    private const string RawLine = "CSC : warning CS1591: Missing XML comment for publicly visible type";

    /// <summary>Animasyon AÇIK bir konsol, gerçek bir (ekran dışı) pencerede — blink/daktilo saatleri ancak
    /// canlı bir <see cref="PresentationSource"/> altında gözlemlenebilir (bkz. <see cref="AnimationHost"/>).</summary>
    private static ConsoleView RealizeWithMotion(out Window window)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, view);
        return view;
    }

    // ---------------------------------------------------------------- 1.8 üretim append yolu daktilo eder


    // ---------------------------------------------------------------- 1.9 gate üretim yolunda tüketiliyor


    // ---------------------------------------------------------------- 1.10 imleç motion AÇIKken yanıp söner

    /// <summary>[POZİTİF kanıt] Boşta "ready" imleci motion açıkken GERÇEKTEN yanıp söner. Karşıtı
    /// (<c>ReducedMotionCoverageTests:222</c>) reduced-motion'da saatin olmadığını pinler — ikisi birlikte
    /// "kapı gerçekten iki yönlü çalışıyor" der.</summary>
    [StaFact]
    public void The_idle_ready_cursor_really_blinks_when_motion_is_on()
    {
        var view = RealizeWithMotion(out var window);

        view.ShowReady();

        Assert.Equal("ready", view.ActiveLineText.Text);
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "ready imlecinin blink saati kurulmadı");
        GC.KeepAlive(window);
    }


    // ---------------------------------------------------------------- [fix-1 · I-D] canlı motion sinyali

    /// <summary>
    /// [fix-1 · I-D] OS "animasyon efektleri" ayarı koşu SIRASINDA kapanırsa konsol imlecinin SONSUZ blink saati
    /// SÖKÜLÜR. Kardeşinin (<c>ReducedMotionCoverageTests:195 EventStreamView_stops_the_cursor_blink…</c>)
    /// deseni birebir: önce saatin GERÇEKTEN döndüğü pinlenir (aksi halde ikinci assert vacuous PASS olurdu),
    /// sonra sinyal kapatılır.
    ///
    /// <para><b>Neden gerçek bir boşluktu:</b> seam'in ilk hâli <see cref="Controls.MotionGate"/>'in
    /// <b>aboneliksiz</b> kipini kullanıyordu (o kip <c>StickyLayerList</c>'ten alınmıştı — ama o sahip sonsuz
    /// saat TUTMAZ). Sonuç: sinyal kapansa bile konsol imleci sonsuza dek dönerdi. Walkthrough §11.1'in
    /// "kapat → imleç DURUR" kolu.</para></summary>
    [StaFact]
    public void The_console_cursor_stops_blinking_when_the_motion_signal_turns_off_while_running()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = true };
        var motion = new MotionSettings(signal);
        var view = new ConsoleView
        {
            AnimationsEnabledProvider = () => motion.AnimationsEnabled, MotionSettings = motion,
        };
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, view);

        view.ShowReady();                                            // idle imleç → blink başlar
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties);   // non-vacuous: saat GERÇEKTEN dönüyor

        signal.AnimationsEnabled = false;
        signal.Raise();                                              // OS ayarı koşu SIRASINDA kapandı

        Assert.False(view.ActiveCursorGlyph.HasAnimatedProperties);  // blink saati SÖKÜLDÜ
        Assert.Equal(1.0, view.ActiveCursorGlyph.Opacity);           // imleç steady
        GC.KeepAlive(window);
    }

    /// <summary>Simetrik yön: sinyal geri AÇILINCA görünür imleç yeniden yanıp sönmeye başlar (tek yönlü bir
    /// handler burada kırılır).</summary>
    [StaFact]
    public void The_console_cursor_starts_blinking_again_when_the_motion_signal_comes_back_on()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var motion = new MotionSettings(signal);
        var view = new ConsoleView
        {
            AnimationsEnabledProvider = () => motion.AnimationsEnabled, MotionSettings = motion,
        };
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, view);

        view.ShowReady();                                            // reduced → blink YOK
        Assert.False(view.ActiveCursorGlyph.HasAnimatedProperties);

        signal.AnimationsEnabled = true;
        signal.Raise();

        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties);
        GC.KeepAlive(window);
    }


    // ---------------------------------------------------------------- [A13/T4 · m2] imleç: 1.1s blink + 420ms sönme

    /// <summary>[A13/T4 fix-1 · A3/A4] Otorite <c>BuildApp.jsx:16</c>: <c>.bo-cursor { animation: bo-blink 1.1s
    /// var(--ease-in-out) infinite; }</c> — <see cref="MotionTokens.CreateBlinkAnimation"/> ÜRETİMİN ÜÇ
    /// başlatıcısının (<c>ConsoleView</c>'in idle/aktif-satır imleçleri + <c>EventStreamView</c>) ORTAK fabrikasıdır;
    /// SAF, deterministik değer pinlemesi burada yapılır: 550ms × <c>AutoReverse</c> = otoritenin 1.1s'i.
    ///
    /// <para><b>fix-1 notu:</b> önceki sürüm bunu faz-örnekleyen GERÇEK saatle (dip/tepe opaklık eşikleri,
    /// <c>InRange(800,1500)</c>) ölçüyordu — ÖLÇÜLDÜ ve yük altında yanlış çıktı: <c>BlinkMs</c> 550→200 mutasyonunda
    /// beklenen ~400ms yerine 1592ms okundu (pompa dip penceresini üst üste kaçırmış — SineEase'in dar penceresi
    /// + 5ms'lik pompa çözünürlüğü). Üretim yolundan GERÇEKTEN oynadığı iddiası zaten <b>zamandan bağımsız</b>
    /// kanıtlanıyor (aşağıdaki <see cref="The_idle_ready_cursor_really_blinks_when_motion_is_on"/>,
    /// <c>HasAnimatedProperties</c>) — bu test SAF değeri taşır, gerçek saat GEREKMEZ.</para></summary>
    [Fact]
    public void The_cursor_blink_animation_is_the_authoritys_550ms_autoreverse_cycle()
    {
        var blink = MotionTokens.CreateBlinkAnimation();

        Assert.Equal(TimeSpan.FromMilliseconds(550), blink.Duration.TimeSpan); // BuildApp.jsx:16 `1.1s` ÷ 2 (AutoReverse)
        Assert.True(blink.AutoReverse);                                       // → tam tur 2×550 = 1100ms
        Assert.Equal(RepeatBehavior.Forever, blink.RepeatBehavior);           // `infinite`
    }

    // [KALDIRILDI — design v1.7.0 §2.5] Konsolun daktilosu, saat sütunu ve satır-bazlı kaskadı kaldırıldı;
    // bu iddiaların konusu artık yok. Yerlerine gelen davranış: satırlar anında basılır, prompt satırı yalnız
    // imleç + "ready" taşır, panel geçişi tek parça tilt-in'dir.
}
