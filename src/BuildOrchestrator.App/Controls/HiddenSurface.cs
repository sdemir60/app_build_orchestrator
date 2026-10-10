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
/// <para><b>Kalıtımın dışında kalan tek yüzey: popup içeriği.</b> Build menüsü ActionBar'ın popup'ında durur ve popup
/// görsel ağacın parçası DEĞİLDİR — pencereden miras almaz. <c>ActionBar</c> kalıtsal değişimi kendi
/// <c>OnPropertyChanged</c>'inde yakalayıp menüye açıkça yazar; sinyali yazan yer pencerenin kendisi ve bu aktarımdır.</para>
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

    /// <summary>
    /// "Yüzey görünür oldu" yönü: bir <c>OnPropertyChanged</c> override'ında gelen değişim bu sinyalin gizliden görünüre
    /// dönüşüdür. Görünümlerin dönüş yakalamasının (bayat kaldıysa tek geçişte modele yetişmek) TEK okuma kuralı: sinyalin
    /// anlamı (kalıtsal DP + yön) burada durur, her görünüm yalnız kendi "bayat kaldım mı" bayrağını ayrıca sorar.
    /// </summary>
    public static bool BecameVisible(DependencyPropertyChangedEventArgs e) =>
        e.Property == IsHiddenProperty && !(bool)e.NewValue;
}
