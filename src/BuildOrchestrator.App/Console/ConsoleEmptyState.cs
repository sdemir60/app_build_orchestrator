using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Seçili proje logu BOŞ iken gösterilen metinler — design-v1 §2.5 BİREBİR (verbatim). Log satırlarının
/// gerçek kaskat animasyonu (remount pop-in) Task 3b'dir; burada yalnız boş-durum metinleri.
///
/// <para><c>skipped</c>/<c>queued</c> metinlerindeki somut veri (SHA, bağımlılık adları, "yesterday 18:42") design
/// örnek değerleridir — 3a'da gerçek "son başarılı build" verisi kaynağı YOK; format birebir korunur, gerçek veri
/// bağlanınca (ileride) bu tek noktadan gelir.</para>
/// </summary>
public static class ConsoleEmptyState
{
    public static string Skipped(string sha) =>
        $"Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 ({sha})";

    public static string Queued(IReadOnlyList<string> unmetDependencies) =>
        $"Queued — waiting for dependencies: {string.Join(", ", unmetDependencies)}";

    public const string NoLog = "No log yet — output streams here once the build starts.";

    /// <summary>Anlatı modunda boşta/boot tek satırı: <c>HH:MM:SS ▮ ready</c>'nin metin kısmı (dim).</summary>
    public const string Idle = "ready";
}

/// <summary>
/// [T56/3a] Proje-log modu panel başlığındaki statü glyph'i + statü adı + statü rengi eşlemesi — design-v1
/// EN_STATUS ile birebir (Started→Building, Pending→Queued). Renkler token ANAHTARLARIdır (hardcode YASAK) —
/// başlık kontrolü DynamicResource ile çözer.
/// </summary>
public static class ConsoleStatus
{
    public static string Glyph(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "✓",
        ProjectRowState.Failed => "✗",
        ProjectRowState.Skipped => "—",
        ProjectRowState.Started => "▸",
        _ => "•",
    };

    public static string Name(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "Succeeded",
        ProjectRowState.Failed => "Failed",
        ProjectRowState.Skipped => "Skipped",
        ProjectRowState.Started => "Building",
        ProjectRowState.Pending => "Queued",
        _ => state.ToString(),
    };

    public static string BrushKey(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "Brush.StatusSuccessText",
        ProjectRowState.Failed => "Brush.StatusFailText",
        ProjectRowState.Skipped => "Brush.StatusSkippedText",
        ProjectRowState.Started => "Brush.AmberText",
        ProjectRowState.Pending => "Brush.StatusQueuedText",
        _ => "Brush.TextSecondary",
    };
}
