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
/// InCycle=true node'lar (TopoSort'un SCC üyeleri, Nodes içinde hâlâ mevcut), <see cref="CycleGroups"/>
/// verilmediyse (null — kill switch kapalı) construction anında Skipped("in dependency cycle") sayılıp
/// PreSkipped'e yazılır; böylece bağımlıları için çözülmüş kabul edilirler (yoksa asla ready olamayacakları
/// için run kilitlenir) — plan A6. <see cref="CycleGroups"/> verildiyse pre-skip YAPILMAZ: her SCC TEK iş
/// kalemi olarak ele alınır — hazırlığı TÜM üyelerin DIŞ bağımlılıklarına bakar (grup-içi/dairesel kenarlar
/// hariç), dispatch build-order'daki İLK dispatch edilebilir üyeyi verirken TÜM üyeleri in-flight işaretler.
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
    private readonly CycleGroups? _groups;                                   // [cycle rounds] null = kill switch kapalı

    private bool _stopRequested;

    // [Task 18] Boş bir snapshot: Completed boş, Queued/ElapsedMs bu ctor tarafından hiç okunmaz (yalnız
    // Completed kullanılır — bkz. resume ctor'un doc'u). Fresh ctor'u resume ctor'un ÜZERİNE (this(plan,
    // EmptySnapshot)) kurmak için var: iki neredeyse-birebir ctor gövdesi TEK gövdeye iner, davranış AYNI kalır
    // (boş Completed ile başlayan resume ctor, eski fresh ctor'un yaptığı HER ŞEYİ birebir yapar — cycle
    // guard'daki `!_completed.ContainsKey` kontrolü boş sözlükte her zaman true'dur, fresh ctor'un koşulsuz
    // eklemesiyle aynı sonucu verir).
    private static readonly RunSnapshot EmptySnapshot =
        new(new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase), [], 0);

    public ReadySetScheduler(BuildPlan plan, CycleGroups? cycleGroups = null)
        : this(plan, EmptySnapshot, cycleGroups)
    {
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
    /// savunmacı: snapshot Completed'ta cycle üyelerini taşımıyorsa (örn. elle kurulmuş bir snapshot, ya da
    /// [Task 18] fresh ctor'un devrettiği <see cref="EmptySnapshot"/>), yine de burada pre-skip edilirler —
    /// aksi halde bağımlılıkları birbirine dairesel olduğu için asla ready olamazlar ve run kilitlenir (plan
    /// A6, fresh ctor ile aynı garanti — artık TEK gövde, iki ayrı garanti değil).
    ///
    /// <paramref name="cycleGroups"/> [cycle rounds]: null (varsayılan) = kill switch KAPALI, yukarıdaki
    /// pre-skip davranışı BİREBİR korunur — mevcut tüm çağrı yerleri hiç değişmeden aynı sonucu almaya devam
    /// eder. Doldurulduğunda SCC'ler artık pre-skip EDİLMEZ; bunun yerine <see cref="IsReadyLocked"/> ve
    /// <see cref="TryDispatch"/> her SCC'yi TEK iş kalemi olarak ele alır (bkz. ilgili doc'lar).
    /// </summary>
    public ReadySetScheduler(BuildPlan plan, RunSnapshot snapshot, CycleGroups? cycleGroups = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        _nodesInOrder = plan.Nodes;
        _byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        _completed = new Dictionary<string, BuildResult>(snapshot.Completed, StringComparer.OrdinalIgnoreCase);
        _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _preSkipped = new List<(string, string)>();
        _groups = cycleGroups;

        foreach (var node in _nodesInOrder)
        {
            _byId[node.Id] = node;
            // [cycle rounds] Gruplar VERİLDİYSE pre-skip YOK — SCC tek iş kalemi olarak dispatch edilir ve
            // turlarla derlenir. Gruplar null ise (kill switch kapalı) eski davranış birebir korunur: üyeler
            // burada Skipped sayılır, yoksa dairesel bağımlılık nedeniyle asla ready olamaz ve run kilitlenirdi [A6].
            if (_groups is null && node.InCycle && !_completed.ContainsKey(node.Id))
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

    /// <summary>
    /// InFlight == 0 VE (stop istendi VEYA artık READY olabilecek hiçbir şey kalmadı).
    ///
    /// [Task 18] "queued boş mu" (eski formülasyon) DEĞİL, "ready olabilecek bir şey var mı" sorulur —
    /// self-loop güvenliği: bağımlılığı asla çözülemeyecek (ör. kendine bağımlı, InCycle olarak
    /// işaretlenmemiş sentetik/bozuk bir düğüm) bir proje sonsuza dek Queued'da kalabilir ama asla ready
    /// olamaz; eski formülasyon böyle bir düğüm varken IsDone'ı SONSUZA dek false döndürürdü (worker'lar
    /// WakeSignal üzerinde parkta kalır, hiçbir Complete tetiklenmediği için asla uyanmazlar — run askıda
    /// kalır). Yeni formülasyon: kalan (Completed/InFlight'ta olmayan) düğümlerden HİÇBİRİ ready değilse run
    /// terminal sayılır — worker'lar döner, run normal şekilde biter (kalan projeler Queued'da raporlanır).
    /// </summary>
    public bool IsDone
    {
        get
        {
            lock (_gate)
                return _inFlight.Count == 0 && (_stopRequested || !_nodesInOrder.Any(n =>
                    !_completed.ContainsKey(n.Id) && !_inFlight.Contains(n.Id) && IsReadyLocked(n)));
        }
    }

    /// <summary>Construction anında cycle nedeniyle Skipped sayılan projeler (build-order sıralı).</summary>
    public IReadOnlyList<(string ProjectId, string Reason)> PreSkipped => _preSkipped;

    /// <summary>
    /// Ready set'ten (bağımlılıkları çözülmüş, henüz dispatch/complete edilmemiş) build-order'da EN ÖNDE
    /// olanı verir; bloklu olanların üzerinden atlar (K2). Stop istendiyse veya ready hiçbir şey yoksa false.
    ///
    /// [cycle rounds] Bir grup (SCC) dispatch edildiğinde dönen id yalnızca grubun LİDERİDİR (build-order'daki
    /// ilk dispatch edilebilir üye) — ama bu TEK çağrıda grubun TÜM üyeleri in-flight'a girer (aşağıda).
    /// Çağıran bu yüzden ZORUNLUDUR: dispatch edilen id bir grup üyesiyse, <see cref="CycleGroups.MembersOf"/>
    /// (id)'nin döndürdüğü üyelerden — çağrı ANINDA zaten <see cref="Completed"/>'te olanlar HARİÇ, çünkü
    /// onlar bu çağrıda in-flight'a hiç girmedi — HER biri için <see cref="Complete"/>'i tam olarak BİR KEZ
    /// çağırmalıdır; stop/cancellation yolları DAHİL. Aksi halde o üye(ler) sonsuza dek in-flight kalır ve
    /// <see cref="IsDone"/> (InFlight == 0 şartı) hiçbir zaman true olmaz, run askıda kalır.
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

                    var members = _groups?.MembersOf(node.Id) ?? [];
                    if (members.Count == 0)
                    {
                        _inFlight.Add(node.Id);
                        projectId = node.Id;
                        return true;
                    }

                    // [cycle rounds] Grup: yalnız build-order'da İLK dispatch edilebilir üye (`head`) verilir;
                    // TÜM üyeler tek seferde in-flight'a eklenir (aşağıda). `head` pratikte hiçbir zaman null
                    // olamaz: `node` bu satıra kadar zaten yukarıdaki `:165` guard'ından geçmiştir (completed
                    // değil, in-flight değil) ve `node.Id` kendi `members` listesinin bir üyesidir —
                    // `FirstOrDefault` en azından `node`'u bulur. İkinci bir worker'ın AYNI gruba tekrar
                    // girmesini engelleyen de bu satır DEĞİL, o `:165` guard'ıdır: grup bir kez dispatch
                    // edildiğinde TÜM üyeleri in-flight'a girer, bu yüzden sonraki her TryDispatch çağrısında
                    // grubun her üyesi outer loop'un başında `_inFlight.Contains(node.Id)` ile elenir ve bu
                    // satıra hiç ulaşmaz.
                    string? head = members.FirstOrDefault(
                        m => !_completed.ContainsKey(m) && !_inFlight.Contains(m));
                    if (head is null) continue;   // savunmacı: yukarıdaki akıl yürütmeyle asla girilmez
                    foreach (string m in members)
                        // _byId.ContainsKey: plan'da karşılığı olmayan bir üye (savunmacı — CycleGroups
                        // böyle bir id'yi de listeye alabilir) in-flight'a hiç girmez; girerse asla
                        // Complete edilemez (hiçbir node onu temsil etmez) ve run sonsuza dek askıda kalırdı —
                        // IsResolvedLocked'ın (:255-258) aynı savunmacı deseni.
                        if (!_completed.ContainsKey(m) && _byId.ContainsKey(m)) _inFlight.Add(m);
                    projectId = head;
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
                    $"'{projectId}' is not in flight (never dispatched, or already completed) — Complete cannot be called.");
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
    // [cycle rounds] node bir SCC üyesiyse (_groups != null ve node.Id grup üyesi) hazırlık TEK kalem olarak
    // hesaplanır: TÜM üyelerin DIŞ (grup dışı) bağımlılıkları çözülmüş olmalı. Grup-içi kenarlar (tanımı
    // gereği dairesel) hariç tutulur — aksi halde grup asla ready olamazdı. _groups null ise (kill switch
    // kapalı) members her zaman boştur ve eski tek-satırlık davranış birebir korunur.
    private bool IsReadyLocked(ProjectNode node)
    {
        var members = _groups?.MembersOf(node.Id) ?? [];
        if (members.Count == 0) return node.Dependencies.All(IsResolvedLocked);

        foreach (string memberId in members)
            if (_byId.TryGetValue(memberId, out var member))
                foreach (string dep in member.Dependencies)
                    if (!members.Contains(dep, StringComparer.OrdinalIgnoreCase) && !IsResolvedLocked(dep))
                        return false;
        return true;
    }

    // Bilinmeyen (plan'da node olarak bulunmayan) bağımlılık id'si, node'u sonsuza dek bloklamasın diye
    // çözülmüş sayılır — savunmacı: ProducerMap/GraphBuilder her zaman geçerli id üretir ama scheduler
    // bu varsayıma kör güvenmez.
    private bool IsResolvedLocked(string depId) => !_byId.ContainsKey(depId) || _completed.ContainsKey(depId);

    private IEnumerable<string> QueuedLocked() =>
        _nodesInOrder.Where(n => !_completed.ContainsKey(n.Id) && !_inFlight.Contains(n.Id)).Select(n => n.Id);
}
