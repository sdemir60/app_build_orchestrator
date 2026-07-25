using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>[T49] design-v1 tokens/*.css ölçeklerinin WPF karşılıkları — değerler BİREBİR.</summary>
[Collection("Console UI (serial)")]
public sealed class DesignTokenScaleTests
{
    // pack:// URI'ler gerçek bir Application olmadan (headless test host) çözülmez — TokenBrushesTests ile aynı
    // desen: TestAssets'e kopyalanan güncel Tokens.xaml'i dosyadan XamlReader ile yükle.
    private static ResourceDictionary Tokens()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Tokens.xaml");
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    [StaFact]
    public void Font_size_scale_matches_typography_css()
    {
        var t = Tokens();
        Assert.Equal(11.0, (double)t["FontSize.2xs"]);
        Assert.Equal(12.0, (double)t["FontSize.Xs"]);
        Assert.Equal(13.0, (double)t["FontSize.Sm"]);
        Assert.Equal(14.0, (double)t["FontSize.Md"]);
        Assert.Equal(16.0, (double)t["FontSize.Lg"]);
        Assert.Equal(20.0, (double)t["FontSize.Xl"]);
        Assert.Equal(26.0, (double)t["FontSize.2xl"]);
        Assert.Equal(34.0, (double)t["FontSize.3xl"]);
    }

    [StaFact]
    public void Radius_and_layout_sizes_match_spacing_css()
    {
        var t = Tokens();
        Assert.Equal(new CornerRadius(0), (CornerRadius)t["Radius.None"]);
        Assert.Equal(new CornerRadius(3), (CornerRadius)t["Radius.Xs"]);
        Assert.Equal(new CornerRadius(4), (CornerRadius)t["Radius.Sm"]);
        Assert.Equal(new CornerRadius(6), (CornerRadius)t["Radius.Md"]);
        Assert.Equal(new CornerRadius(8), (CornerRadius)t["Radius.Lg"]);
        Assert.Equal(40.0, (double)t["Size.TitleBarHeight"]);
        Assert.Equal(42.0, (double)t["Size.ActionBarHeight"]);
        Assert.Equal(32.0, (double)t["Size.RibbonHeight"]);
        Assert.Equal(28.0, (double)t["Size.PanelHeaderHeight"]);   // statusbar-28 → PanelHeaderHeight (v7 T49)
        Assert.Equal(8.0, (double)t["Size.DotSize"]);
    }

    [StaFact]
    public void Row_height_token_agrees_with_LayoutMetrics_so_there_is_exactly_one_authority()
    {
        Assert.Equal(LayoutMetrics.DefaultRowHeight, (double)Tokens()["Size.RowHeight"]);
        Assert.Equal(LayoutMetrics.DefaultHeaderHeight, (double)Tokens()["Size.LayerHeaderHeight"]);
    }

    [StaFact]
    public void Missing_colour_families_are_now_present_with_exact_css_values()
    {
        var t = Tokens();
        Assert.Equal(Color.FromRgb(0x1a, 0x1a, 0x1e), ((SolidColorBrush)t["Brush.SurfaceHover"]).Color);
        Assert.Equal(Color.FromRgb(0x20, 0x20, 0x24), ((SolidColorBrush)t["Brush.SurfaceActive"]).Color);
        Assert.Equal(Color.FromArgb(0x99, 0x04, 0x04, 0x06), ((SolidColorBrush)t["Brush.Scrim"]).Color);  // rgba(4,4,6,.60)
        Assert.Equal(((SolidColorBrush)t["Brush.Amber"]).Color, ((SolidColorBrush)t["Brush.StatusBuilding"]).Color);
    }

    [StaFact]
    public void Neutral_ramp_endpoints_match_colors_css()
    {
        var t = Tokens();
        Assert.Equal(Hex("#08080a"), ((SolidColorBrush)t["Brush.Black"]).Color);   // --black
        Assert.Equal(Hex("#ffffff"), ((SolidColorBrush)t["Brush.White"]).Color);   // --white
        Assert.Equal(Hex("#cdcdd2"), ((SolidColorBrush)t["Brush.Neutral200"]).Color);
    }

    [StaFact]
    public void Building_status_family_aliases_the_amber_family_and_queued_gains_its_fourth_tone()
    {
        var t = Tokens();
        // colors.css: --status-building* AYNEN --amber* ailesine bağlıdır.
        Assert.Equal(((SolidColorBrush)t["Brush.AmberText"]).Color, ((SolidColorBrush)t["Brush.StatusBuildingText"]).Color);
        Assert.Equal(((SolidColorBrush)t["Brush.AmberSoft"]).Color, ((SolidColorBrush)t["Brush.StatusBuildingSoft"]).Color);
        Assert.Equal(((SolidColorBrush)t["Brush.AmberBorder"]).Color, ((SolidColorBrush)t["Brush.StatusBuildingBorder"]).Color);

        // queued ailesinin CSS'te olmayan 4. tonu (README §1.1 "her statünün 4 tonu", -border %24-32):
        // çekirdek #7c7c84 + %24 alfa (skipped -border ile aynı oran) → 0x3D.
        var queuedBorder = ((SolidColorBrush)t["Brush.StatusQueuedBorder"]).Color;
        Assert.Equal((byte)0x3D, queuedBorder.A);
        Assert.Equal(((SolidColorBrush)t["Brush.StatusQueued"]).Color, Color.FromRgb(queuedBorder.R, queuedBorder.G, queuedBorder.B));
    }

    [StaFact]
    public void Weight_line_height_and_tracking_match_typography_css()
    {
        var t = Tokens();
        // README §1.2: "Başlık 600, vurgu 500, gövde 400".
        Assert.Equal(FontWeights.Normal, (FontWeight)t["FontWeight.Body"]);
        Assert.Equal(FontWeights.Medium, (FontWeight)t["FontWeight.Emphasis"]);
        Assert.Equal(FontWeights.SemiBold, (FontWeight)t["FontWeight.Heading"]);

        // WPF LineHeight mutlak DIP ister: CSS oranı × punto.
        Assert.Equal(13 * 1.35, (double)t["LineHeight.Snug13"]);    // --leading-snug
        Assert.Equal(13 * 1.5, (double)t["LineHeight.Normal13"]);   // --leading-normal
        Assert.Equal(12 * 1.55, (double)t["LineHeight.Mono12"], 3); // --leading-mono
        Assert.Equal(0.07, (double)t["Tracking.Caps"]);             // --tracking-caps 0.07em
    }

    [StaFact]
    public void Space_scale_is_the_four_pixel_grid_from_spacing_css()
    {
        var t = Tokens();
        Assert.Equal(4.0, (double)t["Space.1"]);
        Assert.Equal(8.0, (double)t["Space.2"]);
        Assert.Equal(12.0, (double)t["Space.3"]);
        Assert.Equal(16.0, (double)t["Space.4"]);
        Assert.Equal(20.0, (double)t["Space.5"]);
        Assert.Equal(24.0, (double)t["Space.6"]);
        Assert.Equal(32.0, (double)t["Space.8"]);
        Assert.Equal(40.0, (double)t["Space.10"]);
        Assert.Equal(48.0, (double)t["Space.12"]);
        Assert.Equal(64.0, (double)t["Space.16"]);
    }

    [StaFact]
    public void Remaining_sizes_and_the_overlay_alias_are_present()
    {
        var t = Tokens();
        Assert.Equal(new CornerRadius(999), (CornerRadius)t["Radius.Full"]);
        // [T59] Radius.Overlay KALIR — Radius.Lg'nin alias'ı.
        Assert.Equal((CornerRadius)t["Radius.Lg"], (CornerRadius)t["Radius.Overlay"]);
        Assert.Equal(2.0, (double)t["Size.ProgressHeight"]);
        Assert.Equal(30.0, (double)t["Size.RowHeightCompact"]);
        Assert.Equal(2.0, (double)t["Size.FocusRingWidth"]);   // --focus-ring-width
        Assert.Equal(1.0, (double)t["Size.FocusRingOffset"]);  // README: "2px focus-ring amber halka, offset 1px"
        Assert.Equal(1240.0, (double)t["Size.WindowMinWidth"]);
        Assert.Equal(620.0, (double)t["Size.WindowMinHeight"]);
    }

    [StaFact]
    public void Overlay_shadow_approximates_the_elevation_overlay_token()
    {
        // effects.css:5 --elevation-overlay: 0 10px 28px -10px rgba(0,0,0,.66) — DropShadowEffect'te spread yok
        // (A13.1-2), tek katmanla yakınsanır.
        var shadow = (DropShadowEffect)Tokens()["Effect.OverlayShadow"];
        Assert.Equal(270.0, shadow.Direction);
        Assert.Equal(10.0, shadow.ShadowDepth);
        Assert.Equal(28.0, shadow.BlurRadius);
        Assert.Equal(0.66, shadow.Opacity);
        Assert.Equal(Colors.Black, shadow.Color);

        // [T59] Effect.PopoverShadow KALIR (--elevation-popover).
        Assert.Equal(18.0, ((DropShadowEffect)Tokens()["Effect.PopoverShadow"]).BlurRadius);
    }
}
