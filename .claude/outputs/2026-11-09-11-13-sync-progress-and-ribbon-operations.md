# Sync ilerlemesi ve şerit işlem kuralı — TDD dökümü

> **Uygulama:** superpowers:subagent-driven-development (CLAUDE.md: 5+ bulgu). Branch: `feat/sync-progress-and-ribbon-ops`
> (develop `bb69f1b1`'den). Kullanıcı onayı: 2026-11-09 sohbetindeki "eskiden / şimdi" tabloları.

**Amaç:** İlk worktree geçişindeki uzun indexing'i konsolda ve şeritte görünür kılmak; şeritteki pill'i ve yanındaki satırı
her işlemde o işin kendisine bağlamak.

**Kanıt (ölçüm, yalıtılmış motor, `OSYS-Build-Orchestrator`):** soğuk ilk Sync 55,1 sn; `Indexing 23266 source files…`
satırından sonra 51,4 sn hiç olay yok; aynı kopyada ikinci Sync 0,87 sn. Kod okuması: `SWITCHING BRANCH` / `CREATING WORKTREE` /
pull sırasında pill nötr ve önceki koşunun ✓'sini taşıyor, yanındaki satır önceki işin; Deep Clean/Optimize'da satır
`▸ Waiting for Sync — …` (Boot); `-t:Clean` koşusu `▸ Building n/m` diyor; Sync satırı ya baştan sona `▸ Sync — git fetch
origin…` ya da `▸ Sync…`.

## Bulgular (önem sırası)

| # | Önem | Bulgu | Task |
|---|---|---|---|
| B1 | önemli | İlk indexing ~51 sn tek satırdan sonra sessiz; 90 sn sessizlik bekçisi yanlış alarm verebilir | 1, 2 |
| B2 | önemli | Yarıda kalan indexing kaydedilmiyor (yalnız Sync sonunda Flush) | 1 |
| B3 | önemli | Checkout / worktree kurulumu / pull sırasında pill nötr + önceki ✓, satır önceki işin | 4 |
| B4 | önemli | Deep Clean / Optimize'da pill nötr, satır `Waiting for Sync` | 4 |
| B5 | kozmetik | Sync satırı adımı ve sayıyı söylemiyor; fetch bitse de `git fetch origin…` | 2, 3 |
| B6 | kozmetik | `-t:Clean` koşusu `▸ Building n/m` | 4 |
| B7 | kozmetik | İndexing sırasında çubuk belirsiz (oysa payda var); diğer işlerde çubuk önceki koşunun dolgusunda | 3, 4 |

## Global kurallar (her task)

- **Kırmızı önce:** yeni davranışın testi önce gerçek bir assertion ile KIRMIZI gösterilir (gerekirse önce boş iskelet). Bilerek
  değişen davranışı pinleyen eski test silinmez: yeni kurala yeniden yazılır, doc'una `[DEĞİŞEN KURAL]` + eski iddia + gerekçe.
- Kod, UI metni, log: İngilizce. Kod yorumu: Türkçe. Metinler TEK yerde: konsol satırları `PlanProgressLines`, şerit satırları
  `RibbonText`, pill sözcükleri `OperationLabel` (kopya YASAK).
- Dosya düzenleme yalnız Edit/Write (CRLF + UTF-8 korunur; `sed -i`, `Set-Content`, PowerShell yeniden yazımı YASAK).
- Commit mesajı Türkçe, dosyaya yazılıp `git commit -F` ile; Claude attribution satırı YOK.
- Lokal derleme/test kısıtlı: `.claude\temp\bo-build.cmd` (çıktı `.claude\temp\bo-build.txt`), test için
  `$env:BO_FILTER='FullyQualifiedName~<Sınıf>'; $env:BO_OUT='<ad>'; cmd /c .claude\temp\bo-test.cmd` (çıktı
  `.claude\temp\<ad>.txt`). Yalnız dokunulan test sınıfları. Tam süit CI'da.
- Boşta sorgu yok: yeni olaylar yalnız Sync/indexing sürerken akar.

## Review focus

1. Prefill sırasında iptal → `OperationCanceledException` (AggregateException DEĞİL) — Task 1 guard testi.
2. 500 dosyanın altındaki indexing → ne adım olayı ne satır — Task 1 (rapor) + Task 2 (servis).
3. Sessiz Sync (pencereye dönüş) adım olayı alsa da şerit ve pill değişmez — Task 3 testi.
4. Checkout → Sync devri: son hâl pill `SYNC` canlı + Sync satırı, `Switching` asılı kalmaz — Task 4 testi.
5. Düşmüş bir koşunun `Run failed — …` satırı checkout sürerken işin satırına yer verir, reddedilen checkout'tan sonra geri gelir — Task 4 testi.

---

### Task 1: Motor — indexing ilerlemesi ve ara kayıt (B1, B2)

**Files:**
- Create: `src/BuildOrchestrator.Core/Incremental/IndexingReport.cs` (`IPrefillObserver` + `IndexingReport`)
- Modify: `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` (`Prefill`, sabitler, test dikişleri)
- Modify: `src/BuildOrchestrator.Core/Incremental/IncrementalRunBinder.cs:96` (`Prefill` imzası)
- Modify: `src/BuildOrchestrator.Core/Planning/PlanProgressLines.cs` (`IndexingSources`'ın yanına iki metin)
- Modify: `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs:311` ve `src/BuildOrchestrator.Supervisor/Program.cs:196`
  (iki çağıran `new IndexingReport(...)` verir; Sync şimdilik yalnız satır, adım olayı Task 2)
- Test: create `tests/BuildOrchestrator.Tests/Incremental/IndexingReportTests.cs`; modify `tests/BuildOrchestrator.Tests/Incremental/SourceHashCacheTests.cs`

**Interfaces (Produces):**
```csharp
namespace BuildOrchestrator.Core.Incremental;
public interface IPrefillObserver
{
    void Started(int total);                              // okuma başlamadan; yalnız eksik > 0 ise
    void Progress(int done, int total, long elapsedMs);   // iş sürerken her tikte
    void Finished(int total, long elapsedMs);             // iş bitince (hata/iptalde çağrılmaz)
}
public sealed class IndexingReport(Action<string> line, Action<int, int>? progress = null) : IPrefillObserver
{
    public const long HeartbeatIntervalMs = 10_000;
    public bool Reported { get; }                         // Started eşiği geçti mi
}
// PlanProgressLines
public static string IndexingHeartbeat(int done, int total, long elapsedMs);  // $"indexing source files — {done}/{total} · {DurationFormat.Elapsed(elapsedMs)}"
public static string IndexedSources(int files, long elapsedMs);              // $"Indexed {files} source files in {DurationFormat.Elapsed(elapsedMs)}"
// SourceHashCache
public int Prefill(IEnumerable<string> paths, IPrefillObserver? observer = null, CancellationToken ct = default);
internal const int PrefillTickMs = 500;
internal const long PrefillFlushIntervalMs = 10_000;
internal Func<Task, bool>? PrefillTick;          // true = iş bitti; varsayılan Task.WaitAny([work], PrefillTickMs) >= 0
internal Func<long>? PrefillElapsedMs;           // varsayılan Stopwatch
internal Func<string, string?>? PrefillHasher;   // dosya başına iş; varsayılan HashOf
// IncrementalRunBinder
public int Prefill(IPrefillObserver? observer = null, CancellationToken ct = default);
```

**Rapor kuralı (`IndexingReport`):** eşik `SourceHashCache.NoisyPrefillThreshold` (500). `Started(total < 500)` → hiçbir şey,
`Reported=false`. Aksi: `line(IndexingSources(total))`, `progress(0,total)`. `Progress`: sayı değiştiyse `progress(done,total)`;
`elapsed` bir sonraki 10 sn sınırını geçtiyse TEK `line(IndexingHeartbeat(...))` ve sınır `elapsed`'in üstündeki ilk katına.
`Finished`: son bildirilen sayı total değilse `progress(total,total)`; `line(IndexedSources(total, elapsed))`.

**Prefill mekaniği:** eksik küme bugünkü gibi; `observer.Started`; hash işi `Task.Run(() => Parallel.ForEach(...))` içinde,
gövde `PrefillHasher(path)` + `Interlocked.Increment(ref done)`; çağıran thread döngüde `PrefillTick(work)` false oldukça
`observer.Progress(done, total, elapsed)` ve `elapsed >= nextFlush` ise `Flush()` (sonraki sınır `elapsed + PrefillFlushIntervalMs`);
sonra `work.GetAwaiter().GetResult()` (istisna bugünkü türüyle: gövde hatası AggregateException, iptal OperationCanceledException);
`observer.Finished`. Bekleme fırlatmayan biçimde yapılır (`Task.WaitAny`).

- [ ] **Step 1: Testleri yaz (IndexingReportTests — saf, IO yok)**
  - `A_small_index_reports_nothing` — Started(499), Progress(10,499,20_000), Finished(499,30_000) → satır yok, progress yok, Reported false.
  - `A_noisy_index_announces_the_existing_line_and_starts_the_count_at_zero` — Started(600) → satırlar `[IndexingSources(600)]`, progress `[(0,600)]`.
  - `A_heartbeat_line_follows_every_ten_seconds_with_the_count` — Progress(100,600,9_999) satır yok; (200,600,10_000) → `"indexing source files — 200/600 · 10s"`; (300,600,19_000) yok; (400,600,35_000) → TEK satır `"… 400/600 · 35s"`; (500,600,39_999) yok; (550,600,40_000) → `"… 550/600 · 40s"`.
  - `Progress_is_reported_only_when_the_count_moves` — Progress(0,600,500) yok; (5,600,1_000) → (5,600); (5,600,1_500) yok.
  - `The_closing_line_names_the_total_and_the_duration` — … Finished(600,51_400) → son satır `"Indexed 600 source files in 51s"`, `(600,600)` progress'te bir kez.
- [ ] **Step 2: SourceHashCacheTests**
  - Yeniden yaz: `prefill_announces_the_missing_count_once…` → `prefill_tells_the_observer_the_missing_count_once_and_only_reads_what_is_missing` (kayıt eden gözlemci; ikinci geçişte Started çağrılmaz). Doc: `[DEĞİŞEN KURAL]` eski API `Action<int> announce`; gerekçe gözlemcinin ilerlemeyi ve bitişi de taşıması.
  - Yeni `prefill_reports_progress_while_it_hashes_and_finishes_last` — 3 dosya (eski mtime); `PrefillHasher` bir kapıda bekler; tik 1 → false; tik 2 → kapıyı aç, `work.Wait()`, true; `PrefillElapsedMs` sırayla 1_000, 2_000 → gözlemci tam olarak `Started(3)`, `Progress(0,3,1000)`, `Finished(3,2000)`.
  - Yeni `prefill_saves_what_it_has_hashed_while_it_is_still_running` — a, b, c (eski mtime); hasher c'de kapıda bekler, a ve b'den sonra countdown; tik 1: countdown.Wait(), false; `PrefillElapsedMs` ilk çağrıda `PrefillFlushIntervalMs`; tik 2: diskten yeni `SourceHashCache` → a, b `IsCached`, c değil; sonra kapıyı aç, true.
  - Guard `prefill_cancellation_still_surfaces_as_operation_canceled` — iptal edilmiş token → `Assert.ThrowsAny<OperationCanceledException>` (bugün yeşil, öyle kalmalı).
- [ ] **Step 3: Kırmızıyı göster** — önce iskelet (yeni tipler/dikişler, gövde eski davranış: Progress yok, ara Flush yok) → `IndexingReportTests` ve iki yeni SourceHashCache testi assertion ile düşer.
- [ ] **Step 4: Uygula** — `IndexingReport`, Prefill döngüsü, iki metin, iki çağıran (`SyncWorkspaceService`: `new IndexingReport(line => emit(Info(line)))`; `Program`: `new IndexingReport(progress)`).
- [ ] **Step 5: Yeşil** — `IndexingReportTests`, `SourceHashCacheTests`, `IncrementalRunBinderTests`, `SyncWorkspaceServiceTests`.
- [ ] **Step 6: Commit** — `feat(index): ilk indekslemenin ilerlemesini raporla, sürerken ara kayıt al`

### Task 2: Sözleşme + Sync adımları (B1, B5)

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` (enum + olay + `[JsonDerivedType(typeof(SyncStepEvent), "syncStep")]`, `syncDiscovery`'nin yanına)
- Modify: `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` (`RunAsync`, `MeasureRemoteAsync`, `ComputeWillBuild`)
- Test: `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs`, `tests/BuildOrchestrator.Tests/Workspace/SyncWorkspaceServiceTests.cs`

**Interfaces:** Consumes `IndexingReport` (Task 1). Produces:
```csharp
public enum SyncStep { Fetching, Analyzing, Indexing }
public sealed record SyncStepEvent(SyncStep Step, string? Branch = null, int Done = 0, int Total = 0) : IpcEvent;
```
**Yayın:** `MeasureRemoteAsync` fetch edecekse (fetch && branch dolu) `git fetch` satırından HEMEN ÖNCE `Fetching(branch)`;
`RunAsync` HEAD satırından sonra, taramadan önce `Analyzing`; `ComputeWillBuild`
`new IndexingReport(line => emit(Info(line)), (d, t) => emit(new SyncStepEvent(SyncStep.Indexing, Done: d, Total: t)))`,
Prefill'den sonra `Reported` ise yeniden `Analyzing`. Konsol satırları DEĞİŞMEZ.

- [ ] **Step 1:** `IpcMessagesTests.SyncStepEvent_round_trips_with_a_camel_case_step` — `new SyncStepEvent(SyncStep.Indexing, Done: 120, Total: 600)` → JSON'da `"type":"syncStep"`, `"step":"indexing"`; geri okunan eşit. Kırmızı (tip yok) → sözleşmeyi ekle → yeşil.
- [ ] **Step 2: Servis testleri (kırmızı: olay yayınlanmıyor)**
  - `A_fetching_sync_names_its_fetch_step_then_moves_to_the_analysis` — klon + fetch: sıra `Fetching(branch)` < `git fetch origin {branch}` satırı < `Analyzing` < ilk `SyncDiscoveryEvent`.
  - `A_sync_without_fetch_reports_no_fetch_step` — `Fetch: false`: hiç `Fetching` yok, ilk adım `Analyzing`.
  - `A_first_index_reports_its_progress_and_returns_to_the_analysis` — tek SDK projesi + 520 `.cs`, taze cache kökü, `Fetch: false`: Indexing olayları `(0,total)` ile başlar (`total >= 500`), `(total,total)` ile biter, `Done` azalmaz; sonuncunun ardından `Analyzing`; satırlar `IndexingSources(total)` ve `Indexed {total} source files in ` ile başlayan satırı içerir.
  - `A_warm_index_reports_no_indexing_step` — aynı cache köküyle ikinci Sync: Indexing olayı ve `Indexing`/`Indexed` satırı yok.
- [ ] **Step 3:** Uygula → yeşil (`IpcMessagesTests`, `SyncWorkspaceServiceTests`, `SupervisorIpcTests`).
- [ ] **Step 4: Commit** — `feat(sync): Sync'in adımını ve indexing sayısını motordan bildir`

### Task 3: Şerit — Sync satırı ve indexing çubuğu (B5, B7)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RibbonText.cs` (`SyncProgressView`, Sync metinleri, `Compose`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`RibbonLine`, `OnEvent` → `OnSyncStep`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (`SyncProgress`, sıfırlama, `OnSyncStep`; yorumlardaki eski şerit metni)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Discovery.cs` (`OnSyncDiscovery` sayıyı kapıdan ÖNCE `SyncProgress`'e yazar)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Engine.cs:671` (yorum)
- Modify: `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` (`SyncProgress` dinlenir; indexing'de determinate çubuk)
- Test: `RibbonTextTests`, `RunViewModelTests`, `StickyRibbonTests`, `EngineSilenceWatchdogTests` (yalnız yorum)

**Interfaces:** Consumes `SyncStep`, `SyncStepEvent` (Task 2). Produces:
```csharp
public readonly record struct SyncProgressView(SyncStep? Step, string? Branch, int Done, int Total, int ProjectsFound);
public const string SyncStarting = "▸ Sync…";
public static string SyncFetching(string? branch);       // "▸ Sync — fetching origin/{b}…" | boşsa "▸ Sync — fetching origin…"
public static string SyncAnalyzing(int projects);        // ≤0 "▸ Sync — analyzing the workspace…" | 1 "▸ Sync — analyzing 1 project…" | "▸ Sync — analyzing {n} projects…"
public static string SyncIndexing(int done, int total);  // "▸ Sync — indexing source files · {done}/{total}"
public static string SyncLine(SyncProgressView sync);    // Step null → SyncStarting
// Compose: `bool syncFetches` KALKAR, sona `SyncProgressView sync = default` eklenir; Syncing dalı SyncLine(sync)
// SyncingWithFetch / SyncingWithoutFetch KALKAR.
public SyncProgressView SyncProgress { get; }            // RunViewModel, bildirimli; BeginSyncMode ve OnSyncStarted'da default
```
**Çubuk:** `Phase == Syncing` ve `SyncProgress` `{ Step: Indexing, Total: > 0 }` → belirsiz mod kapanır, amber dolgu `Done/Total`;
diğer adımlarda Syncing yine belirsiz.

- [ ] **Step 1: Testler (kırmızı)**
  - RibbonTextTests: `Syncing_line_shows_the_git_fetch_message…` → `A_sync_with_no_step_yet_reads_sync_in_secondary_text` (`"▸ Sync…"`, `Brush.TextSecondary`); doc `[DEĞİŞEN KURAL]`: eski satır Sync boyunca sabitti (`git fetch origin…` / `Sync…`); ölçüm: ilk worktree Sync'inde 55 sn aynı satır.
  - RibbonTextTests: `The_sync_line_names_the_engine_step` (Theory) — `(Fetching,"main")`→`"▸ Sync — fetching origin/main…"`; `(Fetching,null)`→`"▸ Sync — fetching origin…"`; `(Analyzing,0)`→`"▸ Sync — analyzing the workspace…"`; `(Analyzing,1)`→`"▸ Sync — analyzing 1 project…"`; `(Analyzing,177)`→`"▸ Sync — analyzing 177 projects…"`; `(Indexing,11633/23266)`→`"▸ Sync — indexing source files · 11633/23266"`.
  - RunViewModelTests: `A_branch_change_sync_does_not_claim_a_fetch_on_the_ribbon` → beklenen `RibbonText.SyncStarting`, `fetch` içermez.
  - RunViewModelTests: `The_ribbon_follows_the_sync_steps_the_engine_reports` — olaylar sırayla: SyncStarted → `"▸ Sync…"`; Step(Fetching,"main") → fetching; Step(Analyzing) → `analyzing the workspace…`; SyncDiscovery(177,0) → `analyzing 177 projects…` (keşif bloğu açık olmasa da); Step(Indexing,0,600) → `· 0/600`; (300,600) → `· 300/600`; Step(Analyzing) → `analyzing 177 projects…`; topoloji + SyncCompleted → `▸ Ready…`.
  - RunViewModelTests: `A_silent_sync_leaves_the_ribbon_alone_while_it_indexes` — mevcut sessiz Sync testinin düzeneğiyle: sessiz Sync'e Step(Indexing,…) gelir → `RibbonLine.Text` ve `CurrentOperation` değişmez.
  - StickyRibbonTests: `The_ribbon_redraws_when_the_exit_starts_waiting` ön-koşulu `RibbonText.SyncStarting`.
  - StickyRibbonTests: `While_the_sync_indexes_the_progress_bar_fills_with_the_count` — SyncStarted → belirsiz; Step(Indexing,150,600) → `IsIndeterminate` false, gösterge genişliği ≈ 0,25 × iz genişliği; Step(Analyzing) → yeniden belirsiz.
- [ ] **Step 2:** Uygula → yeşil (`RibbonTextTests`, `RunViewModelTests`, `StickyRibbonTests`, `SyncDiscoveryStateTests`, `DiscoveryBlockTests`, `HiddenDecorativeClockTests`, `ReducedMotionCoverageTests`).
- [ ] **Step 3: Commit** — `feat(ribbon): Sync satırı motorun adımını ve indexing sayısını söyler`

### Task 4: Şerit — pill her işte canlı, satır o işi anlatır (B3, B4, B6, B7)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RibbonText.cs` (`RibbonWork`, iş satırları, `Compose` sırası, Cleaning)
- Modify: `src/BuildOrchestrator.App/ViewModels/OperationLabel.cs` (`ForWork`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (`WorkInFlight`, `WorkTarget`, bildirim `NotifySyncGatedCommands`'ta; `OnCleanStarted` yorumu)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Esc.cs` (`UnstoppableOperation => OperationLabel.ForWork(WorkInFlight)`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`RibbonLine`: `work`, `workTarget`, `cleaning: _currentRunMode == RunMode.Clean`)
- Modify: `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` (`WorkInFlight` dinlenir; canlılık; çubuk)
- Modify: `src/BuildOrchestrator.App/Shell/KeyboardShortcuts.cs:40` (yorum: worktree kurulumu da)
- Test: `RibbonTextTests`, `StickyRibbonTests`, `EscStopTests`, `RunViewModelTests`

**Interfaces:** Consumes `SyncProgressView`, `SyncLine` (Task 3). Produces:
```csharp
public enum RibbonWork { None, Sync, Checkout, Pull, CreatingWorktree, DeepClean, Optimize }
// RunViewModel (bildirimli: NotifySyncGatedCommands içinde OnPropertyChanged(nameof(WorkInFlight)))
public RibbonWork WorkInFlight => IsMidRunLocked ? None : CleanBusy ? DeepClean : OptimizeBusy ? Optimize
    : CheckoutBusy ? Checkout : WorktreeBusy ? CreatingWorktree : PullBusy ? Pull
    : SyncBusy && _syncMode.IsVisible() ? Sync : None;
internal string? WorkTarget;   // Checkout → _checkoutRequest?.Target.Name; Pull → Branch (boşsa null); diğerleri null
// OperationLabel
public static string? ForWork(RibbonWork work); // None→null; Sync|Pull→SYNC; Checkout→SWITCHING BRANCH; CreatingWorktree→CREATING WORKTREE; DeepClean→DEEP CLEAN; Optimize→OPTIMIZE
// RibbonText
public static string Switching(string? branch); // "▸ Switching to {b}…" | "▸ Switching branch…"
public static string Pulling(string? branch);   // "▸ Pulling origin/{b}…" | "▸ Pulling…"
public const string CreatingTheWorktree = "▸ Creating the worktree…";
public const string DeepCleaning = "▸ Deep clean — removing bin and obj…";
public const string Optimizing = "▸ Optimize — repairing the workspace…";
// Compose sona: RibbonWork work = RibbonWork.None, string? workTarget = null, bool cleaning = false
// Sıra: engineDied → engineOverdue → exitPending → engineWaking → !hasWorkspace → WORK (Sync → SyncLine) → syncError → runError → faz
// Running: cleaning ise "▸ Cleaning {n}/{m} · {elapsed}{eta}"
```
**Pill:** `live = IsMidRunLocked || Phase == Syncing || WorkInFlight != None`. **Çubuk:** `WorkInFlight != None` → belirsiz
(Task 3'ün indexing istisnası önce gelir).

- [ ] **Step 1: Testler (kırmızı)**
  - RibbonTextTests: `A_workspace_operation_in_flight_replaces_the_previous_result_line` — Done + başarılı sayaçlar + `work: Checkout, workTarget: "feature/x"` → `"▸ Switching to feature/x…"`, glyph null; Theory diğer işler (`Pull,"main"`→`"▸ Pulling origin/main…"`, `CreatingWorktree`, `DeepClean`, `Optimize`).
  - RibbonTextTests: `A_run_failure_gives_way_to_the_operation_in_flight` — `runError: "msbuild crashed", work: DeepClean` → `DeepCleaning`; `work: None` → `"Run failed — msbuild crashed"`.
  - RibbonTextTests: `The_engine_waking_line_still_comes_before_an_operation` — `engineWaking: true, work: Sync` → `StartingTheEngine`.
  - RibbonTextTests: `A_clean_run_counts_what_it_cleans` — Running, willBuild 177, finished 3, elapsed 12_000, `cleaning: true` → `"▸ Cleaning 3/177 · 12s"`.
  - StickyRibbonTests: `A_branch_switch_lights_the_pill_and_says_where_it_is_going` — biten başarılı koşu (pill ✓) → `DebugSendOverride = _ => Task.CompletedTask` ile `SelectBranch(feature/x)`: OpText `SWITCHING BRANCH`, zemin `Brush.AmberSoft`, spinner görünür, OpGlyph gizli, PhaseText `"▸ Switching to feature/x…"`, `IsIndeterminate` true; ardından `CheckoutCompletedEvent(Dirty)` → pill düşer, PhaseText koşunun `Completed — …` satırına döner.
  - StickyRibbonTests: `A_checkout_that_hands_over_to_its_sync_leaves_the_sync_line` — `CheckoutCompletedEvent(Switched)` → OpText `SYNC`, canlı, PhaseText `RibbonText.SyncStarting`.
  - RunViewModelTests: `Deep_clean_and_optimize_say_what_they_do_on_the_ribbon` — gönderimi tutan dikişle CleanCommand / OptimizeCommand → `RibbonLine.Text` `DeepCleaning` / `Optimizing`, `WorkInFlight` doğru.
  - RunViewModelTests: `A_clean_run_reads_cleaning_on_the_ribbon` — RunStarted(Clean) + önizleme → satır `"▸ Cleaning 0/"` ile başlar.
  - EscStopTests: `Esc_while_the_worktree_is_created_explains_once` — `WorktreeSettingsTests` düzeneğiyle kurulum uçuşta → `EscRunState.Unstoppable`, `NoteEscCannotStop` satırı `"creating worktree can't be stopped — it will finish on its own"` bir kez.
- [ ] **Step 2:** Uygula → yeşil (`RibbonTextTests`, `StickyRibbonTests`, `EscStopTests`, `RunViewModelTests`, `BranchCheckoutTests`, `CleanCommandTests`, `OptimizeCommandTests`, `WorktreeSettingsTests`, `EngineWakeTests`, `MaintenanceBoxTests` varsa).
- [ ] **Step 3: Commit** — `feat(ribbon): pill her işte canlı, yanındaki satır o işi anlatır`

### Task 5: Dokümanlar (aynı iş)

- ARCHITECTURE.md: §4.6 (yanlış alarm cümlesi + `git fetch origin…` örneği), §5.3 (`syncStep`), §7.1 (ilk indexing: 10 sn satır, kapanış satırı, ara kayıt),
  §10.2 (Sync'in şerit satırı), §10.3/§10.6 (pill canlı + satır), §13.2 (pill paragrafı, çubuk paragrafı), §16 (source-hash satırı), §22 (IndexingReport,
  OperationLabel listesine CREATING WORKTREE).
- docs/USER-GUIDE.md: Sync paragrafı (şerit adımı söyler, ilk indexing sayılır), worktree paragrafı (ilk geçiş bir kez tüm kaynakları indeksler).
- docs/FEATURES.md + FEATURES.tr.md: "Says where the build stands" etiket listesine CREATING WORKTREE; amber cümlesi artık doğru (dokunma, yalnız liste).
- Kontrol: `DocumentationTests` (dil aynası, bölüm sayısı).
- Commit: `docs: Sync ilerlemesi ve şerit işlem kuralı`

### Task 6: Doğrulama, CI, merge (ana oturum)

- Dokunulan test sınıflarının toplu lokal koşusu (kısıtlı), branch'i `origin/develop` üstüne rebase, push, CI yeşil (curl);
  `develop`'a merge + push, develop CI yeşil; branch'i local ve remote'tan sil; oturum `develop`'ta biter.
