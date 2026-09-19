using System.Linq;

namespace BuildOrchestrator.Contracts.Model;

// It-1 domain DTO'ları — A9 şeklini sabitler. Core → Contracts referansı üzerinden Core bu tipleri üretir.
// It-3: depIssues (ProjectSucceededEvent/ProjectFailedEvent) ve RunRequest.mode genişlemesi (Build/RetryFailed,
// DependentMode) artık IpcMessages.cs'de sabit; BranchRef git-yüzeyi DTO'su burada.

public enum HintPathClass { Edge, ExternalThirdParty, ExternalOsysPlatform, Unclassified }
public enum BuildResult { Succeeded, Failed, Skipped }

public sealed record SolutionRef(string Name, string Path);

// Ham HintPath + basename + sınıf + (varsa) üretici projectId
public sealed record HintPathRef(string Raw, string BaseName, HintPathClass Class, string? ProducerProjectId);

public sealed record ProjectNode(
    string Id,                              // kanonik tam csproj yolu
    string Name,                            // AssemblyName türevi kısa ad
    string ProjectPath,                     // == Id (A9 şekli)
    IReadOnlyList<string> SolutionNames,    // T32
    IReadOnlyList<string> Dependencies,     // üretici projectId'ler (graf kenarları, deduped, sıralı)
    int BuildOrder,
    int? LayerIndex,
    string? LayerName,
    bool InCycle,
    bool? WillBuild,                        // T53: dirty=true, güncel=false, imza-yok/pre-Sync=null
    // WillBuild'in GEREKÇESİ (kullanıcıya gösterilir). Alan SONA ve default'lu: eski NDJSON/plan üreticileri
    // onu yazmaz ve null olarak çözülür — o hâlde yüzey jenerik metne düşer.
    WillBuildReason? WillBuildReason = null,
    // [Harici projeler] Bu düğüm ana repo DIŞINDAN gelen bir kökten mi geldi. Ayrı bir düğüm tipi AÇILMAZ:
    // hariciler sıradan ProjectNode olarak akar, böylece liste gruplaması, graf bandı ve filtreler onları
    // bedavaya taşır — rozet yalnız bu alandan okunur.
    bool IsExternal = false)
{
    // Derleyicinin ürettiği record eşitliği, IReadOnlyList<string> alanlarında EqualityComparer<T>.Default
    // kullanır; List<string> Equals'ı override etmediği için bu referans eşitliğine düşer (JSON round-trip
    // sonrası her zaman farklı liste örneği). SolutionNames/Dependencies için sıralı içerik eşitliği gerekli.
    public bool Equals(ProjectNode? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && ProjectPath == other.ProjectPath
        && SolutionNames.SequenceEqual(other.SolutionNames)
        && Dependencies.SequenceEqual(other.Dependencies)
        && BuildOrder == other.BuildOrder
        && LayerIndex == other.LayerIndex
        && LayerName == other.LayerName
        && InCycle == other.InCycle
        && WillBuild == other.WillBuild
        && WillBuildReason == other.WillBuildReason
        && IsExternal == other.IsExternal;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(ProjectPath);
        foreach (string s in SolutionNames) hash.Add(s);
        foreach (string d in Dependencies) hash.Add(d);
        hash.Add(BuildOrder);
        hash.Add(LayerIndex);
        hash.Add(LayerName);
        hash.Add(InCycle);
        hash.Add(WillBuild);
        hash.Add(IsExternal);
        return hash.ToHashCode();
    }
}

