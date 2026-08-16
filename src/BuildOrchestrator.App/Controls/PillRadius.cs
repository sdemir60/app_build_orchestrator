using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [SCROLLBAR-HOVER] Scrollbar hap'ının içerlek'ini (<c>Padding</c>) boyanan hap'ın yarıçapına çevirir.
///
/// <para><b>Neden bir Converter:</b> BuildApp.jsx:38 hap'ı <c>border-radius:5px</c> + 3px şeffaf kenar +
/// <c>background-clip:padding-box</c> ile tanımlar — yani BOYANAN yarıçap <c>5 − kenar</c>'dır. Kenar sabitken
/// (3) bu tek bir sayıydı (2) ve şablona yazılabilirdi. Hover'da kenar 3'ten 1'e AKTIĞI için yarıçap da
/// akmalıdır: 2 → 4. WPF'te <c>CornerRadius</c>'ün animate edilebilir bir <c>AnimationTimeline</c>'ı YOKTUR,
/// ama <c>Padding</c>'in animasyonu her karede property-changed yayınlar ve buna bağlı bir Binding de her
/// karede yeniden değerlenir — yarıçap böylece BEDAVA ve KESİNTİSİZ akar.</para>
///
/// <para><b>Neden önemli:</b> yarıçapı sabit 4'te bırakmak bir kestirme DEĞİL, farklı bir şekildir — ölçüldü:
/// WPF <c>Border</c>'ı taşan yarıçapı yatayda kırpar ama dikeyde kırpmaz, yani 4px'lik hapta yarıçap 4
/// yarım daire değil ELİPS üretir (yarıçap 2'ye göre 18 piksel fark). Her iki genişlikte de tam kapsül
/// istiyorsak yarıçap kenarı takip etmek zorundadır (<c>ScrollBarStyleTests</c> ikisini de ölçer).</para>
/// </summary>
public sealed class PillRadius : IValueConverter
{
    /// <summary>BuildApp.jsx:38 <c>border-radius: 5px</c> — hap'ın DIŞ (border-box) yarıçapı. Boyanan
    /// (padding-box) yarıçap bundan kenar kalınlığı düşülerek elde edilir.</summary>
    public const double OuterPx = 5.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Thickness inset
            ? new CornerRadius(Math.Max(0.0, OuterPx - inset.Left))
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way only: the radius is derived from the inset.");
}
