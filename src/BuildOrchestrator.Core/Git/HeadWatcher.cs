using System.Text;

namespace BuildOrchestrator.Core.Git;

/// <summary>[Faz 2/T7] HEAD izleyicisinin dikişi — App'in koordinatörü bunu tanır, testler sahtesini verir.</summary>
public interface IHeadWatcher : IDisposable
{
    /// <summary>İzlemeyi başlatır; kurulamazsa <c>false</c> döner ve gerekçe <see cref="UnavailableReason"/>'dadır.
    /// <paramref name="onSettled"/> thread-pool thread'inde çağrılır.</summary>
    bool Start(string gitDir, Action<HeadMove> onSettled);

    /// <summary>Son <see cref="Start"/> kurulamadıysa gerekçesi (kısa, İngilizce); aksi hâlde <c>null</c>.</summary>
    string? UnavailableReason { get; }
}

/// <summary>
/// [Faz 2/T7 · spec 2026-09-18 §6.1] <c>&lt;gitDir&gt;\logs\HEAD</c>'i izler — Windows dosya bildirimi, yoklama
/// YOK (boştayken iş yok); repo, kaynak dosyalar, <c>bin</c>/<c>obj</c> izlenmez. Her yazım
/// <see cref="SettleDebouncer"/>'ı yeniden kurar; <see cref="SettleDelay"/> sessizlikten sonra pencerede
/// reflog'a EKLENEN satırlar <see cref="ReflogEntry.Classify"/> ile sınıflanır ve en güçlüsüyle
/// (<see cref="HeadMoveRules.Stronger"/>) TEK çağrı yapılır.
///
/// <para><b>Neden yalnız son satır değil:</b> checkout'un hemen ardından gelen commit'ler aynı pencereye düşer; son
/// satır bir commit olsa da branch değişmiştir. Pencerede eklenen bütün satırlar okunur (izleyici son okuduğu
/// uzunluğu tutar). Dosya kısaldıysa (reflog expire / yeniden yazım) yalnız son satır sınıflanır; okunamazsa
/// <see cref="HeadMove.Other"/> — güvenli yön: koordinatör kararı HEAD'e bakarak verir.</para>
///
/// <para>[reftable] Ref'leri tablolarda tutan repoda (<see cref="HeadReader.IsReftable"/>) <c>logs</c> klasörü yoktur:
/// izleyici <c>reftable\tables.list</c>'i izler (git her ref değişikliğinde onu yeniden yazar) ve pencere kapanınca
/// HEAD reflog'unun en yeni kayıtlarını git'ten alır (<see cref="GitService.GetHeadReflogAsync"/>, git dizininin içinden —
/// linked worktree'nin kendi tablosu da orada). Yeni hareketler, son okumada en yeni olan kaydın ÜSTÜNDEKİ kayıtlardır
/// (<see cref="ReflogRecord.Key"/>) ve dosyadakiyle aynı kural uygulanır: en güçlüsü; son kayıt bulunamazsa (taban yok,
/// süresi dolup silindi, pencerede <see cref="ReflogWindow"/>'dan fazla hareket) yalnız en yenisi; okunamazsa Other. Tablo
/// listesi HEAD'e dokunmayan değişikliklerde de yenilenir (fetch'in uzak ref'leri, yeni branch ya da tag) — en yeni kayıt
/// aynı kaldığı için çağrı yapılmaz.</para>
/// </summary>
public sealed class HeadWatcher : IHeadWatcher
{
    /// <summary>Git'in bir işlemdeki ardışık yazımlarını tek tetiğe indiren sessizlik — TEK tanım (spec §6.1).</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1.5);

    private const string LogsFolder = "logs";
    private const string HeadLog = "HEAD";

    /// <summary>[reftable] Tabloların klasörü ve git'in her ref değişikliğinde yeniden yazdığı liste dosyası.</summary>
    private const string ReftableFolder = "reftable";
    private const string TablesList = "tables.list";

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private SettleDebouncer? _debouncer;
    private string _logPath = string.Empty;
    private long _offset;

    /// <summary>[reftable] Bir pencerede git'ten okunan en yeni reflog kaydı sayısı — reflog'un tamamı okunmaz.</summary>
    internal const int ReflogWindow = 50;

    /// <summary>[reftable] İzlenen git dizini (reflog git'e buradan sorulur).</summary>
    private string _gitDir = string.Empty;

    /// <summary>[reftable] Son okumada en yeni olan kaydın kimliği (reflog boşsa <c>null</c>); ilk pencereye dek
    /// <see cref="_reflogBaseline"/> geçerlidir.</summary>
    private string? _lastReflogKey;

