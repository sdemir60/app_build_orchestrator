using System.Globalization;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D2/T38] Sticky şeridin TEK metin satırı — <c>Text</c> + çözülecek bir brush anahtarı (<c>BrushKey</c>) +
/// opsiyonel statü glyph'i (<c>Glyph</c>: <c>"succeeded"</c>/<c>"failed"</c>/<c>null</c>). Saf DATA;
/// hiçbir WPF türü taşımaz.
/// </summary>
public readonly record struct RibbonLine(string Text, string BrushKey, string? Glyph);

/// <summary>
/// [D2/T38+T39+T70] Sticky şeridin SAF metin/ilerleme mantığı (design-v1 <c>BuildApp.jsx:752-776</c> birebir
/// portu). UI'sız, statik, tüm biçimleme <see cref="CultureInfo.InvariantCulture"/> (Türkçe Windows'ta
/// <c>4,2s</c>/<c>~35s</c> tuzağı VM'e sızmasın). 11 faz-metni satırı burada üretilir; şerit görünümü
/// (<c>StickyRibbon</c>) yalnız bunu okuyup boyar (mantık kontrolde kopyalanmaz).
/// <para>
/// <b>Süre biçimleyicileri:</b> canlı/elapsed süreler <see cref="DurationFormat.Elapsed"/> (fmtElapsed portu),
/// all-clean check süresi <see cref="DurationFormat.Duration"/> (fmtDur portu — alt-saniye ondalık dalıyla,
/// <c>BuildApp.jsx:767</c>'deki <c>BO.fmtDur</c> gibi). ETA eşik/yuvarlama sabitleri <see cref="EtaCalculator"/>'dan
/// (TEK kaynak) okunur — <see cref="EtaSuffix"/> yalnız GÖSTERİM biçimleyicisidir, EMA/ham tahmin matematiğini
/// (o <see cref="EtaCalculator.ComputeRawEstimateMs"/>/<see cref="EtaCalculator.Smooth"/>'ta) YENİDEN YAZMAZ.
/// </para>
/// </summary>
public static class RibbonText
{
    /// <summary>[T38] 11 koşulun her biri için TEK satır (design-v1 <c>BuildApp.jsx:752-770</c> birebir).</summary>
    /// <param name="phase">Uygulama fazı.</param>
    /// <param name="hasWorkspace">Repo seçili mi (prototip <c>workspace</c>).</param>
    /// <param name="allClean">Bu koşuda derlenecek proje YOK (her şey güncel) — prototip <c>eng.allClean</c>.</param>
    /// <param name="c">Durum sayaçları (failed/succeeded/skipped/dep-affected/building/queued).</param>
    /// <param name="willBuild">Derlenecek (willBuild) proje sayısı — koşu boyunca SABİT (prototip <c>wb</c>).</param>
    /// <param name="finishedOfWillBuild">willBuild kümesinden tamamlanan sayısı (prototip <c>fin</c>).</param>
    /// <param name="totalProjects">Toplam proje sayısı (prototip <c>36</c> placeholder'ı yerine gerçek sayı).</param>
    /// <param name="elapsedMs">Koşu geçen süresi (running/done satırlarında).</param>
    /// <param name="etaMs">Yumuşatılmış ETA (ms) — yoksa <c>null</c>.</param>
    /// <param name="checkDurMs">All-clean check koşusunun süresi (done+allClean satırında; <c>fmtDur</c> biçimi).</param>
    /// <param name="warnings">Derleyici warning sayısı (done satırlarında, dep-uyarıları HARİÇ).</param>
    public static RibbonLine Compose(AppPhase phase, bool hasWorkspace, bool allClean, RunCounters c,
                                     int willBuild, int finishedOfWillBuild, int totalProjects,
                                     long elapsedMs, long? etaMs, long? checkDurMs, int warnings,
                                     string? engineDiedMessage = null, string? syncError = null,
                                     string? runError = null, string? engineOverdue = null)
    {
        // [E2/T37 · EngineDiedMessage ÖNCELİĞİ] Engine process öldüyse şerit, HANGİ Phase'de olursa olsun (F3:
        // mid-run ölümde Phase kozmetik olarak Stopped'a çekilse de) bu KALICI KIRMIZI hata metnini gösterir —
        // Phase YOK SAYILIR. En yüksek öncelik. Banner/toast YOK: kalıcı mod şerit-içidir, "Restart engine"
        // aksiyonu görünümde (StickyRibbon) eklenir.
        if (engineDiedMessage is { Length: > 0 })
            return new RibbonLine(engineDiedMessage, "Brush.StatusFailText", "failed");

        // Motor YAŞIYOR ama beklenen cevabı vermiyor. Bu, geçmişe ait bir olgu (Sync/run hatası) ya da bir
        // durum betimlemesi (faz) DEĞİL, CANLI bir bekleyiştir: alttaki metinlerin hepsi "sistem çalışıyor"
        // varsayar ve o varsayım şu an geçerli değildir. Motor GERÇEKTEN öldüyse (yukarıdaki dal) o bilgi
        // daha kesindir ve üstte kalır. Renk KIRMIZI DEĞİL amber + glyph YOK: bu bir başarısızlık değil —
        // drain hâlâ meşru olabilir; satır yalnız çıkış kapısını (şerit-içi "Restart engine") gerekçelendirir.
        if (engineOverdue is { Length: > 0 })
            return new RibbonLine(engineOverdue, "Brush.AmberText", null);

        if (!hasWorkspace)
            return new RibbonLine("Not ready — no repository selected", "Brush.TextFaint", null);

        // [E2/T10] Son Sync başarısız oldu (repo seçili): KIRMIZI "Sync failed — {reason}" — faz-metninin önüne
        // geçer (retry = Sync; başarılı Sync ya da yeni Sync başlangıcı temizler). Engine-died'dan sonra gelir.
        if (syncError is { Length: > 0 })
            return new RibbonLine(
                string.Format(CultureInfo.InvariantCulture, "Sync failed — {0}", syncError),
                "Brush.StatusFailText", "failed");

        // [runFailed] Koşan bir run motor tarafında düştü: runCompleted ASLA gelmeyeceği için faz-metni tek
        // başına bırakılsa donmuş bir "▸ Building 3/10" gösterirdi. Sync hatasının ALTINDA: durumlar bilinmiyorsa
        // (Sync düştü) kullanıcının bir sonraki adımı zaten Sync'tir, run gerekçesi o zaman ikincildir.
        if (runError is { Length: > 0 })
            return new RibbonLine(
                string.Format(CultureInfo.InvariantCulture, "Run failed — {0}", runError),
                "Brush.StatusFailText", "failed");

        switch (phase)
        {
            case AppPhase.Boot:
                return new RibbonLine("▸ Waiting for Sync — project states appear after Sync", "Brush.TextDim", null);

            case AppPhase.Syncing:
                return new RibbonLine("▸ Sync — git fetch origin…", "Brush.TextSecondary", null);

            case AppPhase.Idle:
                if (totalProjects == 0) // [E2/T10] repo Sync'lendi ama hiç proje yok (0-proje state)
                    return new RibbonLine("Ready — nothing to build", "Brush.TextSecondary", null);
                return new RibbonLine(
                    allClean
                        ? "▸ Ready — everything looks up to date"
                        : string.Format(CultureInfo.InvariantCulture,
                            "▸ Ready — {0} to build · {1} up to date", willBuild, totalProjects - willBuild),
                    "Brush.TextSecondary", null);

            // [planlama görünürlüğü] Run istendi, motor henüz runStarted yazmadı — taze bir segmentte bu
            // pencerede planlama koşar ve 177 projede saniyeler sürer. Şerit burada önceki metinde DONUYORDU
            // ("▸ Ready — …" / "▸ Stopped — …") ve konsol da temizlendiği için tıklamanın kaydedildiğine dair
            // ekranda hiçbir kanıt kalmıyordu. Sayaç YOK: henüz plan yok, "0/0" uydurma olurdu — ilerlemeyi
            // motorun konsola akıttığı planlama adımları taşır. Renk Running/Stopping ile aynı (faz etkin).
            case AppPhase.Starting:
                return new RibbonLine("▸ Starting — resolving what to build", "Brush.TextSecondary", null);

            case AppPhase.Running:
                if (allClean)
                    return new RibbonLine("▸ Checking — scanning for changes…", "Brush.TextSecondary", null);
                return new RibbonLine(
                    string.Format(CultureInfo.InvariantCulture, "▸ Building {0}/{1} · {2}{3}",
                        finishedOfWillBuild, willBuild, DurationFormat.Elapsed(elapsedMs), EtaSuffix(etaMs, c) ?? ""),
                    "Brush.TextSecondary", null);

            // [Stopping] Stop istendi, uçuştakiler drain oluyor. Running satırı BURADA kullanılamaz: Stop'a
            // bastıktan sonra "Building 7/14" görmek tıklamanın kaydedilmediği izlenimini verir (kusurun ta
            // kendisi). ETA eki BİLEREK yok — yeni dispatch olmadığı için kuyruk tahmini gerçeğe karşılık
            // gelmez; bu pencerede tek anlamlı sayı kaç projenin hâlâ uçuşta olduğudur.
            // Renk Running ile AYNI (TextSecondary): faz hâlâ etkin, Stopped'ın dim'i henüz hak edilmedi.
            case AppPhase.Stopping:
                return new RibbonLine(
                    c.Building > 0
                        ? string.Format(CultureInfo.InvariantCulture, "▸ Stopping — {0}/{1} · finishing {2} in flight",
                            finishedOfWillBuild, willBuild, c.Building)
                        : "▸ Stopping — wrapping up", // uçuşta bir şey kalmadı: "finishing 0" yanıltıcı olurdu
                    "Brush.TextSecondary", null);

            // Kalanlar için "queued" DENMEZ: Continue yüzeyi yok, o projeler bir sonraki Build'de baştan
            // işlenecek. Satır yalnız olguyu söyler — sürdürülebilirlik sözü vermez.
            case AppPhase.Stopped:
                return new RibbonLine(
                    string.Format(CultureInfo.InvariantCulture, "▸ Stopped — {0}/{1} · {2} not built",
                        finishedOfWillBuild, willBuild, c.Queued),
                    "Brush.TextDim", null);

            case AppPhase.Done:
                // [cycle rounds/Task 8 review] Kalıcı kırık (yakınsamayan) bir grup pre-skip edilse bile AllClean
                // true olabilir: önizleme (WillBuild) yalnız İMZAYA bakar, "bu grup daha önce yakınsamadı"
                // bilgisini görmez — bkz. WillBuildEvaluator tip özetindeki BİLİNEN AYRIŞMA notu ([Task 11]'de
                // bilerek açık bırakıldı). Bu dal RunCounters'ı HİÇ okumadan sabit metin döndürüyordu — "Completed — … skipped …"
                // dalından bile daha güçlü bir false-green (döngünün VAR OLMADIĞINI ima eder). AYNI StuckCyclesSuffix
                // (kopya YASAK) burada da eklenir; StuckCycles==0 iken ek boş kalır, metin BYTE-FOR-BYTE aynı kalır.
                if (allClean)
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "Everything up to date — {0} projects checked in {1}, nothing to build{2}",
                            totalProjects, DurationFormat.Duration(checkDurMs), StuckCyclesSuffix(c)),
                        "Brush.StatusSuccessText", "succeeded");

