using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Paths;
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
            string text = AtomicFile.ReadAllTextSharingDelete(_path);
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
    /// [v1.16.0] Projenin KENDİ girdi dosyaları son başarılı derlemeden bu yana değişti mi — satırın
    /// <c>modified</c> ↔ <c>affected</c> ayrımı.
    ///
    /// <para>Karşılaştırma deftere yazılmış içerik özetiyle bugünkü özet arasındadır, İMZAYLA DEĞİL: imza
    /// upstream'leri de taşır, yani bir bağımlılığın kaydı geçersizleştiğinde (hata sonrası invalidasyon) de
    /// değişir. Ölçüldü — gerçek bir çalışma alanında bağımlılığı patlamış altı proje, kullanıcı hiçbir
    /// dosyasına dokunmadığı hâlde <c>modified</c> gösteriyordu; doğru sözcük <c>affected</c>'dı.</para>
    ///
    /// <para><c>null</c> ⇒ ayrım bilinmiyor (kayıt yok, ya da kayıt bu alandan önceki bir sürümde yazılmış).
    /// Satır o durumda daha ihtiyatlı olan <c>affected</c>'ı gösterir: "senin dosyan değişti" demek, olmadığı
    /// hâlde söylenirse kullanıcıyı yanlış yere baktırır.</para>
    /// </summary>
    public static bool? OwnFilesChanged(
        IReadOnlyDictionary<string, BuildState>? state, string projectId, string? currentContent)
    {
        if (state is null || !state.TryGetValue(projectId, out var found)) return null;
        if (found.BuiltContent is not { Length: > 0 } stored) return null;

        return !string.Equals(stored, currentContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// [v1.16.0] Bir projenin SON BAŞARILI derlemesinin zamanı — satırın <c>up to date · 2h</c> etiketindeki
    /// göreli yaş ve proje logu başlığındaki "Last successful build" satırı bunu okur. Kayıt yoksa ya da son
    /// koşu başarılı DEĞİLSE <c>null</c>: "hiç derlenmemiş" ile "en son patladı" ayrı olgulardır ve ikisinde
    /// de bir başarı yaşı yazılamaz. <see cref="BuiltCommitOf"/> ile aynı desen — tek arama yeri.
    /// </summary>
    public static DateTimeOffset? LastBuiltAtOf(IReadOnlyDictionary<string, BuildState>? state, string projectId) =>
        state is not null && state.TryGetValue(projectId, out var found)
        && found.LastResult == BuildResult.Succeeded ? found.LastRunAt : null;

    /// <summary>
    /// [spec 2026-09-18 §1-14] Bir projenin KANITLI son hatasının zamanı — satırın <c>failed · 2h</c>
    /// etiketindeki göreli yaş bunu okur. Kayıt yoksa ya da <see cref="BuildState.FailedSignature"/> boşsa
    /// (hiç hata yaşanmamış temiz kayıt, ya da kanıtsız/kesilmiş bir deneme — bkz. <see
    /// cref="BuildOrchestrator.Core.Planning.WillBuildEvaluator"/>) <c>null</c>: gösterilecek bir hata yaşı yoktur. <see
    /// cref="LastBuiltAtOf"/> ile AYNI desen — tek arama yeri, "imzalı kanıt var mı" sorusunu ikinci kez
    /// yazmaz.
    /// </summary>
    public static DateTimeOffset? FailedAtOf(IReadOnlyDictionary<string, BuildState>? state, string projectId) =>
        state is not null && state.TryGetValue(projectId, out var found)
        && found.FailedSignature is not null ? found.FailedAt : null;

    /// <summary>
    /// [Task 7] Bir SCC üyesinin, PLANLANAN (şu anki) bileşik imzada DAHA ÖNCE turlarla yakınsAMADIĞI hafızası —
    /// <see cref="BuildState.NonConvergentSignature"/>'ın TEK okuyucusu. Plan aşaması (RunCoordinator) ve
    /// (ileride) Sync/önizleme yolu aynı aramayı iki kez YAZMAZ — <see cref="BuiltCommitOf"/> ile aynı desen.
    /// <paramref name="currentSignature"/> <c>null</c>ise (hollow / imza hesaplanamadı) her zaman <c>false</c>:
    /// karşılaştırılacak bir taban yoktur. <paramref name="state"/> <c>null</c> olsa BİLE fırlatmaz.
    /// </summary>
    public static bool IsCycleNonConvergent(IReadOnlyDictionary<string, BuildState>? state, string projectId, string? currentSignature) =>
        currentSignature is not null
        && state is not null
        && state.TryGetValue(projectId, out var found)
        && found.NonConvergentSignature is not null
        && string.Equals(found.NonConvergentSignature, currentSignature, StringComparison.Ordinal);

    /// <summary>
    /// Tek projenin state'ini merge edip TÜM map'i atomik olarak (temp dosyaya yaz → <see cref="File.Move"/>
    /// overwrite:true rename) diske yazar. Eşzamanlı çağrılar <see cref="_writeGate"/> ile serialize edilir —
    /// concurrent Upsert'ler ne birbirini kaybeder ne de dosyayı yarım bırakır.
    /// </summary>
    public void Upsert(BuildState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Write(map => { map[state.ProjectId] = state; return true; });
    }

    /// <summary>
    /// [tek proje · Clean] Bir projenin kaydını defterden SİLER — kayıt yoksa dosyaya hiç dokunulmaz.
    ///
    /// <para>Tek çağıranı başarılı bir <c>Clean</c> koşusudur: çıktılar gittiğinde defter de onları bilmemeli.
    /// Çıktı kanıtı (ARCHITECTURE §7.6) silinen çıktıyı yalnız çıktı yolu türetilebilen projede görür
    /// (SDK-style'da göremez); kayıt kalsaydı bir sonraki <c>Build</c> böyle bir projeyi "güncel" sayıp atlar ve
    /// kullanıcı silinmiş çıktılarla yeşil bir koşu görürdü. Kaydı <b>geçersizleştirmek</b> (LastResult=Failed)
    /// yerine SİLMEK doğrudur: proje başarısız olmadı, bu araç artık onun hiçbir çıktısını bilmiyor —
    /// <c>WillBuildEvaluator</c> da kayıtsız projeyi tam olarak böyle okur.</para>
    /// </summary>
    public void Remove(string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Write(map => map.Remove(projectId));
    }

    /// <summary>
    /// [spec 2026-09-18 §5.5 · karar 12] <b>Kanıtsız geçersizlemenin TEK yeri.</b> Projenin mevcut kaydı "son deneme
    /// başarısız, ama bu kaynağın patladığına dair kanıt yok" hâline çekilir: <c>LastResult=Failed</c>,
    /// <c>LastRunAt=</c><paramref name="now"/>, <c>FailedSignature=null</c>, <c>FailedAt=null</c>. İmza, commit ve
    /// süre KORUNUR (Fast modda dependent'ların tabanı, ETA'nın ölçümü). Satır bir sonraki Sync'te gri
    /// <c>never built</c> olur: derleme kanıtı <c>LastRunAt</c>'tan eski kalır, zaman kipine giremez.
    ///
    /// <para>Kayıt yoksa no-op — kanıtsız bir olay deftere yeni kayıt AÇMAZ (kayıtsız proje zaten derlenir).
    /// İki çağıran: koşu içinde kanıtsız biten proje (<c>RunCoordinator.InvalidateBuildStateOnFailure</c>) ve
    /// açılıştaki çökme kurtarması (<see cref="InFlightLedger.Recover"/>). Okuma ile yazma aynı kilit
    /// altındadır: eşzamanlı bir <see cref="Upsert"/> araya giremez.</para>
    /// </summary>
    public void InvalidateWithoutEvidence(string projectId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Write(map =>
        {
            if (!map.TryGetValue(projectId, out var existing)) return false; // kayıt yok ⇒ hiçbir şey açılmaz
            map[existing.ProjectId] = existing with
            {
                LastResult = BuildResult.Failed,
                LastRunAt = now,
                FailedSignature = null,
                FailedAt = null,
            };
            return true;
        });
    }

    /// <summary>Defterin TEK yazma yolu: kilit → oku → değiştir → geçici dosya → atomik rename. <paramref
    /// name="mutate"/> <c>false</c> derse (değişen bir şey yok) dosyaya hiç dokunulmaz.</summary>
    private void Write(Func<Dictionary<string, BuildState>, bool> mutate)
    {
        _writeGate.Wait();
        try
        {
            // Load() zaten ignore-case dedup edilmiş bir map döner (yukarıdaki [Review Important 1] fix'i); bu
            // kopya sadece ilgili anahtarı merge eder, ayrıca bir case-collision riski taşımaz.
            var map = new Dictionary<string, BuildState>(Load(), StringComparer.OrdinalIgnoreCase);
            if (!mutate(map)) return;
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(map, Json), EffectiveRenameRetryDelay);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// [clean] Verilen workspace kökü ALTINDAKİ tüm kayıtları kaldırır ve kaldırılan sayıyı döner — Clean'in
    /// "build state reset" adımı. Dosya GLOBALDİR (birden çok workspace aynı <c>build-state.json</c>'ı
    /// paylaşır), bu yüzden dosyanın kendisi SİLİNMEZ: yalnız <paramref name="rootPath"/> öneki taşıyan
    /// anahtarlar çıkar. Önek normalizasyonu ve prefix tuzağı <see cref="RootScope"/>'un işidir. Silinmiş /
    /// yeniden adlandırılmış projelerin artık kayıtları da bu süpürmeye takılır.
    /// <para>Eşleşme yoksa dosyaya HİÇ dokunulmaz (yazım yok, rename yarışı yok) — <see cref="Write"/>'ın
    /// "değişen bir şey yok" sözleşmesi. Bozuk yol ya da okunamaz dosya fırlatmaz, 0 döner — <see cref="Load"/>'un
    /// never-throw sözleşmesiyle aynı çizgi.</para>
    /// </summary>
    public int RemoveUnderRoot(string rootPath)
    {
        if (RootScope.NormalizeRoot(rootPath) is not { } prefix) return 0; // bozuk yol → Clean akışı durmaz

        int removed = 0;
        Write(map =>
        {
            var doomed = map.Keys.Where(k => RootScope.Contains(prefix, k)).ToList();
            foreach (string key in doomed) map.Remove(key);
            removed = doomed.Count;
            return removed > 0; // 0 ⇒ Write dosyayı YENİDEN YAZMAZ — dokunulmamış kalır
        });
        return removed;
    }

    /// <summary>
    /// [optimize] Kök altındaki ÖLÜ kayıtları budar: anahtarı (csproj yolu) artık diskte olmayan girdiler
    /// gider, kaldırılan sayı döner. <see cref="RemoveUnderRoot"/>'tan AYRI bir semantiktir — o kök altındaki
    /// HER kaydı siler (Clean'in "sıfırla"sı), bu yalnız karşılığı kaybolmuş olanı (Optimize'ın hijyeni).
    /// Diri kayıtlara dokunulmaz, dolayısıyla hiçbir projenin build kararı değişmez.
    /// <para>Kök dışındaki kayıtlar (başka workspace'ler, worktree yollu girdiler) korunur; budanacak bir şey
    /// yoksa dosya YENİDEN YAZILMAZ — <see cref="Write"/>'ın sözleşmesi.</para>
    /// </summary>
    public int PruneMissingUnderRoot(string rootPath)
    {
        if (RootScope.NormalizeRoot(rootPath) is not { } prefix) return 0;

        int removed = 0;
        Write(map =>
        {
            var dead = map.Keys.Where(k => RootScope.Contains(prefix, k) && !File.Exists(k)).ToList();
            foreach (string key in dead) map.Remove(key);
            removed = dead.Count;
            return removed > 0;
        });
        return removed;
    }

    /// <summary>[optimize] Yarım kalmış atomik yazımlardan kalan kendi <c>.tmp</c> artıklarını süpürür
    /// (bkz. <see cref="TempFileSweeper"/>); silinen sayıyı döner.</summary>
    public int SweepOrphanTempFiles(TimeSpan olderThan) => TempFileSweeper.Sweep(_path, olderThan, UtcNow);

    /// <summary>[D8] Süpürme eşiğinin okuduğu saat — testte ileri alınır, üretimde <c>null</c>.</summary>
    internal Func<DateTime>? UtcNow { get; set; }


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
