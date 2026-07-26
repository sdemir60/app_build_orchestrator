using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 FINAL PASS] <c>Spikes/FontAbWindow</c> (font A/B karar penceresi, <c>--font-ab</c>) paletini artık
/// token sözlüğünden çözer. Pencere HİÇBİR testte instantiate edilemez (gerçek bir <see cref="Application"/>
/// ister) — yani bir anahtar adı sürüklenirse hata YALNIZ o pencere açılınca görünürdü. Bu test o boşluğu
/// kaynak üzerinden kapatır: kullanılan HER anahtar Tokens.xaml'de gerçekten VAR ve bir fırçadır.
///
/// <para><b>Neden ayrı bir sınıf:</b> tam da bu dosyada bir DRIFT yaşandı — <c>BorderStrong</c> adlı sabit
/// <c>--border-strong</c> (#3a3a42) yerine <c>--border</c> (#2a2a30) değerini taşıyordu. Aşağıdaki son iddia o
/// sapmanın ta kendisini pinler: iki token AYNI değere sahip olamaz.</para>
/// </summary>
public sealed class FontAbWindowTokenKeysTests
{
    private static readonly Regex TokenCall = new("Token\\(\"(?<key>[^\"]+)\"\\)", RegexOptions.Compiled);

    private static List<string> KeysUsedByTheSpike()
    {
        string source = File.ReadAllText(
            Path.Combine(RepoPaths.AppSrcRoot, "Spikes", "FontAbWindow.xaml.cs"));
        return TokenCall.Matches(source).Select(m => m.Groups["key"].Value).Distinct().ToList();
    }

    [StaFact]
    public void Every_token_key_the_font_ab_spike_resolves_exists_in_the_token_dictionary()
    {
        var keys = KeysUsedByTheSpike();
        Assert.NotEmpty(keys); // tarama boşsa test SESSİZCE yeşil kalırdı

        var tokens = DsResources.Load("Tokens.xaml");
        var missing = keys.Where(k => tokens[k] is not SolidColorBrush).ToList();

        Assert.Empty(missing);
    }

    [StaFact]
    public void The_spike_uses_border_strong_which_is_a_distinct_tone_from_border()
    {
        Assert.Contains("Brush.BorderStrong", KeysUsedByTheSpike());

        var tokens = DsResources.Load("Tokens.xaml");
        var strong = ((SolidColorBrush)tokens["Brush.BorderStrong"]).Color;
        var border = ((SolidColorBrush)tokens["Brush.Border"]).Color;

        // DRIFT'İN KENDİSİ: spike bu iki tonu karıştırmıştı (--border-strong yerine --border yazılıydı).
        Assert.NotEqual(border, strong);
        Assert.Equal(Color.FromRgb(0x3a, 0x3a, 0x42), strong); // tokens/colors.css:19 --border-strong
    }
}
