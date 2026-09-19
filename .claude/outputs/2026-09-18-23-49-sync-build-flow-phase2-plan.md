# Faz 2 — Tek ağaç ve branch: uygulama planı (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-18-07-56-sync-build-flow-spec.md` §1 (kararlar 1, 7-13,
20-23), §3, §6 (6.1-6.6), §7 (satır 13-14, 28-30, 32, 45, 48-55), §8, §10, §11 Faz 2. Faz 1 planının sonundaki
görev listesi (a-l) bu dökümün girdisidir. Faz 1 kararları (`.claude/summaries/2026-09-18-23-34-…-phase1-done.md`)
geçerlidir.

**Hedef:** araç her zaman kullanıcının çalışma ağacında derler; worktree kodu ve UI'ı kalkar. Branch chip'i gerçek
`git checkout` yapar (kirli ağaçta ayar karar verir). `.git\logs\HEAD` izlenir; branch değişimi, commit, pull ve
pencereye dönüş Sync'i kendiliğinden koşturur. Kendiliğinden Sync sessizdir, branch değişimi yeni konsol bölümü
açar. Koşu sırasında branch değişirse koşu nazikçe durur ve uçuştakilerin sonucu deftere yazılmaz. Git işlemi
yarıdayken Sync bekler. Motor çökerse uçuştaki projeler bir sonraki açılışta geçersizlenir. Dışarıdan derleme
kredisi Faz 3'tür; bu plan ona dokunmaz.

**Mimari:** Git okuma primitifleri (git dizini, HEAD, işlem işaretleri, reflog satırı) Core'da saf dosya
okumasıdır. Git yazımı tek dosyada kalır (`Core/Git/RepositoryWriter.cs`). HEAD izleyicisi Core'dadır, App onu
bağlar ve Sync kipine (Manual / Appended / BranchChange / Silent) karar verir. Motor tarafında yeni bir komut
(`checkoutBranch`), yeni bir durdurma türü (`StopKind.Interrupt`) ve çökme kurtarma dosyası (`run-inflight.json`)
eklenir.

## Preflight — kod ile plan / spec karşılaştırması (2026-09-18)

Referanslar sync-build-flow (`5eeaef6`) üzerinde tek tek doğrulandı. Bulunan uyuşmazlıklar ve alınan kararlar
(kullanıcı bilgisayar başında değildi; "en sağlıklı kararı ver, sonda raporla" dedi):

