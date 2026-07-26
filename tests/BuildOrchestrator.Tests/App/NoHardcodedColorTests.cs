using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 · T64] App projesindeki HER XAML dosyasında ham renk literali kalmadığını pinler (token zorunluluğu).
/// Dosya listesi ELLE tutulmaz, kaynak ağacı TARANIR (bkz. <see cref="RepoPaths.AppSourceFiles"/>) — elle
/// tutulan liste hem <c>Spikes/FontAbWindow.xaml</c>'ı gözden kaçırmıştı (orada <c>Brush.SurfaceBase</c>'in
/// birebir kopyası olan bir literal duruyordu) hem de B3-E6'nın ekleyeceği her yeni XAML'ı listeyi
/// genişletmeyi hatırlayan birine bağlı bırakıyordu.
///
/// <para>TEK istisna <see cref="AllowedFiles"/>'dadır: <c>Resources/Tokens.xaml</c> — renk literalleri oraya
/// AİTTİR. Bu testin garantisi budur: "token sözlüğü dışında hiçbir uygulama XAML'ı renk literali yazmaz".</para>
///
/// <para>[T49 FINAL PASS] Tarama ARTIK <c>*.cs</c>'i de kapsar. Gerekçe DRIFT: <c>Spikes/FontAbWindow.xaml.cs</c>
/// code-behind'ında 12 ham hex duruyordu ve biri (<c>BorderStrong</c>) <c>--border-strong</c> yerine
/// <c>--border</c> değerini taşıyordu — yalnız XAML tarandığı için sapma HİÇ görünmedi. Kapsam boşluğu bu testin
/// kendi sınıf özetinde anlattığı hatanın (elle tutulan liste) code-behind sürümüydü.</para>
/// </summary>
public sealed class NoHardcodedColorTests
{
    /// <summary>Renk literali yazması MEŞRU olan dosyalar (App kaynak köküne göreli, tam eşleşme).</summary>
    private static readonly string[] AllowedFiles =
    [
        Path.Combine("Resources", "Tokens.xaml"),   // tasarım token'larının TEK tanım yeri (T49)
    ];

    /// <summary>
    /// WPF'in kabul ettiği renk literali biçimleri: attribute değeri (<c>Background="#0e0e10"</c>,
    /// <c>Color="#AARRGGBB"</c>) ve property-element metni (<c>&lt;X.Background&gt;#0e0e10&lt;/&gt;</c>),
    /// ayrıca scRGB (<c>sc#1,0,0</c>). Tırnak/açı-parantez sınırlayıcısı ZORUNLUDUR: çıplak bir <c>#[0-9a-f]{3,8}</c>
    /// deseni yorumlardaki madde/issue numaralarını (<c>Ek A #3</c>, <c>dotnet/wpf#293</c>) yanlış yakalardı —
    /// yani "daha geniş" ile "yanlış" arasındaki sınır burasıdır. İsimli renkler (<c>Red</c>) BİLEREK kapsam
    /// dışıdır: <c>Transparent</c> gibi meşru kullanımlardan ayırt edilemezler.
    /// </summary>
    private static readonly Regex ColourLiteral = new(
        "(?:\"|>)\\s*(?:#[0-9a-fA-F]{3,8}|sc#[0-9.,\\s]+)\\s*(?:\"|<)", RegexOptions.Compiled);

