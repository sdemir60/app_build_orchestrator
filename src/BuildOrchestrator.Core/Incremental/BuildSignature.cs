using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Incremental;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T25][A6][D6][D1] Byte-stable proje imzası — incremental build kararının çekirdeği.
/// Signature = <c>configuration</c> + <c>content fingerprint</c> (bu projenin girdi dosyalarının DİSKTEKİ
/// içeriği, bkz. <see cref="IncrementalPlanner.ComputeContentFingerprint"/>) + transitive upstream producer
/// imzaları. Aynı girdi kümesi HER ZAMAN byte-identik SHA256 hex string üretir (determinism testli) ve girdi
/// listelerinin (dosyalar, upstream id'leri) SIRASI SONUCU ETKİLEMEZ — dahili olarak case-insensitive
/// (OrdinalIgnoreCase) sıralanırlar. Her liste elemanının RAW (değişken uzunluklu/serbest karakterli) kısmı
/// (dosya yolu, upstream projectId) ayraç yanına gömülmeden ÖNCE ayrıca hash'lenir (bkz. <see cref="HashText"/>)
/// — böylece bir yol veya id içinde tesadüfen (ya da kasıtlı) bir ayraç/<c>=</c> karakteri geçse bile iki
/// farklı terim kümesi aynı pre-hash string'e indirgenemez (bkz. <c>BuildSignatureTests</c>).
///
/// <para>
/// §4 kaynak-sinyali kuralı: yalnız kaynak sinyalleri (config string, kaynak dosya İÇERİĞİ, upstream imzası)
/// girdi olur — DLL/bin/obj veya herhangi bir DERLEME ÇIKTISI timestamp'ı ASLA okunmaz.
/// </para>
///
/// <para>
/// <b>[D1] Tek kaynak: disk.</b> Sürüm kontrolü imzaya GİRMEZ. Eskiden bu terim git'in HEAD blob tablosundan
/// (<c>ls-tree</c>) gelen bir "committed fingerprint" ile working-tree'deki kirli dosyaların içeriğinden gelen
/// ayrı bir "local-diff" teriminin toplamıydı; harici kökler ise (git ağacında olmadıkları için) zaten
/// diskten okunuyordu. O ikilik üç somut açık bırakıyordu: imza dosya listesi yalnız <c>Compile</c>
/// öğelerinden kurulduğu için commit'lenmiş bir <c>.xaml</c>/<c>.resx</c> değişikliği GÖRÜNMÜYOR (under-build),
/// git'e eklenmemiş ya da gitignore'lanmış kaynak dosyalar hiçbir terime girmiyor, ve aynı soruyu iki ayrı kod
/// yolu cevaplıyordu. Karar diskten verildiğinde üçü de kapanır; bedeli dosya okumaktır ve o bedel
/// <see cref="SourceHashCache"/> ile koşu başına bir stat geçişine iner.
/// </para>
///
/// <para>
/// <b>In-place ve worktree AYNI imzayı üretir.</b> Eskiden worktree modunda local-diff terimi tamamen
/// atlanıyordu (o ağaç committed hâli tarif ediyordu) — yani mod değiştirmek imzayı değiştirebiliyordu.
/// Artık yol terimi çalışma alanı köküne göreli, içerik ise derlenen ağacın FİZİKSEL dosyasından okunur:
/// aynı içerik iki modda da aynı imzadır (bkz. <c>IncrementalRunBinderTests</c>).
/// </para>
///
/// <para>
/// <b>Transitive upstream propagation:</b> <paramref name="upstreamSignature"/> yalnız bu projenin DOĞRUDAN
/// producer'larının (bkz. <see cref="ProjectNode.Dependencies"/>) ZATEN hesaplanmış imzasını sorgular —
/// transitivite ayrıca kodlanmaz; <see cref="IncrementalPlanner"/> upstream imzalarını DFS+memo ile ürettiği
/// için her upstream imzası KENDİ upstream'lerini zaten özyinelemeli biçimde içerir. Böylece bir kök projenin
/// imzası değişince bu değişiklik zincir boyunca doğal olarak yayılır (GLOBAL propagation girdisi).
/// </para>
/// </summary>
public static class BuildSignature
{
    /// <summary>[Global Constraints][D2] Yalnız bu uzantılar imzaya girer — bir projenin klasöründeki .md/.txt/.png
    /// gibi dosyalar derlemeyi etkilemez ve kararı oynatmamalıdır.</summary>
    public static readonly IReadOnlyList<string> BuildAffectingExtensions =
        [".cs", ".xaml", ".resx", ".csproj", ".props", ".targets"];

    // Kaynak dosya path/içeriğinde pratikte hiç görünmeyen ASCII kontrol byte'ları — alan/eleman ayracı.
    // (char)hex-kod ile tanımlanır: kaynak dosyada literal/görünmez bir karakter GÖMÜLMEZ, yalnız rakamlar
    // yazılır — kopyala/yapıştır ya da düzenleme sırasında sessizce başka bir karaktere bozulma riski yok.
    private static readonly char FieldSeparator = (char)0x1F; // Unit Separator — alanlar arası (cfg / content / up)

    /// <summary>Record Separator — bir alan içindeki liste elemanları arası. <c>internal</c>: aynı assembly
    /// içindeki <see cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeContentFingerprint"/>
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
    /// <param name="contentFingerprint">[D1] Bu projenin girdi dosyalarının DİSKTEKİ içeriğini temsil eden hash (bkz. <see cref="IncrementalPlanner.ComputeContentFingerprint"/>). <c>null</c> tolere edilir (projenin hiçbir girdisi okunamadı) — sabit bir null-işaretiyle imzaya girer, hata fırlatılmaz.</param>
    /// <param name="upstreamSignature">projectId → o projenin imzası (<see cref="IncrementalPlanner"/>, DFS+memo ile talep üzerine hesaplar). Bilinmeyen/plan dışı bir id için <c>null</c> dönebilir; <c>null</c> da imzaya deterministik biçimde girer (ör. cycle/hollow upstream).</param>
    /// <returns>SHA256 hex string (64 karakter, upper-case hex — <see cref="Convert.ToHexString(byte[])"/>).</returns>
    public static string Compute(
        ProjectNode node,
        string configuration,
        string? contentFingerprint,
        Func<string, string?> upstreamSignature)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(upstreamSignature);

        var sb = new StringBuilder();

        sb.Append("cfg=").Append(configuration).Append(FieldSeparator);
        sb.Append("content=").Append(contentFingerprint ?? NullMarker).Append(FieldSeparator);

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
    /// cref="BuildOrchestrator.Core.Incremental.IncrementalPlanner.ComputeContentFingerprint"/> da AYNI
    /// primitive'i kullanır (review fix — Task 7b: eskiden burada duplike/verbatim-kopya ediliyordu, artık tek
    /// kaynak — bkz. <see cref="ItemSeparator"/> ile aynı gerekçe).</summary>
    internal static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
