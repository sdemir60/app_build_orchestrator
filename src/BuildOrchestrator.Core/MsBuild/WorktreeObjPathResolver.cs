using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [I2-K2/It-3 Task 10] Worktree build'lerinde obj-izolasyonu: OutDir'e DOKUNULMAZ [§4], ama <c>obj</c>
/// (BaseIntermediateOutputPath) proje **Id** (tam yol) anahtarıyla worktree kökü altında ayrı bir klasöre
/// izole edilir — böylece bayat-obj zehri (SPIKE-proven OSYS.Types.NewSales.Print vakası: aynı obj klasörünü
/// paylaşan iki farklı proje) worktree yolunda oluşamaz. In-place build bu sınıfı hiç ÇAĞIRMAZ (I2-K2: kendi
/// varsayılan obj'i, VS-parity).
/// <para>
/// Saf fonksiyon: dosya I/O yok, git komutu yok — tamamen deterministik, birim test edilebilir. Şema:
/// <c>&lt;worktreeRoot&gt;\_obj\&lt;projectId-hash&gt;</c>. Hash, <see cref="Logs.ProjectLogNaming.FileNameFor"/>
/// ile AYNI desen (SHA256, ilk 16 hex karakter, lower-invariant normalize edilmiş girdi) — Windows dosya
/// sisteminin case-insensitive olması nedeniyle aynı projenin farklı case'li yol string'leri AYNI klasöre
/// eşlenir; hex alfabesi filesystem-güvenlidir (geçersiz Windows karakteri içermez).
/// </para>
/// </summary>
public static class WorktreeObjPathResolver
{
    private const string ObjFolderName = "_obj";

    /// <summary>
    /// <paramref name="worktreeRoot"/> altında <paramref name="projectId"/>'ye özel, çakışmasız bir obj klasörü
    /// döner. Trailing backslash EKLEMEZ — <see cref="MsBuildArguments.Build"/> zaten
    /// <see cref="MsBuildArguments.EnsureTrailingBackslash"/> ile bunu garanti eder.
    /// </summary>
    public static string Resolve(string worktreeRoot, string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectId.ToLowerInvariant()));
        string slug = Convert.ToHexString(hash.AsSpan(0, 8)); // 16 hex char — ProjectLogNaming ile aynı desen

        return Path.Combine(worktreeRoot, ObjFolderName, slug);
    }
}
