using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Incremental;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T25][A6][D6] Byte-stable proje imzası — incremental build kararının çekirdeği (bkz. plan v7 D6/A6).
/// Signature = <c>configuration</c> + <c>committedFingerprint</c> (PER-PROJECT, bkz. aşağıdaki A6-refinement
/// notu) + (YALNIZ in-place modda) <c>local-diff hash</c> + transitive upstream producer imzaları. Aynı girdi
/// kümesi HER ZAMAN byte-identik SHA256 hex string üretir (determinism testli) ve girdi listelerinin (dirty
/// dosyalar, upstream id'ler) SIRASI SONUCU ETKİLEMEZ — dahili olarak case-insensitive (OrdinalIgnoreCase)
/// sıralanırlar. Her liste elemanının RAW (değişken uzunluklu/serbest karakterli) kısmı (dosya yolu, upstream
/// projectId) da ayraç yanına gömülmeden ÖNCE ayrıca hash'lenir (bkz. <see cref="HashText"/> kullanımı Compute
/// içinde) — böylece bir yol veya id içinde tesadüfen (ya da kasıtlı) bir ayraç/`=` karakteri geçse bile iki
/// farklı terim kümesi aynı pre-hash string'e indirgenemez (bkz. <c>BuildSignatureTests</c>: separator/`=`
/// içeren id/yol testleri).
///
/// <para>
/// §4 kaynak-sinyali kuralı: yalnız kaynak sinyalleri (config string, committed fingerprint, dirty dosya
/// İÇERİĞİ, upstream imzası) girdi olur — DLL/bin/obj veya herhangi bir dosya/derleme timestamp'ı ASLA okunmaz.
/// </para>
///
/// <para>
/// <b>[A6 refinement — Task 7b] PER-PROJECT committed fingerprint (global HEAD DEĞİL):</b> eskiden bu terim
/// repo-GLOBAL <c>headCommit</c> idi — repo'da HERHANGİ bir yeni commit/branch-bounce, ilişkisiz projeler
/// DAHİL TÜM projeleri dirty işaretliyordu (over-build). Artık <paramref name="committedFingerprint"/>,
/// çağıranın (Task 7/IncrementalPlanner, bkz. <see
/// cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeCommittedFingerprint"/>) hesapladığı,
/// YALNIZ BU PROJENİN build-etkileyen dosyalarının HEAD'deki committed blob içeriğini temsil eden bir hash'tir
/// — commit değişimi, yalnız o projeyi GERÇEKTEN etkiliyorsa (+ Safe modda transitive dependent'lerine) imzayı
/// değiştirir. <c>null</c> tolere edilir (ör. proje hiç commit'lenmemiş / no-commits repo) — sabit bir
/// null-işaretiyle imzaya girer, hata fırlatılmaz.
/// </para>
///
/// <para>
/// <b>In-place vs worktree/committed:</b> in-place modda (<paramref name="inPlace"/>=true, bkz. <see
/// cref="Compute"/>) committed fingerprint'e ek olarak working-tree'deki henüz commit'lenmemiş yerel
/// değişiklikler de projenin gerçek kaynak durumunu oluşturduğu için "local-diff hash" terimi imzaya dahil
/// edilir. Worktree (committed) modda (inPlace=false) bu terim TAMAMEN atlanır: o worktree'nin kaynağı zaten
/// committed fingerprint tarafından tam olarak yakalanmıştır — working-tree'deki dirty değişiklikler o
/// worktree'yi etkilemez, o yüzden dirty-dosya girdisi committed modda dikkate ALINMAZ (bkz.
/// <c>BuildSignatureTests</c>: "worktree modunda dirty girdisi imzayı değiştirmez").
/// </para>
///
/// <para>
/// <b>Transitive upstream propagation:</b> <paramref name="upstreamSignature"/> yalnız bu projenin DOĞRUDAN
/// producer'larının (bkz. <see cref="ProjectNode.Dependencies"/>) ZATEN hesaplanmış imzasını sorgular —
/// transitivite ayrıca kodlanmaz; Task 7 (IncrementalPlanner) upstream imzalarını DFS+memo ile ürettiği için
/// her upstream imzası KENDİ upstream'lerini zaten özyinelemeli biçimde içerir. Böylece bir kök projenin imzası değişince
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
    private static readonly char FieldSeparator = (char)0x1F; // Unit Separator — alanlar arası (cfg / committed / diff / up)

    /// <summary>Record Separator — bir alan içindeki liste elemanları arası. <c>internal</c>: aynı assembly
    /// içindeki <see cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeCommittedFingerprint"/>
    /// da AYNI ayracı kullanır (review fix — Task 7b: eskiden burada duplike/senkronize-yorum ile kopyalanıyordu,
    /// artık tek kaynak).</summary>
    internal const char ItemSeparator = (char)0x1E;

    /// <summary>Ayırt edici null-işareti (gerçek path/commit/imza değeriyle asla çakışmaz). <c>internal</c>:
    /// aynı assembly içindeki <see cref="IncrementalPlanner"/>, döngüyü kırmak için bazı upstream terimlerini
    /// bu işarete düşürür — kendine bağımlı (self-loop) bir düğümün on-stack guard'a çarpan kenarı ve [A3]
    /// bir SCC'nin KOMPOZİT imzası hesaplanırken SCC-İÇİ kenarlar — "bilinmeyen upstream" ile birebir aynı
    /// deterministik değere düşsünler diye. DEĞERİ DEĞİŞMEZ.</summary>
    internal const string NullMarker = "￿__NULL__";

    /// <summary>Bir yolun build-etkileyen uzantılardan biriyle bitip bitmediği (Ordinal, case-insensitive uzantı karşılaştırması).</summary>
    public static bool IsBuildAffecting(string path) =>
        BuildAffectingExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bu projenin byte-stable imzasını hesaplar.
    /// </summary>
    /// <param name="node">Bu projenin graph düğümü — yalnız <see cref="ProjectNode.Dependencies"/> (upstream producer projectId'leri) kullanılır.</param>
    /// <param name="configuration">"Debug"/"Release" vb. derleme configuration'ı — aynen (case-sensitive) imzaya girer.</param>
    /// <param name="committedFingerprint">[A6 refinement] Bu projenin PER-PROJECT committed fingerprint'i — YALNIZ bu projenin build-etkileyen dosyalarının HEAD'deki committed blob içeriğini temsil eder (bkz. <see cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeCommittedFingerprint"/>). Repo-GLOBAL bir commit SHA'sı DEĞİLDİR. <c>null</c> tolere edilir (ör. proje hiç commit'lenmemiş / no-commits repo) — sabit bir null-işaretiyle imzaya girer, hata fırlatılmaz.</param>
    /// <param name="dirtyFiles">Bu projeye ait, working-tree'de dirty (GitService.GetDirtyPaths) olan dosya yollarının listesi. Build-etkileyen olmayanlar burada dahili olarak elenir; <paramref name="inPlace"/>=false ise bu parametrenin TÜM içeriği yok sayılır.</param>
    /// <param name="readFileContent">path → o dosyanın güncel (working-tree) İÇERİĞİ. Yalnız <paramref name="inPlace"/>=true iken ve yalnız sıralı/filtrelenmiş dirty dosyalar için çağrılır.</param>
    /// <param name="upstreamSignature">projectId → o projenin imzası (Task 7, DFS+memo ile talep üzerine hesaplar). Bilinmeyen/plan dışı bir id için <c>null</c> dönebilir; <c>null</c> da imzaya deterministik biçimde girer (ör. cycle/hollow upstream).</param>
    /// <param name="inPlace">true → in-place mod (local-diff dahil). false → worktree/committed mod (local-diff terimi tamamen atlanır, yalnız committed fingerprint + upstream sayılır).</param>
    /// <returns>SHA256 hex string (64 karakter, upper-case hex — <see cref="Convert.ToHexString(byte[])"/>).</returns>
    public static string Compute(
        ProjectNode node,
        string configuration,
        string? committedFingerprint,
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
        sb.Append("committed=").Append(committedFingerprint ?? NullMarker).Append(FieldSeparator);

        sb.Append("diff=");
        if (inPlace)
        {
            // Determinizm [D8]: build-etkileyen filtre + case-insensitive sıralama (Windows path'leri
            // case-insensitive'dır — upstream id listesiyle TUTARLI karşılaştırıcı) — çağıran hangi
            // sırada/hangi ek (non-build-affecting) dosyalarla verirse versin sonuç aynı kalır.
            var sortedDirty = dirtyFiles
                .Where(IsBuildAffecting)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var path in sortedDirty)
            {
                string hash = HashText(readFileContent(path));
                // RAW path ASLA doğrudan ayraç yanına gömülmez — sabit-genişlikli hash'lenir (bkz. tip
                // özeti): bir yol içinde tesadüfen ItemSeparator/FieldSeparator/'=' geçmesi terimler
                // arası sınırı kaydıramaz.
                sb.Append(HashText(path)).Append('=').Append(hash).Append(ItemSeparator);
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
            // RAW upstreamId (tam dosya yolu olabilir) ASLA doğrudan ayraç yanına gömülmez — sabit-
            // genişlikli hash'lenir (bkz. tip özeti): örn. id içinde bir ItemSeparator + başka bir id +
            // '=' + sig geçmesi, iki-öğeli bir kümeyi tek-öğeli başka bir kümeyle aynı pre-hash string'e
            // indirgeyemez (bkz. BuildSignatureTests: upstream_ids_containing_separator...).
            sb.Append(HashText(upstreamId)).Append('=').Append(sig ?? NullMarker).Append(ItemSeparator);
        }

        return HashText(sb.ToString());
    }

    /// <summary>SHA256→upper-case-hex. <c>internal</c>: aynı assembly içindeki <see
    /// cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeCommittedFingerprint"/> da AYNI
    /// primitive'i kullanır (review fix — Task 7b: eskiden burada duplike/verbatim-kopya ediliyordu, artık tek
    /// kaynak — bkz. <see cref="ItemSeparator"/> ile aynı gerekçe).</summary>
    internal static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
