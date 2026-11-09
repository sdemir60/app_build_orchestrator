using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D2/T38] Sticky şeridin TEK metin satırı — <c>Text</c> + çözülecek bir brush anahtarı (<c>BrushKey</c>) +
/// opsiyonel statü glyph'i (<c>Glyph</c>: <c>"succeeded"</c>/<c>"failed"</c>/<c>null</c>). Saf DATA;
/// hiçbir WPF türü taşımaz.
/// </summary>
public readonly record struct RibbonLine(string Text, string BrushKey, string? Glyph)
{
    /// <summary>Şeridin KENDİ ayırıcısı (<c>"Completed — …"</c>, <c>"Stopped — …"</c>, <c>"Run failed — …"</c>,
    /// <c>"Sync failed — …"</c>). TEK yerde tanımlıdır: <see cref="Head"/>/<see cref="Detail"/> bölmeyi buradan
    /// okur, <see cref="RibbonText.Compose"/>'un biçim dizgileri kaynak sanattır ve yeniden yazılmaz.</summary>
    public const string HeadSeparator = " — ";

    /// <summary>
    /// [tray indicator/K-5] Satırın BAŞI — ilk <see cref="HeadSeparator"/>'ın öncesi (<c>"Completed"</c>,
    /// <c>"▸ Stopped"</c>, <c>"Run failed"</c>, <c>"Sync failed"</c>); ayırıcı yoksa <c>null</c>.
    ///
    /// <para><b>Başsızlık motor ölümüyle EŞ ANLAMLI DEĞİLDİR:</b> bugün ayırıcı taşımayan tek satır
    /// <c>"Engine stopped unexpectedly (…)"</c>'dır. Motorun diğer iki ölüm satırı
    /// (<c>RunViewModel.EngineMissingMessage</c>, <c>RunViewModel.EngineCannotStartMessage</c>) ayırıcıyı
    /// TAŞIR ve baş üretir ("Engine missing", "Engine could not start").</para>
    ///
    /// <para>Tek tüketicisi tepsideki bitiş bildirimidir: başlığı buradan, gövdeyi <see cref="Detail"/>'den
    /// alır. Bildirim İKİNCİ bir özet DERLEMEZ — aynı satırı okur ve kendi ayırıcısında bir kez böler, yani
    /// şerit ile balloon ayrışamaz. Bölme burada durur çünkü ayırıcıyı yazan da bu dosyadır.</para></summary>
    public string? Head => SeparatorIndex is { } i ? Text[..i] : null;

    /// <summary>Satırın GÖVDESİ — ilk <see cref="HeadSeparator"/>'ın sonrası (sayılar, süre, gerekçe); ayırıcı
    /// yoksa <c>null</c>. Bkz. <see cref="Head"/>.</summary>
    public string? Detail => SeparatorIndex is { } i ? Text[(i + HeadSeparator.Length)..] : null;

    /// <summary>İLK ayırıcının yeri — satır bir kez bölünür: gövdenin içinde ikinci bir tire geçse de gövdede
    /// kalır. <c>Text</c> hiç verilmemiş (varsayılan) bir satırda da sessizce <c>null</c> döner.</summary>
    private int? SeparatorIndex =>
        Text?.IndexOf(HeadSeparator, StringComparison.Ordinal) is { } i and >= 0 ? i : null;
}

/// <summary>
/// [sync ilerlemesi] Uçuştaki Sync'in şeride giden ilerlemesi: motorun son bildirdiği adım (<see cref="SyncStepEvent"/>) ve
/// keşfin o ana kadar bulduğu proje sayısı (<see cref="SyncDiscoveryEvent"/>). Saf DATA; hiçbir WPF türü taşımaz. Satırı
/// <see cref="RibbonText.SyncLine"/> kurar; <c>default</c> = Sync başladı ama henüz adım yok.
/// </summary>
/// <param name="Step">Motorun son bildirdiği adım; <c>null</c> = henüz adım gelmedi (<see cref="RibbonText.SyncStarting"/>).</param>
/// <param name="Branch">Yalnız <see cref="SyncStep.Fetching"/>'de anlamlı: fetch edilen branch (bilinmiyorsa <c>null</c>).</param>
/// <param name="Done">Yalnız <see cref="SyncStep.Indexing"/>'de anlamlı: o ana kadar özetlenen dosya sayısı (kümülatif).</param>
/// <param name="Total">Yalnız <see cref="SyncStep.Indexing"/>'de anlamlı: özetlenecek toplam dosya sayısı.</param>
/// <param name="ProjectsFound">Keşfin o ana kadar bulduğu proje sayısı (ana repo + harici) — analiz satırı okur; adım
/// değişse de korunur.</param>
public readonly record struct SyncProgressView(SyncStep? Step, string? Branch, int Done, int Total, int ProjectsFound);

