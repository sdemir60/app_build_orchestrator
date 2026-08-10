namespace BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// <see cref="ProjectSkippedEvent.Reason"/>'ın taşıyabileceği dört yalın gerekçe — TEK doğruluk kaynağı.
/// Contracts'ta yaşar çünkü hem Supervisor (<c>RunCoordinator</c>) hem Core (<c>ReadySetScheduler</c> —
/// Core zaten Contracts'a referans verir) YAZAR, App
/// (<c>StreamText</c>/<c>RunViewModel.Stream</c>) OKUR — üç katmanda da aynı literal iki kez tanımlanırsa
/// (kopya YASAK, CLAUDE.md) biri değişip diğeri unutulduğunda stream ile decision.log sessizce ayrışır.
/// Her değer YALINDIR: "skipped — " öneki BURADA YOKTUR, onu basan katman (decision.log formülü,
/// <c>StreamText.Skipped</c>) kendi önekini ekler — aksi halde decision.log çift önek basardı
/// ("skipped — skipped — up to date").
/// </summary>
public static class SkipReasons
{
    /// <summary>İncremental karar: kaynak değişmedi, proje güncel.</summary>
    public const string UpToDate = "up to date";

    /// <summary>[cycles] Proje, Cycles koşusunun kapsamı (SCC'ler + transitif upstream'leri) DIŞINDA kaldı.</summary>
    public const string OutOfCycleScope = "not needed by a dependency cycle";

    /// <summary>Build/Rebuild/Continue/RetryFailed modunda bir SCC üyesi — turlar yalnız Cycles modunda koşar.</summary>
    public const string InDependencyCycle = "in dependency cycle";

    /// <summary>[cycle rounds/Task 8] SCC daha önce aynı bileşik imzada yakınsamadı, bir daha tur harcamadan pre-skip edildi.</summary>
    public const string CycleNonConvergent = "cycle did not converge at this signature";
}
