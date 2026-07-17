using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Incremental;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T25][A6][D6] Byte-stable proje imzası — incremental build kararının çekirdeği (bkz. plan v7 D6/A6).
/// Signature = <c>configuration</c> + <c>headCommit</c> + (YALNIZ in-place modda) <c>local-diff hash</c> +
/// transitive upstream producer imzaları. Aynı girdi kümesi HER ZAMAN byte-identik SHA256 hex string üretir
/// (determinism testli) ve girdi listelerinin (dirty dosyalar, upstream id'ler) SIRASI SONUCU ETKİLEMEZ —
/// dahili olarak Ordinal sıralanırlar.
///
/// <para>
/// §4 kaynak-sinyali kuralı: yalnız kaynak sinyalleri (config string, commit SHA, dirty dosya İÇERİĞİ,
/// upstream imzası) girdi olur — DLL/bin/obj veya herhangi bir dosya/derleme timestamp'ı ASLA okunmaz.
/// </para>
///
/// <para>
/// <b>In-place vs worktree/committed:</b> in-place modda (<paramref name="inPlace"/>=true, bkz. <see
/// cref="Compute"/>) HEAD commit'e ek olarak working-tree'deki henüz commit'lenmemiş yerel değişiklikler de
/// projenin gerçek kaynak durumunu oluşturduğu için "local-diff hash" terimi imzaya dahil edilir. Worktree
/// (committed) modda (inPlace=false) bu terim TAMAMEN atlanır: o worktree'nin kaynağı zaten HEAD commit'i
/// tarafından tam olarak yakalanmıştır — working-tree'deki dirty değişiklikler o worktree'yi etkilemez, o
/// yüzden dirty-dosya girdisi committed modda dikkate ALINMAZ (bkz. <c>BuildSignatureTests</c>: "worktree
/// modunda dirty girdisi imzayı değiştirmez").
/// </para>
///
/// <para>
/// <b>Transitive upstream propagation:</b> <paramref name="upstreamSignature"/> yalnız bu projenin DOĞRUDAN
/// producer'larının (bkz. <see cref="ProjectNode.Dependencies"/>) ZATEN hesaplanmış imzasını sorgular —
/// transitivite ayrıca kodlanmaz; Task 7 (IncrementalPlanner) topological sırada ilerlediği için her upstream
/// imzası KENDİ upstream'lerini zaten özyinelemeli biçimde içerir. Böylece bir kök projenin imzası değişince
/// bu değişiklik zincir boyunca doğal olarak yayılır (GLOBAL propagation girdisi).
/// </para>
/// </summary>
public static class BuildSignature
{
    /// <summary>[Global Constraints] Yalnız bu uzantılar local-diff'e girer — diğer dirty dosyalar (ör. .md/.txt) yok sayılır.</summary>
    public static readonly IReadOnlyList<string> BuildAffectingExtensions =
        [".cs", ".xaml", ".resx", ".csproj", ".props", ".targets"];

    // Kaynak dosya path/içeriğinde pratikte hiç görünmeyen ASCII kontrol byte'ları — alan/eleman ayracı.
    // (char)hex-kod ile tanımlanır: kaynak dosyada literal/görünmez bir karakter GÖMÜLMEZ, yalnız rakamlar
    // yazılır — kopyala/yapıştır ya da düzenleme sırasında sessizce başka bir karaktere bozulma riski yok.
    private static readonly char FieldSeparator = (char)0x1F; // Unit Separator — alanlar arası (cfg / head / diff / up)
    private static readonly char ItemSeparator = (char)0x1E;  // Record Separator — bir alan içindeki liste elemanları arası
    private const string NullMarker = "￿__NULL__"; // ayırt edici null-işareti (gerçek path/commit/imza değeriyle asla çakışmaz)

    /// <summary>Bir yolun build-etkileyen uzantılardan biriyle bitip bitmediği (Ordinal, case-insensitive uzantı karşılaştırması).</summary>
    public static bool IsBuildAffecting(string path) =>
        BuildAffectingExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bu projenin byte-stable imzasını hesaplar.
    /// </summary>
    /// <param name="node">Bu projenin graph düğümü — yalnız <see cref="ProjectNode.Dependencies"/> (upstream producer projectId'leri) kullanılır.</param>
    /// <param name="configuration">"Debug"/"Release" vb. derleme configuration'ı — aynen (case-sensitive) imzaya girer.</param>
    /// <param name="headCommit">HEAD commit SHA'sı. <c>null</c> tolere edilir (ör. no-commits repo) — sabit bir null-işaretiyle imzaya girer, hata fırlatılmaz.</param>
    /// <param name="dirtyFiles">Bu projeye ait, working-tree'de dirty (GitService.GetDirtyPaths) olan dosya yollarının listesi. Build-etkileyen olmayanlar burada dahili olarak elenir; <paramref name="inPlace"/>=false ise bu parametrenin TÜM içeriği yok sayılır.</param>
    /// <param name="readFileContent">path → o dosyanın güncel (working-tree) İÇERİĞİ. Yalnız <paramref name="inPlace"/>=true iken ve yalnız sıralı/filtrelenmiş dirty dosyalar için çağrılır.</param>
    /// <param name="upstreamSignature">projectId → o projenin ZATEN hesaplanmış imzası (Task 7 topological sırayla besler). Henüz hesaplanmamış/bilinmeyen bir id için <c>null</c> dönebilir; <c>null</c> da imzaya deterministik biçimde girer (ör. cycle/hollow upstream).</param>
    /// <param name="inPlace">true → in-place mod (local-diff dahil). false → worktree/committed mod (local-diff terimi tamamen atlanır, yalnız commit + upstream sayılır).</param>
    /// <returns>SHA256 hex string (64 karakter, upper-case hex — <see cref="Convert.ToHexString(byte[])"/>).</returns>
    public static string Compute(
        ProjectNode node,
        string configuration,
        string? headCommit,
        IReadOnlyList<string> dirtyFiles,
        Func<string, string> readFileContent,
        Func<string, string?> upstreamSignature,
        bool inPlace)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(dirtyFiles);
        ArgumentNullException.ThrowIfNull(readFileContent);
        ArgumentNullException.ThrowIfNull(upstreamSignature);

        var sb = new StringBuilder();

        sb.Append("cfg=").Append(configuration).Append(FieldSeparator);
        sb.Append("head=").Append(headCommit ?? NullMarker).Append(FieldSeparator);

        sb.Append("diff=");
        if (inPlace)
        {
            // Determinizm [D8]: build-etkileyen filtre + Ordinal sıralama — çağıran hangi sırada/hangi
            // ek (non-build-affecting) dosyalarla verirse versin sonuç aynı kalır.
            var sortedDirty = dirtyFiles
                .Where(IsBuildAffecting)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal);

            foreach (var path in sortedDirty)
            {
                string hash = HashText(readFileContent(path));
                sb.Append(path).Append('=').Append(hash).Append(ItemSeparator);
            }
        }
        // inPlace=false (worktree/committed): local-diff terimi kasıtlı olarak TAMAMEN atlanır — bkz. tip özeti.
        sb.Append(FieldSeparator);

        sb.Append("up=");
        var sortedUpstream = node.Dependencies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

        foreach (var upstreamId in sortedUpstream)
        {
            string? sig = upstreamSignature(upstreamId);
            sb.Append(upstreamId).Append('=').Append(sig ?? NullMarker).Append(ItemSeparator);
        }

        return HashText(sb.ToString());
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
