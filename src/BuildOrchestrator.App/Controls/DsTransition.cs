using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS'in 120ms renk geçişlerinin ŞABLON TARAFINDAKİ tek kablajı. Bir <c>Style</c>/<c>ControlTemplate</c>
/// state trigger'ı buradaki bir attached property'yi (<c>AnimatedBackground</c> / <c>AnimatedForeground</c> /
/// <c>AnimatedBorderBrush</c> / <c>AnimatedTranslateX</c>) hedef TOKEN fırçasına ayarlar; bu sınıf da öğenin
/// gerçek <c>Background</c>/<c>Foreground</c>/<c>BorderBrush</c>'ına ŞABLON-LOKAL (donmamış) bir
/// <see cref="SolidColorBrush"/> yerleştirip O KOPYANIN rengini <see cref="MotionTokens.TransitionColor"/> ile
/// yumuşatır.
///
/// <para><b>Neden bu dolaylılık (A13.2):</b> Tokens.xaml'deki fırçalar PAYLAŞILIR ve donmuştur (frozen) —
/// doğrudan animasyon hem imkânsızdır (<c>InvalidOperationException</c>) hem de aynı token'ı kullanan HER
/// öğeyi birlikte oynatırdı. Trigger'ın doğrudan <c>Background</c>'a yazması ise ANİ SIÇRAMA olurdu.</para>
///
/// <para><b>Neden kod-tarafı, saf-XAML <c>Storyboard</c> değil (T60 Step 1 kararı):</b>
/// <c>ControlTemplate.Triggers</c> içindeki bir <c>Storyboard</c>, şablon mühürlenirken (Seal) DONDURULMAK
/// zorundadır; <c>{DynamicResource Duration.Fast}</c> taşıyan bir zaman çizelgesi dondurulamaz ve şablon
/// yükleme anında patlar. <c>{StaticResource}</c> ise reduced-motion'ın canlı sıfırlamasını hiç görmezdi.
/// Ölçüm/kanıt: <c>MotionResourcesTests</c>.</para>
///
/// <para><b>Kullanım:</b> temel değer <c>Style</c>'ın Setter'ında, durumlar <c>Style.Triggers</c>'da verilir —
/// üçü de <c>{DynamicResource Brush.X}</c> ile, çünkü bir Setter değerinin DynamicResource olması meşrudur ve
/// canlıdır. İlk atama animate EDİLMEZ (kuruluş), sonraki her değişim 120ms'de akar.</para>
/// </summary>
public static class DsTransition
{
    // Öğe başına, hedef DP başına TEK lokal fırça. (Attached property, çünkü hedefler paylaşılan şablon
    // öğeleridir; bir sözlük alanı taşıyacak sahibimiz yok.)
    private static readonly DependencyProperty LocalBrushesProperty = DependencyProperty.RegisterAttached(
        "LocalBrushes", typeof(Dictionary<DependencyProperty, SolidColorBrush>), typeof(DsTransition));

    public static readonly DependencyProperty AnimatedBackgroundProperty = DependencyProperty.RegisterAttached(
        "AnimatedBackground", typeof(Brush), typeof(DsTransition),
        new PropertyMetadata(null, (d, e) => Apply(d, BackgroundTarget(d), e.NewValue as Brush)));

    public static readonly DependencyProperty AnimatedForegroundProperty = DependencyProperty.RegisterAttached(
        "AnimatedForeground", typeof(Brush), typeof(DsTransition),
        new PropertyMetadata(null, (d, e) => Apply(d, ForegroundTarget(d), e.NewValue as Brush)));

    public static readonly DependencyProperty AnimatedBorderBrushProperty = DependencyProperty.RegisterAttached(
        "AnimatedBorderBrush", typeof(Brush), typeof(DsTransition),
        new PropertyMetadata(null, (d, e) => Apply(d, BorderBrushTarget(d), e.NewValue as Brush)));

    /// <summary>[T60] Switch başparmağının 120ms'lik yatay kayması (_ds_bundle.js:901
    /// <c>transform: translateX(12px)</c>). Öğeye lokal bir <see cref="TranslateTransform"/> kurar ve
    /// <c>X</c>'ini <see cref="MotionTokens.TransitionDouble"/> ile sürer.</summary>
    /// <remarks>Varsayılan <see cref="double.NaN"/>'dır, 0 DEĞİL: şablon başlangıç konumunu <c>0</c> olarak
    /// ilan eder ve varsayılan da 0 olsaydı ETKİN DEĞER DEĞİŞMEZ, callback hiç çalışmaz ve öğe WPF'in
    /// varsayılan (donmuş, identity) <c>RenderTransform</c>'uyla kalırdı — sonraki geçişler de imkânsız olurdu.</remarks>
    public static readonly DependencyProperty AnimatedTranslateXProperty = DependencyProperty.RegisterAttached(
        "AnimatedTranslateX", typeof(double), typeof(DsTransition),
        new PropertyMetadata(double.NaN, OnAnimatedTranslateXChanged));

