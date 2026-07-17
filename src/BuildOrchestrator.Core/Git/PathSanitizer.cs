using System.Linq;
using System.Text;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [T13] Path sanitization: git branch adından güvenli, filesystem-uyumlu worktree slug/adı türetimi.
/// Saf string mantığı — git çağrısı, dosya I/O YOK; tamamen deterministik, unit-testable. Path traversal
/// (`..`), mutlak yol enjeksiyonu ve geçersiz Windows dosya-adı karakterleri (`: * ? " &lt; &gt; |`)
/// burada ya temizlenir (<see cref="SanitizeBranchSlug"/> — best-effort, her zaman tek güvenli segment
/// üretmeye çalışır) ya da reddedilir (<see cref="IsSafeSegment"/> — salt validator, mutasyon yapmaz).
/// </summary>
public static class PathSanitizer
{
    /// <summary>Windows dosya/klasör adlarında yasak karakterler (sürücü harfi ayracı `:` dahil).</summary>
    private static readonly char[] InvalidChars = { ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Path ayraçları — segment sınırı taşıyan karakterler.</summary>
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>
    /// Branch adından tek-segment, filesystem-güvenli bir slug türetir: `/` ve `\` → `-`; geçersiz Windows
    /// karakterleri (<see cref="InvalidChars"/>) → `-`; ardışık `-` tekilleştirilir, baş/son `-` kırpılır.
    /// Boş/whitespace girdi VEYA sanitize sonrası boş/reserved (tam olarak `.` ya da `..`) kalan sonuç
    /// <see cref="ArgumentException"/> fırlatır — sessiz, yanıltıcı bir fallback (ör. traversal'a açık ".."
    /// adının olduğu gibi geçmesi) yerine net bir hata tercih edildi (tanımlı, test edilmiş sözleşme).
    /// </summary>
    public static string SanitizeBranchSlug(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            throw new ArgumentException("branch adı boş/whitespace olamaz.", nameof(branch));

        var replaced = new StringBuilder(branch.Trim());
        for (int i = 0; i < replaced.Length; i++)
        {
            char c = replaced[i];
            if (Array.IndexOf(Separators, c) >= 0 || Array.IndexOf(InvalidChars, c) >= 0 || char.IsControl(c))
                replaced[i] = '-';
        }

        // Ardışık '-' tekilleştir (ör. "/a//b\" → "-a--b-" → "-a-b-").
        var collapsed = new StringBuilder(replaced.Length);
        char prev = '\0';
        bool first = true;
        foreach (char c in replaced.ToString())
        {
            if (c == '-' && !first && prev == '-') continue;
            collapsed.Append(c);
            prev = c;
            first = false;
        }

        string slug = collapsed.ToString().Trim('-');

        if (slug.Length == 0 || slug == "." || slug == "..")
            throw new ArgumentException($"branch adından güvenli bir slug türetilemedi: '{branch}'.", nameof(branch));

        return slug;
    }

    /// <summary>
    /// v7 A6 auto-name kuralı: <c>slug + "-" + (aynı prefix'i paylaşan mevcut worktree sayısı + 1)</c>.
    /// "Aynı prefix'i paylaşma" = ad birebir slug'a eşit VEYA <c>slug-&lt;sayı&gt;</c> kalıbına uyuyor
    /// (ör. slug=main iken "main" ve "main-2" sayılır; "main-experimental" gibi sayısal olmayan bir suffix
    /// SAYILMAZ — alakasız elle oluşturulmuş bir ad yanlışlıkla numaralandırmayı etkilemez). Karşılaştırma
    /// OrdinalIgnoreCase (Windows dosya sistemi case-insensitive). <paramref name="existing"/> boşsa sonuç
    /// "&lt;slug&gt;-1" olur — brief'in tek verdiği örnek ("main → main-2 when one already exists", yani
    /// existing count=1 → main-2) formülün literal uzantısıdır (existing count=0 → suffix 0+1=1).
    /// </summary>
    public static string NextWorktreeName(string branch, IEnumerable<string> existing)
    {
        string slug = SanitizeBranchSlug(branch);
        int count = existing.Count(name => SharesPrefix(name, slug));
        return $"{slug}-{count + 1}";
    }

    /// <summary>
    /// Bir adayın güvenli TEK path segmenti olup olmadığını doğrular — salt validator, mutasyon/throw yok
    /// (null dahil her girdi için güvenle çağrılabilir, yalnızca true/false döner). false döner: null/boş/
    /// whitespace, tam olarak `.`/`..`, path ayracı (`/` veya `\`) içeren, mutlak yol
    /// (<see cref="Path.IsPathRooted"/>), veya geçersiz Windows karakteri içeren adaylar için.
    /// </summary>
    public static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment == "." || segment == "..") return false;
        if (segment.IndexOfAny(Separators) >= 0) return false;
        if (segment.IndexOfAny(InvalidChars) >= 0) return false;
        if (Path.IsPathRooted(segment)) return false;
        return true;
    }

    private static bool SharesPrefix(string name, string slug)
    {
        if (string.Equals(name, slug, StringComparison.OrdinalIgnoreCase)) return true;

        string dashPrefix = slug + "-";
        if (name.Length > dashPrefix.Length && name.StartsWith(dashPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = name[dashPrefix.Length..];
            return suffix.Length > 0 && suffix.All(char.IsDigit);
        }

        return false;
    }
}
