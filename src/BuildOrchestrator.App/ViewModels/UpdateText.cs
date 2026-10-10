using System.Globalization;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Uygulama sayıları — güncelleme"] Güncelleme hapının, kartının ve restart ekranının
/// metinlerinin TEK kaynağı — XAML (<c>x:Static</c>), kabuk ve testler aynı sabiti okur. Metinler tasarımın kendisidir.
/// Tüm metin İngilizce.
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

    /// <summary>[K5] Kartın 5 maddeden sonrasını sayan satır — düz metin, tıklanmaz: gelen sürümün tüm notları restart'tan
    /// sonra What's new'dedir.</summary>
    public static string MoreHighlights(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"+{count} more in What's new after restart");

    /// <summary>Kartı kapatır; hap kalır.</summary>
    public const string Later = "Later";

    /// <summary>Kartın birincil düğmesi.</summary>
    public const string RestartToUpdate = "Restart to update";

    /// <summary>[plan U3 · 1] Clean / Optimize / Resolve / checkout / pull sürerken.</summary>
    public const string WaitForTask = "Available once the running task finishes.";

    /// <summary>[plan U3 · 2] Bir Sync (sessizi dahil) sürerken.</summary>
    public const string WaitForSync = "Available once Sync finishes.";

    /// <summary>[plan U3 · 3] Koşu sürerken ya da işaretlenirken (açılış koreografisi oynarken) — Stop henüz istenmedi
    /// (istendikten sonrası <see cref="WaitForBuildAt"/>).
    /// <para><b>Tasarımdan sapma:</b> tasarım "— F5 stops it." der; uygulamada F5 koşuyu durdurmaz, Esc durdurur. Tuşun
    /// adı elle yazılmaz, kısayol kataloğundan okunur (<see cref="ShortcutCatalog"/> — jestin tek kaynağı).</para></summary>
    public static string WaitForBuild { get; } =
        $"Available once the build finishes — {EscapeKey} stops it.";

    /// <summary>[perf Faz B · F-M1] Stop İSTENDİ, uçuştakiler bitiyor (<see cref="StopStage.StopNow"/>): bir sonraki Esc artık
    /// hard stop'tur ("Stop now" — uçuştakiler öldürülür), yani <see cref="WaitForBuild"/>'in "Esc stops it" ipucu yanlış
    /// olurdu. Cümle aynı tuşu söyler ama tırmanmayı da söyler.</summary>
    public static string WaitForBuildStopping { get; } =
        $"Available once the build stops — {EscapeKey} stops it now.";

    /// <summary>[perf Faz B · F-M1] Hard stop GİTTİ (<see cref="StopStage.Terminating"/>): Esc artık hiçbir şey yapmaz
    /// (<c>StopAsync</c> bu aşamada yutar) — ipucu YOKTUR.</summary>
    public const string WaitForBuildTerminating = "Available once the build stops.";

    /// <summary>[perf Faz B · F-M1] Koşu nedeninin metni Stop'un aşamasına göre: istenmedi → <see cref="WaitForBuild"/>,
    /// graceful gitti → <see cref="WaitForBuildStopping"/>, hard gitti → <see cref="WaitForBuildTerminating"/>.
    /// ARCHITECTURE §4.5'in kuralı: hiçbir yüzey, bir sonraki basış hard stop iken "Stop" demez — güncelleme kartı da dahil.</summary>
    public static string WaitForBuildAt(StopStage stage) => stage switch
    {
        StopStage.Stop => WaitForBuild,
        StopStage.StopNow => WaitForBuildStopping,
        StopStage.Terminating => WaitForBuildTerminating,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>Durduran tuşun adı kısayol kataloğundan okunur (jestin tek kaynağı) — hesaplanan özellik: statik başlatma
    /// sırasından bağımsız.</summary>
    private static string EscapeKey => ShortcutCatalog.Get(ShortcutId.Escape).Gestures[0];

    /// <summary>Restart kilidinin nedeni — sıra görev &gt; Sync &gt; koşu (plan U3); hiçbir iş yoksa <c>null</c>. Koşu cümlesi
    /// Stop'un aşamasını izler (<see cref="WaitForBuildAt"/>); aşama ZORUNLU parametredir ki VM'in bağlamayı unutması
    /// derlenmesin. Soruları VM sorar (<see cref="RunViewModel.UpdateRestartBlockedReason"/>), karar burada tek yerdedir.</summary>
    public static string? RestartBlockedReason(bool taskRunning, bool syncRunning, bool buildRunning, StopStage stopStage) =>
        taskRunning ? WaitForTask
        : syncRunning ? WaitForSync
        : buildRunning ? WaitForBuildAt(stopStage)
        : null;

    /// <summary>[design v1.23.0 §2.12] Restart ekranının başlığı (13px/600). Ürün adı YAZILMAZ, kimlikten okunur
    /// (<see cref="AppIdentity.Product"/>).</summary>
    public static string RestartHeading { get; } = "Updating " + AppIdentity.Product;

    /// <summary>Restart ekranının adım etiketi — sürerken yazıldığı için sonunda üç nokta (U+2026) taşır. [motor · Task 11
    /// · K6] Tek adım kapanıştır (<see cref="UpdateRestartStep"/>); kurulum ve açılış pencere kapandıktan sonra
    /// Update.exe'de olur, etiketleri yoktur.</summary>
    public static string RestartStepLabel(UpdateRestartStep step) => "Closing " + AppIdentity.Product + "…";
}
