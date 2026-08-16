using System.Windows;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS şablonlarının WPF'te KARŞILIĞI OLMAYAN kabuk (chrome) özellikleri. Hepsi attached property'dir,
/// çünkü hedefleri hazır WPF tipleridir (<c>Button</c>, <c>TextBox</c>, <c>Rectangle</c>) — bu tipleri
/// türetmek yalnız bir alan taşımak için gereksiz bir kontrol hiyerarşisi doğururdu.
/// </summary>
public static class DsChrome
{
    /// <summary>
    /// Kabuk köşe yarıçapı. <see cref="System.Windows.Controls.Control"/>'ün <c>CornerRadius</c>'u YOKTUR;
    /// DS'in şablonları ise onu değişken tutmak zorundadır — split button'ın iki yarısı AYNI şablonu kullanıp
    /// yalnız köşelerinde ayrışır (BuildApp.jsx:1592-1596: <c>borderTopRightRadius: 0</c> / <c>…LeftRadius: 0</c>).
    /// </summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(DsChrome),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);
    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);

    /// <summary>DS <c>Input</c>'un <c>placeholder</c>'ı (BuildApp.jsx:837 "Search branches…"). WPF
    /// <see cref="System.Windows.Controls.TextBox"/>'ında yoktur; şablon bunu boş metinde görünen bir
    /// katman olarak çizer.</summary>
    public static readonly DependencyProperty WatermarkProperty = DependencyProperty.RegisterAttached(
        "Watermark", typeof(string), typeof(DsChrome), new PropertyMetadata(null));

    public static void SetWatermark(DependencyObject d, string? value) => d.SetValue(WatermarkProperty, value);
    public static string? GetWatermark(DependencyObject d) => (string?)d.GetValue(WatermarkProperty);

    /// <summary>DS <c>Input</c>'un <c>prefix</c> yuvası (_ds_bundle.js:749 / :763-772): metin alanı
    /// <c>paddingLeft 26</c>'ya kayar, ikon <c>left: 8</c>'de dikey ortalanır.</summary>
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.RegisterAttached(
        "Prefix", typeof(object), typeof(DsChrome), new PropertyMetadata(null));

    public static void SetPrefix(DependencyObject d, object? value) => d.SetValue(PrefixProperty, value);
    public static object? GetPrefix(DependencyObject d) => d.GetValue(PrefixProperty);

    /// <summary>DS <c>Input</c>'un <c>invalid</c> bayrağı (_ds_bundle.js:717): kenar
    /// <c>status-fail-border</c>'a döner ve focus'ta amber'e GEÇMEZ.</summary>
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached(
        "IsInvalid", typeof(bool), typeof(DsChrome), new PropertyMetadata(false));

    public static void SetIsInvalid(DependencyObject d, bool value) => d.SetValue(IsInvalidProperty, value);
    public static bool GetIsInvalid(DependencyObject d) => (bool)d.GetValue(IsInvalidProperty);

    /// <summary>
    /// Focus halkasının öğe DIŞINDAKİ boşluğu (README:44 "2px halka, offset 1px"). WPF'in
    /// <c>FocusVisualStyle</c>'ı bir ADORNER'dır ve öğenin sınırlarına birebir oturur; halkayı dışarı itmek
    /// NEGATİF margin ister ve gereken değer <c>-(offset + kalınlık/2)</c> ARİTMETİĞİDİR (kontur çizgi
    /// ORTALANIR). XAML aritmetik yapamaz, token'ı literal olarak yeniden yazmak ise YASAK — bu yüzden
    /// hesap tek yerde, burada. Kalınlık öğenin kendi <see cref="Shape.StrokeThickness"/>'ından okunur
    /// (o da <c>{DynamicResource Size.FocusRingWidth}</c>'tir), offset bu property ile verilir.
    ///
    /// <para><b>Varsayılan 0 OLAMAZ</b> (ölçüldü): <c>Ds.Input</c> halkasını <c>FocusRingOffset="0"</c> ile
    /// kurar — kenarın hemen dışında, boşluksuz. Bir DP'ye VARSAYILANINA eşit bir değer verildiğinde WPF
    /// değişiklik geri çağrısını hiç çalıştırmaz; hesap koşmadığı için halka ne dışarı itiliyor ne köşesi
    /// yuvarlanıyordu (kutunun köşeleri oval, halkası köşeliydi). <see cref="double.NaN"/> "verilmedi"
    /// demektir; 0 dahil her gerçek offset artık bir değişikliktir.</para>
    /// </summary>
    public static readonly DependencyProperty FocusRingOffsetProperty = DependencyProperty.RegisterAttached(
        "FocusRingOffset", typeof(double), typeof(DsChrome),
        new PropertyMetadata(double.NaN, OnFocusRingOffsetChanged));

    public static void SetFocusRingOffset(DependencyObject d, double value) => d.SetValue(FocusRingOffsetProperty, value);
    public static double GetFocusRingOffset(DependencyObject d) => (double)d.GetValue(FocusRingOffsetProperty);

    private static void OnFocusRingOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Rectangle ring) return;
        ApplyFocusRingOutset(ring);
        // StrokeThickness de {DynamicResource} ile gelir ve ÇÖZÜLME SIRASI garanti değildir — yükleme
        // tamamlanınca bir kez daha hesapla (o an her iki değer de kesin çözülmüştür).
        ring.Loaded -= OnFocusRingLoaded;
        ring.Loaded += OnFocusRingLoaded;
    }

    private static void OnFocusRingLoaded(object sender, RoutedEventArgs e) => ApplyFocusRingOutset((Rectangle)sender);

    private static void ApplyFocusRingOutset(Rectangle ring)
    {
        double offset = GetFocusRingOffset(ring);
        if (double.IsNaN(offset)) return;   // offset verilmemiş: bu Rectangle bir focus halkası değil
        double outset = offset + ring.StrokeThickness / 2;
        ring.Margin = new Thickness(-outset);
        // Halka, sardığı kabuğun köşesini İZLEMELİDİR ve dışarı çıktığı kadar da yuvarlanır. WPF'te
        // Rectangle.RadiusX bir DOUBLE'dır; `Radius.Sm` ise bir CornerRadius token'ıdır — köşe değeri
        // ondan OKUNUR (literal olarak yeniden yazılmaz). Kaynak çözülemezse (headless) keskin kalır.
        double baseRadius = ring.TryFindResource("Radius.Sm") is CornerRadius r ? r.TopLeft : 0;
        ring.RadiusX = ring.RadiusY = baseRadius > 0 ? baseRadius + outset : 0;
    }
}
