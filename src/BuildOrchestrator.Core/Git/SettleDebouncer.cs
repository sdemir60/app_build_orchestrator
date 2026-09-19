namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1] Sessizlik penceresi: her <see cref="Poke"/> bekleyişi baştan kurar; son
/// dürtmeden sonra <c>window</c> kadar sessizlik olunca <c>onSettled</c> TEK kez çağrılır. Git'in bir işlemde
/// yaptığı ardışık yazımlar (checkout + commit'ler, rebase'in adımları) böylece tek tetiğe iner.
///
/// <para>Saat enjekte edilir (<c>delay</c>): testte elle tamamlanan bir görev, üretimde gerçek bekleme
/// (<see cref="HeadWatcher"/>'ın varsayılanı). Geri çağrı bekleyişi tamamlayan thread'de koşar (üretimde
/// thread-pool) — çağıran UI'a kendisi taşır.</para>
/// </summary>
public sealed class SettleDebouncer : IDisposable
{
    private readonly TimeSpan _window;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action _onSettled;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _disposed;

    public SettleDebouncer(TimeSpan window, Func<TimeSpan, CancellationToken, Task> delay, Action onSettled)
    {
        _window = window;
        _delay = delay;
        _onSettled = onSettled;
    }

    /// <summary>Bir yazım görüldü: bekleyen pencere iptal edilir, yenisi kurulur.</summary>
    public void Poke()
    {
        CancellationTokenSource next;
        lock (_gate)
        {
            if (_disposed) return;
            CancelPending();
            next = _pending = new CancellationTokenSource();
        }
        _ = SettleAsync(next);
    }

    private async Task SettleAsync(CancellationTokenSource cts)
    {
        try
        {
            await _delay(_window, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // daha yeni bir yazım pencereyi yeniden kurdu (ya da izleyici kapandı)
        }

        lock (_gate)
        {
            // Bekleyiş tamamlanırken yeni bir dürtme geldiyse bu pencere artık onun değildir.
            if (_disposed || !ReferenceEquals(_pending, cts) || cts.IsCancellationRequested) return;
            _pending = null;
        }
        _onSettled();
    }

    private void CancelPending()
    {
        if (_pending is null) return;
        // Dispose EDİLMEZ: bekleyen SettleAsync jetonu hâlâ okuyabilir; zamanlayıcısız bir CTS'nin bırakacağı kaynak yok.
        _pending.Cancel();
        _pending = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CancelPending();
        }
    }
}
