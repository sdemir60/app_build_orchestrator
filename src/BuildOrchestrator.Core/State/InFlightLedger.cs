using System.Text.Json;

namespace BuildOrchestrator.Core.State;

/// <summary>
/// [spec 2026-09-18 §5.5 · karar 12] Uçuştaki projelerin defteri: <c>&lt;cacheRoot&gt;\run-inflight.json</c>.
/// Supervisor bir projeyi dispatch ettiği an buraya yazar, sonucu raporlanınca siler, koşunun her çıkışında
/// dosyayı boşaltır. Motor koşu ortasında ölürse (çökme, Görev Yöneticisi, kapanan oturum) dosya dolu kalır ve bir
/// sonraki açılışta <see cref="Recover"/> listedeki her projeyi kanıtsız hata olarak geçersizler — yarım yazılmış
/// taze bir çıktı zaman kipine giremez.
///
/// <para><b>Bellek + dosya aynası:</b> gerçek küme bellektedir; her <see cref="Add"/>/<see cref="Remove"/> dosyayı
/// baştan (atomik, <see cref="AtomicFile"/>) yazar — liste en fazla paralellik kadar kısadır. Worker'lar eşzamanlı
/// koştuğu için küme ve yazım TEK kilit altındadır: dosya hiçbir an kümenin eski bir hâlini üzerine yazamaz.</para>
///
/// <para>§4: yalnız bu JSON dosyasına I/O yapar; DLL/bin/obj asla okunmaz.</para>
/// </summary>
public sealed class InFlightLedger
{
    /// <summary>Defter dosyasının adı — TEK tanım; <c>build-state.json</c>'ın yanında durur.</summary>
    public const string FileName = "run-inflight.json";

    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);

    public InFlightLedger(string cacheRoot) => _path = Path.Combine(cacheRoot, FileName);

    /// <summary>Dosyanın tam yolu (tanı ve testler için).</summary>
    public string FilePath => _path;

    /// <summary>
    /// [D8] Atomik rename retry'ının gecikme dikişi — <see cref="BuildStateStore.RenameRetryDelay"/> ile aynı desen.
    /// Üretimde null → <see cref="BuildStateStore.DefaultRenameRetryDelay"/> (üretim backoff'unun tek sahibi).
    /// </summary>
    internal Action<int>? RenameRetryDelay { get; set; }

    /// <summary>Proje dispatch edildi — dosyaya yazılır. Zaten listedeyse (SCC'nin sonraki turu) dosyaya dokunulmaz.
    /// I/O hatası çağırana yayılır; koşuyu durdurup durdurmamak çağıranın kararıdır.</summary>
    public void Add(string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        lock (_gate)
        {
            if (_ids.Add(projectId)) WriteLocked();
        }
    }

    /// <summary>Projenin sonucu raporlandı — dosyadan silinir. Listede yoksa dosyaya dokunulmaz.</summary>
    public void Remove(string projectId)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        lock (_gate)
        {
            if (_ids.Remove(projectId)) WriteLocked();
        }
    }

    /// <summary>Koşu bitti (her çıkış) — küme boşalır, dosya silinir. Uçuşta kimse kalmadığı için "uçuşta ölen"
    /// de kalmaz; raporlanmadan kalmış bir satır bir sonraki açılışta boşuna geçersizleme yapardı.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _ids.Clear();
            File.Delete(_path); // dosya yoksa no-op
        }
    }

    /// <summary>
    /// Diskteki listeyi okur. Dosya yok / boş / bozuk → boş liste, ASLA fırlatmaz.
    /// </summary>
    public IReadOnlyList<string> ReadListed() => TryReadListed(out var ids) ? ids : [];

    /// <summary>
    /// Açılış kurtarması: dosyadaki her proje <see cref="BuildStateStore.InvalidateWithoutEvidence"/> ile kanıtsız
    /// hata olur (kaydı olmayan için no-op), dosya silinir, listelenen id'ler döner.
    /// </summary>
    public IReadOnlyList<string> Recover(BuildStateStore stateStore, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        lock (_gate)
        {
            if (!TryReadListed(out var listed))
            {
                // Bozuk dosyada kimin uçuşta olduğu BİLİNMEZ — kurtarma uydurulmaz; dosya gider ki bir sonraki
                // açılış aynı bozuk dosyaya takılmasın.
                File.Delete(_path);
                return [];
            }
            // Dosya ANCAK her kayıt yazıldıktan sonra silinir: defter yazımı patlarsa istisna çağırana (Program,
            // uyarı) yayılır ve dosya yerinde kalır — bir sonraki açılış aynı listeyle yeniden dener (geçersizleme
            // idempotenttir). Bilinen sınır: bu motor ömründe bir koşu dispatch ederse ilk Add dosyayı bellekteki
            // kümeyle baştan yazar ve kalan satırlar gider.
            foreach (string id in listed) stateStore.InvalidateWithoutEvidence(id, now);
            File.Delete(_path);
            return listed;
        }
    }

    /// <summary>Kümenin TAMAMINI dosyaya yazar (kilit altında çağrılır); küme boşsa dosya silinir.</summary>
    private void WriteLocked()
    {
        if (_ids.Count == 0) { File.Delete(_path); return; }
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_ids.ToList()),
            RenameRetryDelay ?? BuildStateStore.DefaultRenameRetryDelay);
    }

    /// <summary>Dosyayı ayrıştırır. <c>false</c> ⇒ dosya okunamadı ya da bozuk (içerik güvenilmez).</summary>
    private bool TryReadListed(out IReadOnlyList<string> ids)
    {
        ids = [];
        if (!File.Exists(_path)) return true;
        try
        {
            string text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text)) return true;
            var list = JsonSerializer.Deserialize<List<string>>(text);
            if (list is null || list.Any(string.IsNullOrWhiteSpace)) return false;
            ids = list;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
