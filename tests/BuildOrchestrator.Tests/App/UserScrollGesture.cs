using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Testlerin "kullanıcı GERÇEKTEN kaydırdı" jestini kaydırma çubuğundan üretme yolu — üretimin dinlediği ham girdi kanalının
/// (<c>UserScrollSignal</c>) ta kendisi tetiklenir, panelin iç durumuna elle dokunulmaz.
///
/// <para>Çubuk olayı (<see cref="ScrollBar.ScrollEvent"/>) yalnız kullanıcı etkileşiminde doğar ve pencere gerekmeden yükseltilebilir —
/// ölçülmüş ama gösterilmemiş bir görünümde de. Gerçek pencerede tekerlek (<see cref="MouseInput.Wheel"/>) ve tuşlar (<see cref="KeyInput"/>)
/// de yükseltilir (<c>RealizedNarrative</c>, kanal testleri) — üç kanal aynı sinyale bağlıdır. [B2 fix 3 · madde 2] Kural dikeydir: yalnız
/// DİKEY çubuk jesttir; yatay çubuk (ve ←/→, yatay tekerlek) yana bir adımdır — direksiyonu almaz, animasyonu durdurmaz.</para>
/// </summary>
internal static class UserScrollGesture
{
    /// <summary>[B2 fix 2 · madde 2] Kullanıcının <paramref name="scrollHost"/>'un (konsolun editörü, stream'in ve listenin ScrollViewer'ı) DİKEY
    /// çubuğunu sürüklemesi: çubuk şablondan bulunur, olay ondan baloncuklanır — gerçek kablaj yolu (çubuk → ScrollViewer → panel).</summary>
    public static void DragVerticalBarOf(DependencyObject scrollHost) => Drag(BarOf(scrollHost, Orientation.Vertical));

    /// <summary>[B2 fix 3 · madde 2] Aynı jest YATAY çubuktan — yana bir adım.</summary>
    public static void DragHorizontalBarOf(DependencyObject scrollHost) => Drag(BarOf(scrollHost, Orientation.Horizontal));

    private static ScrollBar BarOf(DependencyObject scrollHost, Orientation orientation) =>
        DsResources.Descendants(scrollHost).OfType<ScrollBar>().Single(bar => bar.Orientation == orientation);

    /// <summary>Başlığı sürüklemek: <see cref="ScrollBar.ScrollEvent"/> çubuğun kendisinden — programatik <c>ScrollToVerticalOffset</c> onu yaymaz,
    /// aradığımız ayrım budur.</summary>
    private static void Drag(ScrollBar bar) =>
        bar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 0) { RoutedEvent = ScrollBar.ScrollEvent });
}
