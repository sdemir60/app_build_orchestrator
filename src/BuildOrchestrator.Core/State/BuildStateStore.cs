using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.State;

/// <summary>
/// [T27] Global build-state persist: `<cacheRoot>\build-state.json`, projectId anahtarlı <see cref="BuildState"/>
/// map'i. Tek writer semaforu ile serialize edilir, yazım atomik temp+rename (bkz. EvaluationCache/StaleObjDetector
/// deseni) — reader hiçbir zaman yarım/bozuk JSON görmez. Bozuk/okunamaz dosya asla fırlatmaz (warn-only,
/// StaleObjDetector deseni): boş map ile devam edilir. §4: yalnız bu JSON dosyasına I/O yapar; DLL/bin/obj asla okunmaz.
/// </summary>
public sealed class BuildStateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public BuildStateStore(string cacheRoot) => _path = Path.Combine(cacheRoot, "build-state.json");

    /// <summary>
    /// TEST-ONLY hook: her başarısız rename denemesinden sonra, retry sleep'inden ÖNCE çağrılır (parametre: 1-based
    /// attempt no). Üretimde null bırakılır — zero-cost (bir null-check dışında davranış değişmez, 20x5ms bütçesi
    /// AYNEN korunur). Testler (BuildOrchestrator.Tests, InternalsVisibleTo ile) bunu, sabit bir wall-clock bekleme
    /// (ör. Task.Delay(40)) yerine GÖZLEMLENEN retry ilerlemesine göre kilidi bırakmak için kullanır — böylece
    /// paralel test yükü altında (xUnit sınıfları paralel koşar) makine yavaşlasa bile test deterministik kalır.
    /// </summary>
    internal Action<int>? OnRenameRetry { get; set; }

    /// <summary>Diskten tüm build-state map'ini okur. Dosya yok/boş/bozuk → boş map, ASLA fırlatmaz.</summary>
    public IReadOnlyDictionary<string, BuildState> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string text = ReadAllTextSharingDelete(_path);
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);
            var map = JsonSerializer.Deserialize<Dictionary<string, BuildState>>(text, Json);
            if (map is null) return new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);
            // [Review Important 1] map, JSON'dan Ordinal-comparer bir Dictionary olarak gelir. Elle bozulmuş bir
            // dosyada büyük/küçük harfle FARKLI iki anahtar aynı projeye işaret edebilir (ör. "C:\repo\A.csproj" ve
            // "c:\repo\a.csproj"); bunu doğrudan bir OrdinalIgnoreCase Dictionary'e kopyalamak ArgumentException
            // fırlatır (never-throw sözleşmesini ihlal eder). GroupBy ile ignore-case dedup edilir — dosyadaki JSON
            // sırasında SON görülen değer kazanır; map tamamen boşalmak yerine hayatta kalan kayıtlarla döner.
            return map
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase); // bozuk build-state → warn-only, sıfırdan kurulur
        }
    }

    /// <summary>
    /// Tek projenin state'ini merge edip TÜM map'i atomik olarak (temp dosyaya yaz → <see cref="File.Move"/>
    /// overwrite:true rename) diske yazar. Eşzamanlı çağrılar <see cref="_writeGate"/> ile serialize edilir —
    /// concurrent Upsert'ler ne birbirini kaybeder ne de dosyayı yarım bırakır.
    /// </summary>
    public void Upsert(BuildState state)
    {
        _writeGate.Wait();
        try
        {
            // Load() zaten ignore-case dedup edilmiş bir map döner (yukarıdaki [Review Important 1] fix'i); bu
            // kopya sadece state.ProjectId'yi merge eder, ayrıca bir case-collision riski taşımaz.
            var map = new Dictionary<string, BuildState>(Load(), StringComparer.OrdinalIgnoreCase) { [state.ProjectId] = state };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(map, Json));
                MoveAtomicWithRetry(tmp, _path);
            }
            catch
            {
                // [Review Minor 4] rename retry bütçesini aşarsa (veya yazım sonrası başka bir şey fırlarsa) tmp
                // dosyası diskte öksüz kalmasın — best-effort temizlik, orijinal exception önceliklidir.
                try { File.Delete(tmp); } catch { /* best-effort, temizlik başarısızlığı orijinal hatayı gölgelemez */ }
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// <see cref="File.ReadAllText(string)"/> yerine: varsayılan <c>FileShare.Read</c> Delete-share İZİN VERMEZ,
    /// bu da eşzamanlı bir <see cref="Upsert"/>'in atomik rename'ini (<see cref="File.Move"/> hedefte açık bir
    /// okuma handle'ı varken silme/rename gerektirir) sharing-violation ile bloklayabilir. Nazik bir reader
    /// Delete-share'i AÇIKÇA vererek yazıcının rename'ini asla engellememelidir.
    /// </summary>
    private static string ReadAllTextSharingDelete(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// <see cref="File.Move(string, string, bool)"/> hedefte açık bir okuma handle'ı olduğunda — Delete-share
    /// verilmiş olsa BİLE — geçici bir sharing-violation (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>)
    /// ile başarısız olabilir (gözlemlenen Windows davranışı: handle kapanışı ile rename arasında kısa bir yarış
    /// penceresi kalıyor). Bu GERÇEK VERİ KAYBI değildir — tmp dosya hâlâ diskte durur; kısa, sınırlı bir retry
    /// bu geçici pencereyi absorbe eder (bkz. RetryingMsBuildInvoker'daki MSB302x contention retry deseni — burada
    /// enjekte edilmiş delay yerine küçük sabit bekleme yeterli, çünkü pencere mikrosaniyeler mertebesinde).
    /// </summary>
    private void MoveAtomicWithRetry(string tmp, string target)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tmp, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                // [Review Minor 3] Deliberate deviation: RetryingMsBuildInvoker enjekte edilebilir async delay
                // kullanır (testability için DI), ama Upsert senkron bir metot — o desen burada doğrudan uymuyor.
                // Gerçek Thread.Sleep(5) kullanılıyor; üst bütçe küçük ve sabit (20 deneme x 5ms ≈ 100ms max).
                OnRenameRetry?.Invoke(attempt); // test-only, null → zero-cost; üretim davranışı/bütçesi değişmez
                Thread.Sleep(5);
            }
        }
    }
}
