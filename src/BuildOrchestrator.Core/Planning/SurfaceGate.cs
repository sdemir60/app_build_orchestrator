namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

/// <summary>[D7] Kapının hükmü: <see cref="Build"/> ⇒ proje derlenir; <see cref="Unchanged"/> ⇒ hiçbir doğrudan bağımlılığın
/// yüzeyi değişmedi, proje "up to date" atlanır.</summary>
public enum SurfaceGateVerdict { Build, Unchanged }

/// <summary>
/// [D5/D7] Yüzey kapısı — "bir bağımlılığın API yüzeyi değişmediyse bağımlısını derleme". Döngü içinde aynı soruyu
/// <see cref="CycleMemberNeed"/> sorar; bu sınıf sıradan projeler içindir. SAF: I/O, process, saat YOK; yüzeyleri
/// çağıran okur. Koordinatör yalnız uygular (<c>RunCoordinator.TrySkipWhileDependencySurfacesUnchanged</c>).
///
/// <para><b>Aday.</b> Safe plan projeyi "imza değişti" ile kirli görür, Fast plan (frozen-upstream: upstream'lerin
/// defterdeki imzası, cascade yok — <c>IncrementalPlanner</c>) ise "güncel" der: kendi terimi (içerik + configuration)
/// değişmemiş, kanıtı yerinde, beslenen kopyası sağlam; kirlilik yalnız bir upstream'den gelir. Configuration değişimi
/// Fast imzasını da değiştirdiği için aday olmaz. Doğrudan bağımlılığı olmayan proje ve döngü üyesi aday değildir.
/// Fast'in "güncel"i son sonucun başarı olduğunu GARANTİ ETMEZ: defterin "kaynak geri alındı" kuralı kanıtlı bir
/// hatadan sonra geri alınan kaynağı da güncel okur — bu yüzden son sonuç kararda ayrıca sorulur. <b>Bilinen sınır:</b>
/// upstream daha önceki bir koşuda derlenmişse (Stop sonrası, satırdan Build) Fast imzası defterdeki yeni upstream
/// imzasını görür ve "değişti" der — bağımlı bir kez koşulsuz derlenir (güvenli yön).</para>
///
/// <para><b>Karar (sırası gelince).</b> Kaydın son sonucu başarı değilse ya da kayıtta yüzey yoksa DERLENİR
/// (<see cref="CycleMemberNeed"/> kural iii'nin aynası: başarısız sonuç hiçbir zaman güvenilmez). Sonra her doğrudan
/// bağımlılık için: bu koşuda patladıysa, kayıtta yüzeyi yoksa, şimdiki yüzeyi okunamıyorsa ya da kayıttakinden farklıysa
/// DERLENİR; hepsi aynıysa atlanır. Atlanan ya da güncel bağımlılığın yüzeyi de DİSKTEN okunur — "atlandı ⇒ değişmedi"
/// varsayımı yoktur: bağımlılık satırdan derlenmiş ya da kesilmiş bir koşuda yenilenmiş olabilir.</para>
/// </summary>
public static class SurfaceGate
{
    /// <summary>Kapı hangi koşuda uygulanır: defteri dinleyen (<see cref="IncrementalModes"/>) TAM koşular. Satırdan tetiklenen
    /// koşunun hedefi koşulsuz derlenir, Rebuild her şeyi derler.</summary>
    public static bool AppliesTo(RunMode mode, bool scopedRun) => IncrementalModes.Includes(mode) && !scopedRun;

    /// <summary>Atlama satırının ayrıntısı: <c>D: skipped — up to date (no dependency surface changed)</c>.</summary>
    public const string UnchangedDetail = "no dependency surface changed";

    /// <summary>Deftere yazılabilir yüzey: okunamayan (null) ve olmayan (<see cref="ApiSurfaceHash.Absent"/>) dosya YAZILMAZ —
    /// "yok == yok" eşleşip çıktı yokken bağımlıyı atlatırdı. TEK normalizasyon yeri.</summary>
    public static string? Persistable(string? hash) => hash is null || hash == ApiSurfaceHash.Absent ? null : hash;

    /// <summary>Sırası gelen adayın hükmü (bkz. sınıf özeti "Karar"). Kaydın son sonucu başarı olmalı; her doğrudan bağımlılık
    /// bu koşuda terminal olmalı (Failed ⇒ derle), kayıtta yüzeyi bulunmalı ve şimdiki yüzeyi kayıttakiyle aynı olmalı; biri
    /// tutmazsa derlenir. Kayıt bozuksa (null üretici/özet, aynı üretici iki kez) güvenilmez ⇒ derlenir.</summary>
    /// <param name="completed">Bu koşuda terminal olan projeler ve sonuçları (scheduler'ın Completed'ı).</param>
    /// <param name="record">Projenin koşu başındaki kaydı; bağımlılık yüzeyleri <see cref="BuildState.DependencySurfaces"/>'tadır.</param>
    /// <param name="surfaceOf">Bağımlılık → (kanıt dosyası, ŞİMDİKİ yüzey özeti); dosya türetilemiyorsa null, okunamıyorsa
    /// hash null. Çağıran diskten okur ve koşu boyunca önbellekler.</param>
    public static SurfaceGateVerdict Decide(IReadOnlyList<string> directDependencies,
        IReadOnlyDictionary<string, BuildResult> completed, BuildState? record,
        Func<string, (string File, string? Hash)?> surfaceOf)
    {
        ArgumentNullException.ThrowIfNull(directDependencies);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(surfaceOf);
        if (record is not { LastResult: BuildResult.Succeeded, DependencySurfaces: { Count: > 0 } recorded })
            return SurfaceGateVerdict.Build;
        if (directDependencies.Count == 0) return SurfaceGateVerdict.Build; // "hiçbiri değişmedi" vakum doğruluğuyla atlatmaz
        var recordedByDep = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recorded)
        {
            if (r?.Producer is null || r.Hash is null) return SurfaceGateVerdict.Build; // bozuk kayıt
            if (!recordedByDep.TryAdd(r.Producer, r.Hash)) return SurfaceGateVerdict.Build;
        }
        foreach (string dep in directDependencies)
        {
            if (!completed.TryGetValue(dep, out var result) || result == BuildResult.Failed) return SurfaceGateVerdict.Build;
            if (!recordedByDep.TryGetValue(dep, out string? seen) || Persistable(seen) is null) return SurfaceGateVerdict.Build;
            if (surfaceOf(dep) is not { } now || Persistable(now.Hash) is not { } current) return SurfaceGateVerdict.Build;
            if (!string.Equals(seen, current, StringComparison.Ordinal)) return SurfaceGateVerdict.Build;
        }
        return SurfaceGateVerdict.Unchanged;
    }

    /// <summary>Kapıdan geçecek projeler (bkz. sınıf özeti "Aday"). <paramref name="fast"/> düğümleri <paramref name="safe"/>
    /// ile aynı id kümesidir (aynı plan, iki bağlama).</summary>
    public static IReadOnlySet<string> CandidateIds(BuildPlan safe, BuildPlan fast)
    {
        ArgumentNullException.ThrowIfNull(safe);
        ArgumentNullException.ThrowIfNull(fast);
        var fastById = fast.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        return safe.Nodes
            .Where(n => n is { WillBuild: true, WillBuildReason: WillBuildReason.SignatureChanged, InCycle: false }
                        && n.Dependencies.Count > 0
                        && fastById.GetValueOrDefault(n.Id) is { WillBuild: false, WillBuildReason: WillBuildReason.UpToDate })
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
