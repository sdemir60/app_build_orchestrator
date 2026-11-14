using System.Text;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>[files-in-use] Bir <c>cmd</c> kopya komutu (<c>copy</c>/<c>xcopy</c>): kaynaklar ve hedef, komutta YAZILDIĞI gibi —
/// göreli yol ve <c>%DEĞİŞKEN%</c> burada çözülmez; komutun çalıştığı klasörü bilen <c>CopyTargets</c> çözer.</summary>
public sealed record CopyCommand(IReadOnlyList<string> Sources, string Destination);

/// <summary>
/// [files-in-use] Bir projenin kopya komutunu MSBuild çıktısının TEK bir satırından okur. İki kaynak vardır:
/// <list type="bullet">
/// <item><see cref="FromEcho"/> — normal verbosity'de <c>Exec</c> çalıştırdığı komutu hedefin başlığı altında (ör.
/// <c>PostBuildEvent:</c>) yankılar; komut başarılı olsa da yankı vardır. İki satırlı bir olayın İLK satırı düşüp son satırı
/// başarılı olunca MSBuild hata vermez (exit 0) — o durumda komutu yalnız yankı söyler.</item>
/// <item><see cref="FromMsb3073"/> — <c>Exec</c> sıfırdan farklı bittiğinde hata satırı komutu taşır; verbosity düşükken de
/// durur. Mesajın sarmalayıcısı ve kuyruğu YERELLEŞTİRİLMİŞTİR (<c>The command "…" exited with code 1</c> /
/// <c>"…" komutundan 1 koduyla çıkıldı</c>), bu yüzden okunmaz: yalnız fiilden sonraki TIRNAKLI mutlak yollar alınır.</item>
/// </list>
/// Kapsam yalnız <c>cmd</c>'nin kopyasıdır. MSBuild'in Copy görevi (<c>Copying file from …</c>, <c>MSB302x</c>) komut değildir;
/// birleştirme (<c>copy a+b c</c>), kaynaksız ya da hedefsiz komut okunmaz.
/// </summary>
public static partial class CopyCommandReader
{
    // Yankı: trim'li satır fiille başlar ve fiili boşluk izler ("copying", "copyright", "robocopy" fiil değildir).
    [GeneratedRegex(@"^x?copy\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EchoVerb();

    [GeneratedRegex(@"\bMSB3073\b", RegexOptions.CultureInvariant)]
    private static partial Regex Msb3073Code();

    // MSB3073 içinde fiil: kelime karakteri ya da ters bölü ardından GELMEYEN (yol parçası "\copy\" fiil değildir) ve boşlukla
    // izlenen copy/xcopy. İlk satırda sarmalayıcının tırnağı onu izler; devam satırlarında doğrudan "MSB3073: " sonrasıdır.
    [GeneratedRegex(@"(?<![\w\\])x?copy(?=\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Msb3073Verb();

    // Tırnaklı mutlak yol: sürücü harfli (C:\…) ya da UNC (\\sunucu\paylaşım\…).
    [GeneratedRegex(@"""((?:[A-Za-z]:\\|\\\\)[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedAbsolutePath();

    /// <summary>Yankılanan bir komut satırı; komut değilse <c>null</c>.</summary>
    public static CopyCommand? FromEcho(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        string text = line.Trim();
        if (!EchoVerb().IsMatch(text)) return null;
        if (Tokenize(text) is not { Count: > 0 } tokens) return null;
        // İlk belirteç fiildir; "/" ile başlayanlar anahtardır (/y, /i …) — kalanlar yol.
        var paths = tokens.Skip(1).Where(t => t.Length > 0 && t[0] != '/').ToList();
        return paths.Count < 2 ? null : new CopyCommand(paths[..^1], paths[^1]);
    }

    /// <summary>Bir <c>MSB3073</c> satırı; satır bir kopya komutu taşımıyorsa ya da yolları tırnaksızsa <c>null</c>.</summary>
    public static CopyCommand? FromMsb3073(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var code = Msb3073Code().Match(line);
        if (!code.Success) return null;
        var verb = Msb3073Verb().Match(line, code.Index + code.Length);
        if (!verb.Success) return null;
        var paths = QuotedAbsolutePath().Matches(line, verb.Index + verb.Length).Select(m => m.Groups[1].Value).ToList();
        return paths.Count < 2 ? null : new CopyCommand(paths[..^1], paths[^1]);
    }

    /// <summary>
    /// <c>cmd</c>'nin kuralıyla belirteçlere ayırır: <c>"</c> tırnağı açar/kapar (ters bölü kaçış DEĞİLDİR), tırnak dışındaki boşluk
    /// belirteci bitirir. Tırnak dışındaki <c>+</c> birleştirmedir → okunmaz (<c>null</c>). Tırnak dışındaki <c>&gt;</c> <c>&lt;</c>
    /// <c>|</c> <c>&amp;</c> komutu bitirir (yönlendirme ya da ikinci komut); yönlendirmenin önündeki akış numarası (<c>2&gt;nul</c>)
    /// atılır. Kapanmamış tırnak → <c>null</c>.
    /// </summary>
    private static List<string>? Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, started = false;
        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                started = true;
                continue;
            }
            if (!inQuotes)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (started) { tokens.Add(current.ToString()); current.Clear(); started = false; }
                    continue;
                }
                if (c == '+') return null;
                if (c is '>' or '<' or '|' or '&')
                {
                    if (started && !current.ToString().All(char.IsAsciiDigit)) tokens.Add(current.ToString());
                    return tokens;
                }
            }
            current.Append(c);
            started = true;
        }
        if (inQuotes) return null;
        if (started) tokens.Add(current.ToString());
        return tokens;
    }
}

/// <summary>
/// [files-in-use · son inceleme I2] Bir kopyanın gerçekten DENENİP bir dosya kilidine çarptığının kanıtı: <c>cmd</c> (ve <c>xcopy</c>'nin
/// "File creation error - …" satırı) hatayı sistemin kendi metniyle yazar — Win32'nin 32 (paylaşım ihlali) ve 33 (kilit ihlali) numaralı
/// hatalarının Windows'un dilindeki iletisi. Metin motorun içinden aynı kaynaktan okunur (<see cref="System.ComponentModel.Win32Exception"/>);
/// İngilizcesi de her zaman tanınır. Kanıt yoksa (yankılanan satır çalışmayan bir <c>if</c> bloğundaydı, <c>xcopy /d</c> dosyayı bilerek
/// atladı) kilitli kopya kararı hiç verilmez.
/// </summary>
public static class SharingViolation
{
    private static readonly string[] Texts =
    [
        .. new[]
        {
            new System.ComponentModel.Win32Exception(32).Message,
            new System.ComponentModel.Win32Exception(33).Message,
            "The process cannot access the file because it is being used by another process",
            "The process cannot access the file because another process has locked a portion of the file",
        }
        .Select(t => t.Trim().TrimEnd('.').Trim())
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Satır bir paylaşım ya da kilit ihlali iletisi taşıyor mu.</summary>
    public static bool IsReported(string line) =>
        !string.IsNullOrEmpty(line) && Texts.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// [files-in-use] Bir denemenin çıktısındaki kopya komutlarını toplar: her satırı önce yankı, sonra <c>MSB3073</c> olarak dener;
/// aynı komut (kaynaklar + hedef, büyük/küçük harf duyarsız) bir kez ve ilk görüldüğü sırada kalır — yankı ile hata satırı aynı
/// komutu iki kez söyler. Çıktıda bir paylaşım ya da kilit ihlali görüldüyse onu da not eder (<see cref="SawSharingViolation"/>).
/// Satırlar MSBuild'in stdout/stderr pompalarından gelir; toplayıcı kilitle korunur.
/// </summary>
public sealed class CopyCommandCollector
{
    private readonly object _gate = new();
    private readonly List<CopyCommand> _commands = [];
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private int _violation;

    public void Observe(string line)
    {
        if (SharingViolation.IsReported(line)) Interlocked.Exchange(ref _violation, 1);
        if ((CopyCommandReader.FromEcho(line) ?? CopyCommandReader.FromMsb3073(line)) is not { } command) return;
        string key = string.Join('\u001f', command.Sources) + '\u001e' + command.Destination;
        lock (_gate)
        {
            if (_keys.Add(key)) _commands.Add(command);
        }
    }

    /// <summary>Denemenin çıktısında bir paylaşım ya da kilit ihlali iletisi görüldü mü (<see cref="SharingViolation"/>).</summary>
    public bool SawSharingViolation => Volatile.Read(ref _violation) == 1;

    /// <summary>Şimdiye kadar görülen komutlar (anlık kopya).</summary>
    public IReadOnlyList<CopyCommand> Commands
    {
        get { lock (_gate) return [.. _commands]; }
    }
}
