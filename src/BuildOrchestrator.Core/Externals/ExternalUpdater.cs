using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Bir harici çalışma kopyası koşuya hazırlanamadı — koşu HİÇ BAŞLAMAZ.
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

    /// <summary>Ayarlar'daki yol hiçbir projeye çözülemedi. <paramref name="problem"/>
    /// <see cref="ExternalWorkspaceResolver"/>'ın cümlesidir.</summary>
    public static ExternalPreparationException NotScanned(string name, string path, string problem) => new(
        $"External project '{name}' contributes no projects from '{path}': {problem} — fix the path in Settings, then build again.");

    /// <summary>Çalışma kopyası okunamadı / güncellenemedi (ağ hatası DEĞİL — o degrade edilir).</summary>
    public static ExternalPreparationException UpdateFailed(string name, string rootPath, string? detail) => new(
        $"External project '{name}' could not be prepared in '{rootPath}': {detail ?? "unknown error"}");

    /// <summary>tf.exe yok ya da TFVC sorgusu başarısız — TFVC harici bu makinede hazırlanamaz.</summary>
    public static ExternalPreparationException Tfvc(string name, string detail) => new(
        $"External project '{name}' could not be prepared: {detail}");
}

/// <summary>
/// [D5] Build'in İLK adımı: harici çalışma kopyalarını kendi sürüm kontrolünden günceller. <b>Taramadan ÖNCE
/// koşar</b> — bir fast-forward yeni proje dosyaları getirebilir ve tarama onları görmelidir.
///
/// <para><b>Yalnız günceller.</b> "Ne derlenecek" kararı burada verilmez: harici projeler taramadan sonra
/// sıradan düğümler olur ve ana repo projeleriyle AYNI incremental kararı alır. Bu sınıfın tek çıktısı
/// çalışma kopyasının diskteki hâli ve kullanıcıya yazılan satırlardır.</para>
///
/// <para><b>İki farklı hata sınıfı.</b> Kullanıcının çözmesi gereken bir durum (kir, ayrışma, detached HEAD,
/// kurulu olmayan tf.exe) koşuyu <see cref="ExternalPreparationException"/> ile HİÇ BAŞLATMADAN durdurur —
/// güncellenemeyen bir kaynak üstünde derlemek yarım bir koşudur. Geçici bir ağ/kimlik hatası ise yalnız
/// uyarır ve yerel sürümle devam edilir; ana reponun degraded fetch davranışı da tam olarak budur.</para>
///
/// <para><b>Seçilen türde çalışma kopyası yoksa</b> (yolun üstünde <c>.git</c> / <c>$tf</c> bulunamadı)
/// güncelleme atlanır ve uyarı yazılır: projeler yine de olduğu gibi derlenir. Kir kapısı da o durumda
/// çalışmaz — güncelleme yoksa kullanıcının dosyalarının üstüne yazma riski de yoktur.</para>
///
/// <para><b>tf.exe tembel çözülür:</b> yalnızca gerçekten bir TFVC harici varken aranır — git-only
/// kullanıcılar Team Explorer kurmak zorunda kalmaz.</para>
/// </summary>
/// <param name="runner">Process çalıştırıcı.</param>
/// <param name="tfResolver">tf.exe'yi çözen delege; null ise <see cref="TfResolver"/> kullanılır.</param>
public sealed class ExternalUpdater(IProcessRunner runner, Func<CancellationToken, Task<string>>? tfResolver = null)
{
    private string? _tfExePath;

    /// <summary>
    /// Bu koşu harici çalışma kopyalarına DOKUNACAK mı — kararın TEK yeri (Supervisor yalnız uygular).
    /// Üç koşul da sağlanmalı:
    /// <list type="bullet">
    /// <item>[D5] kullanıcı güncellemeyi açık bırakmış olmalı (<paramref name="updateExternals"/>);</item>
    /// <item>[D12] koşu <see cref="RunMode.Cycles"/> ya da <see cref="RunMode.Clean"/> OLMAMALI — ilki ana
    /// reponun SCC onarımıdır, ikincisi ise yalnız çıktı siler; ikisinde de kullanıcının çalışma kopyalarını
    /// güncellemek sürpriz olurdu (tarama yine yapılır: graf Build'inkiyle aynı kalır);</item>
    /// <item>ortada gerçekten bir harici kart olmalı — boş listede tek bir process bile açılmaz.</item>
    /// </list>
    /// </summary>
    public static bool ShouldUpdate(RunMode mode, bool updateExternals, IReadOnlyList<ExternalProject>? externals) =>
        updateExternals && mode is not (RunMode.Cycles or RunMode.Clean) && externals is { Count: > 0 };

