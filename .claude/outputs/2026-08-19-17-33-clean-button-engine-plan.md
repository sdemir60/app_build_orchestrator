# Clean Butonuna Motor Takma — Uygulama Planı

> Bu plan başka bir makinede (Fable) hazırlandı; uygulamayı Opus güncel koda göre yapacak.
> **Tüm satır numaraları YAKLAŞIKTIR** — kod değişmiş olabilir. Dosya adları ve kalıplar bağlayıcıdır;
> satır numaraları yalnız arama ipucudur, uygulamadan önce tazelenir.

Hedef: `MaintenanceBox`'taki `PART_Clean`'i (sync'in sağındaki bakım kutusu, silgi ikonu, bugün kalıcı
disabled) gerçek bir `cleanWorkspace` IPC komutuna bağlamak. İş mantığı Core'da, Supervisor yalnız wiring,
App yalnız komut gönderir + event gösterir.

## Ürün kapsamı (kullanıcı kararları)

- **Silinen:** aktif workspace'in (RootPath) keşfedilmiş projelerinin `bin\` ve `obj\` klasörleri + o
  workspace'in `build-state.json` kayıtları.
- **Silinmeyen:** `packages\`, ortak OutDir, worktree havuzu (`_obj` dahil), run logları, `.tmp`'ler,
  `evaluation-cache.json`, `ui-state.json`.
- **UX:** onay dialogu YOK — tıklar tıklamaz çalışır. Console sıfırlanır, ilerleme satır satır console'a
  akar, event stream'e tek bitiş özeti düşer. Görsel tasarım/animasyon bu işin kapsamı DIŞINDA (Claude
  Design'da sonra yapılacak); yalnız işlevsel akış kurulur.
- NuGet restore/cache işleri ileride **Optimize** butonunun konusudur — bu işte yalnız doküman notu düşülür.

## 0. Bağlayıcı kararlar (K-1…K-10)

**K-1. `/t:Clean` EKLENMEZ; yalnız dosya sistemi silme.**
OSYS eski-stil projelerde `OutputPath=bin` varsayılanı + post-build copy kullanır: `/t:Clean`'in sildiği
küme (`FileListAbsolute.txt` kayıtlıları) bin/obj FS silmenin alt kümesidir. Ayrıca (a) obj silinince
FileListAbsolute da gider — iki adımın sırası çözülemeyen bağımlılık üretir; (b) tracked çıktılar ortak
OutDir'e yazılmışsa `/t:Clean` oradan da siler → "OutDir'e dokunulmaz" değişmezi ihlali; (c) solution
başına MSBuild spawn maliyeti sıfır kazanca çalışır. Tooltip metni buna göre değişir (T6).

**K-2. build-state temizliği workspace-scoped: RootPath prefix filtresi.**
`build-state.json` globaldir (`BuildStateStore.cs` ~:28 — `<cacheRoot>\build-state.json`, anahtar = tam
csproj yolu). Tüm dosyayı silmek başka workspace'in state'ini de yok ederdi; `RootPath + '\'` öneki
(OrdinalIgnoreCase) tam isabetlidir ve silinmiş/yeniden adlandırılmış projelerin artık kayıtlarını da
süpürür. `evaluation-cache.json`'a DOKUNULMAZ.

