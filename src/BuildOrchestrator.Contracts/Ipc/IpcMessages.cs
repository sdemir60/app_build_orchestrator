using System.Text.Json;
using System.Text.Json.Serialization;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Contracts.Ipc;

public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PingCommand), "ping")]
[JsonDerivedType(typeof(ShutdownCommand), "shutdown")]
[JsonDerivedType(typeof(StopRunCommand), "stopRun")]
[JsonDerivedType(typeof(GetProjectLogCommand), "getProjectLog")]
[JsonDerivedType(typeof(DebugSpawnChildrenCommand), "debugSpawnChildren")]
[JsonDerivedType(typeof(StartRunCommand), "startRun")]
[JsonDerivedType(typeof(SyncWorkspaceCommand), "syncWorkspace")]
[JsonDerivedType(typeof(CleanWorkspaceCommand), "cleanWorkspace")]
[JsonDerivedType(typeof(OptimizeWorkspaceCommand), "optimizeWorkspace")]
[JsonDerivedType(typeof(ListBranchesCommand), "listBranches")]
[JsonDerivedType(typeof(ListWorktreesCommand), "listWorktrees")]
[JsonDerivedType(typeof(DeleteWorktreeCommand), "deleteWorktree")]
[JsonDerivedType(typeof(SetPerfModeCommand), "setPerfMode")]
[JsonDerivedType(typeof(PullRepositoryCommand), "pullRepository")]
public abstract record IpcCommand;

/// <summary>
/// [v1.16.0] Ana repoyu uzak ucuna ff-only ilerletir — alt bardaki <c>N behind</c> chip'inin tek tetikleyicisi.
///
/// <para><b>Yalnız kullanıcı tıklamasıyla.</b> Araç kendiliğinden ASLA pull yapmaz: bu komut ne Sync'in ne
/// Build'in içinden çıkar. Yürütme <c>FastForwardUpdater</c>'ın harici kartlarda kullandığı ilkeli ANA REPO
/// köküne uygular — kir kapısı, ref-only fetch, "yalnız geride miyim" kontrolü ve <c>merge --ff-only</c>.
/// Kirli ya da ayrışmış ağaç REDDEDİLİR ve gerekçe konsola yazılır.</para>
/// </summary>
/// <param name="Branch">Ilerletilecek branch — App bunu YALNIZ aktif branch seçiliyken gönderir.</param>
public sealed record PullRepositoryCommand(string RootPath, string Branch) : IpcCommand;

public sealed record PingCommand(int Seq) : IpcCommand;
public sealed record ShutdownCommand : IpcCommand;
public enum StopKind { Graceful, Hard }
public sealed record StopRunCommand(string RunId, StopKind Kind) : IpcCommand;
public sealed record GetProjectLogCommand(string ProjectId) : IpcCommand;
public sealed record DebugSpawnChildrenCommand(int Count, bool Breakaway) : IpcCommand;

