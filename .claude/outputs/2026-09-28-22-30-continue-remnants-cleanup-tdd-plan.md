# Continue kalıntıları temizliği + §13.3 düzeltmesi — TDD dökümü

- **Tarih:** 2026-09-28 · **Taban:** `main` `6224eeb` · **Branch:** `chore/continue-remnants-and-root-doc`
- **İnceleme (kanıtlar, satır numaraları):** `.claude/outputs/2026-09-28-22-30-continue-remnants-investigation.md`
  (aşağıda "rapor"). Continue/RetryFailed `a2ff12e`'de kaldırıldı; makinenin bir kısmı ölü, bir kısmı başka amaçla canlı.
- **Kullanıcı kararları (2026-09-28):** Continue kalıntıları "nasıl olması gerekiyorsa" temizlenir; §13.3 koda göre
  düzeltilir; acceptance testleri koşulur (OSYS'te çalışma yok).
- **İlke:** davranış DEĞİŞMEZ — ölü kod silinir, yeniden amaçlanmış kod adını/dokümanını gerçeğe göre alır, yanlış
  yorum düzeltilir. Rapor §4: `OnBuildPreview` guard'ının koşulu üretimde oluşamaz; gizli bug yok.

## Özet

| Task | Ne | Test |
|---|---|---|
| 0 | ARCHITECTURE §13.3 kök değişimi paragrafı + `ApplyRepositoryRoot` iç yorumu koda göre | — |
| 1 | Supervisor ölü alanları (StoppedFailedIds/torn-DLL guard, `_plan`, `_root`/`SameRootLocked`/`Canonical`, `_depIssuesById` alanı) | değişmez |
| 2 | `RunSnapshot` kalkar: tohum sözlüğü + `UnfinishedCount` | 2 yeni birim pini (mutasyonla ayırt edici) |
| 3 | `RunClock.accumulatedMs` kalkar, doc gerçeğe göre | RunClockTests :30 ve :41 yeniden yazılır |
| 4 | `BeginRunAsync.clearBuffers` + `OnBuildPreview` guard'ı kalkar | RVMT :1583 ve :2141 [DEĞİŞEN KURAL] ile yeniden yazılır |
| 5 | `RunStartedEvent.ElapsedMsAtStart` zinciri kalkar (kontrat + motor + App) | ~177 test çağrısı mekanik; ölü değeri pinleyen testler |
| 6 | Kalan yanlış yorumlar + ARCHITECTURE D1-D5 | — |
| 7 | Ad hijyeni: `RunSegmentAsync`, `CanRebuildOrRetry`, "continue/segment" geçen test adları ve yorumları | yeniden adlandırma |

Kapsam dışı (rapor §8, ürün kararı ister, dokunulmaz, kullanıcıya raporlanır): `ProjectSkippedEvent.CycleUnconverged` ve
`SkipReasons.CycleNonConvergent` (yakınsamama pre-skip'inin kalıntısı, App okuyor); tohum hesabının Supervisor'dan Core'a
taşınması (katman refactor'u); `QueuedProjectIds`/`InFlight` yalnız testlerden okunuyor.

---

## Global Constraints

- Repo `D:\Projects\Other\Apps\app_build_orchestrator`, branch `chore/continue-remnants-and-root-doc`, yerinde (worktree yok).
- Derleme/test **Release**: `dotnet build BuildOrchestrator.slnx -c Release` ·
  `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "FullyQualifiedName~<Sınıf>"`.
  Tam süiti koşma (controller koşar).
- Kod/UI/log İngilizce; yorumlar ve test doc'ları Türkçe. README/ARCHITECTURE İngilizce, anlatı üslubu (changelog yok).
- Dosya düzenleme yalnız Edit/Write; toplu mekanik düzenleme gerekiyorsa Write ile yazılmış, bayt koruyan bir Python
  betiği (dosyadan çalıştırılır). **YASAK:** `sed -i`, PowerShell `Get-Content|Set-Content`, heredoc'la kod. CRLF korunur.
- Uyarı susturma YASAK; build 0 uyarı kalır. Kopya YASAK. `[DEĞİŞEN KURAL]` tarihçe notlarına dokunulmaz.
- `CycleRoundDecision.Continue`, `IdleResume`, `Ds.Segment`, "Debug | Release segment" vb. İLGİSİZ eşleşmelerdir (rapor §1.5) — dokunma.
- Davranış değişmez: bir testin assert'i değişiyorsa bu yalnız ölü bir yapıyı pinlediği içindir ve doc'una eski iddia +
  gerekçe yazılır (CLAUDE.md). Yeni pin eklenirse bir kez mutasyonla KIRMIZI gösterilir, mutasyon geri alınır.
- Task başına commit, ASCII Türkçe mesaj, `git commit -F`, attribution satırı YOK; yalnız task'ın dosyaları eklenir.
- Subagent dispatch etmezsin.

---

### Task 0: ARCHITECTURE §13.3 kök değişimi + ApplyRepositoryRoot yorumu (kullanıcı: "koda göre düzelt")

Kod: kök sonradan değişince not yazılır, satırlar hollow'a döner (`ResetRowsToHollow`), son Sync HEAD'i unutulur
(`ForgetLastSync`), plan yüzeyi boşalır (`SyncAfterRootChangeAsync` → `ClearPlanSurface`) ve Save'in tek Sync'i yeni kökte gider.

1. `ARCHITECTURE.md` (§13.3, "A root that changes *later*…" paragrafı) şununla değişir:
   ```
   A root that changes *later* announces itself in the console — `Repository root → D:\src\osys — Sync
   required` — and the old repository's state goes with it: the rows fall back to hollow, the plan surface empties
   (§13.2) and the last Sync's HEAD is forgotten, so the first trigger on the new root is not compared with the old
   branch. Save's one Sync then runs on the new root. The first setup stays silent, because a Sync starts there
   anyway and the note would be noise.
   ```
2. `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs` `ApplyRepositoryRoot` gövdesindeki yorum:
   ```
   // [design v1.8.0 §2.9] Kök SONRADAN değiştiğinde konsola dim bir not düşer; eski reponun durumu da gider:
   // satırlar hollow'a döner, son Sync HEAD'i unutulur ve Save'in TEK Sync'i yeni kökte başlar (plan
   // yüzeyini SyncAfterRootChangeAsync boşaltır). (İlk kurulumda — Empty'den çıkarken — not YAZILMAZ: orada
   // zaten otomatik bir Sync akışı başlar ve not gürültü olurdu.)
   ```
Doğrulama: build 0 uyarı. Commit: `docs: kok degisimi anlatimi koda gore duzeltildi (satirlar hollow, plan bosalir, Save Sync'i)`

### Task 1: Supervisor ölü koşu-durumu alanları (rapor T1: S-4…S-7)

Rapor §6 T1'deki silme listesini uygula: `_stoppedFailedIds` + yerel + `RunContext.StoppedFailedIds` + `MarkStoppedFailed`
+ `TryRemove` + çağrı; `_plan`; `_root` + `SameRootLocked` + `Canonical`; `_depIssuesById` ALANI (yerel `depIssuesById` ve
`RunContext.DepIssuesById` KALIR); finally'deki temizlik `lock (_gate) _logs = null;` olur. `_logs?.Dispose()` (RC:743)
KALIR. Yorumlar: rapor §2 #13 ve #17 "Doğru anlam"a göre yeniden yazılır; #12, #14, #22, #24, #26 kodla gider.
Doğrulama: `git grep -n "StoppedFailed\|SameRootLocked\|_depIssuesById\|\b_plan\b\|\b_root\b" -- src` → sıfır; build 0 uyarı;
`--filter "FullyQualifiedName~RunCoordinator|FullyQualifiedName~CycleRounds|FullyQualifiedName~SingleProjectRun|FullyQualifiedName~FullCleanRun"` yeşil.
Commit: `refactor(engine): Continue'dan kalan olu kosu durumu alanlari kaldirildi (torn-DLL izi, plan/kok alanlari)`

### Task 2: `RunSnapshot` kalkar — tohum sözlüğü ve `UnfinishedCount` (rapor T2: K-1…K-4, S-2)

- `ReadySetScheduler(BuildPlan, RunSnapshot, CycleGroups?)` → `ReadySetScheduler(BuildPlan plan, IReadOnlyDictionary<string, BuildResult> seed, CycleGroups? cycleGroups = null)`;
  tohumsuz ctor boş bir `static readonly` sözlüğe (OrdinalIgnoreCase) devreder. Doc: koşu başı pre-skip tohumu (Build'de
  "up to date", Cycles'ta kapsam dışı/güncel upstream/güncel SCC); tohumdakiler dispatch edilmez ve bağımlıları için
  çözülmüş sayılır; gruplar null iken tohumda olmayan döngü üyesi "in dependency cycle" ile pre-skip edilir — bu NORMAL
  yoldur (Build tohumu SCC üyelerini taşımaz), savunmacı değil.
- `RunSnapshot TakeSnapshot(long)` → `public int UnfinishedCount` (lock altında, `_completed`'ta olmayan düğüm sayısı,
  **in-flight DAHİL**). `QueuedProjectIds` KULLANILMAZ (in-flight'ı dışlar).
- `RunSnapshot.cs` silinir. RunCoordinator: `schedulerSeed` → `Dictionary<string, BuildResult>?`, `new ReadySetScheduler(runPlan.Plan, schedulerSeed, groups)`;
  bitişte `int unfinished = scheduler.UnfinishedCount;`, `snapshotAtEnd.Queued.Count` → `unfinished`.
- Yorumlar: rapor §2 #1-4, #16, #20, #23 + RC:1037, 1043-1045 ("snapshot" → "kalan sayısı").
- **Yeni pinler** (`tests/BuildOrchestrator.Tests/Scheduling/ReadySetSchedulerTests.cs`): (1) tohumlu ctor — tohumdaki id
  dispatch edilmez ve bağımlısı ready olur; tohumda olmayan InCycle düğüm gruplar null iken `InDependencyCycle` ile
  PreSkipped'e düşer. (2) `UnfinishedCount` in-flight'ı sayar (bir düğüm dispatch edilmiş ama bitmemişken sayıma dahil).
  Her pin bir kez mutasyonla KIRMIZI gösterilir (ör. `UnfinishedCount`'ı in-flight'ı dışlayacak şekilde bozmak), geri alınır.
- Doğrulama: `git grep -n "RunSnapshot\|TakeSnapshot" -- src tests` → sıfır; build 0 uyarı;
  `--filter "FullyQualifiedName~ReadySetScheduler|FullyQualifiedName~RunCoordinator|FullyQualifiedName~CycleRounds|FullyQualifiedName~SingleProjectRun|FullyQualifiedName~FullCleanRun"` yeşil.
Commit: `refactor(core): RunSnapshot kalkti — kosu basi tohum sozlugu ve UnfinishedCount (in-flight dahil)`

### Task 3: `RunClock` — `accumulatedMs` kalkar (rapor T3 Öneri A)

- Ctor'daki `accumulatedMs` parametresi silinir (iç Pause→Start birikimi KALIR). Sınıf doc'u rapor §2 #5 "Doğru anlam"a
  göre yazılır: koşu süre saati; `runStarted`'dan hemen önce Start, her koşunun finally'sinde Pause; değer dondurulur ki
  `runCompleted.DurationMs` ile decision.log aynı süreyi yazsın.
- Testler: `RunClockTests` :30 ve :41 tohumsuz sürüme yeniden yazılır (Start'tan önce 0; koşu sırasında yalnız geçen süre);
  doc'larına eski iddia ("saat önceki segmentin süresiyle tohumlanır") + gerekçe (tohum üreticisi `a2ff12e` ile kalktı);
  sınıf yorumu güncellenir; :10 testi kalır.
- Doğrulama: `git grep -n accumulatedMs` → sıfır; `--filter "FullyQualifiedName~RunClock|FullyQualifiedName~RunCoordinator"` yeşil.
Commit: `refactor(core): RunClock'tan Continue tohumu kalkti, dokuman kosu suresi saatine gore yazildi`

### Task 4: App — `clearBuffers` ve `OnBuildPreview` guard'ı (rapor T4 + T5)

- `BeginRunAsync(RunMode mode, bool clearBuffers, …)` → `BeginRunAsync(RunMode mode, string? scopeProjectId = null)`;
  iki `if (clearBuffers)` koşulsuz olur; tüm çağıranlar sadeleşir; rapor §2 #43 yorumları.
- `OnBuildPreview`: `if (RunActive && row.State is Succeeded or Failed or Skipped) continue;` satırı ve gerekçe yorumu silinir;
  rapor §2 #35-38 yorumları yeniden yazılır (önizleme koşu başına bir kez, runStarted'ın ardından ve ilk proje olayından
  önce gelir; satırlar tıklamada `NeutralizeRows` ile nötrlenir). InRunQueue'nun `RunActive` kapısı KALIR.
- Testler (CLAUDE.md: sessizce silinmez): `RunViewModelTests` `A_later_segments_preview_refreshes_the_current_sha_of_an_already_terminal_row`
  ve `BuildPreviewEvent_on_a_Continue_segment_does_not_clobber_an_already_clean_row` rapor §6 T5'teki (a) ve (b)
  değişmezlerini pinleyecek şekilde yeniden yazılır ve yeniden adlandırılır; doc'larına [DEĞİŞEN KURAL] + eski iddia +
  gerekçe (Continue `a2ff12e`'de kalktı; motor koşu başına tek önizleme yayar; BeginRunAsync satırları nötrler); ikisinin de
  kaldırma öncesinde ve sonrasında yeşil olduğu (değişmez pini, kusur fix'i değil) doc'ta belirtilir.
- Doğrulama: `--filter "FullyQualifiedName~RunViewModelTests|FullyQualifiedName~RunViewModelStateTests|FullyQualifiedName~Choreography|FullyQualifiedName~CleanAllCommandTests"` yeşil; build 0 uyarı.
Commit: `refactor(app): clearBuffers parametresi ve erisilemeyen segment guard'i kalkti`

### Task 5: `RunStartedEvent.ElapsedMsAtStart` zinciri kalkar (rapor T6)

- Kontrat: `RunStartedEvent`'ten `long ElapsedMsAtStart` parametresi ve doc'u silinir (sonraki opsiyonel parametreler
  aynen). JSON bilinmeyen alanı yok saydığı için eski NDJSON satırları çözülmeye devam eder (IpcJson.Options'ta
  `UnmappedMemberHandling` yok) — bunu bir IPC testiyle pinle (eski `elapsedMsAtStart` alanlı satır çözülür).
- Motor: `elapsedAtStart` yereli, `RunStartedEvent` argümanı ve decision.log'daki `elapsedAtStart={…}ms` alanı silinir
  (format indeksleri kayar — dikkat).
- App: `_elapsedBaseMs` kalkar; geçen süre yalnız koşunun kendi saatinden.
- Testler: `new RunStartedEvent(…)` çağrılarında 6. konumsal argüman (`0`) çıkarılır (~177 çağrı / 34 dosya; bayt koruyan
  betik + derleyici kanıtı). Ölü değeri pinleyenler: `RunStarted_sets_ElapsedMs_from_ElapsedMsAtStart` SİLİNMEZ, yeni
  kuralı pinleyecek şekilde yeniden yazılır ve adlandırılır (ör. `RunStarted_starts_the_elapsed_clock_from_zero`; doc'ta
  [DEĞİŞEN KURAL]: eski iddia "geçen süre motorun gönderdiği tabandan devam eder", gerekçe: taban yalnız Continue'da sıfırdan
  farklıydı, `a2ff12e`'den beri hep 0, alan kalktı); `TickElapsed_uses_the_injected_clock_deterministically` tabanı 0'a;
  `RunCoordinatorTests` `Assert.Equal(0, started.ElapsedMsAtStart)` düşer; `IpcMessagesTests` :69/:766/:771 yeni şekle.
- Doğrulama: `git grep -n "ElapsedMsAtStart\|elapsedAtStart\|_elapsedBaseMs" -- src tests` → sıfır (yeni geriye-uyum testi hariç);
  build 0 uyarı; `--filter "FullyQualifiedName~Ipc|FullyQualifiedName~RunCoordinator|FullyQualifiedName~RunViewModel|FullyQualifiedName~EventStream|FullyQualifiedName~ActionBar"` yeşil.
Commit: `refactor(contracts): hep 0 olan ElapsedMsAtStart alani kontrattan, motordan ve App'ten kalkti`

### Task 6: Kalan yorumlar + ARCHITECTURE D1-D5

- Rapor §2'nin T1-T5'e girmeyen maddeleri: #6-11, #15, #18, #19, #21, #25, #27-34, #39-42 ("Doğru anlam" sütununa göre).
  Özellikle #19 (RC:903-906) ve #32 (RVM:1446-1450) içerik hatası taşır.
- Rapor §8'den iki yorum doğruluğu: RVS:126 bayat satır atfı ("RunCoordinator.cs:456" → satır numarasız, metot adıyla);
  RC:1896-1898 ("hafıza grubu pre-skip eder" → RC:773-789'a göre yalnız raporlar); `SkipReasons.cs:4` sayı ("dört").
- ARCHITECTURE: rapor §5 D1-D5 önerilen metinlerle (D5: §22 satırı Task 2-3 sonrası gerçeğe göre: RunSnapshot yok;
  "Run elapsed clock | Core/Scheduling/RunClock.cs"; scheduler satırına "pre-skip seed"). Her iddia koda karşı doğrulanır.
- Doğrulama: build 0 uyarı. Commit: `docs: Continue'dan kalan yanlis yorumlar ve ARCHITECTURE anlatimlari koda gore duzeltildi`

### Task 7: Ad hijyeni (rapor T8)

- `RunCoordinator.RunSegmentAsync` → `PlanAndRunAsync` (atıflar: rapor §2 son paragraf + IPC:600, RVM:2336, RVMST:238).
- `RunViewModel.CanRebuildOrRetry` → `CanStartRunOnIdleWorkspace` (tüm `CanExecute = nameof(...)` ve atıflar).
- Rapor §3.3'teki test adları/değişkenleri/doc'ları: adı ne pinlediğini söylesin (ör. `…_offers_continue` → neyi
  doğruluyorsa; `…_Rebuild_Stop_and_Continue` → `…_Rebuild_and_Stop`; `firstSegment` → `firstRun`); `CleanCommandTests`
  :279/:396 `SyncCoreAsync(clearBuffers…)` atfı. Başka yerden (cref/yorum) atıf yapılan test adı değişirse atıf da güncellenir.
- Doğrulama: `git grep -n -i "RunSegmentAsync\|CanRebuildOrRetry"` → sıfır; kalan `continue`/`segment` geçişleri yalnız
  §1.5 ilgisizleri ve tarihçe notları; build 0 uyarı; değişen test sınıfları yeşil.
Commit: `refactor: Continue/segment adlari bugunku anlamlarina gore yeniden adlandirildi`
