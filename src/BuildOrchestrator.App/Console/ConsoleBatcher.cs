using System.Text;
using System.Threading.Channels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/A13.2 + 3b] Canlı konsol için kilitsiz, satır-başına-Dispatcher-YASAK batching. IPC arka plan thread'i
/// <see cref="Post"/> ile satır yazar (asla bloklamaz, UI'a dokunmaz); <see cref="PumpAsync"/> enjekte
/// edilmiş <c>tick</c>'i (üretimde ~50ms <c>Task.Delay</c>, testte deterministik — D8) bekleyip kanalı
/// boşaltır ve o döngüde satır varsa TEK bir <c>flush(joinedText)</c> çağırır; kanal boşsa flush ÇAĞRILMAZ.
///
/// Join şekli: her <c>Post</c>'lanan satır, sonuna '\n' EKLENEREK (ayraç değil sonek) birleştirilir —
/// böylece hem tek satırlık hem boş batch'ler doğru davranır ve editöre eklenen metin her zaman tam
/// satırla biter.
///
/// <para><b>Reseed tek-okuyucudan geçer [3b → D4/Solution B]:</b> mod değişiminde (run↔proje) taze doküman
/// artık TIKLAMA ANINDA UI thread'inde SENKRON kurulur (başlık ve gövde AYNI karede değişir — reseed flicker
/// kapanır). Kanala yazılan sentinel ise yalnız <b>uçuştaki bayat satırları atmak</b> için kalır: "pump satırı
/// TryRead'le çekti ama henüz flush etmedi" yarım-dequeue satırı, sentinel ile AYNI tek-okuyucu FIFO sırasında
/// ele alınır → sentinel'den önceki tüm satırlar (senkron kurulan snapshot'a zaten dahildir) atılır, sonrakiler
/// yeni dokümana akar. Üretimde <see cref="PostReseedDrop"/> (doküman-set yapmayan drop-only sentinel) kullanılır
/// (bkz. <c>RunViewModel.SeedRunDocument</c>/<c>SeedProjectDocument</c>); apply'lı <see cref="PostReseed"/>
/// varyantı (pump snapshot'ı kendisi kurar) korunur. Tek okuyucu YALNIZ pump olduğundan <c>SingleReader=true</c>
/// optimizasyonu geri açılır.</para>
///
/// UI-agnostiktir: hem <c>flush</c>'ı hem reseed <c>apply</c>'ını UI thread'ine taşımak (batch başına TEK
/// <c>Dispatcher.InvokeAsync</c>) çağıranın işidir (MainWindow/Task 12).
/// </summary>
public sealed class ConsoleBatcher
{
    /// <summary>Kanal birimi: ya bir log satırı ya da bir reseed komutu (snapshot metni + uygula-eylemi).</summary>
    private readonly record struct Op(string? Line, string? Snapshot, Action<string>? Apply, bool Reseed = false)
    {
        public bool IsReseed => Reseed || Apply is not null;
        public static Op ForLine(string line) => new(line, null, null);
        public static Op ForReseed(string snapshot, Action<string> apply) => new(null, snapshot, apply);
        /// <summary>[D4/Solution B] Doküman-set YAPMAYAN sentinel: yalnız uçuştaki (snapshot'a zaten dahil) bayat
        /// satırları atmak için. Doküman tıklama anında UI thread'inde SENKRON kurulduğundan pump'ın ayrıca
        /// set etmesine gerek yoktur (bkz. <see cref="PostReseedDrop"/>).</summary>
        public static Op ForReseedDrop() => new(null, null, null, Reseed: true);
    }

    // SingleReader=true: kanalı YALNIZ PumpAsync okur (reseed dahil her şey tek okuyucudan geçer). Yazıcılar
    // birden çok (IPC bg + UI reseed) → SingleWriter=false (varsayılan).
    private readonly Channel<Op> _channel = Channel.CreateUnbounded<Op>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<CancellationToken, Task> _tick;

    public ConsoleBatcher(Func<CancellationToken, Task> tick) => _tick = tick;

