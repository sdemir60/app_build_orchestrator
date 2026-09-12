using System.Windows.Threading;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [clean · kullanıcı kararı 2026-09-12] Bir işlem dizisinin ADIMLARI arasındaki bekletme —
/// <c>RunViewModel.OperationHold</c>'un üretim karşılığı. Diziyi VM bilir, zamanı burası sayar
/// (<see cref="OperationChoreographer"/> ile AYNI bölüşüm).
///
/// <para><b>Neden <see cref="DispatcherTimer"/>:</b> bu bir UI temposudur ve UI thread'inde sayılmalıdır —
/// <see cref="Controls.StepPlayer"/>'ın gerekçesinin aynısı. Üretimde <c>Task.Delay</c> ayrıca YASAKTIR
/// (D8 guard'ı kaynak ağacını tarar).</para>
///
/// <para><b>Azaltılmış hareket:</b> hiç beklenmez (§1.3 "tüm süreler 0") — adım anında biter ve dizi kesintisiz
/// akar. Sinyal CANLI okunur, kurulumda dondurulmaz.</para>
///
/// <para>Üst üste gelen bir bekletme öncekini ASILI BIRAKMAZ: yeni istek eskisini tamamlar. Bir bekleyen
/// <c>await</c>'in sonsuza kalması, kullanıcı için düğmenin kalıcı kilitlenmesi demek olurdu.</para>
/// </summary>
public sealed class StepHold
{
    private readonly Func<bool> _animationsEnabled;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private TaskCompletionSource? _pending;

    public StepHold(Func<bool> animationsEnabled)
    {
        _animationsEnabled = animationsEnabled ?? throw new ArgumentNullException(nameof(animationsEnabled));
        _timer.Tick += (_, _) => Complete();
    }

    /// <summary>[test yüzeyi] Şu an bir bekletme sürüyor mu.</summary>
    public bool IsHolding => _timer.IsEnabled;

    /// <summary>
    /// <paramref name="ms"/> kadar bekler. Süre pozitif değilse ya da animasyonlar kapalıysa TAMAMLANMIŞ bir
    /// Task döner: çağıran dizisini kesintisiz sürdürür.
    /// </summary>
    public Task HoldAsync(double ms)
    {
        Complete(); // bekleyen varsa serbest bırak — asılı await yok
        if (ms <= 0 || !_animationsEnabled()) return Task.CompletedTask;

        _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _timer.Interval = TimeSpan.FromMilliseconds(ms);
        _timer.Start();
        return _pending.Task;
    }

    private void Complete()
    {
        _timer.Stop();
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult();
    }
}
