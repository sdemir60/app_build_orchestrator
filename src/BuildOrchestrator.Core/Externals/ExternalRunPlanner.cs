using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Bir harici proje koşuya hazırlanamadı — koşu HİÇ BAŞLAMAZ.
///
/// <para>Mesaj doğrudan kullanıcıya gösterilir: hangi harici, nerede ve ne yapması gerektiğini söyler.
/// Metinler burada, tek yerde durur — bunlar progress satırı değil, iptal gerekçesidir.</para>
/// </summary>
public sealed class ExternalPreparationException(string message) : Exception(message)
{
    /// <summary>Commit'lenmemiş yerel değişiklik — araç kullanıcının dosyalarının üstüne çalışmaz.</summary>
    public static ExternalPreparationException Dirty(string name, string rootPath) => new(
        $"External project '{name}' has uncommitted changes in '{rootPath}' — commit, stash or shelve them, then build again.");

    /// <summary>Yerel branch remote'tan ayrışmış; fast-forward mümkün değil.</summary>
    public static ExternalPreparationException Diverged(string name, string rootPath) => new(
        $"External project '{name}' has diverged from its remote in '{rootPath}' — reconcile it manually, then build again.");

    /// <summary>HEAD bir branch'e bağlı değil; neyin güncelleneceği belirsiz.</summary>
    public static ExternalPreparationException Detached(string name, string rootPath) => new(
        $"External project '{name}' is not on a branch in '{rootPath}' — check out a branch, then build again.");

    /// <summary>Harici projenin dizini diskte yok.</summary>
    public static ExternalPreparationException FolderMissing(string name, string projectPath) => new(
        $"External project '{name}' was not found at '{projectPath}' — fix the path in Settings, then build again.");

    /// <summary>Çalışma kopyası okunamadı / güncellenemedi (ağ hatası DEĞİL — o degrade edilir).</summary>
    public static ExternalPreparationException UpdateFailed(string name, string rootPath, string? detail) => new(
        $"External project '{name}' could not be prepared in '{rootPath}': {detail ?? "unknown error"}");

    /// <summary>tf.exe yok ya da TFVC sorgusu başarısız — TFVC harici bu makinede hazırlanamaz.</summary>
    public static ExternalPreparationException Tfvc(string name, string detail) => new(
        $"External project '{name}' could not be prepared: {detail}");
}

/// <summary>Bir harici projenin bu koşu için hazırlanmış hâli.</summary>
/// <param name="Project">Kullanıcının listelediği harici proje.</param>
/// <param name="Vcs">Çalışma kopyasının sürüm kontrol türü.</param>
/// <param name="Revision">Güncelleme sonrası revizyon kimliği; bilinmiyorsa null.</param>
/// <param name="Signature">Bu koşudaki imza — derleme başarılı biterse deftere bu yazılır.</param>
/// <param name="WillBuild">Derlenecek mi.</param>
/// <param name="Reason">Kararın gerekçesi; Rebuild zorlamasında null.</param>
public sealed record ExternalBuildPlan(
    ExternalProject Project,
    VcsKind Vcs,
    string? Revision,
    string Signature,
    bool WillBuild,
    WillBuildReason? Reason);

/// <summary>
/// [D6] Build anındaki harici fazı: liste sırasıyla her harici için kök keşfi → kir kapısı → güncelleme →
/// revizyon → karar.
///
/// <para><b>İki farklı hata sınıfı.</b> Kullanıcının çözmesi gereken bir durum (kir, ayrışma, detached HEAD,
/// eksik dizin, kurulu olmayan tf.exe) koşuyu <see cref="ExternalPreparationException"/> ile HİÇ
/// BAŞLATMADAN durdurur — yarım bir koşu kimseye yaramaz. Geçici bir ağ/kimlik hatası ise yalnız uyarır ve
/// yerel sürümle devam edilir; ana reponun degraded fetch davranışı da tam olarak budur.</para>
///
/// <para><b>tf.exe tembel çözülür:</b> yalnızca gerçekten bir TFVC harici varken aranır — git-only
/// kullanıcılar Team Explorer kurmak zorunda kalmaz.</para>
/// </summary>
/// <param name="runner">Process çalıştırıcı.</param>
/// <param name="tfResolver">tf.exe'yi çözen delege; null ise <see cref="TfResolver"/> kullanılır.</param>
public sealed class ExternalRunPlanner(IProcessRunner runner, Func<CancellationToken, Task<string>>? tfResolver = null)
{
    private string? _tfExePath;

    /// <param name="externals">Kullanıcının listesi, KENDİ SIRASIYLA — hazırlık da o sırada koşar.</param>
    /// <param name="configuration">İmzaya giren configuration.</param>
    /// <param name="rebuild">Rebuild koşusunda hariciler koşulsuz derlenir (kapılar aynı kalır).</param>
    /// <param name="state">Build-state defteri (anahtar = TargetPath).</param>
    /// <param name="progress">Kullanıcıya görünen satırlar buraya akar.</param>
    public async Task<IReadOnlyList<ExternalBuildPlan>> PlanAsync(
        IReadOnlyList<ExternalProject> externals,
        string configuration,
        bool rebuild,
        IReadOnlyDictionary<string, BuildState>? state,
        Action<string> progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(externals);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(progress);

        var plans = new List<ExternalBuildPlan>(externals.Count);
        foreach (var project in externals)
            plans.Add(await PlanOneAsync(project, configuration, rebuild, state, progress, ct));

        return plans;
    }