    /// <summary>[reftable] <see cref="_lastReflogKey"/> bir okumadan geldi mi — gelmediyse taban kullanılır.</summary>
    private bool _reflogRead;

    /// <summary>[reftable] Başlangıçta en yeni kaydın kimliği — <see cref="Start"/> onu arka planda ister (çağıran UI thread'i
    /// bir git process'ini beklemez); ilk pencere kapanınca okunur.</summary>
    private Task<string?>? _reflogBaseline;

    /// <summary>İzleyici hareket görmeye hazır: reftable'da başlangıç okuması bitti (normal repoda hemen). O okuma bitmeden
    /// olan bir hareket tabana karışır ve görülmez (pencereye dönüş güvenlik ağıdır). Testler değişikliği bundan sonra yapar.</summary>
    internal Task Ready => _reflogBaseline ?? Task.CompletedTask;

    /// <summary>Bir pencere kapandı ve okundu — hareket olsun olmasın. Testler "hiçbir şey olmadı" iddiasını sabit bir
    /// bekleme yerine bununla sınar.</summary>
    internal event Action? WindowClosed;

    /// <summary>[final review M6] Son okuma yarım bir satırla bittiyse dosyanın o anki uzunluğu; bitmediyse <c>null</c>.</summary>
    private long? _partialTailAt;

    /// <summary>[final review M6] Pencerenin en son hangi uzunlukta yarım satır yüzünden yeniden kurulduğu — aynı yarım
    /// kuyruk için pencere BİR kez yeniden kurulur (bozuk, hiç tamamlanmayan bir kuyruk yoklamaya dönüşmesin).</summary>
    private long _rearmedAtLength = -1;

    /// <param name="delay">Sessizlik saati; verilmezse gerçek bekleme (bir debounce — yoklama DEĞİL).</param>
    public HeadWatcher(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? ((window, ct) => Task.Delay(window, ct));
    }

    public string? UnavailableReason { get; private set; }

    public bool Start(string gitDir, Action<HeadMove> onSettled)
    {
        Stop();
        UnavailableReason = null;
        return HeadReader.IsReftable(gitDir) ? StartOnTables(gitDir, onSettled) : StartOnReflogFile(gitDir, onSettled);
    }

    /// <summary>Normal (files) repo: <c>logs\HEAD</c>'e eklenen satırlar okunur.</summary>
    private bool StartOnReflogFile(string gitDir, Action<HeadMove> onSettled)
    {
        string logsDir = Path.Combine(gitDir, LogsFolder);
        if (!Directory.Exists(logsDir))
        {
            UnavailableReason = "no reflog folder";
            return false;
        }

        _logPath = Path.Combine(logsDir, HeadLog);
        _offset = LengthOrZero(_logPath);
        return Watch(logsDir, HeadLog, () => Settle(onSettled));
    }

    /// <summary>[reftable] Tablo listesi izlenir, hareketler git'in reflog'undan okunur (<see cref="ReadNewTableMove"/>).</summary>
    private bool StartOnTables(string gitDir, Action<HeadMove> onSettled)
    {
        string tablesDir = Path.Combine(gitDir, ReftableFolder);
        if (!Directory.Exists(tablesDir))
        {
            UnavailableReason = "no reftable folder";
            return false;
        }

        _gitDir = gitDir;
        _lastReflogKey = null;
        _reflogRead = false;
        _reflogBaseline = Task.Run(() => ReadReflog(gitDir) is { Count: > 0 } records ? records[0].Key : null);
        return Watch(tablesDir, TablesList, () =>
        {
            if (ReadNewTableMove() is { } move) onSettled(move);
            WindowClosed?.Invoke();
        });
    }

    /// <summary>Tek dosyanın Windows bildirimini kurar; her yazım sessizlik penceresini yeniden kurar. İki kipin ortak yolu.</summary>
    private bool Watch(string folder, string file, Action onSettled)
    {
        _debouncer = new SettleDebouncer(SettleDelay, _delay, onSettled);
        try
        {
            var watcher = new FileSystemWatcher(folder, file)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            watcher.Changed += (_, _) => _debouncer?.Poke();
            watcher.Created += (_, _) => _debouncer?.Poke();
            watcher.Renamed += (_, _) => _debouncer?.Poke();
            watcher.Error += (_, _) => _debouncer?.Poke(); // tampon taştı: bir şey oldu, satırlar yine okunur
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            Stop();
            UnavailableReason = ex.Message.TrimEnd('.', ' ', '\r', '\n');
            return false;
        }
    }

