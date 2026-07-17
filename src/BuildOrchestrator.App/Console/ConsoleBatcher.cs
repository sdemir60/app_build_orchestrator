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
    // [Fix wave 1, Finding 3] SingleReader ARTIK false: DiscardPending, PumpAsync'in kendi arka plan
    // okuyucusuyla EŞZAMANLI çağrılabilir (mod değişiminde reseed'den hemen önce, RunViewModel'in _gate
    // kilidi altında) — true iken bu, kanalın iç durumunu bozma riski taşırdı (yalnız tek okuyucu varsayımı
    // altında optimize edilir). Hacim düşük (satır değil, tick başına en fazla birkaç Dispose/Discard) —
    // performans farkı ölçülemez.
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = false });
    private readonly Func<CancellationToken, Task> _tick;

    public ConsoleBatcher(Func<CancellationToken, Task> tick) => _tick = tick;

    /// <summary>IPC arka plan thread'inden çağrılır. Kilitsiz (Channel writer), asla bloklamaz, UI'a dokunmaz.</summary>
    public void Post(string line) => _channel.Writer.TryWrite(line);

    /// <summary>Kanalı tamamlar; pump bir sonraki tick'te kalan satırları boşaltıp döngüyü biter.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>[Fix wave 1, Finding 3] Kanaldaki BEKLEYEN (henüz bu tick'e uğramamış) satırları senkron
    /// olarak boşaltır ve atar — <c>flush</c> ÇAĞRILMAZ. Mod değişiminde (proje/run geçişi) VM'in taze
    /// dokümanı tohumlamasından HEMEN ÖNCE, aynı satırların pump'ın bir SONRAKİ tick'inde yeni dokümana
    /// TEKRAR eklenmesini (kopya) önlemek için çağrılır. Çağıranın sorumluluğu: bunu VM'in kendi
    /// senkronizasyon noktasıyla (_gate) sarmalamak — bkz. <c>RunViewModel.SeedRunDocument</c>/
    /// <c>SeedProjectDocument</c>.</summary>
    public void DiscardPending()
    {
        while (_channel.Reader.TryRead(out _)) { }
    }

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
