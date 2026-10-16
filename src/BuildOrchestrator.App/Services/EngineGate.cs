namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 · T21 OOS-1 · karar 34 · B1 fix 3] Motor kapısında bekleyen isteklerin kaydı — kapının DURUMU; WPF'siz ve saf
/// (<see cref="EngineWakeRule"/> ile aynı yerleşim). Kapının kuralı VM'dedir (<c>RunViewModel.EnsureEngineAwakeAsync</c>); burada yalnız kayıt ve
/// boşalma durur, değişmezi tek yerde:
/// <list type="bullet">
/// <item><see cref="Enter"/> beklemeye başlayan isteği kaydeder (bilet + komut türü) ve biletini döner; <see cref="Leave"/> bileti siler —
/// tekrarlanabilir, bilinmeyen ya da zaten silinmiş bilet hiçbir şey yapmaz (istek motora teslim edilirken ve geçişin dispose'unda ikisi de çağrılır).
/// Bir türün penceresi aynı anda tek isteğindir — VM'in kapıları ikincisini açmaz —; kayıt yine de biletle tutulur, böylece her geçiş yalnız kendi
/// kaydını bırakır.</item>
/// <item><b>Değişmez:</b> <see cref="Count"/> 0 iken <see cref="Drained"/> tamamlanmıştır; <see cref="Count"/> &gt; 0 iken <see cref="Drained"/>
/// güncel, tamamlanmamış görevdir — boşalmadan sonraki ilk <see cref="Enter"/> onu YENİDEN kurar (eski, tamamlanmış görev geri gelmez).</item>
/// <item>Boşalmanın devamları asenkron koşar (<see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>): son kaydı silen <see cref="Leave"/>
/// hiçbir devamı satır içi koşturmaz. Kesilmiş koşu kurtaran uyanmanın tazelemesi boşalmayı bekler; son teslimin gönderimi, kaydın bırakılmasıyla
/// aynı senkron adımda, ondan ÖNCE gider.</item>
/// </list>
/// Tek kullanıcısı UI thread'idir — kilitsizdir.
/// </summary>
internal sealed class EngineGate
{
    private readonly List<(int Ticket, Type Command)> _waiting = [];
    private int _lastTicket;
    private TaskCompletionSource? _drained;

    /// <summary>Kapıda beklemiş, motora henüz teslim edilmemiş isteklerin sayısı.</summary>
    public int Count => _waiting.Count;

    /// <summary>Kapıda bekleyen her istek teslim edildiğinde ya da geçişini bıraktığında tamamlanır; kapı boşken tamamlanmıştır.</summary>
    public Task Drained => _drained?.Task ?? Task.CompletedTask;

    /// <summary>Kapıda beklemeye başlayan isteği kaydeder; biletini döner. Kapı boşken gelen ilk kayıt boşalmayı yeniden kurar.</summary>
    /// <param name="command">Bekleyen isteğin komut türü.</param>
    public int Enter(Type command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_waiting.Count == 0) _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int ticket = ++_lastTicket;
        _waiting.Add((ticket, command));
        return ticket;
    }

    /// <summary>Bileti kayıttan siler — yoksa hiçbir şey yapmaz. Son kaydı silen boşalmayı tamamlar.</summary>
    /// <param name="ticket"><see cref="Enter"/>'in döndüğü bilet.</param>
    public void Leave(int ticket)
    {
        int index = _waiting.FindIndex(entry => entry.Ticket == ticket);
        if (index < 0) return;
        _waiting.RemoveAt(index);
        if (_waiting.Count == 0) _drained?.TrySetResult();
    }

    /// <summary>Bu türde bir istek kapıda bekliyor mu — beklemiş ve motora henüz teslim edilmemiş.</summary>
    /// <param name="command">Komut türü.</param>
    public bool Holds(Type command) => _waiting.Exists(entry => entry.Command == command);
}
