using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Ürün markası (design-v1.2.1 §6, <c>prototype/assets/app-mark.svg</c>). Delta wordmark'ından AYRI bir
/// işarettir: o FİRMA logosudur, bu ÜRÜNÜN kendi markası. İkisi title bar'da ve About başlığında birlikte
/// durur — ürün önde (daha büyük, tam renk), firma arkada (küçük, soluk).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AppMarkTests
{
    /// <summary>Chevron'un ayırt edici ilk komutu (app-mark.svg). Kaynak ağacında TAM BİR kez geçmeli.</summary>
    private const string SignatureFigure = "M151 83";

    [Fact]
    public void The_mark_geometry_is_declared_in_exactly_one_source_file()
    {
        var carriers = RepoPaths.AppSourceFiles("*.xaml")
            .Concat(RepoPaths.AppSourceFiles("*.cs"))
            .Where(f => File.ReadAllText(f).Contains(SignatureFigure, StringComparison.Ordinal))
            .Select(f => IoPath.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([IoPath.Combine("Controls", "AppMark.xaml")], carriers);
    }

    /// <summary>Kontrol GERÇEKTEN çiziyor: beş pill + chevron, hepsi boyalı, verilen yükseklikte oranını
    /// koruyor (Viewbox Uniform — işaret geniştir: 186×128 viewBox).</summary>
    [StaFact]
    public void The_mark_renders_five_strips_and_a_chevron_and_keeps_its_aspect_ratio()
    {
        var host = DsResources.NewHost();
        // Hizalama gerçek kullanımdaki gibi: işaret bir StackPanel'de kendi genişliğini alır. Host Border
        // varsayılan olarak çocuğunu GERER — Stretch bırakılırsa ölçülen genişlik pencerenin genişliği olur
        // ve oran iddiası anlamsızlaşır.
        var mark = new AppMark { Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
        var window = DsResources.Realize(host, mark);

        var shapes = DsResources.Descendants(mark).OfType<Shape>().ToList();
        Assert.Equal(5, shapes.OfType<Rectangle>().Count());
        Assert.Single(shapes.OfType<ShapePath>());
        Assert.All(shapes, s => Assert.NotNull(s.Fill));

        Assert.IsType<Viewbox>(mark.Content);
        Assert.Equal(30.0, mark.ActualHeight, precision: 1);
        // 186/128 ≈ 1.45 — Uniform ölçek oranı korumalı (bozulma YOK).
        Assert.Equal(30.0 * 186.0 / 128.0, mark.ActualWidth, precision: 0);
        GC.KeepAlive(window);
    }

    /// <summary>Chevron amber bir GRADIENT'tir (design-v1.2.1: `#FFB52E → #EDA10F → #C9860C`) — düz dolgu
    /// değil. Üç durak da token'dan çözülür.</summary>
    [StaFact]
    public void The_chevron_is_painted_with_the_three_stop_amber_gradient()
    {
        var host = DsResources.NewHost();
        // Hizalama gerçek kullanımdaki gibi: işaret bir StackPanel'de kendi genişliğini alır. Host Border
        // varsayılan olarak çocuğunu GERER — Stretch bırakılırsa ölçülen genişlik pencerenin genişliği olur
        // ve oran iddiası anlamsızlaşır.
        var mark = new AppMark { Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
        var window = DsResources.Realize(host, mark);

        var chevron = DsResources.Descendants(mark).OfType<ShapePath>().Single();
        var gradient = Assert.IsType<LinearGradientBrush>(chevron.Fill);
        Assert.Equal(3, gradient.GradientStops.Count);

        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberBright"), gradient.GradientStops[0].Color);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), gradient.GradientStops[1].Color);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Brand.ChevronDeep"), gradient.GradientStops[2].Color);
        GC.KeepAlive(window);
    }

    /// <summary>Markanın DS nötr rampasında karşılığı OLMAYAN iki rengi token olarak tanımlıdır — XAML'de ham
    /// hex yazılamaz (NoHardcodedColorTests) ve bu iki değerin tek yeri Tokens.xaml'dir.</summary>
    [StaFact]
    public void The_two_mark_specific_colours_resolve_as_tokens()
    {
        var host = DsResources.NewHost();
        Assert.NotEqual(default, DsResources.TokenColor(host, "Brush.Brand.StripDim"));
        Assert.NotEqual(default, DsResources.TokenColor(host, "Brush.Brand.ChevronDeep"));
    }
}