/// <summary>Bir koşunun KAPSAMINI seçer; NDJSON'a camelCase METİN olarak yazılır (<c>"cycles"</c>), sayı olarak
/// DEĞİL — bu yüzden yeni bir değer sona eklemek mevcut satırların anlamını kaydırmaz.</summary>
public enum RunMode { Rebuild, Build, Cycles, Clean }
/// <summary>Genel incremental dependent-propagation kapısı (bkz. <c>IncrementalPlanner</c> Safe/Fast, Task 7):
/// Build modunda WillBuild hesabını besler — Safe = dirty + tüm transitive dependent'lar yeniden derlenir;
/// Fast = yalnız dirty (cascade yok). [It-3]</summary>
public enum DependentMode { Safe, Fast }
/// <param name="Mode">Rebuild = tüm projeler; Build = incremental (yalnız dirty). [v7Δ-4] [It-3]
/// <para><b>Sürdürme/yeniden deneme AYRI bir mod DEĞİLDİR</b> (design v1.7.0 §3.1): Stop'tan sonra da hata
/// sonrasında da <b>Build</b> koşulur. Tamamlanıp yeşil bitmiş projeler imzalarını persist ettikleri için
/// <c>up to date</c> atlanır; öldürülenler ve başarısız olanlar <c>LastResult</c> invalidasyonuyla kirli
/// kalır, hata etkilenmiş bağımlılar ise imzalarını hiç persist etmedikleri için yeniden derlenir. Tek fark
/// elapsed'in sıfırdan başlamasıdır — bu yeni bir koşudur.</para>
/// <para><b>Cycles</b> = dairesel bağımlılık (SCC) oluşturan projeler, sıralı turlarla — ve onların
/// TRANSİTİF UPSTREAM'i (gerekçe <c>Core/Planning/CycleRunScope.cs</c>'te: kirli bir upstream'in eski DLL'ine
/// karşı derlenen üye yeşil döner, bayat çıktı verir ve imzası persist edildiği için bir daha ASLA
/// derlenmez). Kapsam dışı kalan her proje <see cref="SkipReasons.OutOfCycleScope"/> ile pre-skip edilir. Bu,
/// diğer modlardan bir DERECE farkı değil, ayrı bir iştir: Build/Rebuild bir SCC'yi ASLA derlemez (üyeleri
/// <see cref="SkipReasons.InDependencyCycle"/> ile atlanır). İkisi ardışık kullanılır — önce Cycles, sonra
/// Build.</para>
/// <para><b>Clean</b> = kapsamdaki her projede <c>msbuild /t:Clean</c> — Visual Studio'nun <i>Clean</i>'i:
/// yalnız o projenin derleme çıktıları silinir, cache'lere dokunulmaz. Hiçbir şey DERLEMEZ, dolayısıyla
/// incremental karar da sorulmaz. Çıktılar gittiği için temizlenen projenin build-state kaydı SİLİNİR —
/// §4 gereği DLL/bin timestamp'i okunmadığından defter, diskte çıktı olup olmadığını bilen tek yerdir ve
/// kayıt kalsaydı bir sonraki Build projeyi "güncel" sayıp atlardı. Bugün yalnız satır menüsünden,
/// <see cref="ScopeProjectId"/> ile birlikte gönderilir.</para></param>
/// <param name="Branch">Sync/build hedefi branch adı. [It-3]</param>
/// <param name="UseWorktree">true ise derleme ayrı bir git worktree üzerinde yapılır. [It-3]</param>
/// <param name="WorktreeName">UseWorktree=true iken kullanılacak worktree adı; null ise varsayılan ad türetilir. [It-3]</param>
/// <param name="DependentMode">Genel incremental dependent-propagation kapısı (bkz. <c>IncrementalPlanner</c>
/// Safe/Fast — Task 7): Build modunda WillBuild hesaplamasını besler (Safe = dirty+transitive cascade, Fast =
/// yalnız dirty, cascade yok). Varsayılan Safe. [It-3]</param>
/// <param name="LayerPatterns">[A1/T15] Katman ataması pattern'leri (bkz. <see cref="LayerPattern"/>); Core'daki
/// <c>LayerEngine</c> yalnız bu liste DOLU geldiğinde çalışır — null/boş ise katmanlama KAPALIDIR (varsayılan,
/// mevcut davranış). Sıra anlamlıdır: <c>Order</c> hem eşleşme önceliği hem atanan LayerIndex'tir.</param>
/// <param name="ExternalProjects">[Harici projeler] Ana repo DIŞINDA yaşayan çalışma alanı kökleri: her biri
/// taranır, bulduğu projeler ana taramayla BİRLEŞİR ve tek bir grafa girer. Sıradan projelerdir — sıraları
/// bağımlılıklarından doğar, listedeki sıradan değil. null/boş ise akış bugünküyle bayt-bayt aynıdır.</param>
/// <param name="PerfMode">[T20-b/K11] Perf profilinin ADI ("Full"/"Balanced"/"Light") — Supervisor bunu Core'daki
/// <c>PerfProfile.TryParse</c> ile çözer ve run boyunca inner Job'a CPU cap + priority uygular.
/// <b>Yalnız cap/priority'nin kaynağıdır:</b> paralellik AYRI bir alandır (<paramref name="Parallelism"/>) ve
/// App tarafında aynı tablodan türetilir — Supervisor worker sayısını burada YENİDEN hesaplamaz.
/// <c>null</c> (varsayılan) ⇒ perf modu bildirilmemiş: cap/priority'ye HİÇ dokunulmaz. Bu alan nullable +
/// varsayılan değerlidir; P2 öncesi yazılmış NDJSON satırları alansız çözülmeye devam eder.</param>
/// <param name="UpdateExternals">[Harici projeler] Bu koşu, harici çalışma kopyalarını derlemeden ÖNCE kendi
/// sürüm kontrolünden güncellesin mi (git <c>fetch</c> + <c>merge --ff-only</c> / <c>tf vc get</c>).
/// <b>Varsayılan <c>true</c></b> — alanı hiç yazmayan eski NDJSON satırları da güncelleme YAPAR, yani mevcut
/// davranış korunur.
/// <para><c>false</c> iken TEK BİR VCS komutu bile çalışmaz ve <b>kir kapısı da yoktur</b>: güncelleme
/// olmayınca kullanıcının dosyalarının üstüne yazma riski de yoktur, harici tıpkı ana repo gibi olduğu hâliyle
/// derlenir. Karar doğruluğu bundan etkilenmez — harici projelerin imzası çalışma kopyasının İÇERİĞİNDEN
/// hesaplanır (bkz. <c>IncrementalPlanner.ComputeContentFingerprint</c>), revizyon kimliğinden değil.</para></param>
/// <param name="ScopeProjectId">[tek proje · design v1.11.0 §3.8] Satırdan tetiklenen koşunun HEDEFİ — proje
/// kimliği (tam csproj yolu). <c>null</c> (varsayılan) ⇒ kapsam yok, koşu <see cref="Mode"/>'un anlattığı tam
/// kümedir; eski NDJSON satırları alansız çözülür.
/// <para>Dolu iken motor planı TEK düğüme indirger (<c>Core.Planning.ProjectRunScope</c>): bağımlılıklar
/// DERLENMEZ, kapsam dışı projeler koşuya hiç girmez (skip satırı yok, sayaç yok). Hedef tam koşuyla AYNI
/// motor yolundan geçer — Build modunda incremental kural, Rebuild'de koşulsuz. Bayat (kirli) bağımlılıklar
/// dep-issue olarak hedefe yapışır: bir sonraki Build hedefi yeniden derler, aksi halde taze imzası onu
/// bayat bir DLL'e kalıcı olarak link'li bırakırdı. Döngü üyesi bir hedef tek başına, döngü dışıymış gibi
/// derlenir; döngüdeki bağımlılıkları her koşulda bayat sayılır.</para></param>
public sealed record StartRunCommand(string RunId, RunMode Mode, string RootPath, string Configuration, int Parallelism,
    string Branch = "", bool UseWorktree = false, string? WorktreeName = null, DependentMode DependentMode = DependentMode.Safe,
    IReadOnlyList<LayerPattern>? LayerPatterns = null, string? PerfMode = null,
    IReadOnlyList<ExternalProject>? ExternalProjects = null, bool UpdateExternals = true,
    string? ScopeProjectId = null) : IpcCommand;

/// <summary>
/// [T20-b/K11] KOŞARKEN perf profilini değiştir. <b>Canlı değişen YALNIZ CPU cap + priority'dir</b>: worker'lar
/// run başında bir kez yaratılır (bkz. <c>RunCoordinator</c>'ın worker döngüsü), dolayısıyla yeni profilin
/// paralelliği ancak BİR SONRAKİ run'da geçerli olur. Aktif run yokken no-op'tur — perf modu o durumda zaten
/// bir sonraki <see cref="StartRunCommand.PerfMode"/> ile gelir.
/// </summary>
/// <param name="PerfMode">"Full"/"Balanced"/"Light"; çözülemeyen metin <c>error(badPerfMode)</c> ile yanıtlanır.</param>
public sealed record SetPerfModeCommand(string PerfMode) : IpcCommand;

