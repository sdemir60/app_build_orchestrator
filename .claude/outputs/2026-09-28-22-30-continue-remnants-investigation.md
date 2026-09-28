# Continue kalıntıları — salt-okur araştırma raporu

- Repo: `D:\Projects\Other\Apps\app_build_orchestrator` · branch `main` · HEAD `6224eeb`
- Tarih: 2026-09-28 22:19
- Yöntem: yalnız Read/Grep/`git grep`/`git log -S`/`git show`. Build yok, test koşumu yok, dosya değişikliği yok (bu rapor hariç).
- Continue/RetryFailed'ı koddan kaldıran commit: **`a2ff12e`** ("refactor: Continue ve RetryFailed kosu modlari koddan kaldirildi").
  O commit `Core/Scheduling/RetryPlanning.cs`, `tests/.../Scheduling/ContinueRunTests.cs` ve `RetryFailedTests.cs`'i sildi;
  `RunSnapshot`, snapshot ctor'u, `TakeSnapshot`, `RunClock` ve `StoppedFailedIds` makinesi ise yerinde kaldı.

**Kısaltmalar**

| Kısa | Dosya |
|---|---|
| RC | `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` |
| PRG | `src/BuildOrchestrator.Supervisor/Program.cs` |
| RSS | `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` |
| RSN | `src/BuildOrchestrator.Core/Scheduling/RunSnapshot.cs` |
| RCK | `src/BuildOrchestrator.Core/Scheduling/RunClock.cs` |
| PPL | `src/BuildOrchestrator.Core/Planning/PlanProgressLines.cs` |
| IPC | `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` |
| PM | `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs` |
| RVM | `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` |
| RVS | `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` |
| APH | `src/BuildOrchestrator.App/ViewModels/AppPhase.cs` |
| RBT | `src/BuildOrchestrator.App/ViewModels/RibbonText.cs` |
| ARCH | `ARCHITECTURE.md` |
| T:RVMT | `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs` |
| T:RVMST | `tests/BuildOrchestrator.Tests/App/RunViewModelStateTests.cs` |
| T:RCT | `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorTests.cs` |
| T:CRT | `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` |
| T:RCKT | `tests/BuildOrchestrator.Tests/Scheduling/RunClockTests.cs` |
| T:RSST | `tests/BuildOrchestrator.Tests/Scheduling/ReadySetSchedulerTests.cs` |
| T:IPCT | `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs` |

**Sınıflar:** (a) ÖLÜ — üretimde çağıran/okuyan yok · (a′) üretimde ETKİSİZ ama savunmacı (tut) · (b) CANLI, başka amaçla
kullanılıyor · (c) CANLI ve özgün gerekçesiyle hâlâ gerekli.

---

## 0. Kısa sonuç

- **Sayılar:** (a) 9 ana yapı (S-3…S-7, C-1, A-1…A-3) + 3 alt üye (K-1.ElapsedMs, K-4.elapsedMs, K-5.accumulatedMs) + kodsuz
  S-10 kavramı · (a′) 1 (iki satır) · (b) 9 · (c) 1. Ayrıca `CycleRoundDecision.Continue` gibi
  ilgisiz eşleşmeler (false positive) var — onlara dokunulmaz (§1.5).
- **Gerçek bug: kodda YOK.** `OnBuildPreview` guard'ının koşulu üretimde oluşamıyor (§4); gizli bir bastırma kusuru yok.
  Yorum düzeyinde iki "gerçek yanlış" var: RC:903-906 (modlar ters yazılmış) ve RVM:1446-1450 (A2 kuralıyla çelişiyor).
- **Doküman ↔ kod uyuşmazlığı 5 yerde** (ARCH:213, 402-403, 1288-1289, 2329, 5061) — CLAUDE.md gereği kullanıcıya soruluyor, sessizce seçilmiyor (§5).
- Temizlik 8 task'a bölündü (T1-T5 + T7 önerilir, T6/T8 opsiyonel) — §6.

---

## 1. Yapı envanteri ve sınıflandırma (soru 1 + 2)

### 1.1 Core (Scheduling)