/// <param name="Nodes">Build-order'da. Katman ataması uygulanmışsa sert faz bariyeri bu sırayı TOPOLOJİK
/// OLMAYAN bir hâle getirebilir (warn-only tasarım, bkz. <c>LayerEngine</c>) — planı tüketen algoritmalar
/// sıra-bağımsız olmalıdır [A1].</param>
/// <param name="Cycles">Her biri bir SCC (&gt;1 üye), üyeler sıralı.</param>
/// <param name="LayerWarnings">[A1/T15] <c>LayerEngine</c>'ın ürettiği ters-katman uyarıları (warn-only DATA:
/// hiçbir alan bunları okuyup bloklama/yeniden sıralama yapmaz — yalnız kullanıcıya gösterilir). Katman
/// ataması çalışmadıysa boş; planı doğrudan kuran (katmandan habersiz) yollarda null.</param>
/// <param name="ProducerWarnings">Aynı <c>AssemblyName</c>'i üreten proje çiftleri için hazır uyarı satırları
/// (warn-only DATA, <see cref="LayerWarnings"/> ile AYNI sözleşme). Belirsiz DLL kenar üretmez, yani bu bir
/// SESSİZ kenar kaybıdır ve kullanıcıya ulaşmak zorundadır. Çakışma yoksa boş.</param>
public sealed record BuildPlan(
    IReadOnlyList<ProjectNode> Nodes,
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    string Configuration,
    IReadOnlyList<string>? LayerWarnings = null,
    IReadOnlyList<string>? ProducerWarnings = null);

/// <summary>[T15][N8] Katman ataması config'i: sıralı regex+isim. Order ÇİFT görev görür — (1) eşleşme
/// önceliği (LayerEngine, küçük Order'ı önce dener, ilk eşleşen kazanır), (2) eşleşen projelere atanan
/// katmanın LayerIndex'i (bu pattern = "Order numaralı katmana şu regex'e uyanlar girer"). Regex,
/// ProjectNode.Name'e (AssemblyName türevi kısa ad) karşı denenir — Id (tam csproj yolu) değil.</summary>
public sealed record LayerPattern(int Order, string Regex, string Name);

/// <summary>
/// Bir projenin NEDEN derleneceği (ya da derlenmeyeceği). Karar <c>WillBuildEvaluator</c>'da tek gövdede
/// verilir; bu tip onu kullanıcıya taşır.
///
/// <para>Var olma sebebi: nokta (plan) ile kartın sha çifti (commit) AYRI kanallardır ve yan yana
/// durduklarında "commit aynı ama neden derlenecek?" diye okunuyorlardı. Cevabı motor biliyordu ama IPC
/// sınırında düşüyordu — <c>bool?</c> gerekçe taşımaz.</para>
/// </summary>
public enum WillBuildReason
{
    /// <summary>Derlenmeyecek: imza kayıtlı imzayla aynı ve son koşu temiz başarıydı.</summary>
    UpToDate,
    /// <summary>Bu araç bu projeyi hiç başarıyla derlemedi (kayıt yok).</summary>
    NeverBuilt,
    /// <summary>Son koşusu başarısız/yarıda kaldı — çıktısı "bilinen iyi" değil.</summary>
    LastFailed,
    /// <summary>Son başarısı BAŞARISIZ bir bağımlılığın çıktısına link'liydi (bkz. <c>BuildState.DepIssue</c>) ve kök
    /// bağımlılıkları defterde yok (kökler kaydedilmeden önce yazılmış kayıt) — kesin derlenir. Kökler biliniyorsa
    /// gerekçe <see cref="WaitingForDependency"/>'dir.</summary>
    DepIssue,
    /// <summary>Kaynak imzası değişti (kendi dosyaları ya da bir upstream'in imzası).</summary>
    SignatureChanged,
    /// <summary>KOŞULLU: son başarısı başarısız bir bağımlılığın çıktısına link'liydi, kendi imzası değişmedi ve
    /// kök bağımlılıkları defterde kayıtlı (<c>BuildState.DepIssueRoots</c>). Build/Cycles koşusu onu sırası
    /// geldiğinde değerlendirir: köklerden en az biri artık başarılıysa derlenir, hepsi hâlâ hatalıysa atlanır
    /// (<c>ConditionalRebuild</c>). Alan SONA eklendi: sayısal değeri eskilerini kaydırmaz.</summary>
    WaitingForDependency,
    /// <summary>[Faz 3 — spec 2026-09-18 §5.4] Derlenmeyecek: çıktı bu araç dışında (VS, komut satırı) derlendi —
    /// zaman kipi, derleme kanıtı her girdiden ve HintPath hedefinden yeni, beslenen kopyalar sağlam. Yeşildir.</summary>
    BuiltOutside,
    /// <summary>[§5.4] Zaman kipi: kendi girdisi (dosya ya da taranan klasör) ya da bir HintPath hedefi derleme
    /// kanıtından yeni — çıktı bayat (<c>modified</c> ya da <c>affected</c>).</summary>
    OutputStale,
    /// <summary>[§5.3/§5.4] Derleme kanıtı (projenin kendi çıktı dosyası) diskte yok — defterde kayıt olsa bile
    /// çıktı ortada değildir (<c>never built</c> gibi okunur).</summary>
    OutputMissing,
    /// <summary>[§5.3/§5.4] Öğrenilmiş beslenen kopya (paylaşılan klasördeki DLL) eksik, boyutu farklı ya da
    /// derleme kanıtından eski — bağımlılar başka bir çıktıya link'lenir (<c>affected</c>).</summary>
    OutputReplaced,
}