    /// <summary>IPC arka plan thread'inden çağrılır. Kilitsiz (Channel writer), asla bloklamaz, UI'a dokunmaz.</summary>
    public void Post(string line) => _channel.Writer.TryWrite(Op.ForLine(line));

    /// <summary>[3b] Mod değişiminde taze dokümanı tohumlamak için kanala bir reseed sentinel'i yazar. Pump bunu
    /// tek-okuyucu FIFO sırasında işler: sentinel'e KADAR biriken satırları (snapshot'ta zaten var) ATAR, sonra
    /// <paramref name="apply"/>'ı çağırır (çağıranın marshal ettiği doküman-set). Çağıran bunu VM'in <c>_gate</c>
    /// kilidi altında çağırmalıdır ki <paramref name="snapshot"/> metni ile sentinel'in kanaldaki konumu, aynı
    /// anda <c>Post</c> eden <c>OnProjectLog</c>'a göre atomik/tutarlı olsun (snapshot'taki her satır sentinel'den
    /// ÖNCE, sonrasındaki her satır snapshot DIŞINDA).</summary>
    public void PostReseed(string snapshot, Action<string> apply)
        => _channel.Writer.TryWrite(Op.ForReseed(snapshot, apply));

    /// <summary>[D4/Solution B] Mod değişiminde doküman TIKLAMA ANINDA UI thread'inde SENKRON kurulur (başlık ve
    /// gövde aynı karede değişir — reseed flicker kapanır); bu sentinel yalnız <b>uçuştaki bayat satırları
    /// atmak</b> için kalır. Pump bunu tek-okuyucu FIFO sırasında işler: sentinel'e KADAR biriken satırları
    /// (snapshot'ta zaten var, doküman senkron kurulduğunda dahil edilmiştir) ATAR; doküman-set YAPMAZ. Çağıran
    /// bunu VM'in <c>_gate</c> kilidi altında, senkron doküman-set'ten HEMEN ÖNCE çağırmalıdır ki snapshot metni
    /// ile sentinel'in kanaldaki konumu, aynı anda <c>Post</c> eden <c>OnProjectLog</c>'a göre atomik/tutarlı
    /// olsun (snapshot'taki her satır sentinel'den ÖNCE, sonrasındaki her satır snapshot DIŞINDA).</summary>
    public void PostReseedDrop() => _channel.Writer.TryWrite(Op.ForReseedDrop());

    /// <summary>Kanalı tamamlar; pump bir sonraki tick'te kalan satırları boşaltıp döngüyü biter.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// tick → boşalt → (satır varsa) TEK flush döngüsü. Kanaldaki bir reseed sentinel'i, o ana kadar biriken
    /// batch'i ATAR (snapshot'ta zaten dahildir) ve <c>apply(snapshot)</c> çağırır; reseed'den SONRAki satırlar
    /// yeni bir batch'te toplanır ve döngü sonunda flush edilir (Dispatcher sırası: önce doküman-set, sonra
    /// append — kopya yok). Kanal <see cref="Complete"/> ile tamamlanıp tamamen boşaltılınca döngü biter.
    /// </summary>
    public async Task PumpAsync(Action<string> flush, CancellationToken ct)
    {
        var reader = _channel.Reader;
        while (true)
        {
            await _tick(ct).ConfigureAwait(false);

            StringBuilder? batch = null;
            while (reader.TryRead(out var op))
            {
                if (op.IsReseed)
                {
                    batch = null;               // sentinel'den önceki satırlar snapshot'ta zaten var → at
                    // [D4/Solution B] Drop-only sentinel (Apply == null): doküman zaten senkron kuruldu — yalnız
                    // uçuştaki bayat satırları attık. Eski (apply'lı) reseed'de çağıran marshal eder + dokümanı
                    // taze snapshot'la kurar.
                    op.Apply?.Invoke(op.Snapshot!);
                }
                else
                {
                    (batch ??= new StringBuilder()).Append(op.Line).Append('\n');
                }
            }

            if (batch is not null)
                flush(batch.ToString());

            if (reader.Completion.IsCompleted)
                break;
        }
    }
}
