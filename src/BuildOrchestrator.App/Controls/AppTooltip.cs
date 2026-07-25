using System.Windows;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T61] A13.2 (feasibility otoritesi, harfiyen): WPF'in <see cref="PlacementMode.Top"/>/<see cref="PlacementMode.Bottom"/>'u
/// popup'ı hedefin SOL kenarıyla hizalar — ORTALAMAZ. Merkezleme yalnız <see cref="PlacementMode.Custom"/> +
/// bir <see cref="CustomPopupPlacementCallback"/> ile mümkündür (bkz. <c>Resources/Controls.xaml</c>'deki
/// implicit <c>ToolTip</c> stili, <c>Placement="Custom"</c> + bu sınıfın <c>PlacementTop/Bottom/Left/Right</c>
/// alanlarını tüketir).
///
/// <para><b>Side nasıl bildirilir:</b> <see cref="SideProperty"/>, DsChrome.Prefix'in TextBox üzerinde kendi
/// kendini tetiklediği desenin AYNISIYLA, EXPLICIT bir <c>&lt;ToolTip&gt;</c> öğesinin KENDİSİNE
/// (<c>controls:AppTooltip.Side="Bottom"</c>) ilan edilir — stilin <c>Style.Triggers</c>'ı bunu doğrudan okur.
/// Otomatik sarılan düz-metin tooltip'ler (Side verilmemiş) varsayılan <see cref="Top"/> kalır.</para>
/// </summary>
public static class AppTooltip
{
    public const string Top = "Top";
    public const string Bottom = "Bottom";
    public const string Left = "Left";
    public const string Right = "Right";

    // BuildApp.jsx Tooltip.jsx (_ds_bundle.js:1296/:1300/:1304/:1308) — dört yönde de hedefle arada 6px boşluk.
    private const double Gap = 6.0;

    public static readonly DependencyProperty SideProperty = DependencyProperty.RegisterAttached(
        "Side", typeof(string), typeof(AppTooltip), new PropertyMetadata(Top));

    public static void SetSide(DependencyObject d, string value) => d.SetValue(SideProperty, value);
    public static string GetSide(DependencyObject d) => (string)d.GetValue(SideProperty);

    /// <summary>Varsayılan (Top) merkezleme: popup, hedefin ÜSTÜNDE yatayda ortalanır, 6px boşlukla.</summary>
    public static CustomPopupPlacement[] Placement(Size popupSize, Size targetSize, Point offset)
        => PlacementForSide(Top, popupSize, targetSize, offset);

    /// <summary>Dört yönün ortak matematiği. <c>internal</c> — dört yönün ayrı ayrı doğrulanabilmesi için
    /// (Placement(3 argüman)'ın public imzası brief'in pinlediği TEK yön olan Top'a ayrılmıştır).</summary>
    internal static CustomPopupPlacement[] PlacementForSide(string side, Size popupSize, Size targetSize, Point offset)
    {
        double x, y;
        switch (side)
        {
            case Bottom:
                x = (targetSize.Width - popupSize.Width) / 2;
                y = targetSize.Height + Gap;
                break;
            case Left:
                x = -(popupSize.Width + Gap);
                y = (targetSize.Height - popupSize.Height) / 2;
                break;
            case Right:
                x = targetSize.Width + Gap;
                y = (targetSize.Height - popupSize.Height) / 2;
                break;
            case Top:
            default:
                x = (targetSize.Width - popupSize.Width) / 2;
                y = -(popupSize.Height + Gap);
                break;
        }
        return [new CustomPopupPlacement(new Point(x + offset.X, y + offset.Y), PopupPrimaryAxis.None)];
    }

    // Resources/Controls.xaml'in {x:Static} ile tükettiği hazır delegate'ler — x:Static yalnız statik
    // alan/property çözer, bir metot grubunu XAML'de delegate'e dönüştüremez.
    public static readonly CustomPopupPlacementCallback PlacementTop = Placement;
    public static readonly CustomPopupPlacementCallback PlacementBottom = (p, t, o) => PlacementForSide(Bottom, p, t, o);
    public static readonly CustomPopupPlacementCallback PlacementLeft = (p, t, o) => PlacementForSide(Left, p, t, o);
    public static readonly CustomPopupPlacementCallback PlacementRight = (p, t, o) => PlacementForSide(Right, p, t, o);
}
