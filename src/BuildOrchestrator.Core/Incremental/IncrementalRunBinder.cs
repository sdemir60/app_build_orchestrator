using System.Collections.Concurrent;
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
    private readonly IReadOnlyDictionary<string, EvaluatedProject> _evaluatedById;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProjectInput>> _inputsById;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _foldersById;
    // Fingerprint'ler Prefill'de PARALEL ısıtılır (aşağıda) ve DFS'ten tek tek okunur — eşzamanlı sözlük şart.
    private readonly ConcurrentDictionary<string, string?> _fingerprintById = new(StringComparer.OrdinalIgnoreCase);
    // [Faz 3/Task 4] OutputsById TEMBEL hesaplanır — yalnız istenirse (başarılı derlemeler için Supervisor okur).
    private IReadOnlyDictionary<string, ProjectOutputs>? _outputsById;

    /// <param name="plan">Bağlanacak plan.</param>
    /// <param name="evaluatedById">projectId (tam csproj yolu) → değerlendirme; eksik proje yalnız kendi
    /// klasörünün taramasıyla temsil edilir.</param>
    /// <param name="workspaceRoot">Çalışma alanı kökü — imzanın yol terimleri buna göredir.</param>
    /// <param name="hashes">Kaynak içerik özetlerinin önbelleği (koşu boyunca TEK örnek).</param>
    public IncrementalRunBinder(
        BuildPlan plan,
        IReadOnlyDictionary<string, EvaluatedProject> evaluatedById,
        string workspaceRoot,
        SourceHashCache hashes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evaluatedById);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(hashes);

        _plan = plan;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _hashes = hashes;
        _evaluatedById = evaluatedById;
        // Girdi toplama proje başına BAĞIMSIZDIR ve büyük kısmı klasör taramasıdır (IO). Gerçek OSYS'te 177
        // projenin toplamı seri koşuşta ölçülebilir bir gecikmeydi; paralel toplamak sonucu değiştirmez.
        // [Task 2] Klasörler de AYNI taramadan (CollectWithFolders) toplanır — ikinci bir yürüyüş yapılmaz.
        var collectedInputs = new ConcurrentDictionary<string, IReadOnlyList<ProjectInput>>(StringComparer.OrdinalIgnoreCase);
        var collectedFolders = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(plan.Nodes, new ParallelOptions { MaxDegreeOfParallelism = 16 }, node =>
        {
            var (files, folders) = ProjectInputs.CollectWithFolders(
                node.Id, evaluatedById.TryGetValue(node.Id, out var ev) ? ev : null, _workspaceRoot);
            collectedInputs[node.Id] = files;
            collectedFolders[node.Id] = folders;
        });
        _inputsById = collectedInputs;
        _foldersById = collectedFolders;
    }

    /// <summary>Bu koşuda özeti gerekecek TÜM girdi dosyaları (tekil).</summary>
    public IReadOnlyList<string> InputPaths =>
        [.. _inputsById.Values.SelectMany(i => i).Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Önbellekte olmayan dosyaların özetlerini PARALEL hesaplar (bkz. <see cref="SourceHashCache.Prefill"/>).
    /// Bağlamadan ÖNCE çağrılır: aksi hâlde ilk geçiş tek tek, sıralı okunurdu — ölçümde soğuk diskte dosya
    /// başına 8,89 ms yerine 1,86 ms.
    /// </summary>
    /// <param name="announce">Okunacak dosya sayısı, okuma başlamadan önce (konsol satırı için).</param>
    public int Prefill(Action<int>? announce = null, CancellationToken ct = default)
    {
        int read = _hashes.Prefill(InputPaths, announce, ct);

        // Fingerprint'ler de BURADA, proje başına paralel ısıtılır. Ölçüldü: sıcak önbellekte bile bedelin
        // yarısı stat geçişiydi ve bağlama DFS'i tek iş parçacığında ilerlediği için o geçiş seri koşuyordu
        // (gerçek OSYS'te 177 proje / 22.982 dosya: 544 ms). Projeler birbirinden bağımsız olduğundan ısıtma
        // paralelleştirilebilir; hesaplanan değer birebir aynıdır, yalnız daha erken ve daha hızlı hazırdır.
        Parallel.ForEach(
            _plan.Nodes,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            node => FingerprintOf(node));

        return read;
    }

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

    /// <summary>
    /// [v1.16.0] Proje → KENDİ girdi dosyalarının içerik özeti (upstream ve configuration HARİÇ).
    ///
    /// <para>İmzadan ayrı yayınlanır çünkü ayrı bir soruyu cevaplar: "bu projenin kendi dosyaları değişti mi?"
    /// Satırın <c>modified</c> ↔ <c>affected</c> ayrımı ve deftere yazılan <see cref="BuildState.BuiltContent"/>
    /// bunu okur. <c>Fast</c> geçişinden türetilemez: bir bağımlılığın kaydı geçersizleştiğinde (hata sonrası)
    /// Fast de "değişti" der ve satır kullanıcının hiç dokunmadığı bir projeye <c>modified</c> yazardı.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string?> ContentById =>
        _plan.Nodes.ToDictionary(n => n.Id, FingerprintOf, StringComparer.OrdinalIgnoreCase);

    /// <summary>Bir projenin girdi dosyaları — tanı ve test içindir.</summary>
    public IReadOnlyList<ProjectInput> InputsOf(string projectId) =>
        _inputsById.TryGetValue(projectId, out var inputs) ? inputs : [];

    /// <summary>[Task 2] Bir projenin gezilen klasörleri (proje klasörü dahil, <c>obj</c>/<c>bin</c> hariç)
    /// — "zaman modu"nda bir dosya silme/yeniden adlandırmayı klasör mtime'ından yakalamak içindir; bkz.
    /// <see cref="ProjectInputs.CollectWithFolders"/>.</summary>
    public IReadOnlyList<string> FoldersOf(string projectId) =>
        _foldersById.TryGetValue(projectId, out var folders) ? folders : [];

    /// <summary>
    /// [Faz 3/Task 4 — spec 2026-09-18 §5.1] Her düğüm için çıktı kanıtı + beslenen aday kopyalar (<see
    /// cref="OutputEvidence.Locate"/>) — ikinci bir hesap YOK, Supervisor başarılı bir derlemeden sonra bunu
    /// okuyup <see cref="OutputEvidence.LearnFedOutputs"/> ile <see cref="Contracts.Model.BuildState.FedOutputs"/>'a
    /// yazar. Kanıt yolu türetilemeyen (SDK-style, belirsiz OutputPath) düğümler haritada YOKTUR — <see
    /// cref="OutputEvidence.Locate"/>'in <c>null</c> dönüşü.
    /// </summary>
    public IReadOnlyDictionary<string, ProjectOutputs> OutputsById => _outputsById ??= ComputeOutputsById();

    private IReadOnlyDictionary<string, ProjectOutputs> ComputeOutputsById()
    {
        // Ters kenar: her düğümün ÜRETİCİLERİ (Dependencies) için "beni tüketen" listesine kendini ekler —
        // graf zaten kurulu, ikinci bir DFS/tarama YOK.
        var dependentsById = new Dictionary<string, List<EvaluatedProject>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _plan.Nodes)
        {
            if (!_evaluatedById.TryGetValue(node.Id, out var dependentProject)) continue;
            foreach (string producerId in node.Dependencies)
            {
                if (!dependentsById.TryGetValue(producerId, out var list))
                    dependentsById[producerId] = list = [];
                list.Add(dependentProject);
            }
        }

        var result = new Dictionary<string, ProjectOutputs>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _plan.Nodes)
        {
            var project = _evaluatedById.TryGetValue(node.Id, out var ev) ? ev : null;
            IReadOnlyList<EvaluatedProject> dependents =
                dependentsById.TryGetValue(node.Id, out var list) ? list : [];
            if (OutputEvidence.Locate(project, _plan.Configuration, dependents) is { } outputs)
                result[node.Id] = outputs;
        }
        return result;
    }

    /// <summary>
    /// Fingerprint koşu boyunca proje başına BİR KEZ hesaplanır: iki bağlama geçişi (Safe/Fast) ve SCC
    /// kompoziti aynı değeri okur — hem ikinci bir stat geçişi ödenmez hem de iki geçiş aynı diski görür.
    /// </summary>
    private string? FingerprintOf(ProjectNode node) =>
        _fingerprintById.GetOrAdd(node.Id, _ => IncrementalPlanner.ComputeContentFingerprint(
            InputsOf(node.Id), path => PathTerm(_workspaceRoot, path), _hashes.HashOf));

    /// <summary>
    /// [D5] Bir girdi dosyasının imzaya giren YOL terimi: çalışma alanı kökünün altındaysa köke göreli ve
    /// <c>/</c>-normalize, değilse tam yol (yine <c>/</c>-normalize).
    ///
    /// <para>Köke göreli olmak zorunludur: aynı içerik, reponun başka bir klonunda (başka bir kökün altında)
    /// da AYNI imzayı üretmelidir — tam yol kullanılsaydı imza içeriği değil konumu ölçerdi.</para>
    ///
    /// <para>Kök DIŞINDAKİ girdiler (harici köklerden gelen projeler, kökün üstündeki bir
    /// <c>Directory.Build.props</c>) tam yolla temsil edilir: onların köke göreli bir kimliği yoktur ve
    /// bulundukları yer koşudan koşuya değişmez.</para>
    /// </summary>
    public static string PathTerm(string workspaceRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(path);

        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(workspaceRoot, full);
        bool outside = Path.IsPathRooted(relative)
            || relative.StartsWith("..", StringComparison.Ordinal);

        return (outside ? full : relative).Replace('\\', '/');
    }
}
