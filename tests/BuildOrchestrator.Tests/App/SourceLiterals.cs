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
    ///
    /// <para>[fix-1 · C1] Interpolated string'in SONU, <c>{…}</c> hole'ları sayılarak bulunur. Önceki sürüm
    /// ilk iç tırnakta duruyordu; <c>$"… {x ?? "iç metin"}"</c> gibi bir satırda <b>iç literal taramaya HİÇ
    /// girmiyordu</b> (canlı örnek: <c>GitService.cs</c>'in fetch fallback satırı) — oraya konan Türkçe
    /// guard'ı yeşil bırakırdı. Artık literalin metni hole'ların içeriğini de KAPSAR, yani iç içe her
    /// seviyedeki metin taranır.</para>
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
            if (c is '"' or '@' or '$')
            {
                int end = ReadCSharpString(text, i);
                if (end > i)
                {
                    result.Add(new SourceLiteral(LineOf(text, i), text[i..end]));
                    i = end;
                    continue;
                }
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

    /// <summary>
    /// <paramref name="start"/>'taki C# string literalinin BİTİŞ indisini (hariç) döner; orada bir literal
    /// yoksa <paramref name="start"/>'ı döner (ör. <c>@class</c> verbatim identifier'ı ya da tek başına
    /// <c>$</c>). Tüm biçimleri tanır: ham · verbatim · interpolated · düz, ve bunların bileşimleri.
    ///
    /// <para>Interpolated biçimde <c>{…}</c> hole'ları içindeki <b>iç içe string ve char literalleri</b>
    /// özyinelemeli olarak atlanır — kapanış tırnağının doğru yerde bulunması bunu gerektirir.</para>
    /// </summary>
    private static int ReadCSharpString(string text, int start)
    {
        int n = text.Length, i = start;
        bool interpolated = false, verbatim = false;

        while (i < n && text[i] is '$' or '@')                        // $ / @ / $@ / @$ / $$ önekleri
        {
            if (text[i] == '$') interpolated = true; else verbatim = true;
            i++;
        }
        if (i >= n || text[i] != '"') return start;                   // literal değil (ör. @identifier)

        // --- ham string: """ … """ (çit uzunluğu kadar tırnak). İçerik AYNEN alınır; iç tırnaklar serbesttir.
        if (i + 2 < n && text[i + 1] == '"' && text[i + 2] == '"')
        {
            int fenceLen = 0;
            while (i + fenceLen < n && text[i + fenceLen] == '"') fenceLen++;
            string fence = new('"', fenceLen);
            int close = text.IndexOf(fence, i + fenceLen, StringComparison.Ordinal);
            return close < 0 ? n : close + fenceLen;
        }

        i++;                                                          // açılış tırnağını geç
        int depth = 0;                                                // interpolation hole derinliği
        while (i < n)
        {
            char c = text[i];

            if (!verbatim && c == '\\') { i += 2; continue; }                        // \" \\ kaçışı
            if (verbatim && c == '"' && i + 1 < n && text[i + 1] == '"') { i += 2; continue; } // "" kaçışı

            if (interpolated && c == '{')
            {
                if (i + 1 < n && text[i + 1] == '{') { i += 2; continue; }           // {{ = düz '{'
                depth++; i++; continue;
            }
            if (interpolated && c == '}')
            {
                if (depth == 0 && i + 1 < n && text[i + 1] == '}') { i += 2; continue; } // }} = düz '}'
                if (depth > 0) depth--;
                i++; continue;
            }

            if (depth > 0)                                            // hole İÇİ: burası ifade, metin değil
            {
                if (c is '"' or '$' or '@')                           // iç içe string — özyinelemeli atla
                {
                    int nested = ReadCSharpString(text, i);
                    if (nested > i) { i = nested; continue; }
                }
                if (c == '\'')                                        // hole içindeki char literali
                {
                    i++;
                    while (i < n && text[i] != '\'' && text[i] != '\n') { if (text[i] == '\\') i++; i++; }
                    i++; continue;
                }
                i++; continue;
            }

            if (c == '"') return i + 1;                               // kapanış
            if (!verbatim && c == '\n') return i;                     // kesik/hatalı literal — satırda dur
            i++;
        }
        return n;
    }

    /// <summary>XML/XAML: <c>&lt;!-- --&gt;</c> yorumları elendikten sonra öznitelik değerleri ve eleman içi
    /// metinler. (<c>.csproj</c>/<c>.props</c> de buradan geçer — MSBuild <c>&lt;Error Text="..."/&gt;</c>
    /// metinleri kullanıcının build çıktısında görünür.)</summary>
    /// <para>[fix-1 · I1/I2] <c>&lt;![CDATA[…]]&gt;</c> blokları ve <b>tek tırnaklı</b> öznitelik değerleri
    /// (<c>Text='…'</c>) de kapsanır; önceki sürüm ikisini de hiç eşleştirmiyordu.</para>
    public static List<SourceLiteral> FromXml(string text)
    {
        // Yorumları satır sayısını BOZMADAN boşluğa çevir (satır no doğru kalsın).
        string stripped = Regex.Replace(text, "<!--[\\s\\S]*?-->", m => Regex.Replace(m.Value, "[^\r\n]", " "));
        var result = new List<SourceLiteral>();

        // CDATA ÖNCE gelir: içeriği ham metindir, içindeki tırnak/açılı ayraç öznitelik sanılmamalıdır.
        // Ardından çift ve tek tırnaklı öznitelik değerleri, en son eleman içi metin.
        const string pattern = @"<!\[CDATA\[[\s\S]*?\]\]>|""[^""]*""|'[^']*'|>[^<>]+<";
        foreach (Match m in Regex.Matches(stripped, pattern))
        {
            string value = m.Value.StartsWith("<![CDATA[", StringComparison.Ordinal)
                ? m.Value[9..^3]
                : m.Value.Trim('"', '\'', '>', '<');
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
            // [fix-1 · C2] here-string: @" … \n"@  /  @' … \n'@
            // Kapanış PowerShell'de SATIR BAŞINDA olmak ZORUNDADIR, bu yüzden "\n"@" aranır. Önceki sürüm
            // here-string'i hiç tanımıyordu: gövdedeki tek bir tırnak tokenizer'ı kaydırıyor ve ARDINDAN
            // gelen KOD metin sanılıyordu (sonraki gerçek literaller de kayıyordu).
            if (c == '@' && i + 1 < n && stripped[i + 1] is '"' or '\'')
            {
                char fence = stripped[i + 1];
                int start = i;
                string terminator = "\n" + fence + "@";
                int close = stripped.IndexOf(terminator, i + 2, StringComparison.Ordinal);
                i = close < 0 ? n : close + terminator.Length;
                result.Add(new SourceLiteral(LineOf(stripped, start), stripped[start..i]));
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
