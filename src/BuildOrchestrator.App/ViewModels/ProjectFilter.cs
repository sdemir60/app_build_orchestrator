namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [C2] Proje listesi görünürlük kuralı: serbest metin sorgusu (proje ADINDA case-insensitive alt-dize)
/// <b>VE</b> statü chip'leri. Chip anahtarları prototiple birebir küçük harftir; tüm UI metni İngilizce
/// (<see cref="Label"/>).
///
/// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §2.7-4/§3.5]</b> Filtre eskiden TEK bir chip'ti (<c>string?</c>):
/// ikinci bir chip'e basmak birinciyi düşürüyordu. Artık bir <b>küme</b>dir ve seçili chip'ler <b>VEYA</b> ile
/// birleşir (✓ + ✗ = "bu koşuda derlenenler"); arama ile hâlâ VE'lenir. Ayrıca <c>dep</c> ve <c>cycle</c>
/// chip'leri TEK bir <see cref="Warn"/> chip'inde birleşti — v1.11.0 turuncuyu UI'dan çıkardı ve döngü ile
/// dep-issue tek amber uyarı üçgeniyle ifade edilir; iki ayrı filtre iki ayrı renk ima ediyordu.</para>
/// </summary>
public static class ProjectFilter
{
    public const string Building = "building", Succeeded = "succeeded", Failed = "failed",
                        Skipped = "skipped", Warn = "warn";

    /// <summary>Chip'lerin bardaki SIRASI — birleşik etiket (<see cref="ChipLabel"/>) bu sırayı izler, böylece
    /// aynı küme her zaman aynı metni verir (küme sırası belirsizdir).</summary>
    public static readonly IReadOnlyList<string> Order = [Building, Succeeded, Failed, Skipped, Warn];

    /// <summary>Boş küme — "filtre yok". Tek örnek: her temizlemede yeni bir küme ayırmak gereksizdir.</summary>
    public static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Bir satır, sorgu + filtre kümesi altında görünür mü? Sorgu boşsa ad kontrolü atlanır; küme
    /// boşsa statü kontrolü atlanır (ikisi de boş → her satır görünür). Küme çok elemanlıysa <b>VEYA</b>:
    /// satır seçili chip'lerden HERHANGİ birine uyuyorsa görünür.</summary>
    public static bool Matches(ProjectRowViewModel row, string? query, IReadOnlySet<string>? filters)
    {
        ArgumentNullException.ThrowIfNull(row);
        var q = query?.Trim();
        if (!string.IsNullOrEmpty(q) &&
            row.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            return false; // ad-yalnız alt-dize; yol/id'ye BAKILMAZ

        if (filters is null || filters.Count == 0) return true;
        foreach (string f in filters) if (MatchesOne(row, f)) return true;
        return false;
    }

    private static bool MatchesOne(ProjectRowViewModel row, string filter)
    {
        // [design v1.11.0 §2.7-4] warn = döngü üyeliği ∪ dependency issue — TEK birleşik uyarı kanalı.
        if (filter == Warn) return row.InCycle || row.HasDepIssue;
        if (filter == Building) return row.State is ProjectRowState.Started or ProjectRowState.Pending; // queued dahil
        return string.Equals(StatusKey(row.State), filter, StringComparison.Ordinal); // succeeded/failed/skipped
    }

    /// <summary>Chip'in görünen İngilizce adı (design-v1.11.0 <c>FILTER_LABELS</c>, BuildApp.jsx:578).</summary>
    public static string Label(string filter) => filter switch
    {
        Building => "Building",
        Succeeded => "Succeeded",
        Failed => "Failed",
        Skipped => "Skipped",
        Warn => "Warnings",
        _ => filter,
    };

    /// <summary>[design v1.11.0 §2.7-4] PROJECTS başlığındaki kaldırılabilir chip'in etiketi: seçili kümeyi
    /// <c>" + "</c> ile listeler (BuildApp.jsx:2250). Küme boşsa chip hiç çizilmez → <c>null</c>.</summary>
    public static string? ChipLabel(IReadOnlySet<string>? filters)
    {
        if (filters is null || filters.Count == 0) return null;
        return string.Join(" + ", Order.Where(filters.Contains).Select(Label));
    }

    /// <summary>[design v1.11.0 §2.7-4] "Aktif çip KENDİ statü renginde yanar" — chip değerinin fırça anahtarı.
    /// Pasifken hepsi <c>Brush.TextPrimary</c>'dir (çağıranın işi).</summary>
    public static string ActiveBrushKey(string filter) => filter switch
    {
        Succeeded => "Brush.StatusSuccessText",
        Failed => "Brush.StatusFailText",
        Skipped => "Brush.StatusSkippedText",
        _ => "Brush.AmberText", // building + warn: ikisi de amber ailesindendir
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
