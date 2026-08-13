using System.Globalization;

namespace BuildOrchestrator.App;

/// <summary>
/// [E5/T47] Etkileşimli öğelerin <c>AutomationProperties.Name</c> (ekran-okuyucu) metinlerinin TEK kaynağı —
/// tümü İngilizce (global kısıt: tüm UI/SR metni İngilizce). Kod-tarafı kurulan (ikon-ağırlıklı) kontroller
/// buradan adlanır; sayaç chip'lerinde AYNI metin hem tooltip hem UIA-adı olur (kopya YASAK — tek yer).
///
/// <para>Adlar KISA ve ANLAMLI: ikon-yalnız bir kontrolün görsel içeriği ekran okuyucuya hiçbir şey söylemez,
/// bu yüzden buradaki metin o kontrolün işlevini tarif eder.</para>
/// </summary>
public static class AccessibilityNames
{
    // ---- Action bar: durum/filtre sayaç chip'leri (AYNI metin tooltip + UIA-adı) ----
    public const string FilterAll = "All projects — clear filter";
    public const string FilterBuilding = "Building now — filter";
    public const string FilterSucceeded = "Succeeded — filter";
    public const string FilterFailed = "Failed — filter";
    public const string FilterSkipped = "Skipped — filter";
    public const string FilterDep = "Dependency-affected — filter";
    public const string FilterCycle = "In a dependency cycle — filter";

    // ---- Action bar: birincil kontroller ----
    public const string SyncButton = "Sync";

    // ---- Action bar: bakım kutusu (design v1.7.0 §2.7-2) ----
    /// <summary>Üç bakım düğmesinin UIA adı. Düğmeler ikon-yalnızdır (etiket bara sığmıyor), bu yüzden
    /// ekran okuyucunun duyacağı TEK metin budur. Ad DURUMDAN BAĞIMSIZ sabittir — tooltip değişir, ad
    /// değişmez: ekran okuyucu kontrolün İŞLEVİNİ duyar, sayılarını değil.</summary>
    public const string CleanButton = "Clean";
    public const string OptimizeButton = "Optimize";
    public const string ResolveCyclesButton = "Resolve cycles";

    /// <summary>[karar 2026-08-13] Clean/Optimize'ın arka ucu henüz yazılmadı; düğmeler tasarımdaki yerlerinde
    /// ama pasif durur ve tooltip nedenini söyler. Ek metin TEK yerde durur, iki tooltip de ondan türer.</summary>
    private const string NotAvailableSuffix = " — not available yet";

    public const string CleanTooltip =
        CleanButton + " — /t:Clean on every solution, then remove bin/, obj/, artifacts/" + NotAvailableSuffix;

    public const string OptimizeTooltip =
        OptimizeButton + " — restore packages, prune the cache, rebuild the dependency index" + NotAvailableSuffix;

    /// <summary>Resolve cycles düğmesinin ToolTip'i: döngü varsa ne yapacağını üye sayısıyla anlatır, yoksa
    /// neden pasif olduğunu söyler.
    ///
    /// <para><b>Tasarımdan bilinçli SAPMA (karar 2026-08-13):</b> prototip metni "in two passes" der; sabit
    /// bir tur sayısı VAAT EDİLMEZ, çünkü tur sayısını motor belirler (<c>CycleRoundPolicy</c>: yakınsama
    /// ölçütü iki ardışık yeşil tur, tavan üç). Sözcük de motorunkidir ("round"): şerit, konsol ve event
    /// stream aynı kelimeyi kullanır, arayüz tek dil konuşur. Cümlenin geri kalanı tasarımdakiyle aynıdır.</para>
    ///
    /// <para><b>Korunan geliştirme:</b> grup sayısı yalnız BİRDEN ÇOK ayrı döngü varken eklenir — tasarımın
    /// tek-sayılı cümlesi o durumda eksik kalıyor, "5 proje" beş projelik TEK bir döngü sanılabiliyordu.
    /// Tek gruplu yaygın durumda cümle tasarımdakiyle birebir aynıdır.</para></summary>
    public static string ResolveCyclesTooltip(int groupCount, int memberCount)
    {
        if (memberCount <= 0) return ResolveCyclesButton + " — no dependency cycles detected";

        string text = string.Format(CultureInfo.InvariantCulture,
            "{0} — build the {1} cycle projects in repeated rounds: stale references first, then rebuild until they converge",
            ResolveCyclesButton, memberCount);
        return groupCount > 1
            ? string.Format(CultureInfo.InvariantCulture, "{0} ({1} separate cycles)", text, groupCount)
            : text;
    }

    public const string StopButton = "Stop build";
    public const string BranchChip = "Branch — choose build target";
    public const string WorktreeChip = "Worktree — build isolation";
    public const string PerfChip = "Performance profile";
    public const string BuildOptions = "Build options";

