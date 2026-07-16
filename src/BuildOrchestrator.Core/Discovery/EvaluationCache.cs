using System.Security.Cryptography;
using System.Text.Json;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// mtime+hash tabanlı csproj evaluation cache'i: warm Sync'te değişmeyen projeler
/// yeniden değerlendirilmez. Hızlı yol mtime karşılaştırması; mtime değişse de içerik
/// aynıysa (ör. touch) hash doğrulamasıyla gereksiz evaluate önlenir.
/// </summary>
public sealed class EvaluationCache(string cachePath)
{
    private sealed record Entry(long MtimeTicks, string Hash, EvaluatedProject Project);
    private readonly Dictionary<string, Entry> _entries = Load(cachePath);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public EvaluatedProject GetOrEvaluate(string csprojPath, Func<string, EvaluatedProject> evaluate)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        long mtime = new FileInfo(csprojPath).LastWriteTimeUtc.Ticks;
        if (_entries.TryGetValue(csprojPath, out var e))
        {
            if (e.MtimeTicks == mtime) return e.Project;                 // hızlı yol: mtime eşit
            if (Hash(csprojPath) is var h && h == e.Hash)                // mtime kaydı ama içerik aynı
            { _entries[csprojPath] = e with { MtimeTicks = mtime }; return e.Project; }
        }
        var proj = evaluate(csprojPath);
        _entries[csprojPath] = new Entry(mtime, Hash(csprojPath), proj);
        return proj;
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
        catch { return new(StringComparer.OrdinalIgnoreCase); } // bozuk cache → yeniden kur
    }
}
