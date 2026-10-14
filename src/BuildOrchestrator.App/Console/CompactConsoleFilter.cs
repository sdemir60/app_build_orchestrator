using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.5 — K4] Kompakt konsolun SAF süzgeci: bir işlemde projelerin MSBuild çıktısından
/// (<c>projectLog</c>) koşu anlatısına ne gideceğine karar verir. Yalnız proje çıktısına uygulanır — uygulamanın kendi anlatı
/// satırları ve motor notları süzülmez. Proje logu sayfası ve disk her zaman tamdır: süzgeç yalnız anlatıyı küçültür.
///
/// <para><b>Kurallar</b> (spec §6.5 tablosu): projenin işlemdeki ilk satırından önce tek başlık (<see cref="ProjectHeader"/>:
/// <c>building &lt;ad&gt;</c>); hata satırı aynen; uyarı satırı projenin ilk <see cref="ShownWarningsPerProject"/> TEKİL uyarısıysa
/// aynen, sonrası gizlenir ve koduna göre sayılır; diğer her satır (bilgi, komut, boş) gizlenir; proje bitince (her sonuç) gizlenen
/// uyarı varsa tek özet satırı (<see cref="ProjectDone"/>). Satırın türü <see cref="ConsoleLineClassifier.Classify"/>'ındır, uyarı
/// kodu <see cref="ConsoleLineClassifier.WarningCode"/>'undur — ikinci bir tanım yoktur.</para>
///
/// <para><b>Tekil sayım.</b> MSBuild her tanıyı bitiş özetinde bir kez daha yazar: girintili ve çoğu zaman sonunda
/// <c>[…\Proje.csproj]</c> ekiyle. Ham satır sayımı "N more warnings"ı gerçek sayının katlarına şişirirdi (son altı gerçek koşuda
/// toplamda 3,4×). Bu yüzden bir uyarının kimliği kırpılmış ve proje eki atılmış metnidir; görülmüş bir uyarının tekrarı ne yeniden
/// gösterilir ne sayılır — "ilk 3" ilk üç TEKİL uyarıdır. Hata hiç gizlenmez: tekrarı da geçer.</para>
///
/// <para><b>Bellek.</b> Bir uyarının kimliği metnin kendisi değil 64 bitlik parmak izidir (<see cref="Fingerprint"/>): binlerce
/// uyarılı bir projede metinleri tutmak log hacmiyle orantılı bellek olurdu (spec §5 ilke 1). Projenin sayacı özetiyle birlikte düşer;
/// özeti olmayan projede en çok <see cref="ShownWarningsPerProject"/> iz kalır ve yeni işlemde (<see cref="Reset"/>) o da gider.</para>
///
/// <para><b>Kilit yok:</b> süzgeç kendi başına thread-safe değildir; VM her çağrıyı kendi <c>_gate</c>'i altında yapar (satırlar IPC
/// thread'inden, proje sonuçları UI thread'inden gelir). Proje kimlikleri harf duyarsızdır (Windows yolu — VM'in bütün sözlükleri
/// gibi).</para>
/// </summary>
public sealed partial class CompactConsoleFilter
{
    /// <summary>Bir projenin anlatıda gösterilen en çok tekil uyarısı.</summary>
    public const int ShownWarningsPerProject = 3;

    /// <summary>Özet satırının adını verdiği en çok kod (çoktan aza, eşitlikte alfabetik); toplam sayı hepsini içerir.</summary>
    public const int SummaryCodes = 3;

    /// <summary>Kodu çıkarılamayan uyarının (uygulamanın kendi <c>warning:</c> satırı — bağımlılık uyarısı) özet kodu.</summary>
    private const string OtherCode = "other";

    // Özet satırının sabit parçaları: biçim (Summary) ve tanıma (IsSummary — sınıflandırıcının rengi) AYNI sabitlerden kurulur.
    private const string MoreWarnings = "more warning";
    private const string SummaryTail = " — see the project log";

    private readonly Dictionary<string, ProjectWarnings> _projects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bu işlemde başlığı yazılmış projeler — başlık işlem başına bir kezdir (döngü turları yinelemez); sayaçtan ayrı tutulur
    /// çünkü sayaç özetle birlikte düşer.</summary>
    private readonly HashSet<string> _headed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Projenin başlığı (<c>building &lt;ad&gt;</c>) — bu işlemde ilk soruluşunda; sonrasında <c>null</c>. VM onu projenin ilk
    /// satırıyla AYNI kilit bölümünde, satırdan hemen önce yazar: başlık kendi projesinin satırlarından sonra düşemez.</summary>
    public string? ProjectHeader(string projectId, string name) =>
        _headed.Add(projectId) ? "building " + name : null;

