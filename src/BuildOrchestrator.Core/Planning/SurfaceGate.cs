namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [D5/D7] Yüzey kapısı — "bir bağımlılığın API yüzeyi değişmediyse bağımlısını derleme". Döngü içinde aynı soruyu
/// <see cref="CycleMemberNeed"/> sorar; bu sınıf sıradan projeler içindir. SAF: I/O, process, saat YOK; yüzeyleri
/// çağıran okur. Koordinatör yalnız uygular (<c>RunCoordinator.TrySkipWhileDependencySurfacesUnchanged</c>).
///
/// <para><b>Aday.</b> Safe plan projeyi "imza değişti" ile kirli görür, Fast plan (frozen-upstream: upstream'lerin
/// defterdeki imzası, cascade yok — <c>IncrementalPlanner</c>) ise "güncel" der: kendi terimi (içerik + configuration)
/// değişmemiş, kanıtı yerinde, beslenen kopyası sağlam, son sonucu başarı; kirlilik yalnız bir upstream'den gelir.
/// Configuration değişimi Fast imzasını da değiştirdiği için aday olmaz. Doğrudan bağımlılığı olmayan proje ve döngü
/// üyesi aday değildir. <b>Bilinen sınır:</b> upstream daha önceki bir koşuda derlenmişse (Stop sonrası, satırdan Build)
/// Fast imzası defterdeki yeni upstream imzasını görür ve "değişti" der — bağımlı bir kez koşulsuz derlenir (güvenli yön).</para>
///
/// <para><b>Karar (sırası gelince).</b> Her doğrudan bağımlılık için: bu koşuda patladıysa, kayıtta yüzeyi yoksa,
/// şimdiki yüzeyi okunamıyorsa ya da kayıttakinden farklıysa DERLENİR; hepsi aynıysa atlanır. Atlanan ya da güncel
/// bağımlılığın yüzeyi de DİSKTEN okunur — "atlandı ⇒ değişmedi" varsayımı yoktur: bağımlılık satırdan derlenmiş ya da
/// kesilmiş bir koşuda yenilenmiş olabilir.</para>
/// </summary>
public static class SurfaceGate
{
    /// <summary>Kapı hangi koşuda uygulanır: defteri dinleyen (<see cref="IncrementalModes"/>) TAM koşular. Satırdan tetiklenen
    /// koşunun hedefi koşulsuz derlenir, Rebuild her şeyi derler.</summary>
    public static bool AppliesTo(RunMode mode, bool scopedRun) => IncrementalModes.Includes(mode) && !scopedRun;

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
