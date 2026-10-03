using System.Windows;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [perf Faz A · A1] <b>"Yüzey gizli" sinyalinin TEK tanımı.</b> Pencere tepsiye inmişken (ya da hiç
/// gösterilmemişken) kimsenin görmediği ekran işi — koreografiler, finaller, layout/render'ı tetikleyen
/// bildirimler — yapılmamalı. Hepsinin okuduğu sinyal bu kalıtsal attached DP'dir: <c>MainWindow</c> onu
/// pencerenin kendisine yazar (<c>SetSurfaceHidden</c>), tüm torunlar miras alır, görünümler
/// <see cref="GetIsHidden"/> okur ve değişimini <c>OnPropertyChanged(DependencyPropertyChangedEventArgs)</c>
/// override'ında yakalar (GraphView'ün <c>Visibility</c> bekletmesiyle aynı idiom).
///
/// <para><b>Neden <c>IsVisible</c> değil:</b> headless süitte pencere hiç <c>Show()</c> edilmez (tepsi ikonu ve
/// kısayol kaydı kurulamaz) ve bağlı olmayan ağaçta <c>IsVisible</c> her zaman <c>false</c>'tur — ona bağlanan bir
/// kapı bütün mevcut kabuk testlerini "gizli" sanırdı. Varsayılan <c>false</c> (görünür) olduğundan mevcut
/// testler değişmez; gizli-mod testleri sinyali açıkça yazar.</para>
/// </summary>
internal static class HiddenSurface
{
    public static readonly DependencyProperty IsHiddenProperty = DependencyProperty.RegisterAttached(
        "IsHidden", typeof(bool), typeof(HiddenSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsHidden(DependencyObject d) => (bool)d.GetValue(IsHiddenProperty);

    public static void SetIsHidden(DependencyObject d, bool value) => d.SetValue(IsHiddenProperty, value);
}