                // [cycle rounds/Task 8] Kalıcı kırık (yakınsamayan) döngüler plain "skipped" görünür — bu
                // ek olmadan 0-failed bir run tamamen yeşil okunurdu (StuckCyclesSuffix TEK kaynak, iki dal
                // da aynı segmenti çağırır — kopya YASAK).
                if (c.Failed > 0)
                {
                    string dep = c.DepAffected > 0
                        ? string.Format(CultureInfo.InvariantCulture, " ({0} dependency-affected)", c.DepAffected)
                        : "";
                    string warn = warnings > 0
                        ? string.Format(CultureInfo.InvariantCulture, " · {0} warnings", warnings)
                        : "";
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "Completed — {0} failed · {1} succeeded{2} · {3} skipped{4}{5} · {6}",
                            c.Failed, c.Succeeded, dep, c.Skipped, StuckCyclesSuffix(c), warn, DurationFormat.Elapsed(elapsedMs)),
                        "Brush.StatusFailText", "failed");
                }
                else
                {
                    string warn = warnings > 0
                        ? string.Format(CultureInfo.InvariantCulture, " · {0} warnings", warnings)
                        : "";
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "Completed — {0} succeeded · {1} skipped{2}{3} · {4}",
                            c.Succeeded, c.Skipped, StuckCyclesSuffix(c), warn, DurationFormat.Elapsed(elapsedMs)),
                        "Brush.StatusSuccessText", "succeeded");
                }

            default: // Empty (workspace var ama faz Empty — teorik; boot'a düşmeden önce) → nötr davet
                return new RibbonLine("Not ready — no repository selected", "Brush.TextFaint", null);
        }
    }

    /// <summary>
    /// [T70] Running satırının ETA eki — design-v1 <c>BuildApp.jsx:762</c> birebir:
    /// <c>eta != null &amp;&amp; building + queued &gt; 0</c> kapısı geçilirse <c>eta &lt; 4000</c> →
    /// <c>" · almost done"</c>, aksi <c>" · ~{max(5, round(eta/5000)*5)}s left"</c>; kapı geçilmezse <c>null</c>.
    /// Eşik/yuvarlama sabitleri <see cref="EtaCalculator"/>'dan okunur (TEK kaynak; matematik YENİDEN yazılmaz).
    /// </summary>
    public static string? EtaSuffix(long? etaMs, RunCounters c)
    {
        if (etaMs is not { } eta || c.Building + c.Queued <= 0)
            return null; // kapı: canlı bir ETA yok ya da derlenen/kuyrukta hiçbir şey kalmadı

        if (eta < EtaCalculator.AlmostDoneThresholdMs)
            return " · almost done";

        // round(eta/5000)*5 → en yakın 5sn (AwayFromZero = JS Math.round pozitiflerde); tabanı 5sn'ye clamp.
        long roundedSec = Math.Max(5,
            (long)Math.Round(eta / (double)EtaCalculator.DisplayRoundingMs, MidpointRounding.AwayFromZero) * 5);
        return string.Format(CultureInfo.InvariantCulture, " · ~{0}s left", roundedSec);
    }

    /// <summary>[cycle rounds/Task 8] Done özetinin "skipped" segmentine eklenen ek — bir run kalıcı kırık
    /// (yakınsamayan) döngü içeriyorsa "0 failed" bile koşulsuz başarı OKUNMASIN diye. Skipped sayısına
    /// bitişik yazılır (dep-affected'in succeeded'e bitişik yazılması gibi); yoksa boş.</summary>
    public static string StuckCyclesSuffix(RunCounters c) =>
        c.StuckCycles > 0
            ? string.Format(CultureInfo.InvariantCulture, " ({0} stuck in a cycle)", c.StuckCycles)
            : "";

    /// <summary>[T38] İlerleme yüzdesi (0..100) — design-v1 <c>BuildApp.jsx:773-775</c>: allClean →
    /// (done ? 100 : skipped/total*100); aksi wb&gt;0 ? fin/wb*100 : 0. Sıfıra bölme savunmacı korunur.</summary>
    public static double Progress(AppPhase phase, bool allClean, RunCounters c, int willBuild,
                                  int finishedOfWillBuild, int totalProjects)
    {
        if (allClean)
            return phase == AppPhase.Done ? 100.0 : (totalProjects > 0 ? (double)c.Skipped / totalProjects * 100.0 : 0.0);
        return willBuild > 0 ? (double)finishedOfWillBuild / willBuild * 100.0 : 0.0;
    }

    /// <summary>[T38] İlerleme çubuğunun DOLGU statüsü — design-v1 <c>BuildApp.jsx:776</c>: herhangi bir hata
    /// varsa <b>anında</b> <c>"failed"</c> (koşu ortasında bile kırmızı), aksi done ise <c>"succeeded"</c>
    /// (yeşil), aksi <c>"building"</c> (amber). Dönen anahtar <see cref="FillBrushKeyFor"/> ile token brush'ına
    /// çevrilir.</summary>
    public static string ProgressStatus(AppPhase phase, RunCounters c)
        => c.Failed > 0 ? "failed" : phase == AppPhase.Done ? "succeeded" : "building";

    /// <summary>[T38] Dolgu statüsü → token brush anahtarı (design-v1 <c>_ds_bundle.js:498-503 FILL</c>):
    /// building→amber, succeeded→status-success, failed→status-fail (hepsi güçlü renk; *-text DEĞİL).</summary>
    public static string FillBrushKeyFor(string status) => status switch
    {
        "failed" => "Brush.StatusFail",
        "succeeded" => "Brush.StatusSuccess",
        _ => "Brush.Amber",
    };
}
