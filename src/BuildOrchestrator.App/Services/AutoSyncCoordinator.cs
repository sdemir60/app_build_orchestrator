using System.IO;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [Faz 2/T7] <see cref="AutoSyncCoordinator"/>'ın VM'e açılan dar kapısı — koordinatör WPF'siz test edilir
/// (sahte port), üretimde <see cref="RunViewModel"/> uygular.
/// </summary>
internal interface IAutoSyncPort
{
    /// <summary>Bir workspace seçili mi.</summary>
    bool HasWorkspace { get; }

    /// <summary>Workspace'e dokunan bir iş (Sync/Clean/Optimize/checkout/pull) YA DA bir koşu uçuşta mı — tetik bekletilir.</summary>
    bool IsWorkspaceBusy { get; }

    /// <summary>Bir koşu uçuşta mı — planlama dahil, <c>runCompleted</c> gelene dek (<c>runStopped</c> kilidi düşürse
    /// de) — <see cref="IsWorkspaceBusy"/>'nin koşu yarısı. Bitişi (true → false) koşunun sonudur.</summary>
    bool IsRunInFlight { get; }

    /// <summary>[spec §6.1 · karar 10] Uçuştaki koşuyu branch değişimi yüzünden nazikçe keser: akışa tek satır ve
    /// <c>StopKind.Interrupt</c>. Kesme isteyen koşu bitince özeti <see cref="TakeInterruptedRunSummary"/> verir.</summary>
    Task RequestInterruptAsync();

    /// <summary>Kesilen son koşunun özet satırı (bir kez verilir); kesilen koşu yoksa <c>null</c>.</summary>
    string? TakeInterruptedRunSummary();

    /// <summary>Son tamamlanan Sync'in ölçtüğü HEAD; hiç Sync tamamlanmadıysa <c>null</c>.</summary>
    (string? Branch, string? HeadSha)? LastSyncHead { get; }

    /// <summary>Son Sync'in en yeni anı (başlangıç ya da bitiş, monoton ms); hiç Sync yoksa <c>null</c>.</summary>
    long? LastSyncAtMs { get; }

    /// <summary>Şimdiki monoton ms — <see cref="LastSyncAtMs"/> ile aynı saat.</summary>
    long NowMs();

    /// <summary>Sessiz Sync (<see cref="SyncMode.Silent"/>); kapı kapalıysa ya da gönderim düştüyse <c>false</c> —
    /// koordinatör tetiği bekletir.</summary>
    Task<bool> SyncSilentlyAsync(SilentSyncReason reason);

    /// <summary>Dışarıdan gelen branch değişimi: yeni bölüm (<see cref="SyncMode.BranchChange"/>), ilk satırları
    /// <paramref name="sectionLines"/>. Kapı kapalıysa ya da gönderim düştüyse <c>false</c>.</summary>
    Task<bool> SyncAfterExternalBranchChangeAsync(IReadOnlyList<string> sectionLines);

    /// <summary>Konsola (temizlemeden) tek satır.</summary>
    void AppendConsoleLine(string line);

    /// <summary>Olay akışına (temizlemeden) tek bilgi satırı.</summary>
    void AppendStreamLine(string line);

    /// <summary>[T9 · spec §6.4 · karar 22] Git dizinini yoklar: yarıda bir git işlemi varsa <c>true</c> — tetik bekler,
    /// akışa işlem başına BİR kez "waiting for git" satırı düşer (satırı VM yazar). İşaret kalkınca VM meşguliyet
    /// bildirimini (<see cref="AutoSyncCoordinator.OnWorkspaceIdle"/>) atar ve bekleyen tetik değerlendirilir.</summary>
    bool WaitForGitOperation();
}

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1 · §6.2 · karar 11] Kendiliğinden Sync'in kararı: HEAD izleyicisinin tetiği
/// (<see cref="HeadTriggerAsync"/>) ve pencereye dönüş (<see cref="WindowActivatedAsync"/>) buradan geçer.
/// </summary>
internal sealed class AutoSyncCoordinator : IDisposable
{
    /// <summary>Pencereye dönüşün eşiği: son Sync'ten (başlangıç ya da bitiş) bu kadar geçmediyse dönüş Sync'lemez
    /// (spec karar 11) — TEK tanım.</summary>
    public const long ActivationQuietMs = 5000;