    public static void SetAnimatedBackground(DependencyObject d, Brush? value) => d.SetValue(AnimatedBackgroundProperty, value);
    public static Brush? GetAnimatedBackground(DependencyObject d) => (Brush?)d.GetValue(AnimatedBackgroundProperty);

    public static void SetAnimatedForeground(DependencyObject d, Brush? value) => d.SetValue(AnimatedForegroundProperty, value);
    public static Brush? GetAnimatedForeground(DependencyObject d) => (Brush?)d.GetValue(AnimatedForegroundProperty);

    public static void SetAnimatedBorderBrush(DependencyObject d, Brush? value) => d.SetValue(AnimatedBorderBrushProperty, value);
    public static Brush? GetAnimatedBorderBrush(DependencyObject d) => (Brush?)d.GetValue(AnimatedBorderBrushProperty);

    public static void SetAnimatedTranslateX(DependencyObject d, double value) => d.SetValue(AnimatedTranslateXProperty, value);
    public static double GetAnimatedTranslateX(DependencyObject d) => (double)d.GetValue(AnimatedTranslateXProperty);

    /// <summary>Zemin fırçasının öğe tipine göre gerçek hedefi. <see cref="Border"/> ve <see cref="Panel"/>
    /// birer <see cref="Control"/> DEĞİLDİR — sıra bu yüzden önemlidir.</summary>
    private static DependencyProperty? BackgroundTarget(DependencyObject d) => d switch
    {
        Border => Border.BackgroundProperty,
        Panel => Panel.BackgroundProperty,
        Control => Control.BackgroundProperty,
        Shape => Shape.FillProperty,   // ikon/nokta gibi çizimlerde "zemin" = dolgu
        _ => null,
    };

    private static DependencyProperty? ForegroundTarget(DependencyObject d) => d switch
    {
        Control => Control.ForegroundProperty,
        TextBlock => TextBlock.ForegroundProperty,
        Shape => Shape.StrokeProperty,  // konturlu ikonda "ön plan" = kontur
        // Border/Panel: metin rengi kalıtımla iner (ContentPresenter'ın Foreground'u yoktur).
        _ => TextElement.ForegroundProperty,
    };

    private static DependencyProperty? BorderBrushTarget(DependencyObject d) => d switch
    {
        Border => Border.BorderBrushProperty,
        Control => Control.BorderBrushProperty,
        Shape => Shape.StrokeProperty,
        _ => null,
    };

    private static void Apply(DependencyObject d, DependencyProperty? target, Brush? wanted)
    {
        if (target is null || d is not FrameworkElement host) return;
        // Token'lar SolidColorBrush'tır; başka bir fırça tipi gelirse (gradient vb.) geçiş yapılamaz —
        // doğru davranış onu OLDUĞU GİBİ yazmaktır, sessizce yutmak değil.
        if (wanted is not SolidColorBrush solid) { d.SetValue(target, wanted); return; }

        var brushes = (Dictionary<DependencyProperty, SolidColorBrush>?)d.GetValue(LocalBrushesProperty);
        if (brushes is null)
        {
            brushes = [];
            d.SetValue(LocalBrushesProperty, brushes);
        }

        if (!brushes.TryGetValue(target, out var local))
        {
            // İLK atama: kuruluştur, animate EDİLMEZ. Kopya donmamıştır — animasyon hedefi budur (A13.2).
            local = new SolidColorBrush(solid.Color);
            brushes[target] = local;
            d.SetValue(target, local);
            return;
        }

        // Şablon yeniden uygulanmışsa (ör. Style değişimi) öğe bizim kopyamızı kaybetmiş olabilir — geri koy.
        if (!ReferenceEquals(d.GetValue(target), local)) d.SetValue(target, local);
        MotionTokens.TransitionColor(host, local, solid.Color);
    }

    private static void OnAnimatedTranslateXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        double to = (double)e.NewValue;
        if (double.IsNaN(to)) return;

        if (element.RenderTransform is not TranslateTransform transform || transform.IsFrozen)
        {
            // İlk kuruluş: animasyonsuz konumlan (hover/checked geçişleri bundan SONRA akar).
            element.RenderTransform = new TranslateTransform(to, 0);
            return;
        }
        if (d is not FrameworkElement host) { transform.X = to; return; }
        MotionTokens.TransitionDouble(host, transform, TranslateTransform.XProperty, to);
    }
}
