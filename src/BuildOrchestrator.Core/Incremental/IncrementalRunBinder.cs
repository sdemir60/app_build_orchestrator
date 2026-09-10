using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [Task 19 wiring][D1] Planlama kökleri (Supervisor'ın <c>Program</c>'ı ve Core'un Sync servisi) ile <see
/// cref="IncrementalPlanner"/> arasındaki glue: her projenin GİRDİ KÜMESİNİ (<see cref="ProjectInputs"/>) bir
/// kez toplar, içerik özetlerini <see cref="SourceHashCache"/>'ten okur ve planı imzalarla bağlar.
///
/// <para><b>Neden bir NESNE (statik metot değil):</b> aynı koşu planı BİRDEN ÇOK kez bağlar — Sync hem
/// <c>Safe</c> (derlenecek küme) hem <c>Fast</c> (yalnız kendi dosyası değişenler) geçişini yapar, Build de
/// aynı ikiliyi satır etiketleri için ister. Girdi toplama (klasör taraması) ve fingerprint hesabı bu
/// geçişler arasında PAYLAŞILIR; statik bir metot her çağrıda diski yeniden tarardı.</para>
///
/// <para><b>§4:</b> DLL/bin/obj timestamp'ı ASLA okunmaz. Okunan tek şey KAYNAK dosyaların içeriğidir; stat
/// bilgisi yalnız <see cref="SourceHashCache"/>'in "yeniden özetlemeye gerek var mı" kapısıdır.</para>
///
/// <para><b>Kök varsayımı:</b> <c>workspaceRoot</c> kullanıcının çalışma alanı köküdür ve imzanın yol
/// terimleri ona göredir. Kökün DIŞINDA kalan girdiler (harici köklerden gelen projeler, kökün üstündeki bir
/// <c>Directory.Build.props</c>) tam yolla temsil edilir — bkz. <see cref="PathTerm"/>.</para>
/// </summary>
public sealed class IncrementalRunBinder
{
    private readonly BuildPlan _plan;
    private readonly string _workspaceRoot;
    private readonly SourceHashCache _hashes;
    private readonly Dictionary<string, IReadOnlyList<ProjectInput>> _inputsById;
    private readonly Dictionary<string, string?> _fingerprintById = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="plan">Bağlanacak plan (kimlikleri ana köke taşınmış olmalıdır — bkz. <see
    /// cref="BuildOrchestrator.Core.Planning.ProjectIdentityRebase"/>).</param>
    /// <param name="evaluatedById">projectId (tam csproj yolu) → değerlendirme; eksik proje yalnız kendi
    /// klasörünün taramasıyla temsil edilir.</param>
    /// <param name="workspaceRoot">Çalışma alanı kökü — imzanın yol terimleri buna göredir.</param>
    /// <param name="hashes">Kaynak içerik özetlerinin önbelleği (koşu boyunca TEK örnek).</param>
    /// <param name="physicalPathOf">Kimlik yolu → diskteki gerçek yol. Worktree koşusunda havuzdaki kopyayı
    /// gösterir; <c>null</c> ⇒ in-place.</param>
    public IncrementalRunBinder(
        BuildPlan plan,
        IReadOnlyDictionary<string, EvaluatedProject> evaluatedById,
        string workspaceRoot,
        SourceHashCache hashes,
        Func<string, string>? physicalPathOf = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evaluatedById);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(hashes);

        _plan = plan;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _hashes = hashes;
        _inputsById = plan.Nodes.ToDictionary(
            n => n.Id,
            n => ProjectInputs.Collect(
                n.Id, evaluatedById.TryGetValue(n.Id, out var ev) ? ev : null, _workspaceRoot, physicalPathOf),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Bu koşuda özeti gerekecek TÜM fiziksel dosyalar (tekil).</summary>
    public IReadOnlyList<string> PhysicalPaths =>
        [.. _inputsById.Values.SelectMany(i => i).Select(i => i.PhysicalPath).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Önbellekte olmayan dosyaların özetlerini PARALEL hesaplar (bkz. <see cref="SourceHashCache.Prefill"/>).
    /// Bağlamadan ÖNCE çağrılır: aksi hâlde ilk geçiş tek tek, sıralı okunurdu — ölçümde soğuk diskte dosya
    /// başına 8,89 ms yerine 1,86 ms.
    /// </summary>
    /// <param name="announce">Okunacak dosya sayısı, okuma başlamadan önce (konsol satırı için).</param>
    public int Prefill(Action<int>? announce = null, CancellationToken ct = default) =>
        _hashes.Prefill(PhysicalPaths, announce, ct);

    /// <summary>
    /// Planı incremental willBuild + imza haritası ile bağlar. Dönen imzalar <b>her zaman non-null</b>'dır:
    /// karar diskten geldiği için hesaplanamayan bir imza yoktur (bkz. <see cref="IncrementalPlanner"/>).
    /// </summary>
    /// <param name="state">projectId → build-state kaydı (<see cref="BuildOrchestrator.Core.State.BuildStateStore.Load"/>).</param>
    /// <param name="buildCycles">[Task 11] Bu koşu SCC üyelerini derliyor mu — yalnız <c>RunMode.Cycles</c>.
    /// <b>Varsayılanı YOKTUR:</b> her çağıran koşunun kapsamını açıkça yazar, yoksa o yüzeydeki önizleme
    /// motorla ayrışır.</param>
    /// <param name="mode">Safe (dirty + transitive dependent) ya da Fast (yalnız kendi terimi bayatlayanlar).</param>
    public (BuildPlan Plan, IReadOnlyDictionary<string, string> SignatureById) Bind(
        IReadOnlyDictionary<string, BuildState> state, bool buildCycles, DependentMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IncrementalPlanner.ComputeWillBuildWithSignatures(
            _plan, FingerprintOf, state, buildCycles, mode);
    }

    /// <summary>Bir projenin girdi dosyaları (kimlik + fiziksel yol) — tanı ve test içindir.</summary>
    public IReadOnlyList<ProjectInput> InputsOf(string projectId) =>
        _inputsById.TryGetValue(projectId, out var inputs) ? inputs : [];

    /// <summary>
    /// Fingerprint koşu boyunca proje başına BİR KEZ hesaplanır: iki bağlama geçişi (Safe/Fast) ve SCC
    /// kompoziti aynı değeri okur — hem ikinci bir stat geçişi ödenmez hem de iki geçiş aynı diski görür.
    /// </summary>
    private string? FingerprintOf(ProjectNode node)
    {
        if (_fingerprintById.TryGetValue(node.Id, out var cached)) return cached;

        string? fingerprint = IncrementalPlanner.ComputeContentFingerprint(
            InputsOf(node.Id), logical => PathTerm(_workspaceRoot, logical), _hashes.HashOf);

        _fingerprintById[node.Id] = fingerprint;
        return fingerprint;
    }

    /// <summary>
    /// [D5] Bir girdi dosyasının imzaya giren YOL terimi: çalışma alanı kökünün altındaysa köke göreli ve
    /// <c>/</c>-normalize, değilse tam yol (yine <c>/</c>-normalize).
    ///
    /// <para>Köke göreli olmak zorunludur: worktree koşusunda dosyalar başka bir kökün altında yaşar ve tam
    /// yol kullanılsaydı aynı içerik iki modda FARKLI imza üretirdi — worktree ile alınan tek bir Build'den
    /// sonra her şey yeniden "derlenecek" görünürdü.</para>
    ///
    /// <para>Kök DIŞINDAKİ girdiler (harici köklerden gelen projeler, kökün üstündeki bir
    /// <c>Directory.Build.props</c>) tam yolla temsil edilir: onların köke göreli bir kimliği yoktur ve
    /// bulundukları yer koşudan koşuya değişmez.</para>
    /// </summary>
    public static string PathTerm(string workspaceRoot, string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(logicalPath);

        string full = Path.GetFullPath(logicalPath);
        string relative = Path.GetRelativePath(workspaceRoot, full);
        bool outside = Path.IsPathRooted(relative)
            || relative.StartsWith("..", StringComparison.Ordinal);

        return (outside ? full : relative).Replace('\\', '/');
    }
}
