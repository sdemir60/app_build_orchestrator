using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// Bir worktree'de kurulmuş planın KİMLİKLERİNİ ana repo köküne taşır.
///
/// <para><b>Neden gerekli.</b> Proje kimliği bu kod tabanında tam csproj YOLUDUR. Farklı bir branch'e build
/// alındığında tarama havuzdaki worktree'de yapılır, dolayısıyla aynı proje bambaşka bir kimlik kazanır.
/// Sonuçları ağırdı: (a) imzanın upstream terimi bağımlılık id'lerini hash'lediği için bağımlılığı olan HER
/// projenin imzası ayrışır; (b) build-state kayıtları worktree yoluyla yazılır ve bir sonraki in-place Sync
/// onları ANA kök id'siyle arayıp bulamaz — yani worktree'li tek bir Build'den sonra her şey "derlenecek"
/// görünür; (c) koşunun önizlemesi worktree id'leriyle yayıldığı için App listede KOPYA satırlar üretir.</para>
///
/// <para><b>Kural: kimlik mantıksaldır, yol fiziksel.</b> Rebase'ten sonra plan, imza, state, event'ler,
/// decision.log ve App — hepsi ana kök kimliğini görür. Worktree yolunu yalnız MSBuild'i çağıran nokta
/// kullanır ve onu <see cref="Result.BuildPathById"/>'den okur. Bu yüzden rebase planlamanın ERKEN bir
/// adımıdır: imza hesabından SONRA yapılsaydı imzalar yine worktree yollarıyla hesaplanmış olurdu.</para>
///
/// <para>In-place koşuda (<paramref name="fromRoot"/> == <c>toRoot</c>) hiçbir şey kopyalanmaz — girdi
/// nesneleri aynen döner.</para>
/// </summary>
public static class ProjectIdentityRebase
{
    /// <param name="Plan">Kimlikleri (Id/ProjectPath/Dependencies/Cycles) ana köke taşınmış plan.</param>
    /// <param name="SolutionRefs">ANAHTARLARI ana köke taşınmış solution haritası. DEĞERLER (sln yolları)
    /// worktree'de KALIR: <c>SolutionDirResolver</c> diskteki gerçek dosyayı görmelidir.</param>
    /// <param name="EvaluatedById">Anahtarları VE içerikleri (Path/CompileFiles/ProjectReferences) ana köke
    /// taşınmış değerlendirme haritası — committed fingerprint repo-göreli yolları buradan türetir.</param>
    /// <param name="BuildPathById">Ana kök id → MSBuild'e verilecek GERÇEK (worktree) csproj yolu.</param>
    public readonly record struct Result(
        BuildPlan Plan,
        IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> SolutionRefs,
        IReadOnlyDictionary<string, EvaluatedProject> EvaluatedById,
        IReadOnlyDictionary<string, string> BuildPathById);

    public static Result To(
        string toRoot,
        string fromRoot,
        BuildPlan plan,
        IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> solutionRefs,
        IReadOnlyDictionary<string, EvaluatedProject> evaluatedById)
    {
        ArgumentNullException.ThrowIfNull(toRoot);
        ArgumentNullException.ThrowIfNull(fromRoot);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(solutionRefs);
        ArgumentNullException.ThrowIfNull(evaluatedById);

        string from = WithSeparator(fromRoot);
        string to = WithSeparator(toRoot);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return new Result(plan, solutionRefs, evaluatedById, EmptyMap());

        // Kök ALTINDA olmayan bir yol aynen kalır (savunmacı): kimlik uydurmaktansa fiziksel yolu korumak
        // yeğdir — böyle bir yol zaten bu planın parçası olmamalıdır.
        string Rebase(string path) =>
            path.StartsWith(from, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(to, path[from.Length..])
                : path;

        var nodes = plan.Nodes.Select(n => n with
        {
            Id = Rebase(n.Id),
            ProjectPath = Rebase(n.ProjectPath),
            Dependencies = [.. n.Dependencies.Select(Rebase)],
        }).ToList();

        var cycles = plan.Cycles.Select(c => (IReadOnlyList<string>)[.. c.Select(Rebase)]).ToList();

        var refs = solutionRefs.ToDictionary(kv => Rebase(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var evaluated = evaluatedById.ToDictionary(
            kv => Rebase(kv.Key),
            kv => kv.Value with
            {
                Path = Rebase(kv.Value.Path),
                CompileFiles = [.. kv.Value.CompileFiles.Select(Rebase)],
                ProjectReferences = [.. kv.Value.ProjectReferences.Select(Rebase)],
            },
            StringComparer.OrdinalIgnoreCase);

        var buildPaths = plan.Nodes.ToDictionary(n => Rebase(n.Id), n => n.Id, StringComparer.OrdinalIgnoreCase);

        return new Result(plan with { Nodes = nodes, Cycles = cycles }, refs, evaluated, buildPaths);
    }

    private static string WithSeparator(string root)
    {
        string full = Path.GetFullPath(root);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    private static IReadOnlyDictionary<string, string> EmptyMap() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