public sealed record BuildState(
    string ProjectId,
    string? BuiltSignature,
    string? BuiltCommit = null,
    BuildResult? LastResult = null,
    DateTimeOffset? LastRunAt = null,
    string? LastBranch = null,
    long? LastDurationMs = null,             // T70 (It-3) burada alan olarak hazır
    // [Task 7] Bir SCC'nin (bu proje üyesiyken) turlarda SIKIŞTIĞI (NoProgress — aynı küme iki tur üst üste
    // patladı) bileşik imza. Tavana dayanmak (CapReached) buraya YAZILMAZ: o "bütçe bitti ama hâlâ hareket
    // var" demektir ve tavanın meşruiyeti zaten "bir sonraki Build kaldığı yerden devam eder"e dayanır.
    // BuiltSignature'dan KASITLI olarak AYRI: o alan yalnız SON BAŞARIYLA derlenen (Fast modun
    // frozen-upstream tabanı olarak okuduğu) imzayı taşır — ikisi aynı alanda karışırsa Fast'teki dependent'lar
    // hiç derlenmemiş bir imzayı "temiz" sanır. Bu alan currentSignature ile eşleştiğinde (bkz.
    // BuildStateStore.IsCycleNonConvergent) grup bir daha turlarla DENENMEZ; gerçek bir tur kararına ulaşan
    // (Converged/CapReached) her koşu ise alanı temizler.
    string? NonConvergentSignature = null,
    // [v1.16.0] Bu proje derlendiğinde KENDİ girdi dosyalarının içerik özeti (upstream'siz, cfg'siz).
    // BuiltSignature'dan AYRI durur çünkü ayrı bir soruyu cevaplar: "bu projenin KENDİ dosyaları değişti mi?"
    // İmza upstream'leri de taşır, dolayısıyla bir bağımlılığın kaydı geçersizleştiğinde de değişir — o yüzden
    // satırın `modified` / `affected` ayrımı ondan TÜRETİLEMEZ. Eski kayıtlarda yoktur (null): o durumda ayrım
    // bilinmez ve satır daha ihtiyatlı olan `affected`ı gösterir.
    string? BuiltContent = null,
    // Bu başarı BAŞARISIZ bir bağımlılığın çıktısına link'liydi. Kayıt yine de yazılır (aksi hâlde defter
    // hiç ilerlemez — bir koşuda 74 başarının 0'ı yazıldığı ölçüldü) ama not projeyi derleme listesinde
    // tutar: kökleri biliniyorsa (DepIssueRoots) koşullu — kök düzelince derlenir —, bilinmiyorsa kesin. LastResult
    // Succeeded KALIR — derleme gerçekten başarılıydı; bu ortogonal bir uyarıdır, sonucun kendisi değil.
    // Alan SONA ve default'lu eklendi: eski build-state.json kayıtları alansızdır ve false olarak çözülür.
    bool DepIssue = false,
    // DepIssue notunun KÖK bağımlılıkları — proje KİMLİKLERİ (tam csproj yolu; ad değil): bu koşuda patlayan
    // doğrudan kökler + zincirden miras alınanlar + tek proje koşusunda derlenmeden bırakılan bayat
    // bağımlılıklar. WillBuildEvaluator bunu görünce projeyi "kesin derlenecek" değil KOŞULLU sayar
    // (WaitingForDependency) ve koşu, köklerden biri düzelince derler (ConditionalRebuild). Alan SONA ve
    // default'lu: bu alandan önce yazılmış kayıtlar null çözülür ve kök bilinmediği için eski davranış
    // (her Build'de derlenir) sürer — güvenli yön.
    IReadOnlyList<string>? DepIssueRoots = null,
    // [spec 2026-09-18 §1-14] Bu projenin KENDİ MSBuild çağrısı exit != 0 ile bittiği andaki bileşik imza —
    // kırmızının KANITI. Yalnız derleyici hatası bunu yazar (ortam hatası/timeout/durdurma DEĞİL, bkz. Task 2);
    // başarıda null'a döner. WillBuildEvaluator bunu bugünkü imzayla karşılaştırır: eşitse gerekçe LastFailed
    // (kanıtlı kırmızı), aksi hâlde (imza yok ya da farklı) hata kanıt SAYILMAZ ve karar NeverBuilt'e düşer —
    // kanıtsız kırmızı YASAK. Alan SONA ve default'lu: eski build-state.json kayıtları alansızdır ve null
    // çözülür (hiçbir eski kayıt yanlışlıkla "kanıtlı" sayılmaz).
    string? FailedSignature = null,
    // [spec 2026-09-18 §1-14] FailedSignature'ın YAZILDIĞI an — `failed · 2h` etiketinin yaşı buradan okunur
    // (LastRunAt'tan AYRI: bir proje başarısızlıktan SONRA hiç derlenmeden imzası değişebilir, o durumda
    // FailedSignature hâlâ eski hatayı anlatır ama LastRunAt onun zamanını taşımaz — bkz. BuildStateStore.
    // FailedAtOf, LastBuiltAtOf ile aynı desen). Başarıda ya da kanıtsız hatada null.
    DateTimeOffset? FailedAt = null,
    // [Faz 3 — spec 2026-09-18 §5.1] Bu projenin derlemesiyle GERÇEKTEN güncellendiği öğrenilen "beslenen
    // çıktılar": bağımlılarının HintPath hedeflerinden, başarılı derlemeden sonra var olan, boyutu derleme
    // kanıtına eşit ve zamanı ondan en çok 2 s farklı olanlar (bkz. OutputEvidence.LearnFedOutputs). Hem defter
    // hem zaman kipinde denetlenir: biri yoksa, boyutu farklıysa ya da kanıttan eskiyse çıktı bozuk sayılır.
    // Alan SONA ve default'lu: eski kayıtlar null çözülür — liste yok, yalnız derleme kanıtı konuşur.
    IReadOnlyList<string>? FedOutputs = null)
{
    // Derleyicinin record eşitliği liste alanında referans eşitliğine düşer (JSON round-trip sonrası her zaman
    // farklı örnek) — ProjectNode ile aynı gerekçe, kökler sıralı içerikle karşılaştırılır.
    public bool Equals(BuildState? other) =>
        other is not null
        && ProjectId == other.ProjectId
        && BuiltSignature == other.BuiltSignature
        && BuiltCommit == other.BuiltCommit
        && LastResult == other.LastResult
        && LastRunAt == other.LastRunAt
        && LastBranch == other.LastBranch
        && LastDurationMs == other.LastDurationMs
        && NonConvergentSignature == other.NonConvergentSignature
        && BuiltContent == other.BuiltContent
        && DepIssue == other.DepIssue
        && (DepIssueRoots is null
            ? other.DepIssueRoots is null
            : other.DepIssueRoots is not null && DepIssueRoots.SequenceEqual(other.DepIssueRoots))
        && FailedSignature == other.FailedSignature
        && FailedAt == other.FailedAt
        && (FedOutputs is null
            ? other.FedOutputs is null
            : other.FedOutputs is not null && FedOutputs.SequenceEqual(other.FedOutputs));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectId);
        hash.Add(BuiltSignature);
        hash.Add(BuiltCommit);
        hash.Add(LastResult);
        hash.Add(LastRunAt);
        hash.Add(LastBranch);
        hash.Add(LastDurationMs);
        hash.Add(NonConvergentSignature);
        hash.Add(BuiltContent);
        hash.Add(DepIssue);
        foreach (string root in DepIssueRoots ?? []) hash.Add(root);
        hash.Add(FailedSignature);
        hash.Add(FailedAt);
        foreach (string fed in FedOutputs ?? []) hash.Add(fed);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Ana repo DIŞINDA yaşayan, build'den ÖNCE kendi klonundan güncellenip derlenen bir proje (ör. müşteriye
