using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [design v1.11.0 §9-4 · §2.3 "İşlem koreografisi"] <b>Açılış koreografisinin sürücüsü.</b> Saf çekirdek
/// (<see cref="MarkingChoreography"/>) sayıları verir, <see cref="StepPlayer"/> zamanı sayar; bu sınıf ikisini
/// satırlara ve grafa bağlar.
///
/// <para>Sıra: <b>nötr an → random dalga (kapsam tek tek amber'a yanar) → sarı-gri an → örtüşen veda →
/// nefes.</b> Dalga, satırların <see cref="ProjectRowViewModel.Marked"/>'ını sırayla açarak oluşur —
/// prototipteki per-node <c>transition-delay</c>'in WPF karşılığı budur ve düğüm başına fırça animasyonu
/// gerektirmez.</para>
///
/// <para><b>Bitiş ve koşunun devralması.</b> Prototipte koşu koreografinin son anında başlar
/// (<c>_mark(scope, () =&gt; startRun())</c>): vedanın son opaklıkları (0.45 / 0.18) doğrudan koşu
/// opaklıklarına (1 / 0.13 / 0.2) geçer, arada "geri gelme" yoktur. Burada komut koreografi BİTİNCE gönderilir
/// (<c>RunViewModel.BeginRunAsync</c>'in kapısı) ve motor planlamaya (worktree → tarama → graf → incremental)
/// saniyeler harcayabilir; bu pencerede ekran koreografinin <b>son adımında TUTULUR</b> (<see cref="Settle"/>) —
/// <c>runStarted</c> gelince kabuk koşu fazını grafa iter ve ardından <see cref="Cancel"/> ile adımı düşürür,
/// yani settle → running tek geçiştir.
/// <b>[DEĞİŞEN KURAL — ölçüldü]</b> Eskiden doğal bitişte adım <see cref="MarkStep.None"/>'a düşüyor ve graf
/// 1.0 opaklığa GERİ GELİYOR, motor koşuyu başlatınca node'lar İKİNCİ kez sönüyordu — "sönüş ve akış garip"
/// diye görülen buydu.</para>
///
/// <para><b>Reduced-motion:</b> koreografi HİÇ oynamaz (§1.3 "tüm süreler 0") — kapsam işaretlenir ve satırlar
/// doğrudan koşu görünümüne geçer.</para>
/// </summary>
public sealed class OperationChoreographer
{
    private readonly StepPlayer _player = new();
    private readonly Func<bool> _animationsEnabled;
    private IReadOnlyList<ProjectRowViewModel> _scope = [];
    private int _runCount;