**K-3. Proje listesi kaynağı: taze `WorkspaceScanner.Scan`, son sync sonucu DEĞİL.**
Supervisor sync sonucunu hiçbir yerde saklamaz (`SupervisorHost.SyncWorkspaceAsync` ~:130-146 yalnız event
köprüler; `WorkspaceServices` ~:21-35 durumsuz fabrika). Scan ucuz ve deterministiktir
(bin/obj/.git/.vs/node_modules atlar, sıralı döner) ve Sync yapılmamış workspace'te de çalışır. Csproj
değerlendirmesi GEREKMEZ (bin/obj csproj'un yanındadır) → `CsprojEvaluator`/`EvaluationCache` bağımlılığı
alınmaz.

**K-4. Supervisor'da komut döngüsü BLOKLANIR (Sync kalıbı), arka plan task açılmaz.**
`SyncWorkspaceAsync`'in gerekçesi aynen geçerli: komut serileşmesi Build/Sync yarışlarını bedavaya kapatır.
Silme I/O-bound'dur; proje başına progress satırı App'in 90 sn sessizlik watchdog'unu besler. Ek güvence:
App kapıları yarışa açık olduğundan Supervisor tarafında **run aktifken `cleanWorkspace` reddedilir** →
`error(cleanRejected)`. Bunun için `RunCoordinator`'a salt-okur `IsRunActive` probe eklenir (`_runActive`
alanı `_gate` kilidi altında okunur; `_finishing` dahil edilmez — drain sırasında da red doğru davranış).

**K-5. Yeni event üçlüsü: `cleanStarted` / `cleanProgress` / `cleanCompleted`.**
`SyncProgressEvent` yeniden KULLANILMAZ: `PlanProgressEvent` emsali geçerli — Clean satırları Sync
transkripti değildir ve `_syncInFlight` yüzeyine karışmamalıdır. `CleanProgressEvent(Line, Level)` imzası
`SyncProgressEvent` ile aynıdır (Level: "dim"/"info"/"warn").

**K-6. Hata modeli:** kilitli/silinemeyen dosya **hata DEĞİLDİR** — dosya başına toplanır, warn satırı +
`cleanCompleted.LockedFileCount` olarak raporlanır, akış devam eder. IPC sınırına yalnız iki kod çıkar:
`cleanFailed` (beklenmeyen exception — `planFailed` catch-all ikizi) ve `cleanRejected` (run uçuşta).
Exception IPC sınırını ASLA geçmez.

**K-7. App kapıları (karşılıklı dışlama):** Sync guard'ın birebir simetriği
(`_syncRequested`/`_syncInFlight`/`SyncBusy`, `RunViewModel.Workspace.cs` ~:100-118):
`_cleanRequested` + `_cleanInFlight` + `CleanBusy`.
- `CanClean() => HasWorkspace && !IsRunning && !IsStarting && !IsEngineUnavailable && !SyncBusy && !CleanBusy`
- `CanSync()` ve `CanRebuildOrRetry()` sonuna `&& !CleanBusy` (CanBuildCycles kalıtır).
- Yeni `AppPhase` EKLENMEZ; anlatı konsol satırlarıyla taşınır. `WaitingOnEngine` `|| CleanBusy` alır ki
  donmuş motorda "Restart engine" kapısı görünsün.

**K-8. Konsol/stream sıfırlama:** mevcut kalıp `BeginRunAsync`'in clearBuffers bloğudur (~:569-577,
`lock(_gate)` altında `_liveLines/_projectText/_runText/...`). Bu blok **`ClearConsoleBuffers()`
helper'ına çıkarılır** (kopya yasak); hem `BeginRunAsync` hem `CleanAsync` kullanır. Event stream tamponu
bilinçli korunur (mevcut sözleşme); Clean bitişte tek özet satırı push eder.

**K-9. Silme sırası: ÖNCE state, SONRA klasörler.**
Klasörler önce silinip state kalsaydı, imzası "güncel" görünen ama bin'i silinmiş proje pre-skip
edilebilirdi (bayat çıktı — tehlike). State önce giderse en kötü durum `NeverBuilt` → over-build = güvenli.

