using System.Windows.Threading;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.11.0 §9-4/§9-5] Zamanlanmış adımları <b>TEK</b> bir <see cref="DispatcherTimer"/> ile oynatır.
///
/// <para><b>Neden tek timer:</b> koreografiler yedi-sekiz adımdan ve (dalgada) kapsam kadar ek uyanıştan
/// oluşur; her biri için ayrı bir timer açmak 36 projede 40+ canlı saat demekti. Oynatıcı adımları zamana
/// göre sıralar, aralarındaki FARKI interval yapar ve her tick'te bir sonrakine geçer.</para>
///
/// <para><b>Gerçek zamanda koşar</b> — prototipin kararı da budur (build-data.js:357): koreografi sim
/// saatine bağlandığında arka plan sekmede asılı kalıyordu. Burada da motorun olay akışına DEĞİL duvar
/// saatine bağlıdır: koreografi motor planlama yaparken oynar.</para>
///
/// <para><see cref="Stop"/> bekleyen tüm adımları iptal eder (prototipin <c>_clearMarkTimers</c>/
/// <c>_clearEndTimers</c>'ı) — yeni bir işlem koreografiyi ANINDA keser.</para>
/// </summary>
public sealed class StepPlayer
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private readonly List<(double AtMs, Action Do)> _steps = [];
    private int _next;
    private double _playedMs;
    private Action? _onDone;

    public StepPlayer() => _timer.Tick += OnTick;

    /// <summary>Şu an bir koreografi oynuyor mu.</summary>
    public bool IsPlaying => _timer.IsEnabled;

    /// <summary>Adımları oynatır. <paramref name="steps"/> zamana göre sıralanır; aynı ana düşen adımlar
    /// verildikleri sırayla koşar. <paramref name="onDone"/> son adımdan sonra çağrılır.</summary>
    public void Play(IEnumerable<(double AtMs, Action Do)> steps, Action? onDone = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Stop();
        _steps.AddRange(steps.OrderBy(s => s.AtMs));
        _onDone = onDone;
        if (_steps.Count == 0) { onDone?.Invoke(); return; }
        Schedule();
    }

    /// <summary>Bekleyen adımları iptal eder. <c>onDone</c> ÇAĞRILMAZ — durdurmak bitirmek değildir.</summary>
    public void Stop()
    {
        _timer.Stop();
        _steps.Clear();
        _next = 0;
        _playedMs = 0;
        _onDone = null;
    }

    private void Schedule()
    {
        double delta = Math.Max(0, _steps[_next].AtMs - _playedMs);
        // Sıfır aralıklı bir DispatcherTimer hiç tick etmez → en küçük anlamlı adıma yuvarla.
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delta));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        // Aynı ana düşen tüm adımları TEK tick'te koştur (dalgada iki node aynı milisaniyeye düşebilir).
        double now = _steps[_next].AtMs;
        while (_next < _steps.Count && _steps[_next].AtMs <= now) _steps[_next++].Do();
        _playedMs = now;

        if (_next < _steps.Count) { Schedule(); return; }

        var done = _onDone;
        Stop();
        done?.Invoke();
    }
}
