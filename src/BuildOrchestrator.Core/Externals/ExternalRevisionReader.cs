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
/// <para><b>Okuma yereldir.</b> <c>rev-parse HEAD</c> ağa çıkmaz ve ucuzdur, bu yüzden güncelleme bayrağı
/// kapalı olsa bile koşar — kullanıcı hangi sürümü derlediğini her hâlde görür.</para>
///
/// <para>Okunamayan kök sessizce atlanır: revizyon bir TANI bilgisidir, hiçbir kararı beslemez (harici
/// projelerin imzası dosya İÇERİĞİNDEN gelir) — bir git hatası yüzünden koşuyu durdurmak orantısız olurdu.</para>
/// </summary>
public sealed class ExternalRevisionReader(IProcessRunner runner)
{
    /// <param name="roots">Çözülmüş harici kökler (bkz. <see cref="ExternalWorkspaceResolver"/>).</param>
    /// <param name="revisionByWorkingCopy">Güncelleme adımında okunmuş revizyonlar (<c>çalışma kopyası kökü →
    /// sha</c>, bkz. <see cref="ExternalUpdater.UpdateAsync"/>). Varsa buradaki değer yereldeki okumaya
    /// YEĞLENİR: aynı koşuda, fast-forward'ın hemen ardından okunmuştur.</param>
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IReadOnlyList<ExternalRoot> roots,
        IReadOnlyDictionary<string, string>? revisionByWorkingCopy = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var byProjectId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            string? workingCopy = VcsDetector.FindRoot(root.SearchRoot);
            string? revision = workingCopy is not null
                && revisionByWorkingCopy is not null
                && revisionByWorkingCopy.TryGetValue(workingCopy, out var updated)
                    ? updated
                    : await ReadLocallyAsync(workingCopy, ct);
            if (revision is null) continue;

            // Aynı kökten doğan HER proje aynı revizyonu taşır — çalışma kopyası tektir.
            foreach (string projectId in root.Scan.CsprojPaths) byProjectId[projectId] = revision;
        }

        return byProjectId;
    }

    /// <summary>Ağa çıkmadan okunan revizyon: <c>rev-parse HEAD</c>. Çalışma kopyası yoksa <c>null</c>.</summary>
    private async Task<string?> ReadLocallyAsync(string? workingCopy, CancellationToken ct)
    {
        if (workingCopy is null) return null;

        var head = await new GitService(runner, workingCopy).GetHeadCommitAsync(ct);
        return head.Success ? head.Value : null;
    }
}
