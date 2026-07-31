using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>Kaynaktan çıkarılmış TEK bir metin parçası — <see cref="Line"/> dosyadaki 1-tabanlı satırdır.</summary>
internal readonly record struct SourceLiteral(int Line, string Text);

/// <summary>
/// [A13/B2] Kaynak dosyadan <b>KULLANICIYA ULAŞABİLECEK metin parçalarını</b> çıkarır: C# string literalleri,
/// XML/XAML öznitelik değerleri ve eleman içi metinler, PowerShell string literalleri.
///
/// <para><b>Neden ham metin taraması YETMEZ:</b> bu projenin kod yorumları TASARIM GEREĞİ Türkçedir
/// (CLAUDE.md). <c>src/</c> altında Türkçe karakter için ham bir <c>grep</c> 174 dosyada 6346 isabet verir ve
/// bunların neredeyse tamamı yorumdur. Yorumları "satır başı <c>//</c> mı" diye elemek de yetmez: satır sonu
/// yorumları (<c>Foo(); // Türkçe not</c>) kodun BAŞLADIĞI satırda durur ve elenemez. Bu yüzden guard, metni
/// satır/regex ile değil, <b>küçük bir tokenizer</b> ile ayrıştırır — yorum bir daha asla taramaya giremez.</para>
///
/// <para>Tokenizer kasıtlı olarak küçüktür (tam bir C# lexer'ı DEĞİL): yalnız yorum/string/char sınırlarını
/// doğru tanıması gerekir, çünkü tek işi "bu bayt bir yorumun mu yoksa bir metnin mi içinde" sorusuna
/// cevap vermektir.</para>
/// </summary>
internal static class SourceLiterals
{
    /// <summary>Uzantıya göre doğru çıkarıcıyı seçer. Bilinmeyen uzantı → BOŞ (sessiz yanlış-pozitif yerine
    /// hiç tarama; hangi uzantıların tarandığı guard testinde AÇIKÇA assert edilir).</summary>
    public static IReadOnlyList<SourceLiteral> From(string text, string extension) => extension.ToLowerInvariant() switch
    {
        ".cs" => FromCSharp(text),
        ".xaml" or ".csproj" or ".props" or ".targets" => FromXml(text),
        ".ps1" => FromPowerShell(text),
        _ => [],
    };

    /// <summary>
    /// C#: <c>"..."</c>, <c>$"..."</c>, <c>@"..."</c>, <c>$@"..."</c> ve ham (<c>"""..."""</c>) literaller.
    /// <c>//</c> · <c>///</c> · <c>/* */</c> yorumları ve <c>'c'</c> char literalleri ATLANIR.
    /// </summary>
    public static List<SourceLiteral> FromCSharp(string text)
    {
        var result = new List<SourceLiteral>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];

            if (c == '/' && i + 1 < n && text[i + 1] == '/')          // satır yorumu (/// dahil)
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '*')          // blok yorumu
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            if (c == '"' && i + 2 < n && text[i + 1] == '"' && text[i + 2] == '"')   // ham string """..."""
            {
                int fenceLen = 0;
                while (i + fenceLen < n && text[i + fenceLen] == '"') fenceLen++;
                string fence = new('"', fenceLen);
                int start = i;
                int end = text.IndexOf(fence, i + fenceLen, StringComparison.Ordinal);
                end = end < 0 ? n : end + fenceLen;
                result.Add(new SourceLiteral(LineOf(text, start), text[start..end]));
                i = end;
                continue;
            }
            if (c is '@' or '$')                                       // @"..." / $@"..." / @$"..."
            {
                int j = i;
                while (j < n && text[j] is '@' or '$') j++;
                bool verbatim = text.AsSpan(i, j - i).Contains('@');
                if (j < n && text[j] == '"' && verbatim)
                {
                    int start = i;
                    j++;
                    while (j < n)
                    {
                        if (text[j] == '"' && j + 1 < n && text[j + 1] == '"') { j += 2; continue; } // "" kaçışı
                        if (text[j] == '"') break;
                        j++;
                    }
                    result.Add(new SourceLiteral(LineOf(text, start), text[start..Math.Min(n, j + 1)]));
                    i = j + 1;
                    continue;
                }
            }
            if (c == '"')                                              // "..." / $"..."
            {
                int start = i;
                i++;
                while (i < n && text[i] != '"' && text[i] != '\n')
                {
                    if (text[i] == '\\') i++;
                    i++;
                }
                result.Add(new SourceLiteral(LineOf(text, start), text[start..Math.Min(n, i + 1)]));
                i++;
                continue;
            }
            if (c == '\'')                                             // char literali — metin DEĞİL, atla
            {
                i++;
                while (i < n && text[i] != '\'' && text[i] != '\n') { if (text[i] == '\\') i++; i++; }
                i++;
                continue;
            }
            i++;
        }
        return result;
    }

    /// <summary>XML/XAML: <c>&lt;!-- --&gt;</c> yorumları elendikten sonra öznitelik değerleri ve eleman içi
    /// metinler. (<c>.csproj</c>/<c>.props</c> de buradan geçer — MSBuild <c>&lt;Error Text="..."/&gt;</c>
    /// metinleri kullanıcının build çıktısında görünür.)</summary>
    public static List<SourceLiteral> FromXml(string text)
    {
        // Yorumları satır sayısını BOZMADAN boşluğa çevir (satır no doğru kalsın).
        string stripped = Regex.Replace(text, "<!--[\\s\\S]*?-->", m => Regex.Replace(m.Value, "[^\r\n]", " "));
        var result = new List<SourceLiteral>();
        foreach (Match m in Regex.Matches(stripped, "\"[^\"]*\"|>[^<>]+<"))
        {
            string value = m.Value.Trim('"', '>', '<');
            if (value.Trim().Length == 0) continue;
            result.Add(new SourceLiteral(LineOf(stripped, m.Index), value));
        }
        return result;
    }

    /// <summary>PowerShell: <c>#</c> yorumları ve <c>&lt;# #&gt;</c> blok yorumları elendikten sonra
    /// <c>'...'</c> / <c>"..."</c> literalleri.</summary>
    public static List<SourceLiteral> FromPowerShell(string text)
    {
        string stripped = Regex.Replace(text, "<#[\\s\\S]*?#>", m => Regex.Replace(m.Value, "[^\r\n]", " "));
        var result = new List<SourceLiteral>();
        int i = 0, n = stripped.Length;
        while (i < n)
        {
            char c = stripped[i];
            if (c == '#')                                              // satır yorumu
            {
                while (i < n && stripped[i] != '\n') i++;
                continue;
            }
            if (c is '"' or '\'')
            {
                char quote = c;
                int start = i;
                i++;
                while (i < n)
                {
                    if (stripped[i] == quote && i + 1 < n && stripped[i + 1] == quote) { i += 2; continue; } // '' / "" kaçışı
                    if (stripped[i] == quote) break;
                    if (quote == '"' && stripped[i] == '`') i++;       // backtick kaçışı yalnız "..." içinde
                    i++;
                }
                result.Add(new SourceLiteral(LineOf(stripped, start), stripped[start..Math.Min(n, i + 1)]));
                i++;
                continue;
            }
            i++;
        }
        return result;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;
}
