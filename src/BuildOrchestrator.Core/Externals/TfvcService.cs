using System.Xml.Linq;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// [D8] TFVC local workspace yüzeyi: bekleyen değişiklik var mı, get-latest, ve mevcut changeset.
///
/// <para><b>Lokalize metin ASLA ayrıştırılmaz.</b> tf.exe kullanıcının Visual Studio dilinde konuşur;
/// "There are no pending changes." Türkçe bir kurulumda başka bir cümledir. Bu yüzden kirlilik kararı
/// XML YAPISINDAN, hata kararı EXIT KODUNDAN, changeset ise satırın baştaki RAKAM dizisinden okunur.</para>
///
/// <para>Sonuç tipi olarak <see cref="GitResult{T}"/> yeniden kullanılır: taşıdığı şey "başarı/değer/hata
/// metni" üçlüsüdür ve git'e özgü bir yanı yoktur. Süreç başlatma hataları da dahil hiçbir hata exception
/// olarak yukarı sızmaz.</para>
/// </summary>
public sealed class TfvcService
{
    /// <summary>Salt-okur sorgular — git tarafındaki karşılıklarıyla aynı tavan.</summary>
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>get-latest tüm kapsamı diske yazar; büyük bir workspace'te dakikalar sürer.</summary>
    private static readonly TimeSpan GetTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Bekleyen değişiklikleri taşıyan XML öğesinin adı (<c>PendingChanges</c> KABI değil, tekil kayıt).</summary>
    private const string PendingChangeElement = "PendingChange";

    private readonly IProcessRunner _runner;
    private readonly string _rootPath;
    private readonly string _tfExePath;

    /// <param name="runner">Process çalıştırıcı.</param>
    /// <param name="rootPath">TFVC çalışma kopyasının kökü — komutlar bu dizinde koşar.</param>
    /// <param name="tfExePath">tf.exe'nin tam yolu (bkz. <see cref="TfResolver"/>).</param>
    public TfvcService(IProcessRunner runner, string rootPath, string tfExePath)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _tfExePath = string.IsNullOrWhiteSpace(tfExePath)
            ? throw new ArgumentException("tfExePath must not be empty.", nameof(tfExePath))
            : tfExePath;
    }

    /// <summary>
    /// Kökün altında bekleyen (checkout edilmiş/eklenmiş/silinmiş) bir değişiklik var mı. Karar XML
    /// yapısından okunur — ham metinde arama yapmak <c>&lt;PendingChanges /&gt;</c> kabı yüzünden temiz bir
    /// workspace'i kirli gösterirdi.
    /// </summary>
    public async Task<GitResult<bool>> HasPendingChangesAsync(CancellationToken ct = default)
    {
        var outcome = await CommandLineTool.RunAsync(_runner, CommandLineTool.Tf, _tfExePath,
            ["vc", "status", ".", "/recursive", "/noprompt", "/format:xml"], _rootPath, QueryTimeout, ct);
        if (!outcome.Success) return GitResult<bool>.Fail(outcome.Error!);

        var r = outcome.Value!;
        if (r.ExitCode != 0) return GitResult<bool>.Fail(CommandLineTool.DescribeFailure(CommandLineTool.Tf, r));

        try
        {
            var document = XDocument.Parse(r.StandardOutput);
            return GitResult<bool>.Ok(document.Descendants(PendingChangeElement).Any());
        }
        catch (System.Xml.XmlException ex)
        {
            // Beklenen yapı gelmedi — tahmin yürütmek yerine hata olarak yüzeye çıkar; çağıran koşuyu durdurur.
            return GitResult<bool>.Fail($"the tf status output could not be parsed as XML: {ex.Message}");
        }
    }

    /// <summary>Kökü sunucudaki son sürüme çeker (<c>tf vc get</c>). Başarısızlık veri olarak döner.</summary>
    public async Task<GitResult<bool>> GetLatestAsync(CancellationToken ct = default)
    {
        var outcome = await CommandLineTool.RunAsync(_runner, CommandLineTool.Tf, _tfExePath,
            ["vc", "get", ".", "/recursive", "/noprompt"], _rootPath, GetTimeout, ct);
        if (!outcome.Success) return GitResult<bool>.Fail(outcome.Error!);

        var r = outcome.Value!;
        return r.ExitCode == 0
            ? GitResult<bool>.Ok(true)
            : GitResult<bool>.Fail(CommandLineTool.DescribeFailure(CommandLineTool.Tf, r));
    }

}
