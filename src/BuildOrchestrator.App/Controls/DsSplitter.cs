using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>Ayracın çizdiği çizginin yönü — dikey (kolon ayracı, yatay sürükleme) / yatay (satır ayracı).</summary>
public enum SplitterLine { Vertical, Horizontal }

/// <summary>
/// [T35] design-v1 2×2 yerleşim ayracı: 7px KAVRAMA bandı (rahat hit alanı) + ortada 1px görünür
/// <c>Brush.Border</c> çizgi; sürüklerken çizgi <c>Brush.AmberBorder</c>'a döner.
///
/// <para><see cref="GridSplitter"/> (dolayısıyla <see cref="System.Windows.Controls.Primitives.Thumb"/>)
/// türevidir — <c>DragStarted</c>/<c>DragCompleted</c> oradan gelir. Çizgi bu control'ün SAHİP OLDUĞU tek
/// visual child'tır (şablon YOK): headless testte pencere/ApplyTemplate olmadan da <see cref="Line"/> hazırdır.
/// <b>Motion (B3/It-4a):</b> tasarım ayraç çizgisi için bir geçiş süresi TANIMLAMAZ → renk anında
/// <c>SetResourceReference</c> ile takas edilir (uydurma animasyon eklenmez — brief).</para>
/// </summary>
public sealed class DsSplitter : GridSplitter
{
    /// <summary>Kavrama bandının kalınlığı (px).</summary>
    public const double GrabBand = 7;
    /// <summary>Görünür çizginin kalınlığı (px).</summary>
    public const double LineThickness = 1;

    /// <summary>Ortadaki 1px çizgi (test doğrudan ölçer).</summary>
    public Rectangle Line { get; }

    public static readonly DependencyProperty LineOrientationProperty = DependencyProperty.Register(
        nameof(LineOrientation), typeof(SplitterLine), typeof(DsSplitter),
        new PropertyMetadata(SplitterLine.Vertical, (d, _) => ((DsSplitter)d).ApplyOrientation()));

    public SplitterLine LineOrientation
    {
        get => (SplitterLine)GetValue(LineOrientationProperty);
        set => SetValue(LineOrientationProperty, value);
    }

    public DsSplitter()
    {
        // Şablon YOK: <see cref="OnRender"/> tüm RenderSize'ı Background (saydam) ile ÇİZER — Control'ün
        // varsayılan render'ı YOKTUR (boyama normalde yalnız bir ControlTemplate'ten gelir), bu yüzden
        // Background atamak TEK BAŞINA hiçbir şeyi hit-test edilebilir yapmaz (C1 review I-1). Aşağıdaki
        // OnRender override'ı olmadan yalnız 1px'lik Line child'ı tıklanabilir kalırdı.
        Template = null;
        Background = Brushes.Transparent;
        Focusable = false;
        ShowsPreview = false;
        ResizeBehavior = GridResizeBehavior.PreviousAndNext;

        Line = new Rectangle { SnapsToDevicePixels = true };
        Line.SetResourceReference(Shape.FillProperty, "Brush.Border");
        AddVisualChild(Line);
        AddLogicalChild(Line);
        ApplyOrientation();

        DragStarted += (_, _) => Line.SetResourceReference(Shape.FillProperty, "Brush.AmberBorder");
        DragCompleted += (_, _) => Line.SetResourceReference(Shape.FillProperty, "Brush.Border");
    }

    private void ApplyOrientation()
    {
        if (LineOrientation == SplitterLine.Vertical)
        {
            Width = GrabBand; Height = double.NaN;
            HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Stretch;
            ResizeDirection = GridResizeDirection.Columns;
            Cursor = Cursors.SizeWE;
            Line.Width = LineThickness; Line.Height = double.NaN;
            Line.HorizontalAlignment = HorizontalAlignment.Center; Line.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            Height = GrabBand; Width = double.NaN;
            HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Center;
            ResizeDirection = GridResizeDirection.Rows;
            Cursor = Cursors.SizeNS;
            Line.Height = LineThickness; Line.Width = double.NaN;
            Line.HorizontalAlignment = HorizontalAlignment.Stretch; Line.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    /// <summary>[C1 review I-1] Control'ün kendi render'ı yoktur (şablon YOK) — bu override olmadan
    /// <see cref="Background"/> hiç ÇİZİLMEZ, dolayısıyla hit test edilemez; yalnız <see cref="Line"/>
    /// (1px) tıklanabilir kalırdı. Saydam da olsa ÇİZİLMİŞ bir <see cref="Brush"/> hit-testable'dır —
    /// bu yüzden tüm 7px kavrama bandı burada RenderSize kadar doldurulur.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (Background is not null)
            dc.DrawRectangle(Background, null, new Rect(RenderSize));
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => Line;

    protected override Size MeasureOverride(Size constraint)
    {
        Line.Measure(constraint);
        return base.MeasureOverride(constraint);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // FrameworkElement.Arrange, çizginin kendi hizalamasını (Center + 1px) verilen dikdörtgen içinde uygular.
        Line.Arrange(new Rect(arrangeSize));
        return base.ArrangeOverride(arrangeSize);
    }
}
