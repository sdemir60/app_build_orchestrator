# Optimize Butonuna Motor Takma — Uygulama Planı

> Bu plan başka bir makinede (Fable) hazırlandı; uygulamayı Opus güncel koda göre yapacak.
> **Tüm satır numaraları YAKLAŞIKTIR** — kod değişmiş olabilir. Dosya adları ve kalıplar bağlayıcıdır;
> satır numaraları yalnız arama ipucudur, uygulamadan önce tazelenir.

Hedef: `MaintenanceBox`'taki `PART_Optimize`'ı (Clean'in sağındaki gauge ikonu, bugün kalıcı disabled)
gerçek bir `optimizeWorkspace` IPC komutuna bağlamak. Optimize bir **workspace doktoru**dur: tıklandığı anda
workspace'in bilinen sorun sınıflarını tarar ve **düzeltebildiğini o anda düzeltir**, düzeltemediğini isim
isim raporlar. İş mantığı Core'da, Supervisor yalnız wiring, App yalnız komut gönderir + event gösterir.

## Ürün kapsamı (v1'de düzeltilen sorun sınıfları)

1. **Eksik NuGet paketleri:** `packages.config`'li projelerde `\packages\` altını gösteren HintPath hedefi
   diskte yoksa proje restore edilir (mevcut kanıtlanmış per-proje `-t:restore` sözleşmesi). Kullanım
   senaryosu: paketleri silinmiş/taşınmış workspace'i VS'de açmadan veya build almadan önce tek tıkla
   tamamlamak. (Build zaten kendi derlediği projeyi restore eder — Optimize'ın değeri build'in DOKUNMADIĞI,
   skip edilen projeleri de iyileştirmesidir.)
2. **Restore'un çözemediği kırık referanslar (teşhis):** restore SONRASI hâlâ eksik HintPath hedefleri —
   third-party sürüm drift'i (`HintPath` ≠ `packages.config` sürümü) ve eksik OSYS platform DLL'leri —
   proje + dosya adıyla warn olarak listelenir. Bugün bu, run ortasında kriptik bir derleme hatası olarak
   patlıyor; Optimize onu tık anında isimli, eyleme dönük bir listeye çevirir.
3. **Stale obj NuGet artıkları:** `StaleObjDetector`'ın bugün yalnız UYARDIĞI, ölçülmüş build-kırıcı
   yabancı-TFM artıkları (`obj\project.assets.json`, `*.nuget.g.props`, `*.nuget.g.targets`) legacy
   projelerde SİLİNİR. (Uyarının kökü temizlenir — bugüne dek "left untouched, the build may break" idi.)
4. **Ölü cache girdileri + öksüz .tmp'ler:** `build-state.json` ve `evaluation-cache.json`'da RootPath
   altında olup csproj'u artık diskte olmayan girdiler budanır; cacheRoot'ta yarım kalmış atomik-yazma
   `.tmp` artıkları süpürülür.

- **UX:** onay dialogu YOK — tıklar tıklamaz çalışır (Clean ile aynı karar). Console sıfırlanır, ilerleme
  satır satır console'a akar, event stream'e tek bitiş özeti düşer. Görsel tasarım/animasyon kapsam DIŞI
  (Claude Design'da sonra); yalnız işlevsel akış.
- **Dokunulmayanlar:** global NuGet cache'leri (`%LOCALAPPDATA%\NuGet`, global-packages, http-cache),
  `NuGet.config`, `nuget.exe` (yok — mevcut karar), git (Optimize hiçbir git komutu koşmaz), worktree
  havuzu + `_obj`, bin/OutDir, run logları, `ui-state.json`.
- **v1 SONRASI adaylar (bu işte YAPILMAZ, doküman notu bile gerekmez):** run log yaşlandırma, SDK-style
  projeler için düz `-t:restore`, HintPath↔packages.config sürüm-drift otomatik onarımı, paralel restore,
  worktree havuzunda orphan dizin tespiti. Servis yapısı "yeni sorun sınıfı = yeni private adım + sayaç"
  olacak şekilde düz tutulur.

## 0. Bağlayıcı kararlar (K-1…K-14)

**K-1. NuGet ihtiyacı tespiti HintPath-varlık tabanlı; packages.config İÇERİĞİ PARSE EDİLMEZ.**
Needy tanımı: csproj yanında `packages.config` VAR **ve** en az bir `\packages\`-sınıfı HintPath'in csproj
dizinine göre çözülmüş hedefi diskte YOK. Gerekçe: (a) NuGet `repositoryPath` override'ı yüzünden
`<SolutionDir>\packages` varsayımı güvenilmez — HintPath'in kendisi paketlerin gerçek yerini söyler;
(b) repo'da packages.config parser'ı yok (doğrulandı), yazmak yeni yüzey açar; (c) tespit ve "restore
sonrası hâlâ kırık" teşhisi TEK mekanizma olur. Bilinçli körlük: HintPath'siz paketler (analyzer/
content-only) tespit edilmez; `$(` içeren (MSBuild property'li) ham HintPath'ler çözülemez ve HİÇBİR
sayıma girmez (ne needy ne unresolved).

**K-2. `\packages\` ayrımı tek kaynaktan: `HintPathClassifier`'a public yardımcı.**
`\packages\` literal'i bugün `HintPathClassifier.IsThirdParty` içinde private (~:51-56) ve "Program Files"
yollarını da kapsıyor. Kopya yasak: literal yeniden yazılmaz. `IsThirdParty`'nin packages ayağı
`public static bool IsNuGetPackagesPath(string raw)` olarak açılır; `IsThirdParty` onu çağırır, Optimize
da onu kullanır. (Program Files HintPath'leri needy tetiklemez — restore onları düzeltemez.)

**K-3. HintPath çözümü: HAM yol csproj dizinine göre mutlaklaştırılır.**
`EvaluatedProject.HintPaths` HAM relative metin taşır (`RawHintPath(Raw, BaseName)`,
`CsprojEvaluator.cs` ~:8, ~:63-67; resolve eden yardımcı BUGÜN YOK — doğrulandı). Varlık kontrolü
`Path.GetFullPath(Path.Combine(csprojDir, raw))` ile yapılır; bu küçük çözücü Optimize servisinde tek
private statik olur (başka tüketici çıkarsa o gün taşınır).

**K-4. Restore per-PROJE, mevcut kanıtlanmış sözleşmeyle; sln-level restore YAPILMAZ; SIRALI koşar.**
Argümanlar `MsBuildArguments.RestorePackagesConfig(projectPath, solutionDir)` (~:20-24, 5 argüman,
`-nodeReuse`/`-p:UseSharedCompilation` bilinçli YOK — MsBuildArgumentsTests ~:47-55 pini),
`SolutionDir` = `SolutionDirResolver.Resolve(csprojPath, refs)`; refs `SolutionMapper.MapRefs(scan.SlnPaths,
scan.CsprojPaths)` sözlüğünden (dönüş: `IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>>`).
Sln-level restore kanıtsız yoldur (motor hiç kullanmıyor) ve bozuk sln üyelerinde yeni hata yüzeyi açar.
Paralellik v2.

**K-5. Restore child'ı `MsBuildInvoker`'ın mevcut çekirdeğiyle koşar; interface bir üye kazanır.**
`IMsBuildInvoker`'a `Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string>
onLine, CancellationToken ct)` eklenir; `public sealed record MsBuildRestoreRequest(string ProjectId,
string SolutionDir)` `MsBuildInvokeRequest`'in yanına. `MsBuildInvoker` implementasyonu private
`RunChildAsync` çekirdeğini (~:63-134: launch + inner job assign + pump + bounded drain + kill) aynen
kullanır — timeout kurulumu (`PerProjectTimeout` cts + linked cts, ~:47-48) ve workingDirectory/Stopwatch
prologu InvokeAsync'ten küçük bir ortak private helper'a çıkarılabilir ya da 3-4 satır yinelenir (makine
kopyalanmaz). `RetryingMsBuildInvoker` `RestoreAsync`'i inner'a DOĞRUDAN forward eder (retry'sız — MSB302x
copy-contention build'e özgüdür; XML doc'a yazılır). Interface büyüyünce derlemesi kırılan 5 implementer
(doğrulandı): `MsBuildInvoker`, `RetryingMsBuildInvoker`, `RunCoordinatorTests.FakeInvoker` (CycleRoundsTests
de bunu paylaşır), `ProjectLogStreamTests.FakeInvoker`, `RetryingMsBuildInvokerTests.ScriptedInvoker`.

**K-6. MSBuild çözülemezse Optimize BAŞARISIZ OLMAZ; restore adımı atlanır.**
Toolset resolve'u lazy'dir ve yalnız needy proje varsa denenir. `MsBuildResolveException` →
`warning: MSBuild.exe could not be resolved — package restore skipped` + kalan adımlar koşar
(`error(optimizeFailed)` DEĞİL). Core servis seam'i `Func<CancellationToken, Task<IMsBuildInvoker>>`dır —
`MsBuildToolset` Supervisor'da tanımlı olduğu için (RunCoordinator.cs ~:51, doğrulandı) Core o tipe
bağlanamaz; Supervisor fabrikası `async ct => (await ResolveMsBuildAsync(innerJob, ct)).Invoker` lambda'sını
verir (Program.cs ~:92'deki mevcut kalıbın ikizi; `_toolset` memoization paylaşımlı kalır).

**K-7. Stale obj artığı silme YALNIZ legacy projelerde; SDK-style ATLANIR (bloklayıcı kural).**
`IsSdkStyle == true` projede `project.assets.json` meşrudur ve silinirse motor onu restore ETMEZ
(packages.config yok) → build "assets file not found" ile kırılır. Bu yüzden: yalnız `IsSdkStyle == false`
projelerde, `StaleObjDetector.Inspect(csproj, tfm)` stale derse `obj\project.assets.json` +
`obj\*.nuget.g.props` + `obj\*.nuget.g.targets` silinir. obj klasörünün KENDİSİ, bin ve OutDir'e
dokunulmaz; build-state RESETLENMEZ (imza kaynak-tabanlı, bin yerinde — pre-skip kararı değişmez, tehlike
yok). TFM'i boş/okunamayan proje sessizce atlanır (Warner kalıbı). Run-time davranış DEĞİŞMEZ: koşu
başındaki tespit salt-teşhis kalır; silme yalnız kullanıcı-tetikli Optimize'dadır —
`StaleObjDetector`/`StaleObjRunStartWarner` XML doc'larına ve ARCHITECTURE §9.4'e bu nüans işlenir.

**K-8. Cache budaması workspace-scoped + VARLIK-tabanlı (Clean'in remove-all'ından ayrı semantik).**
`BuildStateStore.PruneMissingUnderRoot(rootPath)` ve `EvaluationCache.PruneMissingUnderRoot(rootPath)`:
RootPath prefix'i (ayraç eklenmiş, OrdinalIgnoreCase) altındaki anahtarlardan csproj'u `File.Exists` ile
diskte OLMAYANLAR silinir; dönüş = silinen sayı; 0 ise dosya yazılmaz. Mevcut never-throw + temp+atomik
rename desenleri izlenir (`Upsert` ~:95-122, `Flush` ~:89-105). Clean planının `RemoveUnderRoot`'u merge
edilmişse prefix-normalizasyon mantığı ortak private helper'a çıkarılır (kopya yasak). Bilinçli sınır:
worktree koşularının cache'e yazdığı worktree-yollu girdiler RootPath dışıdır, budanmaz (nota geçer).

**K-9. Öksüz .tmp süpürme store'ların KENDİ metodudur (desen tek kaynakta kalır).**
`<hedef>.<guid N>.tmp` deseni `BuildStateStore.Upsert` (~:104) ve `EvaluationCache.Flush` (~:92) içinde
yaşar; süpürme de oraya eklenir: her iki sınıfa `SweepOrphanTempFiles(TimeSpan olderThan)` — kendi hedef
dosyasının deseniyle eşleşen, son yazımı eşikten eski dosyaları best-effort siler, sayı döner. Eşik
serviste sabit **1 saat** (aktif yazımla yarışmaz — rename retry penceresi milisaniyelerdir). Saat,
test için enjekte edilebilir (`RenameRetryDelay` dikiş kalıbı).

**K-10. Yeni event üçlüsü + hata modeli Clean/Sync ikizi.**
`optimizeStarted` / `optimizeProgress(Line, Level)` (Level: cmd/info/dim/warn — `SyncProgressEvent`
sözlüğü) / `optimizeCompleted`. IPC sınırına yalnız iki kod çıkar: `optimizeFailed` (beklenmeyen exception
— `planFailed` catch-all ikizi) ve `optimizeRejected` (run uçuşta). Exception IPC sınırını ASLA geçmez.
Kilitli/silinemeyen dosya hata DEĞİLDİR: warn + `LockedFileCount`, akış devam eder. Restore exit≠0 de hata
değildir: warn + `FailedRestores`, sonraki projeye geçilir (offline/kaynak-erişilemez senaryosu böyle akar).

**K-11. `optimizeCompleted` sayaçları:**
`OptimizeCompletedEvent(int ProjectCount, int RestoredProjects, int FailedRestores,
int UnresolvedReferences, int StaleObjCleaned, int PrunedStateEntries, int PrunedCacheEntries,
int RemovedTempFiles, int LockedFileCount, long BytesReclaimed)`.
`UnresolvedReferences` = restore SONRASI hâlâ eksik HintPath hedefleri (ad bilinçli: "broken" değil —
restore denendikten sonra kalanlar). `BytesReclaimed` = TÜM silme adımlarının (obj artıkları + .tmp)
toplamı. Needy toplamı `RestoredProjects + FailedRestores`'tan türetilir, ayrı alan yok.

**K-12. Supervisor'da komut döngüsü BLOKLANIR (Sync kalıbı); run aktifken `optimizeRejected`.**
`RunCoordinator`'a salt-okur `public bool IsRunActive { get { lock (_gate) return _runActive; } }` probe
(`RunCompletion` ~:184 kalıbı; `_finishing` dahil edilmez) — Clean planıyla ORTAK primitif: hangisi önce
merge olduysa diğeri var olanı kullanır, İKİNCİ TANIM YAZILMAZ. Bilinçli karar: Optimize'ın iptal komutu
YOK (Sync emsali); N needy proje × restore sürebilir — heartbeat (K-13) + adımlar/projeler arası
`ct.ThrowIfCancellationRequested()` + donmuş motorda "Restart engine" kapısı tek kaçıştır; bu sınır
dokümana yazılır.

**K-13. Watchdog'u restore sırasında heartbeat besler.**
90 sn sessizlik eşiği 30 sn'lik git timeout'una göre kalibre edilidir; tek bir NuGet indirmesi 90 sn'den
uzun SESSİZ kalabilir → sağlıklı motorda yanlış "Engine has stopped responding". Serviste restore child'ı
beklerken periyodik (30 sn) `dim` satırı basılır: `still restoring {name} ({elapsed})` — bekleme dikişi
enjekte edilebilir (`internal Func<CancellationToken, Task>? HeartbeatDelay`, D8: testte senkron sinyal,
gerçek bekleme yok).

**K-14. App kapıları: Sync guard'ın birebir simetriği; yeni AppPhase YOK.**
`_optimizeRequested` + `_optimizeInFlight` + `OptimizeBusy`;
`CanOptimize() => HasWorkspace && !IsRunning && !IsStarting && !IsEngineUnavailable && !SyncBusy && !OptimizeBusy`
(+ Clean merge edilmişse `&& !CleanBusy`). `CanSync()` ve `CanRebuildOrRetry()` sonuna `&& !OptimizeBusy`
(CanBuildCycles kalıtır); Clean varsa `CanClean()`'e de `&& !OptimizeBusy`. `WaitingOnEngine`
`|| OptimizeBusy` alır. Konsol sıfırlama Clean'in `ClearConsoleBuffers()` helper'ıyla ortak: helper hangi
oturumda önce doğduysa ikincisi ONU kullanır, blok üçüncü kez kopyalanmaz.

---

## Clean planıyla paralel yürütme koordinasyonu

Bu plan, `2026-08-19-17-33-clean-button-engine-plan.md` ile AYNI bölgeleri büyütür ve iki iş bağımsız
oturumlarda uygulanabilir. Kural: **ikinci gelen uyarlanır, hiçbir primitif iki kez tanımlanmaz.** Ortak
dokunma noktaları:

| Bölge | Clean | Optimize | İkinci gelenin görevi |
|---|---|---|---|
| `IpcMessages.cs` whitelist'ler + record bölgesi | cleanWorkspace + 3 event | optimizeWorkspace + 3 event | yalnız kendi satırlarını ekle |
| `RunCoordinator.IsRunActive` | ekler | ekler | var olanı KULLAN |
| `SupervisorHost` dispatch + `WorkspaceServices` | Clean fabrikası | Optimize fabrikası + `Default`'a invoker-factory parametresi | ctor kuran testleri (SyncStreamingTests ~:123-129 doğrudan ctor, ProjectLogStreamTests ~:119-120 Default) TEK seferde güncelle |
| `RunViewModel` `CanSync`/`CanRebuildOrRetry`/`WaitingOnEngine`/`OnEvent`/`OnError`/`ReleaseAfterEngineLoss` | CleanBusy | OptimizeBusy | **karşılıklı dışlamayı ikinci gelen kurar:** `CanClean += !OptimizeBusy`, `CanOptimize += !CleanBusy` + çift kapı testi |
| `RunViewModel.Workspace.cs` bayrak bölgesi + `NotifySyncGatedCommands` + `TryConsume*Failure` | clean üçlüsü | optimize üçlüsü | tek listeye kendi komutunu ekle |
| `ClearConsoleBuffers()` helper | çıkarır | çıkarır | var olanı KULLAN |
| `AccessibilityNames` ~:35-43 | CleanTooltip yeniden; `NotAvailableSuffix` "yalnız Optimize'da" | OptimizeTooltip yeniden | **suffix'in son kullanıcısı const'u + karar doc'unu (~:35) SİLER** |
| `MaintenanceBox.Build()` foreach ~:84-88 | Clean'i çıkarır | Optimize'ı çıkarır | son çıkan foreach'i TAMAMEN siler; sınıf doc paragrafı (~:22-24) yeniden yazılır |
| `MaintenanceBoxTests` birleşik disabled pini ~:91-106 | böler | böler | kalan pini kendi davranışına çevir |
| `ByteFormat` (boyut metni) | serviste statik arar/kurar | `Core/Formatting/ByteFormat` kurar | tek kaynağa TERFİ ettir, iki çağıran |
| `BuildStateStore` `RemoveUnderRoot` / `PruneMissingUnderRoot` | ekler | ekler | prefix-normalizasyonu ortak private helper'a çıkar |
| Doküman cümleleri: ARCHITECTURE §5.2 "ten commands execute" (~:286), §5.3 listesi, §4.6 bekleme pencereleri (~:238-240), §13.2 bakım kutusu (~:1299-1305), §16 tablosu; README ~:174-175 | günceller | günceller | ikinci gelen "ikisi de canlı" hâline BİRLEŞTİRİR |
| `EngineSilenceWatchdogTests` | Clean ikiz testi | Optimize ikiz testi | yalnız kendi testini ekle |

---

## Task 0 — İş branch'i aç

`feature/optimize-engine` gibi bir branch; task başına commit; sonda main'e merge + push + branch temizliği.

## Task 1 — IPC sözleşmesi (Contracts)

**Dosya:** `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs`

**Önce KIRMIZI test** — `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs` (mevcut roundtrip kalıbı):
- `OptimizeWorkspace_roundtrips_with_discriminator` — serialize → `"type":"optimizeWorkspace"`; RootPath korunur.
- `Optimize_events_roundtrip_with_discriminators` — üç event `Event_roundtrip_all_types` deseninde.

**Implementasyon:**
- Komut whitelist'ine (~:17-28): `[JsonDerivedType(typeof(OptimizeWorkspaceCommand), "optimizeWorkspace")]`.
- `public sealed record OptimizeWorkspaceCommand(string RootPath) : IpcCommand;` — XML doc: neyi düzeltir /
  neye dokunmaz (ürün kapsamı özeti), run uçuştayken `optimizeRejected` (K-12), komut döngüsünü Sync gibi
  bloklar. Configuration TAŞIMAZ (hiçbir adım gerektirmiyor).
- Event whitelist'ine üç `JsonDerivedType` ("optimizeStarted"/"optimizeProgress"/"optimizeCompleted").
- Record'lar (Sync event'lerinin yanına):
  - `public sealed record OptimizeStartedEvent(string RootPath) : IpcEvent;`
  - `public sealed record OptimizeProgressEvent(string Line, string Level) : IpcEvent;` — doc:
    `SyncProgressEvent` ikizi ama Sync yüzeyine AİT DEĞİL.
  - `OptimizeCompletedEvent` — K-11'deki 10 alan; hepsi default değerli (eski satır alansız çözülür —
    mevcut sözleşme deseni).

**Kabul:** yeni testler + mevcut Ipc süiti yeşil; `Unknown_discriminator_throws` etkilenmez.

## Task 2 — Core: store hijyen primitifleri (T1'den bağımsız, paralel)

**Dosyalar:** `src/BuildOrchestrator.Core/State/BuildStateStore.cs`,
`src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs`

**Önce KIRMIZI testler** (mevcut test dosyalarına — Glob ile bul):
- `PruneMissingUnderRoot_removes_only_entries_whose_csproj_no_longer_exists` — kök-altı iki kayıt (biri
  diskte var, biri yok) + kök-DIŞI diskte olmayan bir kayıt → yalnız kök-altı ölü kayıt gider, dönüş 1.
- `PruneMissingUnderRoot_normalizes_case_and_trailing_separator_and_avoids_the_prefix_trap` —
  `c:\REPO\` vs `C:\repo`; `C:\repo2\...` SİLİNMEZ.
- `PruneMissingUnderRoot_with_nothing_to_prune_does_not_rewrite_the_file` — mtime/içerik değişmez, dönüş 0.
- `PruneMissingUnderRoot_on_a_missing_file_returns_zero`.
- Aynı dörtlü `EvaluationCache` için (+ prune sonrası `GetOrEvaluate` diri kayıtları hâlâ döner).
- `SweepOrphanTempFiles_removes_only_this_stores_stale_tmp_files` — kendi deseninde eski `.tmp` gider;
  YENİ (eşik altı) `.tmp` durur; farklı desenli komşu dosya durur; hedef json'ın kendisi durur; dönüş = sayı.
- `SweepOrphanTempFiles_clock_is_injectable` — D8: enjekte saat ile eşik senkron test edilir.

**Implementasyon:** K-8 + K-9. `BuildStateStore`'da `_writeGate` altında Load → filtre → 0 ise dönüş →
temp + `MoveAtomicWithRetry`; `EvaluationCache`'te aynı akış kendi `Flush` deseniyle. Clean'in
`RemoveUnderRoot`'u varsa prefix helper ortaklaştırılır (koordinasyon tablosu).

**Kabul:** yeni + mevcut State/Discovery testleri yeşil; `Upsert`/`Load`/`GetOrEvaluate` davranışı değişmez.

## Task 3 — Core: restore-only invoker yüzeyi + classifier yardımcısı (T1-T2'den bağımsız, paralel)

**Dosyalar:** `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs`,
`RetryingMsBuildInvoker.cs`, `src/BuildOrchestrator.Core/Graph/HintPathClassifier.cs`

**Önce KIRMIZI testler:**
- `MsBuildInvokerTests`'e (mevcut sahte-exe harness'i ile): `RestoreAsync_runs_a_single_child_with_the_
  packages_config_restore_arguments` — child'a giden argümanlar `MsBuildArguments.RestorePackagesConfig`
  seti, `-t:Build` YOK; exit code aynen döner. + `RestoreAsync_honors_the_per_project_timeout` (mevcut
  timeout test kalıbının ikizi).
- `RetryingMsBuildInvokerTests`: `RestoreAsync_is_forwarded_without_retry` — fırlatan/başarısız inner'da
  backoff koşulmaz.
- `HintPathClassifierTests`: `IsNuGetPackagesPath_matches_the_packages_segment_and_not_program_files` +
  mevcut `Classify` pinleri DEĞİŞMEDEN yeşil (IsThirdParty davranışı aynı kalır).

**Implementasyon:** K-2 + K-5. FakeInvoker'lar (`RunCoordinatorTests`, `ProjectLogStreamTests`,
`ScriptedInvoker`) yeni üyeyi delegate-passthrough olarak alır.

**Kabul:** MsBuild + Graph süitleri yeşil; `The_compiler_server_switches_have_exactly_one_source_in_the_repo`
guard'ı etkilenmez (restore argümanlarına bayrak EKLENMEZ).

## Task 4 — Core: `OptimizeWorkspaceService` (T1+T2+T3'e bağlı)

**Yeni dosya:** `src/BuildOrchestrator.Core/Workspace/OptimizeWorkspaceService.cs` —
`SyncWorkspaceService`'in kardeşi (Clean merge edilmiş ve `CleanWorkspaceService` başka klasördeyse ONUN
yanına; klasör taşıma bu işte YAPILMAZ).

```csharp
public sealed class OptimizeWorkspaceService(
    WorkspaceScanner scanner,
    CsprojEvaluator evaluator,
    EvaluationCache cache,
    BuildStateStore stateStore,
    Func<CancellationToken, Task<IMsBuildInvoker>> restoreInvoker)
{
    internal Func<CancellationToken, Task>? HeartbeatDelay { get; set; }   // K-13 dikişi
    public async Task RunAsync(OptimizeWorkspaceCommand cmd, Action<IpcEvent> emit, CancellationToken ct = default)
}
```

Akış (her adım arası `ct.ThrowIfCancellationRequested()`):
1. `emit(new OptimizeStartedEvent(cmd.RootPath))`.
2. Kapı: `Directory.Exists(RootPath)` değilse `emit(new ErrorEvent("optimizeFailed", $"Workspace root not
   found: '...'."))` + return (`SyncWorkspaceService` ~:58-62 deseni).
3. `scanner.Scan(RootPath)` → csproj + sln listeleri; `SolutionMapper.MapRefs(scan.SlnPaths,
   scan.CsprojPaths)`; her csproj `cache.GetOrEvaluate(p, evaluator.Evaluate)` (null → atla).
4. **Adım 1 — NuGet (K-1…K-6, K-13):** needy kümesi hesaplanır → info satırı
   `checking NuGet packages — {P} projects, {N} need restore` (N=0 ise `all packages present`) → N>0 ise
   invoker lazy resolve (hata → K-6 warn + adım atlanır) → needy başına: cmd satırı (`Cmd` level; metin
   `"msbuild " + string.Join(' ', MsBuildArguments.RestorePackagesConfig(...))` — argüman listesi tek
   kaynaktan, `ConsoleLine.CommandHeads`'in "msbuild " ön eki komut rengini verir) → `RestoreAsync`
   (child satırları `Dim` ile akar; heartbeat K-13) → exit≠0: warn `restore failed for {name} (exit {c})`
   + `FailedRestores++`; exit=0: `RestoredProjects++`.
5. **Adım 2 — kırık referans teşhisi:** TÜM projelerde `\packages\`-sınıfı ve `ExternalOsysPlatform`
   (`\bin\`, producer'sız) HintPath hedefleri yeniden kontrol edilir; eksikler warn:
   `unresolved reference: {proj}: {basename} — {expected path}` (platform için
   `... — build the producing solution first`); detay satırı **en çok 30**, kalanı tek satır
   `... and {n} more unresolved references`; `UnresolvedReferences` tam sayıyı taşır.
6. **Adım 3 — stale obj artıkları (K-7):** legacy projelerde Inspect → stale ise üç artık dosya silinir
   (boyutları `BytesReclaimed`'e), info satırı `{name} — stale NuGet leftovers removed from obj`; kilitli →
   warn + `LockedFileCount++`, devam; `StaleObjCleaned` = temizlenen proje sayısı.
7. **Adım 4 — cache budama (K-8):** iki store'da `PruneMissingUnderRoot(RootPath)` → info satırı
   `pruned {a} build-state and {b} evaluation-cache entries` (a+b=0 ise dim `caches are clean`).
8. **Adım 5 — .tmp süpürme (K-9):** iki store'da `SweepOrphanTempFiles(1h)` → sayaç + (varsa) dim satırı.
9. Özet (info): `Optimize complete — {restored} restored · {unresolved} unresolved refs · {staleObj} obj
   cleaned · {prunedTotal} cache entries pruned · {size} reclaimed`; hiçbir sorun yoksa
   `Optimize complete — nothing to fix, {P} projects healthy`; kilitli varsa ek warn
   `warning: {n} files could not be removed (in use) — close the running application and run Optimize again`.
   Boyut metni yeni `Core/Formatting/ByteFormat` ile (`DurationFormat` kardeşi; Clean'in formatlayıcısı
   varsa TERFİ ettirilir — koordinasyon tablosu).
10. `emit(new OptimizeCompletedEvent(...))`.

Tüm kullanıcı metinleri İNGİLİZCE (NoTurkishUserTextTests). Progress factory'leri `Cmd/Info/Dim/Warn` —
`OptimizeProgressEvent` üreten private statikler (`SyncWorkspaceService` ~:234-237 kalıbı).

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/Workspace/OptimizeWorkspaceServiceTests.cs`
(temp dizinde sahte workspace + sahte `IMsBuildInvoker`; git GEREKMEZ; gerçek MSBuild YOK):
- `A_project_with_packages_config_and_a_missing_packages_hintpath_is_restored` — sahte invoker'a giden
  istek `(csproj, resolved SolutionDir)`; sahte restore paket dosyasını YARATIR → re-check temiz,
  `RestoredProjects=1`, `UnresolvedReferences=0`, sıra `optimizeStarted → progress* → optimizeCompleted`.
- `A_healthy_workspace_reports_nothing_to_fix_and_spawns_no_restore` — invoker HİÇ çağrılmaz, tüm
  sayaçlar 0, "nothing to fix" satırı.
- `A_project_without_packages_config_is_never_restored_even_with_missing_hintpaths` — needy tanımı pinlenir;
  eksikler yalnız `UnresolvedReferences`'a gider.
- `A_failed_restore_warns_and_continues_to_the_next_project` — exit 1 → `FailedRestores=1`, ikinci proje
  yine restore edilir (offline senaryosunun servis-düzeyi ikizi).
- `A_hintpath_still_missing_after_restore_is_reported_as_unresolved` — drift senaryosu; detay 30 sınırı
  ayrı testte (`Unresolved_detail_lines_are_capped_and_the_counter_keeps_the_true_total`).
- `A_hintpath_with_an_msbuild_property_is_ignored_everywhere` — `$(SolutionDir)packages\...` ne needy
  tetikler ne unresolved sayılır (K-1).
- `Stale_obj_leftovers_are_removed_only_from_legacy_projects` — legacy stale → üç dosya gider, obj klasörü
  ve diğer içerik durur; **SDK-style stale → HİÇBİR dosya silinmez** (K-7 bloklayıcı kuralın pini).
- `A_locked_leftover_is_reported_and_skipped_without_stopping_the_optimize` — `FileShare.None`;
  `LockedFileCount ≥ 1`, akış sürer.
- `Dead_cache_entries_under_the_root_are_pruned_and_foreign_roots_survive`.
- `MSBuild_resolve_failure_skips_restore_but_the_other_steps_still_run` — K-6; fırlatan factory.
- `The_restore_heartbeat_line_appears_while_a_child_is_slow` — HeartbeatDelay dikişi senkron sinyalle (D8).
- `A_missing_workspace_root_becomes_a_defined_error_event` — `error(optimizeFailed)`, exception yok.
- `Two_csproj_in_the_same_folder_are_deduplicated` / `A_workspace_with_no_projects_completes_with_zero_counters`.

**Kabul:** yeni Workspace testleri yeşil; `StaleObjDetectorTests`/`StaleObjRunStartWarnerTests` DEĞİŞMEZ
(teşhis davranışı aynı; yalnız doc nüansı — K-7).

## Task 5 — Supervisor wiring (T4'e bağlı)

**Dosyalar:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` — `IsRunActive` probe (K-12; Clean eklemişse ATLA).
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs`:
  - `WorkspaceServices`'a `Func<string, OptimizeWorkspaceService> Optimize` parametresi; `Default`
    imzasına `Func<CancellationToken, Task<IMsBuildInvoker>> msbuildInvoker` eklenir; fabrika:
    `root => new OptimizeWorkspaceService(new WorkspaceScanner(), new CsprojEvaluator(),
    new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json")), new BuildStateStore(cacheRoot),
    msbuildInvoker)`.
  - Dispatch switch'e (~:97-108) `case OptimizeWorkspaceCommand o: await OptimizeWorkspaceAsync(o, ct); break;`.
  - Handler `SyncWorkspaceAsync` (~:130-146) deseni: önce `if (coordinator.IsRunActive)
    { error("optimizeRejected", "a run is in flight — stop it before optimizing"); return; }`; sonra Emit
    köprüsü + `catch (Exception ex when not OCE) → error("optimizeFailed", ex.Message)`.
- `src/BuildOrchestrator.Supervisor/Program.cs` — `Default(...)` çağrısına
  `async ct => (await ResolveMsBuildAsync(innerJob, ct)).Invoker` (K-6; ~:92'deki lambda kalıbı).
- Güncellenen kurucular: `SyncStreamingTests` (~:123-129 doğrudan ctor) ve `ProjectLogStreamTests`
  (~:119-120 Default) — Optimize fabrikası/parametresi eklenir (testte fırlatan sahte invoker yeterli).

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/Supervisor/OptimizeDispatchTests.cs`
(`SyncStreamingTests`'in host-in-memory kalıbı: MemoryStream stdin, gözlemlenen stdout, gerçek host +
gerçek servis + sahte invoker; gerçek process YOK):
- `OptimizeWorkspace_streams_started_progress_and_completion_in_order_on_the_wire` — NDJSON `ParseWire`,
  sıra + diskte etki (ölü cache girdisi budanmış / sahte restore koşmuş).
- `OptimizeWorkspace_is_rejected_with_optimizeRejected_while_a_run_is_active_and_changes_nothing` —
  RunCoordinatorTests'in sahte planner/msbuild altyapısıyla aktif run; disk aynen durur.
- `An_unexpected_optimize_exception_becomes_error_optimizeFailed_not_a_crash` — fırlatan sahte servis;
  host yaşar (badCommand/planFailed test deseni).

**Kabul:** Supervisor süiti yeşil; stdout YALNIZ NDJSON.

## Task 6 — App VM: `OptimizeCommand`, kapılar, konsol + stream (T1'e derlenme, T5'e davranışça bağlı)

Clean planının T5'inin birebir simetriği — farklar ve tuzaklar:

- `RunViewModel.cs`:
  - `[RelayCommand(CanExecute = nameof(CanOptimize))] private async Task OptimizeAsync()` — `SyncAsync`
    (~:667-700) simetriği: `SelectedProjectId = null`; `ClearConsoleBuffers()` (K-14 — helper yoksa
    Clean planındaki tarifle ÇIKAR); `AppendRunLine(OptimizeRequestedLine)`
    (`internal static string OptimizeRequestedLine => "optimize requested";`); bayrak + notify +
    `ArmEngineWatchdog()`; `TrySendAsync(new OptimizeWorkspaceCommand(RootPath), "optimize")`;
    `sent=false` → `ReleaseOptimizeRequest()`.
  - `CanOptimize()` K-14; `CanSync()` (~:705) ve `CanRebuildOrRetry()` (~:632) `&& !OptimizeBusy`;
    Clean varsa çift yönlü dışlama (koordinasyon tablosu).
  - `[NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]` zincirleri: `_isRunning`, `_isStarting`,
    `_engineDiedMessage`, `_engineRestartable`, `_rootPath`.
  - `WaitingOnEngine` (~:972) `|| OptimizeBusy`.
  - `OnEvent` switch: `OptimizeStartedEvent → OnOptimizeStarted()` · `OptimizeProgressEvent e →
    AppendRunLine(e.Line)` · `OptimizeCompletedEvent e → OnOptimizeCompleted(e)`.
  - **TUZAK — `OnError` yerleşimi:** `TryConsumeOptimizeFailure(code, message)` çağrısı
    `if (!RunEndingErrorCodes.Contains(e.Code)) return;` satırından **ÖNCE** durmalıdır
    (`optimizeFailed`/`optimizeRejected` o kümede DEĞİL — sonra konursa hiç çalışmaz). Consume yalnız
    optimize yüzeyini bırakır, run state'ine (IsRunning/IsStarting/Phase) DOKUNMAZ — run-sonu slot
    yarışındaki `optimizeRejected` canlı run'ı yıkmamalı.
- `RunViewModel.Workspace.cs` (sync guard'ın yanı): `_optimizeRequested`/`_optimizeInFlight`/`OptimizeBusy`
  + `internal` test dikişleri; `OnOptimizeStarted()` (istek→uçuş, faz DEĞİŞMEZ);
  `OnOptimizeCompleted(e)` → `ReleaseOptimizeSurface()`; `ReleaseOptimizeRequest()`;
  `TryConsumeOptimizeFailure` (`{"optimizeFailed","optimizeRejected"}` && `OptimizeBusy`);
  `ReleaseAfterEngineLoss` (~:1366-1390) `ReleaseOptimizeSurface()` da çağırır (motor mid-optimize
  ölürse kapı sızmaz); `NotifySyncGatedCommands()` (~:210-216) listesine `OptimizeCommand`.
- `RunViewModel.Stream.cs` + `StreamText`: `case OptimizeCompletedEvent e:` →
  `PushStream(StreamKind.Info, null, StreamText.Optimize(e.RestoredProjects, e.UnresolvedReferences,
  e.PrunedStateEntries + e.PrunedCacheEntries))` — öneri metin:
  `"Optimize — {0} restored, {1} unresolved refs, {2} entries pruned"` (InvariantCulture, tek yer).

**Önce KIRMIZI testler** — yeni `tests/BuildOrchestrator.Tests/App/OptimizeCommandTests.cs`
(RunViewModelStateTests kalıbı: `vm.OnEvent(...)` + `DebugOnCommandSent`):
- `Optimize_sends_optimizeWorkspace_with_the_workspace_root`.
- `Optimize_clears_the_console_and_writes_the_requested_line`.
- Kapı matrisi: workspace yok / starting-running / sync uçuşta / motor yok (+ Clean varsa clean uçuşta).
- `Sync_build_rebuild_and_cycles_are_disabled_while_an_optimize_is_in_flight_and_reopen_on_completion`
  (istek penceresi dahil).
- `A_failed_send_reopens_the_optimize_gate`.
- `An_optimizeFailed_or_optimizeRejected_error_releases_the_optimize_surface`.
- `An_optimizeRejected_during_a_live_run_leaves_the_run_state_untouched` — IsRunning/Phase değişmez.
- `Engine_death_mid_optimize_releases_the_optimize_surface`.
- `Optimize_progress_lines_flow_into_the_run_document` ve `Optimize_completion_pushes_one_stream_summary_line`.
- Watchdog: `EngineSilenceWatchdogTests`'e `A_silent_engine_during_an_optimize_raises_the_overdue_gate`
  (Sync ikizi).

**Kabul:** yeni testler + RunViewModelStateTests/EnginePreflightTests/EngineSilenceWatchdogTests yeşil.

## Task 7 — MaintenanceBox + tooltip (T6'ya bağlı)

- `AccessibilityNames.cs` (~:35-43): `OptimizeTooltip` yeni metin — öneri:
  `Optimize — restore missing NuGet packages, clean stale obj leftovers and prune dead cache entries`
  ("rebuild the dependency index" vaadi DÜŞER — v1'de öyle bir adım yok; o iş Sync'indir).
  `NotAvailableSuffix`: Clean merge edilmişse son kullanıcı BİZİZ → const + karar doc'u (~:35) silinir;
  değilse Clean'e kalır.
- `MaintenanceBox.xaml.cs` `Build()` (~:69-91): Optimize kalıcı-disabled kümesinden çıkar (foreach'in son
  üyesiyse foreach TAMAMEN silinir); `PART_Optimize.SetBinding(ButtonBase.CommandProperty,
  new Binding(nameof(RunViewModel.OptimizeCommand)))` (~:79 Resolve deseni);
  `ToolTipService.SetShowOnDisabled(PART_Optimize, true)` KORUNUR; sınıf doc'u (~:22-24) güncellenir.
- XAML kökü değişmez → yeni realize testi GEREKMEZ.

**Önce KIRMIZI testler** — `MaintenanceBoxTests` (~:91-106 birleşik pin SESSİZCE SİLİNMEZ, yeniden yazılır):
- `Optimize_is_wired_to_the_optimize_command_and_its_tooltip_names_the_job` — `Assert.Same`, tam tooltip
  metni, `GetShowOnDisabled` true.
- Clean'in durumuna göre kalan pin: Clean henüz pasifse `Clean_stays_disabled_...` pini kalır; Clean de
  canlıysa birleşik pin tamamen iki wiring testine dönüşmüş olur.
- `ActionBarTests` sıra testi (~:153-175) davranışça değişmez — koşup doğrula.
- `TooltipDelayTests`'in kendi sentetik literal'ına DOKUNULMAZ.

**Kabul:** App süiti yeşil; gerçek pencerede repo yokken Optimize pasif, repo seçilince aktif.

## Task 8 — Doküman + tam süit + git kapanışı (en son)

- `ARCHITECTURE.md`:
  - §5.2 (~:279-297): komut listesine `optimizeWorkspace`; "ten commands execute" cümlesi bayatlıyor —
    sayı yerine dayanıklı ifade önerilir ("every command except `debugSpawnChildren` executes in the
    shipped pair"); kısa paragraf: ne düzeltir, run uçuşta `optimizeRejected`, döngüyü Sync gibi bloklar,
    iptal yok (K-12 sınırı).
  - §5.3 (~:299-342): `Optimize: optimizeStarted · optimizeProgress · optimizeCompleted`.
  - §4.6 (~:238-240): sessizlik-watchdog "cevap borçlu pencere" listesine Optimize eklenir; restore
    heartbeat'i bir cümleyle.
  - §9.3/§9.4 (~:933-956): restore'un artık iki çağıranı olduğu (build öncesi + Optimize); §9.4'ün
    "Nothing is deleted or modified" cümlesi yerinde yeniden yazılır: koşu başı tespit salt-teşhis KALIR,
    kullanıcı-tetikli Optimize legacy projelerdeki NuGet artıklarını kaldırır (K-7).
  - §13.2 bakım kutusu (~:1299-1305) + §16 tablosu (~:2239-2246: build-state/evaluation-cache satırlarına
    "Optimize ölü girdileri budar" notu) + §22'ye yeni dosya satırları.
- `README.md` (~:174-175): paragraf yeniden — Optimize canlı (tek cümlelik kapsam: eksik paketleri restore
  eder, stale obj artıklarını temizler, ölü cache girdilerini budar; build kararlarını DEĞİŞTİRMEZ — imza
  kaynak-tabanlıdır); Clean'in durumu merge sırasına göre yazılır (koordinasyon tablosu).
- Anlatı üslubu korunur; "şu oturumda eklendi" YAZILMAZ; bayatlayacak rakam gömülmez.
- Tam süit: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
  (token/motion/D8 guard'ları dahil). Uygulama açıkken build alınmaz.
- Git: main'e merge + push; merge doğrulandıktan sonra branch local + remote silinir; oturum main'de biter.

## Sıra ve bağımlılıklar

```
T0 branch aç
T1 (Contracts) ──┐
T2 (Store hijyen)┼→ T4 (Core servis) → T5 (Supervisor) → T6 (App VM) → T7 (MaintenanceBox) → T8 (doküman+süit+merge)
T3 (Restore API) ┘      (T1‖T2‖T3 paralel)
```

## Edge-case dizini

| Durum | Davranış | Nerede |
|---|---|---|
| Sync hiç yapılmamış workspace | Optimize çalışır (kendi scan'i; CanOptimize yalnız HasWorkspace ister) | T4, T6 |
| Run uçuşta (yarış dahil) | App kapısı + Supervisor `optimizeRejected` (çift katman); red canlı run'ı YIKMAZ | K-12, T5, T6 |
| MSBuild/VS kurulu değil | restore adımı warn ile atlanır, kalan adımlar koşar | K-6 |
| NuGet kaynağı erişilemez / offline | proje başına exit≠0 → warn + `FailedRestores`, akış sürer | K-10, T4 |
| sln'siz packages.config projesi | `SolutionDirResolver` proje dizinine düşer → restore büyük olasılıkla düşer → warn + sayaç (§9.3 sln bağlamı şartı) | K-4, T4 |
| HintPath'te `$(...)` property | hiçbir sayıma girmez (çözülemez) | K-1 |
| HintPath ≠ packages.config sürümü (drift) | restore düzeltmez → `UnresolvedReferences` + isimli warn | Adım 2 |
| SDK-style projede stale obj | SİLİNMEZ (restore'suz silmek build'i kırar) — v1 bilinçli sınır | K-7 |
| Kilitli artık dosya (çalışan OSYS/VS) | warn + `LockedFileCount`, akış sürer | K-10 |
| 90 sn'den uzun sessiz NuGet indirmesi | heartbeat satırı watchdog'u besler; yine donarsa `OptimizeBusy ∈ WaitingOnEngine` → "Restart engine" | K-13 |
| Çok needy proje × uzun restore | iptal yok (bilinçli, K-12); heartbeat + proje arası ct kontrolü; süre sorunu v2 paralellik adayı | K-12 |
| Motor mid-optimize ölür | `ReleaseAfterEngineLoss` optimize yüzeyini de bırakır | T6 |
| Worktree modu kullanılıyor | Optimize yalnız RootPath'e (in-place) bakar; havuz + `_obj` + worktree-yollu cache girdileri DOKUNULMAZ | K-8 |
| Bir sorun da yok | "nothing to fix" özeti, sıfır sayaçlı başarı | T4 |
| Optimize build kararlarını değiştirir mi | HAYIR — imza kaynak-tabanlı; restore/artık temizliği hiçbir projeyi dirty yapmaz (ARCH ~:860-863) | T8 doc |

## Kritik dosyalar

- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs`
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` (yeni servisin kalıbı) · yeni `OptimizeWorkspaceService.cs`
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` · `MsBuildArguments.cs` · `SolutionDirResolver.cs` · `RetryingMsBuildInvoker.cs`
- `src/BuildOrchestrator.Core/Graph/HintPathClassifier.cs` · `src/BuildOrchestrator.Core/Discovery/CsprojEvaluator.cs` (yalnız OKUNUR) · `EvaluationCache.cs` · `StaleObjDetector.cs` (yalnız doc)
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` · yeni `Core/Formatting/ByteFormat.cs`
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` · `RunCoordinator.cs` · `Program.cs`
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` · `RunViewModel.Workspace.cs` · `RunViewModel.Stream.cs` · `StreamText.cs`
- `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs` · `AccessibilityNames.cs`
- `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs` (pinler yeniden yazılır) · `Supervisor/SyncStreamingTests.cs` + `ProjectLogStreamTests.cs` (WorkspaceServices kurucuları)
