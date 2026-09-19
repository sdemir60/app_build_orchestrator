# Faz 1 — Kümülatif renk modeli: uygulama planı (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-18-07-56-sync-build-flow-spec.md` §1 (kararlar 2, 3, 14-18),
§4 (durum modeli ve etiketler), §8 (kalkanlar). Kod kanıtları: `2026-09-17-19-41-cumulative-status-analysis-verification.md`.

**Hedef:** Sync sonrası satır ve node'lar çıktının durumunu gösterir (yeşil güncel · gri derlenecek · kırmızı
kanıtlı bozuk · boş bilinmiyor); hiçbir işlem listeyi sıfırlamaz; koreografiler aynen kalır. Worktree ve branch
işleri Faz 2'dir; dışarıdan derleme kredisi Faz 3'tür — bu plan onlara dokunmaz.

**Mimari:** Motor tarafında defter hata anındaki imzayı da tutar ve karar sırası "kanıtlı kırmızı" ile başlar.
App tarafında görsel durum iki katmandan türer: **standing** (önizlemenin `WillBuild` + `Reason`'ından: Unknown /
Current / Stale / Failed) ve üstüne binen **koşu** durumu (queued / building / sonuç). Nötrleme yalnız koşuya özel
alanları temizler. Üçgen defter notundan da beslenir; küp döngü üyesinde her zaman amber.

## Kullanıcı kararları (2026-09-17/18)

Spec §1'de. Bu faz için belirleyici olanlar: kümülatif renk; kırmızı = aynı imzada derleyici hatası; timeout gri
`never built`; ▲ kümülatif; küp hep amber; etiketler `up to date · 2h` · `modified` · `modified · local` ·
`affected` · `never built` · `failed · 2h`; yuva 134 px; sayaçlar durum sayar; animasyonlar değişmez.

## Global Constraints

- Proje kuralları `CLAUDE.md`: **kırmızı test kuralı** (kod değişikliğinden ÖNCE testin KIRMIZI verdiği gösterilir);
  davranış değişince eski test YENİ kuralı pinleyecek şekilde yeniden yazılır ve doc'una eski iddia + gerekçe
  (`[DEĞİŞEN KURAL — spec 2026-09-18 §…]`); eşik/bütçe gevşetmek YASAK; **kopya YASAK / tek doğruluk kaynağı**;
  yeni XAML kökü = realize testi; kod/UI metinleri İngilizce, yorumlar Türkçe.
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıksa Debug bin kilitlenir → `-c Release` ile derle/test et.
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` KULLANMA (UTF-8/CRLF bozar) — Edit/Write.
- Commit mesajı dosyaya yazılıp `git commit -F` ile atılır; Türkçe, ASCII (`feat(status): ...`). **Attribution satırı
  eklenmez** (global CLAUDE.md). Task başına commit.
- **Branch yapısı (kullanıcı kararı 2026-09-18):** çatı `sync-build-flow` (`main`'den açıldı, uzakta yedekli; spec,
  plan ve analiz kayıtları orada). Bu faz `sync-build-flow-phase1` branch'inde yürür: çatıdan açılır, uzağa push'lanır,
  task başına commit. Faz yeşil olunca `--no-ff` ile çatıya merge + push; merge doğrulanınca faz branch'i local ve
  remote'tan silinir. **`main`'e merge YOK** — çatı, kullanıcı test edip onaylayınca `main`'e birleşir. İsimlerde
  eğik çizgi değil tire (git `sync-build-flow` ile `sync-build-flow/x`'i aynı anda tutamaz). Oturum `main` üzerinde
  biter (CLAUDE.md); sonraki oturum önce çatıya ya da faz branch'ine geçer.
- Sözleşme değişiklikleri **alan SONA, default'lu**: eski NDJSON ve eski `build-state.json` alansız çözülür.
- ARCHITECTURE.md anlatı üslubu: değişen davranış ilgili bölümde yerinde yeniden yazılır; changelog yok; sayı gömme yok.
- Tasarım paketi: Task 0 tasarım README'sini günceller; implementasyon testleri gerekçe adresi olarak
  `design v1.20.0 §…` yazar. Prototip (`BuildApp.jsx`) güncellenmez (kullanıcı kararı bekler; README bağlayıcıdır).
- Sıra: **bloklayıcı yok. Önemli:** T0-T6. **Orta:** T7-T8. **Kapanış:** T9.

---

### Task 0: Tasarım sürümü v1.20.0 (README metni)

**Dosyalar:** `.claude/outputs/2026-09-10-10-23-design-v1.19.0/README.md` kopyalanarak
`.claude/outputs/2026-09-18-<HH-mm>-design-v1.20.0/README.md` (prototip klasörü kopyalanmaz; README başında
"prototip v1.19.0'dır, bu sürümde yalnız metin değişti" notu).

**Değişen bölümler (yerinde yeniden yazılır, changelog değil):**
- §2.3 "Renk kuralı (v1.11.0 — tek statü kanalı)" → "Renk kuralı (v1.20.0 — tek kanal, çıktının durumu)": görsel
  durumlar **bilinmiyor** (kesikli border, gri küp — yalnız Sync yokken) · **güncel** (yeşil) · **derlenecek** (gri) ·
  **bozuk** (kırmızı, kanıtlı) · **işaretli** (amber) · **derleniyor** (amber + beads) · **sonuç** yeşil/kırmızı
  duruma yazılır ve kalır. Döngü üyesinde küp **her zaman** amber. "Sync sonrası/açılış başlangıç modu" cümlesi
  "yalnız ilk Sync'ten önce" olur.
- §2.4 madde 4: etiket sözlüğü `modified` · `modified · local` · `affected` · `never built` · `failed · 2h` ·
  `up to date · 2h`; yuva **134 px**; "`failed · retry`" ve üç parçalı etiket kalkar. Madde 6: üçgen "defterdeki
  bağımlılık notu durdukça durur; bir sonraki koşu silmez". Madde 2: nokta başlangıç modunda halka — yalnız Sync yokken.
- §2.7 (action bar): chip'ler Σ · building · ✓ up to date · ○ to build · ✗ failed · ⚠; `—` chip'i kalkar; "✓ + ✗ =
  bu koşuda derlenenler" cümlesi "durum filtreleri" olur.
- §3.1 fazlar: "Sync sonrası satırlar başlangıç modunda" → "Sync sonrası satırlar durum renginde".
- §5 statü tablosu: `başlangıç modu` satırı "yalnız Sync yokken"; `will-build` satırı "plan bilgisi renkte ve
  etikette"; yeni satır `bozuk (kanıtlı)`.
- §8: "v1.11.0: renk YALNIZ son işlemin hikâyesini anlatır" maddesi → "v1.20.0: renk ÇIKTININ DURUMUNU anlatır; tek
  kanal ilkesi korunur; ayrı will-build noktası ve turuncu döngü işareti GERİ GELMEZ".
- §9: `## v1.20.0 — 2026-09-18` sürüm notu (neden: Sync sonrası neyin güncel olduğu renkten okunmuyordu; toplam
  görünmüyordu — kullanıcı ölçümü).

