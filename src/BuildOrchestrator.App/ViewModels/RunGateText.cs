namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [perf Faz B · son toparlama · F-M5 / F-O1] Run kapısının (<c>RunViewModel.CanRequestRun</c>) KAPALI olma nedenlerinin
/// metinlerinin TEK KAYNAĞI (<see cref="StopText"/> / <see cref="UpdateText"/> deseni: VM soru sorar, cümle burada durur;
/// testler de aynı sabiti okur — nedenin bir kopyası testte yaşamaz). Her cümle <c>"Build not started — {neden}."</c>
/// balonunun (<c>AppTrayIcon.BuildIgnoredBody</c>) içine girer: küçük harfle başlar, noktasızdır, tek başına okunur.
/// Koşulların kendisi ve SIRALARI <c>RunViewModel.WhyRunCannotStart</c>'tadır; kapı ile neden ayrışamaz.
/// </summary>
public static class RunGateText
{
    /// <summary>Motor hiç doğamadı ya da düştü — koşu başlatmak anlamsız.</summary>
    public const string EngineUnavailable = "the engine is not available";

    /// <summary>Tam çıkış bekleniyor: bekleyiş iş bitince kapanmak içindir, o sırada başlayan koşu çıkışı geciktirirdi.</summary>
    public const string ApplicationClosing = "the application is closing";

    /// <summary>Bir koşu uçuşta ya da başlıyor (işaretleme koreografisi dahil).</summary>
    public const string RunInFlight = "a run is already in flight";

    /// <summary>Bir Sync sürüyor (kendiliğinden olan, görünmeyen dahil).</summary>
    public const string SyncInProgress = "a Sync is in progress";

    /// <summary>Bakım kutusunun Clean'i sürüyor.</summary>
    public const string CleanInProgress = "a Clean is in progress";

    /// <summary>Bakım kutusunun Optimize'ı sürüyor.</summary>
    public const string OptimizeInProgress = "an Optimize is in progress";

    /// <summary>Branch chip'inden bir checkout (ve devrettiği Sync) sürüyor.</summary>
    public const string BranchSwitchInProgress = "a branch switch is in progress";

    /// <summary>Ana repo pull'u sürüyor.</summary>
    public const string PullInProgress = "a pull is in progress";

    /// <summary>Yukarıdakilerin hiçbirine girmeyen bir workspace işi — kapıyı sonradan girecek bir iş de kapalı tutsun diye.</summary>
    public const string WorkspaceTaskInProgress = "a workspace task is in progress";

    /// <summary>Repository root boş: workspace yok. Sync de kapalıdır, bu yüzden "Sync first" DENMEZ — doğru yol Settings'ten bir
    /// kök seçmektir (konsolun aynı durum için metni "Waiting for a workspace").</summary>
    public const string NoWorkspace = "no workspace yet — choose a repository root in Settings";

    /// <summary>Workspace var ama proje listesi yok (henüz Sync olmadı): Sync açıktır ve listeyi o üretir.</summary>
    public const string NoProjectList = "no project list yet — Sync first";
}