/// <summary>
/// [K1 DOC FIX] Workspace'i verilen branch'e senkronize et. Sync REF-ONLY'dir: yalnızca <c>git fetch origin
/// &lt;branch&gt;</c> ile remote-tracking ref'i günceller — checkout/pull/reset KESİNLİKLE çağrılmaz, aktif
/// branch ve working tree ASLA değişmez (bkz. Core'daki <c>GitService.FetchRefOnlyAsync</c>). [It-3]
/// <para>
/// [A5/T69] Sync yalnız fetch DEĞİLDİR: tam analiz (scan → evaluate → graf → topo → will-build) BURADA koşar
/// (v7 A5 — "tam analiz yalnız Sync"), sonucu <see cref="WorkspaceTopologyEvent"/> + <see
/// cref="BuildPreviewEvent"/> + <see cref="SyncCompletedEvent"/> ile App'e taşınır.
/// </para>
/// </summary>
/// <param name="Branch">Fetch edilecek ref (<c>git fetch origin &lt;Branch&gt;</c>) ve
/// <see cref="SyncCompletedEvent.TargetSha"/>'in kaynağı.
/// <b>[A5/T69 bilinen seam] Analizi SEÇMEZ:</b> tarama ve will-build pass'i her zaman AKTİF çalışma ağacı
/// üzerinde (in-place) koşar — K1 gereği hiçbir branch checkout EDİLMEZ. Aktif branch'ten farklı bir ad
/// verilirse fetch ve <c>TargetSha</c> o branch'i gösterir ama topoloji/önizleme/sayaçlar hâlâ AKTİF ağacı
/// tarif eder. Seam'i kapatmak branch seçimini UI'a bağlayan task'ın (D6) işidir.</param>
/// <param name="LayerPatterns">[A1/T15] Katman ataması pattern'leri — <see cref="StartRunCommand.LayerPatterns"/>
/// ile AYNI anlam. null/boş ise katmanlama KAPALIDIR; dolu ise topoloji event'i LayerIndex/LayerName ve
/// ters-katman uyarılarını taşır.</param>
/// <param name="ExternalProjects">[Harici projeler] Ana repo DIŞINDA yaşayan, build'den önce güncellenip
/// derlenen projeler — kullanıcının Ayarlar'da sıraladığı liste, o sırayla. Sync bunları yalnız OKUR
/// (hiçbir VCS mutasyonu yapmaz): git olanların yerel HEAD'i ve kirliliği okunur, TFVC olanlar hollow
/// kalır. null/boş ise akış bugünküyle bayt-bayt aynıdır.</param>
/// <param name="Configuration">Will-build pass'inin imza terimine giren configuration (Debug/Release) — config
/// değişimi TÜM projeleri dirty yapar (bkz. <c>BuildSignature.Compute</c> "cfg=" terimi), bu yüzden Sync'in
/// önizlemesi ancak doğru configuration ile anlamlıdır.</param>
public sealed record SyncWorkspaceCommand(string RootPath, string Branch,
    IReadOnlyList<LayerPattern>? LayerPatterns = null, string Configuration = "Debug",
    IReadOnlyList<ExternalProject>? ExternalProjects = null) : IpcCommand;

/// <summary>
/// [clean] Aktif workspace'in derleme çıktısını sıfırla. <b>Siler:</b> <paramref name="RootPath"/> altında
/// keşfedilen her csproj'un klasöründeki <c>bin\</c> ve <c>obj\</c> + o workspace'e ait
/// <c>build-state.json</c> kayıtları (RootPath önekiyle, workspace-scoped). <b>Silmez:</b> <c>packages\</c>,
/// ortak OutDir, worktree havuzu (<c>_obj</c> dahil), run logları, <c>evaluation-cache.json</c>,
/// <c>ui-state.json</c>.
/// <para><b>MSBuild <c>/t:Clean</c> ÇAĞRILMAZ</b> — yalnız dosya sistemi silme. Gerekçe: eski-stil
/// projelerde <c>/t:Clean</c>'in sildiği küme (<c>FileListAbsolute.txt</c> kayıtlıları) bin/obj silmenin alt
/// kümesidir; obj silinince o kayıt da gider; ve tracked çıktılar ortak OutDir'e yazılmışsa <c>/t:Clean</c>
/// oradan da silerdi — "OutDir'e dokunulmaz" değişmezinin ihlali.</para>
/// <para>Bir koşu uçuştayken komut <c>error(cleanRejected)</c> ile REDDEDİLİR; App kapısıyla birlikte çift
/// katmanlı korumadır. Komut döngüsünü Sync gibi bloklar (arka plan task açılmaz).</para>
/// </summary>
/// <param name="ExternalProjects">[Harici projeler] Ayarlar'daki harici kökler — <see cref="SyncWorkspaceCommand"/>
/// ile AYNI kart listesi ve AYNI çözümleme (<c>ExternalWorkspaceResolver</c>). Harici projeler sıradan
/// projelerdir: aynı grafa girer, aynı kararı alır, dolayısıyla Clean de onların <c>bin</c>/<c>obj</c>'ini ve
/// defter kayıtlarını temizler. Kökleri ana kökün DIŞINDA olduğu için tarama onları ancak bu liste ile bulur;
/// liste boşsa akış ana kökle bayt-bayt aynıdır. Silme izni de bu köklerle sınırlıdır — kartı verilmemiş bir
/// dizine ASLA dokunulmaz. <c>null</c> (varsayılan): alanı hiç yazmayan eski NDJSON satırları çözülmeye devam
/// eder.</param>
public sealed record CleanWorkspaceCommand(
    string RootPath, IReadOnlyList<ExternalProject>? ExternalProjects = null) : IpcCommand;