    // ---- Filtre / popover input'ları ----
    public const string ProjectFilter = "Filter projects";
    /// <summary>[A13/T2 · 2.3] PROJECTS başlığındaki kaldırılabilir filtre chip'i (ikon-yalnız ✕ göstergesi
    /// ekran okuyucuya bir şey söylemez — işlev burada tarif edilir).</summary>
    public const string ClearFilterChip = "Clear the active filter";
    public const string BranchFilter = "Filter branches";
    public const string WorktreeSwitch = "Build in worktree";

    // ---- [About] Title bar ----
    /// <summary>Title bar'daki ikon-yalnız info butonu. Tooltip'ten AYRIDIR: tooltip, kısayolu da anlatan
    /// katalog cümlesidir (<c>ShortcutCatalog.Get(ShortcutId.About).Description</c>); UIA adı ise kontrolün
    /// işlevini KISA tarif eder.</summary>
    public const string About = "About";

    // ---- Ayraçlar (resize separator'ları — E5 fold: klavye ile odaklanır + ok tuşlarıyla resize) ----
    public const string ColumnSplitter = "Resize left and right columns";
    public const string GraphListSplitter = "Resize graph and project list";
    public const string ConsoleStreamSplitter = "Resize console and event stream";

    // ---- Proje satırı eylemleri (XAML'de de var — burada merkezileşir) ----
    public const string RevealInExplorer = "Reveal in Explorer";
    public const string OpenInVisualStudio = "Open in Visual Studio";

    // ---- [A13/T5] Konsol paneli ----
    /// <summary>Konsol başlığındaki ikon-yalnız copy butonu. AYNI metin hem tooltip hem UIA-adıdır (sayaç
    /// chip'leriyle aynı kural) — buton başarılı kopyada tooltip'i geçici "Copied"e çevirir, adı ise
    /// DEĞİŞMEZ: ad kontrolün işlevini tarif eder, anlık geri bildirimini değil.</summary>
    public const string CopyLog = "Copy log";

    /// <summary>[About] Tanı raporunu panoya yazan footer butonu. AYNI metin hem görünür etiket hem UIA
    /// adıdır (CopyLog kuralının eşi); başarılı kopyada yalnız ETİKET geçici "Copied" olur, ad DEĞİŞMEZ —
    /// ad kontrolün işlevini tarif eder, anlık geri bildirimini değil.</summary>
    public const string CopyDiagnostics = "Copy diagnostics";

    // ---- [A13/T5] `⌄ latest` pill'leri ----
    // Pill'in ROLÜNÜ (en sona git) kontrol bilir, ama HANGİ akışın sonu olduğunu yalnız host bilir; bu yüzden
    // üç ayrı metin vardır ve adı host verir (ShellRoot'un ayraçlara ad vermesiyle AYNI ilke). "latest"
    // etiketi tek başına ekran okuyucuya hiçbir şey söylemez.
    public const string LatestProjects = "Jump to the latest project";
    public const string LatestConsole = "Jump to the latest console output";
    public const string LatestEvents = "Jump to the latest event";

    // ---- [A13/T5] Settings: katman satırı ----
    /// <summary>Katman kartındaki ad input'u (kolon başlığı "LAYER NAME" + watermark ile aynı sözcükler).</summary>
    public const string LayerName = "Layer name";
    /// <summary>Katman kartındaki desen input'u — kolon başlığı "PATTERN", işlevi design-v1'in kendi
    /// cümlesiyle ("regex on the project name") tarif edilir; watermark bir ÖRNEK desendir, etiket değildir.</summary>
    public const string LayerPattern = "Layer pattern — regex on the project name";
    /// <summary>"Add layer" ghost butonu: görünür etiketi <c>Content</c>'in İÇİNDEKİ bir TextBlock'tur, bu
    /// yüzden WPF'in peer'ı adı içerikten türetemez (ölçüldü: ad boş kalır) — ad AÇIKÇA verilir.</summary>
    public const string AddLayer = "Add layer";

    // ---- [A13/T5] Worktree popover: hedef satırı ----
    /// <summary>Hedef satırındaki çöp kutusunun tooltip'i (satır başına AYNI metin).</summary>
    public const string DeleteWorktree = "Delete worktree";

    /// <summary>Çöp kutusunun UIA adı: liste birden çok satır taşır ve hepsinde AYNI ikon durur — ad HANGİ
    /// worktree'nin silineceğini söylemelidir (tooltip kısa kalır, ekran okuyucu tam bilgiyi alır).</summary>
    public static string DeleteWorktreeNamed(string worktreeName) => $"{DeleteWorktree} {worktreeName}";

    /// <summary>[A13/T5] Graf düğümü — ad DÜĞÜM BAŞINA anlamlıdır: sabit bir "graph node" metni ekran
    /// okuyucuya hiçbir şey söylemez. Tam proje adı + statü etiketi (<see cref="Controls.StatusGlyph.LabelFor"/>,
    /// design-v1 EN_STATUS) birleşir; ayraç, uygulamanın diğer birleşik adlarıyla aynı em-dash'tır.</summary>
    public static string GraphNode(string projectName, string statusLabel) => $"{projectName} — {statusLabel}";
}
