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
    private const string NarrativeLine = "12:00:01 ▸ git fetch origin main — resolving deltas and refreshing refs";
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

    /// <summary>
    /// Anlatı satırı üretim yolundan (<see cref="ConsoleView.AppendNarrativeBatch"/>) akınca satır <b>bir anda tam
    /// basılmaz</b>: overlay'de açılır ve daktilo zamanlayıcısı KURULUR. Seam kopsa (görünüm statik sinyale geri
    /// dönse) headless'ta instant kola düşer — overlay Collapsed kalır, satır tek hamlede dokümana girer → KIRMIZI.
    ///
    /// <para><b>[fix-1 · I-C] Deterministik.</b> Önceki hâli bir ARA KARE avlıyordu (<c>0 &lt; len &lt; full</c>);
    /// o pencere yalnız ~198 ms sürer (<see cref="TypewriterScheduler.Duration"/>) ve örnekleyen pompa daktilodan
    /// DAHA DÜŞÜK önceliktedir → yük altında kare kaçıp teşhissiz kırmızı verebilirdi (D8: yeni flake YASAK).
    /// Aynı iddia artık <see cref="ConsoleView.ActiveLineInstant"/> seam'iyle zamandan bağımsız kurulur —
    /// kademelemenin KENDİSİ (hangi t'de kaç karakter) zaten <c>TypewriterSchedulerTests</c>'te pinli.</para></summary>
    [StaFact]
    public void A_narrative_line_arriving_through_the_production_append_path_is_typed_out_progressively()
    {
        var view = RealizeWithMotion(out var window);

        view.AppendNarrativeBatch(NarrativeLine + "\n");

        // Daktilo GERÇEKTEN kuruldu (instant kola düşmedi) — zamandan bağımsız kanıt.
        Assert.False(view.ActiveLineInstant, "en yeni satır instant basıldı — daktilo hiç kurulmadı");
        // İlk kare: satır overlay'de, HENÜZ tam değil ve dokümana commit EDİLMEMİŞ.
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);
        Assert.True(view.ActiveLineText.Text.Length < NarrativeLine.Length,
            "en yeni satır ilk karede TAM basıldı — daktilo hiç koşmadı");
        Assert.StartsWith(view.ActiveLineText.Text, NarrativeLine, StringComparison.Ordinal); // önek, atlamalı değil
        Assert.DoesNotContain("refreshing refs", view.Document.Text);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 1.9 gate üretim yolunda tüketiliyor

    /// <summary>
    /// Ham MSBuild çıktısı AYNI üretim yolundan aksa bile daktilo HİÇ koşmaz (DD2) — motion AÇIK olmasına rağmen.
    /// Kararı <see cref="ConsoleTypingGate"/> verir; bu test onun <see cref="ConsoleView.AppendNarrativeBatch"/>
    /// içinde GERÇEKTEN tüketildiğini kanıtlar (gate çağrısı silinse satır overlay'de daktilolanır → KIRMIZI).
    ///
    /// <para>Non-vacuous olduğunun kanıtı kardeş testtir: AYNI kurulumda (aynı seam, aynı <c>() =&gt; true</c>)
    /// bir ANLATI satırı daktilolanır. İki satır tek testte sınanamaz — gate'in 340ms burst penceresi ikinci
    /// varışı zaten instant'a düşürürdü ve ayrım anlamını yitirirdi.</para></summary>
    [StaFact]
    public void Raw_msbuild_output_on_the_production_append_path_never_starts_the_typewriter()
    {
        // [fix-1 · I-F] `Assert.True(view.AnimationsEnabledProvider())` KALDIRILDI: testin kendi lambda'sını geri
        // okuyan totolojik bir assert'ti — ConsoleView'ın onu TÜKETTİĞİNE dair hiçbir şey söylemiyordu.
        var view = RealizeWithMotion(out var window);

        view.AppendNarrativeBatch(RawLine + "\n");

        Assert.True(view.ActiveLineInstant, "ham MSBuild satırı için daktilo KURULDU — gate tüketilmemiş");
        Assert.Equal(Visibility.Collapsed, view.ActiveLineOverlay.Visibility); // overlay hiç açılmadı
        Assert.Equal("", view.ActiveLineText.Text);
        Assert.Contains("CS1591", view.Document.Text);                        // satır ANINDA ve TAM dokümanda
        GC.KeepAlive(window);
    }

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

    /// <summary>Aktif satır (daktilo) imleci de motion açıkken blink saatiyle doğar — üretim append yolundan.</summary>
    [StaFact]
    public void The_active_line_cursor_really_blinks_while_a_line_is_being_typed()
    {
        var view = RealizeWithMotion(out var window);

        view.AppendNarrativeBatch(NarrativeLine + "\n");

        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "aktif satır imlecinin blink saati kurulmadı");
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

    /// <summary>[A13/T4 fix-1 · A3] Otorite <c>BuildApp.jsx:91</c>: <c>doneT = setTimeout(onDone, 420)</c> — daktilo
    /// bitince imleç ANINDA sönmez, <see cref="ConsoleView.CursorHoldMs"/> (420ms) kadar SABİT kalır, ANCAK ondan
    /// sonra fade-out'a girer (<see cref="ConsoleView.BeginCursorRemoval"/>). İki iddia: (1) sabitin DEĞERİ (saf
    /// assert), (2) üretim yolundan gerçekten tüketildiği (durum-tabanlı gözlem — sabit süre BEKLENMEZ).
    ///
    /// <para><b>fix-1 notu:</b> önceki sürüm <c>PumpUntil(() =&gt; clock.ElapsedMilliseconds &gt;= 250, …)</c> ile
    /// SÜRE-tabanlı bekliyordu (gizli <c>Sleep(250)</c>, D8) ve 420 sayısı hiçbir yerde LİTERAL okunmuyordu (420→800
    /// yapılsa test yeşil kalırdı). Artık (a) <c>CursorHoldMs</c> SAF <c>Assert.Equal</c> ile pinli, (b) ara-durum
    /// kontrolü zamana değil DURUMA bağlı: daktilonun TAMAMEN yazıldığı an (<c>ActiveLineText.Text == shortLine</c>)
    /// beklenir, o anda overlay'in HÂLÂ açık olması (commit henüz olmamış) hold'un sıfır OLMADIĞININ zamandan
    /// bağımsız kanıtıdır.</para></summary>
    [StaFact]
    public void The_active_line_cursor_holds_steady_for_420ms_before_it_starts_to_fade()
    {
        Assert.Equal(420.0, ConsoleView.CursorHoldMs); // BuildApp.jsx:91 `setTimeout(onDone, 420)` — saf literal pin

        var view = RealizeWithMotion(out var window);
        const string shortLine = "12:00:01 ▸ a"; // charsPerStep=1 → Duration ≈ 12×11ms ≈ 132ms (küçük, ihmal edilebilir)

        view.AppendNarrativeBatch(shortLine + "\n"); // ÜRETİM yolu (brief kural 3)
        Assert.False(view.ActiveLineInstant, "ön-koşul: daktilo kurulmadı");

        // Daktilo TAMAMEN yazılana kadar bekle (durum-tabanlı, D8 — sabit süre DEĞİL).
        DispatcherPump.PumpUntil(() => view.ActiveLineText.Text == shortLine, TimeSpan.FromSeconds(2));
        Assert.Equal(shortLine, view.ActiveLineText.Text); // ön-koşul: daktilo GERÇEKTEN bitti

        // Daktilo BİTTİ ama hold DOLMADI: satır HÂLÂ commit EDİLMEDİ (overlay açık kalır) — bu, hold'un sıfır
        // OLMADIĞININ zamandan bağımsız kanıtıdır (hold atlansaydı bu an overlay ZATEN kapanmış olurdu).
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);

        // Hold + fade sonunda satır commit edilir, overlay kapanır — gevşek bir güvenlik ağı (asıl 420ms iddiası
        // artık yukarıdaki literal'de; bu yalnız "sonunda gerçekten biter" davranışını doğrular).
        DispatcherPump.PumpUntil(() => view.ActiveLineOverlay.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));
        Assert.Equal(Visibility.Collapsed, view.ActiveLineOverlay.Visibility);
        Assert.Contains("12:00:01", view.Document.Text); // satır sonunda GERÇEKTEN commit edildi
        GC.KeepAlive(window);
    }
}