/// <summary>
/// [optimize] Workspace'i ONAR — Optimize bir <b>workspace doktoru</b>dur: bilinen sorun sınıflarını tarar,
/// düzeltebildiğini o anda düzeltir, düzeltemediğini isim isim raporlar. <see cref="SyncWorkspaceCommand"/>'in
/// aksine salt-okur DEĞİLDİR; <see cref="CleanWorkspaceCommand"/>'in aksine de bir SİLİCİ değildir: Clean
/// derleme çıktısını götürür, Optimize eksik olanı geri getirir ve yalnız build'i kıran artığı ayıklar.
/// <para><b>Düzelttikleri:</b> (1) eksik NuGet paketleri — <c>packages.config</c>'li ve <c>\packages\</c>
/// HintPath hedefi diskte olmayan projeler per-proje <c>-t:restore</c> ile restore edilir; (2) restore'un
/// çözemediği kırık referanslar proje + dosya adıyla raporlanır (teşhis); (3) LEGACY projelerde build-kırıcı
/// stale <c>obj</c> NuGet artıkları (<c>project.assets.json</c>, <c>*.nuget.g.props/targets</c>) silinir —
/// SDK-style projede ASLA (restore'suz silmek build'i kırar); (4) üç kalıcı defterde (<c>build-state.json</c>,
/// <c>evaluation-cache.json</c>, <c>source-hash-cache.json</c>) dosyası artık var olmayan girdiler budanır ve
/// öksüz <c>.tmp</c> artıkları süpürülür.</para>
/// <para><b>Dokunmadıkları:</b> global NuGet cache'leri, <c>NuGet.config</c>, git (Optimize hiçbir git komutu
/// KOŞMAZ), worktree havuzu, <c>bin</c>/OutDir, run logları, <c>ui-state.json</c>. Build kararlarını
/// DEĞİŞTİRMEZ — imza kaynak-tabanlıdır, hiçbir projeyi dirty yapmaz.</para>
/// <para>Bir koşu uçuştayken reddedilir (<c>error(optimizeRejected)</c>) ve Sync gibi Supervisor'ın komut
/// döngüsünü BLOKLAR — iptal komutu YOKTUR (uzun restore'larda tek kaçış "Restart engine"dir).</para>
/// </summary>
/// <param name="RootPath">Onarılacak workspace kökü. Configuration TAŞINMAZ: hiçbir adım configuration'a bakmaz
/// (restore per-proje çalışır, artık temizliği ve defter budaması configuration'dan bağımsızdır).</param>
/// <param name="ExternalProjects">[Harici projeler] Ayarlar'daki harici kökler — <see cref="SyncWorkspaceCommand"/>
/// ve <see cref="CleanWorkspaceCommand"/> ile AYNI kart listesi ve AYNI çözümleme
/// (<c>ExternalWorkspaceResolver</c>). Harici projeler sıradan projelerdir: aynı grafa girer, aynı kararı alır,
/// dolayısıyla Optimize da onların paketlerini restore eder, kırık referanslarını raporlar ve stale
/// <c>obj</c> artıklarını temizler. Onarım izni bu köklerle sınırlıdır — kartı verilmemiş bir dizine ASLA
/// dokunulmaz. <c>null</c> (varsayılan): alanı hiç yazmayan eski NDJSON satırları çözülmeye devam eder.</param>
public sealed record OptimizeWorkspaceCommand(
    string RootPath, IReadOnlyList<ExternalProject>? ExternalProjects = null) : IpcCommand;

/// <summary>[A5/T69] Yerel + remote-tracking branch listesi iste (yanıt: <see cref="BranchListEvent"/>). SALT-OKUR.</summary>
public sealed record ListBranchesCommand(string RootPath) : IpcCommand;

/// <summary>[A5/T69] Worktree havuzunun envanterini iste (yanıt: <see cref="WorktreeListEvent"/>). SALT-OKUR.</summary>
public sealed record ListWorktreesCommand(string RootPath) : IpcCommand;

/// <summary>[A5/T69] Havuzdaki tek bir worktree'yi sil; ardından güncel envanter (<see cref="WorktreeListEvent"/>)
/// yayınlanır. <paramref name="Name"/> havuz kökü altındaki DİZİN ADIDIR (yol değil) — Core tarafında
/// <c>PathSanitizer.IsSafeSegment</c> ile doğrulanır.</summary>
public sealed record DeleteWorktreeCommand(string RootPath, string Name) : IpcCommand;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EngineReadyEvent), "engineReady")]
[JsonDerivedType(typeof(PongEvent), "pong")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(RunStoppedEvent), "runStopped")]
[JsonDerivedType(typeof(ProjectLogChunkEvent), "projectLogChunk")]
[JsonDerivedType(typeof(DebugChildrenSpawnedEvent), "debugChildrenSpawned")]
[JsonDerivedType(typeof(RunStartedEvent), "runStarted")]
[JsonDerivedType(typeof(ProjectStartedEvent), "projectStarted")]
[JsonDerivedType(typeof(ProjectLogEvent), "projectLog")]
[JsonDerivedType(typeof(ProjectSucceededEvent), "projectSucceeded")]
[JsonDerivedType(typeof(ProjectFailedEvent), "projectFailed")]
[JsonDerivedType(typeof(ProjectSkippedEvent), "projectSkipped")]
[JsonDerivedType(typeof(RunCompletedEvent), "runCompleted")]
[JsonDerivedType(typeof(SyncStartedEvent), "syncStarted")]
[JsonDerivedType(typeof(SyncProgressEvent), "syncProgress")]
[JsonDerivedType(typeof(SyncCompletedEvent), "syncCompleted")]
[JsonDerivedType(typeof(PullCompletedEvent), "pullCompleted")]
[JsonDerivedType(typeof(CleanStartedEvent), "cleanStarted")]
[JsonDerivedType(typeof(CleanProgressEvent), "cleanProgress")]
[JsonDerivedType(typeof(CleanCompletedEvent), "cleanCompleted")]
[JsonDerivedType(typeof(OptimizeStartedEvent), "optimizeStarted")]
[JsonDerivedType(typeof(OptimizeProgressEvent), "optimizeProgress")]
[JsonDerivedType(typeof(OptimizeCompletedEvent), "optimizeCompleted")]
[JsonDerivedType(typeof(PlanProgressEvent), "planProgress")]
[JsonDerivedType(typeof(BranchListEvent), "branchList")]
[JsonDerivedType(typeof(BuildPreviewEvent), "buildPreview")]
[JsonDerivedType(typeof(WorkspaceTopologyEvent), "workspaceTopology")]
[JsonDerivedType(typeof(WorktreeListEvent), "worktreeList")]
[JsonDerivedType(typeof(CycleRoundStartedEvent), "cycleRoundStarted")]
[JsonDerivedType(typeof(CycleCompletedEvent), "cycleCompleted")]
public abstract record IpcEvent;

