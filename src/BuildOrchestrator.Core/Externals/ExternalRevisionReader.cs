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
/// <para><b>Git yerelden, TFVC yalnız güncellemeden.</b> Git'te okuma yereldir ve ucuzdur
/// (<c>rev-parse HEAD</c>), bu yüzden güncelleme kapalı olsa bile koşar. TFVC'de karşılığı
/// (<c>tf vc history</c>) SUNUCUYA gider; planlamayı ağa bağlamamak için o değer BURADA sorulmaz —
/// <see cref="ExternalUpdater"/> zaten ağa çıkmışken, <c>tf vc get</c>'in hemen ardından okur ve buraya
/// <paramref name="revisionByWorkingCopy"/> ile verir. Güncelleme kapalıysa TFVC kökleri revizyonsuz kalır.</para>
///
/// <para>Okunamayan kök sessizce atlanır: revizyon bir TANI bilgisidir, hiçbir kararı beslemez (harici
/// projelerin imzası dosya İÇERİĞİNDEN gelir) — bir git hatası yüzünden koşuyu durdurmak orantısız olurdu.</para>
/// </summary>
public sealed class ExternalRevisionReader(IProcessRunner runner)
{
    /// <param name="roots">Çözülmüş harici kökler (bkz. <see cref="ExternalWorkspaceResolver"/>).</param>
    /// <param name="revisionByWorkingCopy">Güncelleme adımında okunmuş revizyonlar (<c>çalışma kopyası kökü →
    /// revizyon</c>, bkz. <see cref="ExternalUpdater.UpdateAsync"/>). TFVC köklerinin revizyonu YALNIZ buradan
    /// gelebilir; git köklerinde de varsa buradaki değer yereldeki okumaya YEĞLENİR (aynı koşuda, güncellemenin
    /// hemen ardından okunmuştur).</param>
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IReadOnlyList<ExternalRoot> roots,
        IReadOnlyDictionary<string, string>? revisionByWorkingCopy = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var byProjectId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            string? workingCopy = VcsDetector.FindRoot(root.SearchRoot, root.Project.Vcs);
            string? revision = workingCopy is not null
                && revisionByWorkingCopy is not null
                && revisionByWorkingCopy.TryGetValue(workingCopy, out var updated)
                    ? updated
                    : await ReadLocallyAsync(root, workingCopy, ct);
            if (revision is null) continue;

            // Aynı kökten doğan HER proje aynı revizyonu taşır — çalışma kopyası tektir.
            foreach (string projectId in root.Scan.CsprojPaths) byProjectId[projectId] = revision;
        }

        return byProjectId;
    }

    /// <summary>Ağa çıkmadan okunabilen revizyon: yalnız git (<c>rev-parse HEAD</c>). TFVC'de yereldeki
    /// karşılığı yoktur — <c>null</c> döner.</summary>
    private async Task<string?> ReadLocallyAsync(ExternalRoot root, string? workingCopy, CancellationToken ct)
    {
        if (root.Project.Vcs is not VcsKind.Git || workingCopy is null) return null;

        var head = await new GitService(runner, workingCopy).GetHeadCommitAsync(ct);
        return head.Success ? head.Value : null;
    }
}