**K-10. Silinen küme:** yalnız keşfedilen her csproj klasörünün `bin\` ve `obj\`'i. Worktree havuzu ve
`_obj` DOKUNULMAZ (RootPath dışındadır ve kendi yaşam döngüsü vardır: LRU 20 GiB + deleteWorktree).

---

## Task 0 — İş branch'i aç

`feature/clean-engine` gibi bir branch; task başına commit; sonda main'e merge + push + branch temizliği.

## Task 1 — IPC sözleşmesi (Contracts)

**Dosya:** `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs`

**Önce KIRMIZI test** — `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs` (mevcut kalıp ~:10-51):
- `CleanWorkspace_roundtrips_with_discriminator` — serialize → `"type":"cleanWorkspace"`; deserialize →
  `CleanWorkspaceCommand`, RootPath korunur.
- `Clean_events_roundtrip_with_discriminators` — üç event `Event_roundtrip_all_types` deseninde. (Tipler
  yokken derlenmez = kırmızı.)

**Implementasyon:**
- Komut whitelist'ine (~:28): `[JsonDerivedType(typeof(CleanWorkspaceCommand), "cleanWorkspace")]`.
- `public sealed record CleanWorkspaceCommand(string RootPath) : IpcCommand;` — XML doc: neyi siler/neyi
  silmez (K-10), `/t:Clean` çağrılmadığı (K-1), run uçuştayken `cleanRejected` (K-4).
- Event whitelist'ine üç `JsonDerivedType` ("cleanStarted"/"cleanProgress"/"cleanCompleted").
- Record'lar (Sync event'lerinin yanına):
  - `public sealed record CleanStartedEvent(string RootPath) : IpcEvent;`
  - `public sealed record CleanProgressEvent(string Line, string Level) : IpcEvent;` — doc:
    `SyncProgressEvent` ikizi ama Sync yüzeyine AİT DEĞİL.
  - `public sealed record CleanCompletedEvent(int ProjectCount, int FoldersRemoved, long BytesRemoved, int LockedFileCount, int StateEntriesCleared) : IpcEvent;`

**Kabul:** yeni testler + mevcut Ipc süiti yeşil; `Unknown_discriminator_throws` etkilenmez.

## Task 2 — Core: `BuildStateStore.RemoveUnderRoot` (T1'den bağımsız, paralel yapılabilir)

**Dosya:** `src/BuildOrchestrator.Core/State/BuildStateStore.cs`

**Önce KIRMIZI testler** (mevcut BuildStateStore test dosyasına — Glob ile bul):
- `RemoveUnderRoot_deletes_only_entries_under_the_given_root` — iki köke ait kayıt; biri temizlenir, öteki
  AYNEN kalır; dönüş = silinen sayı.
- `RemoveUnderRoot_matches_case_insensitively_and_normalizes_the_trailing_separator` — `C:\repo` vs
  `c:\REPO\` aynı kökü siler; **`C:\repo2\...` (prefix tuzağı) SİLİNMEZ** — ayraç eklenmiş prefix garanti eder.
- `RemoveUnderRoot_with_no_matching_entries_returns_zero_and_does_not_rewrite_the_file` — dosya
  mtime/içerik değişmez.
- `RemoveUnderRoot_on_a_missing_file_returns_zero`.

**Implementasyon:** `public int RemoveUnderRoot(string rootPath)` — `Upsert` (~:95-122) deseni:
`_writeGate` altında `Load()` →
`string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;`
→ `StartsWith(prefix, OrdinalIgnoreCase)` anahtarlar çıkarılır → 0 ise yazmadan dön → değilse temp +
`MoveAtomicWithRetry` (~:145). Bozuk yol fırlatmaz (`GetFullPath` hatası → 0; Load'un never-throw
sözleşmesiyle uyumlu).

**Kabul:** yeni + mevcut State testleri yeşil; `Upsert`/`Load` davranışı değişmez.

## Task 3 — Core: `CleanWorkspaceService` (T1 + T2'ye bağlı)

**Yeni dosya:** `src/BuildOrchestrator.Core/Workspace/CleanWorkspaceService.cs` —
`SyncWorkspaceService`'in kardeşi olarak aynı klasör/namespace (tek dosyalık `Core/Maintenance/` açılmaz;
Optimize gelirse birlikte taşınır).

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/Workspace/CleanWorkspaceServiceTests.cs`
(temp dizinde sahte workspace — `SyncStreamingTests.SeedWorkspace` ~:79-88 dosya-dökme deseni; git
GEREKMEZ, düz klasör yeter):
- `Clean_removes_bin_and_obj_of_every_discovered_project_and_reports_the_summary` — 2 proje × bin+obj;
  klasörler yok olur, `CleanCompletedEvent` sayaçları doğru (FoldersRemoved=4, BytesRemoved>0), sıra
  `cleanStarted → progress* → cleanCompleted`.
