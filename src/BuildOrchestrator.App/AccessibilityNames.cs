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

    // ---- Ayraçlar (resize separator'ları — E5 fold: klavye ile odaklanır + ok tuşlarıyla resize) ----
    public const string ColumnSplitter = "Resize left and right columns";
    public const string GraphListSplitter = "Resize graph and project list";
    public const string ConsoleStreamSplitter = "Resize console and event stream";

    // ---- Proje satırı eylemleri (XAML'de de var — burada merkezileşir) ----
    public const string RevealInExplorer = "Reveal in Explorer";
    public const string OpenInVisualStudio = "Open in Visual Studio";
}
