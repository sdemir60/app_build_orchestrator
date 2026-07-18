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
/// öldürmez", A3). Bu durumun raporlanması (depIssue zinciri, ▲ badge — It-3/T54) artık <see cref="DepIssueTracker"/>
/// ile gerçeklendi: bu class'ın resolved semantiği DEĞİŞMEDİ, yalnız <see cref="Completed"/> üzerinden okunur.
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

    /// <summary>
    /// [T55] Continue (resume) ctor'u: AYNI plan'dan devam eder — yeniden planlama/tarama/sıralama YOK.
    /// <paramref name="snapshot"/>.Completed olduğu gibi devralınır (bu projeler bir daha ASLA dispatch
    /// edilmez); geri kalan her şey (Completed'ta olmayan) build-order sırasıyla queued sayılır — bu,
    /// <paramref name="snapshot"/>.Queued alanının kendisi kullanılmadan da _completed üyeliğinden türetilir,
    /// çünkü TakeSnapshot ile Queued zaten "Completed'ta olmayanlar" olacak şekilde üretilir (partisyon
    /// invaryantı) — burada ayrıca kullanılması gereken tek şey Completed'tır.
    ///
    /// Cycle/pre-skip DAVRANIŞI korunur: normal akışta (TakeSnapshot'tan gelen snapshot) cycle üyeleri zaten
    /// önceki construction'da Skipped olarak Completed'a yazılmıştı, bu yüzden burada YENİDEN pre-skip
    /// edilmezler (PreSkipped bu construction için boş kalır — hiçbir şey YENİ pre-skip edilmedi). Ama
    /// savunmacı: snapshot Completed'ta cycle üyelerini taşımıyorsa (örn. elle kurulmuş bir snapshot),
    /// yine de burada pre-skip edilirler — aksi halde bağımlılıkları birbirine dairesel olduğu için asla
    /// ready olamazlar ve run kilitlenir (plan A6, fresh ctor ile aynı garanti).
    /// </summary>
    public ReadySetScheduler(BuildPlan plan, RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        _nodesInOrder = plan.Nodes;
        _byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        _completed = new Dictionary<string, BuildResult>(snapshot.Completed, StringComparer.OrdinalIgnoreCase);
        _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _preSkipped = new List<(string, string)>();

        foreach (var node in _nodesInOrder)
        {
            _byId[node.Id] = node;
            if (node.InCycle && !_completed.ContainsKey(node.Id))
            {
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

    /// <summary>
    /// [T55] Stop/Continue sınırını aşacak run state'ini alır (<paramref name="elapsedMs"/>, çağıran
    /// tarafından <see cref="RunClock.ElapsedMs"/>'ten geçirilir — bu class saat tutmaz [D3]).
    ///
    /// Queued = plan'daki, Completed'ta OLMAYAN tüm node id'leri, build-order sıralı — bu, dispatch edilmiş
    /// ama henüz Complete çağrılmamış (in-flight) projeleri DAHİL eder. Kasıtlı: gerçek Stop akışında engine
    /// snapshot'ı ancak in-flight tükendikten sonra alır (IsDone), ama Task 9 bu metodu worker'lar hâlâ
    /// koşarken de çağırabilir (thread-safety gereksinimi) — o anda in-flight olan bir proje ne tamamlanmış
    /// sayılabilir (sonucu henüz yok) ne de sessizce kaybolabilir; bu yüzden "henüz kesin bitmemiş her şey"
    /// Queued'a düşer. Sonuç: her node id tam olarak Completed VEYA Queued'dadır — asla ikisinde birden, asla
    /// hiçbirinde (bkz. ContinueRunTests.take_snapshot_mid_run_keeps_in_flight_project_in_queued_not_lost).
    ///
    /// Not: bu, QueuedProjectIds property'sinden farklıdır — o "hiç dispatch edilmemiş" demektir ve in-flight'ı
    /// hariç tutar (farklı bir soruya cevap verir: "TryDispatch'in bu run'da hiç vermediği projeler").
    /// </summary>
    public RunSnapshot TakeSnapshot(long elapsedMs)
    {
        lock (_gate)
        {
            var completed = new Dictionary<string, BuildResult>(_completed, StringComparer.OrdinalIgnoreCase);
            var queued = _nodesInOrder.Where(n => !_completed.ContainsKey(n.Id)).Select(n => n.Id).ToList();
            return new RunSnapshot(completed, queued, elapsedMs);
        }
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
