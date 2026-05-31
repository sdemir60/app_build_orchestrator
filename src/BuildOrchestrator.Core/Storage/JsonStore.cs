using System.Text.Json;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Storage;

/// <summary>
/// Small atomic JSON file store. Writes to a temp file then moves into place to avoid
/// partially-written files if the process is killed mid-write (Section 6.1 robustness).
/// </summary>
public sealed class JsonStore
{
    private readonly object _gate = new();

    public T? Read<T>(string path) where T : class
    {
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<T>(stream, ProtocolJson.Options);
            }
            catch (JsonException)
            {
                // Corrupt cache is treated as "absent" so the app can self-heal via Sync.
                return null;
            }
        }
    }

    public void Write<T>(string path, T value)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
            {
                JsonSerializer.Serialize(stream, value, ProtocolJson.Options);
            }

            // Atomic replace where supported.
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
