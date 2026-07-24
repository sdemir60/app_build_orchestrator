using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T45] "Anti-slop" statik denetimleri — design-v1'in ölçülü estetiğini KAZA ile bozan eklemeleri (emoji,
/// gradient, ikinci marka rengi, panel gölgesi, yuvarlak konsol, dönen globe) kaynak taramasıyla YASAKLAR.
/// Dosya listesi ELLE tutulmaz, kaynak ağacı taranır (<see cref="RepoPaths.AppSourceFiles"/> — NoHardcodedColor
/// deseni). Bilinçli/meşru istisnalar (floating overlay gölgesi; amber ailesi; drawn ring/spinner) açıkça korunur.
/// </summary>
public sealed class AntiSlopTests
{
    // Gölge (elevation) taşımasına İZİN VERİLEN dosyalar — yalnız floating overlay'ler + token tanımı.
    private static readonly string[] ShadowAllowed =
    [
        Path.Combine("Resources", "Tokens.xaml"),      // Effect.* TANIMLARI
        Path.Combine("Resources", "Controls.xaml"),    // Ds.Popover / Ds.Dialog / ToolTip (floating overlay stilleri)
        Path.Combine("Controls", "LatestPill.xaml"),   // `⌄ latest` floating pill
    ];

    // Emoji: astral (surrogate çifti pictographic 1F000+) + BMP misc-symbols/dingbats (2600-27BF) + emoji
    // variation selector (FE0F). design-v1 ikon dili ÇİZİLMİŞ geometridir (Icons.xaml) — emoji YOK.
    private static readonly Regex Emoji = new(
        "[\uD800-\uDBFF][\uDC00-\uDFFF]|[☀-➿️]", RegexOptions.Compiled);

    private static readonly Regex XmlComment = new("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Gradient = new("GradientBrush|GradientStop", RegexOptions.Compiled);
    private static readonly Regex Shadow = new("DropShadowEffect|Effect\\.OverlayShadow|Effect\\.PopoverShadow", RegexOptions.Compiled);
    private static readonly Regex RoundedRadius = new("Radius\\.(Xs|Sm|Md|Lg|Full|Overlay)|CornerRadius=\"[1-9]", RegexOptions.Compiled);

    [Fact]
    public void No_xaml_uses_an_emoji()
    {
        var offenders = ScanXaml((rel, text) => Emoji.IsMatch(text) ? rel : null);
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_xaml_declares_a_gradient_fill()
    {
        // design-v1: düz yüzeyler; gradient dekorasyon YASAK (tek marka rengi amber, düz).
        var offenders = ScanXaml((rel, text) => Gradient.IsMatch(text) ? rel : null);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_floating_overlays_carry_a_drop_shadow()
    {
        // Paneller (liste/konsol/graf/stream/action-bar/ribbon) DÜZ durur; gölge YALNIZ floating overlay'lerde
        // (popover/dialog/tooltip/pill). Bir panel dosyası gölge referansı taşırsa offender.
        var offenders = ScanXaml((rel, text) =>
            Shadow.IsMatch(text) && !ShadowAllowed.Contains(rel, StringComparer.OrdinalIgnoreCase) ? rel : null);
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_console_surface_stays_sharp_cornered()
    {
        // design-v1 §2.5: konsol keskin (radius 0). Konsol yüzeyi/başlığı yuvarlak köşe token'ı KULLANMAZ.
        foreach (string rel in new[] { Path.Combine("Console", "ConsoleView.xaml"), Path.Combine("Console", "ConsoleHeader.xaml") })
        {
            string text = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, rel));
            Assert.False(RoundedRadius.IsMatch(text), $"{rel} yuvarlak köşe kullanıyor — konsol keskin (radius 0) kalmalı");
        }
    }

    [Fact]
    public void The_only_brand_accent_hue_is_amber()
    {
        // Amber TEK marka rengidir (Tokens.xaml yorumu). İkinci bir marka HUE'su (mavi/mor/teal/…) token'ı
        // eklenirse yakala. Statü renkleri (success/fail/cycle/skipped/queued) SEMANTİKtir — marka değil, serbest.
        string tokens = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Resources", "Tokens.xaml"));
        var brandHues = new Regex("x:Key=\"Brush\\.(Blue|Indigo|Violet|Purple|Magenta|Pink|Teal|Cyan|Lime|Emerald)", RegexOptions.Compiled);
        Assert.False(brandHues.IsMatch(tokens), "amber dışı bir marka rengi token'ı eklenmiş");
    }

    [Fact]
    public void No_spinning_globe_icon_exists()
    {
        // Dekoratif dönen globe/dünya YASAK. Amber build spinner (yay) BİLİNÇLİ istisna — ayrı ad. Bir globe/
        // world ikonu (dolayısıyla onu döndürebilecek bir motion) hiç TANIMLANMAMIŞ olmalı.
        var globe = new Regex("Icon\\.(Globe|World|Earth|Planet)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (string file in RepoPaths.AppSourceFiles("*.xaml").Concat(RepoPaths.AppSourceFiles("*.cs")))
            if (globe.IsMatch(File.ReadAllText(file)))
                offenders.Add(Path.GetRelativePath(RepoPaths.AppSrcRoot, file));
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_scan_actually_reads_the_xaml_it_claims_to()
    {
        // Tarama boş dönerse yukarıdaki testler sessizce yeşil kalırdı (yol/filtre bozulması).
        var scanned = RepoPaths.AppSourceFiles("*.xaml")
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f)).ToList();
        Assert.Contains(Path.Combine("Console", "ConsoleView.xaml"), scanned);
        Assert.Contains(Path.Combine("Controls", "LatestPill.xaml"), scanned);
        Assert.All(ShadowAllowed, a => Assert.Contains(a, scanned));
    }

    private static List<string> ScanXaml(Func<string, string, string?> probe)
    {
        var offenders = new List<string>();
        foreach (string file in RepoPaths.AppSourceFiles("*.xaml"))
        {
            string rel = Path.GetRelativePath(RepoPaths.AppSrcRoot, file);
            // Yorumlar (Türkçe geliştirici notları) SHIPPED markup DEĞİL — "Effect.OverlayShadow" gibi bir kaynak
            // referansı yorumda geçebilir (BuildMenu.xaml). Yalnız gerçek markup taranır (yorumlar çıkarılır).
            string markup = XmlComment.Replace(File.ReadAllText(file), "");
            if (probe(rel, markup) is { } hit) offenders.Add(hit);
        }
        return offenders;
    }
}
