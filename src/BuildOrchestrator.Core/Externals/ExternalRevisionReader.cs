using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Harici köklerin revizyon kimliğini okur ve o kökten gelen HER projeye dağıtır: <c>projectId → revision</c>.
///
/// <para><b>Neden gerekli.</b> Bir projenin build-state kaydındaki "hangi commit'ten derlendi" yuvası
/// (<see cref="BuildState.BuiltCommit"/>) satırın sha alanını besler. Ana repo için o değer reponun HEAD'idir;
/// harici bir proje BAŞKA bir çalışma kopyasından gelir, dolayısıyla ana reponun HEAD'ini oraya yazmak yanlış
/// bir repoyu göstermek olurdu. Kendi kökünün revizyonu yazılınca satır ana repo satırlarıyla AYNI mantığı
/// izler: en son hangi sürümden derlendiyse onu gösterir.</para>
///
/// <para><b>Yalnız git.</b> Okuma yereldir ve ucuzdur (<c>rev-parse HEAD</c>), bu yüzden bayrak kapalı olsa
/// bile koşar. TFVC'de karşılığı (<c>tf vc history</c>) SUNUCUYA gider; planlamayı ağa bağlamamak için TFVC
/// kökleri revizyonsuz bırakılır ve o satırların sha yuvası boş kalır.</para>
///
/// <para>Okunamayan kök sessizce atlanır: revizyon bir TANI bilgisidir, hiçbir kararı beslemez (harici
/// projelerin imzası dosya İÇERİĞİNDEN gelir) — bir git hatası yüzünden koşuyu durdurmak orantısız olurdu.</para>
/// </summary>
public sealed class ExternalRevisionReader(IProcessRunner runner)
{
    /// <param name="roots">Çözülmüş harici kökler (bkz. <see cref="ExternalWorkspaceResolver"/>).</param>
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IReadOnlyList<ExternalRoot> roots, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var byProjectId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root.Project.Vcs is not VcsKind.Git) continue;

            string? workingCopy = VcsDetector.FindRoot(root.SearchRoot, VcsKind.Git);
            if (workingCopy is null) continue;

            var head = await new GitService(runner, workingCopy).GetHeadCommitAsync(ct);
            if (!head.Success || head.Value is not { Length: > 0 } revision) continue;

            // Aynı kökten doğan HER proje aynı revizyonu taşır — çalışma kopyası tektir.
            foreach (string projectId in root.Scan.CsprojPaths) byProjectId[projectId] = revision;
        }

        return byProjectId;
    }
}