public sealed record EngineReadyEvent(int Pid, string EngineVersion) : IpcEvent;
public sealed record PongEvent(int Seq) : IpcEvent;
public sealed record ErrorEvent(string Code, string Message) : IpcEvent;
public sealed record RunStoppedEvent(string RunId, bool WasHard) : IpcEvent;
/// <param name="ThroughLineNumber">Snapshot anında diske yazılmış son satır no — App canlı `projectLog`
/// satırlarını bununla dikiş yapar (LineNumber &lt;= ThroughLineNumber olanlar zaten chunk'ta). [T28]</param>
public sealed record ProjectLogChunkEvent(string ProjectId, int Sequence, string Text, bool IsLast, int ThroughLineNumber) : IpcEvent;
public sealed record DebugChildrenSpawnedEvent(int[] Pids) : IpcEvent;

public enum RunOutcome { Completed, Stopped }
/// <param name="CpuCapPercent">[T20-b/K11] Bu run BAŞLARKEN inner Job'a GERÇEKTEN uygulanmış hard CPU cap'i
/// (Balanced 70 · Light 40). <c>null</c> demek "cap YOK" demektir; üç sebebi olabilir: Full profili,
/// <see cref="StartRunCommand.PerfMode"/>'un hiç gelmemesi, ya da cap yazımının Win32 seviyesinde
/// BAŞARISIZ olması (o durumda Supervisor konsoluna bir uyarı da düşer). Yani bu alan İSTENEN değil
/// YÜRÜRLÜKTEKİ değeri taşır. Run ORTASINDA <see cref="SetPerfModeCommand"/> ile değişen cap'i İZLEMEZ:
/// bu, run'ın başlangıç durumunun kaydıdır.</param>
public sealed record RunStartedEvent(string RunId, RunMode Mode, int TotalProjects, int Parallelism,
    string Configuration, long ElapsedMsAtStart, int? CpuCapPercent = null) : IpcEvent;
public sealed record ProjectStartedEvent(string RunId, string ProjectId, string Name) : IpcEvent;
public sealed record ProjectLogEvent(string RunId, string ProjectId, int LineNumber, string Text) : IpcEvent;
/// <param name="DepIssues">Bu proje için tespit edilen dependency-uyarıları (ör. "dependent X henüz derlenmedi");
/// yoksa null (JSON'a yazılmaz). [It-3]</param>
/// <param name="CycleUnsettled">[cycle rounds] Bu proje bir SCC üyesidir ve grup TUR TAVANINA dayanarak bitti
/// (iki ardışık yeşil tur hiç olmadı): derleme başarılı, ama çıktı bir kuşak geride OLABİLİR. <b>Ayrı bir
/// alandır, <see cref="DepIssues"/>'a sahte bir isim enjekte EDİLMEZ</b> — o liste "hangi bağımlılık patladı"
/// sorusunun cevabıdır ve ikinci bir anlam yüklenirse App'in <c>▲ N</c> sayacı ile filtre chip'i yanlış sayar.
/// Varsayılan <c>false</c>: bu alandan ÖNCE yazılmış NDJSON satırları aynen çözülmeye devam eder.</param>
public sealed record ProjectSucceededEvent(string RunId, string ProjectId, long DurationMs,
    IReadOnlyList<string>? DepIssues = null, bool CycleUnsettled = false) : IpcEvent;
/// <param name="DepIssues">Bu proje için tespit edilen dependency-uyarıları; yoksa null (JSON'a yazılmaz). [It-3]</param>
public sealed record ProjectFailedEvent(string RunId, string ProjectId, long DurationMs, string Reason,
    IReadOnlyList<string>? DepIssues = null) : IpcEvent;
/// <param name="CycleUnconverged">[cycle rounds/Task 8] Bu skip, bir SCC'nin ÖNCEKİ bir Build'de yakınsamayıp
/// aynı bileşik imzada bir daha hiç tur harcanmadan pre-skip edildiğini işaretler (bkz. <c>RunCoordinator</c>'ın
/// <see cref="SkipReasons.CycleNonConvergent"/> seed'i). <b>Ayrı bir tipli alandır, Reason metninden
/// ÇIKARILMAZ</b> — sıradan "güncel" (up-to-date) skip'iyle Reason dışında hiçbir farkı olmadığı için App'in
/// metin eşleştirmesi yapması YASAKTIR (tek doğruluk kaynağı <see cref="SkipReasons"/>). Varsayılan
/// <c>false</c>: bu alandan ÖNCE yazılmış NDJSON satırları aynen çözülmeye devam eder.</param>
public sealed record ProjectSkippedEvent(string RunId, string ProjectId, string Reason, bool CycleUnconverged = false) : IpcEvent;
/// <param name="DepIssueCount">Run genelinde depIssues taşıyan proje-sonucu sayısı. [It-3]</param>
public sealed record RunCompletedEvent(string RunId, RunOutcome Outcome, int Succeeded, int Failed, int Skipped,
    int Queued, long DurationMs, int DepIssueCount = 0) : IpcEvent;

