using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>[files-in-use] Kenara taşınmış bir dosya: asıl yolu ve taşındığı yol.</summary>
public sealed record StashedFile(string OriginalPath, string StashPath);

/// <summary>[files-in-use] Bir süpürmenin sonucu: silinen, geri konan ve hâlâ tutulduğu için kalan dosya sayısı.</summary>
public sealed record StashSweep(int Removed, int Restored, int Kept);

/// <summary>
/// [files-in-use] Aracın çıktı klasörlerindeki TEK mutasyon yüzeyi (CLAUDE.md "OutDir'e dokunulmaz"ın kullanıcı onaylı istisnası):
/// bir projenin kendi kopya komutunun üzerine yazamadığı, başka bir programın tuttuğu dosyayı yanındaki gizli <see cref="FolderName"/>
/// klasörüne TAŞIR, gerekirse geri koyar ve kendi taşıdığını kilidi kalkınca siler. Araç burada hiçbir şey KOPYALAMAZ ve başka hiçbir
/// dosyaya dokunmaz — klasörde desene uymayan dosya süpürmede de olduğu yerde kalır.
/// <para><b>Neden taşımak:</b> Windows görüntü olarak yüklenmiş bir DLL'in (ve debugger'ın tuttuğu PDB'nin) üzerine yazdırmaz ama aynı
/// birimde yeniden adlandırılmasına izin verir; tutan program taşınan dosyayla çalışmayı sürdürür. Taşıma klasörü hedefin İÇİNDEDİR —
/// aynı birim garantisi oradan gelir.</para>
/// <para><b>Ad deseni</b> <c>&lt;asıl ad&gt;~&lt;16 küçük hex&gt;.pending|old</c>: <c>.pending</c> sonucu henüz belli olmayan taşımadır
/// (yeniden deneme koşuyor); sonuç belli olunca <see cref="Settle"/> onu <c>.old</c> yapar ya da <see cref="Restore"/> geri koyar.
/// Uzantı <c>.dll</c> ile bitmez; hiçbir yükleyici taşınan dosyayı bulmaz.</para>
/// <para><b>Kayıt</b> (<see cref="RecordFileName"/>, motorun durum klasöründe): taşıma klasörü açılan hedef klasörlerin listesi — taşımadan
/// ÖNCE yazılır, böylece motor taşımayla sonuç arasında ölse de <see cref="Sweep"/> o klasörü bulur. Kayıt yazılamazsa taşıma yine yapılır
/// (o klasör yalnız süpürülmez).</para>
/// </summary>
public sealed partial class InUseStash(string recordPath)
{
    /// <summary>Taşıma klasörünün adı — hedef klasörün içinde, gizli.</summary>
    public const string FolderName = ".in-use";

    /// <summary>Kaydın dosya adı (motorun durum klasöründe).</summary>
    public const string RecordFileName = "in-use-folders.json";

    private const string Pending = "pending";
    private const string Settled = "old";

