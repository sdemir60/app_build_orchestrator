using System.Linq;

namespace BuildOrchestrator.Contracts.Model;

// It-1 domain DTO'ları — A9 şeklini sabitler. Core → Contracts referansı üzerinden Core bu tipleri üretir.
// It-3: depIssues (ProjectSucceededEvent/ProjectFailedEvent) ve RunRequest.mode genişlemesi (Build/RetryFailed,
// DependentMode) artık IpcMessages.cs'de sabit; BranchRef/Worktree git-yüzeyi DTO'ları burada.

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
    /// <summary>Son başarısı BAŞARISIZ bir bağımlılığın çıktısına link'liydi (bkz. <c>BuildState.DepIssue</c>).</summary>
    DepIssue,
    /// <summary>Kaynak imzası değişti (kendi dosyaları ya da bir upstream'in imzası).</summary>
    SignatureChanged,
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
    // tutar: WillBuildEvaluator bunu görünce bağımlılık düzelene kadar "derlenecek" der. LastResult
    // Succeeded KALIR — derleme gerçekten başarılıydı; bu ortogonal bir uyarıdır, sonucun kendisi değil.
    // Alan SONA ve default'lu eklendi: eski build-state.json kayıtları alansızdır ve false olarak çözülür.
    bool DepIssue = false);

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

/// <summary>Bir git worktree bilgisi (GitService.ListWorktrees). IPC yüzeyi minimal — bu DTO It-3'te yalnız
/// GitService tarafında kullanılır; tam listWorktrees/deleteWorktree komutları It-4 UI'a ertelendi. [It-3]</summary>
public sealed record Worktree(string Name, string Branch, string Path, bool IsActive, long? DiskSizeBytes);