    /// <summary>Bir proje satırının anlatıdaki karşılığı: satırın kendisi ya da <c>null</c> (gizle).</summary>
    public string? LogLine(string projectId, string text) =>
        ConsoleLineClassifier.Classify(text) switch
        {
            ConsoleLineType.Error => text,
            ConsoleLineType.Warn => Warning(projectId, text),
            _ => null,
        };

    /// <summary>Proje bitti (başarı, hata ya da atlama): gizlenen uyarı varsa özet satırı, yoksa <c>null</c>. Özet üretilince projenin
    /// sayacı düşer — aynı sonucun tekrarı özeti ikilemez.</summary>
    public string? ProjectDone(string projectId, string name)
    {
        if (!_projects.TryGetValue(projectId, out var warnings) || warnings.Hidden == 0) return null;
        _projects.Remove(projectId);
        return Summary(name, warnings);
    }

    /// <summary>Yeni işlem: bütün sayaçlar ve başlıklar sıfırlanır.</summary>
    public void Reset()
    {
        _projects.Clear();
        _headed.Clear();
    }

    /// <summary>[tek tanım] Metin bu süzgecin özet satırı mı — <see cref="ConsoleLineClassifier"/> özet satırını bununla tanır ve uyarı
    /// rengini verir; biçimin ikinci bir literali yoktur. Önce ucuz kuyruk kontrolü: sınıflandırıcı her satır için sorar.</summary>
    public static bool IsSummary(string content) =>
        content.EndsWith(SummaryTail, StringComparison.Ordinal) && SummaryShape().IsMatch(content);

    private string? Warning(string projectId, string text)
    {
        if (!_projects.TryGetValue(projectId, out var warnings)) _projects[projectId] = warnings = new ProjectWarnings();
        if (!warnings.Seen.Add(Fingerprint(text))) return null; // tekrar (bitiş özeti): ne gösterilir ne sayılır
        if (warnings.Shown < ShownWarningsPerProject)
        {
            warnings.Shown++;
            return text;
        }
        string code = ConsoleLineClassifier.WarningCode(text) ?? OtherCode;
        warnings.HiddenByCode[code] = warnings.HiddenByCode.GetValueOrDefault(code) + 1;
        warnings.Hidden++;
        return null;
    }

    /// <summary><c>&lt;ad&gt;: &lt;n&gt; more warnings — CS1591 ×200, MSB3277 ×90 — see the project log</c> — tekil/çoğul ortak çekim
    /// kuralıyla (<see cref="StreamText.Counted"/>); sayılar InvariantCulture.</summary>
    private static string Summary(string name, ProjectWarnings warnings)
    {
        var codes = warnings.HiddenByCode
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(SummaryCodes)
            .Select(c => string.Format(CultureInfo.InvariantCulture, "{0} ×{1}", c.Key, c.Value));
        return string.Concat(name, ": ", StreamText.Counted(warnings.Hidden, MoreWarnings), " — ", string.Join(", ", codes),
            SummaryTail);
    }

    /// <summary>Bir uyarının tekil kimliği: kırpılmış metin, sonundaki <c>[…proj]</c> eki atılmış (bitiş özetinin kopyası satır içi
    /// kopyada eki taşımayabilir); SHA-256'nın ilk 8 baytı — ayırma yok, çakışma olasılığı ihmal edilir.</summary>
    private static ulong Fingerprint(string text)
    {
        ReadOnlySpan<char> line = text.AsSpan().Trim();
        if (line.EndsWith("proj]", StringComparison.OrdinalIgnoreCase))
        {
            int suffix = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (suffix >= 0) line = line[..suffix].TrimEnd();
        }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(MemoryMarshal.AsBytes(line), digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    /// <summary>Özet satırının şekli — <see cref="Summary"/>'nin ürettiğiyle AYNI sabitlerden.</summary>
    [GeneratedRegex("^.+: [0-9]+ " + MoreWarnings + "s? — .+" + SummaryTail + "$", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryShape();

    /// <summary>Bir projenin bu işlemdeki uyarı sayacı.</summary>
    private sealed class ProjectWarnings
    {
        /// <summary>Görülen tekil uyarıların parmak izleri (gösterilen ve gizlenen).</summary>
        public HashSet<ulong> Seen { get; } = [];

        /// <summary>Gösterilen tekil uyarı sayısı (≤ <see cref="ShownWarningsPerProject"/>).</summary>
        public int Shown { get; set; }

        /// <summary>Gizlenen tekil uyarı sayısı — özetin <c>n</c>'i.</summary>
        public int Hidden { get; set; }

        /// <summary>Gizlenen tekil uyarılar, koda göre.</summary>
        public Dictionary<string, int> HiddenByCode { get; } = new(StringComparer.Ordinal);
    }
}
