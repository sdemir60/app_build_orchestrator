namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [K2] Sıra-koruyan ready-set scheduler, İLERİ ATLAMALI: boş bir slot dolduğunda <see cref="BuildPlan.Nodes"/>
/// sırasında (build-order) EN ÖNDE olan, bağımlılıkları çözülmüş (ready) projeyi dispatch eder;
/// bağımlılığı henüz çözülmemiş projelerin üzerinden atlanır (asla onlarda beklemez). Rastgele/hash sırası
/// yok — aynı graf + aynı complete sırası ⇒ her zaman aynı dispatch dizisi [D8].
///
/// Bir bağımlılık "çözülmüş" sayılır: Succeeded | Failed | Skipped (yalnız Succeeded değil) — başarısız bir
/// bağımlılık dependent'ini BLOKLAMAZ, aksi halde tek bir hata run'ı sonsuza dek bekletirdi ("hata derlemeyi
/// öldürmez", A3). Bu durumun raporlanması (depIssue zinciri, ▲ badge) It-3'ün işi (T54) — burada yok.
///
/// InCycle=true node'lar (TopoSort'un SCC üyeleri, Nodes içinde hâlâ mevcut) construction anında
/// Skipped("in dependency cycle") sayılıp PreSkipped'e yazılır; böylece bağımlıları için çözülmüş kabul
/// edilirler (yoksa asla ready olamayacakları için run kilitlenir) — plan A6.
///
/// Saf Core state: I/O, process, async, log YOK [D3]. Thread-safety: TryDispatch/Complete/RequestStop ve tüm
/// okuma üyeleri (QueuedProjectIds/Completed/IsDone/InFlight) tek bir lock (_gate) altında senkronize edilir.
/// Task 9, bunu N paralel worker'dan sürdüğü için gerekli; hot path olmadığından (177 proje, saniyede birkaç
/// çağrı) tek kilit yeterli ve basit — ince taneli kilitleme veya lock-free yapı YAGNI.
/// </summary>
public sealed class ReadySetScheduler
{
    private readonly object _gate = new();

    private readonly IReadOnlyList<ProjectNode> _nodesInOrder;               // plan.Nodes — zaten build-order
    private readonly Dictionary<string, ProjectNode> _byId;                  // dangling dependency tespiti için
    private readonly Dictionary<string, BuildResult> _completed;             // Succeeded/Failed/Skipped (cycle dahil)
    private readonly HashSet<string> _inFlight;                              // dispatch edildi, henüz Complete olmadı
    private readonly List<(string ProjectId, string Reason)> _preSkipped;    // construction'da cycle nedeniyle Skipped

    private bool _stopRequested;

    public ReadySetScheduler(BuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _nodesInOrder = plan.Nodes;
        _byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        _completed = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);
        _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _preSkipped = new List<(string, string)>();

        foreach (var node in _nodesInOrder)
        {
            _byId[node.Id] = node;
            if (node.InCycle)
            {
                // Cycle üyeleri asla ready olamaz (bağımlılıkları birbirine dairesel) — plan anında çözülmüş say.
                _completed[node.Id] = BuildResult.Skipped;
                _preSkipped.Add((node.Id, "in dependency cycle"));
            }
        }
    }

    /// <summary>Hiç dispatch edilmemiş (henüz TryDispatch tarafından verilmemiş) proje id'leri, build-order sıralı.</summary>
    public IReadOnlyList<string> QueuedProjectIds
    {
        get
        {
            lock (_gate) return QueuedLocked().ToList();
        }
    }

    /// <summary>Tamamlanmış (Succeeded/Failed/Skipped) projelerin sonuçları — cycle nedeniyle pre-skipped olanlar dahil.</summary>
    public IReadOnlyDictionary<string, BuildResult> Completed
    {
        get
        {
            lock (_gate) return new Dictionary<string, BuildResult>(_completed, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Dispatch edilmiş ama henüz Complete çağrılmamış proje sayısı.</summary>
    public int InFlight
    {
        get
        {
            lock (_gate) return _inFlight.Count;
        }
    }

    /// <summary>InFlight == 0 VE (stop istendi VEYA dispatch edilecek başka proje kalmadı).</summary>
    public bool IsDone
    {
        get
        {
            lock (_gate) return _inFlight.Count == 0 && (_stopRequested || !QueuedLocked().Any());
        }
    }

    /// <summary>Construction anında cycle nedeniyle Skipped sayılan projeler (build-order sıralı).</summary>
    public IReadOnlyList<(string ProjectId, string Reason)> PreSkipped => _preSkipped;

    /// <summary>
    /// Ready set'ten (bağımlılıkları çözülmüş, henüz dispatch/complete edilmemiş) build-order'da EN ÖNDE
    /// olanı verir; bloklu olanların üzerinden atlar (K2). Stop istendiyse veya ready hiçbir şey yoksa false.
    /// </summary>
    public bool TryDispatch(out string projectId)
    {
        lock (_gate)
        {
            if (!_stopRequested)
            {
                foreach (var node in _nodesInOrder)
                {
                    if (_completed.ContainsKey(node.Id) || _inFlight.Contains(node.Id)) continue;
                    if (!IsReadyLocked(node)) continue;

                    _inFlight.Add(node.Id);
                    projectId = node.Id;
                    return true;
                }
            }
        }
        projectId = null!;
        return false;
    }

    /// <summary>Dispatch edilmiş bir projeyi sonuçlandırır; dependent'lerini ready set'e açabilir.</summary>
    public void Complete(string projectId, BuildResult result)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        lock (_gate)
        {
            if (!_inFlight.Remove(projectId))
                throw new InvalidOperationException(
                    $"'{projectId}' in-flight değil (dispatch edilmemiş ya da zaten complete edilmiş) — Complete çağrılamaz.");
            _completed[projectId] = result;
        }
    }

    /// <summary>Bundan sonra TryDispatch daima false döner; halihazırda in-flight olan işler etkilenmez.</summary>
    public void RequestStop()
    {
        lock (_gate) _stopRequested = true;
    }

    // _gate zaten tutulu iken çağrılmalı.
    private bool IsReadyLocked(ProjectNode node) => node.Dependencies.All(IsResolvedLocked);

    // Bilinmeyen (plan'da node olarak bulunmayan) bağımlılık id'si, node'u sonsuza dek bloklamasın diye
    // çözülmüş sayılır — savunmacı: ProducerMap/GraphBuilder her zaman geçerli id üretir ama scheduler
    // bu varsayıma kör güvenmez.
    private bool IsResolvedLocked(string depId) => !_byId.ContainsKey(depId) || _completed.ContainsKey(depId);

    private IEnumerable<string> QueuedLocked() =>
        _nodesInOrder.Where(n => !_completed.ContainsKey(n.Id) && !_inFlight.Contains(n.Id)).Select(n => n.Id);
}
