using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [T63] Tek bir graf düğümünün WPF görselleri. Statü değiştiğinde bu parçalar YERİNDE güncellenir — şablon
/// yeniden kurulmaz (kenar geometrileri gibi düğüm ağacı da koşu boyunca sabittir).
///
/// <para><b>İki ayrı opaklık taşıyıcısı:</b> <see cref="Cell"/> ilk açılıştaki katman stagger'ının hedefi,
/// <see cref="Body"/> ise seçim sönmesinin hedefidir. Aynı elemanda olsalardı stagger animasyonu (HoldEnd)
/// sönme değerini kilitlerdi.</para>
/// </summary>
internal sealed class GraphNodeVisual
{
    public required GraphNode Model { get; set; }
    /// <summary>Canvas'a yerleştirilen dış hücre — katman reveal (opacity + 5px yukarıdan) animasyonunun hedefi.</summary>
    public required Grid Cell { get; init; }
    /// <summary>Tıklanabilir gövde (kare + etiket) — seçim sönmesinin (%25) hedefi.</summary>
    public required StackPanel Body { get; init; }
    /// <summary>26px, 4px köşe yarıçaplı KARE (K4/DS; README'deki "daire" ifadesi DS koduyla çelişir).
    /// discovered'ta kesikli çerçeve — WPF Border dashed desteklemediği için Rectangle.</summary>
    public required Rectangle Square { get; init; }
    /// <summary>Seçiliyken görünen amber halka (DS: 2px outline, 2px offset).</summary>
    public required Rectangle SelectionRing { get; init; }
    /// <summary>13px paket ikonu (lucide "package" geometrisi; tam ikon seti T64).</summary>
    public required Path Icon { get; init; }
    /// <summary>Kare altındaki mono 10px kısa ad — <c>TextFormattingMode=Ideal</c> LOKAL override taşır.</summary>
    public required TextBlock Label { get; init; }
    /// <summary>Dep-hata rozeti kabı (13px, sağ üst köşe) — yalnız <c>HasDepIssue</c> iken görünür.</summary>
    public required Grid Badge { get; init; }
    public required Ellipse BadgeCircle { get; init; }
    public required Path BadgeTriangle { get; init; }
    /// <summary>Düğümün graf koordinatlarındaki merkezi (kamera hedefi).</summary>
    public required Point Center { get; init; }
}

/// <summary>[T63] Tek bir kenarın görseli + en son uygulanan stil (akan kenarlar paylaşılan dash clock'una bağlanır).</summary>
internal sealed class GraphEdgeVisual
{
    public required GraphEdge Model { get; init; }
    public required Path Path { get; init; }
    public EdgeStyle? Style { get; set; }
}