    public OperationChoreographer(Func<bool> animationsEnabled) =>
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));

    /// <summary>[test yüzeyi] O anki adım — koreografi oynamıyorsa <see cref="MarkStep.None"/>.</summary>
    public MarkStep Step { get; private set; } = MarkStep.None;

    /// <summary>[test yüzeyi] Koreografi şu an oynuyor mu.</summary>
    public bool IsPlaying => _player.IsPlaying;

    /// <summary>Grafa adım/kapsam iten kablo — <c>MainWindow</c> bağlar (kabuk bilgisi buraya sızmasın).</summary>
    public Action<MarkStep, IReadOnlySet<string>>? PushToGraph { get; set; }

    /// <summary>
    /// Koreografiyi baştan oynatır. Kapsam BOŞSA (ya da reduced-motion) hiç oynamaz: satırlar yalnız
    /// işaretlenir ve koşu görünümüne doğrudan geçilir.
    /// </summary>
    /// <param name="allRows">Listenin TÜM satırları — kapsam dışındakiler "örtüşen veda"nın gri yarısıdır.</param>
    /// <param name="scope">Bu işlemin kapsamı (dalgada amber'a yanan küme).</param>
    /// <summary>
    /// <see cref="Play"/>'in bekleyen biçimi: dönen Task koreografi BİTTİĞİNDE (ya da kesildiğinde) tamamlanır.
    /// Koşu komutunu bu Task'a bağlayan <c>RunViewModel</c>'dir — dizi böylece HER SEFERİNDE baştan sona oynar.
    /// Koreografi hiç oynamayacaksa (reduced-motion ya da boş kapsam) tamamlanmış bir Task döner: bekleme yok.
    /// </summary>
    public Task PlayAsync(IReadOnlyList<ProjectRowViewModel> allRows, IReadOnlyList<ProjectRowViewModel> scope)
    {
        Play(allRows, scope); // içerideki Cancel önceki bekleyeni zaten serbest bırakır
        if (!_player.IsPlaying) return Task.CompletedTask;
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _completion.Task;
    }

    public void Play(IReadOnlyList<ProjectRowViewModel> allRows, IReadOnlyList<ProjectRowViewModel> scope)
    {
        ArgumentNullException.ThrowIfNull(allRows);
        ArgumentNullException.ThrowIfNull(scope);

        Cancel(allRows);
        _runCount++;
        _scope = scope;

        if (scope.Count == 0 || !_animationsEnabled())
        {
            foreach (var row in scope) row.Marked = true;
            PushGraph();
            return;
        }

        int n = scope.Count;
        var order = MarkingChoreography.Order(n, _runCount);
        double stagger = MarkingChoreography.StaggerMs(n);

        var steps = new List<(double AtMs, Action Do)>();
        foreach (var step in MarkingChoreography.Steps)
            steps.Add((MarkingChoreography.StepAtMs(step, n), () => Enter(step, allRows)));
        // Dalga: kapsam RANDOM sırayla amber'a yanar. Her üye kendi gecikmesinde işaretlenir.
        for (int i = 0; i < n; i++)
        {
            var row = scope[i];
            steps.Add((MarkingChoreography.NeutralMs + order[i] * stagger, () => { row.Marked = true; PushGraph(); }));
        }

        _player.Play(steps, onDone: Settle);
    }

    /// <summary>
    /// Doğal bitiş: bekleyen koşu komutu serbest bırakılır ama adım (<see cref="MarkStep.Wait2"/>) ve grafa
    /// itilmiş opaklıklar <b>olduğu gibi KALIR</b> — ekran koşu başlayana dek vedanın son hâlinde bekler.
    /// Sıfırlama yalnız <see cref="Cancel"/>'dadır (koşu başladı ya da başlayamadı).
    /// </summary>
    private void Settle()
    {
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult();
    }

    /// <summary>Koreografiyi keser: adım <see cref="MarkStep.None"/>'a döner ve satırlar tam opaklığa çıkar.
    /// <b>İşaretlilik KORUNUR</b> — koşu başladığında amber kapsam sönmemelidir; onu statü kanalı devralır.
    /// Doğal bitişten sonra da çağrılır (<see cref="Settle"/>'ın tuttuğu adımı düşürür).</summary>
    public void Cancel(IReadOnlyList<ProjectRowViewModel> allRows)
    {
        ArgumentNullException.ThrowIfNull(allRows);
        _player.Stop();
        Finish(allRows);
    }

    /// <summary>Kapsam işaretini de siler — yeni bir işlemin <c>_neutralize</c>'ı ve koşu başlangıcı bunu yapar.</summary>
    public void ClearMarks(IReadOnlyList<ProjectRowViewModel> allRows)
    {
        ArgumentNullException.ThrowIfNull(allRows);
        _scope = [];
        foreach (var row in allRows) row.Marked = false;
        PushGraph();
    }

    /// <summary>Bekleyen <see cref="PlayAsync"/> çağrısının tamamlayıcısı; koreografi oynamiyorsa <c>null</c>.</summary>
    private TaskCompletionSource? _completion;

    /// <summary>
    /// [DEĞİŞEN KURAL — v1.13.2, ölçüm: "koşu zaten başlamış olduğu için listede ikinci bir sönme
    /// okunmuyordu"] Satırlara ARTIK koreografi opaklığı yazılmaz — her adımda <see cref="RowFade.None"/>
    /// (opaklık 1) yazılır. Eski kural: kapsam dışı satır <c>MarkingChoreography.RowEnvOpacity</c>'ye (eski
    /// değeri 0.3), kapsam içi satır vedanın sarı yarısında <see cref="MarkingChoreography.MarkedOpacity"/>'ye
    /// (0.45) sönerdi. Veda ve neon finali artık YALNIZ grafta yaşar — <see cref="PushGraph"/> yolu
    /// DEĞİŞMEDİ, graf hâlâ aynı opaklık zincirini (<see cref="MarkingChoreography.Opacity"/> +
    /// <see cref="MarkingChoreography.NodeEnvOpacity"/>) okur.
    /// </summary>
    private void Enter(MarkStep step, IReadOnlyList<ProjectRowViewModel> allRows)
    {
        Step = step;
        foreach (var row in allRows) row.Fade = RowFade.None;
        PushGraph();
    }

    private void Finish(IReadOnlyList<ProjectRowViewModel> allRows)
    {
        // Bekleyen serbest birakilir HER kosulda: kosu komutu buna baglidir ve koreografi kesilmis olsa da
        // (Stop, yeni islem) cagiran sonsuza dek beklememelidir.
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult();

        if (Step == MarkStep.None) return;
        Step = MarkStep.None;
        foreach (var row in allRows) row.Fade = RowFade.None;
        PushGraph();
    }

    private void PushGraph() =>
        PushToGraph?.Invoke(Step, new HashSet<string>(_scope.Where(r => r.Marked).Select(r => r.Name), StringComparer.Ordinal));
}
