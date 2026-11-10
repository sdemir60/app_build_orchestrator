using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Kayan bir alanın kenarındaki SOLMA ŞERİDİ (gölge): kendi kutusunu <see cref="Fill"/> renginden (yaslandığı
/// kenarda tam opak) aynı rengin saydamına inen dikey bir rampayla boyar. <see cref="Fill"/> alanın zemini
/// olduğunda altından geçen içerik zemine karışarak solar; şerit boşken zeminden ayırt edilemez. Tıklamayı ve
/// tekerleği almaz — ikisi de alttaki içeriğe geçer.
///
/// <para><b>Neden içeriğe maske değil, üstüne şerit:</b> aynı rampa kayan içeriğin sunucusuna
/// <c>OpacityMask</c> olarak da konabilirdi; canlı pencerede ölçüldü — maske içeriği ayrı bir katmana çizer ve
/// katmanın kenar piksellerini 1px dışarı taşırır: What's new'in sol kolonundaki her sürüm numarasının, tarihin ve
/// <c>INSTALLED</c> çipinin solunda silik bir iz kalıyordu (ekran dışı çizimde görünmez). Bu şerit ara katman
/// açmaz, altındaki çizime dokunmaz.</para>
///
/// <para>Rampanın iki ucu AYNI RGB'dir, yalnız alfa değişir: saydam siyaha inen bir rampa (<c>Transparent</c>)
/// koyu zeminde ortada zeminden koyu bir bant bırakırdı.</para>
/// </summary>
public sealed class EdgeFade : FrameworkElement
{
    /// <summary>Şeridin rengi — altından geçen içeriğin zemini. KALITILIR: kabuk (<see cref="ModalDialog"/>) onu
    /// çerçevesinin zemininden bir kez verir, içindeki her şerit oradan alır. Düz renk değilse şerit çizilmez.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        nameof(Fill), typeof(Brush), typeof(EdgeFade),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush? GetFill(DependencyObject element) => (Brush?)element.GetValue(FillProperty);
    public static void SetFill(DependencyObject element, Brush? value) => element.SetValue(FillProperty, value);

    /// <summary>Bir <see cref="ScrollViewer"/>'ın içeriğini üst ve alt kenar boşluklarında soldurur: içeriğin
    /// <c>Margin.Top</c>'u kadar bir şerit üstte, <c>Margin.Bottom</c>'u kadar bir şerit altta. Kenar boşluğu içerikle
    /// birlikte kayar; durağanken şerit yalnız o boşluğu örter, içerik şeride girdiği anda solmaya başlar.</summary>
    public static readonly DependencyProperty InsetsProperty = DependencyProperty.RegisterAttached(
        "Insets", typeof(bool), typeof(EdgeFade), new PropertyMetadata(false, OnInsetsChanged));

    public static bool GetInsets(ScrollViewer viewer) => (bool)viewer.GetValue(InsetsProperty);
    public static void SetInsets(ScrollViewer viewer, bool value) => viewer.SetValue(InsetsProperty, value);

    public EdgeFade() => IsHitTestVisible = false;

    public Brush? Fill { get => GetFill(this); set => SetFill(this, value); }

    private static void OnInsetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer || e.NewValue is not true) return;
        // XAML'de öznitelik, kaydırıcı daha şablonsuzken atanır; tema stili (şablon) Initialized'dan hemen önce gelir.
        if (viewer.IsInitialized) AttachTo(viewer);
        else viewer.Initialized += (_, _) => AttachTo(viewer);
    }

    /// <summary>Şeritleri kaydırıcının KENDİ şablonundaki içerik hücresine, içerik sunucusunun üstüne koyar: genişlikleri
    /// viewport'tur (scrollbar ayrı sütunda kalır, hapı solmaz) ve kaydırıcıyla birlikte görünüp gizlenirler (About'un
    /// sekme panelleri). Şablon burada uygulanır ki dialog ilk çizildiğinde şeritler yerinde olsun; içerik sunucusu
    /// olmayan özel bir şablonda hiçbir şey yapılmaz.</summary>
    private static void AttachTo(ScrollViewer viewer)
    {
        viewer.ApplyTemplate();
        if (viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) is not FrameworkElement presenter
            || VisualTreeHelper.GetParent(presenter) is not Panel cell
            || cell.Children.OfType<EdgeFade>().Any()) return;
        cell.Children.Add(Strip(viewer, presenter, VerticalAlignment.Top, "Content.Margin.Top"));
        cell.Children.Add(Strip(viewer, presenter, VerticalAlignment.Bottom, "Content.Margin.Bottom"));
    }

    private static EdgeFade Strip(ScrollViewer viewer, FrameworkElement presenter, VerticalAlignment edge, string insetPath)
    {
        var strip = new EdgeFade { VerticalAlignment = edge };
        Grid.SetRow(strip, Grid.GetRow(presenter));
        Grid.SetColumn(strip, Grid.GetColumn(presenter));
        strip.SetBinding(HeightProperty, new Binding(insetPath) { Source = viewer }); // tek kaynak: içeriğin kenar boşluğu
        return strip;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Fill is not SolidColorBrush { Color: var color } || RenderSize.Height <= 0) return;
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        bool bottom = VerticalAlignment == VerticalAlignment.Bottom; // şerit yaslandığı kenarda opaktır
        var ramp = new LinearGradientBrush(bottom ? clear : color, bottom ? color : clear, new Point(0, 0), new Point(0, 1));
        ramp.Freeze();
        drawingContext.DrawRectangle(ramp, null, new Rect(RenderSize));
    }
}