/// <summary>[K1 DOC FIX] Sync (ref-only <c>git fetch origin &lt;branch&gt;</c> — checkout/pull/reset YOK, aktif
/// branch değişmez) başladı. [It-3]</summary>
public sealed record SyncStartedEvent(string RootPath, string Branch) : IpcEvent;
/// <param name="Level">dim/info/warn — App tarafında satır rengini belirler. [It-3]</param>
public sealed record SyncProgressEvent(string Line, string Level) : IpcEvent;
/// <summary>[planlama görünürlüğü] Bir run'ın TAZE segmentinde, <see cref="RunStartedEvent"/>'ten ÖNCE koşan
/// planlama penceresinin adım satırı (worktree hazırlığı → tarama → graf → topo → incremental → MSBuild
/// çözümü). Satır metinleri <c>Core.Planning.PlanProgressLines</c>'tan gelir — Sync'in yazdıklarıyla AYNI
/// kaynak. <c>syncProgress</c>'ten AYRI bir kanaldır: bu pencere Sync DEĞİLDİR ve App'in Sync yüzeyini
/// (<c>_syncInFlight</c>) hiç ilgilendirmez.</summary>
public sealed record PlanProgressEvent(string Line) : IpcEvent;
/// <param name="TargetSha">Sync sonrası HEAD sha'sı; belirlenemediyse null.</param>
/// <param name="FetchDegraded">true ise fetch başarısız/kısıtlı oldu ve sync yerel state ile devam etti.</param>
/// <remarks>[A5/T69 bilinen seam] Aşağıdaki ÜÇ sayaç da (<paramref name="ChangedCount"/>/
/// <paramref name="ToBuildCount"/>/<paramref name="UpToDateCount"/>) AKTİF çalışma ağacına karşı hesaplanır —
/// <paramref name="Branch"/> farklı bir branch adlandırsa bile (bkz. <see cref="SyncWorkspaceCommand.Branch"/>).
/// <paramref name="TargetSha"/> ise fetch edilen ref'i taşır; ikisi FARKLI commit'leri tarif edebilir.</remarks>
/// <param name="ChangedCount">[A5/T69] DOĞRUDAN değişen (kendi imza terimi bayatlamış) proje sayısı — will-build
/// pass'inin <c>DependentMode.Fast</c> (cascade YOK) sonucudur. <paramref name="ToBuildCount"/>'tan TÜRETİLEMEZ:
/// o küme transitive dependent'ları da içerir (§3.1 "7 changed projects, 14 to build" tam olarak bu farktır).</param>
/// <param name="ToBuildCount">[A5/T69] Will-build kümesinin boyutu (<c>DependentMode.Safe</c> — dirty + transitive dependent).</param>
/// <param name="UpToDateCount">[A5/T69] Güncel (<c>WillBuild=false</c>) proje sayısı — Build'de pre-skip edilecekler.</param>
/// <param name="Behind">[v1.16.0] Yerel HEAD'in <c>origin/&lt;branch&gt;</c>'ten kaç commit geride olduğu —
/// alt bardaki <c>N behind</c> chip'i bunu okur. <c>null</c> ⇒ mesafe BİLİNMİYOR (fetch degrade oldu ya da
/// seçili branch aktif branch değil): chip çizilmez, uydurma sayı gösterilmez. Alan default'lu: eski NDJSON
/// satırları alansız çözülür.</param>
public sealed record SyncCompletedEvent(string Branch, string? TargetSha, bool FetchDegraded,
    int ProjectCount, int CycleCount,
    int ChangedCount = 0, int ToBuildCount = 0, int UpToDateCount = 0, int? Behind = null) : IpcEvent;
/// <summary>
/// [v1.16.0] <see cref="PullRepositoryCommand"/>'ın sonucu. Gerekçe satırları zaten <see
/// cref="SyncProgressEvent"/> olarak akmıştır; bu event yalnız "ilerledi mi" sorusunu cevaplar.
/// </summary>
/// <param name="Succeeded">Fast-forward gerçekleşti mi. <c>true</c> ⇒ App chip'i düşürür ve otomatik bir Sync
/// koşar (konsol KORUNARAK — kullanıcı kendi tetiklediği pull'un sonucunu görmeye devam etmeli).</param>
public sealed record PullCompletedEvent(bool Succeeded) : IpcEvent;
/// <summary>[clean] <see cref="CleanWorkspaceCommand"/> kabul edildi ve silme başlıyor.</summary>
public sealed record CleanStartedEvent(string RootPath) : IpcEvent;
/// <summary>[clean] Clean transkriptinin tek satırı. İmzası <see cref="SyncProgressEvent"/> ile aynıdır ama
/// Sync yüzeyine AİT DEĞİLDİR: App'in <c>_syncInFlight</c> kapısını hiç ilgilendirmez, ayrı bir kanaldır
/// (<see cref="PlanProgressEvent"/> emsali).</summary>
/// <param name="Level">dim/info/warn — App tarafında satır rengini belirler.</param>
public sealed record CleanProgressEvent(string Line, string Level) : IpcEvent;
/// <summary>[clean] Clean bitti — tek bitiş özeti. Kilitli dosya HATA DEĞİLDİR: akış durmaz, dosya başına
/// atlanır ve yalnız <paramref name="LockedFileCount"/> ile raporlanır.</summary>
/// <param name="ProjectCount">Taramanın bulduğu ve temizlenen proje klasörü sayısı.</param>
/// <param name="FoldersRemoved">Gerçekten silinen <c>bin</c>/<c>obj</c> klasörü sayısı.</param>
/// <param name="BytesRemoved">Silinen dosyaların toplam boyutu.</param>
/// <param name="LockedFileCount">Kullanımda olduğu için silinemeyen dosya sayısı.</param>
/// <param name="StateEntriesCleared">Kaldırılan <c>build-state.json</c> kaydı sayısı (workspace-scoped).</param>
public sealed record CleanCompletedEvent(int ProjectCount, int FoldersRemoved, long BytesRemoved,
    int LockedFileCount, int StateEntriesCleared) : IpcEvent;