/// <summary>
/// [şerit işlemi] Pill'in sözcüğüyle uçuşta duran, kullanıcıya görünen workspace işi: şerit bu iş sürerken pill'i amber yakar, çubuğu
/// belirsiz sürer ve satırı o işi anlatır (<see cref="RibbonText.Compose"/>). Pill'in sözcüğü istek yerlerinde <c>CurrentOperation</c>'a
/// yazılır; işin sözcüğe eşlemesi (Esc'in "durdurulamaz" satırı için) <see cref="OperationLabel.ForWork"/>'tedir. Saf
/// DATA; hiçbir WPF türü taşımaz. Değeri VM üretir (<c>RunViewModel.WorkInFlight</c>): koşu kilidi varken ve görünmeyen sessiz Sync'te
/// <see cref="None"/>'dır — koşu kendi faz satırlarıyla anlatılır, sessiz Sync ekranda bir işlem değildir.
/// </summary>
public enum RibbonWork
{
    /// <summary>Uçuşta görünür bir workspace işi yok.</summary>
    None,
    /// <summary>Görünür Sync — Sync düğmesi ya da bir işin zincirlediği Sync; satırı adımını izler (<see cref="RibbonText.SyncLine"/>).</summary>
    Sync,
    /// <summary>Branch chip'inden checkout.</summary>
    Checkout,
    /// <summary><c>N behind</c> chip'inin ff-only pull'u.</summary>
    Pull,
    /// <summary>Settings Save'in worktree kurulumu.</summary>
    CreatingWorktree,
    /// <summary>Bakım kutusunun Clean'i — keşfedilen projelerin <c>bin</c>/<c>obj</c>'i silinir.</summary>
    DeepClean,
    /// <summary>Bakım kutusunun Optimize'ı — workspace onarımı.</summary>
    Optimize,
}

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
    /// <summary>[design v1.8.0 §3.1] Repo yokken şeridin metni.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski metin <c>Not ready — no repository selected</c> idi ve "seçmek"
    /// fiilini kullanıyordu, çünkü boş durum tek bir klasör seçiciye açılıyordu. v1.8.0 akışı Settings'e
    /// yönlendirdi: eksik olan bir SEÇİM değil bir AYARDIR.</para></summary>
    public const string NotConfigured = "Not configured — repository root not set";

    /// <summary>[sync ilerlemesi] Görünür bir Sync başladı ama motor henüz bir adım bildirmedi. Her Sync bununla açılır:
    /// fetch'siz bir Sync (branch değişimi) <see cref="SyncStep.Fetching"/> göndermez, yani şerit ağa çıkıldığını ancak
    /// motor söylerse söyler.
    /// <para><b>[DEĞİŞEN KURAL]</b> Satır eskiden Sync boyunca SABİTTİ ve onu Sync'in kipi seçiyordu (fetch'li
    /// <c>▸ Sync — git fetch origin…</c>, fetch'siz <c>▸ Sync…</c>). Ölçüm: ilk worktree Sync'i 55 sn sürdü (51 sn'si ilk
    /// indeksleme); satır bu süre boyunca sabitti — fetch'li Sync'te fetch bitmişken de "git fetch" diyordu.</para></summary>
    public const string SyncStarting = "▸ Sync…";

    /// <summary>[sync ilerlemesi] Motor uzak ucu getiriyor (<see cref="SyncStep.Fetching"/>). Branch bilinmiyorsa yalnız
    /// <c>origin</c> söylenir.</summary>
    public static string SyncFetching(string? branch) =>
        branch is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "▸ Sync — fetching origin/{0}…", branch)
            : "▸ Sync — fetching origin…";

    /// <summary>[sync ilerlemesi] Motor taramadan will-build'e kadar olan analizi koşuyor (<see cref="SyncStep.Analyzing"/>).
    /// Keşif henüz proje bulmadıysa çalışma alanı anılır; sayı varsa o söylenir (tekilde "project").</summary>
    /// <param name="projects">Keşfin o ana kadar bulduğu proje sayısı (<see cref="SyncProgressView.ProjectsFound"/>).</param>
    public static string SyncAnalyzing(int projects) => projects switch
    {
        <= 0 => "▸ Sync — analyzing the workspace…",
        1 => "▸ Sync — analyzing 1 project…",
        _ => string.Format(CultureInfo.InvariantCulture, "▸ Sync — analyzing {0} projects…", projects),
    };

    /// <summary>[sync ilerlemesi] İlk indeksleme sürüyor (<see cref="SyncStep.Indexing"/>): özetlenen dosya sayısı paydasıyla.
    /// Sayılar düz tamsayıdır (binlik ayırıcı yok) — çubuk aynı oranı çizer.</summary>
    public static string SyncIndexing(int done, int total) =>
        string.Format(CultureInfo.InvariantCulture, "▸ Sync — indexing source files · {0}/{1}", done, total);

    /// <summary>[sync ilerlemesi] Görünür Sync'in satırı — adımın TEK eşlemesi: adım yok → <see cref="SyncStarting"/>,
    /// <see cref="SyncStep.Fetching"/> → <see cref="SyncFetching"/>, <see cref="SyncStep.Analyzing"/> →
    /// <see cref="SyncAnalyzing"/>, <see cref="SyncStep.Indexing"/> → <see cref="SyncIndexing"/>.</summary>
    public static string SyncLine(SyncProgressView sync) => sync.Step switch
    {
        SyncStep.Fetching => SyncFetching(sync.Branch),
        SyncStep.Analyzing => SyncAnalyzing(sync.ProjectsFound),
        SyncStep.Indexing => SyncIndexing(sync.Done, sync.Total),
        _ => SyncStarting,
    };

    /// <summary>[idle-memory · dalga 3 · karar 38] Uyuyan motor uyanırken (VM'in motor durumu <c>Waking</c>) — metnin TEK yeri.</summary>
    public const string StartingTheEngine = "▸ Starting the engine…";

    /// <summary>[şerit işlemi] Branch chip'inden checkout uçuşta (<see cref="RibbonWork.Checkout"/>): hedef branch'in adıyla; hedef
    /// bilinmiyorsa yalnız iş söylenir.</summary>
    public static string Switching(string? branch) =>
        branch is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "▸ Switching to {0}…", branch)
            : "▸ Switching branch…";

    /// <summary>[şerit işlemi] <c>N behind</c> chip'inin pull'u uçuşta (<see cref="RibbonWork.Pull"/>): ilerletilen uzak uçla
    /// (<c>origin/{branch}</c>); branch bilinmiyorsa yalnız iş söylenir.</summary>
    public static string Pulling(string? branch) =>
        branch is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "▸ Pulling origin/{0}…", branch)
            : "▸ Pulling…";

    /// <summary>[şerit işlemi] Settings Save'in worktree kurulumu uçuşta (<see cref="RibbonWork.CreatingWorktree"/>).</summary>
    public const string CreatingTheWorktree = "▸ Creating the worktree…";

    /// <summary>[şerit işlemi] Bakım kutusunun Clean'i uçuşta (<see cref="RibbonWork.DeepClean"/>, pill <c>DEEP CLEAN</c>) — ne sildiğini
    /// söyler.</summary>
    public const string DeepCleaning = "▸ Deep clean — removing bin and obj…";

    /// <summary>[şerit işlemi] Bakım kutusunun Optimize'ı uçuşta (<see cref="RibbonWork.Optimize"/>) — ne yaptığını söyler.</summary>
    public const string Optimizing = "▸ Optimize — repairing the workspace…";

    /// <summary>[T38] 11 koşulun her biri için TEK satır (design-v1 <c>BuildApp.jsx:752-770</c> birebir).</summary>
    /// <param name="phase">Uygulama fazı.</param>
    /// <param name="hasWorkspace">Repo seçili mi (prototip <c>workspace</c>).</param>
    /// <param name="allClean">Önizleme KİRLİ tek bir proje bile görmedi (koşullu olanlar DAHİL — bkz.
    /// <see cref="RunViewModel.AllClean"/>), yani ortada derlenecek bir iş yok — prototip <c>eng.allClean</c>.
    /// <paramref name="willBuild"/> ile AYNI ŞEY DEĞİLDİR: o yalnız KESİN derlenecekleri sayar, dolayısıyla
    /// <c>willBuild==0</c> iken bile koşu koşullu bir proje derliyor olabilir.</param>
    /// <param name="c">Durum sayaçları (failed/succeeded/skipped/dep-affected/building/queued).</param>
    /// <param name="willBuild">Derlenecek (willBuild) proje sayısı — koşu boyunca SABİT (prototip <c>wb</c>).</param>
    /// <param name="finishedOfWillBuild">willBuild kümesinden tamamlanan sayısı (prototip <c>fin</c>).</param>
    /// <param name="totalProjects">Toplam proje sayısı (prototip <c>36</c> placeholder'ı yerine gerçek sayı).</param>
    /// <param name="elapsedMs">Koşu geçen süresi (running/done satırlarında).</param>
    /// <param name="etaMs">Yumuşatılmış ETA (ms) — yoksa <c>null</c>.</param>
    /// <param name="checkDurMs">All-clean check koşusunun süresi (done+allClean satırında; <c>fmtDur</c> biçimi).</param>
    /// <param name="warnings">Derleyici warning sayısı (done satırlarında, dep-uyarıları HARİÇ).</param>
    /// <param name="exitPending">[P3 · Task 2] Güvenli çıkış uçuştaki işi bekliyor mu (<see cref="RunViewModel.ExitPending"/>)
    /// — satır fazdan bağımsız Stopping satırıdır (<see cref="StoppingLine"/>).</param>
    /// <param name="engineWaking">[idle-memory · dalga 3 · karar 38] Uyuyan motor uyanıyor mu (VM'in motor durumu <c>Waking</c>) — satır
    /// <see cref="StartingTheEngine"/>'dir; yeri öncelik zincirinde aşağıda.</param>
    /// <param name="sync">[sync ilerlemesi] Uçuştaki Sync'in motordan gelen adımı ve keşif sayısı — yalnız
    /// <see cref="AppPhase.Syncing"/> satırı ve görünür Sync işi (<see cref="RibbonWork.Sync"/>) okur (<see cref="SyncLine"/>); sessiz Sync ne
    /// bu faza geçer ne de bir iştir, onun adımları şeride çıkmaz.</param>
    /// <param name="work">[şerit işlemi] Pill'in sözcüğüyle uçuştaki workspace işi (<c>RunViewModel.WorkInFlight</c>): satır o işi anlatır.
    /// Motor metinleri, çıkış bekleyişi ve uyanma satırı onun ÜSTÜNDE; Sync/koşu hataları ve faz satırları ALTINDA kalır.</param>
    /// <param name="workTarget">[şerit işlemi] Hedefli işin hedefi — checkout'un ve pull'un branch'i; bilinmiyorsa <c>null</c> (satır yalnız
    /// işi söyler).</param>
    /// <param name="cleaning">[şerit işlemi] Koşu bir <c>-t:Clean</c> koşusu mu (<c>RunMode.Clean</c>): Running satırının fiili
    /// <c>Cleaning</c>'dir — hiçbir şey derlemeyen bir koşu için "Building" yalan olurdu.</param>
    public static RibbonLine Compose(AppPhase phase, bool hasWorkspace, bool allClean, RunCounters c,
                                     int willBuild, int finishedOfWillBuild, int totalProjects,
                                     long elapsedMs, long? etaMs, long? checkDurMs, int warnings,
                                     string? engineDiedMessage = null, string? syncError = null,
                                     string? runError = null, string? engineOverdue = null,
                                     bool resolvingCycles = false, int cycleRound = 0, int cycleRoundCap = 0,
                                     bool exitPending = false, bool engineWaking = false,
                                     SyncProgressView sync = default,
                                     RibbonWork work = RibbonWork.None, string? workTarget = null, bool cleaning = false)
    {
        // [E2/T37 · EngineDiedMessage ÖNCELİĞİ] Engine process öldüyse şerit, HANGİ Phase'de olursa olsun (F3:
        // mid-run ölümde Phase kozmetik olarak Stopped'a çekilse de) bu KALICI KIRMIZI hata metnini gösterir —
        // Phase YOK SAYILIR. En yüksek öncelik. Banner/toast YOK: kalıcı mod şerit-içidir; "Restart engine" düğmesi alt barda,
        // Build'in hemen solundadır (ActionBar, RunViewModel.OffersEngineRestart).
        if (engineDiedMessage is { Length: > 0 })
            return new RibbonLine(engineDiedMessage, "Brush.StatusFailText", "failed");

        // Motor YAŞIYOR ama beklenen cevabı vermiyor. Bu, geçmişe ait bir olgu (Sync/run hatası) ya da bir
        // durum betimlemesi (faz) DEĞİL, CANLI bir bekleyiştir: alttaki metinlerin hepsi "sistem çalışıyor"
        // varsayar ve o varsayım şu an geçerli değildir. Motor GERÇEKTEN öldüyse (yukarıdaki dal) o bilgi
        // daha kesindir ve üstte kalır. Renk KIRMIZI DEĞİL amber + glyph YOK: bu bir başarısızlık değil —
        // drain hâlâ meşru olabilir; satır yalnız çıkış kapısını (alt bardaki "Restart engine") gerekçelendirir.
        if (engineOverdue is { Length: > 0 })
            return new RibbonLine(engineOverdue, "Brush.AmberText", null);

        // [P3 · Task 2 · Exit] Kullanıcı çıkışı onayladı ve çıkış uçuştaki işi durduruyor: şerit, HANGİ fazda olursa
        // olsun (çıkış bir checkout'u ya da pull'u bekleyebilir — onlar fazı Stopping'e çekmez) Stopping der. Pencereyi
        // kapanış ekranı örter; satır onun altındaki kayıttır. Motor ölümü ve sessizliği ÜSTTE kalır: bekleyişi kesen onlardır.
        // Uçuştaki sayı yalnız bir derleme drain'deyken anlamlıdır (faz Stopping); aksi hâlde "wrapping up".
        if (exitPending)
            return StoppingLine(phase == AppPhase.Stopping ? c.Building : 0, finishedOfWillBuild, willBuild);

        // [idle-memory · dalga 3 · karar 38] Uyuyan motor uyanıyor: bir komut kapıda motorun başlamasını bekliyor. CANLI bir bekleyiştir —
        // Sync/koşu hatası (geçmişe ait olgu) ve faz satırları ("motor çalışıyor" varsayar; uyanmayı bekleyen Sync'in satırı henüz doğru
        // değil) onun ALTINDADIR. Ölüm/erişilemezlik metinleri, motor sessizliği ve çıkış bekleyişi ÜSTTE kalır: uyanmadan büyük ya da onu
        // kesen olgulardır. Biçim şeridin faz satırlarınınki: ▸, ikincil metin rengi, glyph yok — sonuç değil bekleyiş.
        if (engineWaking)
            return new RibbonLine(StartingTheEngine, "Brush.TextSecondary", null);

        if (!hasWorkspace)
            return new RibbonLine(NotConfigured, "Brush.TextFaint", null);

        // [şerit işlemi] Pill'in işi uçuşta: satır O işi anlatır. Sync/koşu hatası geçmişe ait bir olgudur, faz satırları da önceki işlemin
        // özetidir ("Completed — …"; Deep Clean'in boşalttığı yüzeyde "▸ Waiting for Sync — …") — ikisi de süren işin ALTINDA kalır ve iş
        // bitince geri gelir. Motor metinleri, çıkış bekleyişi ve uyanma ÜSTTE kalır: işi kesen ya da bekleten olgulardır. Biçim faz
        // satırlarınınki: ▸, ikincil metin rengi, glyph yok — sonuç değil, süren iş.
        if (WorkLine(work, workTarget, sync) is { } workText)
            return new RibbonLine(workText, "Brush.TextSecondary", null);

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
                return new RibbonLine(SyncLine(sync), "Brush.TextSecondary", null);

            case AppPhase.Idle:
                if (totalProjects == 0) // [E2/T10] repo Sync'lendi ama hiç proje yok (0-proje state)
                    return new RibbonLine("Ready — nothing to build", "Brush.TextSecondary", null);
                return new RibbonLine(
                    allClean
                        ? "▸ Ready — everything looks up to date"
                        : string.Format(CultureInfo.InvariantCulture,
                            "▸ Ready — {0} to build · {1} up to date", willBuild, totalProjects - willBuild),
                    "Brush.TextSecondary", null);

            // [planlama görünürlüğü] Run istendi, motor henüz runStarted yazmadı — her koşuda bu
            // pencerede planlama koşar ve 177 projede saniyeler sürer. Şerit burada önceki metinde DONUYORDU
            // ("▸ Ready — …" / "▸ Stopped — …") ve konsol da temizlendiği için tıklamanın kaydedildiğine dair
            // ekranda hiçbir kanıt kalmıyordu. Sayaç YOK: henüz plan yok, "0/0" uydurma olurdu — ilerlemeyi
            // motorun konsola akıttığı planlama adımları taşır. Renk Running/Stopping ile aynı (faz etkin).
            case AppPhase.Starting:
                return new RibbonLine("▸ Starting — resolving what to build", "Brush.TextSecondary", null);

            case AppPhase.Running:
                if (allClean)
                    return new RibbonLine("▸ Checking — scanning for changes…", "Brush.TextSecondary", null);
                // [design v1.7.0 §3.7] Bir döngü grubu turlarını koşarken şerit sıradan bir Build satırı DEĞİLDİR:
                // üyeler ardışık turlarla derlenir ve kullanıcının bilmek istediği ilk şey kaçıncı turda olduğudur.
                // <para>Tasarımın metni "pass 1/2" der ve SABİT iki geçiş vaat eder; tur sayısını MOTOR
                // belirler (CycleRoundPolicy: yakınsama yüzey kanıtı varken bayatsız bir tur, yokken iki ardışık
                // yeşil tur; tavan üç) ve uygulama bu
                // sayıyı olduğu gibi yazar. Sözcük de motorunkidir ("round") — konsol ve event stream aynı
                // kelimeyi kullanır, arayüz tek dil konuşur.</para>
                // <para>[Build cycle derler] Tur satırı moda değil UÇUŞTAKİ TURA bağlıdır: grup hangi düğmeyle başlamış
                // olursa olsun turlarını koşarken şerit turu yazar (cycleRound > 0; sayaçları CycleRoundStartedEvent
                // kurar, CycleCompletedEvent sıfırlar). "preparing dependencies" yalnız Resolve koşusunundur: o koşu
                // turlardan önce döngünün bayat upstream'ini derler (§8.1 kapsam); düz Build'de aynı pencere sıradan
                // "Building" satırıdır.</para>
                if (cycleRound > 0)
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "▸ Resolving cycles · round {0}/{1} · {2}/{3} · {4}",
                            cycleRound, cycleRoundCap, finishedOfWillBuild, willBuild,
                            DurationFormat.Elapsed(elapsedMs)),
                        "Brush.TextSecondary", "building");
                if (resolvingCycles)
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "▸ Resolving cycles · preparing dependencies · {0}/{1} · {2}",
                            finishedOfWillBuild, willBuild, DurationFormat.Elapsed(elapsedMs)),
                        "Brush.TextSecondary", "building");
                // [şerit işlemi] Bir -t:Clean koşusu hiçbir şey derlemez: fiil işin kendisidir. Biçim ve ETA eki TEK — yalnız fiil seçilir.
                return new RibbonLine(
                    string.Format(CultureInfo.InvariantCulture, "▸ {0} {1}/{2} · {3}{4}",
                        cleaning ? "Cleaning" : "Building",
                        finishedOfWillBuild, willBuild, DurationFormat.Elapsed(elapsedMs),
                        EtaSuffix(etaMs, willBuild, finishedOfWillBuild, c) ?? ""),
                    "Brush.TextSecondary", null);

            // [Stopping] Stop istendi, uçuştakiler drain oluyor. Running satırı BURADA kullanılamaz: Stop'a
            // bastıktan sonra "Building 7/14" görmek tıklamanın kaydedilmediği izlenimini verir (kusurun ta
            // kendisi). Metin çıkış bekleyişiyle ortaktır (StoppingLine).
            case AppPhase.Stopping:
                return StoppingLine(c.Building, finishedOfWillBuild, willBuild);

            // Kalanlar için "queued" DENMEZ: Continue yüzeyi yok, o projeler bir sonraki Build'de baştan
            // işlenecek. Satır yalnız olguyu söyler — sürdürülebilirlik sözü vermez.
            case AppPhase.Stopped:
                // [Task 2] c.Queued DEĞİL willBuild-finishedOfWillBuild: RunCounters.Queued HER Pending satırı
                // sayar (bkz. o alanın XML yorumu) — kapsam dışı olanlar dahil. willBuild/finishedOfWillBuild
                // bu run'ın KENDİ SABİT kümesidir (hem tek-proje Build'de hem Resolve cycles'ta zaten doğru
                // kapsamlıdır), "not built" onun tümleyenidir.
                return new RibbonLine(
                    string.Format(CultureInfo.InvariantCulture, "▸ Stopped — {0}/{1} · {2} not built",
                        finishedOfWillBuild, willBuild, willBuild - finishedOfWillBuild),
                    "Brush.TextDim", null);

            case AppPhase.Done:
                // [cycle rounds/Task 8 review] Kalıcı kırık (yakınsamayan) bir grup pre-skip edilse bile AllClean
                // true olabilir: önizleme (WillBuild) yalnız İMZAYA bakar, "bu grup daha önce yakınsamadı"
                // bilgisini görmez — bkz. WillBuildEvaluator tip özetindeki BİLİNEN AYRIŞMA notu ([Task 11]'de
                // bilerek açık bırakıldı). Bu dal RunCounters'ı HİÇ okumadan sabit metin döndürüyordu — "Completed — … skipped …"
                // dalından bile daha güçlü bir false-green (döngünün VAR OLMADIĞINI ima eder). AYNI StuckCyclesSuffix
                // (kopya YASAK) burada da eklenir; StuckCycles==0 iken ek boş kalır, metin BYTE-FOR-BYTE aynı kalır.
                // [final review — C1 guard] Kapıda <c>c.Failed == 0</c> da vardır ve sırası şudur: HATA her
                // özetin üstündedir. Dal eskiden sayaçlara bakmadan önce geldiği için, "hiçbir şey kirli değil"
                // diye açılan ama içinde bir proje patlayan bir koşu (koşullu bir projeyi derleyip patlatan run
                // tam olarak budur — önizlemesinde KESİN bir will-build kalemi yoktur) koşulsuz YEŞİL
                // raporlanıyordu. Hata varsa aşağıdaki "Completed — N failed …" dalı konuşur.
                if (allClean && c.Failed == 0)
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
                return new RibbonLine(NotConfigured, "Brush.TextFaint", null);
        }
    }

    /// <summary>[şerit işlemi] Uçuştaki işin satırı — iş ile satır arasındaki TEK eşleme; iş yoksa <c>null</c>. Görünür Sync'in satırı adımını
    /// izler (<see cref="SyncLine"/>): Sync'in istek penceresi de (motor henüz <c>syncStarted</c> demedi) aynı satırı okur.</summary>
    private static string? WorkLine(RibbonWork work, string? target, SyncProgressView sync) => work switch
    {
        RibbonWork.Sync => SyncLine(sync),
        RibbonWork.Checkout => Switching(target),
        RibbonWork.Pull => Pulling(target),
        RibbonWork.CreatingWorktree => CreatingTheWorktree,
        RibbonWork.DeepClean => DeepCleaning,
        RibbonWork.Optimize => Optimizing,
        _ => null,
    };

    /// <summary>
    /// [Stopping · P3 · Task 2] Stopping satırının TEK yeri — faz dalı (<see cref="AppPhase.Stopping"/>) ve tam çıkışın
    /// bekleyişi (<c>exitPending</c>) aynı metni okur. ETA eki BİLEREK yok: yeni dispatch olmadığı için kuyruk
    /// tahmini gerçeğe karşılık gelmez; bu pencerede tek anlamlı sayı kaç projenin hâlâ uçuşta olduğudur. Renk
    /// Running ile AYNI (<c>Brush.TextSecondary</c>): iş hâlâ etkin, Stopped'ın dim'i henüz hak edilmedi; glyph yok.
    /// </summary>
    /// <param name="inFlight">Hâlâ derlenen proje sayısı; 0 ise "wrapping up" — "finishing 0" yanıltıcı olurdu.</param>
    private static RibbonLine StoppingLine(int inFlight, int finishedOfWillBuild, int willBuild) => new(
        inFlight > 0
            ? string.Format(CultureInfo.InvariantCulture, "▸ Stopping — {0}/{1} · finishing {2} in flight",
                finishedOfWillBuild, willBuild, inFlight)
            : "▸ Stopping — wrapping up",
        "Brush.TextSecondary", null);

    /// <summary>
    /// [T70] Running satırının ETA eki — design-v1 <c>BuildApp.jsx:762</c> birebir:
    /// <c>eta != null &amp;&amp; building + queued &gt; 0</c> kapısı geçilirse <c>eta &lt; 4000</c> →
    /// <c>" · almost done"</c>, aksi <c>" · ~{max(5, round(eta/5000)*5)}s left"</c>; kapı geçilmezse <c>null</c>.
    /// Eşik/yuvarlama sabitleri <see cref="EtaCalculator"/>'dan okunur (TEK kaynak; matematik YENİDEN yazılmaz).
    /// <para><b>[DEĞİŞEN KURAL — Task 2]</b> Kapının "queued" yarısı artık <see cref="RunCounters.Queued"/>
    /// DEĞİL <paramref name="willBuild"/>-<paramref name="finishedOfWillBuild"/>'dir: o sayaç HER Pending
    /// satırı sayar (kapsam dışı olanlar dahil — Resolve cycles'ta bunlar artık koşu boyunca Pending kalıyor,
    /// bkz. <see cref="RunViewModel.OnProjectSkipped"/>), <c>willBuild</c> ise bu run'ın KENDİ SABİT kümesidir
    /// ve zaten doğru kapsamlıdır.</para>
    /// </summary>
    public static string? EtaSuffix(long? etaMs, int willBuild, int finishedOfWillBuild, RunCounters c)
    {
        // [Task 2 review fix M-3] c.Building teknik olarak GEREKSİZDİR: derlenmekte olan bir satır henüz
        // terminal değildir, yani zaten willBuild-finishedOfWillBuild'in İÇİNDEDİR (iki terim ÇAKIŞIR, toplam
        // building rows'u iki kez sayar) — ama kapı yalnız "> 0" sorar, çakışma sonucu DEĞİŞTİRMEZ (building>0
        // iken zaten >0'dır, building==0 iken remaining tek başına karar verir). Terim orijinal prototip
        // formülüyle (`building + queued`) aynı yapıyı BİLEREK korur ve building bir satırın (ör. bir invariant
        // ihlaliyle) willBuild kümesinin DIŞINDA kalması durumunda bile kapıyı açık tutar — savunmacı, zararsız.
        if (etaMs is not { } eta || c.Building + (willBuild - finishedOfWillBuild) <= 0)
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
