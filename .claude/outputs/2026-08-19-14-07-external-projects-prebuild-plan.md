# Harici Projeler (External Projects) — Ön-Derleme Özelliği: Uygulama Planı

> Bu plan derleme yapılamayan makinede, `d5943c1` commit'i üzerindeki kod haritasına göre hazırlandı.
> Uygulama başka makinede yapılacağı ve kod drift etmiş olabileceği için görevler satır numarasına değil
> **davranış + sınıf/metot adı + dosya yolu** çapalarına dayanır. Satır numarası geçen yerler yalnız ipucudur.

## Context

OSYS ana reposundaki projeler, repo DIŞINDA yaşayan müşteriye özel harici projelere (mail, OCR vb.) bağımlı.
Bugün developer bunları elle güncelleyip elle derliyor; orchestrator tek repo varsayımıyla çalışıyor
(ARCHITECTURE §20 "One repository at a time"). Özellik: Ayarlar'da LAYERS'ın yanına HARİCİ PROJELER listesi
eklenecek; Build denildiğinde orchestrator önce bu projeleri sırasıyla VCS'ten güncelleyip (git ff-only /
tf get) değişenleri derleyecek, sonra normal ana repo akışına geçecek. Node panelinde hariciler en üstte
"External" adlı ayrı bir katman grubu olarak görünecek. Mevcut süreç bozulmayacak, yeni özellik
karmaşıklaştırılmayacak.

## Kullanıcı kararları (bağlayıcı)

1. **TFS = Azure DevOps üzerinde TFVC, local workspace** (`$tf` klasörü var; tf.exe yüzeyi). Git olanlar düz
   git. VCS tespiti otomatik; ayarlara eklenir eklenmez rozet olarak görünür.
2. **Kullanıcı doğrudan PROJE dizinini verir** (TFS workspace'inde çok proje olabilir). Araç VCS kökünü
   verilen dizinden YUKARI yürüyerek bulur (`.git` dizini/dosyası ya da `$tf` dizini). Get-latest kök
   kapsamında koşar ("tüm projeyi get latest" — manuel akışla aynı), ama **yalnız verilen proje listede
   görünür ve derlenir**.
3. **Yerel değişiklik varsa build OLMAZ** — console'a net uyarı, dialog yok.
4. **Sıra = ayarlardaki liste sırası** (katmanlar gibi sürükle-bırak), ardışık derleme.
5. **Incremental ana projelerdeki felsefeyle aynı:** çekme her build'de yapılır; çekme sonrası değişiklik
   yoksa ve önceki derleme başarılıysa derleme atlanır ("up to date"). Rebuild modunda hariciler de
   koşulsuz derlenir.
6. Eskiyi bozma, yeniyi karmaşıklaştırma.

## Tasarım — kilit kararlar

- **D1 — Yeni Core alanı `BuildOrchestrator.Core.Externals`** her şeyi sahiplenir (tespit, imza, git
  güncelleme, TFVC, planlama, node üretimi). Planlama Core'da kalır; Supervisor yalnız bağlar ve yürütür;
  App yalnız listeyi düzenler/persist eder ve çizer.
- **D2 — Kimlik = `TargetPath`** (derlenecek .sln/.csproj). `build-state.json` anahtarı, IPC ProjectId,
  proje logu ve satır Id'si budur. Ayarda persist edilen: `Name + ProjectPath + TargetPath`. **VCS türü ve
  VCS kökü ASLA persist edilmez** — her seferinde diskten yeniden bulunur (bayatlamaz).
- **D3 — Kök keşfi:** `ProjectPath`'ten yukarı yürüyerek ilk işaret: `.git` dizini **veya dosyası** → Git;
  `$tf` dizini → Tfvc; sürücü köküne kadar işaret yoksa → Unknown. Unknown: güncelleme/dirty kapısı yok,
  revizyon null → her zaman derlenir + console uyarısı ("no version control detected — building as-is").
- **D4 — İmza = SHA256(configuration ␟ vcs ␟ revisionId)**. Dirty → build iptal olduğu için temiz çalışma
  kopyası garantidir; bu yüzden revizyon kimliği (git HEAD sha / TFVC changeset) tam imzadır. Git'te
  revizyon kök reponun HEAD'i; TFVC'de kök kapsamının son changeset'i (kapsam geniş → fazladan derleme
  olabilir, güvenli taraf). `revision == null` → hiçbir zaman "up to date" olmaz.
- **D5 — Sync HİÇ mutasyon yapmaz ve bloklamaz:** git hariciler için gerçek will-build önizlemesi (yerel
  `rev-parse HEAD` + `status --porcelain`, ucuz); dirty ise yalnız uyarı satırı. **TFVC hariciler Sync'te
  hollow kalır (`WillBuild=null`)** — Sync sırasında tf.exe hiç çalıştırılmaz (Sync hızlı/çevrimdışı-toleranslı
  kalmalı; gerçek karar build planlamasında verilir).
