using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
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
        $"External project '{name}' has uncommitted changes in '{rootPath}' — commit or stash them, then build again.");

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
}

/// <summary>
/// [D5] Build'in İLK adımı: harici çalışma kopyalarını kendi sürüm kontrolünden günceller. <b>Taramadan ÖNCE
/// koşar</b> — bir fast-forward yeni proje dosyaları getirebilir ve tarama onları görmelidir.
///
/// <para><b>Yalnız günceller.</b> "Ne derlenecek" kararı burada verilmez: harici projeler taramadan sonra
/// sıradan düğümler olur ve ana repo projeleriyle AYNI incremental kararı alır (imza diskteki içerikten
/// gelir). Bu sınıfın çıktısı çalışma kopyasının diskteki hâli, kullanıcıya yazılan satırlar ve okunabilen
/// REVİZYON kimlikleridir — revizyon bir TANI bilgisidir, hiçbir kararı beslemez.</para>
///
/// <para><b>İki farklı hata sınıfı.</b> Kullanıcının çözmesi gereken bir durum (kir, ayrışma, detached HEAD)
/// koşuyu <see cref="ExternalPreparationException"/> ile HİÇ BAŞLATMADAN durdurur — güncellenemeyen bir kaynak
/// üstünde derlemek yarım bir koşudur. Geçici bir ağ/kimlik hatası ise yalnız uyarır ve yerel sürümle devam
/// edilir; ana reponun degraded fetch davranışı da tam olarak budur.</para>
///
/// <para><b>Çalışma kopyası yoksa</b> (yolun üstünde <c>.git</c> bulunamadı) güncelleme atlanır ve uyarı
/// yazılır: projeler yine de olduğu gibi derlenir. Kir kapısı da o durumda çalışmaz — güncelleme yoksa
/// kullanıcının dosyalarının üstüne yazma riski de yoktur.</para>
/// </summary>
/// <param name="runner">Process çalıştırıcı.</param>
public sealed class ExternalUpdater(IProcessRunner runner)
{

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

    /// <returns>Güncellenen çalışma kopyalarının revizyonları: <c>çalışma kopyası kökü → HEAD sha</c>.
    /// Okunamayan/güncellenemeyen kök haritada YOKTUR.</returns>
    /// <param name="externals">Kullanıcının listesi, KENDİ SIRASIYLA — güncelleme de o sırada koşar.</param>
    /// <param name="progress">Kullanıcıya görünen satırlar buraya akar.</param>
    /// <param name="scopeProjectPath">[tek proje · design v1.15.0 §9] Satırdan tetiklenen koşunun hedefi
    /// (tam csproj yolu). Dolu iken YALNIZ hedefi içeren kart güncellenir — kapsam dışına dokunulmaz: başka bir
    /// kartın kopyası ne güncellenir ne de onun için satır yazılır (çalışma kopyası olmayan kartın uyarısı
    /// dahil). Hedef ana repodaysa hiçbir karta dokunulmaz. <c>null</c> ⇒ tam koşu, her kart.</param>
    public async Task<IReadOnlyDictionary<string, string>> UpdateAsync(
        IReadOnlyList<ExternalProject> externals, Action<string> progress,
        string? scopeProjectPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(externals);
        ArgumentNullException.ThrowIfNull(progress);

        var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in externals)
        {
            var (root, revision) = await UpdateOneAsync(project, progress, scopeProjectPath, ct);
            if (root is not null && revision is not null) revisions[root] = revision;
        }

        return revisions;
    }

    private async Task<(string? Root, string? Revision)> UpdateOneAsync(
        ExternalProject project, Action<string> progress, string? scopeProjectPath, CancellationToken ct)
    {
        string name = ExternalWorkspaceResolver.DisplayName(project.Path);
        string searchRoot = ExternalWorkspaceResolver.SearchRootOf(project.Path);
        string? root = VcsDetector.FindRoot(searchRoot);
        // Kapsam kapısı: hedef ne kartın arama kökünün ne de çalışma kopyasının altındaysa kart bu koşunun
        // konusu değildir — sessizce geçilir (uyarı satırı bile yok).
        if (scopeProjectPath is not null
            && !Contains(searchRoot, scopeProjectPath)
            && !(root is not null && Contains(root, scopeProjectPath)))
            return (null, null);
        if (root is null)
        {
            progress(PlanProgressLines.ExternalNoWorkingCopy(name));
            return (null, null);
        }

        progress(PlanProgressLines.UpdatingExternal(name));

        string? revision = await UpdateGitAsync(name, root, progress, ct);

        // Satır güncellemenin ARDINDAN yazılır: kullanıcı hangi sürümü derlediğini burada görür. Haritaya
        // TAM revizyon girer (build-state kaydı ana repoyla aynı biçimi taşır); kısaltma yalnız gösterimdedir.
        if (revision is not null) progress(PlanProgressLines.UpdatedExternal(name, RevisionText.Short(revision)));
        return (root, revision);
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

    /// <returns>Güncelleme başarılıysa çalışma kopyasının TAM HEAD sha'sı, değilse <c>null</c>.</returns>
    private async Task<string?> UpdateGitAsync(string name, string rootPath, Action<string> progress, CancellationToken ct)
    {
        var result = await new FastForwardUpdater(runner, rootPath).UpdateAsync(ct);

        switch (result.Status)
        {
            case FastForwardStatus.Updated:
            case FastForwardStatus.AlreadyCurrent:
                return result.Revision;

            case FastForwardStatus.DegradedOffline:
                // Ağ yok: koşu ölmez, yerel sürüm derlenir. Revizyon satırı YAZILMAZ — "Updated ... → X"
                // demek, olmayan bir güncellemeyi bildirmek olurdu; uyarı zaten yerelde kalındığını söylüyor.
                progress(PlanProgressLines.ExternalUpdateDegraded(name, result.Detail ?? "unreachable remote"));
                return null;

            case FastForwardStatus.Dirty:
                throw ExternalPreparationException.Dirty(name, rootPath);
            case FastForwardStatus.Diverged:
                throw ExternalPreparationException.Diverged(name, rootPath);
            case FastForwardStatus.Detached:
                throw ExternalPreparationException.Detached(name, rootPath);
            default:
                throw ExternalPreparationException.UpdateFailed(name, rootPath, result.Detail);
        }
    }
}
