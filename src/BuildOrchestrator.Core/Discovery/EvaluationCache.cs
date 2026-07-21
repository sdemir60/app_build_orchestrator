using System.Security.Cryptography;
using System.Text.Json;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// mtime+size+hash tabanlı csproj evaluation cache'i: warm Sync'te değişmeyen projeler
/// yeniden değerlendirilmez. Hızlı yol mtime+size karşılaştırması (MSBuild incremental
/// yaklaşımı) — NTFS'te değişen bir dosya eski dosyayla aynı mtime tick'ine sahip olabildiği
/// için (ör. git checkout/pull sonrası) yalnız mtime yetersizdir; size ikinci ucuz sinyaldir.
/// mtime veya size değişse de içerik aynıysa (ör. touch) hash doğrulamasıyla gereksiz
/// evaluate önlenir.
/// </summary>
public sealed class EvaluationCache(string cachePath)
{
    private sealed record Entry(long MtimeTicks, long Length, string Hash, EvaluatedProject Project);
    private readonly Dictionary<string, Entry> _entries = Load(cachePath);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>
    /// Canlı build ↔ scan yarışı [Task 0/It-4a]: scanner bir .csproj'u bulduktan sonra bu çağrı
    /// gerçekleşene kadar dosya kaybolabilir (ör. WPF wpftmp geçici projesi scanner filtresini
    /// aşarsa — savunmanın ikinci katı). KAYBOLAN dosya için THROW ETMEZ — bu tolerans yalnız
    /// ilk <see cref="FileInfo"/> okumasını değil, metodun TÜM gövdesini (hash hesaplama +
    /// <paramref name="evaluate"/> çağrısı dahil) kapsar, çünkü dosya <c>info.Exists</c> geçtikten
    /// SONRA da (Hash veya evaluate sırasında) kaybolabilir: daha önce cache'e girmişse mevcut
    /// girdi AYNEN döner (bu "yeniden değerlendir" DEĞİL — kalıcı bir dosya gerçekten silinmişse
    /// bir sonraki Sync onu zaten görmez); hiç girmemişse <c>evaluate</c> çağrısı tamamlanmadan
    /// (veya tamamlanamadan) <c>null</c> döner (girdi güvenle atlanır).
    ///
    /// <para><b>[Final review I-3] Tolerans "her IO hatası" DEĞİLDİR:</b> dosya VAR ama okunamıyorsa (kilit/
    /// paylaşım ihlali, ağ/disk hatası) istisna YUKARI SIZAR — yutulsaydı proje sessizce build plan'ından
    /// düşer ve build eksik graph ile koşardı. Malformed csproj'un <c>XmlException</c>'ı da sızar.</para>
    /// </summary>
    public EvaluatedProject? GetOrEvaluate(string csprojPath, Func<string, EvaluatedProject> evaluate)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        EvaluatedProject? Stale() => _entries.TryGetValue(csprojPath, out var s) ? s.Project : null;

        try
        {
            var info = new FileInfo(csprojPath);
            if (!info.Exists) return Stale();
            long mtime = info.LastWriteTimeUtc.Ticks;
            long length = info.Length;

            if (_entries.TryGetValue(csprojPath, out var e))
            {
                if (e.MtimeTicks == mtime && e.Length == length) return e.Project; // hızlı yol: mtime+size eşit
                if (Hash(csprojPath) is var h && h == e.Hash)                      // mtime/size farklı ama içerik aynı
                { _entries[csprojPath] = e with { MtimeTicks = mtime, Length = length }; return e.Project; }
            }
            var proj = evaluate(csprojPath);
            _entries[csprojPath] = new Entry(mtime, length, Hash(csprojPath), proj);
            return proj;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                   || (ex is IOException && !File.Exists(csprojPath)))
        {
            // Tolerans YALNIZ "dosya kayboldu" yarışına özgüdür [final review I-3]: FileNotFound/
            // DirectoryNotFound zaten tam olarak bu sinyaldir; genel bir IOException ise yalnız dosya
            // GERÇEKTEN ortada yoksa yutulur. Kayıp pipeline'ın HERHANGİ bir aşamasında olabilir (info
            // okuma, Hash, ya da evaluate içindeki XDocument.Load) — hepsi aynı tolerans.
            //
            // VAR OLAN ama okunamayan bir csproj (editör kilidi/paylaşım ihlali, ağ yolu hıçkırığı, disk
            // hatası) buraya DÜŞMEZ: yutulsaydı proje sessizce build plan'ından düşer ve build EKSİK
            // graph ile koşardı (BuildPlanBuilder .OfType<EvaluatedProject>() / Supervisor .Where(is not
            // null) null'ı sessizce eler). Kalıcı/malformed bir csproj'un XmlException'ı da — eskiden
            // olduğu gibi — yakalanmaz, olduğu gibi yukarı sızar.
            return Stale();
        }
    }

    /// <summary>
    /// Cache'i diske yazar: atomik temp+rename [D2/D8].
    ///
    /// <para><b>[Fix wave 1 — Finding 3] Eşzamanlılık altında ASLA FIRLATMAZ.</b> Aynı cache yoluna iki
    /// <see cref="EvaluationCache"/> ÖRNEĞİ paralel flush edebilir (koşan bir run'ın planner thread'i +
    /// eşzamanlı dispatch edilen bir Sync; ikisi de kendi örneğini kurar). Eski kod SABİT bir <c>.tmp</c> adı
    /// kullanıyordu — iki yazıcı aynı geçici dosyada çakışıyor (IOException) ya da rename hedefte paylaşım
    /// ihlaline düşüyordu (UnauthorizedAccessException). İstisna, <c>BuildPlanBuilder.Build</c> üzerinden
    /// Sync'in kendi try'ının DIŞINDAN IPC sınırına kadar çıkıp TÜM Sync'i <c>planFailed</c>'a çeviriyordu.
    /// İki savunma: (1) geçici ad ÖRNEK BAŞINA tekil (çakışma imkânsız), (2) IO hatası YUTULUR.</para>
    ///
    /// <para>Yutmak güvenlidir çünkü bu cache SALT bir optimizasyondur ve <see cref="Load"/> hem YOK olan hem
    /// BOZUK bir dosyayı zaten tolere eder (boş map ile devam) — düşen bir flush'ın bedeli, bir sonraki
    /// taramada yeniden değerlendirilecek csproj'lardır; kaybolan bir Sync değil.</para>
    /// </summary>
    public void Flush()
    {
        // Tekil temp adı: BuildStateStore.Upsert ile AYNI desen (`<path>.<guid>.tmp`).
        string tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Json));
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Öksüz temp dosyası bırakma (her başarısız flush diskte çöp biriktirirdi); temizliğin kendisi de
            // best-effort'tur — zaten yutulmuş bir hatanın üstüne yeni bir hata fırlatmak anlamsız olurdu.
            try { File.Delete(tmp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Dictionary<string, Entry> Load(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Json);
            return d is null ? new(StringComparer.OrdinalIgnoreCase) : new(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(StringComparer.OrdinalIgnoreCase); } // bozuk cache → yeniden kur
    }
}