    private async Task<ExternalBuildPlan> PlanOneAsync(
        ExternalProject project, string configuration, bool rebuild,
        IReadOnlyDictionary<string, BuildState>? state, Action<string> progress, CancellationToken ct)
    {
        progress(PlanProgressLines.UpdatingExternal(project.Name));

        if (!Directory.Exists(project.ProjectPath))
            throw ExternalPreparationException.FolderMissing(project.Name, project.ProjectPath);

        var root = VcsDetector.DetectRoot(project.ProjectPath);
        string? revision = root.Kind switch
        {
            VcsKind.Git => await UpdateGitAsync(project, root.RootPath!, progress, ct),
            VcsKind.Tfvc => await UpdateTfvcAsync(project, root.RootPath!, progress, ct),
            _ => NoVersionControl(project, progress),
        };

        string signature = ExternalSignature.Compute(configuration, root.Kind, revision);
        var decision = rebuild
            ? new ExternalBuildDecision(true, WillBuildReason.NeverBuilt) // gerekçe Rebuild'de gösterilmez
            : ExternalWillBuild.Decide(Lookup(state, project.TargetPath), signature);

        if (!decision.WillBuild) progress(PlanProgressLines.ExternalUpToDate(project.Name));

        return new ExternalBuildPlan(project, root.Kind, revision, signature,
            decision.WillBuild, rebuild ? null : decision.Reason);
    }

    private async Task<string?> UpdateGitAsync(ExternalProject project, string rootPath, Action<string> progress, CancellationToken ct)
    {
        var result = await new ExternalGitUpdater(runner, rootPath).UpdateAsync(ct);

        switch (result.Status)
        {
            case ExternalUpdateStatus.Updated:
            case ExternalUpdateStatus.AlreadyCurrent:
                return result.Revision;

            case ExternalUpdateStatus.DegradedOffline:
                // Ağ yok: koşu ölmez, yerel sürüm derlenir.
                progress(PlanProgressLines.ExternalUpdateDegraded(project.Name, result.Detail ?? "unreachable remote"));
                return result.Revision;

            case ExternalUpdateStatus.Dirty:
                throw ExternalPreparationException.Dirty(project.Name, rootPath);
            case ExternalUpdateStatus.Diverged:
                throw ExternalPreparationException.Diverged(project.Name, rootPath);
            case ExternalUpdateStatus.Detached:
                throw ExternalPreparationException.Detached(project.Name, rootPath);
            default:
                throw ExternalPreparationException.UpdateFailed(project.Name, rootPath, result.Detail);
        }
    }

    private async Task<string?> UpdateTfvcAsync(ExternalProject project, string rootPath, Action<string> progress, CancellationToken ct)
    {
        var tfvc = new TfvcService(runner, rootPath, await ResolveTfAsync(project.Name, ct));

        var pending = await tfvc.HasPendingChangesAsync(ct);
        if (!pending.Success) throw ExternalPreparationException.Tfvc(project.Name, pending.Error!);
        if (pending.Value) throw ExternalPreparationException.Dirty(project.Name, rootPath);

        var get = await tfvc.GetLatestAsync(ct);
        if (!get.Success)
            progress(PlanProgressLines.ExternalUpdateDegraded(project.Name, get.Error!));

        var changeset = await tfvc.CurrentChangesetAsync(ct);
        // "C" öneki changeset numarasını git sha'sından ayırır — ikisi aynı imza alanında yaşar.
        return changeset.Value is null ? null : "C" + changeset.Value;
    }

    /// <summary>tf.exe ilk TFVC haricide çözülür ve koşu boyunca saklanır.</summary>
    private async Task<string> ResolveTfAsync(string externalName, CancellationToken ct)
    {
        if (_tfExePath is not null) return _tfExePath;

        try
        {
            _tfExePath = tfResolver is not null
                ? await tfResolver(ct)
                : await new TfResolver(runner).ResolveAsync(ct: ct);
        }
        catch (TfResolveException ex)
        {
            // Kurulum eksiği kullanıcının çözeceği bir durumdur — koşu hiç başlamaz.
            throw ExternalPreparationException.Tfvc(externalName, ex.Message);
        }

        return _tfExePath;
    }

    private static string? NoVersionControl(ExternalProject project, Action<string> progress)
    {
        progress(PlanProgressLines.ExternalNoVersionControl(project.Name));
        return null; // bilinmeyen revizyon → proje her koşuda derlenir
    }

    private static BuildState? Lookup(IReadOnlyDictionary<string, BuildState>? state, string targetPath)
        => state is not null && state.TryGetValue(targetPath, out var record) ? record : null;
}
