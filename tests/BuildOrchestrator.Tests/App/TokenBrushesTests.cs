using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] App/Resources/Tokens.xaml: design-v1 tokens/colors.css alt kümesi (bu 6 UI task'ının
/// kullandığı brush'lar). Renkler colors.css'ten BİREBİR — uydurma yok.
/// </summary>
public class TokenBrushesTests
{
    // [T60] Yükleme mekaniğinin TEK yeri DsResources'tır (kopya YASAK, CLAUDE.md).
    private static ResourceDictionary LoadTokenDictionary() => DsResources.Load("Tokens.xaml");

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    [StaFact]
    public void Critical_brushes_resolve_to_exact_design_v1_colors()
    {
        var resources = LoadTokenDictionary();

        Assert.Equal(Hex("#060608"), Assert.IsType<SolidColorBrush>(resources["Brush.ConsoleBg"]).Color);
        Assert.Equal(Hex("#eda10f"), Assert.IsType<SolidColorBrush>(resources["Brush.Amber"]).Color);
        Assert.Equal(Hex("#ff706a"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusFailText"]).Color);
        Assert.Equal(Hex("#2a2a30"), Assert.IsType<SolidColorBrush>(resources["Brush.Border"]).Color);
        Assert.Equal(Hex("#54545c"), Assert.IsType<SolidColorBrush>(resources["Brush.TextFaint"]).Color);
        // [A13/T3c · c1] --text-primary (tokens/colors.css) — en sık kullanılan metin rengi hiç pinli DEĞİLDİ.
        Assert.Equal(Hex("#ededee"), Assert.IsType<SolidColorBrush>(resources["Brush.TextPrimary"]).Color);
    }

    [StaFact]
    public void Surface_and_border_ramp_resolves_to_exact_design_v1_colors()
    {
        var resources = LoadTokenDictionary();

        Assert.Equal(Hex("#0a0a0c"), Assert.IsType<SolidColorBrush>(resources["Brush.SurfaceSunken"]).Color);
        Assert.Equal(Hex("#0e0e10"), Assert.IsType<SolidColorBrush>(resources["Brush.SurfaceBase"]).Color);
        Assert.Equal(Hex("#141417"), Assert.IsType<SolidColorBrush>(resources["Brush.Surface"]).Color);
        Assert.Equal(Hex("#1a1a1e"), Assert.IsType<SolidColorBrush>(resources["Brush.SurfaceRaised"]).Color);
        Assert.Equal(Hex("#202024"), Assert.IsType<SolidColorBrush>(resources["Brush.SurfaceOverlay"]).Color);
        Assert.Equal(Hex("#1c1c20"), Assert.IsType<SolidColorBrush>(resources["Brush.BorderSubtle"]).Color);
        Assert.Equal(Hex("#3a3a42"), Assert.IsType<SolidColorBrush>(resources["Brush.BorderStrong"]).Color);
    }

    [StaFact]
    public void Status_family_resolves_to_exact_design_v1_colors()
    {
        var resources = LoadTokenDictionary();

        Assert.Equal(Hex("#43b16b"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusSuccess"]).Color);
        Assert.Equal(Hex("#58cb80"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusSuccessText"]).Color);
        Assert.Equal(Hex("#ee5a52"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusFail"]).Color);
        Assert.Equal(Hex("#6a6a73"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusSkipped"]).Color);
        Assert.Equal(Hex("#888890"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusSkippedText"]).Color);
        Assert.Equal(Hex("#df6f2b"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusCycle"]).Color);
        Assert.Equal(Hex("#f0853f"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusCycleText"]).Color);
        Assert.Equal(Hex("#7c7c84"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusQueued"]).Color);
        Assert.Equal(Hex("#9a9aa2"), Assert.IsType<SolidColorBrush>(resources["Brush.StatusQueuedText"]).Color);
    }

    [StaFact]
    public void Will_dot_and_focus_ring_resolve_to_exact_design_v1_colors()
    {
        var resources = LoadTokenDictionary();

        // dot-dirty = amber, dot-clean = neutral-600 (= border-strong), dot-unknown = transparent.
        Assert.Equal(Hex("#eda10f"), Assert.IsType<SolidColorBrush>(resources["Brush.DotDirty"]).Color);
        Assert.Equal(Hex("#3a3a42"), Assert.IsType<SolidColorBrush>(resources["Brush.DotClean"]).Color);
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(resources["Brush.DotUnknown"]).Color);
        Assert.Equal(Hex("#1c1c20"), Assert.IsType<SolidColorBrush>(resources["Brush.DotOutline"]).Color);

        // focus-ring: rgba(237,161,15,.50) -> alpha byte round(0.50*255) = 128 = 0x80.
        var focusRing = Assert.IsType<SolidColorBrush>(resources["Brush.FocusRing"]).Color;
        Assert.Equal((byte)0x80, focusRing.A);
        Assert.Equal((byte)0xed, focusRing.R);
        Assert.Equal((byte)0xa1, focusRing.G);
        Assert.Equal((byte)0x0f, focusRing.B);
    }
}