/// <summary>[optimize] Workspace onarımı başladı (bkz. <see cref="OptimizeWorkspaceCommand"/>).</summary>
public sealed record OptimizeStartedEvent(string RootPath) : IpcEvent;

/// <summary>[optimize] Onarımın tek bir ilerleme satırı. <see cref="SyncProgressEvent"/> ve
/// <see cref="CleanProgressEvent"/>'in İKİZİDİR ama onların yüzeyine AİT DEĞİLDİR: App bu satırları kendi
/// optimize penceresinin dili sayar — üç akışın tek diskriminatöre binmesi konsol geçmişini de teşhisi de
/// bulandırırdı.</summary>
/// <param name="Level">cmd/info/dim/warn — App tarafında satır rengini belirler.</param>
public sealed record OptimizeProgressEvent(string Line, string Level) : IpcEvent;

/// <summary>[optimize] Onarım bitti; sayaçlar konsol özetini ve stream satırını besler. TÜM alanlar default
/// değerlidir — bu event'ten önce yazılmış NDJSON satırları alansız çözülmeye devam eder.</summary>
/// <param name="ProjectCount">Taranan (değerlendirilebilen) proje sayısı — harici kökler dahil.</param>
/// <param name="RestoredProjects">Restore'u exit 0 ile biten needy proje sayısı.</param>
/// <param name="FailedRestores">Restore'u exit≠0 ile biten needy proje sayısı (offline/kaynak erişilemez
/// senaryosu buraya düşer; HATA DEĞİLDİR, akış sürer). Needy toplamı <c>RestoredProjects + FailedRestores</c>
/// olarak TÜRETİLİR — ayrı bir alan yoktur.</param>
/// <param name="UnresolvedReferences">Restore DENENDİKTEN SONRA hâlâ diskte olmayan HintPath hedefi sayısı
/// (sürüm drift'i, eksik platform DLL'i). Ad bilinçlidir: "broken" değil — restore'un çözemedikleri.</param>
/// <param name="StaleObjCleaned">Stale NuGet artıkları temizlenen LEGACY proje sayısı (SDK-style projeler
/// hiç dokunulmadığı için buraya hiç girmez).</param>
/// <param name="PrunedStateEntries"><c>build-state.json</c>'dan budanan ölü girdi sayısı.</param>
/// <param name="PrunedCacheEntries"><c>evaluation-cache.json</c>'dan budanan ölü girdi sayısı.</param>
/// <param name="PrunedSourceHashEntries"><c>source-hash-cache.json</c>'dan budanan ölü girdi sayısı. Bu defter
/// KAYNAK DOSYA yollarıyla anahtarlanır (ötekiler csproj ile), bu yüzden ayrı sayılır: silinen tek bir dosya
/// bile buradan düşer.</param>
/// <param name="RemovedTempFiles">Süpürülen öksüz <c>.tmp</c> artığı sayısı (üç defterin toplamı).</param>
/// <param name="LockedFileCount">Kilitli olduğu için silinemeyen dosya sayısı — HATA DEĞİLDİR, akış sürer.</param>
/// <param name="BytesReclaimed">TÜM silme adımlarının (obj artıkları + <c>.tmp</c>) topladığı bayt.</param>
public sealed record OptimizeCompletedEvent(int ProjectCount = 0, int RestoredProjects = 0, int FailedRestores = 0,
    int UnresolvedReferences = 0, int StaleObjCleaned = 0, int PrunedStateEntries = 0, int PrunedCacheEntries = 0,
    int PrunedSourceHashEntries = 0, int RemovedTempFiles = 0, int LockedFileCount = 0,
    long BytesReclaimed = 0) : IpcEvent;

public sealed record BranchListEvent(IReadOnlyList<BranchRef> Branches) : IpcEvent;

/// <summary>
/// [A5/T69] Sync'in ürettiği workspace topolojisi — graf paneli (D5), katman gruplaması (D1) ve Open-in-VS
/// (E1) için gereken TÜM veriyi tek seferde App'e taşır. <see cref="SyncCompletedEvent"/>'ten ÖNCE yayınlanır.
/// </summary>
/// <param name="Nodes">Plan'ın build-order'ındaki TÜM düğümler (cycle üyeleri DAHİL); bağımlılıklar, katman
/// ataması, solution adları ve will-build üçlü durumu düğümün üzerindedir.</param>
/// <param name="Cycles">Her biri bir SCC (&gt;1 üye), üyeler sıralı — cycle rozeti bunu okur.</param>
/// <param name="Solutions">Workspace'te bulunan .sln'lerin ad + TAM YOL karşılıkları (ada göre sıralı, tekil).
/// <see cref="ProjectNode.SolutionNames"/> yalnız AD taşır; "VS'de Aç" (E1) yolu buradan çözülür.</param>
/// <param name="LayerWarnings">[A1/T15] Ters-katman uyarıları (warn-only DATA — hiçbir şey bloklanmaz/yeniden sıralanmaz).</param>
public sealed record WorkspaceTopologyEvent(
    IReadOnlyList<ProjectNode> Nodes,
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    IReadOnlyList<SolutionRef> Solutions,
    IReadOnlyList<string> LayerWarnings) : IpcEvent;

/// <summary>[A5/T69] Worktree havuzunun envanteri — <see cref="ListWorktreesCommand"/>/<see
/// cref="DeleteWorktreeCommand"/> yanıtı.</summary>
public sealed record WorktreeListEvent(IReadOnlyList<Worktree> Worktrees) : IpcEvent;

