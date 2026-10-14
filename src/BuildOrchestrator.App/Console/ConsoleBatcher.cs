using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/A13.2 + 3b] Canlı konsol için kilitsiz, satır-başına-Dispatcher-YASAK batching. IPC arka plan thread'i
/// <see cref="Post"/> ile satır yazar (asla bloklamaz, UI'a dokunmaz); <see cref="PumpAsync"/> enjekte
/// edilmiş <c>tick</c>'i (üretimde <see cref="Batching"/>: satır gelene dek bekler, sonra ~50ms biriktirir; testte
/// deterministik — D8) bekleyip kanalı
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
    /// <summary>Kanal birimi: ya bir log satırı ya da bir reseed sentinel'i (snapshot metni + uygula-eylemi; drop-only'de ikisi de
    /// yok). Sentinel ilerlettiği nesli taşır (<see cref="Generation"/>: ilk reseed 1'dir, satırda 0) — pompa batch'i onunla
    /// damgalar (bkz. <see cref="PumpAsync"/>).</summary>
    private readonly record struct Op(string? Line, string? Snapshot, Action<string>? Apply, long Generation)
    {
        public bool IsReseed => Generation > 0;
        public static Op ForLine(string line) => new(line, null, null, 0);
        public static Op ForReseed(string snapshot, Action<string> apply, long generation) => new(null, snapshot, apply, generation);
        /// <summary>[D4/Solution B] Doküman-set YAPMAYAN sentinel: yalnız uçuştaki (snapshot'a zaten dahil) bayat
        /// satırları atmak için. Doküman tıklama anında UI thread'inde SENKRON kurulduğundan pump'ın ayrıca
        /// set etmesine gerek yoktur (bkz. <see cref="PostReseedDrop"/>).</summary>
        public static Op ForReseedDrop(long generation) => new(null, null, null, generation);
    }

    // SingleReader=true: kanalı YALNIZ PumpAsync okur (reseed dahil her şey tek okuyucudan geçer). Yazıcılar
    // birden çok (IPC bg + UI reseed) → SingleWriter=false (varsayılan).
    private readonly Channel<Op> _channel = Channel.CreateUnbounded<Op>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<CancellationToken, Task> _tick;

    // [D4 review §1] Monoton reseed-generation guard. Her reseed (PostReseed/PostReseedDrop) bunu SENTINEL'i
    // yazmadan ÖNCE artırır ve yeni değeri sentinel'e yazar; pump her batch'i TÜKETTİĞİ SON sentinel'in nesliyle damgalar
    // (_consumedReseedGen); MainWindow.AppendConsoleBatch, nesli güncel <see cref="CurrentReseedGen"/>'in GERİSİNDE kalan
    // (aradan bir reseed geçmiş → bayat) batch'leri atar. Solution B'nin senkron doküman-set'i, bg-pump'ın reseed'den HEMEN
    // ÖNCE drenajlayıp reseed'den SONRA koşan bir bayat flush'ının taze dokümana sızması penceresini (T3b'de pump-routed
    // REPLACE'in kapadığı) yeniden açıyordu; generation guard onu KESİN kapatır (reseed UI thread'inde senkron bump'ladığından,
    // kuyruğa alınan bayat flush çalıştığında CurrentReseedGen zaten yeni).
    private long _reseedGen;

    // [idle-memory · final review 95] Pompanın tükettiği son sentinel'in nesli — YALNIZ pompa okur/yazar (tek okuyucu); pompa
    // yeniden başlarsa kaldığı nesilden sürer. Damga güncel nesilden (_reseedGen) okunmaz: reseed nesli sentinel'i yazmadan ÖNCE
    // ilerletir ve ikisinin arasında boşaltan pompa, sentinel'den ÖNCEki (tohumdaki ya da temizlikte silinmiş) satırları yeni
    // nesille damgalardı — kabuk onları atmaz, taze belgeye ikinci kez basardı.
    private long _consumedReseedGen;

    /// <summary>[D4 review §1] Şu anki reseed nesli (monoton artan). MainWindow bunu her flush'ın taşıdığı
    /// nesille kıyaslar: <c>batchGen &lt; CurrentReseedGen</c> ⇒ aradan bir reseed geçti ⇒ bayat batch, atılır.</summary>
    public long CurrentReseedGen => Volatile.Read(ref _reseedGen);

    public ConsoleBatcher(Func<CancellationToken, Task> tick) => _tick = tick;

    private ConsoleBatcher(Func<CancellationToken, Task> window, bool waitForLine) =>
        _tick = waitForLine ? ct => WaitForLineThenAsync(window, ct) : window;

    /// <summary>
    /// Üretim kurulumu: tick önce kanala bir satır (ya da reseed sentinel'i / tamamlanma) gelmesini BEKLER, sonra
    /// <paramref name="window"/> kadar biriktirir. Tick sözleşmesi aynıdır (tick → boşalt → varsa tek flush);
    /// değişen yalnız boşta ne olduğudur.
    ///
    /// <para><b>Neden:</b> koşulsuz bir <c>Task.Delay(50)</c> tick'i pompayı uygulama ömrü boyunca saniyede 20 kez
    /// uyandırıyordu — konsola hiç satır gelmezken ve pencere tepsideyken de. Tepside boşta alınan CPU profilinde
    /// zamanlayıcı thread'i + thread pool bu yüzden UI thread kadar CPU harcıyordu. Akış sürerken davranış
    /// aynıdır; bir sessizlikten sonraki ilk satır pencerenin sonunda (en geç <paramref name="window"/>) basılır —
    /// eskiden de en geç o kadardı.</para>
    /// </summary>
    public static ConsoleBatcher Batching(Func<CancellationToken, Task> window) => new(window, waitForLine: true);

    private async Task WaitForLineThenAsync(Func<CancellationToken, Task> window, CancellationToken ct)
    {
        // false = kanal tamamlandı: pencereyi yine de aç ki pompa kalanları boşaltıp döngüyü bitirsin.
        await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
        await window(ct).ConfigureAwait(false);
    }

    /// <summary>IPC arka plan thread'inden çağrılır. Kilitsiz (Channel writer), asla bloklamaz, UI'a dokunmaz.</summary>
    public void Post(string line) => _channel.Writer.TryWrite(Op.ForLine(line));

    /// <summary>[3b — <b>YALNIZ TEST</b>, D4 review §4] Apply'lı (pump-routed REPLACE) reseed varyantı: pump
    /// sentinel'e KADAR biriken satırları (snapshot'ta zaten var) ATAR, sonra <paramref name="apply"/>'ı çağırır
    /// (çağıranın marshal ettiği doküman-set). <b>Üretimde ARTIK ÇAĞRILMAZ</b> — D4/Solution B'den beri her iki
    /// Seed* de <see cref="PostReseedDrop"/> (senkron doküman-set + drop-only sentinel) kullanır; bu apply-path
    /// yalnız eski <c>ConsoleBatcherTests</c>'in yeşil kalması için korunur. Bir okuyucu üretimin hâlâ pump-routed
    /// REPLACE kullandığını SANMAMALI. Nesil (§1) simetri için burada da ilerletilir.</summary>
    public void PostReseed(string snapshot, Action<string> apply) =>
        PostSentinel(generation => Op.ForReseed(snapshot, apply, generation)); // [D4 review §1/§4] simetri: o da nesli ilerletir

    /// <summary>[D4/Solution B] Mod değişiminde doküman TIKLAMA ANINDA UI thread'inde SENKRON kurulur (başlık ve
    /// gövde aynı karede değişir — reseed flicker kapanır); bu sentinel yalnız <b>uçuştaki bayat satırları
    /// atmak</b> için kalır. Pump bunu tek-okuyucu FIFO sırasında işler: sentinel'e KADAR biriken satırları
    /// (snapshot'ta zaten var, doküman senkron kurulduğunda dahil edilmiştir) ATAR; doküman-set YAPMAZ. Çağıran
    /// bunu VM'in <c>_gate</c> kilidi altında, senkron doküman-set'ten HEMEN ÖNCE çağırmalıdır ki snapshot metni
    /// ile sentinel'in kanaldaki konumu, aynı anda <c>Post</c> eden <c>OnProjectLog</c>'a göre atomik/tutarlı
    /// olsun (snapshot'taki her satır sentinel'den ÖNCE, sonrasındaki her satır snapshot DIŞINDA).</summary>
    public void PostReseedDrop() => PostSentinel(Op.ForReseedDrop);

    /// <summary>
    /// İki reseed'in TEK gövdesi: nesli SENTINEL'den ÖNCE ilerletir ve yeni değeri sentinel'e yazar [D4 review §1]. İlerleme önce
    /// gelir: senkron doküman-set'ten hemen önce bump'lanan nesil, reseed'den ÖNCE drenajlanıp SONRA koşan bayat bir flush'ı
    /// <c>MainWindow.AppendConsoleBatch</c>'te <see cref="CurrentReseedGen"/>'in gerisinde bırakır (kesin partition).
    /// <para>[idle-memory · final review 95] Pompa damgayı <see cref="CurrentReseedGen"/>'den değil sentinel'den alır
    /// (<see cref="PumpAsync"/>): bump ile yazım arasında boşaltan pompa sentinel'den önceki satırları eski nesille damgalar.</para>
    /// </summary>
    private void PostSentinel(Func<long, Op> sentinel)
    {
        long generation = Interlocked.Increment(ref _reseedGen);
        AfterReseedGenerationBump?.Invoke();
        _channel.Writer.TryWrite(sentinel(generation));
    }

    /// <summary>[test yüzeyi] Nesil ilerletildikten SONRA, sentinel kanala yazılmadan ÖNCE çağrılır — üretimde <c>null</c>. Testler
    /// pompanın tam bu aralıkta boşalttığı yarışı onunla deterministik kurar (final review 95).</summary>
    internal Action? AfterReseedGenerationBump { get; set; }

    /// <summary>Kanalı tamamlar; pump bir sonraki tick'te kalan satırları boşaltıp döngüyü biter.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// tick → boşalt → (satır varsa) TEK flush döngüsü. Kanaldaki bir reseed sentinel'i, o ana kadar biriken
    /// batch'i ATAR (snapshot'ta zaten dahildir) ve <c>apply(snapshot)</c> çağırır; reseed'den SONRAki satırlar
    /// yeni bir batch'te toplanır ve döngü sonunda flush edilir (Dispatcher sırası: önce doküman-set, sonra
    /// append — kopya yok). Kanal <see cref="Complete"/> ile tamamlanıp tamamen boşaltılınca döngü biter.
    /// <para><b>Damga:</b> batch, pompanın kendinden ÖNCE tükettiği son sentinel'in nesliyle flush edilir — kanal sırasında
    /// satırın ait olduğu nesil. [idle-memory · final review 95 — DEĞİŞEN KURAL] Eskiden drenajın BAŞINDA güncel nesil
    /// (<see cref="CurrentReseedGen"/>) okunuyordu: reseed nesli sentinel'i yazmadan önce ilerlettiği için ikisinin arasında başlayan
    /// bir drenaj sentinel'den ÖNCEki satırları yeni nesille damgalıyor ve kabuk onları taze belgeye ikinci kez basıyordu (işlem
    /// temizliğinde: önceki işlemin satırı yeni işlemin konsoluna).</para>
    /// </summary>
    public async Task PumpAsync(Action<string, long> flush, CancellationToken ct)
    {
        var reader = _channel.Reader;
        while (true)
        {
            await _tick(ct).ConfigureAwait(false);

            long batchGen = _consumedReseedGen; // son tüketilen sentinel'in nesli (yukarıdaki damga kuralı)
            StringBuilder? batch = null;
            while (reader.TryRead(out var op))
            {
                if (op.IsReseed)
                {
                    batch = null;               // sentinel'den önceki satırlar snapshot'ta zaten var → at
                    batchGen = _consumedReseedGen = op.Generation; // sonraki satırlar sentinel'in nesline ait
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
