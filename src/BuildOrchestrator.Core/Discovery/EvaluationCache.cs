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
    /// aşarsa — savunmanın ikinci katı). Erişilemeyen dosya için THROW ETMEZ — bu tolerans yalnız
    /// ilk <see cref="FileInfo"/> okumasını değil, metodun TÜM gövdesini (hash hesaplama +
    /// <paramref name="evaluate"/> çağrısı dahil) kapsar, çünkü dosya <c>info.Exists</c> geçtikten
    /// SONRA da (Hash veya evaluate sırasında) kaybolabilir: daha önce cache'e girmişse mevcut
    /// girdi AYNEN döner (bu "yeniden değerlendir" DEĞİL — kalıcı bir dosya gerçekten silinmişse
    /// bir sonraki Sync onu zaten görmez); hiç girmemişse <c>evaluate</c> çağrısı tamamlanmadan
    /// (veya tamamlanamadan) <c>null</c> döner (girdi güvenle atlanır).
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
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            // Dosya pipeline'ın HERHANGİ bir aşamasında kayboldu (info okuma, Hash, ya da evaluate
            // içindeki XDocument.Load) — aynı tolerans. Kalıcı/malformed bir csproj'un fırlattığı
            // XmlException gibi diğer istisnalar buraya YAKALANMAZ, olduğu gibi yukarı sızar.
            return Stale();
        }
    }

    public void Flush() // atomik temp+rename [D2/D8]
    {
        string tmp = cachePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Json));
        File.Move(tmp, cachePath, overwrite: true);
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
