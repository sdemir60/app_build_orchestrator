namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [design v1.19.0 §2.9 · prototip <c>LAYER_PLACEHOLDERS</c>] Settings'teki katman kartlarının ad ve desen
/// input'larında görünen ÖRNEKLER — ürün adı taşımayan standart bir katman iskeleti. Değer DEĞİL placeholder'dır:
/// <c>Add layer</c> boş satır ekler ve kullanıcı kendi adını/desenini yazar; kayıtlı katman yokken taslak boştur
/// (ön-dolum yoktur).
///
/// <para>Çift satıra değil SATIR İNDEKSİNE aittir (<see cref="For"/>) ve altıdan sonra başa döner — sürükle-bırak
/// sonrası kart yeni indeksinin çiftini gösterir. Liste başka hiçbir yerde tekrarlanmaz.</para>
/// </summary>
public static class LayerPlaceholders
{
    /// <summary>Çiftler, indeks sırasıyla.</summary>
    public static readonly IReadOnlyList<(string Name, string Pattern)> Pairs =
    [
        ("Core", @"^MyApp\.(Core|Common)\."),
        ("Infrastructure", @"^MyApp\.(Data|Infrastructure)\."),
        ("Domain", @"^MyApp\.Domain\."),
        ("Services", @"^MyApp\.Services\."),
        ("Api", @"\.Api$"),
        ("Client", @"^MyApp\.(Web|Client|Mobile)\."),
    ];

    /// <summary><paramref name="index"/>. satırın çifti (liste sonunda başa döner).</summary>
    public static (string Name, string Pattern) For(int index) => Pairs[index % Pairs.Count];
}
