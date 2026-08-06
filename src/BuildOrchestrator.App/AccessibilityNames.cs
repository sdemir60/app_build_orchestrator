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

    // ---- Action bar: birincil kontroller ----
    public const string SyncButton = "Sync";
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

    /// <summary>[sinema] Graf başlığındaki <c>FOLLOW PAUSED</c> pili — görünür etiketi yalnız DURUMU söyler,
    /// ad ayrıca EYLEMİ de söyler (tıklama takibi hemen döndürür).
    ///
    /// <para><b>Bu ad bugün ekran okuyucuya ULAŞMIYOR ve sebebi WPF değil, ELEMAN SEÇİMİDİR.</b> Pil düz bir
    /// <c>Border</c>'dır; WPF ona automation peer vermez, dolayısıyla UIA ağacında kendi öğesi olarak
    /// görünmez. Aynı repoda <see cref="Controls.LatestPill"/> tam bu sorunu pili özel <c>Border</c> şablonlu
    /// bir <c>Button</c> yaparak çözüyor ve adı KABUĞA değil tıklanan öğeye koyuyor — graf pili de öyle
    /// kurulduğunda sınır kapanır. Ad şimdiden burada durur ki o gün iki yerde ayrışmasın
    /// (bkz. ARCHITECTURE §20).</para></summary>
}
