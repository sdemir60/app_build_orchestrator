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
    /// <summary>
    /// Chevron'un ayırt edici ilk komutu. Kaynak ağacında TAM BİR kez geçmeli.
    ///
    /// <para><b>[DEĞİŞEN DEĞER] Eskiden <c>"M151 83"</c>di.</b> <c>AppMark.xaml</c> kaynak SVG'nin
    /// <c>translate(5.5 0)</c> grup dönüşümünü kendi tuval ötelemesine KATLAMIŞTI; işaret artık animasyonlu
    /// tepsi göstergesiyle AYNI geometriyi paylaştığı için katlama AÇILDI ve ortak kaynak SVG'nin kendi
    /// uzayında durur (X değerleri 5.5 daha büyük). Çizilen şekil ve ekrandaki konum DEĞİŞMEDİ: AppMark'ın iç
    /// tuvali aynı miktarda geriye kaydırıldı.</para></summary>
    private const string SignatureFigure = "M156.5 83";

    /// <summary>
    /// [tray indicator/K-7] <b>[DEĞİŞEN KURAL] Geometri artık <c>AppMark.xaml</c>'de değil, paylaşılan
    /// <c>Resources/BrandGeometry.xaml</c>'dedir.</b>
    ///
    /// <para><b>Eski iddia:</b> beş pill + chevron yalnız <c>Controls/AppMark.xaml</c>'de tanımlıdır (işaretin
    /// tek çizimi oydu).</para>
    ///
    /// <para><b>Değişme gerekçesi:</b> animasyonlu tepsi göstergesi (<c>Controls/TrayBuildIndicator</c>) AYNI
    /// beş pill ve AYNI chevron siluetini ister. İkinci bir çizim, iki dosyanın sessizce ayrışabileceği bir
    /// doğruluk kaynağı yaratırdı. Bu bir GEVŞETME değil KAYNAK TAŞIMASIDIR: iddia hâlâ "tam bir dosya"dır,
    /// yalnız o dosya artık her iki tüketicinin de okuduğu sözlüktür.</para></summary>
    [Fact]
    public void The_mark_geometry_is_declared_in_exactly_one_source_file()
    {
        var carriers = RepoPaths.AppSourceFiles("*.xaml")
            .Concat(RepoPaths.AppSourceFiles("*.cs"))
            .Where(f => File.ReadAllText(f).Contains(SignatureFigure, StringComparison.Ordinal))
            .Select(f => IoPath.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([IoPath.Combine("Resources", "BrandGeometry.xaml")], carriers);
    }

    /// <summary>İşaret geometriyi ÇİZMEZ, paylaşılan sözlükten TÜKETİR — altı anahtarın hepsini anahtar adıyla
    /// ister. (İkinci tüketici <c>TrayBuildIndicator</c> kendi test sınıfında pinlenir.)</summary>
    [Fact]
    public void The_mark_consumes_the_shared_brand_geometry_by_key()
    {
        string markup = File.ReadAllText(IoPath.Combine(RepoPaths.AppSrcRoot, "Controls", "AppMark.xaml"));

        foreach (string key in BrandGeometryKeys)
            Assert.Contains($"{{DynamicResource {key}}}", markup, StringComparison.Ordinal);
    }

    /// <summary>İşaretin kullandığı anahtarlar — beş pill + chevron. Sayaçlı (genişletilmiş) beyaz pill
    /// varyantı BURADA DEĞİL: o yalnız tepsi göstergesinindir.</summary>
    public static readonly string[] BrandGeometryKeys =
    [
        "Brand.Pill.TopDark", "Brand.Pill.Amber", "Brand.Pill.MidDark",
        "Brand.Pill.White", "Brand.Pill.Silver", "Brand.Chevron",
    ];

    /// <summary>Kontrol GERÇEKTEN çiziyor: beş pill + chevron, hepsi boyalı, verilen yükseklikte oranını
    /// koruyor (Viewbox Uniform — işaret geniştir: 186×128 viewBox).
    ///
    /// <para><b>[DEĞİŞEN İDDİA] Eskiden "5 <see cref="Rectangle"/> + 1 <see cref="ShapePath"/>" deniyordu.</b>
    /// Pill'ler paylaşılan sözlükten birer <c>RectangleGeometry</c> olarak geldiği için artık altısı da
    /// <see cref="ShapePath"/>'tir — <c>Rectangle</c> kendi <c>Canvas.Left/Width</c>'ini ister ve o sayılar
    /// geometrinin İÇİNDE yaşıyor. İddianın ÖZÜ aynı: altı boyalı figür, korunan oran.</para></summary>
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
        Assert.Equal(6, shapes.OfType<ShapePath>().Count());
        Assert.Empty(shapes.OfType<Rectangle>());
        Assert.All(shapes.OfType<ShapePath>(), p => Assert.NotNull(p.Data));
        Assert.All(shapes, s => Assert.NotNull(s.Fill));

        Assert.IsType<Viewbox>(mark.Content);
        Assert.Equal(30.0, mark.ActualHeight, precision: 1);
        // 186/128 ≈ 1.45 — Uniform ölçek oranı korumalı (bozulma YOK).
        Assert.Equal(30.0 * 186.0 / 128.0, mark.ActualWidth, precision: 0);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Altı figür, işaretin çerçevesinde KAYNAK SVG'nin verdiği yere oturur.
    ///
    /// <para><b>Neden ayrı bir test:</b> geometri paylaşılan sözlüğe taşınırken iki sayı birlikte değişti —
    /// koordinatlar 5.5 büyüdü, iç tuval 5.5 geriye kaydı. Bunlar birbirini götürür; götürmezse işaret sessizce
    /// kayar ve title bar'da yamuk durur. Yapısal testlerin hiçbiri (kaç figür var, hangi fırça) bunu göremez:
    /// figürler doğru sayıda ve doğru renkte olup yanlış yerde durabilir. Burada ÇİZİLEN kutu ölçülür.</para>
    ///
    /// <para>Beklenen kutu: figürlerin birleşik sınırı kaynak uzayda (51.5, 83)–(229.5, 203)'tür; iç tuval
    /// (−46.5, −78) kaydırınca 186×128'lik çerçevede (5, 5)–(183, 125) olur. Viewbox Uniform ölçeğiyle
    /// verilen yüksekliğe iner.</para></summary>
    [StaFact]
    public void The_mark_lands_every_figure_at_its_source_coordinates()
    {
        var host = DsResources.NewHost();
        var mark = new AppMark { Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
        var window = DsResources.Realize(host, mark);

        var drawn = Rect.Empty;
        foreach (var figure in DsResources.Descendants(mark).OfType<ShapePath>())
            drawn.Union(figure.TransformToAncestor(mark).TransformBounds(figure.Data!.Bounds));

        const double scale = 30.0 / 128.0;   // Viewbox Uniform: verilen yükseklik / işaretin kendi yüksekliği
        Assert.Equal(5.0 * scale, drawn.Left, precision: 2);
        Assert.Equal(5.0 * scale, drawn.Top, precision: 2);
        Assert.Equal(183.0 * scale, drawn.Right, precision: 2);
        Assert.Equal(125.0 * scale, drawn.Bottom, precision: 2);
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

        // [K-7] Altı figürün hepsi artık Path — chevron, gradient TAŞIYAN TEK figür olmasıyla ayrılır
        // (beş pill düz token dolgusudur). Bu ayrım iddianın kendisidir: ikinci bir gradient girerse Single() kırar.
        var chevron = DsResources.Descendants(mark).OfType<ShapePath>()
            .Single(p => p.Fill is LinearGradientBrush);
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