/// <summary>
/// [cycle rounds] Bir SCC'nin (dairesel bağımlılık grubunun) yeni bir turu başladı. Grup TEK bir derleme
/// birimidir: üyeleri her turda build-order sırasıyla ve sıralı derlenir, ara tur sonuçları YAYILMAZ — bu
/// yüzden kullanıcının gördüğü tek ilerleme sinyali budur (üyelerin kendi <see cref="ProjectStartedEvent"/>'i
/// her turda tekrarlanır, ama hangi TURDA olunduğunu yalnız bu olay söyler).
/// </summary>
/// <param name="ProjectId">Grubun build-order'daki İLK üyesi (lider) — konsol satırı grubu bu adla anar.</param>
/// <param name="Round">Başlayan turun 1-tabanlı numarası.</param>
/// <param name="RoundCap">Bu run'da bir SCC için yürütülecek azami tur sayısı (<c>CycleRoundPolicy.RoundCap</c>).</param>
/// <param name="MemberCount">Grubun bu turda derlenecek üye sayısı.</param>
public sealed record CycleRoundStartedEvent(string RunId, string ProjectId, int Round, int RoundCap, int MemberCount)
    : IpcEvent;

/// <summary>[cycles] Bir SCC koşusunun nihai kararı — CycleRoundDecision'ın (Core) wire karşılığı; Continue
/// (yarıda kesilme) bir karar DEĞİLDİR ve bu event hiç yayılmaz. camelCase METİN olarak yazılır.</summary>
public enum CycleOutcome { Converged, NoProgress, CapReached }

/// <summary>[cycles] Bir SCC'nin koşusu bitti. ProjectId = build-order'daki İLK üye (CycleRoundStartedEvent'in
/// lideriyle AYNI — satır tıklanabilir kalır). DurationMs üye sürelerinin toplamıdır; Rounds koşulan tur sayısı;
/// FailedCount SON turun başarısız üye sayısı.</summary>
public sealed record CycleCompletedEvent(string RunId, string ProjectId, CycleOutcome Outcome,
    int MemberCount, int Rounds, int FailedCount, long DurationMs) : IpcEvent;

/// <summary>[It-3][Task 17] Run başında (per-project build event'lerinden ÖNCE) yayınlanan will-build önizlemesi —
/// plan'ın <see cref="ProjectNode.WillBuild"/>'ini App'e taşır: dirty=true / güncel=false / imza-yok-yahut-pre-Sync
/// (hollow)=null. App bunu <c>Projects</c> listesini run başlamadan (proje-başına ilk event'ten önce) PRE-POPULATE
/// etmek için kullanır — bkz. RunViewModel.OnBuildPreview.</summary>
/// <param name="BuiltCommit">[W1/It-5] Bu projenin SON BAŞARIYLA derlendiği commit — <see
/// cref="Model.BuildState.BuiltCommit"/>'ten AYNEN (40-hex ham sha; kısaltma bir GÖRÜNTÜ kararıdır ve App'te
/// yapılır). Kart bunu sha çiftinin SOL yarısı olarak gösterir; sağ yarı run-geneli <see
/// cref="SyncCompletedEvent.TargetSha"/>'dir. <b>Hiç derlenmemiş</b> (build-state kaydı olmayan) proje ⇒
/// <c>null</c> — JSON'a hiç yazılmaz, dolayısıyla W1 ÖNCESİ yazılmış NDJSON satırları da alansız çözülmeye
/// devam eder (geriye dönük uyum). <b>Not:</b> bu değer ile <c>TargetSha</c> FARKLI ref ailelerinden gelir
/// (bu: derleme anındaki yerel/worktree HEAD — o: <c>refs/remotes/origin/&lt;branch&gt;</c>).</param>
/// <param name="Reason">[gerekçe] <see cref="WillBuild"/> kararının NEDENİ — kart, will-build noktasının
/// tooltip'inde bunu söyler ("commit aynı ama neden derlenecek?" sorusunun cevabı). Düğümden AYNEN taşınır;
/// koordinatörün koşu-zamanlama kuralıyla (pre-skip) <c>false</c>'a çevirdiği projelerde <c>null</c>'dır —
/// o karar imzadan gelmez, önizleme yalan söylemez. Alan default'lu: eski NDJSON satırları alansız çözülür.</param>
/// <param name="OwnFilesChanged">[v1.16.0] Projenin KENDİ girdi dosyaları son derlemeden bu yana değişti mi —
/// satırın karar etiketi <c>modified</c> (kendi dosyası) ile <c>affected</c> (yalnız bağımlılığı) ayrımını
/// buradan okur. Motorun Fast geçişinden gelir; karar bilinmiyorsa <c>null</c>. Alan default'lu: eski NDJSON
/// satırları alansız çözülür.</param>
/// <param name="LastBuiltAt">[v1.16.0] SON BAŞARILI derlemenin zamanı — <c>up to date · 2h</c> etiketindeki
/// göreli yaşın kaynağı. Hiç başarıyla derlenmemiş projede <c>null</c> ("never built" olgusu budur).</param>
public sealed record BuildPreviewItem(string ProjectId, string Name, bool? WillBuild, string? BuiltCommit = null,
    WillBuildReason? Reason = null, bool? OwnFilesChanged = null, DateTimeOffset? LastBuiltAt = null);
/// <param name="Items">Plan'ın build-order'ındaki TÜM düğümler (Cycle üyeleri DAHİL) — RunCoordinator bunu
/// <c>RunSegmentAsync</c>'te planlama bittikten hemen sonra, <c>runStarted</c>'dan SONRA ama ilk
/// <c>projectStarted</c>/<c>projectSkipped</c>'ten ÖNCE yayınlar.</param>
public sealed record BuildPreviewEvent(IReadOnlyList<BuildPreviewItem> Items) : IpcEvent;
