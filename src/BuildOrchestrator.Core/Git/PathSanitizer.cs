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
            throw new ArgumentException("the branch name must not be empty/whitespace.", nameof(branch));

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
            throw new ArgumentException($"no safe slug could be derived from the branch name: '{branch}'.", nameof(branch));

        return slug;
    }

    /// <summary>
    /// v7 A6 auto-name kuralı: <c>slug + "-" + (aynı prefix'i paylaşan mevcut adlardaki en büyük sayı + 1)</c>.
    /// "Aynı prefix'i paylaşma" = ad birebir slug'a eşit VEYA <c>slug-&lt;sayı&gt;</c> kalıbına uyuyor
    /// (ör. slug=main iken "main" ve "main-2" sayılır; "main-experimental" gibi sayısal olmayan bir suffix
    /// SAYILMAZ — alakasız elle oluşturulmuş bir ad yanlışlıkla numaralandırmayı etkilemez). Karşılaştırma
    /// OrdinalIgnoreCase (Windows dosya sistemi case-insensitive). Sayı, basit bir count DEĞİL, eşleşen
    /// adlardan çıkarılan en büyük sayıdır: bare <c>slug</c> → 1, <c>slug-N</c> → N; sonuç = (max, yoksa 0) + 1.
    /// Bu, <paramref name="existing"/> içinde boşluk (gap) olsa bile — ör. yalnız "main-2" varken count-tabanlı
    /// formül "main-2" döndürüp mevcutla çakışırdı — sonucun HER ZAMAN <paramref name="existing"/>'de
    /// bulunmayan bir ad olmasını garanti eder. <paramref name="existing"/> boşsa sonuç "&lt;slug&gt;-1" olur
    /// (max 0 → 0+1=1); tek "main" varken "main-2" olur (bare main=1 → max 1 → 1+1=2) — brief'in örneğiyle
    /// birebir uyumlu.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="existing"/> null ise.</exception>
    public static string NextWorktreeName(string branch, IEnumerable<string> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        string slug = SanitizeBranchSlug(branch);

        int maxNumber = 0;
        foreach (string name in existing)
        {
            if (!SharesPrefix(name, slug)) continue;

            int number = string.Equals(name, slug, StringComparison.OrdinalIgnoreCase)
                ? 1
                : int.Parse(name[(slug.Length + 1)..]);

            if (number > maxNumber) maxNumber = number;
        }

        return $"{slug}-{maxNumber + 1}";
    }

    /// <summary>
    /// Bir adayın güvenli TEK path segmenti olup olmadığını doğrular — salt validator, mutasyon/throw yok
    /// (null dahil her girdi için güvenle çağrılabilir, yalnızca true/false döner). false döner: null/boş/
    /// whitespace, tam olarak `.`/`..`, path ayracı (`/` veya `\`) içeren, mutlak yol
    /// (<see cref="Path.IsPathRooted"/>), geçersiz Windows karakteri içeren, veya kontrol karakteri
    /// (&lt; 0x20) içeren adaylar için — bu son kural <see cref="SanitizeBranchSlug"/>'ın kontrol
    /// karakterlerini de `-`'ye çevirmesiyle simetriktir (Task 9 entegrasyonunda tutarlılık).
    /// </summary>
    public static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment == "." || segment == "..") return false;
        if (segment.IndexOfAny(Separators) >= 0) return false;
        if (segment.IndexOfAny(InvalidChars) >= 0) return false;
        if (Path.IsPathRooted(segment)) return false;
        if (segment.Any(char.IsControl)) return false;
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