    private readonly IAutoSyncPort _port;
    private readonly Action<Action> _post;
    private readonly Func<string?, HeadState?> _readHead;
    private readonly Func<IHeadWatcher> _newWatcher;

    /// <param name="port">VM köprüsü.</param>
    /// <param name="post">UI thread'ine taşıma (üretimde <c>Dispatcher.InvokeAsync</c>) — izleyicinin geri
    /// çağrısı ve meşguliyet bitişindeki yeniden değerlendirme bundan geçer.</param>
    /// <param name="readHead">Git dizininden HEAD (verilmezse <see cref="HeadReader.Read"/>); git dizini yoksa
    /// argüman <c>null</c>'dır.</param>
    /// <param name="newWatcher">İzleyici fabrikası (verilmezse <see cref="HeadWatcher"/>).</param>
    public AutoSyncCoordinator(IAutoSyncPort port, Action<Action> post,
        Func<string?, HeadState?>? readHead = null, Func<IHeadWatcher>? newWatcher = null)
    {
        _port = port;
        _post = post;
        _readHead = readHead ?? ReadHeadFromDisk;
        _newWatcher = newWatcher ?? (() => new HeadWatcher());
    }

    /// <summary>İzleyicinin bağlı olduğu kök ve git dizini; <see cref="Attach"/> değiştirir.</summary>
    private string _root = string.Empty;
    private string? _gitDir;
    private IHeadWatcher? _watcher;

    /// <summary>[review M3] Güncel izleyicinin kuşağı: her bırakma artırır. UI kuyruğuna atılmış bir geri çağrı
    /// yalnız kendi kuşağı hâlâ güncelse (ve koordinatör kapanmadıysa) tetik olur — değiştirilmiş/kapatılmış
    /// izleyicinin gecikmiş bildirimi yeni kökte Sync üretmez.</summary>
    private int _generation;
    private bool _disposed;

    /// <summary>"İzleyici kurulamadı" satırı yazılmış kökler — satır kök başına BİR kez (spec §6.1).</summary>
    private readonly HashSet<string> _reportedRoots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Meşgulken gelen tetiklerin en güçlüsü (bkz. <see cref="Remember"/>); yoksa <c>null</c>.</summary>
    private Trigger? _pending;

    /// <summary>Meşguliyet bitişinin yeniden değerlendirmesi UI kuyruğunda bekliyor mu — bitişi bildiren her
    /// geçiş (bir iş birden çok bayrağı sırayla bırakır) ikinci bir değerlendirme kuyruklamasın.</summary>
    private bool _reevaluationQueued;

    /// <summary>[T8] Son bildirimde bir koşu uçuştaydı — koşunun bitişi (<see cref="OnWorkspaceIdle"/>) buradan anlaşılır.</summary>
    private bool _runWasInFlight;

    /// <summary>[T8] Uçuştaki koşu için kesme istendi — kesme koşu başına BİR kez; koşu bitince sıfırlanır.</summary>
    private bool _interruptRequested;

    /// <summary>Bir tetik: HEAD hareketi ya da pencereye dönüş (<see cref="Move"/> = <c>null</c>).</summary>
    /// <param name="Move">İzleyicinin sınıfladığı hareket; pencereye dönüşte <c>null</c>.</param>
    /// <param name="RunEnded">[T8 · spec §6.1 güvenlik ağı] İzleyiciden değil koşunun bitişinden gelen tetik: HEAD
    /// okunamıyorsa hiçbir şey yapmaz — aksi hâlde git'siz bir kökte her koşudan sonra bir Sync koşardı.</param>
    internal readonly record struct Trigger(HeadMove? Move, bool RunEnded = false)
    {
        public bool IsActivation => Move is null;

        /// <summary>Güç sırası: pencereye dönüş ve koşu bitişinin güvenlik ağı &lt; commit &lt; branch değişimi/diğer
        /// (<see cref="HeadMoveRules.Weight"/>) — güvenlik ağı izleyicinin gerçek bir tetiğinin yerini almaz.</summary>
        public int Weight => !RunEnded && Move is { } move ? move.Weight() : 0;
    }

