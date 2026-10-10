namespace BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// <see cref="ProjectSkippedEvent.Reason"/>'ın taşıyabileceği beş yalın gerekçe — TEK doğruluk kaynağı.
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

    /// <summary>Build/Rebuild modunda bir SCC üyesi — turlar yalnız Cycles modunda koşar.</summary>
    public const string InDependencyCycle = "in dependency cycle";

    /// <summary>[B1] Hükmü verilmiş (NoProgress, CapReached) bir SCC'de tur 1'de taşınmış ama son turda okuduğu kardeş
    /// yüzeyi bayat kalan üye: bu koşuda hiç derlenmedi, taşıdığı kayıt atıldı ve kanıtsız geçersizlendi
    /// (<c>RunCoordinator.ReportDiscardedCarry</c>). App satırı bir sonraki Sync'in cevabıyla (never built) çizer ve
    /// sayfasında bunu anlatır (<c>ConsoleEmptyState</c>).
    /// <para>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-09] Eski anlam: SCC daha önce aynı bileşik imzada yakınsamadığı
    /// için tur harcanmadan pre-skip edildi; o pre-skip kalkınca (yakınsamama hafızası yalnız RAPORLAR) motor bu
    /// gerekçeyi hiç yaymıyordu. Bayat taşınan üye o arada <c>succeeded (0ms)</c> raporlanıyordu — derlenmeyen projeye
    /// "succeeded" yazmak yanlıştı; sözcük yeni anlamıyla yeniden kullanılır (IPC'ye yeni alan/olay eklenmedi).</para></summary>
    public const string CycleNonConvergent = "cycle did not converge at this signature";

    /// <summary>Koşullu proje (dep-issue notlu, imzası değişmemiş): kayıtlı kök bağımlılıklarının hepsi hâlâ hatalı —
    /// yeniden derlemek aynı bayat çıktıya link'lemekten başka bir şey yapmazdı (bkz. <c>ConditionalRebuild</c>).</summary>
    public const string DependencyStillFailing = "dependency still failing";
}
