using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

// [D1 fold — B3 review] GraphStatus enum'ı nötr Controls namespace'ine taşındı (Controls/GraphStatus.cs) —
// StatusGlyph'in graf-dışı ilk tüketicisi D1'dir. Buradaki kullanımlar için yukarıdaki using yeterlidir.

/// <summary>
/// [quiet] Graf düğümü — kimlik (proje Id'si), görünen ad, katman indeksi ve statü. Hepsi bu kadardır.
///
/// <para><b>Kimlik <see cref="Id"/>'dir, <see cref="Name"/> DEĞİL.</b> Ad benzersiz değildir: iki ayrı proje
/// aynı <c>AssemblyName</c>'i üretebilir (ör. bir harici kart, ana repo kökünde zaten duran bir solution'ın
/// ikinci bir kopyasını getirir). Yerleşim ada göre anahtarlanırsa ikinci düğüm birincinin konumunu ezer:
/// bant hücreyi AYIRIR ama kimse oraya oturmaz, yani düğüm görünmez olur ve yerinde bir boşluk kalır
/// (ölçüldü: 191 projelik gerçek bir çalışma alanında 7 çakışan ad → 7 görünmez düğüm + 7 boşluk). Ad yalnız
/// bir ETİKETTİR (tooltip, seçim etiketi, ekran-okuyucu adı); eşleşen her yer <see cref="Id"/> kullanır.</para>
///
/// <para><b>Ne taşımadığı da bir karardır.</b> v1.3.0 §2.3'te düğümün üstünde ad etiketi yoktur (ad hover
/// tooltip'i ve seçim etiketiyle verilir) ve graf içi dep-issue rozeti kaldırılmıştır (dep bilgisi liste
/// kartlarında yaşar). Bu yüzden kısa-ad öneki ve <c>HasDepIssue</c> bayrağı bu kayıttan SÖKÜLDÜ — grafta
/// hiçbir okuyucuları kalmamıştı.</para>
///
/// <para>[design v1.11.0 §2.3 "Renk kuralı"] Düğüm TEK bir renk kanalı taşır: <see cref="Visual"/>. Node
/// border'ı, zemini ve içindeki küp AYNI görsel durumdan beslenir — ayrı bir "plan" ya da "cycle" çekirdeği
/// YOKTUR ve grafta uyarı üçgeni de yoktur (döngü, liste satırındaki tek amber üçgende yaşar).</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> v1.7.0'da düğüm ÜÇ kanal taşıyordu: <c>Status</c> "bu koşuda ne oldu"
/// (kenar), <c>WillBuild</c> "sıradaki Build buna dokunacak mı" ve <c>InCycle</c> "kodda döngü var mı" (son
/// ikisi birlikte çekirdeği boyardı, turuncu amber'i eziyordu). v1.11.0 ikisini de kaldırdı; iki alan da bu
/// kayıttan SÖKÜLDÜ çünkü grafta okuyucuları kalmadı.</para>
///
/// <para><see cref="Status"/> KALIR: beads animasyonunun kapısı (Building) ve ekran-okuyucu adı ondan gelir —
/// ikisi de bir RENK sorusu değildir.</para>
/// </summary>
/// <param name="Id">Düğümün KİMLİĞİ — proje Id'si (tam csproj yolu). Yerleşim, slot haritası, kenarlar,
/// seçim, hover, filtre ve işaretleme kümelerinin tamamı bunu anahtarlar.</param>
/// <param name="Name">Görünen ad (<c>AssemblyName</c>) — BENZERSİZ DEĞİLDİR, yalnız etiket olarak kullanılır.</param>
/// <param name="Visual">Tek renk kanalı — <see cref="VisualStatuses"/> tablosuyla boyanır.</param>
public sealed record GraphNode(string Id, string Name, int Layer, GraphStatus Status,
    VisualStatus Visual = VisualStatus.Discovered)
{
    /// <summary>[D5] Ortak öneği atılmış kısa ad. Grafın kendisi ARTIK kullanmaz (§2.3: node üstü etiket
    /// yok) ama proje adını dar bir yerde gösteren diğer yüzeyler kullanır: liste kartının dep-tooltip'i
    /// (<c>ProjectRow</c>) ve şeritteki building chip'leri (<c>StickyRibbon</c>). Önek HARDCODED değildir —
    /// <see cref="CommonDotPrefix"/> ile workspace proje adlarından türetilir; TEK önek otoritesi odur.</summary>
    public static string ShortLabel(string fullName, string prefix) =>
        prefix.Length > 0 && fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..] : fullName;

    /// <summary>[D5] Verilen adların en uzun ortak NOKTA-SINIRLI öneki (sondaki nokta DAHİL). Örn. tümü
    /// <c>OSYS.</c> altındaysa → <c>"OSYS."</c>; ortak nokta-segmenti yoksa → <c>""</c>. Bir adın TAMAMI asla önek
    /// olamaz (her adın son/yaprak segmenti hariç tutulur) — aksi halde etiket boşalırdı. Hardcode edilmiş
    /// <c>"OSYS."</c>'in yerini alan TEK önek otoritesidir.</summary>
    public static string CommonDotPrefix(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0) return "";

        string[]? common = null;
        foreach (var name in names)
        {
            var parts = name.Split('.');
            int usable = parts.Length - 1; // yaprak segment (son ad) önek olamaz
            if (usable <= 0) return "";    // noktasız bir ad varsa hiç ortak önek olamaz
            if (common is null) { common = parts[..usable]; continue; }

            int max = Math.Min(common.Length, usable);
            int i = 0;
            while (i < max && string.Equals(common[i], parts[i], StringComparison.Ordinal)) i++;
            if (i == 0) return "";
            common = common[..i];
        }
        return common is { Length: > 0 } ? string.Join('.', common) + "." : "";
    }
}

/// <summary>[T63] Bağımlılık kenarı: <paramref name="From"/> (bağımlılık) → <paramref name="To"/> (bağımlı proje);
/// prototype <c>GRAPH.edges</c> ile AYNI yön (yukarıdan aşağı).
/// <para>Uçlar <see cref="GraphNode.Id"/>'dir, ad DEĞİL — çakışan adlarda kenar yanlış düğüme bağlanırdı.</para></summary>
public sealed record GraphEdge(string From, string To);
