using System.IO;
using System.Linq;
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

    // ---------------------------------------------------------------- [A13/T4 · n1/n2/n3] Bilinçli KARARLAR (§8)

    // README §8: "Toast/popup yok · 'View failures' butonu yok · perf/Build tooltip'i yok · katman eşleşme
    // sayacı yok." — n1/n2/n3'ün ORTAK otorite kaynağı. (perf/Build tooltip'i n4'tür, ActionBarTests'tedir;
    // n5 DsControlTemplateTests'te — realize edilmiş kontrol assert'i, AntiSlopTests'in saf-tarama desenine
    // uymuyor; n6 EventStreamTests/ProjectRowTests'te — aynı gerekçeyle. [A13/final] Eskiden TabularFiguresTests
    // deniyordu; o sınıf T3b fix-1'de SİLİNDİ, assert'ler kontrollerin kendi test sınıflarına dağıtıldı.)
    // [A13/T4 fix-1 · D3] IgnoreCase eklendi — kardeş guard'la (No_spinning_globe_icon_exists) tutarlı.
    private static readonly Regex ToastVocabulary = new("Toast|Banner|Snackbar", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string ViewFailuresButton = "View failures";
    // [A13/T4 fix-1 · D2] `match.count`'taki serbest joker `\.` ile daraltıldı; `Matches\b` (regex.Matches(...)
    // çağrısına yanlış-pozitif takılabilirdi) KALDIRILDI — `MatchCount`/`match_count`/`match-count` yeterli.
    private static readonly Regex LayerMatchCounter = new(@"MatchCount|match[._-]count", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>[A13/T4 · n1 · fix-1 · B1] design-v1 §8: <i>"Toast/popup yok"</i> — uygulama-içi bir
    /// toast/banner/snackbar bileşeni (sınıf/kontrol/XAML) hiç YOK. <c>RibbonText.cs:48</c>'deki "Banner/toast
    /// YOK" yorumu bu kuralın KENDİSİNİ anlatır (kod değil) — <c>skipCommentLines</c> onu eler.
    ///
    /// <para><b>fix-1 · B1:</b> her iki tarama kümesinin (<c>*.cs</c>/<c>*.xaml</c>) boş OLMADIĞI ayrıca assert
    /// edilir — brief'in açık şartı ("taranan küme boş olmamalı"); önceden yalnız <c>*.xaml</c> için (dosyanın
    /// SONUNDAKİ ortak meta-testte) vardı, <c>*.cs</c> için HİÇ yoktu (n1'in asıl hedefi — bir Toast SINIFI —
    /// ağırlıkla <c>.cs</c> tarafındadır).</para></summary>
    [Fact]
    public void No_toast_or_banner_component_exists()
    {
        var csFiles = SourceGuard.ScannedAppFiles("*.cs");
        var xamlFiles = SourceGuard.ScannedAppFiles("*.xaml");
        Assert.NotEmpty(csFiles);   // [fix-1 · B1] non-vacuous — n1'in asıl hedefi (Toast sınıfı) burada yaşar
        Assert.NotEmpty(xamlFiles);

        var offenders = SourceGuard.ScanApp("*.cs", ToastVocabulary, skipCommentLines: true)
            .Concat(SourceGuard.ScanApp("*.xaml", ToastVocabulary, skipCommentLines: true)).ToList();
        Assert.Empty(offenders);
    }

    /// <summary>[A13/T4 · n2 · fix-1 · D1/D2] design-v1 §2.9 (<c>"Eşleşme sayacı gösterilmez (istenmedi)."</c>) +
    /// §8 — Settings dialog'unun LAYERS bölümü hiçbir katman satırında "bu regex kaç projeyle eşleşiyor" sayacı
    /// GÖSTERMEZ. Kapsam: <c>SettingsDialog.xaml*</c> (görünüm <b>+ code-behind</b> — <c>SettingsDialog.xaml.cs</c>
    /// <c>OnAddLayer</c>/<c>OnRemoveLayer</c>/<c>OnRestoreDefaults</c>'ı barındırır; bir eşleşme sayacı en doğal
    /// biçimde ORADA hesaplanırdı, fix-1 ÖNCESİ joker'siz "SettingsDialog.xaml" deseni bunu KAÇIRIYORDU) +
    /// <c>SettingsDraftViewModel.cs</c> (<c>LayerRowViewModel</c>'i de barındırır — VM'de bir <c>MatchCount</c>
    /// alanı eklense görünüme hiç bağlanmasa bile bu, özelliğin sessizce yarım bırakıldığının işaretidir).
    ///
    /// <para><b>fix-1 · D2:</b> <c>skipCommentLines: true</c> eklendi (n1 zaten veriyordu, n2 vermiyordu) —
    /// <c>SettingsDraftViewModel.cs</c>'in <c>matchTimeout</c> geçen yorum satırları (<c>:33,:68</c>, mevcut kod,
    /// değişmez) ileride "…projelerle **match**…" gibi bir cümleye evrilirse kuralı ANLATAN satırı ihlal
    /// SAYMAMALI.</para></summary>
    [Fact]
    public void Settings_shows_no_layer_match_counter()
    {
        // Non-vacuous: dar dosya adı deseni SESSİZCE sıfır dosya bulursa guard hep yeşil kalırdı.
        Assert.NotEmpty(SourceGuard.ScannedAppFiles("SettingsDialog.xaml*"));
        Assert.NotEmpty(SourceGuard.ScannedAppFiles("SettingsDraftViewModel.cs"));

        var offenders = SourceGuard.ScanApp("SettingsDialog.xaml*", LayerMatchCounter, skipCommentLines: true)
            .Concat(SourceGuard.ScanApp("SettingsDraftViewModel.cs", LayerMatchCounter, skipCommentLines: true)).ToList();
        Assert.Empty(offenders);
    }

    /// <summary>[A13/T4 · n3 · fix-1 · B1] design-v1 §8: <i>"'View failures' butonu yok."</i> — bu TAM metinli
    /// bir buton hiçbir yerde tanımlı DEĞİLDİR (ör. event stream'in "Completed — 5 failed …" satırından
    /// hatalara atlayan bir kısayol istenmemiştir).
    ///
    /// <para><b>fix-1 · B1:</b> n1'in deseniyle aynı non-vacuity assert'i eklendi — önceki mutasyon kanıtı
    /// (rapor) yalnız <c>.xaml</c> tarafını sınamıştı (<c>SettingsDialog.xaml</c>), <c>.cs</c> tarafının taranan
    /// küme olarak boş olmadığı hiç KANITLANMAMIŞTI.</para></summary>
    [Fact]
    public void No_view_failures_button_exists()
    {
        var csFiles = SourceGuard.ScannedAppFiles("*.cs");
        var xamlFiles = SourceGuard.ScannedAppFiles("*.xaml");
        Assert.NotEmpty(csFiles);   // [fix-1 · B1] non-vacuous
        Assert.NotEmpty(xamlFiles);

        var offenders = SourceGuard.ScanApp("*.cs", new Regex(Regex.Escape(ViewFailuresButton)))
            .Concat(SourceGuard.ScanApp("*.xaml", new Regex(Regex.Escape(ViewFailuresButton)))).ToList();
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
