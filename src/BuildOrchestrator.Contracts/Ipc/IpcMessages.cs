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
[JsonDerivedType(typeof(ListBranchesCommand), "listBranches")]
[JsonDerivedType(typeof(ListWorktreesCommand), "listWorktrees")]
[JsonDerivedType(typeof(DeleteWorktreeCommand), "deleteWorktree")]
[JsonDerivedType(typeof(SetPerfModeCommand), "setPerfMode")]
public abstract record IpcCommand;

public sealed record PingCommand(int Seq) : IpcCommand;
public sealed record ShutdownCommand : IpcCommand;
public enum StopKind { Graceful, Hard }
public sealed record StopRunCommand(string RunId, StopKind Kind) : IpcCommand;
public sealed record GetProjectLogCommand(string ProjectId) : IpcCommand;
public sealed record DebugSpawnChildrenCommand(int Count, bool Breakaway) : IpcCommand;

public enum RunMode { Rebuild, Build, Continue, RetryFailed }
/// <summary>Genel incremental dependent-propagation kapısı (bkz. <c>IncrementalPlanner</c> Safe/Fast, Task 7):
/// Build modunda WillBuild hesabını besler — Safe = dirty + tüm transitive dependent'lar yeniden derlenir;
/// Fast = yalnız dirty (cascade yok). RetryFailed her zaman failed + tüm transitive dependent'ları derler
/// (DependentMode'dan BAĞIMSIZ). [It-3]</summary>
public enum DependentMode { Safe, Fast }
/// <param name="Mode">Rebuild = tüm projeler; Build = incremental (yalnız dirty); Continue = önceki run'ın
/// queued'larından sürer (elapsed korunur); RetryFailed = önceki run'da failed olanlar + tüm transitive
/// dependent'ları (DependentMode'dan bağımsız — her zaman full cascade). [v7Δ-4] [It-3]</param>
/// <param name="Branch">Sync/build hedefi branch adı. [It-3]</param>
/// <param name="UseWorktree">true ise derleme ayrı bir git worktree üzerinde yapılır. [It-3]</param>
/// <param name="WorktreeName">UseWorktree=true iken kullanılacak worktree adı; null ise varsayılan ad türetilir. [It-3]</param>
/// <param name="DependentMode">Genel incremental dependent-propagation kapısı (bkz. <c>IncrementalPlanner</c>
/// Safe/Fast — Task 7): Build modunda WillBuild hesaplamasını besler (Safe = dirty+transitive cascade, Fast =
/// yalnız dirty, cascade yok). RetryFailed modunda ETKİSİZDİR — o mod DependentMode'dan bağımsız, her zaman
/// failed + tüm transitive dependent'ları derler. Varsayılan Safe. [It-3]</param>
/// <param name="LayerPatterns">[A1/T15] Katman ataması pattern'leri (bkz. <see cref="LayerPattern"/>); Core'daki
/// <c>LayerEngine</c> yalnız bu liste DOLU geldiğinde çalışır — null/boş ise katmanlama KAPALIDIR (varsayılan,
/// mevcut davranış). Sıra anlamlıdır: <c>Order</c> hem eşleşme önceliği hem atanan LayerIndex'tir.</param>
/// <param name="PerfMode">[T20-b/K11] Perf profilinin ADI ("Full"/"Balanced"/"Light") — Supervisor bunu Core'daki
/// <c>PerfProfile.TryParse</c> ile çözer ve run boyunca inner Job'a CPU cap + priority uygular.
/// <b>Yalnız cap/priority'nin kaynağıdır:</b> paralellik AYRI bir alandır (<paramref name="Parallelism"/>) ve
/// App tarafında aynı tablodan türetilir — Supervisor worker sayısını burada YENİDEN hesaplamaz.
/// <c>null</c> (varsayılan) ⇒ perf modu bildirilmemiş: cap/priority'ye HİÇ dokunulmaz. Bu alan nullable +
/// varsayılan değerlidir; P2 öncesi yazılmış NDJSON satırları alansız çözülmeye devam eder.</param>
public sealed record StartRunCommand(string RunId, RunMode Mode, string RootPath, string Configuration, int Parallelism,
    string Branch = "", bool UseWorktree = false, string? WorktreeName = null, DependentMode DependentMode = DependentMode.Safe,
    IReadOnlyList<LayerPattern>? LayerPatterns = null, string? PerfMode = null) : IpcCommand;

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
/// <param name="Configuration">Will-build pass'inin imza terimine giren configuration (Debug/Release) — config
/// değişimi TÜM projeleri dirty yapar (bkz. <c>BuildSignature.Compute</c> "cfg=" terimi), bu yüzden Sync'in
/// önizlemesi ancak doğru configuration ile anlamlıdır.</param>
public sealed record SyncWorkspaceCommand(string RootPath, string Branch,
    IReadOnlyList<LayerPattern>? LayerPatterns = null, string Configuration = "Debug") : IpcCommand;

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
[JsonDerivedType(typeof(BranchListEvent), "branchList")]
[JsonDerivedType(typeof(BuildPreviewEvent), "buildPreview")]
[JsonDerivedType(typeof(WorkspaceTopologyEvent), "workspaceTopology")]
[JsonDerivedType(typeof(WorktreeListEvent), "worktreeList")]
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
public sealed record ProjectSucceededEvent(string RunId, string ProjectId, long DurationMs,
    IReadOnlyList<string>? DepIssues = null) : IpcEvent;
/// <param name="DepIssues">Bu proje için tespit edilen dependency-uyarıları; yoksa null (JSON'a yazılmaz). [It-3]</param>
public sealed record ProjectFailedEvent(string RunId, string ProjectId, long DurationMs, string Reason,
    IReadOnlyList<string>? DepIssues = null) : IpcEvent;
public sealed record ProjectSkippedEvent(string RunId, string ProjectId, string Reason) : IpcEvent;
/// <param name="DepIssueCount">Run genelinde depIssues taşıyan proje-sonucu sayısı. [It-3]</param>
public sealed record RunCompletedEvent(string RunId, RunOutcome Outcome, int Succeeded, int Failed, int Skipped,
    int Queued, long DurationMs, int DepIssueCount = 0) : IpcEvent;

/// <summary>[K1 DOC FIX] Sync (ref-only <c>git fetch origin &lt;branch&gt;</c> — checkout/pull/reset YOK, aktif
/// branch değişmez) başladı. [It-3]</summary>
public sealed record SyncStartedEvent(string RootPath, string Branch) : IpcEvent;
/// <param name="Level">dim/info/warn — App tarafında satır rengini belirler. [It-3]</param>
public sealed record SyncProgressEvent(string Line, string Level) : IpcEvent;
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
public sealed record SyncCompletedEvent(string Branch, string? TargetSha, bool FetchDegraded,
    int ProjectCount, int CycleCount,
    int ChangedCount = 0, int ToBuildCount = 0, int UpToDateCount = 0) : IpcEvent;
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
public sealed record BuildPreviewItem(string ProjectId, string Name, bool? WillBuild, string? BuiltCommit = null);
/// <param name="Items">Plan'ın build-order'ındaki TÜM düğümler (Cycle üyeleri DAHİL) — RunCoordinator bunu
/// <c>RunSegmentAsync</c>'te planlama bittikten hemen sonra, <c>runStarted</c>'dan SONRA ama ilk
/// <c>projectStarted</c>/<c>projectSkipped</c>'ten ÖNCE yayınlar.</param>
public sealed record BuildPreviewEvent(IReadOnlyList<BuildPreviewItem> Items) : IpcEvent;
