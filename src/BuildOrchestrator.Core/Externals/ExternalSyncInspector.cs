using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>Sync'in bir harici proje hakkında OKUYARAK öğrendikleri.</summary>
/// <param name="Project">Kullanıcının listelediği harici proje.</param>
/// <param name="Vcs">Çalışma kopyasının sürüm kontrol türü.</param>
/// <param name="Revision">Yerel revizyon kimliği (git HEAD); bilinmiyorsa null.</param>
/// <param name="WillBuild">Önizleme kararı; durum bilinmiyorsa null (hollow).</param>
/// <param name="Reason">Kararın gerekçesi; hollow ise null.</param>
/// <param name="Dirty">Commit'lenmemiş yerel değişiklik var mı — önizlemeyi DEĞİŞTİRMEZ, yalnız uyarır.</param>
/// <param name="Warning">Kullanıcıya gösterilecek uyarı satırı; yoksa null.</param>
public sealed record ExternalInspection(
    ExternalProject Project,
    VcsKind Vcs,
    string? Revision,
    bool? WillBuild,
    WillBuildReason? Reason,
    bool Dirty,
    string? Warning);

/// <summary>
/// [D5] Sync'in harici projelere bakışı — <b>hiçbir mutasyon yapmaz ve hiçbir zaman bloklamaz</b>.
///
/// <para><b>Git hariciler</b> gerçek bir önizleme alır: yerel <c>HEAD</c> ve <c>status</c> ucuz, çevrimdışı
/// çalışan sorgulardır; fetch bile YAPILMAZ (Sync'in kendi fetch'i ana repoya aittir). <b>TFVC hariciler
/// hollow kalır</b> — <c>tf.exe</c> burada hiç çalıştırılmaz, çünkü TFVC sorguları sunucuya gider ve Sync'in
/// hızlı/çevrimdışı-toleranslı olma sözünü bozardı; gerçek karar koşu planlamasında verilir.</para>
///
/// <para>Bir harici okunamazsa Sync ÖLMEZ: o satır hollow döner, uyarısı taşınır ve diğer hariciler
/// incelenmeye devam eder.</para>
/// </summary>
public sealed class ExternalSyncInspector(IProcessRunner runner)
{
    /// <summary>
    /// Listeyi VERİLEN SIRAYLA inceler — sıra kullanıcının Ayarlar'daki sırasıdır ve node üretimine kadar
    /// korunur.
    /// </summary>
    /// <param name="externals">Kullanıcının harici proje listesi.</param>
    /// <param name="configuration">İmzaya giren configuration (Debug/Release).</param>
    /// <param name="state">Build-state defteri (anahtar = TargetPath); hiç okunmadıysa null.</param>
    public async Task<IReadOnlyList<ExternalInspection>> InspectAsync(
        IReadOnlyList<ExternalProject> externals,
        string configuration,
        IReadOnlyDictionary<string, BuildState>? state,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(externals);
        ArgumentNullException.ThrowIfNull(configuration);

        var inspections = new List<ExternalInspection>(externals.Count);
        foreach (var project in externals)
            inspections.Add(await InspectOneAsync(project, configuration, state, ct));

        return inspections;
    }

    private async Task<ExternalInspection> InspectOneAsync(
        ExternalProject project, string configuration, IReadOnlyDictionary<string, BuildState>? state, CancellationToken ct)
    {
        if (!Directory.Exists(project.ProjectPath))
            return Hollow(project, VcsKind.Unknown, PlanProgressLines.ExternalFolderMissing(project.Name));

        var root = VcsDetector.DetectRoot(project.ProjectPath);
        if (root.Kind is VcsKind.Unknown)
            return Hollow(project, VcsKind.Unknown, PlanProgressLines.ExternalNoVersionControl(project.Name));

        // TFVC: sunucuya gitmeden söylenebilecek bir şey yok — hollow, uyarısız (bu bir arıza değil, tasarım).
        if (root.Kind is VcsKind.Tfvc)
            return Hollow(project, VcsKind.Tfvc, warning: null);

        var git = new GitService(runner, root.RootPath!);

        var head = await git.GetHeadCommitAsync(ct);
        if (!head.Success)
            return Hollow(project, VcsKind.Git, PlanProgressLines.ExternalStateUnknown(project.Name, head.Error!));

        var dirty = await git.GetDirtyPathsAsync(ct);
        if (!dirty.Success)
            return Hollow(project, VcsKind.Git, PlanProgressLines.ExternalStateUnknown(project.Name, dirty.Error!));

        bool isDirty = dirty.Value!.Count > 0;
        string signature = ExternalSignature.Compute(configuration, VcsKind.Git, head.Value);
        var decision = ExternalWillBuild.Decide(Lookup(state, project.TargetPath), signature);

        return new ExternalInspection(project, VcsKind.Git, head.Value, decision.WillBuild, decision.Reason,
            isDirty, isDirty ? PlanProgressLines.ExternalDirtyWarning(project.Name) : null);
    }

    private static BuildState? Lookup(IReadOnlyDictionary<string, BuildState>? state, string targetPath)
        => state is not null && state.TryGetValue(targetPath, out var record) ? record : null;

    private static ExternalInspection Hollow(ExternalProject project, VcsKind vcs, string? warning)
        => new(project, vcs, null, null, null, false, warning);
}