    /// <param name="externals">Kullanıcının listesi, KENDİ SIRASIYLA — güncelleme de o sırada koşar.</param>
    /// <param name="progress">Kullanıcıya görünen satırlar buraya akar.</param>
    /// <param name="scopeProjectPath">[tek proje · design v1.15.0 §9] Satırdan tetiklenen koşunun hedefi
    /// (tam csproj yolu). Dolu iken YALNIZ hedefi içeren kart güncellenir — kapsam dışına dokunulmaz: başka bir
    /// kartın kopyası ne güncellenir ne de onun için satır yazılır (çalışma kopyası olmayan kartın uyarısı
    /// dahil). Hedef ana repodaysa hiçbir karta dokunulmaz. <c>null</c> ⇒ tam koşu, her kart.</param>
    public async Task UpdateAsync(
        IReadOnlyList<ExternalProject> externals, Action<string> progress,
        string? scopeProjectPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(externals);
        ArgumentNullException.ThrowIfNull(progress);

        foreach (var project in externals)
            await UpdateOneAsync(project, progress, scopeProjectPath, ct);
    }

    private async Task UpdateOneAsync(ExternalProject project, Action<string> progress, string? scopeProjectPath, CancellationToken ct)
    {
        string name = ExternalWorkspaceResolver.DisplayName(project.Path);
        string searchRoot = ExternalWorkspaceResolver.SearchRootOf(project.Path);
        string? root = VcsDetector.FindRoot(searchRoot, project.Vcs);
        // Kapsam kapısı: hedef ne kartın arama kökünün ne de çalışma kopyasının altındaysa kart bu koşunun
        // konusu değildir — sessizce geçilir (uyarı satırı bile yok).
        if (scopeProjectPath is not null
            && !Contains(searchRoot, scopeProjectPath)
            && !(root is not null && Contains(root, scopeProjectPath)))
            return;
        if (root is null)
        {
            progress(PlanProgressLines.ExternalNoWorkingCopy(name, project.Vcs));
            return;
        }

        progress(PlanProgressLines.UpdatingExternal(name));

        if (project.Vcs is VcsKind.Tfvc) await UpdateTfvcAsync(name, root, progress, ct);
        else await UpdateGitAsync(name, root, progress, ct);
    }

    /// <summary>
    /// <paramref name="projectPath"/> <paramref name="root"/>'un ALTINDA mı — saf bir yol sorusu: harf-duyarsız
    /// ve ayraç-farkında (<c>D:\ext\mail2</c>, <c>D:\ext\mail</c>'in altı DEĞİLDİR). Diske dokunmaz; bozuk bir
    /// yol "içermiyor" sayılır.
    /// </summary>
    public static bool Contains(string root, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(projectPath)) return false;
        try
        {
            string r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
            string p = Path.GetFullPath(projectPath.Trim());
            if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
            return p.Length == r.Length || p[r.Length] == Path.DirectorySeparatorChar || p[r.Length] == Path.AltDirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task UpdateGitAsync(string name, string rootPath, Action<string> progress, CancellationToken ct)
    {
        var result = await new ExternalGitUpdater(runner, rootPath).UpdateAsync(ct);

        switch (result.Status)
        {
            case ExternalUpdateStatus.Updated:
            case ExternalUpdateStatus.AlreadyCurrent:
                return;

            case ExternalUpdateStatus.DegradedOffline:
                // Ağ yok: koşu ölmez, yerel sürüm derlenir.
                progress(PlanProgressLines.ExternalUpdateDegraded(name, result.Detail ?? "unreachable remote"));
                return;

            case ExternalUpdateStatus.Dirty:
                throw ExternalPreparationException.Dirty(name, rootPath);
            case ExternalUpdateStatus.Diverged:
                throw ExternalPreparationException.Diverged(name, rootPath);
            case ExternalUpdateStatus.Detached:
                throw ExternalPreparationException.Detached(name, rootPath);
            default:
                throw ExternalPreparationException.UpdateFailed(name, rootPath, result.Detail);
        }
    }

    private async Task UpdateTfvcAsync(string name, string rootPath, Action<string> progress, CancellationToken ct)
    {
        var tfvc = new TfvcService(runner, rootPath, await ResolveTfAsync(name, ct));

        var pending = await tfvc.HasPendingChangesAsync(ct);
        if (!pending.Success) throw ExternalPreparationException.Tfvc(name, pending.Error!);
        if (pending.Value) throw ExternalPreparationException.Dirty(name, rootPath);

        var get = await tfvc.GetLatestAsync(ct);
        if (!get.Success) progress(PlanProgressLines.ExternalUpdateDegraded(name, get.Error!));
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
}