    [GeneratedRegex(@"^(?<name>.+)~(?<id>[0-9a-f]{16})\.(?<state>pending|old)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryName();

    private readonly string _recordPath = recordPath ?? throw new ArgumentNullException(nameof(recordPath));
    private readonly object _gate = new();

    /// <summary>
    /// <paramref name="path"/>'i yanındaki taşıma klasörüne <c>.pending</c> olarak taşır. Taşınamazsa (silmeyi paylaşmayan bir tutamaç,
    /// izin) <c>null</c> döner ve dosya yerinde kalır; bu çağrının açtığı boş taşıma klasörü de kalmaz.
    /// </summary>
    public StashedFile? MoveAside(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) is not { } folder) return null;
        string parked = Path.Combine(folder, FolderName);
        bool created = !Directory.Exists(parked);
        try
        {
            Remember(folder);
            var info = Directory.CreateDirectory(parked);
            if (!info.Attributes.HasFlag(FileAttributes.Hidden)) info.Attributes |= FileAttributes.Hidden;
            string target = Path.Combine(parked, $"{Path.GetFileName(full)}~{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.{Pending}");
            File.Move(full, target);
            return new StashedFile(full, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (created) TryRemoveIfEmpty(parked);
            return null;
        }
    }

    /// <summary>Taşımanın sonucu belli: dosya <c>.old</c> olur ve kilidi kalkınca süpürmeyle silinir.</summary>
    public void Settle(StashedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.StashPath.EndsWith("." + Pending, StringComparison.Ordinal)) return;
        try
        {
            File.Move(file.StashPath, file.StashPath[..^Pending.Length] + Settled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // .pending kalır: süpürme dolu yuvada onu siler, boş yuvada geri koyar — ikisi de doğru.
        }
    }

    /// <summary>Yuva boşsa dosyayı yerine geri koyar (<c>true</c>); yuva doluysa ya da geri konamazsa dosya <c>.old</c> olur (<c>false</c>).</summary>
    public bool Restore(StashedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            if (!File.Exists(file.OriginalPath))
            {
                File.Move(file.StashPath, file.OriginalPath);
                if (Path.GetDirectoryName(file.StashPath) is { } parked) TryRemoveIfEmpty(parked);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Yuva bu arada doldu ya da taşıma reddedildi: aşağıda .old olur.
        }
        Settle(file);
        return false;
    }

    /// <summary>
    /// Kayıttaki her klasörün taşıma klasörünü süpürür: kilidi kalkmış dosyayı siler, <c>.pending</c> olup yuvası boş olanı geri koyar
    /// (sonucu yazılamadan kalmış taşıma), hâlâ tutulanı bırakır; boşalan taşıma klasörünü ve kayıttaki satırını kaldırır. Koşu başında,
    /// hiçbir işçi taşıma yapmıyorken çağrılır. Hiçbir koşulda fırlatmaz; bozuk kayıt boş sayılır.
    /// </summary>
    public StashSweep Sweep()
    {
        lock (_gate)
        {
            int removed = 0, restored = 0, kept = 0;
            var folders = ReadRecord();
            var remaining = new List<string>();
            foreach (string folder in folders)
            {
                string parked = Path.Combine(folder, FolderName);
                if (!Directory.Exists(parked)) continue;
                foreach (string entry in FilesIn(parked))
                {
                    var match = EntryName().Match(Path.GetFileName(entry));
                    if (!match.Success) continue;
                    string original = Path.Combine(folder, match.Groups["name"].Value);
                    if (match.Groups["state"].Value == Pending && !File.Exists(original) && TryMove(entry, original))
                    {
                        restored++;
                        continue;
                    }
                    if (TryDelete(entry)) removed++;
                    else kept++;
                }
                if (!TryRemoveIfEmpty(parked)) remaining.Add(folder);
            }
            if (!remaining.SequenceEqual(folders, StringComparer.OrdinalIgnoreCase)) TryWriteRecord(remaining);
            return new StashSweep(removed, restored, kept);
        }
    }

    private void Remember(string folder)
    {
        lock (_gate)
        {
            var folders = ReadRecord();
            if (folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
            TryWriteRecord([.. folders, folder]);
        }
    }

    private List<string> ReadRecord()
    {
        try
        {
            if (!File.Exists(_recordPath)) return [];
            var folders = JsonSerializer.Deserialize<List<string>>(AtomicFile.ReadAllTextSharingDelete(_recordPath));
            return folders?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void TryWriteRecord(IReadOnlyList<string> folders)
    {
        try
        {
            if (folders.Count == 0)
            {
                if (File.Exists(_recordPath)) File.Delete(_recordPath);
                return;
            }
            AtomicFile.WriteAllText(_recordPath, JsonSerializer.Serialize(folders), BuildStateStore.DefaultRenameRetryDelay);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kayıt yazılamadı: taşıma yine yapılır, o klasör yalnız bir sonraki süpürmede görülmez.
        }
    }

    private static IReadOnlyList<string> FilesIn(string folder)
    {
        try
        {
            return Directory.GetFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Klasör boşsa siler (<c>true</c>); dolu ya da silinemezse <c>false</c>. Klasör zaten yoksa <c>true</c>.</summary>
    private static bool TryRemoveIfEmpty(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return true;
            if (Directory.EnumerateFileSystemEntries(folder).Any()) return false;
            Directory.Delete(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