/// özel mail/OCR bileşenleri). Ayarlar'daki bir kartın birebir karşılığı: yalnız bir yol.
///
/// <para><b>Yol bir klasör, bir <c>.sln</c> ya da bir <c>.csproj</c> olabilir</b> (design v1.14.0 §9). Bir KÖKtür:
/// altındaki projeler her koşuda yeniden taranır ve ana taramayla birleşir
/// (<c>Core/Externals/ExternalWorkspaceResolver</c>); çalışma kopyasının kökü de o yoldan yukarı yürünerek
/// bulunur (<c>VcsDetector</c>). Hiçbiri persist edilmez, böylece bayatlayamazlar — bulunan her proje bundan
/// sonra sıradan bir projedir, kimliği kendi csproj yoludur.</para>
///
/// <para><b>[DEĞİŞEN KURAL] Sürüm kontrolü yalnız git'tir.</b> Kart eskiden bir <c>Vcs</c> seçimi de
/// taşıyordu (Git/TFVC) ve TFVC kolu <c>tf.exe</c> ile <c>tf vc get</c> koşuyordu. O kol kaldırıldı: seçim
/// yüzeyi bir karar noktası olarak kullanıcıya maliyet çıkarıyordu, TFVC yolu Team Explorer kurulumuna
/// bağlıydı ve local workspace işareti (<c>$tf</c>) bulunmayan yaygın kurulumlarda zaten hiç koşmuyordu.
/// Eski dosyalardaki <c>vcs</c> anahtarı okunurken SESSİZCE yok sayılır (bkz. <c>SettingsFile</c>,
/// <c>UiStateStore</c>) — kart sıradan bir git kartı olur.</para>
/// </summary>
/// <param name="Path">Klasör, solution ya da proje dosyası — kullanıcının yazdığı gibi.</param>
public sealed record ExternalProject(string Path);