    /// <summary>[Task 8/9 dikişi] Meşgulken saklanan tek tetik — koşu kesme (Task 8) ve git-işlemi kapısı (Task 9)
    /// bunu okur/devralır.</summary>
    internal Trigger? PendingTrigger => _pending;

    /// <summary>
    /// İzleyiciyi <paramref name="root"/>'a bağlar (kök değiştiyse eskisi kapanır). Git dizini yoksa izleyici
    /// kurulmaz ve satır yazılmaz (izlenecek bir şey yok); kurulamazsa konsola kök başına BİR satır.
    /// </summary>
    public void Attach(string root)
    {
        root ??= string.Empty;
        if (_disposed) return;
        if (string.Equals(root, _root, StringComparison.OrdinalIgnoreCase) && _watcher is not null) return;

        DisposeWatcher();
        if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase)) _pending = null; // eski kökün tetiği yeni kökte anlamsız
        _root = root;
        _gitDir = root.Length == 0 ? null : ResolveGitDir(root);
        if (_gitDir is null) return;

        var watcher = _newWatcher();
        int generation = _generation;
        if (watcher.Start(_gitDir, move => _post(() =>
            {
                if (_disposed || generation != _generation) return; // bayat izleyicinin bildirimi
                _ = HeadTriggerAsync(move);
            })))
        {
            _watcher = watcher;
            return;
        }

        string reason = watcher.UnavailableReason ?? "unknown reason";
        watcher.Dispose();
        if (_reportedRoots.Add(root)) _port.AppendConsoleLine(PlanProgressLines.HeadWatcherUnavailable(reason));
    }

    /// <summary>İzleyicinin tetiği (UI thread'inde): <paramref name="move"/> reflog'un sınıflanmış hareketi.</summary>
    public Task HeadTriggerAsync(HeadMove move) => EvaluateAsync(new Trigger(move));

    /// <summary>Pencereye dönüş (UI thread'inde) — spec karar 11. [review M4] Kurulamamış izleyici (ör. henüz reflog'u
    /// olmayan depo: <c>logs</c> ilk commit'le doğar) burada yeniden denenir; ucuzdur (bir klasör kontrolü) ve
    /// "unavailable" satırı kök başına yine BİR kez kalır.</summary>
    public Task WindowActivatedAsync()
    {
        if (!_disposed && _watcher is null && _gitDir is not null) Attach(_root);
        return EvaluateAsync(new Trigger(null));
    }

    /// <summary>
    /// VM'in TEK "meşguliyet değişti" bildirimi (Sync/Clean/Optimize/checkout/pull bayrağı ya da koşu kilidi). Bekleyen
    /// bir tetik varsa ve artık meşgul değilse yeniden değerlendirme UI kuyruğuna ATILIR — senkron değil: bildirim
    /// bir durum geçişinin ortasında gelir (ör. Sync'in bitişi fazı henüz yazmadı) ve orada yeni bir Sync başlatmak
    /// o geçişin kalanını ezerdi.
    /// </summary>
    public void OnWorkspaceIdle()
    {
        // [T8 · spec §6.1] Koşunun bitişi: kesme hakkı sonraki koşuya devreder ve — bekleyen bir tetik yoksa —
        // güvenlik ağı tetiği kurulur: izleyici bir HEAD değişimini kaçırdıysa (ağ sürücüsü, taşan tampon) koşu
        // bitince yine yakalanır. HEAD son Sync'tekiyle aynıysa değerlendirme hiçbir şey yapmaz.
        bool runInFlight = _port.IsRunInFlight;
        if (_runWasInFlight && !runInFlight)
        {
            _interruptRequested = false;
            _pending ??= new Trigger(HeadMove.Other, RunEnded: true);
        }
        _runWasInFlight = runInFlight;

        if (_pending is null || _port.IsWorkspaceBusy || _reevaluationQueued) return;
        _reevaluationQueued = true;
        _post(() =>
        {
            _reevaluationQueued = false;
            if (_pending is not { } pending || _port.IsWorkspaceBusy) return;
            _pending = null;
            _ = EvaluateAsync(pending);
        });
    }

    /// <summary>
    /// Kararın kendisi (spec §6.1 · §6.2 · karar 11), sırasıyla:
    /// <list type="number">
    /// <item>Workspace yok → hiçbir şey.</item>
    /// <item>Koşu uçuşta (spec §6.1 · karar 10): commit → hiçbir şey (saklanmaz da); diğer HEAD hareketi (checkout,
    /// pull, merge, reset, rebase) → koşu BİR kez nazikçe kesilir (<see cref="IAutoSyncPort.RequestInterruptAsync"/>)
    /// ve tetik saklanır; pencereye dönüş → saklanır. Koşu bitince bekleyen tetik değerlendirilir.</item>
    /// <item>Meşgul (workspace işi) → tetik saklanır (<see cref="Remember"/>), meşguliyet bitince yeniden
    /// değerlendirilir (<see cref="OnWorkspaceIdle"/>).</item>
    /// <item>[T9 · spec §6.4] Yarıda bir git işlemi (merge, rebase, cherry-pick, revert, <c>index.lock</c>) → tetik
    /// saklanır (<see cref="IAutoSyncPort.WaitForGitOperation"/>); işaret kalkınca VM'in yoklaması meşguliyet
    /// bildirimini atar ve tetik normal yoldan değerlendirilir.</item>
    /// <item>Hiç Sync tamamlanmamış → hiçbir şey: kıyaslanacak HEAD yok ve ilk bölümü açılış Sync'i açar.</item>
    /// <item>Pencereye dönüş ve son Sync'ten <see cref="ActivationQuietMs"/> geçmemiş → hiçbir şey.</item>
    /// <item>HEAD okunamıyor → sessiz yenileme (güvenli yön); koşu bitişinin güvenlik ağı tetiğinde hiçbir şey.</item>
    /// <item>Branch adı farklı → yeni bölüm (<see cref="SyncMode.BranchChange"/>), ilk satır yeni branch.</item>
    /// <item>HEAD (branch + commit) son Sync'tekiyle aynı ve tetik bir HEAD hareketi → hiçbir şey (çift Sync yok).</item>
    /// <item>Aksi → sessiz Sync: commit → <see cref="SilentSyncReason.Commit"/>, diğer → <see cref="SilentSyncReason.Refresh"/>.</item>
    /// </list>
    /// <para>Pencereye dönüş HEAD aynı olsa da sessiz yeniler (spec §6.2 tablosu: "bir şey değiştiyse akışa tek
    /// satır") — kullanıcı başka bir pencerede dosya düzenlemiş olabilir; bunu yalnız dönüş görür.</para>
    /// <para>[T8 · spec §6.2] Kesilen koşunun özeti (<see cref="IAutoSyncPort.TakeInterruptedRunSummary"/>) varsa:
    /// branch değiştiyse yeni bölümün İLK satırıdır (switch satırından önce); değişmediyse (aynı branch'te pull/reset)
    /// bölüm açılmaz — özet akışa tek satır düşer ve HEAD aynı olsa da sessiz Sync koşar (kesilen koşunun sonuçları
    /// defterde değişti).</para>
    /// </summary>
    private async Task EvaluateAsync(Trigger trigger)
    {
        if (!_port.HasWorkspace) return;
        if (_port.IsRunInFlight && trigger.Move is { } move)
        {
            if (move == HeadMove.Commit) return;
            Remember(trigger);
            if (_interruptRequested) return;
            _interruptRequested = true;
            await _port.RequestInterruptAsync();
            return;
        }
        if (_port.IsWorkspaceBusy || _port.WaitForGitOperation())
        {
            Remember(trigger);
            return;
        }
        string? summary = _port.TakeInterruptedRunSummary();
        if (_port.LastSyncHead is not { } last)
        {
            if (summary is not null) _port.AppendStreamLine(summary);
            return;
        }
        if (summary is null && trigger.IsActivation && _port.LastSyncAtMs is { } at && _port.NowMs() - at < ActivationQuietMs) return;

        var head = _readHead(_gitDir);
        if (head is null)
        {
            if (summary is null && trigger.RunEnded) return;
            KeepIfNotSent(trigger, await SyncSilentlyAfterAsync(summary, SilentSyncReason.Refresh));
            return;
        }

        if (!string.Equals(head.Branch, last.Branch, StringComparison.Ordinal))
        {
            string switched = SwitchedLine(last, head);
            KeepIfNotSent(trigger,
                await _port.SyncAfterExternalBranchChangeAsync(summary is null ? [switched] : [summary, switched]));
            return;
        }
        if (summary is not null)
        {
            KeepIfNotSent(trigger, await SyncSilentlyAfterAsync(summary, SilentSyncReason.Refresh));
            return;
        }
        if (!trigger.IsActivation && string.Equals(head.Sha, last.HeadSha, StringComparison.Ordinal)) return;

        KeepIfNotSent(trigger,
            await _port.SyncSilentlyAsync(trigger.Move == HeadMove.Commit ? SilentSyncReason.Commit : SilentSyncReason.Refresh));
    }

    /// <summary>Sessiz Sync; önünde kesilen koşunun özeti varsa önce o akışa tek satır düşer.</summary>
    private async Task<bool> SyncSilentlyAfterAsync(string? summary, SilentSyncReason reason)
    {
        if (summary is not null) _port.AppendStreamLine(summary);
        return await _port.SyncSilentlyAsync(reason);
    }

    /// <summary>[final review I1] Port Sync'i gönderemediyse (kapı kapalı ya da gönderim düştü — ör. motor ölü) tetik
    /// KAYBOLMAZ: bekleyen tetik olarak kalır ve bir sonraki meşguliyet bitişinde (<see cref="OnWorkspaceIdle"/>;
    /// motorun yeniden hazır oluşu da bir geçiştir) yeniden değerlendirilir. Kesilen koşunun özeti zaten tüketildi —
    /// o satır tekrar yazılmaz.</summary>
    private void KeepIfNotSent(Trigger trigger, bool sent)
    {
        if (!sent) Remember(trigger);
    }

    /// <summary>Tek bekleyen tetik: yenisi eskisinden güçlüyse (ya da eşitse) yerini alır.</summary>
    private void Remember(Trigger trigger)
    {
        if (_pending is { } pending && pending.Weight > trigger.Weight) return;
        _pending = trigger;
    }

    /// <summary>Dışarıdan branch değişiminin bölüm satırı — checkout'unkiyle AYNI metin
    /// (<see cref="PlanProgressLines.SwitchedBranch"/>). Detached HEAD'in adı kısa sha'sıdır.</summary>
    private static string SwitchedLine((string? Branch, string? HeadSha) last, HeadState head) =>
        PlanProgressLines.SwitchedBranch(
            from: last.Branch ?? RevisionText.Short(last.HeadSha),
            to: head.Branch ?? RevisionText.Short(head.Sha),
            revision: RevisionText.Short(head.Sha));

    private static string? ResolveGitDir(string root)
    {
        try { return GitDirectory.Resolve(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static HeadState? ReadHeadFromDisk(string? gitDir)
    {
        if (gitDir is null) return null;
        try { return HeadReader.Read(gitDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        _generation++;
    }

    public void Dispose()
    {
        _disposed = true;
        DisposeWatcher();
        _pending = null;
    }
}
