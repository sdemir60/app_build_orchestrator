using System.Text;
using System.Threading;
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

    // [D4 review §1] Monoton reseed-generation guard. Her reseed (PostReseed/PostReseedDrop) bunu SENTINEL'i
    // yazmadan ÖNCE artırır; pump her batch'i okunduğu nesille damgalar; MainWindow.AppendConsoleBatch, nesli
    // güncel <see cref="CurrentReseedGen"/>'in GERİSİNDE kalan (aradan bir reseed geçmiş → bayat) batch'leri atar.
    // Solution B'nin senkron doküman-set'i, bg-pump'ın reseed'den HEMEN ÖNCE drenajlayıp reseed'den SONRA
    // koşan bir bayat flush'ının taze dokümana sızması penceresini (T3b'de pump-routed REPLACE'in kapadığı)
    // yeniden açıyordu; generation guard onu KESİN kapatır (reseed UI thread'inde senkron bump'ladığından,
    // kuyruğa alınan bayat flush çalıştığında CurrentReseedGen zaten yeni).
    private long _reseedGen;

    /// <summary>[D4 review §1] Şu anki reseed nesli (monoton artan). MainWindow bunu her flush'ın taşıdığı
    /// nesille kıyaslar: <c>batchGen &lt; CurrentReseedGen</c> ⇒ aradan bir reseed geçti ⇒ bayat batch, atılır.</summary>
    public long CurrentReseedGen => Volatile.Read(ref _reseedGen);

    public ConsoleBatcher(Func<CancellationToken, Task> tick) => _tick = tick;

    /// <summary>IPC arka plan thread'inden çağrılır. Kilitsiz (Channel writer), asla bloklamaz, UI'a dokunmaz.</summary>
    public void Post(string line) => _channel.Writer.TryWrite(Op.ForLine(line));

    /// <summary>[3b — <b>YALNIZ TEST</b>, D4 review §4] Apply'lı (pump-routed REPLACE) reseed varyantı: pump
    /// sentinel'e KADAR biriken satırları (snapshot'ta zaten var) ATAR, sonra <paramref name="apply"/>'ı çağırır
    /// (çağıranın marshal ettiği doküman-set). <b>Üretimde ARTIK ÇAĞRILMAZ</b> — D4/Solution B'den beri her iki
    /// Seed* de <see cref="PostReseedDrop"/> (senkron doküman-set + drop-only sentinel) kullanır; bu apply-path
    /// yalnız eski <c>ConsoleBatcherTests</c>'in yeşil kalması için korunur. Bir okuyucu üretimin hâlâ pump-routed
    /// REPLACE kullandığını SANMAMALI. Nesil (§1) simetri için burada da ilerletilir.</summary>
    public void PostReseed(string snapshot, Action<string> apply)
    {
        Interlocked.Increment(ref _reseedGen); // [D4 review §1/§4] simetri: apply'lı reseed de nesli ilerletir
        _channel.Writer.TryWrite(Op.ForReseed(snapshot, apply));
    }

    /// <summary>[D4/Solution B] Mod değişiminde doküman TIKLAMA ANINDA UI thread'inde SENKRON kurulur (başlık ve
    /// gövde aynı karede değişir — reseed flicker kapanır); bu sentinel yalnız <b>uçuştaki bayat satırları
    /// atmak</b> için kalır. Pump bunu tek-okuyucu FIFO sırasında işler: sentinel'e KADAR biriken satırları
    /// (snapshot'ta zaten var, doküman senkron kurulduğunda dahil edilmiştir) ATAR; doküman-set YAPMAZ. Çağıran
    /// bunu VM'in <c>_gate</c> kilidi altında, senkron doküman-set'ten HEMEN ÖNCE çağırmalıdır ki snapshot metni
    /// ile sentinel'in kanaldaki konumu, aynı anda <c>Post</c> eden <c>OnProjectLog</c>'a göre atomik/tutarlı
    /// olsun (snapshot'taki her satır sentinel'den ÖNCE, sonrasındaki her satır snapshot DIŞINDA).</summary>
    public void PostReseedDrop()
    {
        // [D4 review §1] Nesli SENTINEL'den ÖNCE ilerlet: pump sentinel'i işlerken _reseedGen'i okuduğunda zaten
        // yeni değeri görür; reseed'den ÖNCE drenajlanıp SONRA koşan bir bayat flush ise batchGen=eski taşır ve
        // MainWindow.AppendConsoleBatch'te CurrentReseedGen'in gerisinde kaldığından atılır (kesin partition).
        Interlocked.Increment(ref _reseedGen);
        _channel.Writer.TryWrite(Op.ForReseedDrop());
    }

    /// <summary>Kanalı tamamlar; pump bir sonraki tick'te kalan satırları boşaltıp döngüyü biter.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// tick → boşalt → (satır varsa) TEK flush döngüsü. Kanaldaki bir reseed sentinel'i, o ana kadar biriken
    /// batch'i ATAR (snapshot'ta zaten dahildir) ve <c>apply(snapshot)</c> çağırır; reseed'den SONRAki satırlar
    /// yeni bir batch'te toplanır ve döngü sonunda flush edilir (Dispatcher sırası: önce doküman-set, sonra
    /// append — kopya yok). Kanal <see cref="Complete"/> ile tamamlanıp tamamen boşaltılınca döngü biter.
    /// </summary>
    public async Task PumpAsync(Action<string, long> flush, CancellationToken ct)
    {
        var reader = _channel.Reader;
        while (true)
        {
            await _tick(ct).ConfigureAwait(false);

            // [D4 review §1] Batch'i okunduğu nesille damgala: drenaja başlarken güncel nesli al; bir reseed
            // sentinel'ine uğrayınca yeniden oku (sentinel'den SONRAki satırlar yeni nesle aittir — bump
            // sentinel'den önce yapıldığından pump sentinel'i okurken _reseedGen zaten yeni değerdedir).
            long batchGen = Volatile.Read(ref _reseedGen);
            StringBuilder? batch = null;
            while (reader.TryRead(out var op))
            {
                if (op.IsReseed)
                {
                    batch = null;               // sentinel'den önceki satırlar snapshot'ta zaten var → at
                    batchGen = Volatile.Read(ref _reseedGen); // sonraki satırlar YENİ nesle ait
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
                flush(batch.ToString(), batchGen);

            if (reader.Completion.IsCompleted)
                break;
        }
    }
}
