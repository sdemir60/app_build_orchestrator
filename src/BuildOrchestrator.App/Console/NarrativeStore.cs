using System.IO;
using System.Text;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] Koşu anlatısının TEK sahibi. VM her satırı buraya ekler (IPC thread'i), konsol görünümü
/// dilim ister (UI thread'i); görünüm kendi kopyasını tutmaz (eski <c>_runText</c> + <c>_backlogLines</c> çiftinin ölçülen bedeli:
/// 83 k satırlık koşuda 55 + 59 MB). Satırlar '\n' içermez; <see cref="Join"/> '\n' sonekli metin üretir (append/dikiş sözleşmesi).
///
/// <para><b>Bir giriş = belgede bir satır.</b> Görünüm belgenin ilk satırını store indeksiyle izler; canlı akışta kırpılan
/// belge satırlarını o indekse ekler. Bu yüzden çok satırlı bir ekleme (çok satırlı hata mesajı, git stderr) belgenin
/// göreceği satırlara bölünür — kural <see cref="ConsoleLines.Split"/>'tedir (AvalonEdit'in ayraç kuralı).</para>
///
/// <para><b>[idle-memory · dalga 2 · spec §6.3] Diske yayılır.</b> Koşu başlayınca VM store'u koşu klasöründeki dosyaya bağlar
/// (<see cref="BeginSpool"/>): o ana dek biriken satırlar önce yazılır, sonra her satır tamponlu yazıcıya eklenir (BOM'suz UTF-8,
/// '\n' sonekli) ve başlangıç baytı ofset indeksinde tutulur (satır başına 8 B). Bellekte en çok <see cref="MemoryWindowLines"/>
/// satır kalır: pencere dolunca en eski blok — önce diske boşaltılıp — bellekten atılır ve o satırlar istenince dosyadan okunur.
/// <see cref="Count"/> her zaman TOPLAM satır sayısıdır; görünümün indeksleri spool'dan habersiz yürür. Spool yoksa — koşu klasörü
/// olmayan bölüm: Sync, bakım kutusunun Deep Clean'i, Optimize ya da klasörü bilinmeyen bir koşu (Build menüsünün Clean'i bir koşudur,
/// klasörü vardır) — ya da dosya açılamaz/yazılamazsa store bellekte kalır — spool bir optimizasyondur, kaybı doğruluğu bozmaz. Bir
/// yazım hatasından sonra o ana dek tahliye edilmiş satırlar dosyadan okunmaya devam eder, yenileri bellekte kalır. Bir bölüm (iki
/// <see cref="Clear"/> arası) en çok bir spool açar ve en çok bir uyarı bırakır (<see cref="TakeWarning"/>).</para>
///
/// <para><b>Kilit:</b> her erişim iç kilidi alır. VM ekleme yaparken kendi <c>_gate</c>'ini tutar (ekleme ile pompaya
/// gönderim aynı bölümdedir — reseed nesil guard'ı ona dayanır); sıra her yerde VM <c>_gate</c> → store kilididir, tersi
/// yoktur: store dışarı çağırmaz — olay yok, geri çağrı yok; uyarı bir alanda bekler, VM onu kendi bölümünde alır. Hiçbir
/// istisna dışarı sızmaz: <see cref="Append"/> IPC okuma döngüsünde koşar ve fırlatsa motorun olayları sessizce kesilirdi. Disk
/// okuması kilit DIŞINDADIR (yazar o sırada eklemeye devam eder): kilit altında yalnız bellek parçası kopyalanır ve bayt aralığı
/// yakalanır — dosya yalnız büyür, yazılmış bayt değişmez.</para>
/// </summary>
public sealed class NarrativeStore : IConsoleBacklog
{
    /// <summary>[idle-memory · dalga 2 · spec §6.3] Spool açıkken bellekte tutulan en çok satır — TEK tanım. Görünümün render dilimi
    /// bunun çok altındadır: koşunun kuyruğu hep bellektedir, disk yalnız okuyucu pencerenin ötesine kaydırınca okunur.</summary>
    public const int MemoryWindowLines = 2000;

    /// <summary>Bir tahliyenin bıraktığı boşluk: pencere dolunca bellek <c>MemoryWindowLines − EvictionBlockLines</c> satıra iner.
    /// Satır başına tahliye her eklemede bütün pencereyi kaydırır ve her seferinde diske boşaltırdı (K8).</summary>
    private const int EvictionBlockLines = 256;

    /// <summary>BOM'suz UTF-8: ofsetler bayt sayar ve dosyanın ilk satırı 0. bayttan başlar — bir BOM her ofseti kaydırırdı.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();

    /// <summary>Bellek penceresi: genel <c>i</c>. satır <c>_lines[i − _evicted]</c>'tedir.</summary>
    private readonly List<string> _lines = [];
    private long _chars;

    /// <summary>Bellekten atılmış — yalnız dosyada duran — baştaki satırların sayısı. Yalnız spool açıkken artar.</summary>
    private int _evicted;

    /// <summary>Açık spool'un yazıcısı ve akışı; spool yoksa ya da kapandıysa <c>null</c>.</summary>
    private StreamWriter? _writer;
    private Stream? _stream;

    /// <summary>Bölümün spool dosyası. Bir yazım hatası spool'u kapattıktan sonra da tutulur: tahliye edilmiş satırlar oradadır.</summary>
    private string? _spoolPath;

    /// <summary>Dosyaya yazılmış her satırın başlangıç baytı (genel satır indeksiyle); <see cref="_nextOffset"/> sıradakininki.</summary>
    private List<long> _offsets = [];
    private long _nextOffset;

    /// <summary>Bölümün bekleyen uyarısı (<see cref="TakeWarning"/>) ve bölümde bir uyarının çoktan kaydedilip kaydedilmediği.</summary>
    private string? _pendingWarning;
    private bool _warned;

    /// <summary>Bölümün kimliği: her <see cref="Clear"/> bir artırır. Kilit dışındaki bir disk okumasının hatası yalnız aralığını
    /// aldığı bölüm hâlâ sürüyorsa o bölümün uyarısıdır (<see cref="ReadDisk"/>).</summary>
    private int _section;

    /// <summary>Toplam satır sayısı: bellekteki pencere ve diske tahliye edilenler.</summary>
    public int Count { get { lock (_gate) return _evicted + _lines.Count; } }

    /// <summary>Bellekteki karakter sayısı (ayraçlar hariç) — bellek bütçesinin ölçüsü; spool açıkken pencereyle sınırlıdır.</summary>
    public long CharCount { get { lock (_gate) return _chars; } }

    /// <summary>Spool açık mı: yeni satırlar dosyaya yazılıyor ve pencere tahliye ediliyor.</summary>
    public bool IsSpooling { get { lock (_gate) return _writer is not null; } }

    /// <summary>Üretimin spool dosyasını açışı: yoksa yaratır, varsa keser; yazarken başkası okuyabilir. <c>FileShare.Delete</c>
    /// YOKTUR: silme araya girerse dosya "silinmeyi bekliyor" olur ve tahliye edilmiş geçmiş için yeni bir okuma açılamazdı; açık
    /// dosyalı bir klasörün budanması böylece zarifçe sonraki açılışa kalır.</summary>
    internal static Stream OpenSpoolFile(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

    /// <summary>[test yüzeyi] Spool akışını açan dikiş — üretimde <see cref="OpenSpoolFile"/>. Testler yazımı düşen ya da diske hiç
    /// inmeyen bir akış verir.</summary>
    internal Func<string, Stream> OpenSpoolStream { get; set; } = OpenSpoolFile;

    /// <summary>Tahliye edilmiş satırların okuma tutamacı: kısa ömürlü ve yalnız okur. <c>FileShare.ReadWrite</c> zorunludur — yazıcının
    /// yazma erişimi başka bir paylaşımı reddederdi. Tamponsuzdur: aralık tek okumada alınır.</summary>
    internal static Stream OpenSpoolFileForReading(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);

    /// <summary>[test yüzeyi] Okuma tutamacını açan dikiş — üretimde <see cref="OpenSpoolFileForReading"/>. Testler kilit dışındaki
    /// okumanın tam ortasına bir olay (yeni bölüm) sokar.</summary>
    internal Func<string, Stream> OpenSpoolReader { get; set; } = OpenSpoolFileForReading;

    /// <summary>
    /// Bir anlatı satırı ekler. Satır ayraç taşımıyorsa olduğu gibi saklanır — sıcak yol, O(1): her MSBuild satırı. Taşıyorsa
    /// belgenin göreceği satırlara bölünür: pompa her satırın ardına '\n' koyar, belge eklemeyi <c>line + "\n"</c> olarak görür
    /// (sondaki tek '\r' o '\n' ile TEK ayraç olur).
    /// <para><c>null</c> boş satırdır: eski tampon (<c>StringBuilder.Append(null)</c>) ve pompa da öyle basar; IPC thread'inde
    /// fırlatmak koşunun olay akışını keserdi. Aynı gerekçeyle spool'un yazım hatası da dışarı çıkmaz (<see cref="AddStored"/>).</para>
    /// </summary>
    public void Append(string? line)
    {
        line ??= "";
        if (line.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            lock (_gate) AddStored(line);
            return;
        }
        var pieces = ConsoleLines.Split(line + "\n");
        lock (_gate)
        {
            foreach (var piece in pieces) AddStored(piece);
        }
    }

    /// <summary>
    /// SAKLANAN bir satırın TEK yolu (kilit altında; iki ekleme yolu da buradan geçer): bellek, karakter sayısı, spool yazımı + ofset
    /// (<see cref="WriteStored"/>), tahliye. Yazılan, belgenin göreceği parçadır — ham çok satırlı giriş değil: dosyaya '\r' girmez,
    /// satır ile ofset birebir yürür. Her istisna burada yakalanır: spool kapanır, satır bellekte kalır.
    /// </summary>
    private void AddStored(string piece)
    {
        _lines.Add(piece);
        _chars += piece.Length;
        if (_writer is null) return; // spool yok ya da kapandı: satır bellekte kalır, tahliye yok
        try
        {
            WriteStored(_writer, piece, _offsets, ref _nextOffset);
            if (_lines.Count > MemoryWindowLines) Evict();
        }
        catch (Exception ex)
        {
            FailSpool(ex);
        }
    }

    /// <summary>
    /// [inceleme O1 — kopya YASAK] Saklanan bir satırın dosyadaki biçiminin TEK tanımı; iki yazım da buradan geçer: spool açılırken
    /// bellekte biriken satırlar (<see cref="BeginSpool"/>) ve sonra her yeni satır (<see cref="AddStored"/>). Satır, ardından yalnız
    /// '\n' (<c>WriteLine</c> "\r\n" yazardı); satırın başlangıç baytı indekse girer ve sıradaki ofset satırın bayt boyu + 1 kadar
    /// ilerler — yazıcının tamponu önündeyken akışın konumu bir ofset değildir. İndeks yalnız yazım başarılıysa büyür.
    /// </summary>
    private static void WriteStored(StreamWriter writer, string piece, List<long> offsets, ref long nextOffset)
    {
        writer.Write(piece);
        writer.Write('\n');
        offsets.Add(nextOffset);
        nextOffset += Utf8NoBom.GetByteCount(piece) + 1;
    }

    /// <summary>Pencereyi <c>MemoryWindowLines − EvictionBlockLines</c> satıra indirir (kilit altında). Önce boşaltır: tahliye edilen
    /// her satır diskte olmalıdır — boşaltma düşerse hiçbir satır atılmaz (istisna çağırana, oradan <see cref="FailSpool"/>'a gider).</summary>
    private void Evict()
    {
        _writer!.Flush();
        int count = _lines.Count - (MemoryWindowLines - EvictionBlockLines);
        for (int i = 0; i < count; i++) _chars -= _lines[i].Length;
        _lines.RemoveRange(0, count);
        _evicted += count;
    }

    /// <summary>Yazım ya da boşaltma düştü (kilit altında): spool kapanır — yeni satırlar bellekte kalır, tahliye durur; tahliye
    /// edilmiş satırlar dosyadan okunmaya devam eder (yol ve indeks tutulur). Bölümün uyarısı kaydedilir.</summary>
    private void FailSpool(Exception failure)
    {
        var (writer, stream) = (_writer, _stream);
        _writer = null;
        _stream = null;
        RecordWarning(WriteWarning(_spoolPath!, failure));
        Close(writer, stream);
    }

    /// <summary>
    /// Anlatıyı <paramref name="path"/>'e yaymaya başlar — koşu başı: VM, <c>runStarted</c> koşu klasörünü bildirince çağırır. Dosya
    /// yaratılır (varsa kesilir); o ana dek bellekte biriken satırlar ÖNCE yazılır, sonra yazıcı yayımlanır — üçü tek kilit bölümünde:
    /// araya bir <see cref="Append"/> girip sırayı bozamaz. Klasör YARATILMAZ: klasörün yokluğu "yazılamıyor" demektir. Yalnız tam
    /// nitelikli bir yol kabul edilir; göreli bir yol sürecin çalışma klasörüne yazardı.
    /// <para>Açılamazsa hiçbir şey yayımlanmaz, store bellekte devam eder ve bölümün uyarısı kaydedilir. Bölüm en çok bir spool açar:
    /// açık ya da bir yazım hatasıyla kapanmış spool varken çağrı hiçbir şey yapmaz — <c>FileMode.Create</c> dosyayı keser, tahliye
    /// edilmiş geçmiş ve ofset indeksi boşa düşerdi. Dönüş, store'un çağrıdan sonra diske yayılıp yayılmadığıdır.</para>
    /// </summary>
    public bool BeginSpool(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (_spoolPath is not null) return _writer is not null;
            StreamWriter? writer = null;
            Stream? stream = null;
            try
            {
                if (!Path.IsPathFullyQualified(path))
                    throw new ArgumentException("The console history spool needs a fully qualified path.", nameof(path));
                stream = OpenSpoolStream(path);
                writer = new StreamWriter(stream, Utf8NoBom);
                // Spool açılmadıysa tahliye de olmadı (_evicted 0): bellekteki satırlar bölümün TÜM satırlarıdır.
                var offsets = new List<long>(_lines.Count);
                long next = 0;
                foreach (string line in _lines) WriteStored(writer, line, offsets, ref next);
                (_writer, _stream, _spoolPath, _offsets, _nextOffset) = (writer, stream, path, offsets, next);
                return true;
            }
            catch (Exception ex)
            {
                Close(writer, stream);
                RecordWarning(WriteWarning(path, ex));
                return false;
            }
        }
    }

    /// <summary>Spool tamponunu diske indirir (açık değilse hiçbir şey yapmaz). VM bir işlem bitince, kabuk penceresi son kez kapanırken
    /// çağırır: yazıcı bölüm boyunca açık kalır ve süreç kapanırken sonlandırıcı koşmaz — tamponda kalan kuyruk dosyaya hiç inmezdi.
    /// Düşerse spool kapanır.</summary>
    public void Flush()
    {
        lock (_gate) FlushLocked();
    }

    private void FlushLocked()
    {
        if (_writer is null) return;
        try { _writer.Flush(); }
        catch (Exception ex) { FailSpool(ex); }
    }

    /// <summary>Bölümün bekleyen spool uyarısını alır ve siler (yoksa <c>null</c>). Store dışarı çağırmaz: VM uyarıyı kendi kilit
    /// bölümünde, kendi satırını pompaya gönderdikten SONRA alır ve bir anlatı satırı olarak ekler — store sırası pompa sırasıyla aynı
    /// kalır. Bölümün tek uyarısı alınmıştır: o satırın eklenmesi (spool kapalıysa bellekte kalır) yeni bir uyarı doğurmaz.</summary>
    public string? TakeWarning()
    {
        lock (_gate)
        {
            string? warning = _pendingWarning;
            _pendingWarning = null;
            return warning;
        }
    }

    /// <summary>Bölümün TEK uyarısını kaydeder (kilit altında): ilki kalır, sonrakiler yok sayılır — her işlem konsolu zaten temizler,
    /// uyarı bölüm başına bir kez yeter.</summary>
    private void RecordWarning(string warning)
    {
        if (_warned) return;
        _warned = true;
        _pendingWarning = warning;
    }

    /// <summary>Yazım uyarısı (İngilizce): istisnanın TÜR adını taşır, yerelleştirilmiş OS iletisini değil.</summary>
    private static string WriteWarning(string path, Exception failure) =>
        $"warning: console history could not be written to {path} — kept in memory ({failure.GetType().Name})";

    /// <summary>Okuma uyarısı (İngilizce): tahliye edilmiş satırlar dosyadan geri okunamadı.</summary>
    private static string ReadWarning(string path, Exception failure) =>
        $"warning: console history could not be read back from {path} ({failure.GetType().Name})";

    /// <summary>Anlatıyı boşaltır (yeni işlem). [idle-memory · final review m3] Satır dizisi de bırakılır: <c>List.Clear</c> onu en
    /// büyük işlemin boyunda tutardı — 100 k satırlık bir koşudan sonra ≈ 1 MB (LOH'ta), daha küçük bir işlem onu silse bile uygulama
    /// ömrü boyunca. Yeni işlem diziyi yeniden büyütür; büyümenin artıklarını işlem sonu trimi toplar.
    /// <para>[idle-memory · dalga 2 · I5] Spool kapanır — kuyruk önce boşaltılır, dosya SİLİNMEZ: koşu klasöründe kalır ve klasörle
    /// budanır. Ofset indeksi de bırakılır (o da satır başına 8 B'tır ve en büyük işlemin boyunda kalırdı); bölümün uyarısı düşer ve
    /// yeni bölüm yeni bir kimlik alır (<see cref="_section"/>). Kapatma kilit DIŞINDADIR: yazıcı artık yalnız bu çağrınındır.</para></summary>
    public void Clear()
    {
        StreamWriter? writer;
        Stream? stream;
        lock (_gate)
        {
            _lines.Clear();
            _lines.TrimExcess();
            _chars = 0;
            (writer, stream) = (_writer, _stream);
            _writer = null;
            _stream = null;
            _spoolPath = null;
            _offsets = [];
            _nextOffset = 0;
            _evicted = 0;
            _pendingWarning = null;
            _warned = false;
            _section++;
        }
        Close(writer, stream);
    }

    /// <summary>Yazıcıyı kapatır (önce boşaltır) ve akışı bırakır; hiçbir hata dışarı çıkmaz. Boşaltma düşse de akış kapanır —
    /// tutamaç sızmaz; yazılamayan kuyruk bellekteydi.</summary>
    private static void Close(StreamWriter? writer, Stream? stream)
    {
        try { writer?.Dispose(); }
        catch (Exception) { /* bozuk spool: kuyruk yazılamadı */ }
        try { stream?.Dispose(); }
        catch (Exception) { /* bozuk spool: akış zaten kapanıyordu */ }
    }

    /// <summary>[test yüzeyi] Satır dizisinin kapasitesi — temizliğin diziyi de bıraktığını (<see cref="Clear"/>) sınar.</summary>
    internal int Capacity { get { lock (_gate) return _lines.Capacity; } }

    /// <summary>[test yüzeyi] Ofset indeksinin kapasitesi — temizliğin indeksi de bıraktığını sınar.</summary>
    internal int OffsetCapacity { get { lock (_gate) return _offsets.Capacity; } }

    /// <summary>[test yüzeyi] Bellekteki satır sayısı (pencere); <c>Count − LinesInMemory</c> satır yalnız dosyadadır.</summary>
    internal int LinesInMemory { get { lock (_gate) return _lines.Count; } }

    /// <summary>[from, to) satırlarını '\n' SONEKLİ birleştirir (sınırlar kırpılır; boş aralık ⇒ <c>""</c>). Tahliye edilmiş baş
    /// dosyadan okunur — metni zaten '\n' soneklidir, yeniden bölünüp birleştirilmez — bellekteki kuyruk <see cref="ConsoleLines.Join"/>'den
    /// geçer. Sonuç ya TAM aralıktır ya <c>""</c>: dosya okunamazsa kısmi metin dönmez (görünüm dikişini indekslerden hesaplar) ve
    /// bölümün uyarısı kaydedilir.</summary>
    public string Join(int from, int to)
    {
        SpoolRange? disk;
        string memory;
        lock (_gate)
        {
            (from, to) = Clamp(from, to);
            if (from >= to) return "";
            disk = TakeDiskRange(from, to);
            memory = ConsoleLines.Join(_lines, Math.Max(from, _evicted) - _evicted, to - _evicted);
        }
        if (disk is not { } range) return memory;
        return ReadDisk(range) is { } text ? text + memory : "";
    }

    /// <summary>[from, to) satırlarının KOPYASI (sınırlar kırpılır) — <see cref="Join"/>'in liste hâli, aynı sözleşmeyle: tam aralık
    /// ya da boş liste. Dosyadan okunan baş satırlarına <see cref="ConsoleLines.Split"/> ile ayrılır (tek satır kuralı).</summary>
    public IReadOnlyList<string> Slice(int from, int to)
    {
        SpoolRange? disk;
        List<string> memory;
        lock (_gate)
        {
            (from, to) = Clamp(from, to);
            if (from >= to) return [];
            disk = TakeDiskRange(from, to);
            int memoryFrom = Math.Max(from, _evicted);
            memory = memoryFrom >= to ? [] : _lines.GetRange(memoryFrom - _evicted, to - memoryFrom);
        }
        if (disk is not { } range) return memory;
        if (ReadDisk(range) is not { } text) return [];
        var lines = ConsoleLines.Split(text);
        lines.AddRange(memory);
        return lines;
    }

    /// <summary>Sınırları toplam satıra kırpar (kilit altında).</summary>
    private (int From, int To) Clamp(int from, int to) => (Math.Max(0, from), Math.Min(to, _evicted + _lines.Count));

    /// <summary>[from, to)'nun tahliye edilmiş başının bayt aralığı (kilit altında); başı yoksa <c>null</c>. Spool açıksa tampon önce
    /// boşaltılır — tahliye edilen bloklar zaten diskteydi, boşaltma okumayı o sıraya bağlı bırakmaz. Uç ofset bellekteki ilk satırın
    /// başlangıcıdır ve hep indekstedir: tahliye anında pencerenin her satırı yazılmıştı.</summary>
    private SpoolRange? TakeDiskRange(int from, int to)
    {
        int diskTo = Math.Min(to, _evicted);
        if (from >= diskTo) return null;
        FlushLocked();
        return new SpoolRange(_spoolPath!, _offsets[from], _offsets[diskTo], diskTo - from, _section);
    }

    /// <summary>Tahliye edilmiş bir aralığın metni — KİLİT DIŞINDA okunur: kısa ömürlü bir okuma tutamacı
    /// (<see cref="OpenSpoolReader"/>), baytlar BOM'suz UTF-8 olarak çözülür ve satır sayısı doğrulanır. Okunamazsa (silinmiş, kesilmiş
    /// dosya, okuma hatası) <c>null</c> ve bölümün uyarısı.
    /// <para>[inceleme M4] Uyarı yalnız aralığın alındığı bölüm hâlâ sürüyorsa kaydedilir: okuma kilit dışındayken bir temizlik (yeni
    /// işlem) araya girdiyse hata ESKİ bölümündür — yenisinin konsoluna önceki koşunun dosyasından söz eden bir satır düşerdi. Kimlik
    /// bölüm sayacıdır (<see cref="_section"/>), yol değil: bir bölüm tek spool açar, yani aynı bölümün yolu da aynıdır; yalnız yol
    /// karşılaştırması aynı yolu yeniden açan sonraki bir bölümü ayıramazdı.</para></summary>
    private string? ReadDisk(SpoolRange range)
    {
        try
        {
            var bytes = new byte[checked((int)(range.End - range.Start))];
            using (var stream = OpenSpoolReader(range.FilePath))
            {
                stream.Seek(range.Start, SeekOrigin.Begin);
                stream.ReadExactly(bytes);
            }
            string text = Utf8NoBom.GetString(bytes);
            if (text.AsSpan().Count('\n') != range.Lines || !text.EndsWith('\n'))
                throw new InvalidDataException("The console history spool does not hold the indexed lines.");
            return text;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (range.Section == _section) RecordWarning(ReadWarning(range.FilePath, ex));
            }
            return null;
        }
    }

    /// <summary>Dosyadaki bir satır aralığı: yol, [Start, End) baytları, satır sayısı ve aralığın alındığı bölüm.</summary>
    private readonly record struct SpoolRange(string FilePath, long Start, long End, int Lines, int Section);

    /// <summary>Store kırpılan satırları SAKLAMAZ: belgedeki her anlatı satırı önce store'a yazılmıştır, kopyası tutulmaz — görünüm
    /// de kırpılan metni onun için belgeden okumaz (<see cref="IConsoleBacklog.KeepsTrimmedLines"/>).</summary>
    public bool KeepsTrimmedLines => false;

    /// <summary>Kırpılan satırlar zaten store'dadır: yapılacak bir şey yok (<see cref="IConsoleBacklog.AcceptTrimmed"/>).</summary>
    public void AcceptTrimmed(IReadOnlyList<string> lines) { }
}