    [Fact]
    public void No_xaml_outside_the_token_dictionary_declares_a_raw_colour()
    {
        var offenders = new List<string>();

        foreach (string file in RepoPaths.AppSourceFiles("*.xaml"))
        {
            string relative = Path.GetRelativePath(RepoPaths.AppSrcRoot, file);
            if (AllowedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;

            var match = ColourLiteral.Match(File.ReadAllText(file));
            if (match.Success) offenders.Add($"{relative}: {match.Value.Trim()}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_guard_actually_scans_the_xaml_files_it_claims_to()
    {
        // Tarama boş dönerse yukarıdaki test SESSİZCE yeşil kalırdı (yol/filtre bozulması). Bilinen iki
        // dosyanın taramaya girdiği ve istisnanın gerçekten var olduğu ayrıca doğrulanır.
        var scanned = RepoPaths.AppSourceFiles("*.xaml")
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Contains(Path.Combine("Resources", "Icons.xaml"), scanned);
        Assert.Contains(Path.Combine("Spikes", "FontAbWindow.xaml"), scanned);
        Assert.All(AllowedFiles, a => Assert.Contains(a, scanned));
    }

    /// <summary>
    /// [T49 FINAL PASS] <c>*.cs</c> tarafındaki renk literali biçimleri — üç ayrı kalıp, üçü de YASAK:
    /// <list type="number">
    /// <item>hex string literali (<c>"#3a3a42"</c>) — <c>ColorConverter</c>/<c>Brush</c> yolu;</item>
    /// <item>SABİT argümanlı <c>Color.FromRgb(58, 58, 66)</c> / <c>FromArgb</c> / <c>FromScRgb</c> — TÜREV
    /// çağrılar (bir token renginden alfa/kanal taşıyanlar, ör. <c>Color.FromArgb(a, color.R, …)</c>) meşrudur
    /// ve BİLEREK kapsam dışıdır: literal olmayan argüman deseni eşleşmez;</item>
    /// <item>isimli renk (<c>Colors.Red</c>) — TEK istisna <c>Colors.Transparent</c>'tır (renk değil, "yok"
    /// anlamına gelen nötr taban; per-instance animasyon fırçalarının başlangıç değeri, A13.2).</item>
    /// </list>
    /// </summary>
    private static readonly Regex CodeColourLiteral = new(
        "\"#[0-9a-fA-F]{3,8}\"" +
        "|Color\\.From(?:Rgb|Argb|ScRgb)\\(\\s*(?:0x[0-9a-fA-F]+|[0-9.]+f?)\\s*(?:,\\s*(?:0x[0-9a-fA-F]+|[0-9.]+f?)\\s*)*\\)" +
        "|(?<![A-Za-z0-9_])Colors\\.(?!Transparent\\b)[A-Z][A-Za-z]*", // (?<!…): SystemColors.* WPF sistem fırçasıdır, renk literali değil
        RegexOptions.Compiled);

    [Fact]
    public void No_cs_file_in_the_app_tree_declares_a_raw_colour()
    {
        var offenders = new List<string>();

        foreach (string file in RepoPaths.AppSourceFiles("*.cs"))
        {
            var match = CodeColourLiteral.Match(File.ReadAllText(file));
            if (match.Success)
                offenders.Add($"{Path.GetRelativePath(RepoPaths.AppSrcRoot, file)}: {match.Value.Trim()}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_guard_actually_scans_the_cs_files_it_claims_to()
    {
        // XAML kardeşiyle aynı gerekçe: tarama boş dönerse yukarıdaki test SESSİZCE yeşil kalırdı. Sapmanın
        // GERÇEKTEN yaşandığı dosya (FontAbWindow code-behind'ı) taramada olduğu ayrıca doğrulanır.
        var scanned = RepoPaths.AppSourceFiles("*.cs")
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Contains(Path.Combine("Spikes", "FontAbWindow.xaml.cs"), scanned);
        Assert.Contains("MainWindow.xaml.cs", scanned);
        Assert.Contains(Path.Combine("Controls", "IconPaint.cs"), scanned);
    }

    [Theory]
    [InlineData("Frozen(\"#2a2a30\")", true)]                              // DRIFT'in kendisi: ham hex string
    [InlineData("Color.FromRgb(58, 58, 66)", true)]                        // sabit kanal değerleri
    [InlineData("Color.FromArgb(0x52, 0xED, 0xA1, 0x0F)", true)]           // sabit kanal değerleri (hex)
    [InlineData("Foreground = Brushes.Red;", false)]                       // isimli renk — Colors.* dışı form, kapsam dışı
    [InlineData("Colors.Red", true)]                                       // isimli renk
    [InlineData("Colors.Transparent", false)]                              // nötr taban — TEK meşru isimli renk
    [InlineData("Color.FromArgb((byte)(color.A * bucket / 255), color.R, color.G, color.B)", false)] // türev
    [InlineData("Token(\"Brush.BorderStrong\")", false)]                   // doğrusu: token'dan çöz
    [InlineData("// dotnet/wpf#293 — letter-spacing yok", false)]          // yorumdaki issue numarası
    public void Code_regex_separates_colour_literals_from_lookalike_code(string sample, bool isColour)
        => Assert.Equal(isColour, CodeColourLiteral.IsMatch(sample));

    [Theory]
    [InlineData("Background=\"#0e0e10\"", true)]                      // attribute, 6 haneli
    [InlineData("<SolidColorBrush Color=\"#99040406\" />", true)]     // Color attribute, 8 haneli (alfa)
    [InlineData("<Border.Background>#0e0e10</Border.Background>", true)] // property-element metni
    [InlineData("Background=\"sc# 1.0, 0.5, 0.0\"", true)]            // scRGB
    [InlineData("Background=\"{DynamicResource Brush.SurfaceBase}\"", false)]
    [InlineData("<!-- [3b/Ek A #3] copy log -->", false)]             // yorumdaki madde numarası
    [InlineData("<!-- letter-spacing yok (dotnet/wpf#293) -->", false)] // yorumdaki issue numarası
    public void Regex_separates_colour_literals_from_lookalike_text(string sample, bool isColour)
        => Assert.Equal(isColour, ColourLiteral.IsMatch(sample));
}
