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
/// <para><b>[BİLİNÇLİ SAPMA — üretim kararı]</b> Prototipte koşu koreografinin SONUNDA başlar (motor
/// simüledir, beklemenin bedeli yoktur). Burada koreografi motorun PLANLAMA penceresiyle (<c>Starting</c>
/// fazı: worktree hazırlığı → tarama → graf → topoloji → incremental) ÖRTÜŞÜR: komut anında gönderilir,
/// koreografi onun üzerinde oynar. Gerekçe: gerçek bir derlemeyi 3 saniye geciktirmek bir animasyon için
/// savunulabilir değildir ve planlama zaten saniyeler sürer — görsel sıra korunur, maliyeti sıfırdır.
/// Koreografi <c>runStarted</c> geldiğinde biter (<see cref="Cancel"/>).</para>
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

        _player.Play(steps, onDone: () => Finish(allRows));
    }

    /// <summary>Koreografiyi keser: adım <see cref="MarkStep.None"/>'a döner ve satırlar tam opaklığa çıkar.
    /// <b>İşaretlilik KORUNUR</b> — koşu başladığında amber kapsam sönmemelidir; onu statü kanalı devralır.</summary>
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

    private void Enter(MarkStep step, IReadOnlyList<ProjectRowViewModel> allRows)
    {
        Step = step;
        foreach (var row in allRows)
        {
            bool marked = row.Marked;
            row.Fade = new RowFade(
                MarkingChoreography.Opacity(step, marked, MarkingChoreography.RowEnvOpacity),
                MarkingChoreography.GlideMs(step, marked));
        }
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
