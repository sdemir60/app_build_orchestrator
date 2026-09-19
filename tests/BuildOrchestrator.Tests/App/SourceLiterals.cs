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
    ///
    /// <para>[Task 8 fix round 1] <paramref name="holes"/> verilirse, BU çağrının TEK-seviye (üst)
    /// hole'larının İÇERİK aralığını (açılış <c>{</c>'den SONRA, kapanış <c>}</c>'den ÖNCE — iç içe
    /// <c>{}</c>'ler dahil TEK parça) doldurur. Sınır tespiti burada TEK yerde yaşar (kopya YASAK): <see
    /// cref="CodeOnly"/> hole İÇİNİ kod, dışını literal metin sayarken bunu KENDİ regex'iyle YENİDEN
    /// bulmaz, bu listeyi okur. Ham (<c>"""…"""</c>) dalı bu listeyi HİÇ doldurmaz — o dalda hole tespiti
    /// yoktur (bilinen sınır, bu kod tabanında raw+interpolated birleşimi kullanılmıyor).</para>
    /// </summary>
    private static int ReadCSharpString(string text, int start, List<(int Start, int End)>? holes = null)
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
        int holeStart = -1;                        // [Task 8 fix round 1] açık ÜST hole'un içerik başlangıcı
        while (i < n)
        {
            char c = text[i];

            if (!verbatim && c == '\\') { i += 2; continue; }                        // \" \\ kaçışı
            if (verbatim && c == '"' && i + 1 < n && text[i + 1] == '"') { i += 2; continue; } // "" kaçışı

            // [fix-2 · N1] `{{`/`}}` YALNIZ literalin METİN kısmında (depth 0) bir kaçıştır. Hole İÇİNDE
            // (depth > 0) onlar gerçek kod ayracıdır — lambda gövdesi, koleksiyon/nesne initializer'ı vb.
            // Kaçış saymak derinlik sayacını kaydırır, literal ERKEN kapanır ve C1'in çözdüğü semptom
            // (iç literalin taramaya hiç girmemesi) dar bir tetikleyiciyle geri gelir.
            if (interpolated && c == '{')
            {
                if (depth == 0 && i + 1 < n && text[i + 1] == '{') { i += 2; continue; } // {{ = düz '{'
                depth++;
                if (depth == 1) holeStart = i + 1;         // [Task 8] hole İÇERİĞİ '{' HEMEN sonrasında başlar
                i++; continue;
            }
            if (interpolated && c == '}')
            {
                if (depth == 0 && i + 1 < n && text[i + 1] == '}') { i += 2; continue; } // }} = düz '}'
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && holeStart >= 0)      // [Task 8] ÜST hole kapandı — tek parça olarak kaydet
                    {
                        holes?.Add((holeStart, i));
                        holeStart = -1;
                    }
                }
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
            // [fix-2 · N2] Ayraç soyma EŞLEŞME TÜRÜNE duyarlı olmak ZORUNDA. Ortak bir
            // Trim('"', '\'', '>', '<') içeriğin KENDİ tırnağını da yer: MSBuild'in en yaygın deyimi olan
            // Condition="'$(X)' == 'true'" böylece $(X)' == 'true diye KESİLİYORDU (gerçek dosyada
            // BuildOrchestrator.App.csproj:72/:84/:126 üzerinde doğrulandı). Her alternatifin açılış ve
            // kapanışı TEK karakterdir — CDATA hariç, onun ayraçları 9 ve 3 karakter.
            string value = m.Value.StartsWith("<![CDATA[", StringComparison.Ordinal)
                ? m.Value[9..^3]
                : m.Value[1..^1];
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

    /// <summary>
    /// [Faz 3/Task 8] <see cref="FromCSharp"/>'ın TERSİ: yorumları (<c>//</c>/<c>///</c>/<c>/* */</c>) ve
    /// string/char literallerinin METİN kısımlarını, satır sonlarını KORUYARAK boşluğa çevirir — geri kalan
    /// KOD olduğu gibi durur. Kaynak-tanımlayıcı guard'ları (ör. ürün adı sızıntısı) buradan beslenir: kural
    /// yalnız KODA uygulanmalı, yorum ve literal veriye DEĞİL.
    ///
    /// <para>Sınır tespiti (yorum/string/char nerede başlar-biter, iç içe interpolation dahil) <see
    /// cref="ReadCSharpString"/> ile AYNI tarayıcıdan gelir — bu tespit iki ayrı yerde YAZILMAZ (kopya YASAK).
    /// Naif bir regex bu ayrımı YAPAMAZ: <c>$"…{x ?? "iç"}…"</c> gibi bir interpolation hole'undaki iç string,
    /// dış literalin kapanışını erken sanıp geri kalan dosyayı kaydırırdı — <see cref="ReadCSharpString"/>'in
    /// var oluş sebebi tam olarak bu (bkz. sınıf özeti, fix-1 · C1).</para>
    ///
    /// <para>[Task 8 fix round 1] İnterpolated bir string'in <c>{…}</c> hole'u KOD taşır, literal metin
    /// DEĞİL (ör. <c>$"skip {OsysLegacyName}"</c> — <c>OsysLegacyName</c> gerçek bir tanımlayıcı kullanımıdır).
    /// Round 1 öncesi <see cref="ReadCSharpString"/>'in döndürdüğü TÜM aralık (hole dahil) boşluğa çevriliyordu
    /// ve bu, hole'daki bir ürün-adı sızıntısını guard'dan GİZLERDİ. Artık <see cref="ReadCSharpString"/>'in
    /// bildirdiği hole aralıkları (iç içe seviyeler dahil, <see cref="AppendStringWithCodeHoles"/> her hole
    /// içeriğini ÖZYİNELEMELİ olarak yeniden <see cref="CodeOnly"/>'den geçirir) KOD sayılıp OLDUĞU GİBİ
    /// bırakılır; yalnız hole'ların ARASINDAKİ/DIŞINDAKİ literal metin (ve ham string'ler — hole tespiti
    /// olmayan tek dal, bkz. <see cref="ReadCSharpString"/>) boşluğa çevrilir.</para>
    /// </summary>
    public static string CodeOnly(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];

            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                int start = i;
                while (i < n && text[i] != '\n') i++;
                sb.Append(Blanked(text, start, i));
                continue;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(n, i + 2);
                sb.Append(Blanked(text, start, i));
                continue;
            }
            if (c is '"' or '@' or '$')
            {
                var holes = new List<(int Start, int End)>();
                int end = ReadCSharpString(text, i, holes);
                if (end > i)
                {
                    AppendStringWithCodeHoles(sb, text, i, end, holes);
                    i = end;
                    continue;
                }
            }
            if (c == '\'')
            {
                int start = i;
                i++;
                while (i < n && text[i] != '\'' && text[i] != '\n') { if (text[i] == '\\') i++; i++; }
                i = Math.Min(n, i + 1);
                sb.Append(Blanked(text, start, i));
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>[Task 8 fix round 1] [<paramref name="start"/>, <paramref name="end"/>) aralığındaki bir
    /// string literalini yazar: <paramref name="holes"/>'un DIŞINDAKİ (hole'lar arası/öncesi/sonrası) metin
    /// <see cref="Blanked"/> ile boşluğa çevrilir, İÇİNDEKİ ise KOD sayılıp <see cref="CodeOnly"/>'den
    /// ÖZYİNELEMELİ geçirilir (bir hole'un içinde iç içe bir string/yorum olabilir — nested seviyeler böyle
    /// çözülür, ikinci bir parser YAZILMAZ). <paramref name="holes"/> boşsa (interpolated olmayan ya da ham
    /// string) davranış ESKİSİYLE AYNIDIR: tüm aralık literal metin sayılır.</summary>
    private static void AppendStringWithCodeHoles(
        System.Text.StringBuilder sb, string text, int start, int end, List<(int Start, int End)> holes)
    {
        int cursor = start;
        foreach (var (holeStart, holeEnd) in holes)
        {
            sb.Append(Blanked(text, cursor, holeStart));       // hole ÖNCESİ metin (açılış '{' dahil)
            sb.Append(CodeOnly(text[holeStart..holeEnd]));     // hole İÇERİĞİ: KOD, özyinelemeli
            cursor = holeEnd;
        }
        sb.Append(Blanked(text, cursor, end));                 // son hole SONRASI metin (kapanış '}' + kapanış tırnağı)
    }

    /// <summary>[<paramref name="start"/>, <paramref name="end"/>) aralığını, satır sonlarını KORUYARAK
    /// boşluğa çevirir.</summary>
    private static string Blanked(string text, int start, int end)
    {
        char[] blanked = new char[end - start];
        for (int k = 0; k < blanked.Length; k++)
        {
            char ch = text[start + k];
            blanked[k] = ch is '\n' or '\r' ? ch : ' ';
        }
        return new string(blanked);
    }
}
