using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T57] WPF'te letter-spacing yok (dotnet/wpf#293) — tasarımın en yaygın tipografik detayı: caps
/// panel/popover/katman başlıkları (<c>DEPENDENCY GRAPH</c>, <c>PROJECTS</c>, <c>EVENT STREAM</c>…).
/// GlyphRun tabanlı özel <see cref="FrameworkElement"/> (TextBlock türevi DEĞİL — özel glyph çizimi
/// gerekiyor): her karakter advance'ine FontSize×TrackingEm eklenir + uppercase gömülü. Hair-space
/// EKLENMEZ — advance hesabı <see cref="TrackedGlyphs"/>'te saf fonksiyon (bu sınıf yalnız DP + WPF
/// render/measure kablajı). Kök <c>TextOptions.TextFormattingMode="Display"</c>'i miras alır, burada
/// DEĞİŞTİRİLMEZ (Ideal override yalnız T63 graf etiketlerine özgü — LOKAL).
/// </summary>
public sealed class TrackedTextBlock : FrameworkElement
{
    // Gömülü Geist (UI font — Mono DEĞİL). ConsoleView/FontAbWindow ile AYNI pack URI kalıbı [T56/T65].
    private static readonly FontFamily GeistFamily = new(
        new Uri("pack://application:,,,/BuildOrchestrator.App;component/Fonts/"),
        "./#Geist");

    // GlyphRun.language yalnız satır kesme/justification'ı etkiler — tek satırlık caps etiketlerde
    // önemsiz; sabit "en-US" (CultureInfo.InvariantCulture boş IETF tag verir, XmlLanguage bunu kabul
    // etmez, bu yüzden InvariantCulture'dan türetilmez).
    private static readonly XmlLanguage NeutralLanguage = XmlLanguage.GetLanguage("en-US");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackingEmProperty = DependencyProperty.Register(
        nameof(TrackingEm), typeof(double), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(0.07, OnVisualPropertyChanged));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(11.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(GeistFamily, OnVisualPropertyChanged));

    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight), typeof(FontWeight), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(FontWeights.Medium, OnVisualPropertyChanged));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.Register(
        nameof(Uppercase), typeof(bool), typeof(TrackedTextBlock),
        new FrameworkPropertyMetadata(true, OnVisualPropertyChanged));

    public TrackedTextBlock()
    {
        // Varsayılan Foreground = Brush.TextFaint (Foundation Tokens.xaml, T1) — hex burada
        // TEKRARLANMAZ (consume, don't hardcode). App.xaml merge zincirinde çözülür; merge edilmemiş
        // bağlamda (ör. bağımsız headless kurulum) Foreground null kalır ve OnRender no-op yapar.
        SetResourceReference(ForegroundProperty, "Brush.TextFaint");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>em cinsinden tracking (letter-spacing). Varsayılan design-v1 caps etiket değeri: 0.07.</summary>
    public double TrackingEm
    {
        get => (double)GetValue(TrackingEmProperty);
        set => SetValue(TrackingEmProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>Varsayılan Brush.TextFaint token'ına (SetResourceReference, bkz. ctor) bağlıdır — kaynak
    /// merge edilmemiş bağlamda null olabilir (OnRender bu durumda no-op yapar, çökme yok).</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>true iken glyph eşlemesinden önce <c>ToUpperInvariant()</c> uygulanır. Varsayılan true.</summary>
    public bool Uppercase
    {
        get => (bool)GetValue(UppercaseProperty);
        set => SetValue(UppercaseProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (TrackedTextBlock)d;
        self.InvalidateMeasure();
        self.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var glyphTypeface = ResolveGlyphTypeface();
        if (glyphTypeface is null)
            return default;

        var result = TrackedGlyphs.Build(glyphTypeface, Text, FontSize, TrackingEm, Uppercase);
        // Tek-satır yükseklik = fontun doğal satır yüksekliği (feasibility §3.1: LineHeight kullanma —
        // metin yukarı kayar). Dikey ortalama, kapsayıcı VerticalAlignment=Center ile sağlanır.
        double lineHeight = glyphTypeface.Height * FontSize;
        return new Size(result.TotalWidth, lineHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var run = BuildGlyphRun();
        if (run is null || Foreground is null)
            return;
        drawingContext.DrawGlyphRun(Foreground, run);
    }

    /// <summary>
    /// OnRender'ın çizeceği GlyphRun'ı üretir — OnRender ve testler (DrawingContext olmadan) AYNI yolu
    /// paylaşır. Typeface çözülemezse (ör. FontFamily kapsam dışı) veya metin boşsa null (çökme yok).
    /// </summary>
    internal GlyphRun? BuildGlyphRun()
    {
        var glyphTypeface = ResolveGlyphTypeface();
        if (glyphTypeface is null)
            return null;

        var result = TrackedGlyphs.Build(glyphTypeface, Text, FontSize, TrackingEm, Uppercase);
        if (result.GlyphIndices.Length == 0)
            return null;

        // Gerçek monitör DPI'sı (PerMonitorV2, app.manifest) — glyph hinting'in kök
        // TextOptions.TextFormattingMode="Display" ile aynı piksel ızgarasına snap olması için. Bağlı
        // olmayan/headless bir visual'de (ör. testler) GetDpi sistem varsayılanını (1.0) döner, çökmez —
        // bkz. MainWindow.xaml.cs (VisualTreeHelper.GetDpi kullanımı, aynı desen).
        float pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var baselineOrigin = new Point(0, glyphTypeface.Baseline * FontSize);
        return new GlyphRun(
            glyphTypeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: FontSize,
            pixelsPerDip: pixelsPerDip,
            glyphIndices: result.GlyphIndices,
            baselineOrigin: baselineOrigin,
            advanceWidths: result.AdvanceWidths,
            glyphOffsets: null,
            characters: null,
            deviceFontName: null,
            clusterMap: null,
            caretStops: null,
            language: NeutralLanguage);
    }

    private GlyphTypeface? ResolveGlyphTypeface()
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out var glyphTypeface) ? glyphTypeface : null;
    }
}
