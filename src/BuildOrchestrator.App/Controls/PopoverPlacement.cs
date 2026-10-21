using System.Windows;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Bir popover'ın hedefinin ALTINA, SOL kenarı hedefin sol kenarında düşmesinin SAF kararı — CSS'teki
/// <c>top: calc(100% + gap); left: 0</c>. <see cref="RowMenuPlacement"/> deseni: "ne karar verildi" burada, WPF'in
/// <see cref="PlacementMode.Custom"/> yerleşimi yalnız sonucu uygular.
///
/// <para><b>Neden Custom:</b> WPF'in <see cref="PlacementMode.Bottom"/>'u yatay hizayı
/// <see cref="SystemParameters.MenuDropAlignment"/>'a bırakır. Windows'un el tercihi "sağ el" olan makinede (Tablet PC
/// ayarı) bu değer <c>true</c>'dur ve popup'ın SAĞ kenarı hedefin sağ kenarına hizalanır — dar bir hedefin altındaki
/// geniş bir popover hedefin SOLUNA sarkar (ölçüldü: geliştirme makinesinde True). Custom yerleşim makinenin
/// ayarına bakmaz.</para>
/// </summary>
internal static class PopoverPlacement
{
    /// <summary>Popover'ın sol üst köşesinin, hedefin sol üst köşesine göre yeri: sol kenarlar hizalı, hedefin alt
    /// kenarından <paramref name="gap"/> px aşağıda.</summary>
    internal static Point BelowLeftEdge(Size targetSize, double gap) => new(0, targetSize.Height + gap);

    /// <summary><see cref="BelowLeftEdge"/>'in WPF geri çağrısı. Popup'ın kendi offset'leri (<c>offset</c>)
    /// KULLANILMAZ: boşluk tek yerden, <paramref name="gap"/>'ten gelir — popup'ta offset yazılmaz.</summary>
    internal static CustomPopupPlacementCallback BelowLeftEdgeCallback(double gap) =>
        (_, targetSize, _) => [new CustomPopupPlacement(BelowLeftEdge(targetSize, gap), PopupPrimaryAxis.None)];

    /// <summary>[Exit] Sağ kenarı hedefin sağ kenarında: popover hedefin altından SOLA doğru sarkar (CSS <c>top: calc(100% + gap);
    /// right: 0</c>) — kümenin sonundaki bir butonun kartı pencerenin dışına taşmaz.</summary>
    internal static Point BelowRightEdge(Size targetSize, Size popupSize, double gap) =>
        new(targetSize.Width - popupSize.Width, targetSize.Height + gap);

    /// <summary><see cref="BelowRightEdge"/>'in WPF geri çağrısı — <see cref="BelowLeftEdgeCallback"/>'in kuralıyla: boşluk tek yerden
    /// gelir, popup'ın offset'leri kullanılmaz.</summary>
    internal static CustomPopupPlacementCallback BelowRightEdgeCallback(double gap) =>
        (popupSize, targetSize, _) => [new CustomPopupPlacement(BelowRightEdge(targetSize, popupSize, gap), PopupPrimaryAxis.None)];
}