    /// <summary>Pencere kapandı: eklenen satırlar sınıflanır; yeni satır yoksa çağrı yapılmaz. [final review M6] Okuma
    /// yarım bir satırla bittiyse (git satırın ortasında) pencere yeniden kurulur: satırın geri kalanının yazımı
    /// ayrı bir bildirim üretmese de satır bütün hâliyle bir sonraki pencerede okunur.</summary>
    private void Settle(Action<HeadMove> onSettled)
    {
        HeadMove? move = ReadNewMove();
        if (move is { } settled) onSettled(settled);
        if (TakeRearm()) _debouncer?.Poke();
        WindowClosed?.Invoke();
    }

    /// <summary>Son okuma yeni bir yarım kuyrukla bittiyse <c>true</c> (kuyruk başına bir kez).</summary>
    private bool TakeRearm()
    {
        lock (_gate)
        {
            if (_partialTailAt is not { } length || length == _rearmedAtLength) return false;
            _rearmedAtLength = length;
            return true;
        }
    }

    /// <summary>Son okunan konumdan bu yana eklenen satırların en güçlü hareketi; yeni satır yoksa <c>null</c>.</summary>
    internal HeadMove? ReadNewMove()
    {
        lock (_gate) return ReadNewMoveLocked();
    }

    private HeadMove? ReadNewMoveLocked()
    {
        _partialTailAt = null;
        try
        {
            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            if (length == _offset) return null;

            bool truncated = length < _offset;
            long start = truncated ? 0 : _offset;
            stream.Seek(start, SeekOrigin.Begin);
            byte[] bytes = new byte[length - start];
            stream.ReadExactly(bytes);

            // [review M5] Okuma konumu yalnız son TAM satırın sonuna ilerler: git yazımın ortasındaysa yarım satır
            // bir sonraki pencerede bütün hâliyle okunur (yarısı ayrı bir "satır" gibi sınıflanmaz).
            int lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
            if (lastNewline + 1 < bytes.Length) _partialTailAt = length;
            if (lastNewline < 0)
            {
                if (truncated) _offset = 0;
                return null;
            }
            _offset = start + lastNewline + 1;

            string text = Encoding.UTF8.GetString(bytes, 0, lastNewline + 1);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) return null;
            if (truncated) return ReflogEntry.Classify(lines[^1]);
            return HeadMoveRules.Strongest(lines.Select(ReflogEntry.Classify));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _offset = LengthOrZero(_logPath);
            return HeadMove.Other;
        }
    }

    /// <summary>[reftable] Son okumada en yeni olan kaydın üstüne eklenen kayıtların en güçlü hareketi; eklenen yoksa
    /// <c>null</c>. Kural <see cref="ReadNewMoveLocked"/>'ınkiyle aynıdır, sınır satır yerine kaydın kimliğidir.</summary>
    private HeadMove? ReadNewTableMove()
    {
        lock (_gate)
        {
            string? known = _reflogRead ? _lastReflogKey : _reflogBaseline?.GetAwaiter().GetResult();
            var records = ReadReflog(_gitDir);
            if (records is null) return HeadMove.Other; // okunamadı — güvenli yön (dosya kipindeki IO hatası gibi)
            _reflogRead = true;
            _lastReflogKey = records.Count > 0 ? records[0].Key : null;
            if (records.Count == 0) return null;

            int boundary = known is null ? -1 : IndexOfKey(records, known);
            if (boundary == 0) return null; // en yeni kayıt aynı: HEAD oynamadı (fetch, yeni branch ya da tag)
            // Sınır yoksa (taban yok, süresi dolup silindi, pencereden fazla hareket) yalnız en yenisi — dosyanın kısalması gibi.
            var fresh = boundary > 0 ? records.Take(boundary) : records.Take(1);
            return HeadMoveRules.Strongest(fresh.Reverse().Select(r => ReflogEntry.ClassifyMessage(r.Message)));
        }
    }

    private static int IndexOfKey(IReadOnlyList<ReflogRecord> records, string key)
    {
        for (int i = 0; i < records.Count; i++)
            if (string.Equals(records[i].Key, key, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>[reftable] HEAD reflog'unun en yeni kayıtları (en yeniden eskiye); okunamazsa <c>null</c>. Pencere geri
    /// çağrısında ve başlangıç tabanında — thread-pool'da — çağrılır, UI thread'inde değil.</summary>
    private static IReadOnlyList<ReflogRecord>? ReadReflog(string gitDir)
    {
        var result = GitService.InGitDirectory(gitDir).GetHeadReflogAsync(ReflogWindow).GetAwaiter().GetResult();
        return result.Success ? result.Value : null;
    }

    private static long LengthOrZero(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debouncer?.Dispose();
        _debouncer = null;
    }

    public void Dispose() => Stop();
}