Test yok (doküman). Commit: `docs(design): v1.20.0 — renk cikti durumunu anlatir`.

### Task 1: Defterde hata imzası ve yeni karar sırası (Core)

**Mevcut kod:** `Contracts/Model/ProjectModels.cs:124-195` (`BuildState` record; `NonConvergentSignature` deseni
`:140`), `Core/Planning/WillBuildEvaluator.cs:72-91` (karar sırası `:79-88`), `Core/State/BuildStateStore.cs:99-107`
(`LastBuiltAtOf`). Testler: `tests/.../Planning/WillBuildTests.cs`, `tests/.../State/BuildStateStoreTests.cs`.

**Kural:**
- `BuildState`'e SONA, default'lu iki alan: `string? FailedSignature = null` (bu projenin kendi MSBuild çağrısı
  exit ≠ 0 ile bittiği andaki bileşik imza; başarıda `null`'a döner), `DateTimeOffset? FailedAt = null` (o hatanın
  zamanı; `failed · 2h` kuyruğu). `Equals`/`GetHashCode` genişler.
- `WillBuildEvaluator.EvaluateWithReason` sırası: (1) `FailedSignature` dolu ve `== currentSignature` →
  `LastFailed`; (2) `BuiltSignature is null` ya da (`LastResult != Succeeded` ve `FailedSignature` boş — kesilmiş
  deneme) → `NeverBuilt`; (3) imza farklı → `SignatureChanged`; (4) not → `WaitingForDependency` / `DepIssue`;
  (5) `UpToDate`. `LastFailed` artık yalnız kanıtlıyken üretilir.
- `BuildStateStore.FailedAtOf(state, id)` → `FailedSignature` doluysa `FailedAt`, değilse `null` (`LastBuiltAtOf`
  deseni, tek arama yeri).

**Testler (önce KIRMIZI):** `WillBuildTests`: `Failed_at_the_current_signature_reads_LastFailed`,
`Failed_at_another_signature_falls_through_to_the_signature_rule` (BuiltSignature = current, FailedSignature ≠ current
→ `UpToDate`; BuiltSignature null → `NeverBuilt`), `An_interrupted_attempt_without_evidence_reads_NeverBuilt`
(`LastResult=Failed`, `FailedSignature=null`, BuiltSignature = current → `NeverBuilt`, WillBuild true),
`true_when_last_result_failed_even_if_signature_matches` **yeniden yazılır** (`[DEĞİŞEN KURAL — spec §1-14]`: eski
iddia "LastResult=Failed ⇒ LastFailed"; yeni: kanıtsız hata `NeverBuilt`). `BuildStateStoreTests`:
`A_record_written_before_the_failed_signature_existed_loads_with_it_empty` (elle JSON, alan yok → null),
`FailedAtOf_answers_only_while_the_failure_is_evidence`.

