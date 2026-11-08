# Worktree'de derleme — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native). Adımlar `- [ ]` ile izlenir.

**Goal:** Ayarlardan tanımlı tek bir linked worktree'yi ikinci kök olarak kullanmak: alt bardan Repo ↔ Worktree geçişi,
ayarda yoksa oluşturma, kirli ağaçta branch seçiminde karar kartı.

**Architecture:** Worktree bir mod değil, ikinci bir köktür. `RootPath` ayardaki ana repo kökü olarak kalır; yeni
`ActiveRoot` (ActiveCopy'ye göre `RootPath` ya da `WorktreeFolder`) motora giden her komutun ve git okumasının kökü
olur. Geçiş, bugünkü kök değişiminin aynısıdır (satırlar hollow, son Sync unutulur, BranchChange Sync). Tek yeni git
yazımı `worktree add --detach` RepositoryWriter'a girer ve Supervisor'dan çalışır.

**Spec:** Asana 1220246925085205 (Features) — "Worktree: Ayardan tanımlı worktree'de derleme ve Repo | Worktree geçişi".

## Global Constraints

- Git yazımı yalnız `Core/Git/RepositoryWriter.cs`; yeni fiil yalnız `worktree add --detach <folder>`, cwd = ana repo kökü.
- Araç kendiliğinden yazmaz: oluşturma yalnız Settings Save'de, yalnız klasör yoksa ya da boş klasörse.
- Klasör denetimi saf dosya okuması (process yok); `.git` dosyası → gitdir → `commondir` = ana reponun git dizini.
- `StashOnBranchSwitch` ayarı KALIR: açıksa bugünkü gibi sessizce stash; kapalıyken kirli ağaç karar kartını açar.
- Aynı branch iki kopyada açık olamaz: diğer kopyada açık branch → checkout denenmez (stash de), konsola uyarı.
- Kod/UI/log İngilizce; yorumlar ve `.claude/` Türkçe; kopya YASAK (metinler tek kaynakta).
- Yeni XAML kökü → realize testi. Her fix/özellik önce KIRMIZI test.
- Bar en dar pencerede (Size.WindowMinWidth) sığmaya devam eder (`ActionBarTests.RestartEngine` ölçüsü).

## Review Focus

1. Worktree aktifken Settings açılınca Repository root alanı worktree yolunu DEĞİL ana kökü gösterir → Task 4 testi.
2. Worktree aktifken kalıcı durumun `RepositoryRoot`'u worktree yoluna kaymaz (yeniden açılış ana köke döner) → Task 4 testi.
3. Diğer kopyada açık branch kirli ağaçta seçilir: stash YAPILMADAN reddedilir → Task 2 testi.
4. Worktree klasörü repo kökünün içinde: Save reddedilir (keşif projeleri iki kez bulurdu) → Task 1 + Task 5 testi.
5. Worktree aktifken kök değişir ya da worktree alanı silinir: kopya Repo'ya döner, komutlar ana kökü taşır → Task 4 testi.

---

### Task 1: Core — worktree klasör denetimi

**Files:** Create `src/BuildOrchestrator.Core/Git/WorktreeFolder.cs`; Modify `Core/Git/GitDirectory.cs`, `Core/Git/HeadReader.cs`;
Test `tests/BuildOrchestrator.Tests/Git/WorktreeFolderTests.cs`.

**Produces:**
- `GitDirectory.ResolveCommon(string gitDir) : string` (HeadReader'daki özel `ResolveCommonDir` buraya taşınır, HeadReader onu çağırır).
- `enum WorktreeFolderState { Creatable, Linked, Foreign, Nested, NotRooted, NoRepository }`
- `WorktreeFolder.Inspect(string repositoryRoot, string folder) : WorktreeFolderState`
- `WorktreeFolder.CheckedOutIn(string rootPath, string branch) : string?` — branch'i HEAD'inde tutan BAŞKA kopyanın kökü
  (ana HEAD + `<common>/worktrees/*/HEAD`, yol `<common>/worktrees/*/gitdir`'den), yoksa null.

- [ ] Kırmızı testler (temp klasör + elle yazılmış `.git`/`gitdir`/`commondir` dosyaları, git süreci yok):
  yok → Creatable; boş klasör → Creatable; bu reponun linked worktree'si → Linked; başka reponun worktree'si / dolu
  klasör → Foreign; repo kökünün içi ya da kendisi ya da kökü kapsayan → Nested; göreli yol → NotRooted; repo kökünde
  `.git` yok → NoRepository; `CheckedOutIn` ana kopyadan worktree'nin branch'ini ve worktree'den ana branch'i bulur,
  kendi kopyasını saymaz, detached HEAD'i saymaz.
- [ ] Uygula, yeşil, commit.

### Task 2: Core + motor — oluşturma ve "başka kopyada açık" kapısı

**Files:** Modify `Core/Git/RepositoryWriter.cs`, `Contracts/Model/ProjectModels.cs`, `Contracts/Ipc/IpcMessages.cs`,
`Supervisor/SupervisorHost.cs`, `App/Services/EngineWakeRule.cs`; Test `Git/WorktreeCreatorTests.cs`,
`Git/BranchSwitcherTests.cs`, `Ipc/IpcMessagesTests.cs`, `Externals/NoGitMutationOutsideTheWriterTests.cs`,
`Git/NoWorktreeSurfaceTests.cs`.

**Produces:**
- `CheckoutStatus.CheckedOutElsewhere` (sona eklenir); `CheckoutResult.Detail` = tutan kopyanın kökü.
- `BranchSwitcher.SwitchAsync`: AlreadyOn'dan SONRA, kir kapısından ÖNCE `WorktreeFolder.CheckedOutIn` → CheckedOutElsewhere.
- `sealed record WorktreeCreateResult(bool Created, string? Revision, string? Detail)`;
  `sealed class WorktreeCreator(IProcessRunner runner, string repositoryRoot, string gitExecutable = "git")` →
  `Task<WorktreeCreateResult> CreateAsync(string folder, CancellationToken ct)`; önce `Inspect == Creatable` değilse
  oluşturmadan döner (Detail = neden).
- `CreateWorktreeCommand(string RootPath, string Folder)` ("createWorktree"),
  `WorktreeCreatedEvent(string Folder, bool Created, string? Revision, string? Detail)` ("worktreeCreated");
  hata kodları `worktreeRejected` (koşu uçuşta), `worktreeFailed` (beklenmeyen).
- Guard'lar: `MutatingGitVerb`'e `worktree`; NoWorktreeSurfaceTests yeni kuralı pinler (fiil yalnız writer'da,
  sözleşmede yalnız bu iki tür; obj yönlendirme ve StartRun alanı yasağı kalır), doc'unda eski iddia + gerekçe.

- [ ] Kırmızı: gerçek git ile temp repo (BranchSwitcherTests düzeneği) — oluşturma detached HEAD'li worktree kurar,
  dolu klasöre dokunmaz; diğer kopyada açık branch kirli ağaçta stash'lenmeden CheckedOutElsewhere döner; IPC round-trip.
- [ ] Uygula, yeşil, commit.

### Task 3: App — ActiveRoot, Repo ↔ Worktree geçişi, kalıcılık

**Files:** Create `App/ViewModels/RunViewModel.Worktree.cs`, `App/ViewModels/WorkingCopy.cs`; Modify `RunViewModel.cs`,
`RunViewModel.ActionBar.cs`, `RunViewModel.Workspace.cs`, `RunViewModel.AutoSync.cs`, `RunViewModel.GitOperation.cs`,
`App/Shell/UiStateStore.cs`, `MainWindow.xaml.cs`; Test `App/WorktreeSwitchTests.cs`, `App/WorktreeRootGuardTests.cs`.

**Produces:**
- `enum WorkingCopy { Repository, Worktree }`; VM: `WorktreeFolder` (string, ""), `ActiveCopy`, `ActiveRoot`,
  `CanSwitchCopy => HasWorkspace && CanSync()`, `Task SwitchCopyAsync(WorkingCopy copy)`,
  `event EventHandler? WorktreeSetupRequested` (yapılandırılmamış/Linked olmayan worktree'ye geçiş isteği).
- Komutlar ve git okumaları `ActiveRoot` taşır (StartRun, Sync, ListBranches, Clean, Optimize, Checkout, Pull, HEAD
  izleyicisi, git işlem kapısı). Guard: App'te `new …Command(RootPath` biçimi yok.
- Geçiş = kök değişimi: satırlar hollow, son Sync unutulur, BranchChange Sync'i bölüm satırıyla (`WorkingCopyLines`).
- Kök değişimi / workspace kapanışı / worktree alanının değişmesi kopyayı Repository'ye döndürür.
- UiState: `WorktreeFolder` (string?), `WorktreeActive` (bool?); açılışta Worktree yalnız `Inspect == Linked` ise.
  Kalıcı `RepositoryRoot` hep `RootPath`'tir.

- [ ] Kırmızı testler (BranchCheckoutTests düzeneği): Worktree'ye geçiş Sync'i worktree kökünü taşır; Repo'ya dönüş ana
  kökü; yapılandırılmamışken geçiş komut göndermez, kurulum isteği yükselir; kapı kapalıyken hiçbir şey; kök değişince
  kopya Repo; guard.
- [ ] Uygula, yeşil, commit.

### Task 4: Ayarlar — Worktree folder alanı ve oluşturma akışı

**Files:** Modify `SettingsDraftViewModel.cs`, `SettingsDialog.xaml(.cs)`, `MainWindow.xaml.cs` (PickFolder başlıklı tek
seam), `Services/OsActions.cs` (sahipsiz PickFolder kopyası kalkar), `AccessibilityNames.cs`, `RunViewModel.ActionBar.cs`
(ApplySettingsAsync `worktreeFolder`), `RunViewModel.Worktree.cs` (oluşturma), `OperationLabel.cs`;
Test `App/WorktreeSettingsTests.cs`, realize: mevcut SettingsDialog realize testine alan.

**Produces:** taslak `WorktreeFolder`; `SaveBlockedReason` worktree nedenleri (Foreign/Nested/NotRooted/NoRepository);
`ClearAll` alanı da boşaltır; Save: Creatable ise `CreateWorktreeCommand` (iş meşgul → butonlar pasif, konsol satırı,
işlem pill'i), sonuç satırı; kurulum isteğiyle açılmışsa başarıdan sonra Worktree'ye geçer.

- [ ] Kırmızı testler, uygula, yeşil, commit.

### Task 5: Alt bar — Worktree anahtarı

**Files:** Modify `Views/ActionBar.xaml(.cs)`; Test `App/ActionBarWorktreeTests.cs` + en dar pencere ölçüsü.

- [ ] Workspace etiketi ile branch chip'i arasında kare ikon ToggleButton (Ds.Bar.Chip, 24 px); işaretli = Worktree;
  tooltip aktif kopyayı ve yolunu söyler; `CanSwitchCopy` ile etkin; yalnız workspace varken görünür.
- [ ] En dar pencere testi yeni anahtarla da sığar (ölç; sığmıyorsa sorunu raporla, eşik gevşetme).

### Task 6: Branch karar kartı

**Files:** Create `Views/BranchSwitchCard.xaml(.cs)`, `ViewModels/BranchSwitchText.cs`; Modify `ActionBar.xaml(.cs)`,
`RunViewModel.ActionBar.cs`, `RunViewModel.Workspace.cs`; Test `App/BranchSwitchCardTests.cs` + realize.

**Produces:** `BranchDecision { Cancel, Stash, OtherCopy }`; VM `PendingBranchSwitch` (hedef, kopya, kirli dosya sayısı);
`Task DecideBranchSwitchAsync(BranchDecision)`. Dirty cevabı (ayar kapalı) kartı açar. OtherCopy: checkout DİĞER
kökte gönderilir; Switched/AlreadyOn → kopya geçer (bölüm: kopya + branch satırı); Dirty → kopya geçer ve kart o
bağlamda yeniden açılır; CheckedOutElsewhere/Failed → uyarı, geçiş yok. Worktree yapılandırılmamışsa OtherCopy kurulum
isteği yükseltir.

- [ ] Kırmızı testler, uygula, yeşil, commit.

### Task 7: Dokümanlar + guard listesi

ARCHITECTURE (git yazım tablosu, worktree bölümü, alt bar, ayarlar, guard listesi §17, kod haritası §22), CLAUDE.md
değişmezi (dördüncü istisna: worktree oluşturma), USER-GUIDE, FEATURES/WHY (+ .tr.md). DocumentationTests yeşil.

### Task 8: CI

Rebase develop → push → CI yeşil → develop'a merge + push → develop CI yeşil → branch sil.
