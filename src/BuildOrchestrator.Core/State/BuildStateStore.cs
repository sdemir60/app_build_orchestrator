using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

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

    /// <summary>Atomik rename'in retry bütçesi: 20 deneme x <see cref="RenameRetryBackoff"/> ≈ 100ms üst sınır
    /// (bkz. <see cref="MoveAtomicWithRetry"/>).</summary>
    private const int RenameAttempts = 20;

    /// <summary>Rename retry'ının ÜRETİM backoff'u — <see cref="DefaultRenameRetryDelay"/>'in tek kaynağı.</summary>
    private static readonly TimeSpan RenameRetryBackoff = TimeSpan.FromMilliseconds(5);

    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public BuildStateStore(string cacheRoot) => _path = Path.Combine(cacheRoot, "build-state.json");

    /// <summary>
    /// [T49 FINAL PASS · D8] Başarısız bir rename denemesinden SONRAKİ gecikmenin TAMAMI — enjekte edilebilir dikiş
    /// (parametre: 1-based attempt no). Üretimde null → <see cref="DefaultRenameRetryDelay"/> (sabit, küçük, sınırlı
    /// backoff; davranış/bütçe DEĞİŞMEZ). Desen <c>RunCoordinator</c>'ın <c>retryDelay</c> dikişiyle aynıdır.
    ///
    /// <para>Eskiden burada bir gözlem hook'u + AYRI bir <c>Thread.Sleep(5)</c> vardı: testler retry ilerlemesini
    /// hook'tan görüyor ama gecikmeyi GERÇEK ZAMANDA ödüyordu (D8: sleep-poll YASAK). Artık gecikmenin KENDİSİ
    /// enjekte edilir — test onu bir randevuya (kilit bırakıldı sinyali) ya da anında dönüşe çevirir; wall-clock
    /// tahmini de gerçek bekleme de kalmaz.</para>
    /// </summary>
    internal Action<int>? RenameRetryDelay { get; set; }

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
    /// [W1] <see cref="Load"/> sonucundan bir projenin SON BAŞARIYLA derlendiği commit'i çeker; kayıt yoksa
    /// (hiç derlenmemiş proje) ya da map hiç yoksa <c>null</c>. <c>BuildPreviewItem.BuiltCommit</c>
    /// projeksiyonunun TEK yeri: Sync yolu (Core'daki <c>SyncWorkspaceService</c>) ve run yolu
    /// (Supervisor'daki <c>RunCoordinator</c>) aynı aramayı iki kez YAZMAZ.
    /// </summary>
    public static string? BuiltCommitOf(IReadOnlyDictionary<string, BuildState>? state, string projectId) =>
        state is not null && state.TryGetValue(projectId, out var found) ? found.BuiltCommit : null;

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
    /// bu geçici pencereyi absorbe eder (bkz. RetryingMsBuildInvoker'daki MSB302x contention retry deseni; gecikme
    /// orada olduğu gibi burada da ENJEKTE EDİLEBİLİR — <see cref="RenameRetryDelay"/>).
    /// </summary>
    private void MoveAtomicWithRetry(string tmp, string target)
        // [B2] Döngünün kendisi ortak (SyncRetry) — burada yalnız BU yolun kararları durur: kaç deneme, hangi
        // istisna geçici, gecikme nereden gelir, bütçe tükenince ne olur (burada: orijinal istisna yayılır).
        => SyncRetry.Run(
            () => File.Move(tmp, target, overwrite: true),
            RenameAttempts,
            ex => ex is IOException or UnauthorizedAccessException,
            EffectiveRenameRetryDelay,
            rethrowWhenExhausted: true);

    /// <summary>
    /// [B1] Gerçekten koşacak gecikme: dikiş kuruluysa o, değilse ÜRETİM varsayılanı. Ayrı bir üye olmasının
    /// sebebi testtir — "üretimde hangi gecikme koşuyor" sorusu ancak böyle DOĞRUDAN pinlenebilir; aksi halde
    /// varsayılanı no-op'a çeviren bir mutasyon tüm süiti yeşil bırakırdı.
    /// </summary>
    internal Action<int> EffectiveRenameRetryDelay => RenameRetryDelay ?? DefaultRenameRetryDelay;

    /// <summary>
    /// <see cref="RenameRetryDelay"/>'in üretim varsayılanı. Beklenen olay BAŞKA bir process'in okuma handle'ını
    /// kapatmasıdır — bekleyecek bir handle/TCS YOKTUR, bu yüzden sınırlı bir zaman aşımı tek seçenektir; D8'in
    /// hedefi olan "kendi kodumuzun ürettiği bir durumu sleep ile poll etmek" DEĞİLDİR ve testlere hiç sızmaz.
    /// </summary>
    internal static void DefaultRenameRetryDelay(int attempt) => Thread.Sleep(RenameRetryBackoff);
}