- `Clean_does_not_touch_packages_outdir_or_anything_outside_project_folders` — kökte `packages\`,
  `Output\` (ortak OutDir taklidi), `src\A\Properties\` → hepsi AYNEN durur.
- `Clean_resets_the_build_state_before_deleting_folders` — kök-altı + kök-dışı kayıt; sonra kök-altı yok,
  kök-dışı durur, `StateEntriesCleared` doğru. ("Önce state" sırası, silme adımını fırlatan sahte bir
  kilitle bile asserte edilebilir.)
- `A_locked_file_is_reported_and_skipped_without_stopping_the_clean` — bir dosya `FileShare.None` ile açık;
  akış devam eder, öteki proje temizlenir, `LockedFileCount ≥ 1`, warn satırı çıkar, exception yok.
- `Clean_deletes_a_reparse_point_without_recursing_into_its_target` — bin içine junction/symlink; hedefin
  İÇERİĞİ durur, link gider. (Ortam izin vermezse skip-with-reason.)
- `A_missing_workspace_root_becomes_a_defined_error_event` — `error(cleanFailed)`, exception yok
  (`SyncWorkspaceService` ~:58-62 kapı deseni).
- `Two_csproj_in_the_same_folder_clean_that_folder_once` — dedupe.
- `A_workspace_with_no_projects_completes_with_zero_counters` — hata değil.
- `The_delete_retry_delay_is_injectable` — D8: `DeleteRetryDelay` dikişi senkron sinyale bağlanır
  (`BuildStateStore.RenameRetryDelay` ~:40 deseni), gerçek bekleme yok.

**Implementasyon:**

```csharp
public sealed class CleanWorkspaceService(WorkspaceScanner scanner, BuildStateStore stateStore)
{
    internal Action<int>? DeleteRetryDelay { get; set; }   // D8 dikişi
    public void Run(CleanWorkspaceCommand cmd, Action<IpcEvent> emit, CancellationToken ct = default)
}
```

Akış:
1. `emit(new CleanStartedEvent(cmd.RootPath))`.
2. Kapı: `Directory.Exists(RootPath)` değilse
   `emit(new ErrorEvent("cleanFailed", $"Workspace root not found: '...'."))` + return.
3. `scanner.Scan(RootPath)` → csproj klasörleri `OrdinalIgnoreCase` dedupe (scan zaten sıralı → determinizm).
4. **State önce (K-9):** `int cleared = stateStore.RemoveUnderRoot(cmd.RootPath);` → info satırı:
   `build state reset — {cleared} entries cleared, the next build compiles from scratch`.
5. Proje başına (`ct.ThrowIfCancellationRequested()` döngü başında), `<dir>\bin` ve `<dir>\obj`:
   - Reparse point (junction/symlink) → recurse ETME, yalnız girdiyi sil (hedef korunur — güvenlik).
   - Aksi halde dosya-dosya: ReadOnly attribute temizle, `SyncRetry.Run(File.Delete, attempts:3,
     isTransient: IOException|UnauthorizedAccessException, delay: DeleteRetryDelay ?? kısa sabit,
     rethrowWhenExhausted:false)`; başarısız → hata listesine + devam; başarılı → bayt sayacına boyut.
   - Alt klasörler bottom-up best-effort `Directory.Delete`.
   - Proje satırı (dim): `{ad} — bin + obj removed ({boyut})`; kilitli varsa (warn):
     `warning: {n} files in use under {repo-göreli yol} — skipped`.
   - Boyut metni: Core'da mevcut byte-formatlayıcıyı ARA ve yeniden kullan (worktree boyutları bir yerde
     formatlanıyor; kopya yasak) — yoksa serviste tek statik.
   - Savunma kapısı: silinecek her yol `Path.GetFullPath` sonrası RootPath altında VE son segmenti
     "bin"/"obj" olmalı (defense in depth).
6. Özet (info): `Clean complete — {P} projects · {F} folders · {size} removed`; kilitli varsa ek warn:
   `warning: {n} files could not be removed (in use) — close the running application and run Clean again`.
7. `emit(new CleanCompletedEvent(P, F, bytes, lockedCount, cleared))`.

Tüm kullanıcı metinleri İNGİLİZCE (NoTurkishUserTextTests). Progress factory'leri (`Info/Dim/Warn`)
`CleanProgressEvent` üreten private statikler (`SyncWorkspaceService` ~:234-237 kalıbı).

**Kabul:** yeni Workspace testleri yeşil; `StaleObjRunStartWarnerTests` etkilenmez (obj gidince
`StaleObjDetector.Inspect` "obj yok → temiz" döner — Clean uyarının KÖKÜNÜ temizler, kod değişmez; not T7
dokümanına girer).

## Task 4 — Supervisor wiring (T3'e bağlı)

**Dosyalar:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` — salt-okur probe (`TryRequestStop` ~:275-293
  komşusu): `public bool IsRunActive { get { lock (_gate) return _runActive; } }` (doc: cleanWorkspace
  guard'ının TEK tüketicisi; `_finishing` dahil edilmez).
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs`:
  - `WorkspaceServices` record'ına (~:21-35) `Func<string, CleanWorkspaceService> Clean` parametresi;
    `Default`'a `root => new CleanWorkspaceService(new WorkspaceScanner(), new BuildStateStore(cacheRoot))`.
  - Dispatch switch'e (~:97-98 `SyncWorkspaceCommand` komşusu)
    `case CleanWorkspaceCommand c: await CleanWorkspaceAsync(c, ct); break;`.
  - Handler, `SyncWorkspaceAsync` (~:130-146) deseninin Clean sürümü: önce
    `if (coordinator.IsRunActive) { error("cleanRejected", "a run is in flight — stop it before cleaning"); return; }`;
    sonra `Emit` köprüsü + `catch (Exception ex when not OCE) → error("cleanFailed", ex.Message)`.
- `WorkspaceServices` doğrudan kuran testler (`SupervisorIpcTests`, `SyncStreamingTests`) yeni parametreyle
  güncellenir.

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/Supervisor/CleanDispatchTests.cs`
(`SyncStreamingTests`'in host-in-memory kalıbı: MemoryStream stdin, gözlemlenen stdout, gerçek
SupervisorHost + gerçek servis; gerçek process YOK):
- `CleanWorkspace_streams_started_progress_and_completion_in_order_and_deletes_bin_obj` — telde NDJSON
  parse (`ParseWire` deseni), sıra + diskte klasörlerin gittiği.
- `CleanWorkspace_is_rejected_with_cleanRejected_while_a_run_is_active_and_deletes_nothing` — koordinatörü
  aktif-run durumuna sokan mevcut altyapı (RunCoordinatorTests'teki sahte planner/msbuild fabrikaları);
  bin/obj yerinde durur.
- `An_unexpected_clean_exception_becomes_error_cleanFailed_not_a_crash` — fırlatan sahte servis; host
  yaşamaya devam eder (badCommand/planFailed testlerinin deseni).

**Kabul:** Supervisor süiti yeşil; stdout YALNIZ NDJSON (ParseWire tüm satırları çözer).

## Task 5 — App VM: `CleanCommand`, kapılar, konsol + stream (T1'e derlenme, T4'e davranışça bağlı)

**Dosyalar ve noktalar:**
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs`:
  - `ClearConsoleBuffers()` helper — `BeginRunAsync`'in ~:569-577 bloğu taşınır, iki çağıran (kopya yasak).
  - `[RelayCommand(CanExecute = nameof(CanClean))] private async Task CleanAsync()` — `SyncAsync`
    (~:667-700) simetriği: `SelectedProjectId = null` (filtre korunur); `ClearConsoleBuffers()`;
    `AppendRunLine(CleanRequestedLine)`; `_cleanRequested = true; CleanCommand.NotifyCanExecuteChanged();
    ArmEngineWatchdog();` →
    `bool sent = await TrySendAsync(new CleanWorkspaceCommand(RootPath), "clean"); if (!sent) ReleaseCleanRequest();`
  - `internal static string CleanRequestedLine => "clean requested";` (`RunRequestedLine` ~:602 deseni).
  - `CanClean()` — K-7 metni; `CanSync()` (~:705) ve `CanRebuildOrRetry()` (~:632) `&& !CleanBusy`.
  - Attribute zincirleri: `_isRunning`, `_isStarting`, `_engineDiedMessage`, `_engineRestartable`,
    `_rootPath` alanlarına `[NotifyCanExecuteChangedFor(nameof(CleanCommand))]` — repo kapısı komuttadır;
    MaintenanceBox'a `RefreshEnabled` benzeri akış GEREKMEZ ("kutunun enable'ı kendi kontrolündedir"
    sözleşmesi korunur, tek yazıcı komut olur).
  - `WaitingOnEngine` (~:972) `|| CleanBusy`.
  - `OnEvent` switch (~:1018-1027): `CleanStartedEvent → OnCleanStarted()` ·
    `CleanProgressEvent e → AppendRunLine(e.Line)` · `CleanCompletedEvent e → OnCleanCompleted(e)`.
  - `OnError` içinde `TryConsumeSyncFailure` komşusuna `TryConsumeCleanFailure(code, message)` (kodlar
    ayrık — sıra kritik değil).
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (sync guard'ın yanı — karşılıklı
  dışlama tek dosyada okunur):
  - `_cleanRequested`, `_cleanInFlight`, `private bool CleanBusy => _cleanRequested || _cleanInFlight;`,
    `internal bool CleanRequested/CleanInFlight` test dikişleri (`SyncRequested` ~:118 deseni).
  - `OnCleanStarted()` — `_cleanInFlight = true; _cleanRequested = false; NotifySyncGatedCommands();`
    (faz DEĞİŞMEZ, K-7).
  - `OnCleanCompleted(e)` — `ReleaseCleanSurface();` (+ stream özeti).
  - `ReleaseCleanSurface()` — iki bayrağı temizler + `NotifySyncGatedCommands()`; çağıranlar:
    OnCleanCompleted, `TryConsumeCleanFailure`, `ReleaseAfterEngineLoss` (~:1366-1390 —
    `ReleaseSyncPhase()` çağrısının hemen yanına; motor mid-clean ölürse kapı sızmaz).
  - `ReleaseCleanRequest()` — senkron gönderim hatası yolu (`ReleaseSyncRequest` ~:220-224 ikizi).
  - `TryConsumeCleanFailure(code, message)` — `CleanErrorCodes = {"cleanFailed","cleanRejected"}` &&
    `CleanBusy` ise bayrakları bırak + notify + true (`TryConsumeSyncFailure` ~:284-299'un basit hâli; faz
    dalları yok).
  - `NotifySyncGatedCommands()` (~:210-216) listesine `CleanCommand.NotifyCanExecuteChanged();` — tek
    yazıcı liste büyür, ikinci liste AÇILMAZ.
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` + `StreamText`:
  `case CleanCompletedEvent e:` →
  `PushStream(StreamKind.Info, null, StreamText.CleanCompleted(ProjectCount, FoldersRemoved, BytesRemoved, LockedFileCount))`
  (`SyncCompletedEvent` ~:201-203 deseni; metin TEK yerde — StreamText; kind seçiminde implementer enum'a
  bakıp Sync satırıyla aynı görsel tonu seçsin).

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/App/CleanCommandTests.cs`
(RunViewModelStateTests kalıbı: gerçek Supervisor YOK, `vm.OnEvent(...)` + `DebugOnCommandSent`):
- `Clean_sends_cleanWorkspace_with_the_workspace_root`.
- `Clean_clears_the_console_and_writes_the_requested_line` — önceden satır doldur; Execute sonrası konsolda
  yalnız "clean requested".
- Kapı matrisi: `Clean_is_disabled_without_a_workspace / while_a_run_is_starting_or_running /
  while_a_sync_is_in_flight / while_the_engine_is_unavailable`.
- `Sync_build_rebuild_and_cycles_are_disabled_while_a_clean_is_in_flight_and_reopen_on_completion` —
  `OnEvent(CleanStartedEvent)` → dört komut false; `OnEvent(CleanCompletedEvent)` → geri açılır (istek
  penceresi dahil: Execute'tan hemen sonra da kapalı — `_cleanRequested`).
- `A_failed_send_reopens_the_clean_gate` — motor hazır değilken `sent=false` yolu.
- `A_cleanFailed_or_cleanRejected_error_releases_the_clean_surface` — iki kod için.
- `Engine_death_mid_clean_releases_the_clean_surface` — `OnEngineExited(...)`.
- `Clean_progress_lines_flow_into_the_run_document` ve `Clean_completion_pushes_one_stream_summary_line`.
- Watchdog: `EngineSilenceWatchdogTests`'e `A_silent_engine_during_a_clean_raises_the_overdue_gate`
  (mevcut Sync sessizlik testinin ikizi).

**Kabul:** yeni testler + RunViewModelStateTests/EnginePreflightTests/EngineSilenceWatchdogTests yeşil.

## Task 6 — MaintenanceBox + tooltip (T5'e bağlı)

**Dosyalar:**
- `src/BuildOrchestrator.App/AccessibilityNames.cs` (~:35-43): `CleanTooltip` yeni metin — öneri:
  `Clean — remove every project's bin/ and obj/ and reset the build state; the next build compiles everything from scratch`
  (`/t:Clean` ve `artifacts/` ibareleri KALKAR — K-1/K-10; `NotAvailableSuffix` yalnız `OptimizeTooltip`'te
  kalır; doc yorumu güncellenir.)
- `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`:
  - `Build()` (~:69-91): kalıcı-disabled foreach'ten (~:84-88) Clean çıkar (yalnız `PART_Optimize` kalır);
    `PART_Clean.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(RunViewModel.CleanCommand)))`
    (~:79 Resolve deseni); `ToolTipService.SetShowOnDisabled(PART_Clean, true)` KORUNUR (mid-run
    disabled'da tooltip görünmeli).
  - Sınıf doc'u (~:22-24): "Clean/Optimize pasif" paragrafı → yalnız Optimize.
- XAML kökü değişmediği için yeni realize testi gerekmez.

**Önce KIRMIZI testler** — `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs` (~:91-106'daki
`Clean_and_optimize_are_disabled_...` pini SESSİZCE SİLİNMEZ, ikiye bölünüp yeniden yazılır):
- `Clean_is_wired_to_the_clean_command_and_its_tooltip_names_the_job` —
  `Assert.Same(vm.CleanCommand, box.CleanButton.Command)` (~:159-166 Resolve testi deseni); tooltip tam
  metin; `ToolTipService.GetShowOnDisabled` true.
- `Optimize_stays_disabled_and_says_so_in_a_tooltip_that_shows_while_disabled` — Optimize pinleri aynen.
- `ActionBarTests` sıra testi (~:154-175) davranışça değişmez — koşup doğrula.
- `"not available yet"` literal'ını pinleyen başka test var mı grep'le (TooltipDelayTests'in kendi
  sentetik literal'ına DOKUNULMAZ).

**Kabul:** App süiti yeşil; gerçek pencerede repo yokken Clean pasif (CanClean/HasWorkspace), repo
seçilince aktif.

## Task 7 — Doküman + tam süit + git kapanışı (en son)

- `ARCHITECTURE.md`:
  - §5.2 (~:279-296): komut listesine `cleanWorkspace`; komut sayısı ifadesi güncellenir; kısa paragraf:
    ne taşır, neden `/t:Clean` değil FS silme (K-1), run uçuşta `cleanRejected`, komut döngüsünü Sync gibi
    bloklar.
  - §5.3 (~:298-308): event listesine `Clean: cleanStarted · cleanProgress · cleanCompleted`.
  - Bakım kutusu (~:1299-1305): "Clean and Optimize have no engine behind them yet" → yalnız Optimize;
    Clean'in davranışı bir-iki cümle (bin/obj + state reset; OutDir/packages/worktree havuzuna dokunmaz;
    kilitli dosyalar warn'lanır, akış durmaz; StaleObj uyarısının kökünü temizler).
  - §16 (~:2235-2250): `build-state.json` satırına "Clean, aktif workspace'in kayıtlarını kaldırır
    (workspace-scoped reset)" notu.
- `README.md` (~:174-175): "Two more icons… no engine behind them yet" paragrafı yeniden — Clean canlı
  (ne yapar/ne yapmaz, onay yok, sonraki Build her şeyi derler → Sync'e gerek kalmaz; Sync zaten salt-okur
  analizdir, istenirse yine koşulabilir); Optimize hâlâ pasif; NuGet restore/cache işlerinin Optimize'ın
  gelecek kapsamı olduğu tek cümle.
- Anlatı üslubu korunur: "şu oturumda ekledik" YAZILMAZ; davranış yerinde yeniden yazılır; bayatlayacak
  rakam gömülmez.
- Tam süit: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
  (token/motion/D8 guard'ları dahil). Uygulama açıkken build alınmaz.
- Git: main'e merge + push; merge doğrulandıktan sonra branch local + remote silinir; oturum main'de biter.

## Sıra ve bağımlılıklar

```
T0 branch aç
T1 (Contracts)  ──┐
T2 (StateStore) ──┼→ T3 (Core servis) → T4 (Supervisor) → T5 (App VM) → T6 (MaintenanceBox) → T7 (doküman+süit+merge)
   (T1‖T2 paralel)
```

## Edge-case dizini

| Durum | Davranış | Nerede |
|---|---|---|
| Sync hiç yapılmamış workspace | Clean çalışır (kendi scan'i; CanClean yalnız HasWorkspace ister) | K-3, T5 |
| Worktree modu aktif | Yalnız RootPath altındaki in-place bin/obj silinir; havuz + `_obj` DOKUNULMAZ | K-10 |
| Clean sırasında App kapanır | stdin EOF ancak Clean bittikten sonra okunur; güç kaybında state önce silindiği için en kötü sonuç over-build (güvenli) | K-4, K-9 |
| Supervisor yok/ölü | `TrySendAsync` senkron düşer → kapı geri açılır; `IsEngineUnavailable` komutu zaten kapatır | T5 |
| Run uçuşta (yarış dahil) | App kapısı + Supervisor `cleanRejected` (çift katman) | K-4, T4 |
| Kilitli dosyalar (çalışan OSYS) | dosya başına topla + devam; warn + özet + `LockedFileCount`; süreç çökmez | K-6, T3 |
| Çok uzun silme | loop bloklanır (bilinçli — Sync emsali); progress satırları watchdog'u besler; donarsa `CleanBusy ∈ WaitingOnEngine` "Restart engine" kapısını açar | K-4, K-7 |
| Aynı klasörde 2 csproj / hiç proje / bin-obj yok | dedupe / sıfır sayaçlı başarı / state yine resetlenir | T3 |
| bin/obj içinde junction | recurse edilmez, link silinir | T3 |
| StaleObjDetector | obj gidince Inspect "temiz" döner — Clean uyarının kökünü giderir, kod değişmez | T3 kabul |

## Kritik dosyalar

- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs`
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` (yeni `CleanWorkspaceService.cs`'in kalıbı)
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs`
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` · `RunCoordinator.cs`
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` · `RunViewModel.Workspace.cs` · `RunViewModel.Stream.cs`
- `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs` · `AccessibilityNames.cs`
- `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs` (pinler yeniden yazılır)