Doküman: ARCHITECTURE §7.4 ("The evaluator also returns why…" paragrafı: kanıtlı kırmızı, kesilmiş deneme) ve §7.5
(defter alanları: hata imzası ve zamanı). Commit: `feat(state): defter hata imzasini tutar, karar kanitli kirmiziyla baslar`.

### Task 2: Hata yazımı nedene göre (Supervisor)

**Mevcut kod:** `Supervisor/RunCoordinator.cs:1256-1308` (`ReportProjectResult`; `:1306` çağrı), `:1881-1891`
(`InvalidateBuildStateOnFailure` — kayıt yoksa açmaz), `:1893-1904` (`ReasonFor`: `stopped` / `timeout` / `exit N`),
`:1614-1617` (kayıt yoksa taze kayıt açma deseni), `:1815-1837` (`PersistBuildStateOnSuccess`, imza kaynağı
`inc.SignatureById`). Testler: `tests/.../Supervisor/RunCoordinatorTests.cs:1353` (`A_failed_project_is_invalidated…`).

**Kural:**
- `InvalidateBuildStateOnFailure(run, projectId, reason, trustedResult)`: kanıt ⇔ `trustedResult && reason`
  `"exit "` ile başlıyor (derleyici/MSBuild sıfır-dışı çıktı). Kanıtlıysa `FailedSignature = inc.SignatureById[id]`,
  `FailedAt = now`, `LastResult = Failed`; kayıt yoksa `new BuildState(id, BuiltSignature: null, …)` ile açılır.
  Kanıtsızsa (timeout, stopped, invoke error, yakınsamayan grubun yeşil üyesi) bugünkü davranış: yalnız
  `LastResult=Failed, LastRunAt=now`, `FailedSignature` **null'a çekilir** (eski kanıt düşer: çıktı artık güvenilmez
  ama kaynağın bozuk olduğu kanıtlı değil), kayıt yoksa açılmaz.
- `PersistBuildStateOnSuccess` taze kayıt kurduğu için `FailedSignature`/`FailedAt` doğal olarak null'a döner (yeni
  alanlar default) — testle pinlenir.
- Sebep sınıflandırması `ReasonFor`'un yanına tek yerde: `static bool IsCompilerFailure(string reason)`.

