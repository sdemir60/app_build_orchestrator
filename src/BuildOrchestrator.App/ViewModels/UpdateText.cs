using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Uygulama sayıları — güncelleme"] Güncelleme hapının, kartının ve restart ekranının
/// metinlerinin TEK kaynağı — XAML (<c>x:Static</c>), kabuk ve testler aynı sabiti okur. Güncelleme motoru henüz yoktur; metinler
/// tasarımın kendisidir. Tüm metin İngilizce.
/// </summary>
public static class UpdateText
{
    /// <summary>Hapın yazılı etiketi (12px/500) — yanında mono gelen sürüm durur.</summary>
    public const string PillLabel = "Update";

    /// <summary>Kartın kimlik bloğunun caps başlığı (<c>UPDATE READY</c> olarak çizilir).</summary>
    public const string CardHeading = "Update ready";

    /// <summary>Kartın açıklama satırı — kilit yokken ne olacağını söyler.</summary>
    public const string RestartNote =
        "Restart takes a few seconds and reopens the workspace. If you wait, it installs on the next start.";

    /// <summary>Kartı kapatır; hap kalır.</summary>
    public const string Later = "Later";

    /// <summary>Kartın birincil düğmesi.</summary>
    public const string RestartToUpdate = "Restart to update";

    /// <summary>[plan U3 · 1] Clean / Optimize / Resolve / checkout / pull sürerken.</summary>
    public const string WaitForTask = "Available once the running task finishes.";

    /// <summary>[plan U3 · 2] Bir Sync (sessizi dahil) sürerken.</summary>
    public const string WaitForSync = "Available once Sync finishes.";

    /// <summary>[plan U3 · 3] Koşu sürerken, işaretlenirken ya da bir Build isteği beklerken.
    /// <para><b>Tasarımdan sapma:</b> tasarım "— F5 stops it." der; uygulamada F5 koşuyu durdurmaz, Esc durdurur. Tuşun
    /// adı elle yazılmaz, kısayol kataloğundan okunur (<see cref="ShortcutCatalog"/> — jestin tek kaynağı).</para></summary>
    public static string WaitForBuild { get; } =
        $"Available once the build finishes — {ShortcutCatalog.Get(ShortcutId.Escape).Gestures[0]} stops it.";

    /// <summary>Restart kilidinin nedeni — sıra görev &gt; Sync &gt; koşu (plan U3); hiçbir iş yoksa <c>null</c>.
    /// Soruları VM sorar (<see cref="RunViewModel.UpdateRestartBlockedReason"/>), karar burada tek yerdedir.</summary>
    public static string? RestartBlockedReason(bool taskRunning, bool syncRunning, bool buildRunning) =>
        taskRunning ? WaitForTask
        : syncRunning ? WaitForSync
        : buildRunning ? WaitForBuild
        : null;

    /// <summary>[design v1.23.0 §2.12] Restart ekranının başlığı (13px/600). Ürün adı YAZILMAZ, kimlikten okunur
    /// (<see cref="AppIdentity.Product"/>).</summary>
    public static string RestartHeading { get; } = "Updating " + AppIdentity.Product;

    /// <summary>Restart ekranının adım etiketi — sürerken yazıldığı için sonunda üç nokta (U+2026) taşır.</summary>
    public static string RestartStepLabel(UpdateRestartStep step, string incoming) => step switch
    {
        UpdateRestartStep.Closing => "Closing " + AppIdentity.Product,
        UpdateRestartStep.Installing => "Installing " + incoming,
        _ => "Starting " + incoming,
    } + "…";
}