- **D6 — Build-anı harici fazı planner delegate İÇİNDE, `PrepareAsync`'ten ÖNCE** koşar; liste sırasıyla:
  kök keşfi → dirty kapısı (dirty → `ExternalPreparationException` → mevcut `planFailed` kanalı; run hiç
  başlamaz) → güncelleme → revizyon → karar. Ağ/kimlik hatasında güncelleme **uyar + yerel sürümle devam**
  (ana repo degraded fetch ile aynı felsefe). Git'te diverged/detached → dirty gibi iptal.
- **D7 — git güncellemesi `pull` DEĞİL:** `FetchRefOnlyAsync` + `merge-base --is-ancestor` + `merge
  --ff-only` bileşimi (tipli sonuçlar; lokalize stderr ayrıştırma yasağına uyar — `GitService.IsUnbornHeadSignal`
  emsali). Bu, kod tabanındaki **tek mutasyon yapan git yüzeyi** olur ve `Core/Externals` dışına çıkamaz
  (kaynak guard'ı ile çitlenir).
- **D8 — TFVC yüzeyi:** tf.exe vswhere ile çözülür (vswhere çağrısı `VsWhereLocator`'a çıkarılır;
  `MsBuildResolver` da onu kullanır — kopya yasak). `tf vc status /format:xml` (lokalize metin ASLA
  ayrıştırılmaz), `tf vc get . /recursive /noprompt` (kök cwd), `tf vc history /stopafter:1 /version:W`
  (baştaki sayı). tf.exe bulunamaz + TFVC harici varsa → planFailed, net İngilizce mesaj.
- **D9 — Yürütme `RunSegmentAsync` pre-worker bölgesinde**, `RunStartedEvent`/`BuildPreviewEvent` sonrası,
  worker'lar spawn edilmeden önce, sırayla: up-to-date → `ProjectSkippedEvent(SkipReasons.UpToDate)`;
  derlenecek → `ProjectStarted` → MSBuild.exe (inner job, `MsBuildInvoker` üzerinden) → `Succeeded/Failed`
  + BuildState upsert/invalidate. **Herhangi bir harici FAIL olursa run iptal:** worker'lar hiç doğmaz,
  kalan hariciler denenmez, console'a net satır, `RunCompletedEvent` sayaçları gerçeği yansıtır (ana
  projeler Queued). Graceful stop hariciler arasında da dinlenir; hard stop inner job ile zaten öldürür.
