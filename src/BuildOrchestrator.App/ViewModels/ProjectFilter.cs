namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [C2] Proje listesi görünürlük kuralı: serbest metin sorgusu (proje ADINDA case-insensitive alt-dize)
/// VE bir statü chip'i (birbiriyle AND'lenir) — BuildApp.jsx:465-470 portu. Chip anahtarları prototiple
/// birebir küçük harftir. Tüm UI metni İngilizce (<see cref="Label"/>).
/// </summary>
public static class ProjectFilter
{
    public const string Building = "building", Succeeded = "succeeded", Failed = "failed",
                        Skipped = "skipped", Dep = "dep", Cycle = "cycle";

    /// <summary>Bir satır, sorgu + filtre altında görünür mü? Sorgu boşsa ad kontrolü atlanır; filtre boşsa
    /// statü kontrolü atlanır (ikisi de boş → her satır görünür).</summary>
    public static bool Matches(ProjectRowViewModel row, string? query, string? filter)
    {
        var q = query?.Trim();
        if (!string.IsNullOrEmpty(q) &&
            row.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            return false; // ad-yalnız alt-dize; yol/id'ye BAKILMAZ

        if (string.IsNullOrEmpty(filter)) return true;
        if (filter == Dep) return row.HasDepIssue;                                    // statüden bağımsız
        if (filter == Cycle) return row.InCycle;                                      // yapısal, kalıcı
        if (filter == Building) return row.State is ProjectRowState.Started or ProjectRowState.Pending; // queued dahil
        return string.Equals(StatusKey(row.State), filter, StringComparison.Ordinal); // succeeded/failed/skipped
    }

    /// <summary>Chip'in görünen İngilizce adı; "dep" → "Dependency issues".</summary>
    public static string Label(string filter) => filter switch
    {
        Building => "Building",
        Succeeded => "Succeeded",
        Failed => "Failed",
        Skipped => "Skipped",
        Dep => "Dependency issues",
        Cycle => "In a dependency cycle",
        _ => filter,
    };

    // Prototipteki eng.p[name].status karşılığı (build-data.js): satır durumunun chip anahtarı.
    private static string StatusKey(ProjectRowState state) => state switch
    {
        ProjectRowState.Started => Building,
        ProjectRowState.Succeeded => Succeeded,
        ProjectRowState.Failed => Failed,
        ProjectRowState.Skipped => Skipped,
        _ => "queued", // Pending
    };
}
