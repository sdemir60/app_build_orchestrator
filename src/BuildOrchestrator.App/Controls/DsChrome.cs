using System.Windows;
using System.Windows.Media;
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

    /// <summary>
    /// [design v1.17.0 §9 "Alt barda tek hover dili"] Bir <c>Button</c>'ın DS <c>active</c> (aria-pressed)
    /// durumuna eşdeğer görsel durumu. <c>ToggleButton.IsChecked</c>'ın <see cref="System.Windows.Controls.Button"/>
    /// karşılığı YOKTUR — tek tüketicisi koşan bir işi gösteren bar düğmeleridir (Sync/Clean/Optimize/Resolve:
    /// <c>MaintenanceBox.SetBusy</c> / <c>ActionBar.RefreshSyncBusy</c>). Komut kapısı uçuşta disabled olsa da
    /// (ikinci bir tıklama anlamsızdır) düğme AKTİF görünmeye devam eder ve bar'ın tek hover dilinde
    /// "açık/aktif kontrol" muamelesi görür (<c>Ds.Bar.Button.Secondary.Sm</c>/<c>Ds.Bar.IconButton</c>'ın
    /// <c>IsActive</c> tetikleyicileri) — bu yüzden bu bayrağın hover eşleniği <c>IsEnabled</c> ŞARTI ARAMAZ.
    /// </summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(DsChrome), new PropertyMetadata(false));

    public static void SetIsActive(DependencyObject d, bool value) => d.SetValue(IsActiveProperty, value);
    public static bool GetIsActive(DependencyObject d) => (bool)d.GetValue(IsActiveProperty);

    /// <summary>
    /// [design v1.17.0 §9 fix round 1 · I-2] Gerçek <c>IsMouseOver</c>'ın YERİNE geçen hover sinyali — TEK
    /// tüketicisi <see cref="IsActiveProperty"/> ile aynı düğmelerdir (koşan Sync/Clean/Optimize/Resolve).
    /// WPF, <c>IsEnabled=False</c> bir öğeyi hit-test'ten TAMAMEN dışlar (aynı gerekçeyle
    /// <c>ToolTipService.ShowOnDisabled</c> vardır — disabled bir düğmenin tooltip'i normalde hiç açılmaz);
    /// bu düğmelerin komut kapısı iş sürerken KAPALIDIR (<c>CanExecute=false</c> → WPF'in kendi coerce'ı
    /// <c>IsEnabled</c>'ı false yapar), yani düğmenin KENDİ <c>IsMouseOver</c>'ı bu pencerede GERÇEK fare
    /// hareketiyle asla true olmaz — stil bunu okusaydı "aktif kontrol hover'ı" hiçbir zaman ERİŞİLEMEZ kalırdı.
    ///
    /// <para>Çözüm HER ZAMAN etkin bir sarmalayıcı (şeffaf bir <c>Border</c>, düğmeyle AYNI sınırlarda) —
    /// <see cref="WireHoverProxy"/> onun <c>MouseEnter</c>/<c>MouseLeave</c>'ini bu bayrağa yansıtır. Doğru
    /// mekanizma "hit arkaya düşer" DEĞİLDİR: WPF mouse-over'ı en üstteki (disabled) öğeye DEĞİL, en yakın
    /// ETKİN ATAYA yönlendirir — burada o ata sarmalayıcı Border'ın KENDİSİDİR (düğmenin ebeveyni), çünkü
    /// düğme o an disabled'dır.</para>
    /// </summary>
    public static readonly DependencyProperty IsHoverProxyProperty = DependencyProperty.RegisterAttached(
        "IsHoverProxy", typeof(bool), typeof(DsChrome), new PropertyMetadata(false));

    public static void SetIsHoverProxy(DependencyObject d, bool value) => d.SetValue(IsHoverProxyProperty, value);
    public static bool GetIsHoverProxy(DependencyObject d) => (bool)d.GetValue(IsHoverProxyProperty);

    /// <summary>
    /// <paramref name="wrapper"/>'ın (her zaman etkin, hit-test edilebilir bir ata — bkz. <see cref="IsHoverProxyProperty"/>)
    /// <c>MouseEnter</c>/<c>MouseLeave</c>'ini <paramref name="target"/>'ın <see cref="IsHoverProxyProperty"/>'sine
    /// yansıtır. Çağıran, <paramref name="target"/>'ı <paramref name="wrapper"/>'ın TEK ve TAM sınırlı çocuğu
    /// yapmaktan sorumludur — aksi halde sarmalayıcının hit alanı düğmeninkiyle örtüşmez ve sinyal yanlış anda
    /// (ör. komşu bir düğmenin üstündeyken) tetiklenir.
    /// </summary>
    public static void WireHoverProxy(FrameworkElement wrapper, DependencyObject target)
    {
        wrapper.MouseEnter += (_, _) => SetIsHoverProxy(target, true);
        wrapper.MouseLeave += (_, _) => SetIsHoverProxy(target, false);
    }

    /// <summary>
    /// [design v1.17.0 §9 fix round 1 · I-3] Bir sayaç CHIP'inin ikon-ÖZEL rengi — chip'in kendi
    /// <c>Control.Foreground</c>'undan BAĞIMSIZDIR. Tek tüketicisi Σ'dır: DS'in <c>Chip</c> bileşeninde
    /// ikon span'i rest'te <c>text-dim</c>, chip'in geri kalanı (etiket/değer) ise <c>text-secondary</c>/
    /// <c>text-primary</c>'dir — Σ'nin chip'i hiç aktif olmadığından ve <c>Ds.Chip</c>'in REST Foreground'u
    /// <c>text-secondary</c> olduğundan, ikonu doğrudan chip'in Foreground'una bağlamak (<c>BoundToForeground</c>
    /// deseni) rest rengini YANLIŞ değere (text-dim yerine text-secondary) taşırdı. Bu ayrı, PAYLAŞILMAYAN
    /// kanal rest'i text-dim'de tutup yalnız nötr hover'da text-primary'ye taşımayı (<c>Ds.Bar.Chip</c>'in
    /// <see cref="DsTransition.AnimatedIconForegroundProperty"/> setter'ları) mümkün kılar.
    /// </summary>
    public static readonly DependencyProperty IconForegroundProperty = DependencyProperty.RegisterAttached(
        "IconForeground", typeof(Brush), typeof(DsChrome), new PropertyMetadata(null));

    public static void SetIconForeground(DependencyObject d, Brush? value) => d.SetValue(IconForegroundProperty, value);
    public static Brush? GetIconForeground(DependencyObject d) => (Brush?)d.GetValue(IconForegroundProperty);

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