| # | Ne | Kod ne diyor | Karar |
|---|---|---|---|
| P1 | Açılışta Sync | Bugün **yok** (`MainWindow.xaml.cs:156-160` "seed-but-idle"; `OnEngineReady` yalnız satır yazar) | Spec §6.2 "Uygulama açılışı → tam transkript, reveal, fetch" yeni davranıştır; uygulanır (Task 6). Çelişki değil |
| P2 | Kirli ağaç ayarı (Stop / Stash and switch) | Settings → General yalnız açma/kapama satırı destekler (`GeneralSettingDefinition` bool) | İki seçenek tek anahtara iner: **"Stash and switch branches"** (kapalı = Stop, varsayılan). Yeni şablon yok |
| P3 | Git dizini `git rev-parse --git-dir` ile bulunur (spec §6.1) | `.git` dosyası (`gitdir: …`) okuması `VcsDetector`'da zaten var; process yok | Aynı sonucu veren saf dosya okuması (`GitDirectory.Resolve`) — izleyici ve kapı her tetikte git process'i açmaz |
| P4 | Uçuştakilerin sonucu "deftere yazılmaz" (spec §6.1) | Başarı `PersistBuildStateOnSuccess`, hata `InvalidateBuildStateOnFailure` yazar | "Yazılmaz" = **başarı olarak yazılmaz**: sonuç güvenilmez sayılır (`trustedResult=false`), defter kanıtsız hata yazar → satır gri `never built`. Çökme kurtarmasıyla (§5.5) aynı hâl; eski kayıt yeşil kalıp yarım derlenmiş havuzu örtemez |
| P5 | Koşu özeti "run log klasörünün yolu" (spec §6.2) | App koşu klasör adını bilmez (`RunLogWriter.RunDirectory` Supervisor'da) | `RunStartedEvent`'e SONA `LogDirectory` (default `null`) |
| P6 | Mutasyon yüzeyi "tek dosya" (spec §6.6) ve Faz 1 listesinde `RepositoryWriter.cs` | Bugün `FastForwardUpdater.cs` + `WorktreeManager.cs` (guard izin listesi) | Dosya `Core/Git/RepositoryWriter.cs` olur: `FastForwardUpdater` sınıfı adıyla taşınır, `BranchSwitcher` eklenir; `WorktreeManager.cs` silinir; guard tek dosyayı pinler |
| P7 | Doküman kendi içinde çelişik | README branch satırı `Branch changed: … — Sync required`, ARCHITECTURE §10.3 `branch target: … — worktree will be used at Build` | İkisi de Task 12'de yeniden yazılır; koddaki gerçek (`RunViewModel.ActionBar.cs:140-141`) iki satırı da basıyor |
| P8 | ARCHITECTURE §10.1 ve §21.3 "bir kaynak guard'ı mutasyonu pinler" diyor | §17.2 guard tablosunda o guard'ın satırı yok | Task 12'de §17.2'ye satır eklenir |
| P9 | README "Known limits" "externals never enter its graph — no edges" | ARCHITECTURE §10.6 ve kod: harici projeler grafa girer | Task 12'de düzeltilir (Faz 2 dışı ama aynı süpürme) |
| P10 | `ClearPlanSurface` imzayı sıfırlamaz (`RunViewModel.ActionBar.cs:395-408`) | Reveal bugün `|| _syncInFlight` ile kurtarılıyor (`RunViewModel.Workspace.cs:649`) | O koşul kalkınca Clean/Optimize sonrası liste boş kalırdı → `ClearPlanSurface` `_lastTopologySignature = null` yapar (Task 6) |

## Claude kararları (itiraz edilebilir)

- Sync kipi dörttür: **Manual** (Sync butonu: temizler, fetch, transkript), **Appended** (açılış, pull, Clean,
  Optimize, Settings Save: temizlemez, fetch, transkript altına), **BranchChange** (temizler, ilk satır yeni branch,
  fetch yok, transkript), **Silent** (commit, pencereye dönüş, HEAD'de branch dışı hareket: temizlemez, fetch yok,
  transkript gizli; uyarı/hata satırları yine görünür; akışa tek satır).
- "Yeni bölüm" yalnız branch **adı** değişince açılır (spec §6.2 ilkesi). Aynı branch'te VS'den pull / reset:
  Silent + akışa tek satır (`synced after pull`).
- Kendiliğinden Sync meşgulken gelen tetik kaybolmaz: tek bekleyen tetik tutulur, meşguliyet bitince koşar (çift
  Sync kontrolü o anda yapılır).
- Branch chip'i açılır listesi uzak branch'i (`origin/x`) seçince: yerelde `x` varsa `checkout x`, yoksa
  `checkout --track origin/x`. `origin/HEAD` listede gösterilmez.
- Kendiliğinden Sync'in "N projects changed" sayısı App'te satır kararlarının (Standing + etiket) Sync öncesi/sonrası
  farkından sayılır; sıfırsa satır yazılmaz (pencereye dönüş). Commit'te satır her zaman yazılır.
- Eski havuz ipucu her açılışta, havuz klasörü durdukça bir kez yazılır; araç havuzu silmez.
- `StartRunCommand.Branch` da kalkar (koşu branch'i git'ten okur); `UiState.Branch` kalkar (branch = HEAD).
  Eski `ui-state.json` / NDJSON'daki fazla alanlar yok sayılır (System.Text.Json varsayılanı).
- `PathSanitizer.cs` tamamen silinir (tüm çağıranları worktree kodu).
- `ProjectInput(LogicalPath, PhysicalPath)` tek yola iner (`Path`); fizik/mantık ayrımı yalnız worktree içindi.

## Global Constraints

- Proje kuralları `CLAUDE.md`: **kırmızı test kuralı** — kod değişikliğinden ÖNCE testin KIRMIZI verdiği
  gösterilir; kırmızı **derleme hatası değil assertion hatası** olmalıdır (yeni API gerekiyorsa önce `throw new
  NotImplementedException()` gövdeli iskelet derlenir, test assertion ile düşer). Davranış değişince eski test YENİ
  kuralı pinleyecek şekilde yeniden yazılır, doc'una `[DEĞİŞEN KURAL — spec 2026-09-18 §…]` + eski iddia + gerekçe.
  Eşik/bütçe gevşetmek YASAK. **Kopya YASAK / tek doğruluk kaynağı.** Yeni XAML kökü/şablonu = realize testi.
  Kod/UI metinleri İngilizce, yorumlar Türkçe.
- Silinen davranışın testleri silinir; silme commit mesajında ve task raporunda test adlarıyla listelenir. Silme
  task'larının kırmızısı bir **kaynak guard'ıdır** (ör. "src'de `\"worktree\"` git fiili yok") — silmeden önce
  kırmızı, sonra yeşil.
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıksa Debug bin kilitlenir → `-c Release`. Uzun testler (tam süit, Acceptance) ön planda koşulur.
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` KULLANMA (UTF-8/CRLF bozar) — Edit/Write.
- Commit mesajı dosyaya yazılıp `git commit -F` ile atılır; Türkçe, ASCII. **Attribution satırı eklenmez.**
  Task başına commit.
- **Branch:** çatı `sync-build-flow`. Bu faz `sync-build-flow-phase2`'de yürür (çatıdan açılır, push'lanır). Faz
  yeşil olunca (tam süit + Acceptance) `--no-ff` ile çatıya merge + push; doğrulanınca faz branch'i local ve
  remote'tan silinir. **`main`'e merge YOK.** Oturum `main`'de biter.
- Sözleşme değişiklikleri: yeni alan **SONA, default'lu**. Enum'a yeni değer **sona** (metin olarak yazılır).
- Yeni ölçüm/sonda testi yok; zamanlama testleri enjekte edilen gecikme/saatle deterministik. Gerçek dosya
  izleyicisi testi en fazla birkaç saniye bekler (`Category` etiketsiz, normal süitte).
- ARCHITECTURE.md anlatı üslubu: yerinde yeniden yazım, changelog yok, sayı gömme yok.
- Sıra: **bloklayıcı:** T1-T4 (sonrakilerin zemini). **Önemli:** T5-T10. **Orta:** T0, T11. **Kapanış:** T12-T13.

---

### Task 0: Tasarım sürümü v1.21.0 (README metni)

**Dosyalar:** `.claude/outputs/2026-09-18-10-16-design-v1.20.0/README.md` kopyalanarak
`.claude/outputs/<tarih>-design-v1.21.0/README.md` (prototip kopyalanmaz; başta "prototip v1.19.0'dır, bu sürümde
yalnız metin değişti" notu).

**Değişen bölümler (yerinde):** §2.7 action bar (madde 7 `worktree` chip'i kalkar; branch chip'i "checkout eder";
git işlemi amber noktası; hover listesinden worktree düşer; "kaynak bilgisi worktree popover'ında" cümlesi kalkar),
§2.8 popover'lar (Branch popover dipnotu "Switching checks the branch out in your working tree."; aktif olmayan
branch seçiminin worktree/boot/`Sync required` anlatısı kalkar; kirli ağaç davranışı; Worktree popover maddesi
kalkar), §3.1 fazlar (açılışta Sync; kendiliğinden Sync ve bölüm kuralı — spec §6.2 tablosu özetle; "her işlem
konsolu temizler" → "kullanıcının başlattığı işlem ve branch değişimi"), §3.4 (kilitli chip'ler: branch), §3.9
(pull kapıları: git işlemi yarıdayken kilit, worktree koşulu düşer), §4 state (branch kalkar, stash ayarı eklenir),
§8 (v1.16.0 kuralı "araç yalnız iki yerde yazar" → üç kullanıcı eylemi: pull, checkout, stash), §9 `## v1.21.0`
sürüm notu (neden: worktree/in-place ayrımı kalktı, branch değişimi ve dışarıdaki git işlemleri kendiliğinden
yansısın — kullanıcı isteği 2026-09-17).

Test yok (doküman). Commit: `docs(design): v1.21.0 - tek agac, checkout eden branch chipi, kendiliginden sync`.

### Task 1: Git okuma primitifleri (Core)

**Mevcut kod:** `Core/Externals/VcsDetector.cs:17` (`.git` dosyası = gitdir), `Core/Git/GitService.cs:70-125`
(`GetHeadCommitAsync`, `GetCurrentBranchAsync`, `GetDirtyPathsAsync` — process'li okumalar; burada kullanılmaz).
Fixture: `tests/.../Git/GitTestRepo.cs` (gerçek git, temp repo).

**Kural (yeni saf tipler, `Core/Git/`):**
- `GitDirectory.Resolve(string root) → string?`: `<root>\.git` klasörse o; dosyaysa `gitdir: <yol>` satırı (göreliyse
  köke göre) ; yoksa `null`.
- `HeadState` record `(string? Branch, string? Sha)` + `HeadReader.Read(string gitDir) → HeadState?`: `HEAD`
  dosyası `ref: refs/heads/x` → branch `x`, sha loose ref'ten (`refs/heads/x`), yoksa `packed-refs`'ten; detached →
  `(null, sha)`; unborn → `(x, null)`. Linked worktree'de ref'ler ortak dizinde (`commondir`) aranır.
- `GitOperationProbe.Inspect(string gitDir) → GitOperation` (`enum GitOperation { None, CommandRunning, Merge,
  Rebase, CherryPick, Revert }`): `index.lock` → CommandRunning; `MERGE_HEAD` → Merge; `rebase-merge\` /
  `rebase-apply\` → Rebase; `CHERRY_PICK_HEAD` → CherryPick; `REVERT_HEAD` → Revert. Öncelik: işlem işaretleri
  `index.lock`'tan önce (merge sırasında da kilit görülebilir).
- `ReflogEntry.Classify(string lastLine) → HeadMove` (`enum HeadMove { Commit, BranchSwitch, Other }`): mesaj
  (`\t`'den sonrası) `commit` ile başlıyorsa (`commit:`, `commit (amend):`, `commit (initial):`, `commit (merge):`)
  Commit; `checkout: moving from A to B` ve A≠B ise BranchSwitch; diğerleri (pull, merge, reset, rebase,
  cherry-pick, revert, `checkout` aynı branch) Other. Metinler git'in İngilizce reflog öneki — lokalize değil.
- Metinler (tooltip/konsol) tek yerde: `Core/Git/GitOperationText.cs` — `Tooltip(GitOperation)` ("Merge in progress
  — finish or abort it in git", "Rebase in progress — …", "Cherry-pick in progress — …", "Revert in progress — …",
  "A git command is running"), `BuildWarning` ("a merge is in progress — files with conflict markers will not
  compile"; işleme göre ad), `StuckLock` ("git's index.lock has been there for 30 s — a git process may have crashed;
  delete it only if no git command is running").

**Testler (önce KIRMIZI, iskelet `NotImplementedException`):** `GitDirectoryTests`: klasör, gitfile (göreli ve
mutlak), yok. `HeadReaderTests` (GitTestRepo): branch + loose sha, `git pack-refs --all` sonrası packed sha, detached,
unborn, linked worktree (test içinde `git worktree add` — yalnız fixture, ürün kodu değil). `GitOperationProbeTests`:
her işaret dosyası/klasörü, hiçbiri, öncelik. `ReflogEntryTests`: tablo (yukarıdaki mesajlar + bozuk satır → Other).

Doküman: Task 12. Commit: `feat(git): git dizini, HEAD, islem isaretleri ve reflog okuyuculari`.

### Task 2: Tek yazım yüzeyi — checkout ve stash (Core)

**Mevcut kod:** `Core/Git/FastForwardUpdater.cs:1-144` (sınıf, `FastForwardStatus`, `FastForwardResult`),
`tests/.../Externals/NoGitMutationOutsideExternalsTests.cs:46-59` (fiil regex'i, izin listesi: FastForwardUpdater +
WorktreeManager), `:70-80` (`merge` tek dosyada), `:82-90` (tarama kanıtı dosya adını pinler). Testler:
`tests/.../Git/FastForwardUpdaterTests.cs`.

**Kural:**
- `FastForwardUpdater.cs` → `Core/Git/RepositoryWriter.cs` (git mv; sınıf/enum/record adları DEĞİŞMEZ — çağıranlar
  etkilenmez). Aynı dosyaya `BranchSwitcher`:
  `Task<CheckoutResult> SwitchAsync(string target, bool isRemote, bool stashIfDirty, CancellationToken ct)`.
  - Sıra: (1) `GetDirtyPathsAsync`; kirli ve `!stashIfDirty` → `CheckoutStatus.Dirty` (DirtyCount), hiçbir şey
    yapılmaz. (2) kirli ve `stashIfDirty` → `["stash", "push", "-u", "-m", "build-orchestrator: leaving <cur> for
    <target>"]`; başarısızsa `StashFailed`, checkout yok. (3) Hedef: yerel ise `["checkout", target]`; uzak
    (`origin/x`) ise yerelde `x` varsa `["checkout", "x"]`, yoksa `["checkout", "--track", target]`. (4) exit≠0 →
    `Failed` (stderr metni ayrıştırılmaz; `CommandLineTool.DescribeFailure`), stash yapıldıysa sonuç stash mesajını
    yine taşır. (5) başarı → `Switched` + yeni HEAD.
  - `CheckoutResult(CheckoutStatus Status, string? Branch, string? Revision, int DirtyCount, string? StashMessage,
    string? Detail)`; `enum CheckoutStatus { Switched, AlreadyOn, Dirty, StashFailed, Failed }`. Hedef zaten aktif →
    `AlreadyOn`, git'e dokunulmaz.
  - Mesaj metni tek yerde (`BranchSwitcher.StashMessage(from, to)`).
- Guard: izin listesi tek dosya `BuildOrchestrator.Core\Git\RepositoryWriter.cs`; `merge` testi aynı dosyayı bekler;
  tarama kanıtı aynı dosyayı arar. Sınıf adı `NoGitMutationOutsideExternalsTests` → `NoGitMutationOutsideTheWriterTests`
  (doc: `[DEĞİŞEN KURAL — spec §6.6]` eski iddia "iki dosya, checkout/stash hiçbir yerde" + gerekçe).

**Testler (önce KIRMIZI):** `BranchSwitcherTests` (GitTestRepo): `A_clean_tree_switches_to_a_local_branch`,
`A_dirty_tree_with_stop_is_refused_and_nothing_changes` (HEAD, dosya içeriği, `stash list` boş),
`A_dirty_tree_with_stash_stashes_untracked_too_and_switches` (`stash list` mesajı, untracked dosya gitti),
`A_remote_branch_without_a_local_one_is_checked_out_as_tracking` (CloneFull), `A_remote_branch_with_a_local_one_uses_the_local`,
`The_active_branch_is_already_on_and_runs_no_git_write` (FakeProcessRunner: yalnız okuma çağrıları),
`A_failed_checkout_after_a_stash_reports_the_stash`. Guard: `The_writer_file_is_the_only_mutation_surface`
(ürün ağacında `"checkout"` bu dosyada — önce kırmızı: dosya yok). `FastForwardUpdaterTests` değişmez (yeşil kalır).

Doküman: Task 12. Commit: `feat(git): checkout ve stash tek yazim dosyasinda; guard tek dosyayi pinler`.

### Task 3: Worktree'yi motordan sil (Contracts + Core + Supervisor)

**Mevcut kod:**
- Supervisor: `Program.cs:30` (`PreparedWorkspace`), `:41-48` (`WorktreePreparationException`), `:55`
  (`WorktreePoolCapBytes`), `:74` (`--worktrees`), `:90,146` (`prepared`), `:99` (resolver), `:104`
  (`WorkspaceServices.Default(…, worktreePoolRoot, …)`), `:144-146` (`PrepareAsync` çağrısı), `:162` (`scanner.Scan(
  workspace.ScanRoot)`), `:180-185` (`ProjectIdentityRebase.To`), `:244-374` (`PrepareAsync`,
  `MandatoryWorktreeFailure`, `ResolveSelectedShaAsync`), `:419-474` (`ComputeIncremental`: `workspace.ScanRoot` git
  kökü `:430`, `PhysicalPath` `:438-439`, `Rebase` `:467-474`). `SupervisorHost.cs:25-30,51` (`WorkspaceServices.
  Worktree`), `:131-134` (dispatch), `:288-310` (liste/sil). `RunCoordinator.cs:23-29` (`RunPlan.BuildPathById`),
  `:97-108,131` (resolver parametresi), `:173-175` (`_worktreeObjRoot`), `:695-706` (`WorktreePreparationException`
  yakalama), `:725-736` (resolver çağrısı), `:858-863` (stale-obj uyarısı yalnız in-place), `:997`, `:1068-1069`,
  `:1704-1721` (`buildPath`, `BaseIntermediateOutputPath`), `:2002-2012` (`RunContext`).
- Core: `Git/WorktreeManager.cs` (tamamı), `Git/PathSanitizer.cs` (tamamı), `MsBuild/WorktreeObjPathResolver.cs`
  (tamamı), `MsBuild/MsBuildArguments.cs:31,60` + `MsBuild/MsBuildInvoker.cs:7-13`
  (`BaseIntermediateOutputPath`), `Planning/ProjectIdentityRebase.cs` (tamamı), `Planning/PlanProgressLines.cs:16,
  25-29` (`PreparingWorktree`), `Incremental/IncrementalRunBinder.cs:42-64` (`physicalPathOf`),
  `Incremental/ProjectInputs.cs:8-13,82,105` (`ProjectInput(LogicalPath, PhysicalPath)`),
  `Incremental/IncrementalPlanner.cs:219-233`, `Workspace/SyncWorkspaceService.cs:26-35,106` (worktree yorumları).
- Contracts: `Ipc/IpcMessages.cs:28-29,249` (ayrımcılar), `:79-80,113-117` (`StartRunCommand.Branch/UseWorktree/
  WorktreeName`), `:213-219` (`ListWorktreesCommand`, `DeleteWorktreeCommand`), `:417-419` (`WorktreeListEvent`);
  `Model/ProjectModels.cs:236-238` (`Worktree`).
- Testler (silinir): `Git/WorktreeManagerTests.cs`, `MsBuild/WorktreeObjPathResolverTests.cs`,
  `Planning/ProjectIdentityRebaseTests.cs`, `Git/PathSanitizerTests.cs`; `RunCoordinatorTests` içindeki
  `worktree_*` / `in_place_run_ignores_a_supplied_resolver…` / `Every_run_resolves_its_own_worktree_obj_root…` /
  `A_worktree_run_reports_main_root_identities…` / `worktree_preparation_failure…`; `ExternalRunTests.
  A_worktree_run_does_not_redirect_an_external_projects_obj`; `IncrementalRunBinderTests.a_project_built_in_a_worktree…`;
  `ProjectInputsTests.the_physical_path_follows_the_mapper…`; `SupervisorIpcTests.ListWorktrees_answers…`;
  `IpcMessagesTests` worktree round-trip'leri (`:87-101`, `:536`, `:582`); `ProjectModelsTests.Worktree_round_trips…`;
  `PlanProgressLinesTests:30,37`. **Yeniden yazılır:** `RunCoordinatorTests` harness'ı (`:154-200` resolver
  parametresi düşer), `worktree_run_does_not_warn…` → `A_run_warns_about_a_stale_default_obj`
  (`[DEĞİŞEN KURAL — spec §1-1]`: eski "worktree koşusu uyarmaz"; worktree yok, her koşu varsayılan obj'dedir).
  `SupervisorIpcTests.Psi(…, worktreePoolDir)` parametresi düşer. `VcsDetectorTests.A_gitfile_worktree_counts…`
  KALIR (harici kopyanın kendisi linked worktree olabilir). `IncrementalPlannerTests:540` KALIR (kök-göreli yol ikinci
  klonda da geçerli).

**Kural:** yukarıdakiler silinir; planlama tek kökte: `scanner.Scan(cmd.RootPath)`, `ComputeIncremental` git'i
`cmd.RootPath`'te okur, `RunPlan` 3 alan, `RunContext` obj alanı yok, stale-obj uyarısı her koşuda. Harici güncelleme
yorumundaki "worktree'den önce" gerekçesi düşer (sıra aynı kalır: tarama öncesi).

**Testler (önce KIRMIZI):** yeni kaynak guard'ı `NoWorktreeSurfaceTests`: (1) src'de `["worktree"` git fiili yok,
(2) `BaseIntermediateOutputPath` src'de yok, (3) `IpcCommand` / `IpcEvent` türetilmiş tiplerinde "Worktree" adı yok
(reflection), (4) `StartRunCommand`'da `UseWorktree`/`WorktreeName`/`Branch` özelliği yok (reflection). Silmeden önce
dördü de kırmızı. `IpcMessagesTests`: `An_old_start_run_line_with_worktree_fields_still_parses` (eski NDJSON satırı
fazla alanlarla çözülür).

Doküman: Task 12. Commit: `refactor(engine): worktree modu kalkti; her kosu calisma agacinda`.

### Task 4: Worktree'yi App'ten sil; branch = HEAD (App)

**Mevcut kod:** `Shell/UiStateStore.cs:43-45` (`Branch`, `UseWorktree`, `WorktreeName`), `MainWindow.xaml.cs:162-164`
(seed), `:1043-1065` (`OnWorkflowPreferenceChanged`). `RunViewModel.cs:792-798` (`_branch`, `_useWorktree`,
`_worktreeName`), `:944-946` (`StartRunCommand` kurulumu), `:1220-1225` (`ListWorktreesCommand`), `:1740`
(`WorktreeListEvent`). `RunViewModel.ActionBar.cs:46-47` (`IsWorktreeForced`), `:72-75` (`_branchChosenByUser`),
`:95` (`RunBranchIntent`), `:115` (`EffectiveUseWorktree`), `:124-142` (`SelectBranch` — worktree/boot/"Sync
required"), `:159-178` (`AutoWorktreeName`, `EffectiveWorktreeName`, `DeleteWorktreeAsync`).
`RunViewModel.Workspace.cs:96-99` (`CanShowBehind` worktree koşulu), `:183-184` (`Worktrees`), `:703-741`
(`OnBranchList`, `ReconcileBranchWithInventory`). `Views/ActionBar.xaml:53-63` (worktree chip + popup),
`Views/ActionBar.xaml.cs:53,67,72,96-131,139,152-178,204-209,376-383,484-492,620-622`. `Views/WorktreePopover.xaml(.cs)`
(tamamı). `Views/BranchPopover.xaml:74` (dipnot). `AccessibilityNames.cs:121,131,207-213`.
`Services/DiagnosticsReport.cs:26,94` (`WorktreePool` satırı), `Views/AboutDialog.xaml.cs:160`
(`WorktreeManager.DefaultPoolRoot`). Testler: `ActionBarTests`, `BranchInventoryTests`, `PopoverTests`,
`PopoverToggleTests`, `InventoryPublishTests`, `AccessibilityTests`, `UiStateStoreTests`, `DiagnosticsReportTests`,
`AboutDialogTests`, `RunViewModelTests`, `RunViewModelStateTests`, `TitleBarContextTests`, `WorkspaceLabelTests`.

**Kural:**
- Worktree chip'i, popover'ı, VM üyeleri, `UiState` alanları, tanı satırı, erişilebilirlik adları silinir.
- `Branch` yalnız okunur gerçek: `OnBranchList` → `Branch = ActiveBranchName ?? Branch` (detached'te son değer
  durur); `_branchChosenByUser`/`RunBranchIntent`/`ReconcileBranchWithInventory`'nin "açık seçim" dalı kalkar.
- `CanShowBehind => Behind is > 0` (spec §6.5).
- `SelectBranch` bu task'ta yalnız aktif branch dışı seçimde **hiçbir şey yapmaz** (checkout Task 5'te gelir) —
  "Sync required" / `ResetRowsToHollow` / `Phase=Boot` yolu kalkar. Branch popover'da `origin/HEAD` gizlenir.
- `BranchPopover.xaml:74` dipnotu: "Switching checks the branch out in your working tree."
- `ResetRowsToHollow` yalnız `ApplyRepositoryRoot` için kalır.

**Testler (önce KIRMIZI):** `ActionBarTests`: `The_action_bar_has_no_worktree_chip` (realize: `PART_WorktreeChip`
şablonda yok), `The_behind_chip_shows_whenever_the_tree_is_behind` (`[DEĞİŞEN KURAL — spec §6.5]`: eski "worktree
modunda gizli"). `BranchInventoryTests`: `The_branch_value_follows_the_checked_out_branch` (eski "açık seçim
korunur" testleri yeniden yazılır, `[DEĞİŞEN KURAL — spec §1-7/8]`), `Remote_head_is_not_offered`.
`UiStateStoreTests`: `An_old_state_file_with_worktree_fields_still_loads`. `DiagnosticsReportTests`: satır listesi
worktree'siz. Silinen App testleri commit'te listelenir.

Doküman: Task 12. Commit: `refactor(ui): worktree chipi ve popover'i kalkti; branch HEAD'i gosterir`.

### Task 5: Branch chip'i checkout eder; kirli ağaç ayarı (Contracts + Supervisor + App)

**Mevcut kod:** `SupervisorHost.cs:107-142` (dispatch), `:189-210` (`PullRepositoryAsync` — model: komut → Core
yazıcı → `SyncProgressEvent` + tamamlandı olayı). `IpcMessages.cs:17-32,221-252` (ayrımcılar). App:
`RunViewModel.ActionBar.cs:124-142` (`SelectBranch`), `Views/BranchPopover.xaml.cs:110-119` (`Pick`),
`Views/ActionBar.xaml.cs:621` (chip kilidi `hasWs && !midRun`). Ayar: `ViewModels/GeneralSettings.cs:11-23,44-67`,
`ViewModels/SettingsDraftViewModel.cs:99-103,117-122,171-172,197,207,239-249,253-275` (`PullExternalsBeforeBuild`
deseni), `ViewModels/SettingsFile.cs:53,68-82`, `Shell/UiStateStore.cs:89` (`UpdateExternals` deseni),
`MainWindow.xaml.cs:179,1053,1061`. Konsol: `RunViewModel.cs:2372-2394` (`ClearConsoleForNewOperation`),
`RunViewModel.Stream.cs:92-99` (`ClearStreamForNewOperation`).

**Kural:**
- IPC: `CheckoutBranchCommand(string RootPath, string Branch, bool IsRemote, bool StashIfDirty)` (`"checkoutBranch"`),
  `CheckoutCompletedEvent(CheckoutStatus Status, string? FromBranch, string? Branch, string? Revision, int DirtyCount,
  string? StashMessage, string? Detail)` (`"checkoutCompleted"`). `CheckoutStatus` Contracts'a taşınır (Core onu
  kullanır; iki tanım YASAK). Supervisor: koşu uçuştaysa `error(checkoutRejected)`; değilse
  `BranchSwitcher.SwitchAsync` → olay. Supervisor konsol satırı yazmaz — satırları App olaydan kurar (temizlik önce,
  not sonra kuralı yalnız böyle mümkün).
- Satır metinleri Core'da tek yerde (`PlanProgressLines`): `SwitchedBranch(from, to, sha7)` ("Switched to <to>
  (<sha7>) — from <from>"), `StashedBeforeSwitch(message)` ("Stashed uncommitted changes: \"<message>\" — restore them
  with git stash pop"), `SwitchRefusedDirty(n)` ("<n> files have uncommitted changes — commit or stash them first"),
  `SwitchFailed(detail)`.
- App: `SelectBranch(b)` aktif değilse ve kilit yoksa `CheckoutBranchCommand(RootPath, b.Name, b.IsRemoteTracking,
  StashOnBranchSwitch)`; `CurrentOperation = OperationLabel.Checkout` (yeni etiket, "Switching branch"). Olay:
  - `Switched` → konsol + akış temizlenir (yeni bölüm), sonra stash satırı (varsa) ve switch satırı, sonra
    `SyncCoreAsync(SyncMode.BranchChange)` (Task 6 gelene kadar `clearBuffers:false` ile — Task 6 kipe çevirir).
  - `Dirty` / `StashFailed` / `Failed` → konsol TEMİZLENMEZ, uyarı altına eklenir, işlem biter.
  - `AlreadyOn` → hiçbir şey.
- Kilit: `CanSwitchBranch => HasWorkspace && !IsMidRunLocked && !SyncBusy && !CleanBusy && !OptimizeBusy &&
  !IsEngineUnavailable` (Task 9 git işlemi koşulunu ekler); chip `IsEnabled` bunu okur.
- Ayar: `GeneralSetting.StashOnBranchSwitch`, yeni grup `"BRANCHES"`, satır "Stash and switch branches" — açıklama
  "When the working tree has uncommitted changes, stash them (including untracked files) and switch. Off: switching
  stops and asks you to commit or stash first." Varsayılan kapalı. `UiState.StashOnBranchSwitch` (`bool?`, yok ⇒
  false), `SettingsFile` anahtarı `"stashOnBranchSwitch"`, `RunViewModel.StashOnBranchSwitch`, seed + persist
  (`OnWorkflowPreferenceChanged`) `UpdateExternals` desenindeki yerlere.

**Testler (önce KIRMIZI):** `IpcMessagesTests`: iki yeni tipin round-trip'i. `SupervisorIpcTests`
(gerçek Supervisor, GitTestRepo): `Checkout_switches_the_working_tree_and_reports_it`,
`Checkout_on_a_dirty_tree_is_refused_by_default`, `Checkout_is_rejected_while_a_run_is_in_flight`.
`RunViewModelTests` (sahte engine): `Picking_another_branch_sends_a_checkout_with_the_stash_setting`,
`A_successful_switch_clears_the_console_then_writes_the_stash_and_switch_lines` (sıra pinlenir),
`A_refused_switch_keeps_the_console_and_appends_the_warning`, `Picking_the_active_branch_sends_nothing`,
`The_branch_chip_is_locked_mid_run`. `SettingsDraftViewModelTests`: `The_stash_setting_round_trips_through_the_file`,
`It_is_off_by_default`. `GeneralSettingsTests` (varsa) grup listesi. Realize: Settings General sayfası yeni satırla
realize olur.

Doküman: Task 12. Commit: `feat(branch): branch chipi checkout eder; kirli agac ayari`.

### Task 6: Sync kipleri, sessiz Sync, reveal kapısı, açılış Sync'i (Contracts + Core + App)

**Mevcut kod:** `Core/Workspace/SyncWorkspaceService.cs:67-216` (`RunAsync`: fetch `:95-103`, `activeBranch`
`:104`, behind `:105-115`, `SyncCompletedEvent` `:212-215`), `IpcMessages.cs:154-156` (`SyncWorkspaceCommand`),
`:335-337` (`SyncCompletedEvent`), `GitService.cs:224,257` (`GetRemoteTrackingShaAsync`, `CountBehindAsync`).
App: `RunViewModel.cs:1156-1157` (`SyncAsync`), `:1178-1226` (`SyncCoreAsync(bool clearBuffers)`),
`RunViewModel.ActionBar.cs:284-316` (`ApplySettingsAsync`, `ChangeRepositoryAsync` → `false`), `:395-408`
(`ClearPlanSurface`), `RunViewModel.Workspace.cs:319-335` (`HandOverToSyncAsync`), `:458-464`
(`OnPullCompletedAsync`), `:468-476` (`OnSyncCompleted`), `:562-666` (`OnWorkspaceTopology`, kapı `:648-653`),
`:226` (`_lastTopologySignature`), `RunViewModel.cs:2313-2318` (`OnEngineReady`), sync progress satır işleyicisi
(`grep -n "SyncProgressEvent" src/BuildOrchestrator.App/ViewModels`). Testler: `SyncWorkspaceServiceTests`
(`No_distance_is_reported_when_the_selected_branch_is_not_the_active_one` ~`:312`),
`ProjectListFilterTests.A_no_changes_sync_replays_the_reveal`, `StickyRevealTriggerTests.
A_no_changes_sync_returns_the_list_to_the_top`, `OperationPipelineTests`, `RunViewModelTests`.

**Kural:**
- `SyncWorkspaceCommand`'a SONA `bool Fetch = true`. Sync fetch ve behind için **aktif branch'i** kullanır
  (`cmd.Branch` yalnız detached HEAD'de yedek). `Fetch=false` → fetch satırı yok; behind = `GetRemoteTrackingShaAsync
  (active)` bilinen uzak uca göre `CountBehindAsync` (fetch'siz, son bilinen durum — spec §6.2).
- `SyncCompletedEvent`'e SONA `string? HeadSha = null`, `string? ActiveBranch = null` (çift Sync kontrolünün ve
  branch değerinin kaynağı). App `OnSyncCompleted`: `LastSyncHead = (e.ActiveBranch, e.HeadSha)`, `LastSyncAt = now`,
  `Branch = e.ActiveBranch ?? Branch`.
- App `enum SyncMode { Manual, Appended, BranchChange, Silent }`; `SyncCoreAsync(SyncMode mode, string?
  silentReason = null)` — tablo "Claude kararları"nda. Çağıranlar: Sync butonu Manual; açılış, pull, Clean/Optimize
  devri, Settings Save Appended; checkout (Task 5) BranchChange.
  - Silent: `SyncProgressEvent` satırları `dim/info/cmd` ise konsola yazılmaz, `warn/error` yazılır; `SelectedProjectId`
    korunur; `CurrentOperation` değişmez (kalıcı işlem pill'i yok — sessiz); bitince akışa tek satır: commit →
    `synced after commit`; diğer → `synced · N projects changed` (N>0 ise; metinler `StreamText`'te tek yerde).
  - BranchChange: temizlik, ardından Task 5 satırları, ardından transkript (fetch yok).
- Reveal kapısı: `OnWorkspaceTopology` `TopologyChanged`'ı **yalnız** imza değişince ateşler (`|| _syncInFlight`
  kalkar). `SyncCoreAsync` `ClearPlanSurface` çağırmaz. `ClearPlanSurface` (Clean/Optimize tıklaması)
  `_lastTopologySignature = null` yapar — böylece o işlemin zincirli Sync'i reveal'i yeniden oynar.
- Açılış: `OnEngineReady` motorun **ilk** hazır oluşunda ve `HasWorkspace` iken `SyncCoreAsync(Appended)`; motor
  yeniden başlatmada (restart) değil.

**Testler (önce KIRMIZI):** `SyncWorkspaceServiceTests`: `A_sync_without_fetch_runs_no_fetch_and_measures_behind_from_the_known_remote`,
`Sync_fetches_the_checked_out_branch_whatever_the_command_names` (yeniden yazılan `No_distance…` testi,
`[DEĞİŞEN KURAL — spec §6.5]`), `The_completed_event_carries_head_and_active_branch`. `RunViewModelTests`:
`A_silent_sync_keeps_the_console_and_stream_and_adds_one_line`, `A_silent_sync_shows_warnings_but_not_the_transcript`,
`A_silent_sync_with_no_changes_writes_nothing_on_activation`, `The_sync_button_still_clears_the_console`,
`The_first_engine_ready_syncs_with_the_transcript_and_a_restart_does_not`. Reveal: `ProjectListFilterTests.
A_no_changes_sync_replays_the_reveal` ve `StickyRevealTriggerTests.A_no_changes_sync_returns_the_list_to_the_top`
**yeniden yazılır** → `A_sync_with_the_same_structure_updates_in_place` (`[DEĞİŞEN KURAL — spec §1-13]`: eski "her
Sync reveal oynatır"); yeni `A_sync_that_adds_a_project_replays_the_reveal`,
`A_clean_empties_the_list_and_its_sync_replays_the_reveal`. `OperationPipelineTests`: Sync'in tıklamada listeyi
boşaltmadığı pinlenir.

Doküman: Task 12. Commit: `feat(sync): sync kipleri; kendiliginden sync sessiz; reveal yalniz yapi degisince; acilista sync`.

### Task 7: HEAD izleyicisi ve pencereye dönüş (Core + App)

**Mevcut kod:** Task 1 okuyucuları; `MainWindow.xaml.cs:346-352` (`Loaded`), `:1200` (`ShowFromTray`), App'te
`Activated` işleyicisi **yok**. `RunViewModel.cs:1232` (`CanSync`), `Workspace.cs:125` (`SyncBusy`).

**Kural:**
- `Core/Git/HeadWatcher` (`IDisposable`): `Start(gitDir, Action<HeadMove> onSettled)`; `FileSystemWatcher`
  `<gitDir>\logs` üzerinde `HEAD` (Changed/Created/Renamed); her olay debouncer'ı yeniden kurar; 1,5 s sessizlikten
  sonra reflog'un son satırı `ReflogEntry.Classify` ile sınıflanıp **tek** çağrı yapılır. Debounce saati enjekte
  edilir (`Func<TimeSpan, CancellationToken, Task> delay`); sabit `SettleDelay = 1.5 s` tek yerde. `logs` klasörü
  yoksa ya da izleyici kurulamazsa `Start` false döner (sebep metniyle).
- App `Services/AutoSyncCoordinator` (saf karar + VM köprüsü): girdiler `HeadTrigger(HeadMove)` ve
  `WindowActivated()`. Karar:
  1. Meşgul (Sync/Clean/Optimize/checkout uçuşta) → tek bekleyen tetik saklanır, meşguliyet bitince yeniden
     değerlendirilir.
  2. Koşu uçuşta → Task 8.
  3. Git işlemi yarıda → Task 9.
  4. `HeadReader.Read` sonucu `LastSyncHead`'e eşitse **atla** (çift Sync yok — spec §6.1; aracın kendi checkout'u
     ve pull zinciri tek Sync kalır).
  5. Branch adı farklıysa `BranchChange` (konsol ilk satırı `Switched to <to> — from <from>`, dışarıdan geldiği için
     sha7 ile), değilse `Silent` (commit → `synced after commit`, diğer → değişiklik sayısı).
  6. Pencereye dönüş: son Sync'ten (başlangıç ya da bitiş, hangisi yeniyse) 5 s geçmediyse atla; aksi `Silent`
     (HEAD değiştiyse 5. madde geçerli).
- İzleyici `RootPath` değişince yeniden kurulur; kurulamazsa konsola bir kez `HEAD watcher unavailable (<reason>) —
  switching back to the window still syncs`.
- `MainWindow`: `Activated += (_, _) => _vm.OnWindowActivated()`; `ShowFromTray` zaten Activated üretir.

**Testler (önce KIRMIZI):** `HeadWatcherTests` (GitTestRepo, gerçek FS): `A_checkout_settles_into_one_branch_switch`
(`git checkout` + ardışık iki commit → tek çağrı; bekleme ≤ 5 s), `A_commit_settles_into_commit`,
`A_missing_logs_folder_reports_unavailable`; debounce saf testi: `Writes_within_the_window_collapse_into_one`
(enjekte gecikme). `AutoSyncCoordinatorTests` (saf, sahte VM portu): `An_unchanged_head_runs_no_sync`,
`A_branch_switch_opens_a_new_section`, `A_commit_syncs_silently_with_its_line`, `A_trigger_during_a_sync_runs_once_after_it`,
`Activation_within_five_seconds_of_a_sync_does_nothing`, `Activation_later_syncs_silently`,
`Our_own_checkout_plus_the_watcher_is_one_sync`.

Doküman: Task 12. Commit: `feat(sync): HEAD izleyicisi ve pencereye donuste sessiz sync`.

### Task 8: Koşu sırasında branch değişimi — nazik kesme (Contracts + Supervisor + App)

**Mevcut kod:** `IpcMessages.cs:47` (`StopKind { Graceful, Hard }`), `:270-271` (`RunStartedEvent`).
`RunCoordinator.cs:293-311` (`TryRequestStop`), `:327-337` (`DrainCapLocked`), `:488-491` (`StopRequested`),
`:1125-1173` (`BuildProjectAsync`, `ReportProjectResult` çağrısı `trustedResult: true` `:1170-1171`), `:1261-1328`
(`ReportProjectResult`; `invalidates` `:1270`), `:1914-1937` (`InvalidateBuildStateOnFailure`), `:1028-1071` (koşu
sonu, `RunCompletedEvent` `:1056-1057`), `:1355-1507` (SCC grup; `ReportCycleMember` `:1494`). Supervisor
`RunLogWriter.RunDirectory` (`Core/Logs/RunLogWriter.cs:18-22`). Testler: `RunCoordinatorTests.
graceful_stop_lets_in_flight_project_finish…` (`:426`), `FakeInvoker` (`:89`, gated handler).

**Kural:**
- `StopKind`'e SONA `Interrupt`. `TryRequestStop(Interrupt)`: Graceful gibi (yeni dispatch yok, drain cap) + `_interrupted
  = true`. Hard sonradan gelirse Hard kazanır (bugünkü kural).
- `ReportProjectResult`: `trustedResult &= !Interrupted` (kilit altında okunur) — kesme sonrası biten her proje
  güvenilmez: başarı defterde kanıtsız hata olur (`Trusted=false`, gri), hata kanıt sayılmaz (`Evidence=false`).
  Tek kapı: `ReportProjectResult`'ın başındaki okuma; SCC üyeleri de buradan geçer.
- `RunStartedEvent`'e SONA `string? LogDirectory = null` (koşunun log klasörü).
- App: koşu uçuştayken `HeadTrigger(Other|BranchSwitch)` → `StopRunCommand(runId, Interrupt)` bir kez; akışa
  `interrupted by branch change`; koşu bitince (`RunCompleted`) bekleyen tetik değerlendirilir. Branch değiştiyse yeni
  bölüm: temizlik, ilk satır `Run interrupted by a branch change — N built, M not built · logs: <LogDirectory>`
  (metin `StreamText`/`PlanProgressLines` tek yerde), ardından BranchChange Sync. `Commit` → hiçbir şey.
- Güvenlik ağı: koşu bitince `HeadReader.Read` ≠ `LastSyncHead` ise (izleyici kaçırdıysa) aynı karar yolu.

**Testler (önce KIRMIZI):** `RunCoordinatorTests`: `An_interrupt_starts_nothing_new_and_lets_the_in_flight_finish`,
`A_project_that_succeeds_after_an_interrupt_is_not_recorded_as_built` (defter: `LastResult=Failed`,
`FailedSignature=null`; olay `Trusted=false`), `A_project_that_fails_after_an_interrupt_is_not_evidence`,
`A_later_hard_stop_wins_over_an_interrupt`, `Run_started_names_its_log_directory`. `IpcMessagesTests`:
`interrupt` metin olarak yazılır. `AutoSyncCoordinatorTests`/`RunViewModelTests`: `A_branch_switch_mid_run_interrupts_once`,
`A_commit_mid_run_does_nothing`, `After_the_interrupted_run_a_new_section_starts_with_its_summary`,
`A_head_change_missed_by_the_watcher_is_caught_when_the_run_ends`.

Doküman: Task 12. Commit: `feat(engine): kosu sirasinda branch degisimi nazikce keser; ucustakiler deftere yazilmaz`.

### Task 9: Git işlemi yarıdayken kapı (App)

**Mevcut kod:** Task 1 `GitOperationProbe`, `GitOperationText`; `Views/ActionBar.xaml.cs:376-383`
(`BuildBranchWorktreeChips` → Task 4 sonrası branch chip kurulumu), `:484-492` (değer/tooltip), `:609-633`
(`RefreshEnabled`); `RunViewModel.Workspace.cs:451-452` (`CanPullRepository`); `RunViewModel.cs:880-950`
(`BeginRunAsync` — planlama başı satırı yeri), `:1178-1190` (Sync temizliği sonrası).

**Kural:**
- VM `GitOperation` (`[ObservableProperty]`), tetik anında ve 2 s yoklamada `GitOperationProbe.Inspect` ile
  güncellenir. Yoklama **yalnız** `GitOperation != None` iken çalışır (zamanlayıcı enjekte edilir); işaret kalkınca
  bekleyen kendiliğinden Sync koşar.
- Kendiliğinden Sync (izleyici, pencereye dönüş) `GitOperation != None` iken koşmaz; akışa bir kez
  `waiting for git — <tooltip metni>`.
- `CommandRunning` (index.lock) 30 s sürerse konsola bir kez `GitOperationText.StuckLock`. Araç kilidi silmez.
- Branch chip'inde amber nokta (`Brush.Amber` token'ı, 6 px, chip içeriğine eklenen `Ellipse`), tooltip
  `GitOperationText.Tooltip(op)`. `CanSwitchBranch` ve `CanPullRepository` `GitOperation == None` ister; kilitli chip
  tooltip'i nedeni söyler.
- Build/Rebuild/satırdan Build: `GitOperation is Merge or Rebase or CherryPick or Revert` ise planlama başında tek
  satır `GitOperationText.BuildWarning(op)`; koşu engellenmez.
- Sync butonu her zaman çalışır; temizlikten sonra aynı durumda tek satır ("the working tree is mid-<op> — results may
  change once it finishes").

**Testler (önce KIRMIZI):** `RunViewModelTests` (sahte probe + enjekte zamanlayıcı): `A_head_trigger_waits_while_a_merge_is_in_progress`,
`When_the_marker_goes_the_waiting_sync_runs`, `A_lock_held_for_30_seconds_warns_once_and_is_never_deleted`,
`Checkout_and_pull_are_locked_mid_merge`, `Build_mid_merge_warns_once_and_still_runs`, `The_sync_button_still_runs_mid_merge_and_says_so`,
`Polling_runs_only_while_a_marker_exists`. `ActionBarTests` (realize): `The_branch_chip_shows_an_amber_dot_mid_merge`
(nokta görünür, tooltip metni), `No_dot_when_git_is_idle`.

Doküman: Task 12. Commit: `feat(git): git islemi yaridayken sync bekler; chipte amber nokta; checkout ve pull kilitli`.

### Task 10: Çökme kurtarma — `run-inflight.json` (Core + Supervisor + App)

**Mevcut kod:** `Supervisor/Program.cs:63-70` (`cacheRoot`, `BuildStateStore`), `:92` (host `EngineReadyEvent`
yazımı `SupervisorHost.cs:83-92`), `RunCoordinator.cs:1144` (`ProjectStartedEvent` — dispatch noktası), `:1316-1326`
(tamamlanma), `:1427-1431` (SCC üye dispatch), `:593-651` (`ExecuteRunAsync` finally — her koşu çıkışı),
`:1914-1937` (`InvalidateBuildStateOnFailure`; kanıtsız dal). `Core/State/BuildStateStore.cs:140` (`Upsert`),
`:174-184,270` (atomik yazım). `IpcMessages.cs:254` (`EngineReadyEvent`). App `RunViewModel.cs:2313-2318`
(`OnEngineReady`).

**Kural:**
- `Core/State/InFlightLedger` (`<cacheRoot>\run-inflight.json`, atomik yazım `BuildStateStore` deseninden — yardımcı
  paylaşılır, kopyalanmaz): `Add(id)`, `Remove(id)`, `Clear()`, `Recover(BuildStateStore, DateTimeOffset now) →
  IReadOnlyList<string>`.
- Kanıtsız geçersizleme tek yerde: `BuildStateStore.InvalidateWithoutEvidence(id, now)` (`LastResult=Failed,
  LastRunAt=now, FailedSignature=null, FailedAt=null`; kayıt yoksa no-op). `InvalidateBuildStateOnFailure`'ın kanıtsız
  dalı ve `Recover` bunu çağırır.
- Supervisor: `ProjectStartedEvent`'ten hemen önce `Add` (tek proje ve SCC üyesi), `ReportProjectResult` finally'de
  `Remove`, `ExecuteRunAsync` finally'de `Clear`. Yazım hatası uyarıdır, koşuyu durdurmaz.
- Açılış: `Program.Main` host'u kurmadan önce `Recover`; sayı `EngineReadyEvent`'e SONA `int InterruptedProjects =
  0`. App: >0 ise konsola `previous run was interrupted; N projects will rebuild` (metin Core `PlanProgressLines`).

**Testler (önce KIRMIZI):** `InFlightLedgerTests`: add/remove/clear, bozuk dosya → boş liste (kurtarma yapılmaz, dosya
silinir), `Recover_marks_each_listed_project_as_an_unevidenced_failure_and_empties_the_file`,
`Recover_without_a_record_opens_none`. `BuildStateStoreTests`: `InvalidateWithoutEvidence…`. `RunCoordinatorTests`:
`A_project_in_flight_is_listed_until_its_result_is_reported`, `The_ledger_is_empty_after_every_run_exit`
(normal/stop/hard/planFailed). `SupervisorIpcTests`: `A_supervisor_started_after_a_crash_recovers_and_reports_the_count`
(dosyayı elle yaz → `engineReady.interruptedProjects`). `RunViewModelTests`: konsol satırı.

Doküman: Task 12. Commit: `feat(engine): cokme kurtarma; ucustaki projeler acilista gecersizlenir`.

### Task 11: Eski havuz ipucu (Core + App)

**Mevcut kod:** `Core/Git/WorktreeManager.cs:123` (`DefaultPoolRoot` — Task 3'te silinir; yol
`%LOCALAPPDATA%\BuildOrchestrator\worktrees`), `Core/Logs/RunLogPaths.cs` (`DefaultLogsRoot` deseni).

**Kural:** `Core/Paths/LegacyWorktreePool.DefaultRoot` (tek tanım) + `Hint(string root) → string?`: klasör varsa
"The old worktree pool at <root> is no longer used — delete it to reclaim space, then run `git worktree prune` in
the repository". App ilk motor hazır olduğunda bir kez konsola yazar. Araç silmez.

**Testler (önce KIRMIZI):** `LegacyWorktreePoolTests`: klasör var → metin, yok → `null`. `RunViewModelTests`: ilk
engine-ready'de bir kez, restart'ta tekrar yok.

Commit: `feat(ui): eski worktree havuzu icin tek satir ipucu`.

### Task 12: Dokümanlar ve CLAUDE.md

- **ARCHITECTURE.md** (yerinde yeniden yazım): §1.2 garanti satırı (araç yalnız kullanıcı eylemiyle yazar: pull,
  checkout, stash), §1.3, §3.1 Core satırı, §4.5 kilit cümlesi, §5.2 komut listesi (`checkoutBranch`, `interrupt`
  durdurma türü; `listWorktrees`/`deleteWorktree` kalkar; Clean/Optimize "havuza dokunmaz" cümleleri), §5.3 olaylar
  (`checkoutCompleted`, `syncCompleted.headSha/activeBranch`, `runStarted.logDirectory`,
  `engineReady.interruptedProjects`), §7.1 yol terimi cümlesi, §7.5, §8.6 (kimlik/fiziksel yol paragrafı kalkar;
  planlama adımları), §8.7 **kalkar** ya da "Crash recovery" olarak yeniden yazılır, §8.8 (Interrupt), §9.4 (obj
  izolasyonu yok; OutDir kuralı), §10.1 (başlık "The git surface"; tablo: checkout/stash/ff-only tek dosyada), §10.2
  (Sync tetikleri, kipler, fetch'siz Sync, "known seam" kalkar), §10.3 → "Branch switching" (checkout, kirli ağaç
  ayarı, HEAD izleyicisi, çift Sync, koşu içi kesme, git işlemi kapısı), §10.4 **kalkar**, §10.5 **kalkar**, §10.6
  gerekçe cümlesi, §10.7 (behind her zaman; pull kapıları; "tek yazım" cümlesi), §12.1 (açılış Sync'i, kurtarma
  satırı, eski havuz ipucu), §12.3 (pencereye dönüş), §12.4, §13.2 (reveal yalnız yapı değişince; konsol bölüm kuralı;
  branch chip'i ve amber nokta; kilitler), §13.3 (Worktree popover kalkar; Branches envanteri HEAD'i izler), §13.5
  (temizlik kuralı), §13.8, §14.5 (planlama adımından worktree), §16 (`run-inflight.json` eklenir, worktree satırı ve
  `--worktrees` kalkar, `ui-state.json` branch/worktree → stash ayarı), §17.2 (mutasyon guard'ı ve `NoWorktreeSurface`
  satırları), §20, §21.2-21.4 (girdi yüzeyi, yazım yüzeyi, havuz maddeleri), §22 kod haritası (yeni dosyalar; silinen
  satırlar; "Git and worktrees" → "Git").
- **README.md:** "What it does" (worktree cümleleri), obj izolasyon maddesi, git kullanımı cümlesi, adım 2 Sync
  (kendiliğinden Sync, pull kapıları, "nothing else writes" cümlesi), adım 3 "Branch / worktree" → "Branch"
  (checkout, Stop / Stash and switch, dışarıdan değişimin yansıması, git işlemi yarıdayken), adım 5 ("preparing the
  worktree"), About ortam listesi, State on disk (`run-inflight.json`, havuz satırı kalkar), Known limits (worktree
  maddesi kalkar; P9 düzeltmesi).
- **CLAUDE.md değişmezleri:** Core satırı ("git/worktree" → "git"); OutDir maddesi ("Yalnız obj izole edilir"
  kalkar: hiçbir çıktı yolu değiştirilmez; git'in görevleri: fetch, branch, checkout, harici güncelleme; harici
  kökler cümlesindeki worktree kısmı kalkar); git yazım maddesi: "Git'e araç KENDİLİĞİNDEN yazmaz: `pull`/`reset`/
  `switch` hiçbir akışta çalıştırılmaz. Üç istisna da kullanıcının açık kararıdır: (a) harici kökler …; (b) ana repo
  pull — yalnız `N behind` chip'i, yalnız aktif branch, yalnız ff-only, kirli ağaçta ret; (c) branch chip'inden
  checkout — kirli ağaçta ayar karar verir (Stop varsayılan; Stash and switch'te `git stash push -u`). Mutasyon
  yüzeyi TEK dosyadır: `Core/Git/RepositoryWriter.cs` (kaynak guard'ı)." Havuz worktree'si kalktığı için "reset
  hiçbir akışta çalıştırılmaz" harfiyen doğrudur.
- `git grep -n -i "worktree" -- ARCHITECTURE.md README.md CLAUDE.md` → yalnız geliştiricinin `-ai` worktree kuralı
  (CLAUDE.md "Nerede çalışılır") ve harici kopyanın linked worktree olabileceği cümlesi (§10.6) kalır.

Commit: `docs: tek agac ve branch akisi dokumanlara islendi`.

### Task 13: Kapanış — guard'lar, tam süit, Acceptance, merge

- Kaynak guard'ları: `NoGitMutationOutsideTheWriterTests`, `NoWorktreeSurfaceTests`, token/motion/D8, `NoTurkishUserText`
  yeşil.
- `OsysIncrementalAcceptanceTests` (`:31,163,305` — branch-bounce ve `PlanWorktree` niyet satırı) yeni modele göre
  yeniden yazılır (Task 3'te derleme için dokunulur; burada koşulur): worktree'siz, aynı ağaçta.
- Tam süit ön planda: `dotnet test … --filter "Category!=Acceptance"`; ardından `--filter "Category=Acceptance"`
  (uygulama kapalıyken).
- Final branch review (bütün faz), bulgular tek fix dispatch'i.
- Merge: `sync-build-flow-phase2` → çatı `sync-build-flow` (`--no-ff`), push; doğrulanınca faz branch'i local ve
  remote'tan silinir. `main`'e merge YOK. Kullanıcıya çatıda neyi deneyeceğinin kısa listesi. Oturum `main`'de biter.