| # | Yapı | Konum | Üretim kanıtı | Sınıf |
|---|---|---|---|---|
| K-1 | `RunSnapshot` record | RSN:16-19 | Tohum taşıyıcısı: RC:693, RC:842 (`new RunSnapshot(seed, [], 0)`), RC:859. `TakeSnapshot` dönüşü RC:1047 → yalnız `.Queued.Count` okunur (RC:1070, RC:1073). `Completed` = tohum; `Queued` = yalnız bitiş sayısı; **`ElapsedMs` hiçbir yerde okunmuyor**. | **b** (tohum + bitiş partisyonu DTO'su); alt üye `ElapsedMs` **a** |
| K-2 | Snapshot ctor `ReadySetScheduler(BuildPlan, RunSnapshot, CycleGroups?)` | RSS:77-101 | RC:859 (tohum null değilse) + tohumsuz ctor `this(plan, EmptySnapshot, …)` ile RSS:51-54. | **b** — "resume ctor" değil, **koşu başı pre-skip tohum ctor'u**: Build'de "up to date" (RC:833-840), Cycles'ta kapsam dışı + güncel upstream + grup düzeyinde güncel SCC (RC:771-841) |
| K-3 | `EmptySnapshot` | RSS:48-49 | Tohumsuz ctor'un devri (RSS:52) | **b** |
| K-4 | `TakeSnapshot(long elapsedMs)` | RSS:246-254 | Tek çağrı RC:1047; sonucu yalnız `.Queued.Count` (RC:1070/1073). | **b** — koşu sonu "tamamlanmamış düğüm sayısı" (in-flight DAHİL); parametre `elapsedMs` **a** (yalnız ölü `RunSnapshot.ElapsedMs`'e akar) |
| K-5 | `RunClock` | RCK:23-72 | RC:845 `new RunClock(nowMs)`, RC:892 `Start()` (bir kez), RC:1046 `Pause()` (bir kez, finally'de), RC:1047/1070/1073 `ElapsedMs`. | **b** — tek aralıklı koşu kronometresi; `Pause` değeri dondurur ki üç okuma aynı sayıyı versin. Alt üyeler: `accumulatedMs` ctor parametresi (RCK:32) **a** (yalnız T:RCKT:33, :43); Pause→Start birikimi üretimde hiç kullanılmıyor (Start bir kez) |

### 1.2 Supervisor

| # | Yapı | Konum | Üretim kanıtı | Sınıf |
|---|---|---|---|---|
| S-1 | `RunSegmentAsync` | RC:685 | Tek çağıran RC:613 (`ExecuteRunAsync`). | **b** — koşunun gövdesi; "segment" adı yanıltıcı (koşu başına bir) |
| S-2 | `schedulerSeed` | RC:690-693, 842, 857-859 | Build/Cycles tohumu | **b** |
| S-3 | `elapsedAtStart` yereli | RC:694, **RC:844 (`= 0`)**, RC:896, RC:968-970 | Hep 0 → `RunStartedEvent.ElapsedMsAtStart` + decision.log `elapsedAtStart=0ms` | **a** (sabit değer) |
| S-4 | Torn-DLL guard kümesi: `_stoppedFailedIds` alanı, `stoppedFailedIds` yereli, `RunContext.StoppedFailedIds`, `MarkStoppedFailed`, `TryRemove` | RC:185-192, 696, 749, 1011, 1081, 1329, 1357, 2164-2174, 2384-2387 | **Okuyucu yok.** `git grep StoppedFailed` yalnız yazımlar + temizlik verir; okuyan `RetryPlanning.RequeueStoppedFailed` `a2ff12e`'de silindi (git log -S). Testlerde de referans yok. | **a** (write-only) |
| S-5 | `_depIssuesById` ALANI | RC:184, 748, 1081 | Yalnız yazılır/temizlenir. (Yerel `depIssuesById` + `RunContext.DepIssuesById` CANLI.) | **a** |
| S-6 | `_plan` alanı | RC:180, 744, 1081 | Yalnız yazılır/temizlenir. | **a** |
| S-7 | `_root` + `SameRootLocked` + `Canonical` | RC:181, 269-278, 745, 1081 | `SameRootLocked`'ın çağıranı YOK; `_root`'u yalnız o okur; `Canonical`'ı yalnız bu ikisi kullanır (Core'daki `ExternalWorkspaceResolver.Canonical` ayrı). | **a** |
| S-8 | `_logs?.Dispose()` | RC:743 | Normal bitişte finally `_logs = null` yapar (RC:1081). Ama RC:862-864 (`msbuildNotFound`) try/finally'den (RC:974) ÖNCE `return` eder → `_logs` açık kalır; RC:750-974 arası beklenmeyen hata da aynı. Bir sonraki koşu onu RC:743'te kapatır. | **b** — özgün gerekçe "terk edilmiş resumable run", bugünkü "runStarted'a varmadan dönmüş koşunun açık writer'ı" |
| S-9 | Graceful drain / "torn DLL yok" garantisi | RC:174-177, 324-348, 428-449 (ARCH §4.5) | `TryRequestStop` → `DrainCapLocked` (RC:315, 338-348) | **c** — Stop'un kendi garantisi, Continue'ya özgü değil. (Continue'ya özgü "torn-DLL guard" = S-4, ölü.) |
| S-10 | "BuildPreview segment başına yeniden yayınlanır" kavramı | Yayın tek: RC:926 | Koşu başına BİR `BuildPreviewEvent` | **a** (kod yok; yalnız yorumlarda yaşıyor) |

### 1.3 Contracts

| # | Yapı | Konum | Üretim kanıtı | Sınıf |
|---|---|---|---|---|
| C-1 | `RunStartedEvent.ElapsedMsAtStart` | IPC:298-299 | Üretici hep 0 (RC:844→896); tüketici RVM:1878-1880. | **a** (değer sabit; kabloda CANLI) |
| C-2 | `RunMode` | IPC:70 | `{ Rebuild, Build, Cycles, Clean }` — Continue/RetryFailed yok | temiz |

### 1.4 App

| # | Yapı | Konum | Üretim kanıtı | Sınıf |
|---|---|---|---|---|
| A-1 | `BeginRunAsync(…, bool clearBuffers, …)` | RVM:907, 914, 933 | 7 çağıranın HEPSİ `clearBuffers: true`: RVM:1119, 1140, 1150, 1165, 1175, 1181, 1188. Private; testler çağırmıyor. | **a** (false dalı erişilemez) |
| A-2 | `OnBuildPreview` terminal-satır guard'ı | RVM:1944 (doc RVM:1903-1915) | Koşul üretimde oluşamaz — §4 | **a** |
| A-3 | `_elapsedBaseMs` | RVM:462, 1729, 1878 | Hep `ElapsedMsAtStart` = 0 | **a** |
| A-4 | `OnProjectDone`'da `row.CycleUnconverged = false`; `OnProjectStarted`'da `row.SkipReason = null` | RVM:2110; RVM:2012 | `NeutralizeRows` her işlem başında ikisini de temizler (RVM:1054, RVM:1057); bir koşuda proje ya atlanır ya derlenir; `ProjectSkippedEvent.CycleUnconverged` üretimde hep false (RC:830, 840, 989, 1260, 1447, 1457). | **a′** — savunmacı, TUT; yorumları yanlış (§2) |
| A-5 | Stream `_pendingRunStartMode = null` | RVS:173 | Koşu SONRASI gelen Sync önizlemesinin ikinci bir "Build started" satırı basmasını önler | **b** |
| A-6 | `CanRebuildOrRetry` adı | RVM:1135 | CANLI kapı (Build/Rebuild/Clean/Cycles/satır komutları) | ad kalıntısı ("Retry") — kozmetik |

### 1.5 İlgisiz eşleşmeler (dokunma)

- `CycleRoundDecision.Continue` (Core/Planning/CycleRoundPolicy.cs:7, 78; RC:1570, 1583, 1609, 1766, 1771, 1776, 1786, 1831, 1847, 1850, 1869, 1876, 2029) — SCC tur politikasının "bir tur daha" kararı; Continue özelliği değil.
- IPC:495-496 `CycleOutcome` doc'u ("Continue (yarıda kesilme) bir karar DEĞİLDİR") — tur kararı, DOĞRU.
- "sonraki Build kaldığı yerden devam eder" (PM:146, CycleRoundPolicy.cs:33, RC:1906) — tur tavanının Build semantiği, DOĞRU.
- `SnapshotAndReplace`, `Ds.Segment`, `ProjectLogFile.Snapshot`/`SnapshotProjectLog`, `SnapshotCollection`, `IdleResume`, `ResumeThread`, `ResumeFilter`, `continue;` — hepsi ilgisiz.

---

## 2. Yorum doğruluğu (soru 3)

Tarih olarak yazılmış doğru ifadeler ("Eskiden…", "[DEĞİŞEN KURAL]", "kaldırıldı") listeye alınmadı. **YANLIŞ** = bugünkü koda
göre yanlış iddia; **YANILTICI** = "taze segment" gibi artık var olmayan bir ayrımı ima eden niteleyici; **kozmetik** = yalnız
"run/segment" kelimesi.

### 2.1 Core

| # | Konum | Şu an ne diyor | Durum | Doğru anlam (tek satır) |
|---|---|---|---|---|
| 1 | RSS:42-47 | "boş snapshot … Fresh ctor'u resume ctor'un ÜZERİNE kurmak" | YANILTICI | Boş tohum: tohumsuz ctor, tohumlu ctor'a boş Completed ile devreder (tek gövde). |
| 2 | RSS:56-70 | "[T55] Continue (resume) ctor'u: AYNI plan'dan devam eder … normal akışta TakeSnapshot'tan gelen snapshot … savunmacı: snapshot cycle üyelerini taşımıyorsa" | YANLIŞ | Koşu başı pre-skip tohum ctor'u: tohumdakiler Skipped sayılır, dispatch edilmez. Build tohumu SCC üyelerini BİLEREK taşımaz (RC:824-833) → gruplar null iken üyeler burada "in dependency cycle" ile pre-skip edilir: bu savunmacı değil, NORMAL yoldur. |
| 3 | RSS:231-245 (+233) | "[T55] Stop/Continue sınırını aşacak run state … Task 9 worker'lar koşarken de çağırabilir … bkz. ContinueRunTests.take_snapshot_mid_run…" | YANLIŞ | Koşu sonunda (worker'lar join olduktan sonra, RC:1043-1047) tamamlanmamış düğüm sayısını verir, in-flight DAHİL; `RunCompleted.Queued` bundan. `ContinueRunTests` `a2ff12e`'de silindi. |
| 4 | RSN:5-15 | "Stop/Continue sınırını aşan run state … resume ctor'una geçirilir" | YANLIŞ | Koşu başı tohum (Completed) + koşu sonu partisyon (Queued) taşıyıcısı; `ElapsedMs` hiç okunmaz. |
| 5 | RCK:4, 8-11, 14-16, 18-21 | "segment segment biriktiren saat", "Continue senaryosu … accumulatedMs ile yeni RunClock", "Start=run/resume, Pause=Stop, çift Stop tıklaması", "ElapsedMs Task 9'un UI thread'inden okunabilir" | YANLIŞ | Koşu süre saati: `runStarted`'dan hemen önce Start (RC:892), her koşunun finally'sinde Pause (RC:1046, Stop'a özgü değil) — değer dondurulur ki `runCompleted.DurationMs` ile decision.log aynı süreyi yazsın; üretimde tek akıştan okunur, tohum üreticisi yok. |
| 6 | PPL:11 | "`BuildRunPlan`'ı (bir run'ın taze segmenti)" | YANILTICI | "(her koşunun başında)". |

### 2.2 Supervisor

| # | Konum | Şu an ne diyor | Durum | Doğru anlam |
|---|---|---|---|---|
| 7 | PRG:66-68 | "(fresh modda) incremental … Planlayıcı yalnız fresh (Rebuild/Build) modda çağrılır (Continue/RetryFailed mevcut plan'dan devam eder)" | YANLIŞ | Planlayıcı HER koşuda (4 mod) çağrılır; incremental geçişte mod kapısı yok (PRG:131-132, 172-217; Cycles yalnız bileşik imza bayrağı PRG:199). |
| 8 | PRG:162 | "[Task 19] Fresh (Rebuild/Build) run için incremental karar" | YANILTICI | "Her koşu için incremental karar". |
| 9 | RC:28 | "Bir fresh (Rebuild/Build) run için incremental karar verileri" | YANILTICI | "Her koşunun incremental verileri (Clean'de persist yerine kayıt silinir)". |
| 10 | RC:70-71 | "… disk log + IPC event → Stop/Continue" | YANLIŞ | "→ Stop". |
| 11 | RC:99-100 | "Continue AYNI writer'ı (aynı run dizinini) kullanır — log dikişi bozulmaz" | YANLIŞ | Her koşu kendi writer'ını/dizinini açar (RC:746). |
| 12 | RC:185-191 | StoppedFailedIds: "run segmentleri arası kümülatif … Continue/RetryFailed segmentleri BOYUNCA … RetryPlanning re-queue … RequeueStoppedFailed'ın savunmacı re-check'i" | YANLIŞ | Kodla birlikte silinir (T1). |
| 13 | RC:203 | "Aktif/resumable run varsa canlı writer'dan" | YANLIŞ | "Aktif koşu varsa (ya da runStarted'a varmadan dönmüş bir koşunun hâlâ açık writer'ı varsa — RC:743) canlı writer'dan". |
| 14 | RC:272-273 | `Canonical`: "IPC dispatch loop'undan (StartAsync) çağrılır" | YANLIŞ | `StartAsync` çağırmıyor; metot T1'de siliniyor. |
| 15 | RC:300 | "Terminate edilmiş Job yeni process kabul ettiği için ikisi de Continue'ya açıktır" | YANLIŞ | "…ikisinden sonra da bir sonraki koşu aynı inner job'da başlayabilir". |
| 16 | RC:690-692 | "resume'da devralınan snapshot … üç ayrı kurulum" | YANLIŞ | "Build/Cycles'ta pre-skip tohumu, aksi halde null; scheduler tek yerde kurulur". |
| 17 | RC:743 | "terk edilmiş (artık sürdürülmeyecek) önceki run'ın writer'ı" | YANILTICI | "runStarted'a varmadan dönmüş önceki koşunun (msbuildNotFound RC:862-864 ya da beklenmeyen hata) kapanmamış writer'ı — finally'si (RC:1081) hiç koşmadı". |
| 18 | RC:866 | "her taze koşuda tetiklenir" | kozmetik | "her koşuda". |
| 19 | RC:903-906 | "(Bir Build segmenti store'u toplam İKİ kez okur … Build DIŞINDAKİ modlarda ilki hiç çalışmaz.) Segment 2'nin okuduğu map segment 1'in persist'lerini İÇERİR…" | YANLIŞ (iki hata) | Yakınsamama taraması YALNIZ Cycles'ta koşar (RC:771-804): koordinatör store'u Cycles'ta iki, diğer modlarda bir kez okur. İkinci segment yok: önizleme koşu başındaki defteri okur. |
| 20 | RC:984-985 | "resume edilmiş scheduler'ın PreSkipped'i BOŞTUR, bu yüzden Continue'da tekrar yazılmazlar" | YANLIŞ | PreSkipped: Rebuild/Build'de döngü üyeleri ("in dependency cycle"; tohum onları taşımaz), Cycles'ta boş (gruplar turlarla derlenir), Clean'de boş (plan döngü işaretsiz, CleanRunScope). |
| 21 | RC:1059 | "Run genelinde (Continue segmentleri DAHİL, kümülatif)" | YANLIŞ | "Bu koşunun dependency-affected proje sayısı". |
| 22 | RC:1357 | "Continue'un torn-DLL guard'ı için izlenir" | YANLIŞ | Satır silinir (T1). |
| 23 | RC:1490-1491 | "(ör. resume edilmiş bir run'dan devralınan)" | YANLIŞ örnek | "(ör. tohumla Skipped girilmiş — Cycles tohumu grubu hep bütün olarak girer, RC:815-816; yani savunmacı)". |
| 24 | RC:2164-2169 | `MarkStoppedFailed` doc: "run'lar arası devredilen … Continue'un RetryPlanning.RequeueStoppedFailed çağrısı" | YANLIŞ | Kodla birlikte silinir (T1). |
| 25 | RC:2381-2382 | "RunSegmentAsync'te kurulur, Continue segmentleri boyunca aynı örnek paylaşılır" | YANLIŞ | "Koşu başına tek birikim (RC:748)". |
| 26 | RC:2384-2386 | `StoppedFailedIds`: "Continue segmentinin torn-DLL guard'ı … RetryPlanning.RequeueStoppedFailed" | YANLIŞ | Kodla birlikte silinir (T1). |

Bugün doğru ama T2/T8 ile güncellenmesi gerekecek olanlar: RC:1037 ve RC:1043-1045 ("snapshot" — `TakeSnapshot` değişince);
RC:162, 216, 365, 625, RVM:2336, IPC:600, T:RVMST:238 (`RunSegmentAsync` adı — T8'de ad değişirse).

### 2.3 Contracts

| # | Konum | Şu an ne diyor | Durum | Doğru anlam |
|---|---|---|---|---|
| 27 | IPC:342 | "Bir run'ın TAZE segmentinde, RunStartedEvent'ten ÖNCE…" | YANILTICI | "Her run'da, RunStartedEvent'ten ÖNCE…". |
| 28 | PM:5-7 | "RunRequest.mode genişlemesi (Build/RetryFailed, DependentMode) artık IpcMessages.cs'de sabit" | YANLIŞ | "RunMode (Rebuild/Build/Cycles/Clean) ve DependentMode IpcMessages.cs'dedir". |

### 2.4 App (görev tanımı "App yorumları temizlendi" diyor; aşağıdakiler hâlâ yanlış)

| # | Konum | Şu an ne diyor | Durum | Doğru anlam |
|---|---|---|---|---|
| 29 | APH:27 | "taze bir segmentte bu pencerede planlama koşar" | YANILTICI | "her koşuda bu pencerede planlama koşar". |
| 30 | RBT:147 | aynı niteleyici | YANILTICI | aynı. |
| 31 | RVM:943-944 | "(taze segmentte: tarama → graf → topo → incremental)" | YANILTICI | niteleyici düşer. |
| 32 | RVM:1446-1450 | "hata etkilenmiş bağımlılar imzalarını hiç persist etmedikleri için kümeye kendiliğinden girer … RunMode üç değerlidir" | YANLIŞ | Bağımlılar imzalarını not + köklerle persist eder, kök düzelince derlenir (A2: RC:1330-1336, IPC:79-81); RunMode dört değerlidir (IPC:70). |
| 33 | RVM:1886 | "Build/Cycles'ta liste (önceki segmentin sonuçları) olduğu gibi korunur" | YANLIŞ | Build/Cycles/Clean'de burada ikinci nötrleme yok, çünkü BeginRunAsync tıklamada TÜM satırları nötrledi (RVM:919 → RVM:1045-1066); korunan yalnız satır nesneleri ve çıktı-durumu alanları. |
| 34 | RVM:1894 | "bu run/segment için taze başlar" | kozmetik | "bu run için". |
| 35 | RVM:1903-1915 | "Satır zaten varsa bu savunmacı bir edge case DEĞİL: RunCoordinator her segmentin başında AYNI (dondurulmuş, segment-1 zamanlı) plan'dan türetilmiş BuildPreviewEvent'i YENİDEN yayınlar, Projects Continue'da temizlenmez…" | YANLIŞ | Önizleme koşu başına bir kez, runStarted'ın hemen ardından ve ilk proje olayından önce gelir (RC:895→926→988); satırlar tıklamada nötrlendiği için o anda terminal satır yoktur (§4). |
| 36 | RVM:1925-1928 | "segment 2'nin okuduğu değer segment 1'in persist'ini içerdiği için terminal satırların sol yarısı ancak burada TAZELENİR" | YANLIŞ | "CurrentSha her önizlemeden koşulsuz yazılır". |
| 37 | RVM:1939-1943 | "segment 1'in canlı succeeded→clean geçişi segment 2'nin bayat önizlemesiyle ezilmesin" | YANLIŞ | Guard'la birlikte silinir (T5). |
| 38 | RVM:1958-1959, 1960-1967 | "AYNI (terminal satır) guard'ın içinde", "InRunQueue … AYNI guard'ı PAYLAŞAMAZ" | kodla birlikte değişir | T5'te: guard yok; InRunQueue'nun `RunActive` kapısının gerekçesi kalır. |
| 39 | RVM:2012 | "önceki segmentin atlama gerekçesi geçersiz" | YANLIŞ | "Savunmacı: NeutralizeRows tıklamada temizler; bir koşuda proje ya atlanır ya derlenir". |
| 40 | RVM:2106-2109 | "önceki bir segmentten kalma … satır nesneleri segmentler arası hayatta kaldığı için (Projects.Clear() yalnız Rebuild'de)" | YANLIŞ | "Savunmacı: NeutralizeRows (RVM:1054) her işlem başında zaten temizler; OnRunStarted'da Projects.Clear() yok (RVM:1882-1885)". |
| 41 | RVS:38, 123, 134, 140 | "run/segment" | kozmetik | "run". |
| 42 | RVS:145-147 | "Pending'i TEMİZLE ki re-emit edilen bir BuildPreview çift satır yaymasın" | YANILTICI (kod CANLI) | "…ki koşudan sonra gelen bir Sync önizlemesi başlangıç satırını ikinci kez yaymasın". |
| 43 | RVM:893-898, 911-913, 1124-1126, 1211 | `clearBuffers` parametresine atıflar | kodla birlikte değişir | T4'te: "önceki koşunun tortusu her koşuda temizlenir". |

### 2.5 Doğru (dokunma)

RC:1075-1079 (tarih), RC:174-177/324-348/437-439 ("torn DLL yok" — graceful drain), IPC:76-82 (Mode doc: "Sürdürme/yeniden deneme
AYRI bir mod DEĞİLDİR … elapsed sıfırdan"), IPC:495-496, RVM:1386-1391 ("Continue kalktıktan sonra da graceful"), RVM:2336,
RBT:195-196, `Shell/KeyboardShortcuts.cs:112`, `Views/ActionBar.xaml.cs:582`, `Views/BuildMenu.xaml.cs:74`,
`Controls/SplitButton.cs:32`, README.md:277 ve 292.

---

## 3. Testler (soru 4)

### 3.1 Yalnız ÖLÜ yapıyı pinleyenler (yapıyla birlikte silinir ya da CLAUDE.md gereği yeni kurala göre yeniden yazılır)

| Test | Konum | Pinlediği ölü şey | Öneri |
|---|---|---|---|
| `A_later_segments_preview_refreshes_the_current_sha_of_an_already_terminal_row` | T:RVMT:1583-1613 | A-2 guard (koşu içinde ikinci önizleme — üretimde yok) | T5: koşu SONRASI terminal satırda CurrentSha tazelenir biçimine yeniden yaz ([DEĞİŞEN KURAL] doc) |
| `BuildPreviewEvent_on_a_Continue_segment_does_not_clobber_an_already_clean_row` | T:RVMT:2141-2161 | A-2 guard ("Continue segmenti") | T5: "koşunun önizlemesi nötrlenmiş satıra iner ve kararı yazar" biçimine yeniden yaz (BuildCommand + `MainWindowHost.AcceptSends` + `VmTopology.Seed`) |
| `elapsed_while_running_includes_current_segment_on_top_of_accumulated` | T:RCKT:30-38 | `accumulatedMs` | T3: tohumsuz sürüme yeniden yaz |
| `elapsed_before_first_start_is_only_the_accumulated_seed` | T:RCKT:41-46 | `accumulatedMs` | T3: "Start'tan önce 0" ya da sil |
| `segment_accumulation_pause_gap_does_not_count` | T:RCKT:10-27 | Pause→Start birikimi (üretim kullanmıyor) | T3 Öneri A'da KALIR (genel semantik); Öneri B'de silinir |
| `RunStarted_sets_ElapsedMs_from_ElapsedMsAtStart` | T:RVMT:555-564 | sıfırdan farklı taban (yalnız Continue'da vardı) | T6 yapılırsa silinir |

### 3.2 CANLI davranışı yeniden amaçlanmış yapılar üzerinden pinleyenler (yeşil kalmalı)

- **TakeSnapshot → Queued sayısı (K-4):** T:RCT:268, 308, 364, **465** (`graceful_stop_lets_in_flight_project_finish_and_leaves_the_rest_queued`, Queued=3), 504, 552, 702, 732, 1005, 1080; T:CRT:114, 318, 370, 584, 761; `Supervisor/FullCleanRunTests.cs:48`; `Supervisor/SingleProjectRunTests.cs:44`; `Integration/OsysRebuildAcceptanceTests.cs:306` (Acceptance).
- **Tohum ctor'u (K-2, S-2):** T:RCT:1780 `Build_mode_pre_skips_up_to_date_nodes_without_invoking_msbuild_and_persists_the_built_ones`, T:RCT:1826 `A_failed_project_is_invalidated_in_build_state_so_the_next_Build_cannot_pre_skip_it`, T:RCT:1619 `A_project_built_elsewhere_is_skipped_as_up_to_date_and_not_recorded`; T:CRT:174 `a_cycles_run_builds_the_dirty_upstream_of_a_cycle_and_pre_skips_everything_else` (kapsam dışı tohum), T:CRT:209 `a_clean_upstream_inside_the_scope_is_skipped_as_up_to_date_not_as_out_of_scope`, T:CRT:238 `a_cycles_run_pre_skips_a_cycle_whose_composite_signature_is_clean`.
- **Tohumsuz yol + PreSkipped:** T:RCT:969 `cycle_members_are_pre_skipped_again_by_every_fresh_run`, T:CRT:90 `a_build_run_pre_skips_every_member_with_the_original_reason_and_runs_no_rounds`, T:RSST (tümü tohumsuz ctor).
- **RunClock (K-5):** T:RCKT:49, 63, 77, 91 (idempotence, farklı thread, null) — Öneri A'da kalır.
- **Her koşunun kendi planı / stale-obj uyarısı:** T:RCT:415 `every_run_plans_freshly_and_prints_its_own_planning_progress`, T:RCT:1327 `every_fresh_in_place_run_re_diagnoses_and_warns_about_stale_obj_again`.
- **ElapsedMsAtStart'a dokunan CANLI testler (yalnız T6'da değişir):** T:RVMT:1098 `TickElapsed_uses_the_injected_clock_deterministically` (taban 500 tesadüfi → 0), T:RCT:346 (`Assert.Equal(0, started.ElapsedMsAtStart)` düşer), T:IPCT:69 (4200), T:IPCT:766, 771; ayrıca `new RunStartedEvent(…, 0)` kuran ~190 çağrı (RVMT 47, RVMST 27, EventStreamTests 22, ActionBarTests 10, …).
- **A-4 savunmacı satırlar:** T:RVMT:1492 `ProjectSucceeded_after_a_prior_CycleUnconverged_skip_clears_the_flag`, T:RVMT:1512 `ProjectFailed_after_…` — kod tutulduğu için KALIR; yalnız doc'ları (T:RVMT:1485-1490 "segmentler arası HAYATTA KALIR … Projects.Clear() yalnız Rebuild'de") yanlış.
- **Koşu sonrası önizleme:** T:RVMT:1616, 1645 `A_post_run_preview_refreshes_*` — guard'dan bağımsız, KALIR.

### 3.3 Yalnız ad/yorum kalıntısı (davranış CANLI; ad/doc güncellenir — T8)

T:RVMST:220 `RunCompleted_takes_the_phase_out_of_stopping_and_offers_continue` · T:RVMT:1266 `OnEngineExited_raises_CanExecuteChanged_for_Rebuild_Stop_and_Continue` (yalnız Rebuild+Stop'u doğrular) · T:RVMT:14, 17, 27, 851, 1042 · T:RCT:22 ("stop/continue"), 376, 432 ("taze segment"), 976/989/1000 (`firstSegment`), 1356-1367 (`warnsAfterSegment1/2`), 1712 ("her run/segment başında YENİDEN yayınlanır"), 2226 ("run/Continue") · `App/EventStreamTests.cs:612` · `App/AccessibilityTests.cs:374` ("'Build'/'Continue'") · `App/ActionBarTests.cs:735` · `App/KeyboardShortcutTests.cs:9` · `App/EnginePreflightTests.cs:114` · `App/VmTopology.cs:8` ("Build/Rebuild/RetryFailed") · `Supervisor/ProjectLogStreamTests.cs:225` ("resumable değil") · T:RVMST:418 (`BeginRunAsync(clearBuffers:true)`).

### 3.4 Kapsam boşlukları

- `ContinueRunTests` silindiğinden **tohum ctor'u ve `TakeSnapshot` için doğrudan birim testi yok**; "in-flight tamamlanmamış sayılır" iddiası hiçbir yerde pinli değil (yalnız koordinatör üzerinden dolaylı kapsam). T2'de iki birim testi eklenmeli.
- Koordinatör düzeyinde **koşu süresi pinli değil**: `Harness.SetNow` (T:RCT:216) hiç çağrılmıyor, yani `RunCompleted.DurationMs` testlerde hep 0.

---

## 4. `RunViewModel.OnBuildPreview` guard'ı (soru 5)

Guard: RVM:1944 `if (RunActive && row.State is Succeeded or Failed or Skipped) continue;` — `RunActive => IsRunning` (RVM:1539).
Atladığı alanlar: `WillBuild`, `Conditional`, `WillBuildReason`, `DependencyRoots`, `InRunQueue` (RVM:1953-1968).
`CurrentSha`, `OwnFilesChanged`, `LocalEdits` guard'dan ÖNCE yazılır (RVM:1929-1938).

**Kanıt zinciri — koşulun üretimde oluşamadığı:**

1. **Koşu başına tek önizleme, proje olaylarından önce.** Motor tek FIFO kanaldan yazar: `RunStartedEvent` RC:895 →
   `BuildPreviewEvent` RC:926 → pre-skip `projectSkipped`'ler RC:988-993 → worker'lar. İkinci bir `BuildPreviewEvent` üreticisi
   koşu içinde yok (src'de tek diğer üretici `Core/Workspace/SyncWorkspaceService.cs:176`, yani Sync).
2. **App sırayı korur.** `MainWindow.xaml.cs:317-318`: `ProjectLogEvent` dışındaki her olay `Dispatcher.InvokeAsync` ile aynı
   öncelikte kuyruğa girer → FIFO.
3. **Satırlar tıklamada nötrlenir.** `StartRunCommand`'ın App'teki TEK üreticisi RVM:973 (`BeginRunAsync`). O metot gönderimden
   önce RVM:919'da `NeutralizeRows()` çağırır; RVM:1045-1066 `Projects`'teki HER satırı `State = Pending` yapar
   (ScopeFor kapsamından bağımsız, tek-proje koşusunda da). Rebuild ayrıca RVM:1891'de ikinci kez nötrler.
4. **Önceki koşunun geç olayı gelemez.** Yeni koşu `CanStartRun` (RVM:1122: `!IsRunning && !IsStarting`) ister; `IsRunning`
   ancak `runStopped`/`runCompleted` (RVM:2295, 2342), koşu-bitiren hata ya da motor ölümüyle düşer — önceki koşunun proje olayları
   hepsinden ÖNCE kuyruktadır. Motor da uçuşta bir koşu varken `startRun`'ı `runInProgress` ile reddeder (RC:252-253).
5. **Sync önizlemesi koşu sürerken gelemez.** Sync kapısı `CanSync` → `WorkspaceGateOpen` → `WorkspaceIdle` → `!IsMidRunLocked`
   (RVM:1295, `RunViewModel.Workspace.cs:543-547`, RVM:860); kendiliğinden Sync `IsWorkspaceBusy => WorkspaceBusy || IsRunInFlight`
   (`RunViewModel.AutoSync.cs:72`); koşu komutları `!WorkspaceBusy` ister (RVM:1135); Sync'in önizlemesi `SyncCompleted`'tan önce
   gelir (`SyncWorkspaceService.cs:176` < `:211`); workspace işi → Sync devri kapıyı kesintisiz tutar
   (`RunViewModel.Workspace.cs:346-362`).

**Sonuç:** Önizleme işlendiği anda `RunActive == true` ise tüm satırlar `Pending`'dir; `RunActive == false` ise guard zaten
devrede değildir. Koşul üretimde **erişilemez**.

- **Gizli bug yok.** "Önceki koşudan terminal kalmış satır + koşu sürerken yeni önizleme" durumu, adım 3 nedeniyle oluşmuyor.
- **Önemli ayrıntı:** Koşul bir gün oluşursa (BeginRunAsync'i atlayan bir koşu başlatma yolu), guard önceki koşunun terminal
  satırları için BU koşunun kararını (WillBuild/Reason/DependencyRoots) ve kuyruk işaretini (InRunQueue) **bastırırdı**.
  Guard'ın koruduğu senaryo (Continue'nun bayat ikinci önizlemesi) artık yok; kaldığı haliyle yalnız meşru bir güncellemeyi
  bastırabilecek yönde duruyor.
- **Kaldırmak üretim davranışını değiştirmez.** Değişen tek şey, BeginRunAsync'i atlayıp olay dizisini elle kuran iki test
  (T:RVMT:1583, T:RVMT:2144) — ikisi de üretimde olmayan "koşu içi ikinci önizleme"yi kuruyor.
- **Satır tutulursa** yorumu "savunmacı; bugün koşu başına tek önizleme var ve satırlar tıklamada nötrlenir" diye yeniden
  yazılmalı. Öneri yine de kaldırmak (T5), çünkü kod okuyana olmayan bir senaryoyu anlatıyor.

---

## 5. Doküman ↔ kod uyuşmazlıkları (kullanıcı kararı gerekir — CLAUDE.md: sessizce seçme)

| # | ARCHITECTURE.md | Doküman ne diyor | Kod ne yapıyor | Önerilen metin (onay sonrası) |
|---|---|---|---|---|
| D1 | :213 | "`runStopped` and `runCompleted` each fire exactly once, and the elapsed clock is preserved." | Süre `runCompleted.DurationMs` ile raporlanır (RC:1069-1070) ve App onu gösterir (RVM:2294); bir sonraki Build sıfırdan sayar (`ElapsedMsAtStart` hep 0; ARCH:1080, README:292). "Preserved" Continue'nun süreyi taşımasını anlatıyordu. | "…each fire exactly once; the stopped run's elapsed time is reported in `runCompleted` (the next Build counts from zero)." |
| D2 | :402-403 | "carries the planning steps of a fresh segment (§8.6)" | Her koşu planlar (ARCH:1322 de böyle diyor) | "carries the planning steps of the run (§8.6)" |
| D3 | :1288-1289 | "For a fresh run (`Build`/`Rebuild`) the sequence is:" | Planlayıcı dört modda da koşar (PRG:131-132); aynı bölümün :1322'si "Every run plans" diyor | "For every run the sequence is:" (+ Cycles/Clean farkları varsa bir cümle) |
| D4 | :2329 | "declining a request with nothing to resume leaves the `stopped` line standing" | `noResumableRun` reddi `a2ff12e`'de kalktı; bugünkü redler `runInProgress`, `cleanRejected`, `checkoutRejected`, `optimizeRejected` | "declining a request leaves the `stopped` line standing" |
| D5 | :5061 (§22) | "Run snapshot and elapsed clock across segments \| RunSnapshot.cs, RunClock.cs" | Segment yok; RunSnapshot tohum/partisyon taşıyıcısı; T2/T3 sonrası dosya listesi de değişecek | T2+T3 seçimine göre: "Run elapsed clock \| `Core/Scheduling/RunClock.cs`" (Öneri A) ya da satır silinir (Öneri B); scheduler satırına (:5050) "pre-skip seed" eklenir |

---

## 6. Temizlik planı (soru 6)

İlke: davranış DEĞİŞMEZ (yalnız ölü kod + yorum doğruluğu). Her task kendi yorumlarını da düzeltir; T7 geri kalanları toplar.
CLAUDE.md "5'ten fazla bulgu → önce kısa TDD dökümü (`.claude/outputs/`), sonra subagent-driven-development" kuralı bu plana
da uygulanır.

### T1 — Supervisor: ölü koşu-durumu alanları (S-4, S-5, S-6, S-7) · Risk: düşük

- **Sil:** RC:185-192 (yorum + `_stoppedFailedIds`), RC:696 + RC:749 (yerel + atama), RC:1011 (RunContext argümanı),
  RC:1329 (`TryRemove`), RC:1357 (`MarkStoppedFailed` çağrısı), RC:2164-2174 (metot), RC:2384-2387 (RunContext parametresi);
  RC:180 + RC:744 (`_plan`); RC:181 + RC:745 + RC:269-278 (`_root`, `SameRootLocked`, `Canonical`); RC:184 ve RC:748'deki alan
  ataması (`depIssuesById = new …` kalır); RC:1081 → `lock (_gate) _logs = null;`.
- **Tut:** RC:743 `_logs?.Dispose()` (CANLI, S-8) — yorumunu düzelt (§2 #17). RC:203 yorumunu düzelt (#13).
- **Testler:** değişmez (`git grep` testlerde bu adların hiçbirini bulmuyor). Tam süit.
- **Not:** `RunContext` positional record — parametre düşünce tek kurulum yerindeki (RC:998-1028) sıra kayar; tip farkı
  (`ConcurrentDictionary<string,byte>` → `BuildStateStore?`) derleyiciye yakalatır.

### T2 — Core scheduler: `RunSnapshot`'ı kaldır, tohum ve bitiş API'sini adlandır (K-1…K-4, S-2) · Risk: düşük-orta

- **Değiştir:** `ReadySetScheduler(BuildPlan, RunSnapshot, CycleGroups?)` → `ReadySetScheduler(BuildPlan, IReadOnlyDictionary<string, BuildResult> seed, CycleGroups?)`
  (tohum hep `Skipped` — RC:829, 839 — isteğe göre `IReadOnlySet<string>`); `EmptySnapshot` → boş sözlük.
- **Değiştir:** `RunSnapshot TakeSnapshot(long)` → `int UnfinishedCount` = Completed'ta olmayan düğüm sayısı, **in-flight DAHİL**.
  **`QueuedProjectIds` KULLANILMAMALI:** in-flight'ı hariç tutar ve worker-fault yolunda (RC:1034-1039) 1 fark eder.
- **Sil:** RSN (dosya). RC:693/842/857-859 → `Dictionary<string, BuildResult>? schedulerSeed`; RC:1047 → `int unfinished = scheduler.UnfinishedCount;`,
  RC:1070/1073 `snapshotAtEnd.Queued.Count` → `unfinished`.
- **Yorumlar:** §2 #1-4, #16, #20, #23; RC:1037 ve RC:1043-1045 ("snapshot" → "kalan sayısı").
- **Testler:** T:RSST'ye iki pin eklenir: (1) tohumlu ctor — tohumdaki id dispatch edilmez ve bağımlısı için çözülmüş sayılır;
  tohumda olmayan InCycle düğüm, gruplar null iken "in dependency cycle" ile PreSkipped'e düşer (Build'in normal yolu);
  (2) `UnfinishedCount` in-flight'ı sayar (silinen `ContinueRunTests.take_snapshot_mid_run…` iddiasının karşılığı).
  §3.2'deki Queued/tohum testleri yeşil kalmalı.
- **Doküman:** D5 (ARCH:5050, 5061) — onay sonrası.

### T3 — `RunClock`: Continue yüzeyini düşür (K-5) · Risk: A düşük / B düşük-orta

- **Öneri A (asgari, önerilen):** `accumulatedMs` ctor parametresini sil (RCK:32-37); doc'u (RCK:3-22) bugünkü kullanıma göre yaz
  (§2 #5). Start/Pause/ElapsedMs ve idempotence kalır. Testler: T:RCKT:30 ve :41 tohumsuz sürüme yeniden yazılır; doc'larına
  eski iddia ("saat önceki segmentin süresiyle tohumlanır") ve değişme gerekçesi (tohum üreticisi `a2ff12e` ile kalktı;
  `git grep accumulatedMs` → yalnız testler) yazılır; T:RCKT:5 sınıf yorumu güncellenir; T:RCKT:10 kalır.
- **Öneri B (derin):** RunClock'u kaldır; RC:892'de `long startedAtMs = nowMs();`, finally'de tek `long durationMs = nowMs() - startedAtMs;`
  (RC:1046-1047), üç okuma bu yerelden. RCK + T:RCKT silinir. Koordinatör düzeyinde süre pini yok (§3.4), bu yüzden
  B seçilirse `Harness.SetNow` ile bir süre pini eklenmeli.
- **Doküman:** D5.

### T4 — App: `BeginRunAsync`'in `clearBuffers` parametresi (A-1) · Risk: düşük

- RVM:907 imzadan parametreyi çıkar; RVM:914 ve RVM:933'te çağrılar koşulsuz olur; 7 çağıran (RVM:1119, 1140, 1150, 1165, 1175,
  1181, 1188) sadeleşir; yorumlar §2 #43.
- **Testler:** değişmez. T:RVMST:418 doc'u isteğe bağlı güncellenir.

### T5 — App: `OnBuildPreview` terminal-satır guard'ı (A-2) · Risk: düşük (üretimde erişilemez, §4)

- **Sil:** RVM:1944 ve RVM:1939-1943.
- **Yeniden yaz:** RVM:1903-1915, 1925-1928, 1958-1959 satır sonu yorumları, 1960-1967 (InRunQueue'nun `RunActive` kapısı kalır,
  "AYNI guard'ı PAYLAŞAMAZ" ifadesi düşer).
- **Testler:** T:RVMT:1583 ve T:RVMT:2144 CLAUDE.md gereği sessizce silinmez. [DEĞİŞEN KURAL] doc'uyla (eski iddia + gerekçe:
  Continue `a2ff12e`'de kalktı, motor koşu başına tek önizleme yayar RC:926, BeginRunAsync satırları RVM:919'da nötrler)
  yeni değişmezi pinleyecek şekilde yazılır:
  (a) önceki koşuda Succeeded olan satır, `BuildCommand` (`MainWindowHost.AcceptSends` + `VmTopology.Seed`) sonrası `Pending`'dir;
  RunStarted + BuildPreview ardından WillBuild/Reason önizlemeden yazılır.
  (b) koşu SONRASI (RunActive=false) terminal satırda CurrentSha tazelenir.
  İkisi de kaldırma öncesinde ve sonrasında yeşildir (kusur fix'i değil, değişmez pini) — doc'ta bu belirtilir.
- **Uyarı:** İleride koşu içinde ikinci bir önizleme üreten bir özellik gelirse canlı succeeded→clean geçişlerinin korunması
  yeniden düşünülmeli.

### T6 — (opsiyonel, ayrı) Kontrat: `ElapsedMsAtStart` zinciri (S-3, C-1, A-3) · Risk: orta (tek kontrat değişikliği + mekanik churn)

- **Sil:** IPC:299 alanı (+ IPC:298 doc'u), RC:694/844/896 ve decision.log alanı `elapsedAtStart={6}ms` (RC:968-970),
  RVM:462/1729/1878-1880 `_elapsedBaseMs`.
- **Testler:** T:RVMT:555 silinir; T:RVMT:1098 tabanı 0; T:RCT:346 assert'i düşer; T:IPCT:69/766/771; ~190 `new RunStartedEvent(…, 0)` çağrısı.
- **Alternatif (churn'süz):** alanı tut, IPC:298 doc'una "her zaman 0 — her koşu sıfırdan sayar" yaz; App'e dokunma.

### T7 — Kalan yorum doğruluğu (kodu değişmeyen dosyalar) · Risk: yok (yalnız yorum)

§2'de T1/T2/T3/T4/T5'e girmeyen maddeler: #6-11, #15, #18, #19, #21, #25, #27-34, #39-42.
(T1: #12-14, #17, #22, #24, #26 · T2: #1-4, #16, #20, #23 · T3: #5 · T4: #43 · T5: #35-38.)
Özellikle #19 (RC:903-906, modlar ters yazılmış) ve #32 (RVM:1446-1450, A2 ile çelişiyor) içerik hatası taşıyor.

### T8 — (opsiyonel) Ad hijyeni · Risk: düşük

- `RunSegmentAsync` → ör. `PlanAndRunAsync` (RC:685, 613; atıflar RC:162, 216, 365, 625; RVM:2336; IPC:600; T:RVMST:238).
- `CanRebuildOrRetry` → ör. `CanStartOperationRun` (RVM:1135 + `RelayCommand` attribute'ları; T:RVMT:2449 yorumu).
- Test adları, değişkenleri ve doc'ları: §3.3.

**Sıra:** T1 → T2 → T3 (T2 ve T3 ikisi de RC:1046-1047'ye ve ARCH:5061'e dokunur, sıralı gitmeli) → T4 → T5 → T7; T6 ve T8 ayrı karar.
Her task sonunda tam süit (`--filter "Category!=Acceptance"`).

---

## 7. Gerçek bug değerlendirmesi

- **Kodda bug bulunmadı.** Guard erişilemez (§4); `UnfinishedCount` doğru uygulanırsa `TakeSnapshot` ile birebir aynı sayı;
  `RunClock` tek Start/tek Pause ile doğru.
- **Yorum düzeyinde gerçek yanlışlar:** RC:903-906 (yakınsamama taraması yalnız Cycles'ta, yorum tersini söylüyor);
  RVM:1446-1450 (A2 kuralıyla ve RunMode sayısıyla çelişiyor).
- **Tasarım gereği, bug değil:** msbuildNotFound yolunda `_logs` bir sonraki koşuya (ya da Dispose'a) kadar açık kalır;
  RC:743 onu kapatır.

---

## 8. Kapsam dışı gözlemler (bu temizliğe katılmaz)

- **Başka bir kaldırılmış özelliğin kalıntısı (Task 7, yakınsamama pre-skip'i):** `ProjectSkippedEvent.CycleUnconverged` üretimde
  hep false (RC:830, 840); `SkipReasons.CycleNonConvergent` (`Contracts/Ipc/SkipReasons.cs:24-25`) hiç yayılmıyor ama App'te
  okunuyor (`App/Console/ConsoleEmptyState.cs:86`); RC:1896-1898 doc'u hafızanın grubu "pre-skip eder" diyor, oysa RC:773-789
  "artık BLOKLAMAZ, yalnız RAPORLAR". `SkipReasons.cs:4` "dört yalın gerekçe" diyor, sabit sayısı beş.
- RVS:126 satır atfı "RunCoordinator.cs:456" bayat (yayın bugün RC:926).
- `tests/.../App/CleanCommandTests.cs:279, 396` `SyncCoreAsync(clearBuffers…)` diyor; imza artık `SyncMode` alıyor (RVM:1240).
- **Yanlış katman adayı:** Tohum hesabı (RC:754-843: up-to-date + Cycles kapsamı + grup kapısı) planlama mantığı olduğu hâlde
  Supervisor'da duruyor; CLAUDE.md "Planlama Core'da" ilkesine göre Core'a (ör. `CycleRunScope` yanına saf bir `RunSeed`)
  taşınabilir. T2 bunu yapmaz, yalnız tipini adlandırır.
- `ReadySetScheduler.QueuedProjectIds` ve `InFlight` yalnız testlerden okunuyor (üretim çağıranı yok).
