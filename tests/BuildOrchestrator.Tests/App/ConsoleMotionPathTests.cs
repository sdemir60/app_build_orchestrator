using System.Windows;
using BuildOrchestrator.App.Console;
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

    /// <summary>[A13/T4 · m2] Otorite <c>BuildApp.jsx:16</c>: <c>.bo-cursor { animation: bo-blink 1.1s
    /// var(--ease-in-out) infinite; }</c> — <see cref="MotionTokens.BlinkMs"/> (550) <c>AutoReverse</c>'lidir, yani
    /// TAM bir döngü (1.0 → 0.1 → 1.0) 2×550ms = 1100ms sürer. Süre hiçbir testte pinli DEĞİLDİ (yalnız "döndüğü"
    /// pinliydi, "ne kadar sürede döndüğü" değil). Gerçek saatle ölçülür — bir sabit-ms iddiasının literal'i.</summary>
    [StaFact]
    public void The_idle_cursor_completes_one_blink_cycle_in_about_1100_milliseconds()
    {
        var view = RealizeWithMotion(out var window);
        view.ShowReady();
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "ön-koşul: blink saati kurulmadı");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        DispatcherPump.PumpUntil(() => view.ActiveCursorGlyph.Opacity <= 0.15, TimeSpan.FromSeconds(2)); // dip (~550ms)
        Assert.True(view.ActiveCursorGlyph.Opacity <= 0.15, "imleç hiç sönmedi — blink hiç dönmüyor");

        DispatcherPump.PumpUntil(() => view.ActiveCursorGlyph.Opacity >= 0.95, TimeSpan.FromSeconds(2)); // tam tur (~1100ms)
        clock.Stop();

        Assert.True(view.ActiveCursorGlyph.Opacity >= 0.95, "imleç bir tam turu tamamlamadı");
        // BuildApp.jsx:16 `1.1s` — kaba bir sapmayı (ör. 200ms/5s) yakalayacak gevşek bir pencere.
        Assert.InRange(clock.ElapsedMilliseconds, 800, 1500);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T4 · m2] Otorite <c>BuildApp.jsx:91</c>: <c>doneT = setTimeout(onDone, 420)</c> — daktilo
    /// bitince imleç ANINDA sönmez, ~420ms daha SABİT kalır, ANCAK ondan sonra fade-out'a girer
    /// (<see cref="ConsoleView.BeginCursorRemoval"/>'in <c>CursorHoldMs</c>'i). Kısa bir satırla (daktilo süresi
    /// birkaç 11ms'lik adım) daktilonun kendisi neredeyse anında biter, bu yüzden ölçülen bekleme neredeyse SAF
    /// 420ms'tir.</summary>
    [StaFact]
    public void The_active_line_cursor_holds_steady_for_420ms_before_it_starts_to_fade()
    {
        var view = RealizeWithMotion(out var window);
        const string shortLine = "12:00:01 ▸ a"; // charsPerStep=1 → Duration ≈ 12×11ms ≈ 132ms (küçük, ihmal edilebilir)

        var clock = System.Diagnostics.Stopwatch.StartNew();
        view.AppendNarrativeBatch(shortLine + "\n"); // ÜRETİM yolu (brief kural 3)
        Assert.False(view.ActiveLineInstant, "ön-koşul: daktilo kurulmadı");

        // Daktilo bittikten SONRA ama 420ms hold DOLMADAN: satır HÂLÂ commit EDİLMEDİ (overlay açık kalır).
        // [not: imleç bu aralıkta HÂLÂ blink'in kendisiyle (1↔0.1, autoreverse) oynar — "sabit 1.0" DEĞİL; asıl
        // iddia (hold süresi) aşağıdaki toplam-süre penceresiyle ölçülür.]
        DispatcherPump.PumpUntil(() => clock.ElapsedMilliseconds >= 250, TimeSpan.FromSeconds(2));
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility); // hold DOLMADI — henüz commit yok

        // Hold + fade (Duration.Base, fallback 180ms) sonunda satır commit edilir, overlay kapanır.
        DispatcherPump.PumpUntil(() => view.ActiveLineOverlay.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));
        clock.Stop();

        Assert.Equal(Visibility.Collapsed, view.ActiveLineOverlay.Visibility);
        Assert.Contains("12:00:01", view.Document.Text); // satır sonunda GERÇEKTEN commit edildi
        // Daktilo (~130ms) + hold (420ms) + fade (~180ms) ≈ 730ms. Alt sınır 420ms'in KENDİSİNİ garanti eder
        // (hold atlanıp anında fade'e girilseydi toplam ~310ms'de biterdi — 500 alt sınırını KAÇIRIRDI).
        Assert.InRange(clock.ElapsedMilliseconds, 500, 1500);
        GC.KeepAlive(window);
    }
}
