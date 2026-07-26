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
        => Assert.Empty(SourceGuard.ScanApp("*.xaml", ColourLiteral, AllowedFiles));

    [Fact]
    public void The_guard_actually_scans_the_xaml_files_it_claims_to()
    {
        // Tarama boş dönerse yukarıdaki test SESSİZCE yeşil kalırdı (yol/filtre bozulması). Bilinen iki
        // dosyanın taramaya girdiği ve istisnanın gerçekten var olduğu ayrıca doğrulanır.
        var scanned = SourceGuard.ScannedAppFiles("*.xaml");

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
    /// <item>[fix round 1 · A2] PAKETLENMİŞ ARGB sayısal literali (<c>0xFF3A3A42</c> / <c>0x3A3A42</c>) —
    /// hex string'in tırnaksız kardeşi; <c>unchecked((int)0xFF…)</c> yoluyla bir renge çevrilebilir ve önceki
    /// sürüm bunu HİÇ görmüyordu.</item>
    /// </list>
    ///
    /// <para><b>Paketlenmiş literal için dar ve gerekçeli izin:</b> <c>0x800401D0</c> — <c>CLIPBRD_E_CANT_OPEN</c>
    /// HRESULT'ı (<c>Console/ClipboardRetry.cs</c>), renk değil. İzin TEK bir değere verilir (dosyaya değil):
    /// aynı dosyaya eklenecek gerçek bir renk literali yine yakalanır. Yeni bir 6/8 haneli hex sabiti guard'ı
    /// kırmızıya çeker — bu BİLİNÇLİDİR: sayısal bir renk mi yoksa Win32 sabiti mi olduğu insan kararıdır.</para>
    /// </summary>
    private static readonly Regex CodeColourLiteral = new(
        "\"#[0-9a-fA-F]{3,8}\"" +
        "|Color\\.From(?:Rgb|Argb|ScRgb)\\(\\s*(?:0x[0-9a-fA-F]+|[0-9.]+f?)\\s*(?:,\\s*(?:0x[0-9a-fA-F]+|[0-9.]+f?)\\s*)*\\)" +
        "|(?<![A-Za-z0-9_])Colors\\.(?!Transparent\\b)[A-Z][A-Za-z]*" + // (?<!…): SystemColors.* WPF sistem fırçasıdır, renk literali değil
        "|0x(?!800401D0(?![0-9a-fA-F]))(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6})(?![0-9a-fA-F])",
        RegexOptions.Compiled);

    [Fact]
    public void No_cs_file_in_the_app_tree_declares_a_raw_colour()
        => Assert.Empty(SourceGuard.ScanApp("*.cs", CodeColourLiteral, skipCommentLines: true));

    [Fact]
    public void The_guard_actually_scans_the_cs_files_it_claims_to()
    {
        // XAML kardeşiyle aynı gerekçe: tarama boş dönerse yukarıdaki test SESSİZCE yeşil kalırdı. Sapmanın
        // GERÇEKTEN yaşandığı dosya (FontAbWindow code-behind'ı) taramada olduğu ayrıca doğrulanır.
        var scanned = SourceGuard.ScannedAppFiles("*.cs");

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
    [InlineData("unchecked((int)0xFF3A3A42)", true)]                       // [A2] paketlenmiş ARGB
    [InlineData("const uint Packed = 0x3A3A42;", true)]                    // [A2] paketlenmiş RGB
    [InlineData("const int CantOpen = unchecked((int)0x800401D0);", false)] // izinli: CLIPBRD_E_CANT_OPEN HRESULT
    [InlineData("private const int GlobalHotkeyId = 0xB0;", false)]        // kısa sabit — renk olamaz
    public void Code_regex_separates_colour_literals_from_lookalike_code(string sample, bool isColour)
        => Assert.Equal(isColour, CodeColourLiteral.IsMatch(sample));

    /// <summary>
    /// [fix round 1 · A2] <b>Guard'ın kendisinin kanıtı.</b> Yukarıdaki tarama testleri BUGÜN yeşil — yani
    /// "ihlal yok" ile "guard hiç ateşlemiyor" ayırt edilemez. Sahte bir kaynak dosyada ÜÇ ihlal kurulur ve
    /// ÜÇÜNÜN de (yalnız ilkinin değil — A2'nin ta kendisi) doğru satır numarasıyla raporlandığı doğrulanır.
    /// </summary>
    [Fact]
    public void The_guard_reports_every_violation_in_a_file_not_just_the_first()
    {
        const string fake = """
            using System.Windows.Media;
            internal static class Fake
            {
                private static readonly Brush A = Frozen("#2a2a30");
                private static readonly Color B = Color.FromRgb(58, 58, 66);
                // yorumdaki "#ffffff" sayılmaz
                private const uint C = 0xFF3A3A42;
            }
            """;

        var offenders = SourceGuard.ScanText("Fake.cs", fake, CodeColourLiteral, skipCommentLines: true);

        Assert.Equal(
            ["Fake.cs:4: \"#2a2a30\"", "Fake.cs:5: Color.FromRgb(58, 58, 66)", "Fake.cs:7: 0xFF3A3A42"],
            offenders);
    }

    /// <summary>
    /// [fix round 2] <b>ÇOK SATIRLI ihlal de yakalanmalı.</b> Round 1'in satır-bazlı taraması yeni bir kör
    /// nokta açmıştı: satıra bölünmüş bir <c>Color.FromArgb(...)</c> ya da property-element biçimli bir renk
    /// hiçbir satırda tam eşleşmediği için GÖRÜNMEZ oluyordu. İki dilde de kanıtlanır; rapor satırı eşleşmenin
    /// BAŞLADIĞI satırı gösterir ve çok satırlı metin tek satıra düzleştirilir.
    /// </summary>
    [Fact]
    public void The_guard_catches_a_violation_that_is_split_across_lines()
    {
        const string fakeCode = """
            internal static class Fake
            {
                private static readonly Color B = Color.FromRgb(
                    58, 58, 66);
            }
            """;
        var codeOffenders = SourceGuard.ScanText("Fake.cs", fakeCode, CodeColourLiteral, skipCommentLines: true);
        Assert.Equal(["Fake.cs:3: Color.FromRgb( 58, 58, 66)"], codeOffenders);

        const string fakeXaml = """
            <Border>
              <Border.Background>
                #0e0e10
              </Border.Background>
            </Border>
            """;
        var xamlOffenders = SourceGuard.ScanText("Fake.xaml", fakeXaml, ColourLiteral);
        Assert.Single(xamlOffenders);
        Assert.StartsWith("Fake.xaml:2: ", xamlOffenders[0]);
        Assert.Contains("#0e0e10", xamlOffenders[0]);
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
