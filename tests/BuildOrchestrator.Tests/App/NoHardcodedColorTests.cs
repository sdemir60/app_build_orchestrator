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
