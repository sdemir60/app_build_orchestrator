using System.Text;
using System.Threading.Channels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/A13.2] Canlı konsol için kilitsiz, satır-başına-Dispatcher-YASAK batching. IPC arka plan thread'i
/// <see cref="Post"/> ile satır yazar (asla bloklamaz, UI'a dokunmaz); <see cref="PumpAsync"/> enjekte
/// edilmiş <c>tick</c>'i (üretimde ~50ms <c>Task.Delay</c>, testte deterministik — D8) bekleyip kanalı
/// boşaltır ve o döngüde satır varsa TEK bir <c>flush(joinedText)</c> çağırır; kanal boşsa flush ÇAĞRILMAZ.
///
/// Join şekli: her <c>Post</c>'lanan satır, sonuna '\n' EKLENEREK (ayraç değil sonek) birleştirilir —
/// böylece hem tek satırlık hem boş batch'ler doğru davranır ve editöre eklenen metin her zaman tam
/// satırla biter.
///
/// UI-agnostiktir: <c>flush</c>'ı UI thread'ine taşımak (batch başına TEK <c>Dispatcher.InvokeAsync</c>)
/// çağıranın işidir (Task 12).
/// </summary>
public sealed class ConsoleBatcher
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<CancellationToken, Task> _tick;

    public ConsoleBatcher(Func<CancellationToken, Task> tick) => _tick = tick;

    /// <summary>IPC arka plan thread'inden çağrılır. Kilitsiz (Channel writer), asla bloklamaz, UI'a dokunmaz.</summary>
    public void Post(string line) => _channel.Writer.TryWrite(line);

    /// <summary>Kanalı tamamlar; pump bir sonraki tick'te kalan satırları boşaltıp döngüyü biter.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// tick → boşalt → (satır varsa) TEK flush döngüsü. Kanal <see cref="Complete"/> ile tamamlanıp
    /// tamamen boşaltılınca (<see cref="ChannelReader{T}.Completion"/> tamamlanınca) döngü biter.
    /// </summary>
    public async Task PumpAsync(Action<string> flush, CancellationToken ct)
    {
        var reader = _channel.Reader;
        while (true)
        {
            await _tick(ct).ConfigureAwait(false);

            StringBuilder? batch = null;
            while (reader.TryRead(out var line))
                (batch ??= new StringBuilder()).Append(line).Append('\n');

            if (batch is not null)
                flush(batch.ToString());

            if (reader.Completion.IsCompleted)
                break;
        }
    }
}