/// <summary>Bir git branch/ref bilgisi (GitService.ListBranches / BranchListEvent). [It-3]</summary>
public sealed record BranchRef(string Name, string Sha, bool IsActive, bool IsRemoteTracking);

/// <summary>[Faz 2/Task 2] <c>BranchSwitcher.SwitchAsync</c>'in (<c>Core/Git/RepositoryWriter.cs</c>) sonucu —
/// Task 5'in IPC üzerinden App'e taşıyacağı checkout durumu, bu yüzden Contracts'ta yaşar (Core → Contracts
/// referansı, kopya YASAK).</summary>
public enum CheckoutStatus
{
    /// <summary>Checkout başarıyla yapıldı; çalışma ağacı artık hedef branch'te.</summary>
    Switched,
    /// <summary>Hedef zaten aktif branch'ti — git'e hiç dokunulmadı.</summary>
    AlreadyOn,
    /// <summary>Commit'lenmemiş yerel değişiklik var ve stash istenmedi — hiçbir şey yapılmadı.</summary>
    Dirty,
    /// <summary><c>stash push</c> başarısız oldu — checkout hiç denenmedi.</summary>
    StashFailed,
    /// <summary>Checkout (stash başarılıysa stash SONRASI) başarısız oldu.</summary>
    Failed,
}
