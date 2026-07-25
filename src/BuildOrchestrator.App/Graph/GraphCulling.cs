using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [G2/It-5] Viewport cull'un SAF aritmetiği — WPF nesnesine hiç dokunmaz, tamamen birim-testlenebilir
/// (<see cref="GraphLayout"/>/<see cref="GraphCamera"/> ile aynı disiplin).
///
/// <para><b>Neden cull:</b> G1'in ölçümü darboğazın çizim DEĞİL <b>nesne kurulumu</b> olduğunu gösterdi
/// (<c>SetGraph</c>'ın görsel-ağaç kurulumu toplamın %64-72'si, WPF <c>Measure/Arrange</c> %28-36'sı, saf layout
/// aritmetiği %0,03) ve ölçekleme LİNEER. Tek kaldıraç düğüm başına kurulan nesne sayısıdır; en büyük kazanç ise
/// hiç kurulmayan nesnedir. Bu yüzden cull <c>Visibility.Collapsed</c> DEĞİL <b>tembel materyalizasyondur</b>:
/// görünür dünya dikdörtgenine değmeyen düğüm/kenarın UIElement ağacı hiç kurulmaz, görünür alana girdiğinde
/// kurulur (<c>Collapsed</c> nesneyi yine kurar ve asıl maliyeti hiç düşürmezdi).</para>
///
/// <para><b>Tek yönlüdür (materialize-only):</b> bir kez kurulan görsel geri sökülmez. Sökme, sürmekte olan
/// nabız/sönme animasyonlarını ve seçim durumunu yeniden kurmayı gerektirirdi; kazanç ise ilk realize
/// maliyetinde DEĞİL yalnız bellekte olurdu. Kamera otomatik ve odaklı olduğu için materyalize edilen küme
/// pratikte grafın gezilen şeridiyle sınırlı kalır.</para>
/// </summary>
public static class GraphCulling
{
    /// <summary>Görünür dikdörtgene her yönde eklenen pay — bir satır aralığı (96px). Kamera geçişi sırasında
    /// (460ms) kenardan giren düğümün "pop-in" etmemesi için gereklidir.</summary>
    public const double MarginPx = GraphLayout.RowHeight;

    /// <summary>Kameranın o an gösterdiği DÜNYA dikdörtgeni (+ <see cref="MarginPx"/> pay). Kamera bir
    /// <c>RenderTransform</c>'dur: dünya noktası <c>p</c> ekranda <c>p·scale + t</c>'ye düşer, dolayısıyla
    /// ekrandaki <c>[0,viewport]</c> kutusunun dünya karşılığı <c>(0−t)/scale … (viewport−t)/scale</c>'dir.
    /// Ölçek veya viewport sıfırsa (henüz ölçülmemiş panel) <see cref="Rect.Empty"/> döner — çağıran o turda
    /// hiçbir şey materyalize etmez, ilk gerçek <c>SizeChanged</c>'de yeniden sorar.</summary>
    public static Rect VisibleWorldRect(Size viewport, CameraTransform camera)
    {
        if (camera.Scale <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return Rect.Empty;

        var rect = new Rect(
            -camera.Tx / camera.Scale,
            -camera.Ty / camera.Scale,
            viewport.Width / camera.Scale,
            viewport.Height / camera.Scale);
        rect.Inflate(MarginPx, MarginPx);
        return rect;
    }

    /// <summary>Bir düğüm hücresinin dünya sınırları: etiket hücresi genişliğinde (<see cref="GraphLayout.NodeCellWidth"/>),
    /// karenin üst kenarından etiketin altına kadar. Merkez <see cref="GraphLayout"/>'un verdiği KARE merkezidir.</summary>
    public static Rect NodeBounds(Point center) => new(
        center.X - GraphLayout.NodeCellWidth / 2,
        center.Y - GraphLayout.NodeSize / 2,
        GraphLayout.NodeCellWidth,
        GraphLayout.NodeSize + GraphLayout.LabelGap + GraphLayout.LabelHeight);

    /// <summary>Bir kenarın dünya sınırları. Kübik bezier HER ZAMAN kontrol noktalarının dışbükey zarfının
    /// içindedir, dolayısıyla dört noktanın sınır kutusu eğrinin GÜVENLİ bir üst kümesidir (eğriyi örneklemeye
    /// gerek yok).</summary>
    public static Rect EdgeBounds(Point from, Point to)
    {
        var curve = GraphLayout.EdgeCurve(from, to);
        var rect = new Rect(curve.Start, curve.End);
        rect.Union(curve.Control1);
        rect.Union(curve.Control2);
        return rect;
    }
}
