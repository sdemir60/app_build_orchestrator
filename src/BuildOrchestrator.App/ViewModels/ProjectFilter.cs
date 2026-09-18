using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [C2] Proje listesi görünürlük kuralı: serbest metin sorgusu (proje ADINDA case-insensitive alt-dize)
/// <b>VE</b> statü chip'leri. Chip anahtarları küçük harftir; tüm UI metni İngilizce (<see cref="Label"/>).
///
/// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §2.7-4/§3.5]</b> Filtre eskiden TEK bir chip'ti (<c>string?</c>):
/// ikinci bir chip'e basmak birinciyi düşürüyordu. Artık bir <b>küme</b>dir ve seçili chip'ler <b>VEYA</b> ile
/// birleşir; arama ile hâlâ VE'lenir. Ayrıca <c>dep</c> ve <c>cycle</c> chip'leri TEK bir <see cref="Warn"/>
/// chip'inde birleşti — v1.11.0 turuncuyu UI'dan çıkardı ve döngü ile dep-issue tek amber uyarı üçgeniyle ifade
/// edilir; iki ayrı filtre iki ayrı renk ima ediyordu.</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.7]</b> Statü chip'leri eskiden KOŞUNUN sonucunu seçiyordu
/// (<c>succeeded</c> · <c>failed</c> · <c>skipped</c>; ✓ + ✗ = "bu koşuda derlenenler") ve <c>building</c> kuyruğu da
/// kapsıyordu. Artık <b>durum filtreleridir</b>: <see cref="Current"/> "Up to date" (yeşil — güncel çıktı, bu koşuda
/// atlanan güncel satır ve bu koşunun başarıları), <see cref="Stale"/> "To build" (gri — kanıtsız hata dahil),
/// <see cref="Failed"/> "Failed" (yalnız kırmızı görünen), <see cref="Building"/> yalnız ŞU AN derlenen. Atlandı
/// chip'i kalktı: atlanmak bir durum değildir, satır kendi durumunun rengini taşır; koşunun "N skipped" özeti
/// şeritte kalır. Üyelik satırın GÖSTERDİĞİ görsel durumdan okunur (<see cref="StateKey"/>) — sayaç
/// (<see cref="RunCounters"/>) AYNI kovayı sayar, bir chip'e basınca listede kalan satır sayısı chip'in
/// rozetidir.</para>
/// </summary>
public static class ProjectFilter
{
    public const string Building = "building", Current = "current", Stale = "stale", Failed = "failed",
                        Warn = "warn";

    /// <summary>Chip'lerin bardaki SIRASI — birleşik etiket (<see cref="ChipLabel"/>) bu sırayı izler, böylece
    /// aynı küme her zaman aynı metni verir (küme sırası belirsizdir).</summary>
    public static readonly IReadOnlyList<string> Order = [Building, Current, Stale, Failed, Warn];

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
        // [design v1.11.0 §2.7-4] warn = döngü üyeliği ∪ dependency issue — TEK birleşik uyarı kanalı. Dependency issue
        // kümülatiftir (R-D144 · spec 2026-09-18 §1-15): bu koşunun listesi ∪ defterdeki bekleyen bağımlılık notu.
        if (filter == Warn) return row.InCycle || row.HasDepIssue;
        return string.Equals(StateKey(row.VisualStatus), filter, StringComparison.Ordinal);
    }

    /// <summary>[design v1.20.0 §2.7] Satırın GÖSTERDİĞİ görsel durumun chip anahtarı — sayaç
    /// (<see cref="RunCounters.From"/>) ve filtre AYNI soruyu buradan sorar (kopya YASAK). <c>building</c> ŞU AN
    /// derlenendir (<see cref="ProjectRowViewModel.IsCompiling"/> → <see cref="VisualStatus.Building"/>; kuyruk ve
    /// sırasını bekleyen döngü üyesi Queued görünür, dahil DEĞİL); durum anahtarları
    /// <see cref="VisualStatuses.StateOf"/>'tan. Kuyruk · işaretleme dalgası · karar yokluğu hiçbir chip'te
    /// değildir → <c>null</c>.</summary>
    internal static string? StateKey(VisualStatus shown) => shown == VisualStatus.Building
        ? Building
        : VisualStatuses.StateOf(shown) switch
        {
            StandingStatus.Current => Current,
            StandingStatus.Stale => Stale,
            StandingStatus.Failed => Failed,
            _ => null,
        };

    /// <summary>Chip'in görünen İngilizce adı. Durum chip'lerinin sözcüğü satırın ekran-okuyucu sözcüğüyle AYNI
    /// kaynaktandır (<see cref="StatusGlyph.LabelFor(VisualStatus)"/>) — chip "Up to date" diyorsa listede
    /// kalan satırlar da "Up to date" duyurulur.</summary>
    public static string Label(string filter) => filter switch
    {
        Building => StatusGlyph.LabelFor(VisualStatus.Building),
        Current => StatusGlyph.LabelFor(VisualStatus.Current),
        Stale => StatusGlyph.LabelFor(VisualStatus.Stale),
        Failed => StatusGlyph.LabelFor(VisualStatus.Failed),
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
    /// Pasifken hepsi <c>Brush.TextPrimary</c>'dir (çağıranın işi). [design v1.20.0 §2.7] ✓ yeşil, ✗ kırmızı,
    /// ○ (derlenecek) nötr gri.</summary>
    public static string ActiveBrushKey(string filter) => filter switch
    {
        Current => "Brush.StatusSuccessText",
        Failed => "Brush.StatusFailText",
        Stale => "Brush.StatusSkippedText",
        _ => "Brush.AmberText", // building + warn: ikisi de amber ailesindendir
    };
}
