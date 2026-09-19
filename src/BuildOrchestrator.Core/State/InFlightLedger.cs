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
    // [Task 10 fix I2] Açılış kurtarmasının defter yazımında PATLADIĞI satırlar. Dosya bellekteki kümeyle baştan
    // yazıldığı için bunlar ayrıca tutulmasa bu motor ömründeki ilk Add onları dosyadan silerdi; her yazım
    // _ids ∪ _unrecovered yazar, Clear yalnız _ids'i boşaltır. RetryRecovery başarıyla geçersizlediğini düşürür.
    private readonly HashSet<string> _unrecovered = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Koşu bitti (her çıkış) — küme boşalır; kurtarılamamış satır yoksa dosya silinir, varsa yalnız onlarla
    /// yeniden yazılır. Uçuşta kimse kalmadığı için "uçuşta ölen" de kalmaz; raporlanmadan kalmış bir satır bir
    /// sonraki açılışta boşuna geçersizleme yapardı.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _ids.Clear();
            WriteLocked();
        }
    }

    /// <summary>
    /// Diskteki listeyi okur (tanı ve testler için). Dosya yok / boş / bozuk / okunamıyor → boş liste, ASLA fırlatmaz.
    /// </summary>
    public IReadOnlyList<string> ReadListed()
    {
        try { return TryReadListed(out var ids) ? ids : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Açılış kurtarması: dosyadaki her proje <see cref="BuildStateStore.InvalidateWithoutEvidence"/> ile kanıtsız
    /// hata olur (kaydı olmayan için no-op), dosya silinir, listelenen id'ler döner.
    ///
    /// <para><b>Hata sözleşmesi [Task 10 fix M4/I2]:</b> yalnız AYRIŞTIRILAMAYAN içerik bozuktur — kurtarma
    /// uydurulmaz, dosya silinir, boş liste döner. Dosya OKUNAMIYORSA (kilit, izin) ya da defter yazımı patlarsa
    /// istisna çağırana (<c>Program</c>'ın uyarı dalı) yayılır ve dosya yerinde kalır: bir sonraki açılış yeniden
    /// dener. Yazımda patlayan satırlar bellekte de tutulur (<see cref="_unrecovered"/>), bu motor ömründeki
    /// yazımlar onları dosyadan düşürmez ve <see cref="RetryRecovery"/> koşu başında yeniden dener.</para>
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
            _unrecovered.UnionWith(listed);
            InvalidateUnrecoveredLocked(stateStore, now);
            return listed;
        }
    }

    /// <summary>
    /// [Task 10 fix I2] Açılışta kurtarılamamış satırları yeniden dener — koşu başında, PLANLAMADAN önce çağrılır:
    /// kesilmiş projenin kaydı geçersizlenmeden planlanırsa yarım çıktısı "güncel" sayılabilirdi. Kurtarılacak bir
    /// şey yoksa dosyaya dokunmaz. Hata yine çağırana yayılır; kalanlar bir sonraki denemeye kalır.
    /// </summary>
    public void RetryRecovery(BuildStateStore stateStore, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        lock (_gate)
        {
            if (_unrecovered.Count == 0) return;
            InvalidateUnrecoveredLocked(stateStore, now);
        }
    }

    /// <summary>Kurtarılamamışları tek tek geçersizler; her başarı kümeden düşer. Hepsi bitince dosya bellekteki
    /// kümeyle yeniden yazılır (uçuşta kimse yoksa silinir). Yarıda patlarsa dosyaya dokunulmaz — eski liste bir
    /// üst kümedir ve geçersizleme idempotenttir.</summary>
    private void InvalidateUnrecoveredLocked(BuildStateStore stateStore, DateTimeOffset now)
    {
        foreach (string id in _unrecovered.ToList())
        {
            stateStore.InvalidateWithoutEvidence(id, now);
            _unrecovered.Remove(id);
        }
        WriteLocked();
    }

    /// <summary>Uçuştakiler ∪ kurtarılamamışlar kümesinin TAMAMINI dosyaya yazar (kilit altında çağrılır); ikisi de
    /// boşsa dosya silinir.</summary>
    private void WriteLocked()
    {
        var all = _ids.Union(_unrecovered, StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count == 0) { File.Delete(_path); return; }
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(all),
            RenameRetryDelay ?? BuildStateStore.DefaultRenameRetryDelay);
    }

    /// <summary>Dosyayı ayrıştırır. <c>false</c> ⇒ içerik bozuk (güvenilmez). Okuma hatası (kilit, izin) BOZUKLUK
    /// DEĞİLDİR ve çağırana yayılır [Task 10 fix M4]. Okuma Delete-share'lidir (<see cref="AtomicFile"/>).</summary>
    private bool TryReadListed(out IReadOnlyList<string> ids)
    {
        ids = [];
        if (!File.Exists(_path)) return true;
        string text = AtomicFile.ReadAllTextSharingDelete(_path);
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(text);
            if (list is null || list.Any(string.IsNullOrWhiteSpace)) return false;
            ids = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