- **D10 — Harici MSBuild argümanları:** yerinde derleme (obj izolasyonu YOK, OutDir ASLA yok),
  `-p:BuildProjectReferences=false` YOK (harici kendi solution'ını referanslarıyla + post-build copy
  event'leriyle derler — çıktılar OSYS'in HintPath'le okuduğu yerlere böyle düşer), restore her zaman önce.
  Seçim `MsBuildInvokeRequest`'e eklenen kuyruk parametresi `bool ExternalTarget = false` ile — argüman
  eşlemesini invoker sahiplenmeye devam eder.
- **D11 — Tel üzerinde harici node'lar sıradan `ProjectNode`:** topolojinin BAŞINA eklenir;
  `LayerName = "External"`, `LayerIndex = -1` (tek kaynak: `ExternalProjectsConventions`), `Dependencies=[]`,
  `InCycle=false`, + tek yeni kuyruk alanı `VcsKind? ExternalVcs = null` (null → normal proje). Revizyon,
  mevcut `BuildPreviewItem.BuiltCommit` yuvasında taşınır (yeni alan yok). Böylece liste gruplama
  (`LayerGrouping` ilk-görülme sırası) ve graph bandı (`QuietGraphLayout` `SortedDictionary` — negatif
  indeks güvenle en üstte) **bedavaya** çalışır.
- **D12 — Modlar:** Build = incremental; Rebuild = hariciler koşulsuz derlenir (kapılar aynı);
  **Cycles harici fazı tamamen atlar** (ana repo SCC onarım koşusudur).
- **D13 — Sync sayaçları ve "no changes" anlatısı ana workspace'i anlatmaya devam eder** — hariciler
  `SyncCompletedEvent` sayaçlarına karışmaz; UI'a topology + preview üzerinden ulaşırlar.

## Faz 1 — Core: tespit, imza, git güncelleme, TFVC (tel/UI değişikliği yok)

### 1.1 Contracts model: `ExternalProject` + `VcsKind`
- `Contracts/Model/ProjectModels.cs`: `record ExternalProject(string Name, string ProjectPath, string TargetPath)`;
  `enum VcsKind { Git, Tfvc, Unknown }` (camelCase enum metni pinlenir — sonradan reorder tel anlamını kaydıramaz).
- Önce test: `ProjectModelsTests.ExternalProject_round_trips_through_ipc_json`.

### 1.2 `VcsDetector` (kök keşfi) + `ExternalProjectsConventions` + `ExternalTargetResolver`
- `Core/Externals/VcsDetector.cs`: `record VcsRoot(VcsKind Kind, string? RootPath);`
  `static VcsRoot DetectRoot(string startDirectory)` — verilen dizinden yukarı yürür; `.git` (dizin veya
  dosya) → Git, `$tf` → Tfvc, sürücü kökü → `(Unknown, null)`. Var olmayan dizin → Unknown.
- `ExternalProjectsConventions.cs`: `const string LayerName = "External"; const int LayerIndex = -1;`
  (literalin TEK tanımı).
- `ExternalTargetResolver.cs`: `static string? AutoTarget(string projectPath)` — dizinde tam BİR `*.sln`
  varsa yolu, yoksa/birden çoksa null (UI seçtirir).
- Önce testler: `Detect_finds_git_root_from_a_subdirectory` (GitTestRepo alt klasöründen),
  `Detect_reports_git_for_a_gitfile_worktree`, `Detect_finds_tfvc_root_from_a_subdirectory` (gizli `$tf`),
  `Detect_reports_unknown_when_no_marker_up_to_drive_root`, `AutoTarget_picks_the_single_sln`,
  `AutoTarget_returns_null_when_ambiguous_or_absent`.

### 1.3 `ExternalSignature` + `ExternalWillBuild`
- `ExternalSignature.Compute(configuration, VcsKind, string? revision)` — `BuildSignature.HashText` +
  `NullMarker` yeniden kullanılır (internal, aynı assembly), ayraç 0x1F.
- `ExternalWillBuild.Decide(BuildState? state, string signature)` → `(true, NeverBuilt)` / `(true, LastFailed)` /
  `(true, SignatureChanged)` / `(false, UpToDate)` — `WillBuildEvaluator`'ın ilgili kollarının bilinçli mini
  aynası (haricilerde dep/DepIssue yok; ana evaluator'a dokunulmaz).
- Önce testler: imza determinizmi + her terimin imzayı değiştirdiği (null revizyon ≠ boş revizyon); dört
  karar kolu ayrı ayrı (`WillBuildReason` değerleri verbatim pinli).

### 1.4 `ExternalGitUpdater` — kod tabanındaki TEK mutasyon yapan git yüzeyi
- `enum ExternalUpdateStatus { Updated, AlreadyCurrent, DegradedOffline, Dirty, Diverged, Detached, Failed }`
- `record ExternalUpdateResult(Status, string? Revision, string? Detail)`
- `sealed class ExternalGitUpdater(IProcessRunner, string rootPath)` — akış: dirty (`status --porcelain`,
  HER kir sayılır — commit/stash zaten kullanıcı eylemi) → branch (`Ok(null)` → Detached) →
  `FetchRefOnlyAsync` (degraded → yerel HEAD ile DegradedOffline, build devam eder) → HEAD vs
  `refs/remotes/origin/<branch>`: eşit → AlreadyCurrent; `merge-base --is-ancestor` (exit 0/1,
  lokal-bağımsız sinyal) → `merge --ff-only <ref>` → Updated (yeni HEAD); değilse Diverged.
- xmldoc: **bu sınıfa ana repo kökü ASLA verilemez** (kaynak guard'ı Faz 3.4'te).
- Önce testler (GitTestRepo + file:// remote): dirty remote'a dokunmaz; detached; behind → ff + yeni HEAD;
  diverged → HEAD değişmez; offline (origin'i olmayan yola çevir) → DegradedOffline + yerel HEAD;
  up-to-date → AlreadyCurrent.

### 1.5 `VsWhereLocator` çıkarımı + `TfResolver` + `TfvcService`
- `VsWhereLocator(IProcessRunner)` — vswhere çağrısı `MsBuildResolver.ResolveAsync`'ten aynen çıkarılır
  (dosya-var kapısı, 30 sn timeout, ilk dolu stdout satırı, dosya-var kontrolü); `MsBuildResolver` ona
  delege eder ve **mevcut `MsBuildResolverTests` DEĞİŞMEDEN yeşil kalır**.
- `TfResolver(IProcessRunner)` — vswhere args:
  `-latest -products * -find Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\TF.exe`;
  bulunamazsa `TfResolveException` ("install Visual Studio with Team Explorer" içeren net İngilizce mesaj).
- `TfvcService(IProcessRunner, string rootPath, string tfExePath)`:
  - `HasPendingChangesAsync` → `tf vc status . /recursive /noprompt /format:xml` — XML'de `<PendingChange`
    öğesi var mı (lokalize metin ayrıştırma YASAK).
  - `GetLatestAsync` → `tf vc get . /recursive /noprompt` — 5 dk timeout (WorktreeManager emsali).
  - `CurrentChangesetAsync` → `tf vc history . /recursive /stopafter:1 /noprompt /version:W /format:brief`
    — ilk veri satırının baştaki sayısı (yalnız rakam parse, lokal-bağımsız); hata → `Ok(null)`
    (bilinmeyen revizyon = fazladan derleme, güvenli taraf).
  - `GitResult<T>` yeniden kullanılır; süreç başlatma hataları `GitCommandExecutor` gibi sarılır (asla raw throw).
- Önce testler: fake `IProcessRunner` ile XML'li/boş status; changeset parse + hata=null; get hatası
  veri olarak döner; TfResolver mesajı; mevcut MsBuildResolver testleri yeşil.

**Faz 1 doküman:** ARCHITECTURE §22 kod haritasına `Core/Externals/` satırları (anlatı üslubu, birer satır).

## Faz 2 — Contracts/IPC + Core planlayıcılar (tel şekli + Sync entegrasyonu)

### 2.1 IPC alanları (toleranslı kuyruklar)
- `SyncWorkspaceCommand(..., IReadOnlyList<ExternalProject>? ExternalProjects = null)`;
  `StartRunCommand(...)` aynı kuyruk (PerfMode'dan sonra).
- `ProjectNode`'a kuyruk `VcsKind? ExternalVcs = null` — **elle yazılmış `Equals`/`GetHashCode`'a alan
  EKLENMELİ** (record default'u liste alanları yüzünden override edilmiş; unutulursa alan sessizce yutulur).
- Önce testler: eski JSON satırları (alan yokken) parse olur; round-trip;
  `ProjectNode_equality_includes_ExternalVcs` (yalnız `ExternalVcs` farklı iki node eşit DEĞİL — Equals
  güncellenmezse kırmızı kalan test budur).

### 2.2 `ExternalNodeBuilder` + `ExternalSyncInspector` (salt-okur Sync geçişi)
- `record ExternalInspection(Project, VcsKind, string? Revision, bool? WillBuild, WillBuildReason?, bool Dirty, string? Warning)`
- `ExternalNodeBuilder.ToNode(inspection, order)` → Id/ProjectPath = TargetPath, Name = giriş adı,
  `SolutionNames=[Path.GetFileName(TargetPath)]`, `Dependencies=[]`, `InCycle=false`,
  `LayerIndex/-Name = ExternalProjectsConventions`, `ExternalVcs` dolu.
- `ExternalSyncInspector(IProcessRunner)`: kök keşfi → Git: yerel `rev-parse HEAD` + `status` (dirty →
  `Dirty=true` + uyarı verisi; WillBuild'i DEĞİŞTİRMEZ), imza + `ExternalWillBuild` (TargetPath anahtarı);
  Tfvc/Unknown: hollow (`WillBuild=null`), tf.exe HİÇ çalıştırılmaz; kök yok → Unknown + uyarı ("external
  '<name>': folder not found — state unknown"). Herhangi git hatası → hollow + uyarı (Sync bir harici
  yüzünden ölmez).
- Önce testler: up-to-date / signature-changed / dirty-ama-willbuild-korunur / eksik kök / TFVC-hollow
  (runner'da "tf" içeren hiçbir çağrı olmadığı assert edilir) / node şekli pinleri (LayerName "External",
  LayerIndex −1, Id = TargetPath, sln görünüm adı).

### 2.3 `SyncWorkspaceService` entegrasyonu
- `ComputeWillBuildAsync` sonrası, `WorkspaceTopologyEvent` yayınından önce: `cmd.ExternalProjects is
  { Count: > 0 }` ise inspector koşar (ctor'a `IProcessRunner`/inspector factory enjekte edilir;
  `SupervisorHost.WorkspaceServices.Default` sağlar); node'lar ve `BuildPreviewItem`'lar (BuiltCommit =
  `BuildStateStore.BuiltCommitOf`) topolojinin/önizlemenin BAŞINA eklenir; her `Warning` ve `Dirty` için
  uyarı satırı: `warning: external '<name>' has uncommitted changes — Build will refuse to run until they
  are committed or shelved`. **Sayaçlar ve "no changes" satırları ana workspace'i anlatmaya devam eder;
  Cycles listesi dokunulmaz.**
- Önce testler: hariciler ana node'lardan önce (sıra + LayerName pinli); preview'da willBuild + son
  revizyon; dirty uyarır ama Sync tamamlanır; sayaçlar haricileri saymaz; **liste boş/null iken mevcut
  davranış bayt-bayt aynı** (inspector hiç kurulmaz; mevcut SyncWorkspaceServiceTests dokunulmadan yeşil).

### 2.4 `ExternalRunPlanner` + `ExternalPreparationException`
- `ExternalPreparationException` — kullanıcıya görünen İngilizce mesaj, harici adı + eylemi içerir, örn.
  `External project 'Mail' has uncommitted changes in 'D:\src\mail' — commit, stash or shelve them, then build again.`
- `record ExternalBuildPlan(Project, VcsKind, string? Revision, string Signature, bool WillBuild, WillBuildReason? Reason)`
- `ExternalRunPlanner(IProcessRunner, Func<string> tfResolver /*lazy*/)`.`PlanAsync(externals, configuration,
  rebuild, state, progress, ct)` — sırayla: progress satırı → kök keşfi →
  - **Git:** `ExternalGitUpdater.UpdateAsync` → Dirty/Diverged/Detached/Failed → throw (duruma özel mesaj);
    DegradedOffline → uyar + yerel revizyonla devam; Updated/AlreadyCurrent → revizyon.
  - **Tfvc:** tf.exe İLK TFVC haricide lazily çözülür (`TfResolveException` →
    `ExternalPreparationException`'a sarılır); pending → throw; get hatası → uyar + devam;
    changeset → `"C" + id` ya da null.
  - **Unknown:** uyar, revizyon null.
  - Karar: `rebuild ? (true, null) : ExternalWillBuild.Decide(state[TargetPath], signature)`.
- Önce testler: dirty mesajda harici adı; diverged throw; offline degrade + yerel revizyon; rebuild
  zorlaması; tf.exe yok → planFailed metni; unknown güncellenmeden derlenir; progress satırları liste
  sırasında; ikinci koşu up-to-date (gerçek `BuildStateStore` temp ile state round-trip).

### 2.5 `PlanProgressLines` harici sözlüğü
- `UpdatingExternal(name)` → `Updating external '{name}'`; `ExternalUpToDate(name)` → `External '{name}'
  is up to date`; `ExternalUpdateDegraded(name, reason)` → `warning: external '{name}' could not be
  updated — building the local version ({reason})`. (Dirty/iptal metni exception'da yaşar — o progress
  değil hatadır.) Testler üç metni verbatim pinler; bu satır literalleri bu sınıf + exception dışında var olamaz.

**Faz 2 doküman:** ARCHITECTURE §5.2/§5.3 (yeni alanlar, başa eklenen node'lar), §10'a yeni alt bölüm
"External project version control" (ff-only boru hattı, TFVC yüzeyi, degrade kuralları, dirty kapısı).

## Faz 3 — Supervisor: run bağlama + ardışık yürütme

### 3.1 Harici MSBuild argüman varyantı
- `MsBuildArguments.BuildExternal(target, cfg)` → `[target, -t:Build, -p:Configuration=X,
  -p:UseSharedCompilation=false, -nodeReuse:false, -clp:Summary, -nologo]` — `BuildProjectReferences=false`
  YOK, obj izolasyonu YOK, OutDir YOK. `RestoreExternal(target)` → `[target, -t:restore,
  -p:RestorePackagesConfig=true, -nologo]`.
- `MsBuildInvokeRequest`'e kuyruk `bool ExternalTarget = false`; `MsBuildInvoker.InvokeAsync` bu bayrakta
  önce RestoreExternal (her zaman) sonra BuildExternal; mevcut yol bayt-bayt aynı.
- Önce testler: harici arg listesi tam pin + `BuildProjectReferences`/`BaseIntermediateOutputPath`/`OutDir`
  YOKLUĞU asserti; restore-sonra-build sırası (mevcut fake-child kalıbı); harici olmayan istekler değişmedi.

### 3.2 `RunPlan.Externals` + `Program.BuildRunPlan` bağlama
- `RunPlan(..., IReadOnlyList<ExternalBuildPlan>? Externals = null)` (kuyruk).
- `BuildRunPlan` İLK işi: `cmd.Mode != RunMode.Cycles && cmd.ExternalProjects is { Count: > 0 }` ise
  `ExternalRunPlanner.PlanAsync(...).GetAwaiter().GetResult()` (PrepareAsync'in mevcut sync-çağrı kalıbı),
  **`PrepareAsync`'ten ÖNCE**; sonuç `RunPlan`'a taşınır.
- `RunSegmentAsync` planner catch filtresine `or ExternalPreparationException` eklenir →
  `error(planFailed)`, run hiç başlamaz (WorktreePreparationException emsali).
- Önce test: `Planner_throwing_ExternalPreparationException_emits_planFailed_and_no_runStarted`.

### 3.3 Pre-worker bölgesinde ardışık harici yürütme
- `RunContext` kurulduktan ve pre-skip event'leri yazıldıktan sonra, worker spawn'dan önce,
  `runPlan.Externals ?? []` sırayla:
  - `WillBuild == false` → `ProjectSkippedEvent(runId, TargetPath, SkipReasons.UpToDate)` + decision.log
    (adlar harici plandan gelir — `nodeById`'da yoklar).
  - değilse: iki öğe arasında `StopRequested` kontrolü → `ProjectStartedEvent` →
    `run.Logs.OpenProjectLog(TargetPath)` → `run.Invoker.InvokeAsync(new MsBuildInvokeRequest(TargetPath,
    cfg, SolutionDir: Path.GetDirectoryName(TargetPath)!, NeedsRestore: false, ExternalTarget: true), …)` →
    başarı → `BuildStateStore.Upsert(new BuildState(TargetPath, Signature, BuiltCommit: Revision,
    Succeeded, UtcNow, LastBranch: null, durationMs))` (IO hatasında yalnız uyar —
    `PersistBuildStateOnSuccess` kalıbı) + `ProjectSucceededEvent`; hata → invalidate (partial-merge
    kalıbı) + `ProjectFailedEvent` + console: `error: external project '<name>' failed — stopping before
    the main repository build` + decision.log, `externalAborted = true` ve döngü kırılır.
- Worker spawn `if (!externalAborted)` ile kapılanır; `finally`'de harici sayımları
  succeeded/failed/skipped'e eklenir (`Queued` zaten dispatch edilmemiş ana projeleri gösterir; `RunOutcome`
  bugünkü gibi stop-vs-complete'i anlatır, başarısız run yine Completed'dir — hata sayaç/satırlarla görünür).
- `RunStartedEvent.TotalProjects` = ana + harici sayısı; `BuildPreviewEvent` harici öğeleri sırayla başa ekler.
- Önce testler (RunCoordinatorTests'in internal `Harness`/`FakeInvoker` altyapısıyla, `CycleRoundsTests`
  kardeş-dosya emsaliyle yeni `ExternalRunTests.cs`):
  - `Externals_build_sequentially_before_any_main_project` (sıra + harici span'da MaxConcurrent==1 +
    isteklerde `ExternalTarget=true`);
  - `Up_to_date_external_is_skipped_with_up_to_date_reason`;
  - `External_failure_aborts_the_run_without_building_main_projects` (hiçbir ana `projectStarted` yok;
    runCompleted failed==1, queued==ana sayısı; console satırı yakalanır);
  - `External_failure_skips_remaining_externals`;
  - `External_success_persists_build_state_keyed_by_target_path` (BuiltCommit == revizyon);
  - `Run_preview_and_total_count_include_externals`;
  - `Graceful_stop_between_externals_stops_before_the_next_one`;
  - `Cycles_mode_ignores_external_plans`.
- Mevcut RunCoordinatorTests/CycleRoundsTests DEĞİŞMEDEN yeşil.

### 3.4 Kaynak guard + değişmez dokümantasyonu
- Yeni guard testi (mevcut `SourceGuard` tarayıcı kalıbıyla): `merge`, `--ff-only`, `tf`+`get` dizgeleri
  `src/BuildOrchestrator.Core` altında YALNIZ `Core/Externals/` dosyalarında geçebilir — başka yere pull/merge
  ekleyen herkes kırmızı görür.
- CLAUDE.md "Git salt-okur" maddesi (zaten "ana repoda" der) harici istisna cümlesiyle yeniden yazılır:
  tek istisna kayıtlı harici projelerdir; onların kökünde build anında yalnız ff-only güncelleme
  (`fetch` + `merge --ff-only`) ya da `tf vc get` koşar; bu yüzey `Core/Externals` dışına çıkamaz.
- ARCHITECTURE §10.1 tablosuna harici mutasyon satırları (kapsam: yalnız kayıtlı harici kökler).

## Faz 4 — App: persist + Ayarlar editörü

### 4.1 `UiState.ExternalProjects` + seed + komut bağlama
- `UiState`'e `List<ExternalProject> ExternalProjects = []` (xmldoc: `LayerPatterns`'taki explicit-null
  tehlikesi aynen); MainWindow seed: `is { Count: > 0 }` guard'ı; `RunViewModel.ExternalProjects`
  (LayerPatterns aynası) her iki komutta da (`SyncAsync`, `BeginRunAsync`) taşınır.
- Önce testler: round-trip; `"ExternalProjects": null` token'ı Load'u düşürmez; Sync ve StartRun komutları
  listeyi taşır. Eski ui-state.json dosyaları değişmeden yüklenir.

### 4.2 `SettingsDraftViewModel` harici taslağı (saf VM)
- `ExternalRowViewModel : ObservableObject, IDragReorderItem` — Name/ProjectPath/TargetPath/IsDragging;
  `Vcs` her ProjectPath değişiminde `VcsDetector.DetectRoot` ile yeniden hesaplanır; `VcsLabel` →
  `"git" / "tfvc" / "unknown"` (küçük harf, mono — tek eşleme burada); `IsIncomplete` (ad/yol/hedef boş).
- Ctor `(initialLayers, initialExternals, repositoryRoot)`; `AddExternal(projectPath)` (Name default =
  `Path.GetFileName`, TargetPath = `ExternalTargetResolver.AutoTarget` yoksa boş → picker),
  `RemoveExternal`, `BuildExternals()`; `CanSave &=` tüm harici satırlar tam (satır PropertyChanged bağlama
  kalıbı layers ile aynı); `CommitAsync` persist + genişletilmiş `ApplySettingsAsync`.
- Önce testler: kayıtlı sırada seed; ekleyince VCS + hedef otomatik (GitTestRepo + tek .sln); yol değişince
  rozet güncellenir; eksik satır Save'i kilitler; commit persist + apply çağırır; cancel canlı duruma dokunmaz.

### 4.3 SettingsDialog "EXTERNAL PROJECTS" bölümü (görünüm)
- LAYERS'ın altına (REPOSITORY'nin üstüne), birebir LAYERS kalıbıyla: caption tipografisi
  (`FontSize.2xs`/`FontWeight.Emphasis`/`Brush.TextFaint`), açıklama metni ("Projects outside the repository
  are updated from version control and built first, top to bottom."), 36px kartlar (grip +
  `DragReorderBehavior`, `DragDrop.DoDragDrop` YASAK), `Ds.Input` ad kutusu, mono proje yolu + "Change…",
  mono hedef (sln) + seçici, mono soluk VCS rozeti (yalnız metin — yeni renk/ikon YOK, Tokens.xaml'a
  dokunulmaz), `Ds.IconButton` çöp; ghost "Add external project". Tüm metinler İngilizce.
- `AccessibilityNames`: `ExternalName / ExternalRoot / ExternalTarget / AddExternal / DeleteExternal`.
- Önce testler: caption verbatim pini ("EXTERNAL PROJECTS"); `SettingsDialogHost` ile realize (`[StaFact]`)
  — bölüm + seed'li satır + rozet metni; AccessibilityTests genişletmesi. Mevcut guard'lar
  (NoHardcodedColor/DesignTokenScale/AntiSlop/NoTurkishUserText) yeşil kalmalı.

### 4.4 `ApplySettingsAsync` genişletmesi
- İmza: `(patterns, externals, repositoryRoot)`; hariciler Sync'ten ÖNCE atanır (layers ile aynı sıra
  kuralı — komut taze listeyi taşımalı); console notu `External projects updated — {n} external projects` /
  `External projects removed` (ApplyLayerPatterns aynası); kapılar (mid-run/kök yok/engine yok) aynı;
  **Save başına tam BİR Sync**. `SettingsDialog.Open` çağrısı `run.ExternalProjects`'i taslağa geçirir.
- Önce testler: hariciler tek Sync'ten önce set edilir (fake engine komut sırası/yükü kaydeder); mid-run
  save uygular ama Sync'i erteler.

## Faz 5 — App: node paneli, sha yuvası, graph + kalan doküman

### 5.1 Reconciler'da harici satırlar
- `ProjectRowViewModel.IsExternal { get; init; }`; `OnWorkspaceTopology` insert'te `node.ExternalVcs is not
  null` ile set eder ve **harici satırlara ana repo `TargetSha` push'unu atlar** (hem `OnTargetShaChanged`
  hem satır oluşturma noktası).
- Önce testler: harici node giriş adı + sln görünümüyle satır üretir; TargetSha harici satıra basılmaz;
  hariciler ana satırlardan önce.

### 5.2 Harici sha yuvası
- `ProjectRow` sha yuvası: 40-hex değer 7'ye kısaltılır; diğer her değer (örn. `C48213`) verbatim;
  `TargetSha == null` iken tek değer, çift-ok yok (mevcut tek-taraf render'ı doğrula; yalnız null-sol
  destekleniyorsa null-sağ burada eklenir).
- Önce testler: changeset kısaltılmaz; harici satırda hedef yarısı yok.

### 5.3 Gruplama + graph bandı (yalnız pin)
- Üretim değişikliği beklenmez — davranış başa eklenen node'lardan doğar. Pinler:
  `External_group_comes_first_and_is_named_External`; katman tanımsızken bile "External" başlığı çıkar, ana
  projeler altında başlıksız grup; `GraphBinder`'da −1 katmanı ilk banda düşer (`QuietGraphLayout`
  `SortedDictionary` sırası); harici node'ların kenarı yok. Bir kusur çıkarsa kaynağında düzeltilir.

### 5.4 Doküman + README (aşağıdaki listeyle bu fazın commit'lerinde)

## Contract değişiklikleri (tam liste)

| Yüzey | Değişiklik | JSON toleransı |
|---|---|---|
| `ProjectModels.cs` | yeni `record ExternalProject(Name, ProjectPath, TargetPath)` + `enum VcsKind` | yeni tip; camelCase; enum metni pinli |
| `ProjectNode` | kuyruk `VcsKind? ExternalVcs = null` — **elle yazılmış Equals/GetHashCode'a eklenir** | default → eski NDJSON parse olur; normal node'larda alan yazılmaz (`WhenWritingNull`) |
| `SyncWorkspaceCommand` | kuyruk `IReadOnlyList<ExternalProject>? ExternalProjects = null` | eski satırlar parse; boş/null → özellik tamamen kapalı |
| `StartRunCommand` | aynı kuyruk (PerfMode'dan sonra) | aynı |
| `BuildPreviewItem` / `SkipReasons` | DEĞİŞMEZ — revizyon `BuiltCommit`'te, sebep `UpToDate` | — |
| `UiState` | `List<ExternalProject> ExternalProjects = []` | eksik → default; explicit null → seed guard testli |
| `build-state.json` | harici `TargetPath` anahtarlı yeni girişler; `BuiltCommit` = sha ya da `"C<changeset>"` | ekleyici; eski okuyucular etkilenmez |
| Supervisor `RunPlan` | kuyruk `Externals = null` | süreç içi |
| `MsBuildInvokeRequest` | kuyruk `bool ExternalTarget = false` | süreç içi |

## Doküman güncelleme listesi

- **CLAUDE.md** — "Git salt-okur" değişmezine harici istisna (Faz 3.4 metni); "OutDir'e dokunulmaz / yalnız
  obj izole edilir" maddesine haricilerin her zaman yerinde derlendiği notu.
- **ARCHITECTURE.md** — §1.2/1.3 garanti kapsamı; §5.2/§5.3 yeni alanlar + başa eklenen node'lar; §6.6
  rezerve "External" bandı (LayerIndex −1 en üstte); §7.5 build state (TargetPath anahtarları,
  BuiltCommit'te revizyon); §8.6 planlama hattı (harici faz worktree hazırlığından önce) + §8.8 koordinasyon
  (pre-worker ardışık yürütme, iptal semantiği); §9.2 argüman sözleşmesi (harici varyant); yeni §10.6
  "External project version control"; §13.3 ayarlar bölümü; §16 diskteki durum; §20 "One repository at a
  time" yeniden yazımı; §21.2 girdi yüzeyi tablosuna harici yol/tf.exe satırları; §22 kod haritası.
- **README.md** — kullanım: harici proje kaydı (ad/proje dizini/hedef, sürükleme sırası = derleme sırası,
  VCS rozeti), Build'in önce ne yaptığı, dirty davranışı, TFVC için tf.exe (VS Team Explorer) gereksinimi.

## Risk kaydı (ilk 5)

1. **`ProjectNode` equality kayması** — `ExternalVcs` elle yazılmış Equals/GetHashCode'a eklenmezse
   topoloji-değişimi tespiti sessizce bozulur. *Önlem:* yalnız bu alanda farklı iki node'un eşitsizliğini
   pinleyen özel kırmızı test (2.1).
2. **tf.exe çıktısı / lokal bağımlılığı** — *Önlem:* status XML, history sayı-parse, gerisi exit code;
   hepsi fake-runner testli; bilinmeyen revizyon fazladan derlemeye düşer, asla yanlış skip'e değil.
3. **Run-iptal tutarsızlığı** (asılı run, eksik runCompleted, yanlış sayaç) — *Önlem:* harici yürütme
   tamamen mevcut try/finally içinde; worker'lar hiç doğmaz; finally sayaçları testlerle pinli;
   scheduler'a hariciler için dokunulmaz.
4. **Ana repoya kazara mutasyon** — *Önlem:* kaynak guard'ı (3.4) + tip xmldoc + planner yalnız harici
   köklerini alır; CLAUDE.md çitlenen namespace'i adıyla anar.
5. **Başa eklenen node'ların UI yan etkileri** (graph bandı, sticky gruplar, sha yuvası, TargetSha push,
   Rebuild satır temizliği) — *Önlem:* Faz 5 neredeyse tamamen mevcut davranış pinleri; hariciler mevcut
   kanallarda akar; liste boşken özellik tamamen atıl (her giriş `is { Count: > 0 }` kapılı).

## Kabul / doğrulama senaryoları (uygulama bitince)

1. `dotnet test ... --filter "Category!=Acceptance"` — tam süit yeşil (guard'lar dahil).
2. **Boş liste:** hiçbir davranış değişmedi (Sync/Build/UI aynen; NDJSON'da yeni alanlar yazılmıyor).
3. **Git harici, temiz, geride:** Build → console'da "Updating external …", node üstte "External" grubunda
   derleniyor, sonra ana projeler; ikinci Build → "up to date" skip.
4. **Git harici dirty:** Build → run hiç başlamıyor, console'da adıyla net uyarı; commit sonrası Build normal.
5. **TFVC harici (local workspace):** rozet "tfvc"; Build → tf get + changeset (`C…`) sha yuvasında;
   incremental ikinci Build'de skip.
6. **Harici derleme hatası:** run iptal, hiçbir ana proje başlamıyor, sayaçlar doğru.
7. **Rebuild:** hariciler koşulsuz derleniyor; **Cycles:** hariciler tamamen atlanıyor.
8. **Ayarlar:** ekle/sil/sürükle-sırala; tek .sln otomatik seçimi; eksik satır Save'i kilitliyor; eski
   ui-state.json sorunsuz yükleniyor.