**Testler (önce KIRMIZI):** `RunCoordinatorTests`: `A_compiler_failure_records_the_failed_signature_and_time`,
`A_compiler_failure_opens_a_record_for_a_never_built_project`, `A_timeout_records_no_failed_signature`
(mevcut timeout fixture'ı; `LastResult=Failed`, `FailedSignature=null`),
`A_green_member_of_an_unconverged_group_records_no_failed_signature`, `A_success_clears_the_failed_signature`.
`A_failed_project_is_invalidated…` (:1353) doc'una yeni alan iddiası eklenir.

Doküman: ARCHITECTURE §8.8 "Per project … on failure the stored state is invalidated" cümlesi yerinde yeniden
yazılır. Commit: `feat(engine): derleyici hatasi imzasiyla yazilir, kesilme kanit degildir`.

### Task 3: Önizleme hata zamanını ve local işaretini taşır (Contracts + Core Sync)

**Mevcut kod:** `Contracts/Ipc/IpcMessages.cs:467-501` (`BuildPreviewItem`, `Equals`/`GetHashCode`),
`Core/Workspace/SyncWorkspaceService.cs:169-179` (önizleme projeksiyonu), `:106` (`activeBranch`),
`Core/Git/GitService.cs:120-121` (`GetDirtyPathsAsync`), `Core/Incremental/IncrementalRunBinder.cs:120-122`
(`InputsOf`). Supervisor tarafı: `RunCoordinator` koşu önizlemesi (`BuildPreviewItem` üreten yer — `grep
"new BuildPreviewItem" src/BuildOrchestrator.Supervisor`). Testler: `tests/.../Workspace/SyncWorkspaceServiceTests.cs`,
`tests/.../Supervisor/RunCoordinatorTests.cs:1265`.

**Kural:**
- `BuildPreviewItem`'a SONA: `DateTimeOffset? FailedAt = null`, `bool LocalEdits = false`. `Equals`/`GetHashCode`.
- Sync: `FailedAt = BuildStateStore.FailedAtOf(state, id)`; `LocalEdits` = `GetDirtyPathsAsync` sonucundaki bir yol
  projenin girdi kümesindeyse (`binder.InputsOf(id)` mantıksal yolları; kök-göreli karşılaştırma, `OrdinalIgnoreCase`).
  Git sorgusu başarısızsa (repo yok, hata) `false`. Koşu önizlemesi (Supervisor) `FailedAt`'ı aynı yardımcıdan alır,
  `LocalEdits`'i taşımaz (`false`; etiket Sync'ten gelen değeri korur — satırda terminal-guard'lı alanlar gibi).
- Hesap tek yerde: `Core/Workspace/LocalEdits.cs` (`static IReadOnlySet<string> ProjectsWithLocalEdits(dirtyPaths,
  inputsById, root)`), Sync çağırır.

**Testler (önce KIRMIZI):** `SyncWorkspaceServiceTests`: `The_preview_carries_the_failure_time_from_the_ledger`,
`A_dirty_file_marks_only_the_project_that_owns_it` (sahte git: dirty path `A\x.cs` → A `LocalEdits`, B değil),
`A_failing_dirty_query_marks_nothing`. `LocalEditsTests` (saf): klasör dışı dosya → yok; csproj'un kendisi → var.
`RunCoordinatorTests`: koşu önizlemesi `FailedAt` taşır.

Doküman: ARCHITECTURE §5.3 (event alanları) ve §10.2 (Sync'in `status --porcelain` okuması, salt-okur).
Commit: `feat(sync): onizleme hata zamanini ve local duzenleme isaretini tasir`.

### Task 4: Görsel durum = standing + koşu (App çekirdeği)

**Mevcut kod:** `App/Controls/VisualStatus.cs` (enum `:14-38`, `For` `:53-67`, fırça tabloları `:73-117`,
`IsStartMode` `:127`, `NameIsEmphasised` `:131`), `App/Controls/GraphStatus.cs`, `App/ViewModels/GraphBinder.cs:49-51`,
`App/Controls/StatusGlyph*.cs` (`GraphStatus` DP `:43`, tablolar `:84-106`). Testler: `CycleCubeTests`,
`GraphBinderTests:166`, `GraphWillBuildFeedTests`, `GraphSkippedProjectTests`, `GraphRunLifecycleTests`,
`ProjectRowTests`, `StartModeContinuousTests`.

**Kural:**
- Yeni saf tip `App/Controls/StandingStatus.cs`: `enum StandingStatus { Unknown, Current, Stale, Failed }` +
  `static class StandingStatuses { static StandingStatus From(bool? willBuild, WillBuildReason? reason) }`:
  `willBuild is null || reason is null` → Unknown; `LastFailed` → Failed; `UpToDate`, `WaitingForDependency` →
  Current; `NeverBuilt`, `SignatureChanged`, `DepIssue` → Stale. (`WaitingForDependency` yeşildir; "bekliyor"
  bilgisi üçgende.)
- `VisualStatus` enum: `Fresh` → `Unknown`; `Discovered` **kalkar**; yeni `Current`, `Stale`, `StaleFailed`
  (kanıtlı kırmızı, koşu sonucu `Failed`'dan ayrı tutulmaz — aynı fırçalar; tek üye `Failed` yeter). Sonuç: `Unknown`,
  `Current`, `Stale`, `Failed`, `Marked`, `Queued`, `Building`, `Succeeded` (= Current ile aynı fırça; koşu içinde
  "az önce derlendi" vurgusu için ayrı kalır), `Cycle` ve `CycleSkipped` **kalkar** (küp artık `inCycle`'dan bağımsız
  bir parametreyle boyanır).
- `VisualStatuses.For(GraphStatus status, StandingStatus standing, bool marked)`: `Queued`/`Building` →
  kendileri; `Succeeded` → `Succeeded`; `Failed` → `Failed`; **`Skipped` → standing'e düşer** (atlanmak bir renk
  değil); `Discovered` → `marked ? Marked : standing'in görseli` (Unknown → `Unknown`, Current → `Current`, Stale →
  `Stale`, Failed → `Failed`).
- Fırça tabloları: `Current`/`Succeeded` → success; `Failed` → fail; `Stale` → bugünkü nötr gri
  (`Brush.StatusSkippedBorder` / `Brush.BorderStrong` / `Brush.SurfaceRaised` / `Brush.TextFaint`); `Unknown` →
  aynı gri (başlangıç modu çizimi `IsStartMode` ile). `NodeCoreBrushKey(VisualStatus state, bool inCycle)`: `inCycle`
  → `Brush.AmberText` **her durumda**; değilse bugünkü tablo. `IsStartMode` ⇔ `Unknown`. `NameIsEmphasised` değişmez.
- `StatusGlyph`: `GraphStatus` yerine `VisualStatus` alır (glyph seçimi: `Unknown`/`Stale` → kesikli daire,
  `Current`/`Succeeded` → ✓, `Failed` → ✗, `Queued` → saat, `Building` → dönen halka; `—` glyph'i yalnız koşu içinde
  `Skipped` iken görünürdü — artık standing'e düştüğü için kalkar; `Brush.StatusSkippedText` tüketicisiz kalırsa
  token silinmez, guard'a bakılır).
- `GraphBinder.Nodes`: `VisualStatuses.For(status, r.Standing, r.Marked)` + `r.InCycle` node'a ayrıca taşınır
  (küp için).

**Testler (önce KIRMIZI):** yeni `StandingStatusTests` (tablo). `VisualStatusTests` (yeni dosya): `Skipped_falls_to_the_standing_colour`,
`Discovered_shows_the_standing_colour_unless_marked`, `Marked_wins_over_every_standing`, `Only_unknown_is_the_start_mode`,
`The_cube_is_amber_for_every_cycle_member_state` (Current/Stale/Failed/Succeeded × inCycle → AmberText).
`CycleCubeTests`: `The_start_mode_still_wins_so_sync_paints_no_amber_cube` **yeniden yazılır** →
`Sync_paints_the_amber_cube_because_membership_is_structural` (`[DEĞİŞEN KURAL — design v1.20.0 §2.3]`);
`A_member_that_is_actually_built_shows_only_its_result` → küp amber kalır, çerçeve sonuç. `GraphBinderTests:166`
default'u `Unknown`. `GraphSkippedProjectTests`: atlanan node standing renginde, opaklık kuralı aynen.

Doküman: ARCHITECTURE §13.6 "One colour channel" ve §14.3 (durum sözlüğü, başlangıç modu, üçgen paragrafı —
sonraki task'ta). Commit: `feat(ui): gorsel durum = cikti durumu + kosu bindirmesi; kup dongude hep amber`.

### Task 5: Satır VM — standing, kümülatif üçgen, nötrleme (App)

**Mevcut kod:** `App/ViewModels/RunViewModel.cs` — `ProjectRowViewModel` alanları `:150-318` (`InRunQueue` `:169`,
`DepIssues`/`HasDepIssue` `:177-182`, `Status` `:257-274`, `Fresh` `:280-282`, `Marked` `:287-289`, `VisualStatus`
`:296`), `NeutralizeRows` `:958-981`, çağrıları `:827` (tıklama), `:1698` (Rebuild runStarted),
`RunViewModel.Workspace.cs:616` (Sync), `OnRunStarted` `:1672` (`row.Fresh=false`), `OnBuildPreview` `:1719-1745`,
`OnProjectDone` `:1842-1908`, `PropagateRunActive` `:1390-1401`, `RunViewModel.ActionBar.cs:362-373`
(`ResetRowsToHollow`), `RunViewModel.Workspace.cs:588-602` (yeni satır `Fresh = !IsRunning`). Testler:
`RunViewModelTests`, `RunViewModelStateTests`, `StartModeContinuousTests`, `ChoreographyTests`, `OperationPipelineTests`.

**Kural:**
- `ProjectRowViewModel.Standing => StandingStatuses.From(WillBuild, WillBuildReason)`; `WillBuild`/`WillBuildReason`
  setter'ları `Standing` ve `VisualStatus` için bildirim yayınlar. `VisualStatus => VisualStatuses.For(Status,
  Standing, Marked)`. `Fresh` alanı **kalkar**; `IsStartMode => Standing == StandingStatus.Unknown` (satır ve
  nokta bunu okur).
- `HasDepIssue => DepIssues is { Count: > 0 } || WillBuildReason == WillBuildReason.WaitingForDependency`
  (`WillBuildReason` setter'ı `HasDepIssue` bildirimi yayınlar). `RowWarning.For` çağrısına giden `depIssues`:
  koşu listesi boşsa `DependencyRoots` (önizlemeden gelen kök adları) verilir — tooltip `Dependency issue: X` aynı
  metinle.
- `NeutralizeRows(bool clearMarks = true)`: `fresh` parametresi kalkar; `State=Pending`, `DepIssues=null`,
  `DurationMs=0`, döngü bayrakları, `SkipReason=null`, `Marked` (clearMarks). `WillBuild`/`Reason`/`LastBuiltAt`/
  `OwnFilesChanged`/`FailedAt`/`LocalEdits` **dokunulmaz**. Sync'in `:616` çağrısı yalnız koşu alanlarını temizler
  (aynı metot, `fresh` yok); `OnRunStarted`'daki `row.Fresh=false` satırı kalkar; yeni satır `Fresh` yerine
  hiçbir şey (standing önizlemeden gelir). `ResetRowsToHollow` `WillBuild=null` ile Unknown'a düşürmeye devam eder.
- Koşu bitince (`PropagateRunActive`, `active=false`): `State == Skipped` satırlar `Pending`'e döner (glyph
  standing'e; `SkipReason` durur — proje sayfası okur).
- `OnProjectDone` Failed dalı: `row.FailedAt = DateTimeOffset.Now` (kanıt taze; `failed · just now`), Succeeded dalı:
  `row.FailedAt = null`. `OnBuildPreview`: `row.FailedAt`, `row.LocalEdits` (Sync'ten; terminal-guard dışında —
  `LastBuiltAt` gibi). Koşu önizlemesi `LocalEdits=false` gönderdiği için Sync değeri **korunur**: `LocalEdits`
  yalnız `SyncCompleted` öncesi gelen önizlemeden yazılır (event kaynağı ayrımı: `_currentRunId is null`).

**Testler (önce KIRMIZI):** `RunViewModelStateTests`: `Sync_paints_every_row_from_its_reason` (UpToDate → Current,
SignatureChanged → Stale, LastFailed → Failed, null → Unknown), `A_row_build_leaves_every_other_rows_colour_untouched`
(50 Current + hedef; `BeginRunAsync(scope)` sonrası 50'si Current), `A_second_build_keeps_the_greens_of_the_first`,
`A_skipped_up_to_date_row_reads_current_with_a_tick_after_the_run`, `A_waiting_row_carries_the_triangle_after_sync`
(önizleme `WaitingForDependency` + `DependencyRoots` → `HasDepIssue`, `RowWarning` metni),
`The_next_operation_does_not_clear_a_ledger_triangle`, `Only_a_row_without_a_decision_is_in_start_mode`.
`StartModeContinuousTests.Realize(fresh)` → `Realize(standing)`; `The_stripe_is_solid…` yeniden yazılır.
`ChoreographyTests`/`OperationPipelineTests`: nötrlemenin satır rengini değiştirmediği pinlenir (dalga öncesi
Current satır Current kalır; kapsam Marked).

Doküman: ARCHITECTURE §14.3 ("The start mode" paragrafı: yalnız Sync yokken; "Dependency issues come last because
they are the most transient" → kümülatif; "One colour channel" tanımı), §14.5 "The opening … begins by
neutralising" paragrafı (yalnız koşu alanları), §13.2 "The slot is not a result column" ve satır anatomisi.
Commit: `feat(ui): satir rengi kumulatif; ucgen defter notundan; notrleme yalniz kosu alanlarini siler`.

### Task 6: Etiketler ve yuva (App)

**Mevcut kod:** `App/ViewModels/DecisionLabel.cs:79-138`, `App/Views/ProjectRow.xaml:71-90` (`MinWidth="204"`),
`App/Views/ProjectRow.xaml.cs:480-491` (`ApplyDecision`), `App/Console/ConsoleEmptyState.cs:108-121`. Testler:
`DecisionLabelTests`, `ProjectRowTests`, `ConsoleEmptyStateTests` (varsa; `grep -rl ConsoleEmptyState tests`).

**Kural (`DecisionLabel.For` imzası):** `For(bool? willBuild, WillBuildReason? reason, bool? ownFilesChanged,
DateTimeOffset? lastBuiltAt, DateTimeOffset? failedAt, bool localEdits, DateTimeOffset now, bool inCycle = false,
IReadOnlyList<string>? dependencyRoots = null, string namePrefix = "")` — `conditional` parametresi kalkar.
- `NeverBuilt` → `("never built", null, "No build output known to this tool", Stale: true)`.
- `LastFailed` → `("failed", AgeFormat.Age(failedAt, now), inCycle ? "Failed at this source — Resolve cycles will
  retry it" : "Failed at this source — Build will retry it", Stale: true)`; yaş varsa tooltip "Failed at this source
  2h ago — …".
- `UpToDate` ve `WaitingForDependency` → `("up to date", age, "Up to date — last built 2h ago", Stale: false)`;
  bekleyen için tooltip sonuna " · ▲ " eklenmez — üçgenin kendi tooltip'i konuşur (kopya YASAK).
- `SignatureChanged`/`DepIssue` → `ownFilesChanged == true ? ("modified", localEdits ? "local" : null, localEdits ?
  "Its own files changed since the last build — includes uncommitted edits" : "Its own files changed since the last
  build") : ("affected", null, "Its own files are unchanged — a dependency changed")`.
- `ProjectRow.xaml` `MinWidth` 204 → **134** (yorum yeniden yazılır: en uzun etiket `up to date · just now`).
- `ConsoleEmptyState.Pending`: `LastFailed` → `"{head} — it failed at this source."`; `WaitingForDependency` dalı
  `conditional` bayrağına bakmadan `DecisionLabel` tooltip'i yerine `RowWarning`'in dep-issue cümlesini kullanır
  (tek kaynak: `RowWarning.DepIssuePrefix` + kökler + " — rebuilds once it is healthy again").

**Testler (önce KIRMIZI):** `DecisionLabelTests` yeniden yazılır: `A_failure_at_this_source_reads_failed_with_its_age`,
`A_cycle_member_failure_names_resolve_cycles_in_the_tooltip`, `Retry_is_never_promised_in_the_label` (hiçbir
`Tail == "retry"`), `A_waiting_project_reads_plain_up_to_date` (`[DEĞİŞEN KURAL — design v1.20.0 §2.4]`, eski üçlü),
`Uncommitted_edits_add_the_local_tail`, `Never_built_names_the_tool_not_the_disk`. `ProjectRowTests`:
`The_decision_slot_is_134px_wide` (204 pinini yeniden yazar). `AgeFormat` değişmez.

Doküman: ARCHITECTURE §13.2 etiket tablosu ve "The word is a fact…" paragrafı; README "Reading the list" ve Sync
adımındaki "Sync colours nothing" paragrafı. Commit: `feat(ui): etiketler bes sozcuk; failed yasini yazar; yuva 134px`.

### Task 7: Sayaçlar ve filtreler (App)

**Mevcut kod:** `App/ViewModels/RunCounters.cs:31-58`, `App/ViewModels/ProjectFilter.cs:14-70`, action bar chip'leri
(`App/Views/ActionBar.xaml` — `grep -n "Skipped\|Succeeded\|Failed" src/BuildOrchestrator.App/Views/ActionBar.xaml*`),
`RunViewModel.Stream.cs` / ribbon `RibbonText` (koşu tablosu — **değişmez**). Testler: `RunCountersTests`,
`ProjectFilterTests` (varsa), `ActionBarTests`, `AccessibilityTests`.

**Kural:**
- `RunCounters`: `Succeeded`/`Failed`/`Skipped` kovaları koşu tablosu için **kalır** (`RibbonText` okur); yeni
  kovalar `Current`, `Stale`, `Broken` (standing'den: `Current` = Standing Current ∪ State Succeeded; `Stale` =
  Standing Stale; `Broken` = Standing Failed ∪ State Failed). `Building`/`Queued` aynen.
- Chip'ler: Σ · building · ✓ (`Current`) · ○ (`Stale`) · ✗ (`Broken`) · ⚠. `—` chip'i kalkar (XAML + AccessibilityNames).
- `ProjectFilter`: anahtarlar `building` (yalnız `State == Started`), `current`, `stale`, `failed`, `warn`;
  `succeeded`/`skipped` kalkar; `Label`: "Building" · "Up to date" · "To build" · "Failed" · "Warnings"; `Order`
  buna göre; `ChipBrushKey` (varsa) standing renkleri.

**Testler (önce KIRMIZI):** `RunCountersTests`: `Current_counts_standing_current_and_this_runs_successes`,
`Broken_counts_evidence_and_this_runs_failures`, `Building_never_counts_a_pending_row`. `ProjectFilterTests`:
`Building_matches_only_started_rows` (`[DEĞİŞEN KURAL — design v1.20.0 §2.7]`: eski "queued dahil"),
`Current_plus_failed_no_longer_means_what_this_run_built`. Action bar realize: beş sabit chip + ⚠; `—` yok.

Doküman: ARCHITECTURE §13.2 "Action bar" (chip listesi ve "chips combine" paragrafı); README "Reading the list"
chip cümlesi. Commit: `feat(ui): sayaclar durumu sayar; atlandi chipi kalkar`.

### Task 8: Proje sayfası ve konsol metinleri (App)

**Mevcut kod:** `App/Console/ConsoleEmptyState.cs:60-130` (Task 6'da `LastFailed` dokunuldu; burada `Skipped` dalı
ve `Pending` başlığı), `RunViewModel.Stream.cs` kapanış satırı (değişmez). Testler: `ConsoleEmptyStateTests`.

**Kural:** koşu bitince atlanan satır `Pending`'e döndüğü için proje sayfası `SkipReason` üzerinden yine
"Up to date — nothing to compile in this run." der (`Pending` dalı `SkipReason` dolu ise önce ona bakar —
`OutOfCycleScope` deseni genişletilir). `Queued`/`Will build` başlıkları değişmez.

**Testler (önce KIRMIZI):** `ConsoleEmptyStateTests`: `A_row_skipped_as_up_to_date_keeps_its_reason_after_the_run`.
Commit: `feat(ui): proje sayfasi kosu sonrasi atlama gerekcesini korur`.

### Task 9: Kapanış — doküman süpürmesi, guard'lar, tam süit

- `git grep -n "Sync colours nothing\|colour tells the story of the last operation\|failed · retry\|affected · up to
  date\|Discovered\b\|start mode"` ARCHITECTURE.md README.md → her biri yerinde yeniden yazılır (§7.4, §7.5, §8.8,
  §13.2, §13.6, §14.3, §14.5, §17.2 guard tablosu, §22 kod haritası: `StandingStatus.cs`, `LocalEdits.cs`).
- Kaynak guard'ları: `VisualStatus.Fresh`/`Discovered`/`Cycle`/`CycleSkipped` referansı kalmadı (`grep -rn`); motion
  ve token guard'ları yeşil.
- Tam süit yeşil: `dotnet test … --filter "Category!=Acceptance"`; ardından `--filter "Category=Acceptance"` (üç test,
  gerçek OSYS; uygulama kapalıyken).
- Gerçek uygulamada göz kontrolü (`dotnet run --project src/BuildOrchestrator.App/…`): Sync sonrası renkler; Build
  sonrası toplam; satırdan Build'de diğer satırlar sabit; küp amber; 134 px yuva; chip'ler.
- Merge: `sync-build-flow-phase1` → çatı `sync-build-flow` (`--no-ff`), push; merge doğrulanınca faz branch'i local
  ve remote'tan silinir. `main`'e merge YOK — kullanıcı çatıyı test eder; kullanıcıya çatıda neyi deneyeceğinin kısa
  listesi verilir. Oturum `main`'de biter.

Commit: `docs: kumulatif renk modeli dokumanlara islendi`.

---

## Faz 2 ve Faz 3 — görev listesi (kendi dökümleri sırası gelince)

**Faz 2 — Tek ağaç ve branch (Core + Supervisor + App):** (a) worktree kodu silinir: `WorktreeManager` havuz/LRU/
liste/sil, `Program.PrepareAsync` üç durum matrisi, `StartRunCommand.UseWorktree/WorktreeName`, `ListWorktrees`/
`DeleteWorktree` komutları ve event'i, `Worktree` modeli, `UiState.UseWorktree/WorktreeName`, `EffectiveUseWorktree`/
`IsWorktreeForced`/`RunBranchIntent`, worktree chip/popover, "Sync required" akışı; `ProjectIdentityRebase` in-place
no-op olarak kalır ya da silinir; (b) `Core/Git/RepositoryWriter.cs`: ff-only (mevcut `FastForwardUpdater`'dan taşınır)
+ `CheckoutAsync(branch)` + `StashAndCheckoutAsync(branch)`; guard `NoGitMutationOutsideExternalsTests` tek dosyayı
pinler; (c) branch chip'i checkout düğmesi; Settings → General "When switching branches with uncommitted changes:
Stop / Stash and switch" (`UiState.DirtySwitchPolicy`); (d) `Core/Git/HeadWatcher` (`git rev-parse --git-dir` →
`logs/HEAD` FileSystemWatcher, 1,5 s debounce, reflog satırı sınıflandırma) → App: boşta Sync, koşarken checkout/
pull/reset/merge/rebase → `StopRunCommand(Graceful)` + uçuştaki projelerin sonucu deftere yazılmaz (Supervisor'a
`InterruptRunCommand` ya da graceful stop'a "discardInFlight" bayrağı) + koşu sonu Sync; commit → yok; (e) açılışta
ve pencere aktivasyonunda Sync (5 s eşiği); (f) reveal kapısı yapısal imzayla; (g) `run-inflight.json` çökme
kurtarması; (h) eski havuz için konsol ipucu; (i) sessiz Sync ve konsol bölümleri (spec §6.2):
kendiliğinden Sync konsolu/akışı temizlemez, kaydırmaz, reveal oynatmaz, fetch yapmaz; branch değişimi yeni bölüm açar
(temizlik önce, stash/checkout satırları sonra; reddedilen checkout bölüm açmaz; koşu içi değişimde özet satırı);
`ClearPlanSurface` yalnız yapısal imza değişince; çift Sync önleme (HEAD + branch son Sync'le aynıysa atla); (j) git
işlemi kapısı (spec §6.4): `index.lock` / `MERGE_HEAD` / `rebase-merge` / `rebase-apply` / `CHERRY_PICK_HEAD` /
`REVERT_HEAD` işaretleri, kendiliğinden Sync bekler, 2 s işaret yoklaması yalnız bu durumda, 30 s takılı kilit
uyarısı (silinmez), checkout ve pull kilidi, Build'e tek uyarı satırı, branch chip'inde amber nokta; (k) pull kapıları
(spec §6.5): kirli ağaçta uyarı ve ret (stash ayarı uygulanmaz), git işlemi yarıdayken kilit, `CanShowBehind`'den
worktree koşulu düşer; (l) ARCHITECTURE §8.6-8.7, §9.4, §10.1-10.4, §12-13, §16, §20, README
"Branch / worktree", CLAUDE.md değişmezleri (üç istisna; "reset" cümlesi).

**Faz 3 — Dışarıdan derleme kredisi (Core + Supervisor + App):** (a) `CsprojEvaluator`: `OutputPath` (cfg koşullu grup)
ve `OutputType`; `EvaluatedProject.OutputFileFor(cfg)`; (b) `Core/Incremental/OutputEvidence.cs`: derleme kanıtı,
beslenen çıktılar, girdi klasörleri ve HintPath hedefleri; "çıktı kimin" ve iki kip (`EvidenceMode`), döngü grubu;
(c) `BuildState.FedOutputs` (aracın derlemesinden öğrenilir, `PersistBuildStateOnSuccess`); (d) `WillBuildEvaluator`
zaman kipi dalı ve yeni gerekçeler (`OutputStale`, `OutputMissing`, `OutputReplaced`) → etiket/tooltip eşlemesi;
(e) `HintPathClass.ExternalOsysPlatform` → `ExternalPlatformBin`; (f) ARCHITECTURE §4, §7.1, §9.4, §14.3 ve README
"Reading the list" ("built outside this tool").
