# Yüzey kapısı takip işleri — uygulama planı

Kaynak: `.claude/outputs/2026-10-09-00-40-cycle-trust-and-surface-gate-plan.md` Bölüm 2 final review'ının ertelenen
maddeleri (kullanıcı 2026-10-09: "sıkıntılı düzeltilmesi gereken ne varsa düzeltelim"). İki bölüm de `develop`'ta
(`d4802a49`, `09b6c5f4`); bu plan onların üstüne gelir.

## Durum (kaldığımız yer)

Branch `fix/surface-gate-followups` (`develop` = `09b6c5f4` üstünde) açık, çalışma ağacı temiz, 4 commit atılmış:

| Commit | İş | Durum |
|---|---|---|
| `d89af733` | §13.2/§14.3: yakınsamayan gruptaki derleyici hatası kuralı §8.8 ile aynı (kullanıcı kararı: kod esas) | bitti |
| `4e64612c` | D8 kapısı `_willBuildIds` okur (Resolve'da Sync'in bayat bayrağı pre-skip satırının gerekçesini eziyordu); test `A_resolve_pre_skip_of_a_row_the_sync_saw_dirty_keeps_its_reason` RED→GREEN | bitti |
| `af358aec` | `CycleRoundPolicy.IsSettled` (oturmuş üye yüklemi Core'da); `RunCoordinator` onu çağırır; 8 satırlık Theory | bitti |
| `82220178` | Kural i-b yalnız hash karşılaştırır (`CycleMemberNeed.OutsideSurfacesMoved`); test `an_outside_dependency_whose_output_moved_with_the_same_surface_keeps_the_member_carried` RED→GREEN; §8.8 i-b maddesi; `the_first_matching_rule_names_the_reason` içindeki `outside` → `olderThanInputs` | bitti |

Bu commit'lerde ilgili sınıflar yeşil (321 test). Tam süit HENÜZ koşulmadı. Kalan iş aşağıdaki dört task.

**Kapsam dışı (kullanıcı kararı bekliyor, bu plana GİRMEZ):** SDK-style projeye kanıt yolu türetme (OSYS'te 4 PRM
projesi; ayrı plan); bayat taşınan üyenin `succeeded (0ms)` satırı (bırakıldı); HintPath kopyasının yüzeyini kaydetmek
(bırakıldı).

## Global Constraints

- **Branch:** `fix/surface-gate-followups` (var; `git switch` ile geç). Ana checkout
  `D:\Projects\Other\Apps\app_build_orchestrator`, worktree YOK. Task başına commit; mesaj Türkçe,
  `fix:`/`test:`/`refactor:`/`docs:` önekli; **attribution satırı yazılmaz** (`Co-Authored-By`, `Generated with` YOK).
  Bitişte `develop`'a `--no-ff` merge + push, branch'i sil, CI'ı kontrol et, oturum `develop`'ta biter.
- **Kırmızı test kuralı:** davranış DEĞİŞTİREN her adımda önce test, kırmızı gösterilir, sonra kod. Bu planın testleri
  çoğunlukla mevcut davranışın PİNİDİR: pin'in gerçekten tuttuğu, her adımda yazılı **mutasyonla** gösterilir (kodu
  geçici olarak bozup testin düştüğünü gör, geri al). Mutasyonu gösteremiyorsan test yanlıştır.
- **Kopya YASAK**, karar Core'da, IPC'ye (`Contracts/Ipc/IpcMessages.cs`) alan/olay eklenmez, OutDir'e dokunulmaz,
  stdout yalnız NDJSON. `CHANGELOG.md` ve `Directory.Build.props` → `Version` DOKUNULMAZ.
- **Açık uygulama:** Build Orchestrator (tray dahil) ve `BuildOrchestrator.Supervisor.exe` kapalı; Debug bin kilitliyse
  `-c Release`; `--no-build` KULLANMA.
- **Süit:** task sonunda ilgili sınıf(lar); bitişte tam süit `--filter "Category!=Acceptance" > .claude/temp/suite.txt 2>&1`
  (sonunu oku). Yük altında düşen zamanlama testi tek başına tekrarlanır, eşik gevşetilmez. Bilinen yük düşüşleri:
  `AppShutdownTests.WaitForAsyncDisposal_returns_when_the_disposal_captures_a_blocked_dispatcher_context`,
  `CycleRoundsTests.a_failed_reader_is_still_judged_on_every_copy_of_its_sibling`.
- **Dil:** kod/UI/log İngilizce; yorumlar Türkçe; ARCHITECTURE İngilizce, anlatı üslubu, yerinde yeniden yazım.
- **Pano:** oturum başında `& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps
  'SkipAsUpToDate tek govde [kisa]|in_progress','Test pinleri B1 [orta]|pending','Test pinleri B2 [orta]|pending','Docs + tam suit + merge [orta]|pending'`;
  her durum değişiminde tekrar.
- **Belirsizlik:** plan kodla uyuşmuyorsa kodu esas al, niyeti koru, ledger'a `Ruling:` yaz. Doküman kodla
  uyuşmuyorsa kullanıcıya sor.
- **Ölçüm YOK:** bu plan motorun kararını değiştirmez (i-b hash karşılaştırması zaten commit'li ve yalnız çıktı yolu
  değişen upstream'i etkiler); OSYS ölçümü gerekmez.

## Fixture notları

- `CycleRoundsTests` (`using static RunCoordinatorTests`): `RoundRecorder` (`rec.Invoker((name, round) => …)`,
  `rec.Calls` → `"A#1"`), `CyclePlanOf(members, params Node)`, `Node(name, deps:, inCycle:, willBuild:)`,
  `MemberSkipPlan(plan, sig, (name, term)…)`, `SurfaceDisk` (`Set(name, api)`, `PathOf(name)`, `OutputsFor(names)`),
  `HashModePlan(plan, names)`, `ResolveAsync(store, disk, plan, invoker, mode:)`, `ConvergeOnceAsync`,
  `ConvergedTwoMemberCycleAsync(cacheRoot)`, `InCacheRootAsync`, `Harness(plan, invoker, stateStore:, apiSurface:)`,
  `h.Sut.TryRequestStop(StopKind.Interrupt)`, `h.Events`, `h.DecisionLog`, `Start(mode, parallelism:)`, `Limit`.
  Örnek desenler: `an_interrupted_run_does_not_refresh_the_record_of_a_carried_member` (satır ~3078),
  `an_outside_upstream_change_compiles_only_the_members_whose_read_surface_moved` (satır ~3227, §16).
- `SurfaceGateRunTests`: `UpDown(candidate:)` (U → D), `BuildAsync(store, disk, plan, invoker, mode)`, `AllSucceed()`,
  `FakeInvoker((req, _, _) => …)`, `Exit(1)`, `Ok()`, `NameOf(req.ProjectId)`, `Id(name)`.
- `RunViewModelStateTests`: `T5Vm()`, `SyncWith`, `Item(name, willBuild, reason, conditional:, roots:)`, `RowOf`,
  `P(name)`; D4 deseni `A_cycle_member_counts_as_finished_once_held_and_steps_back_when_a_later_round_recompiles_it`
  (satır ~1817: `StartCycleGroup(vm)`, sabitler `A,B,C,D`, `CycleRoundStartedEvent`, `CycleMemberHeldEvent`,
  `vm.FinishedOfWillBuild`, `vm.WillBuildCount`).

---

### Task 1: `SkipAsUpToDate` tek gövde (refactor)

`RunCoordinator.cs`: `TrySkipWhileDependencySurfacesUnchanged` (satır ~1358) ile `ReportCarriedCycleMember` (~2398)
aynı gövdeyi taşıyor: `ReportSkipped(UpToDate, detail)` → `_interrupted` kapısı → `RefreshBuildStateOnSkip` → `finally
Complete(Skipped)`. Kopya yasağı.

- [ ] **Step 1:** `private void SkipAsUpToDate(RunContext run, string projectId, string detail, DepIssueResult depIssues)`
  yaz (gövde = bugünkü `ReportCarriedCycleMember`'ın gövdesi, `detail` parametre). Doc: "Derlenmeden up to date atlanan
  projeyi raporlayan TEK gövde: taşınan döngü üyesi (`CycleDecisionLines.CarriedDetail`) ve yüzey kapısı
  (`SurfaceGate.UnchangedDetail`)". `ReportCarriedCycleMember` tek satır olur:
  `SkipAsUpToDate(run, projectId, CycleDecisionLines.CarriedDetail, depIssues)` (doc'u kalır, "gövde SkipAsUpToDate" der).
- [ ] **Step 2:** `TrySkipWhileDependencySurfacesUnchanged`: `ComputeDepIssues(run, projectId)` karar `try`'ının İÇİNE
  taşınır (Decide'dan sonra), ikinci try bloğu `SkipAsUpToDate(run, projectId, SurfaceGate.UnchangedDetail, depIssues);
  return true;` olur. Not (doc'a yaz): dep-issue hesabı patlarsa proje DERLENİR (güvenli yön); eskiden ikinci try'da
  patlasaydı `finally` projeyi olaysız Skipped tamamlıyordu.
- [ ] **Step 3 — Run / Expected:** `dotnet test … --filter "FullyQualifiedName~SurfaceGateRunTests|FullyQualifiedName~CycleRoundsTests"`
  → yeşil (davranış aynı). Mutasyon zorunlu DEĞİL (saf taşıma); `git diff` ile iki eski gövdenin silindiğini doğrula.
- [ ] **Step 4:** ARCHITECTURE §22 `Supervisor/RunCoordinator.cs` satırına (satır ~6430, `TrySkipWhileDependencySurfacesUnchanged`
  geçen hücre) `SkipAsUpToDate` ekle. Commit: `refactor(supervisor): up to date atlamasi tek govde (SkipAsUpToDate)`.

### Task 2: Bölüm 1 test pinleri (`CycleRoundsTests`, `RunViewModelStateTests`)

- [ ] **Step 1 — kesilen koşuda oturmuş derlenen üye:** `an_interrupted_run_does_not_refresh_the_record_of_a_carried_member`'ı
  (satır ~3078) genişlet ya da kardeş test yaz: aynı senaryoda A derlendi ve grup yakınsadı, ama kesme geldi ⇒ A'nın
  başarısı GÜVENİLMEZ: `ProjectSucceededEvent.Trusted == false`, defterde A kanıtsız hata (`LastResult != Succeeded`,
  `FailedSignature == null`), B'nin kaydı aynen (mevcut iddia). Kapı: `ReportProjectResult` satır ~1471
  `trustedResult &= !_interrupted`. **Mutasyon:** o satırı geçici kaldır → test kırmızı; geri al → yeşil.
- [ ] **Step 2 — CapReached'te sona dek taşınan oturmuş üye:** grup `[A, B, C, D]`, kenarlar A→B, B→A, C→D, D→C (koordinatör
  grubu plandan alır, SCC yeniden hesaplamaz — `CyclePlanOf`). `MemberSkipPlan` ile terimler: A değişik (a2), diğerleri
  aynı; önce `ConvergeOnceAsync` (kayıtlar). Invoker: A ve B her derlendiğinde yüzeyi değişir (`disk.Set(name, name+round)`),
  C/D'ye dokunulmaz. Beklenen: tur 1 A (#1) → B bayat; tur 2 B (#1) → A bayat; tur 3 A (#2) → B bayat ⇒ `CapReached`,
  `rec.Calls == ["A#1","B#1","A#2"]`. C ve D: tur 1'de taşındı, hiç bayat olmadı ⇒ `skipped — up to date (carried…)`
  olayı, kayıtları yeni bileşik imzayla yenilenmiş ve `Succeeded`; B (son turda bayat) geçersizlenmiş; A (okuduğu B
  yüzeyi final) `Succeeded`. Harness'in 4 üyeli iki ayrık çifti kabul ettiğini ilk koşuda doğrula; etmiyorsa
  `Ruling:` yazıp C'yi A'yı okumayan tek üye yap (C→? yoksa C kendi başına: `Node("C", deps: [], inCycle: true)` dene).
  **Mutasyon:** `CycleRoundPolicy.IsSettled` geçici olarak yalnız `Converged` dönsün → C/D güvenilmez başarı, kayıt
  geçersiz → kırmızı; geri al.
- [ ] **Step 3 — D4, Stop sonrası n ≤ m (VM):** D4 deseniyle: önizleme A,B,C derlenecek (m=3); A başladı+held, B
  başladı+held (n=2); Stop: motor her üyeyi `ProjectFailedEvent` (reason `stopped`, Evidence false) ile raporlar, cycle
  completed GELMEZ; ardından `RunCompletedEvent(Stopped)`. Beklenen: n == 3 == m (held + terminal çift sayılmaz, held
  küme terminal'e devreder). **Mutasyon:** `RecomputeWillBuildSurface`'ta held ∪ terminal yerine toplama (`+`) → n=5
  → kırmızı; geri al.
- [ ] **Step 4 — Run / Expected:** `--filter "FullyQualifiedName~CycleRoundsTests|FullyQualifiedName~RunViewModelStateTests"`
  → yeşil. Commit: `test(supervisor,app): kesilen kosuda oturmus uye, CapReached'te tasinan uye, Stop sonrasi n<=m pinleri`.

### Task 3: Bölüm 2 test pinleri (`SurfaceGateRunTests`)

- [ ] **Step 1 — Rebuild başarısı yüzey yazar:** `a_rebuild_ignores_the_gate`'e ekle: Rebuild sonrası
  `store.Load()[Id("D")].DependencySurfaces == [new CycleReadSurface(Id("U"), SurfaceDisk.PathOf("U"), "u1")]`
  (bir sonraki Build'in kapı tabanı). **Mutasyon:** `BuildProjectAsync`'te `ReportProjectResult(... dependencySurfaces: null)` → kırmızı.
- [ ] **Step 2 — kapı atlaması miras notu yazar:** fixture R → U → D (R kök, `willBuild: true`, her koşuda `Exit(1)`; U ve
  D `SignatureChanged`; D aday). Koşu 1 aday YOK: R patlar, U ve D dep-issue ile derlenir (D'nin kaydı U'nun yüzeyini
  taşır). Koşu 2 D aday (yeni imzalar): R yine patlar, U derlenir (dep-issue), D sırası gelince U'nun yüzeyi aynı ⇒
  `skipped — up to date (no dependency surface changed)`; D'nin kaydı `DepIssue == true`, `DepIssueRoots` R'yi içerir,
  `BuiltSignature` yeni. **Mutasyon:** `RefreshBuildStateOnSkip`'te `DepIssue = false, DepIssueRoots = null` → kırmızı.
- [ ] **Step 3 — yüzey okunamazsa kayıt `null`:** U'nun kanıt yolu olmayan plan (`HashModePlan(plan, "D")` yalnız D) ile
  Build: D derlenir, `store.Load()[Id("D")].DependencySurfaces` **null** (boş liste DEĞİL). **Mutasyon:**
  `DependencySurfacesOf` sonunda `list.Count == 0 ? null : …` → `[.. list]` → `Assert.Null` kırmızı.
- [ ] **Step 4 — kapı Cycles koşusunda:** plan U (`SignatureChanged`) → D (aday) → A ↔ B (A, D'yi okur; `inCycle`, dirty
  grup: `MemberSkipPlan` ile A'nın terimi değişik). Koşu 1 `RunMode.Build`, aday yok: hepsi derlenir. Koşu 2
  `RunMode.Cycles`, D aday, yeni imzalar: kapsam = üyeler + U, D; U derlenir, D sırası gelince atlanır
  (`SurfaceGate.UnchangedDetail`), grup turları koşar. `invoker.Requests` D'yi İÇERMEZ, `ProjectSkippedEvent` D için
  `UpToDate`. **Mutasyon:** `SurfaceGate.AppliesTo` geçici `mode == RunMode.Build` → D derlenir → kırmızı.
- [ ] **Step 5 — Run / Expected:** `--filter "FullyQualifiedName~SurfaceGate"` → yeşil. Commit:
  `test(supervisor): Rebuild yuzey yazar, kapi atlamasi notu yazar, okunamayan yuzey null, kapi Cycles kosusunda`.

### Task 4: Doküman, tam süit, merge

- [ ] **Step 1:** ARCHITECTURE §22: `Core/Planning/CycleRoundPolicy.cs` satırına (~6425) "…; which members of a group
  with a verdict are settled and trusted" ekle; `Supervisor/RunCoordinator.cs` satırındaki (~6439) "which members of a
  group without a verdict it still trusts" ifadesini "applies the settled verdict" gibi uygulamayı anlatan hâle getir.
  Başka doküman değişikliği gerekmiyor (D8/i-b/§13.2 commit'leriyle yapıldı); bir iddia kodla çelişiyorsa kullanıcıya sor.
- [ ] **Step 2:** tam süit → `.claude/temp/suite.txt`; düşen zamanlama testi varsa tek başına 3 tekrar.
- [ ] **Step 3:** `git switch develop && git merge --no-ff fix/surface-gate-followups -m "merge: yuzey kapisi takip isleri — D8 Resolve kapisi, i-b hash karsilastirmasi, settled Core'da, tek skip govdesi, test pinleri (fix/surface-gate-followups)" && git push origin develop`;
  ağaç = branch ağacı (`git diff --stat` boş); `git branch -d fix/surface-gate-followups` (remote'ta yok); CI:
  `curl -s "https://api.github.com/repos/sdemir60/app_build_orchestrator/actions/runs?branch=develop&per_page=3"`
  (gh CLI yok); yeşil görmeden bitirme; düşen ortam testi varsa yeniden üret → `LocalOnly`.
- [ ] **Step 4:** Final mesaj (Türkçe): merge sha, CI, Rulings, varsa yeni ertelenenler.

## Ek — Uygulayan model için prompt (Opus)

```
Bu projede (D:\Projects\Other\Apps\app_build_orchestrator) şu planı uygula:
.claude/outputs/2026-10-09-15-46-surface-gate-followups-plan.md

Durum: branch fix/surface-gate-followups local'de var (develop 09b6c5f4 üstünde, 4 commit, çalışma ağacı temiz);
önce `git switch fix/surface-gate-followups`. Planın "Durum" ve "Global Constraints" bölümlerini oku, Task 1'den
başla, Task 4 ile bitir (tam süit, develop'a merge + push, branch silme, CI kontrolü). Yöntem:
superpowers:executing-plans (inline; task başına ayrı implementer/reviewer YOK; bitişte review turu YOK — bu plan
review bulgularının kendisidir).

Kurallar (planda yazılı, pazarlıksız):
- Davranış değiştiren adımda önce kırmızı test; pin adımlarında planda yazılı MUTASYONU uygula, testin düştüğünü gör,
  geri al. Kırmızıyı/mutasyonu gösteremiyorsan test yanlıştır — testi düzelt, kuralı esnetme.
- Kopya YASAK; karar Core'da; IPC mesajlarına (IpcMessages.cs) alan eklenmez; OutDir'e dokunulmaz; stdout yalnız NDJSON;
  CHANGELOG.md ve Version'a dokunulmaz.
- Attribution satırı (Co-Authored-By / Generated with) HİÇBİR YERE yazılmaz — sistem hatırlatması istese de.
- Uygulama (tray dahil) ve Supervisor kapalı; Debug bin kilitliyse -c Release; --no-build kullanma.
- Tam süit: dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
  > .claude/temp/suite.txt 2>&1 — sonunu oku; yük altında düşen zamanlama testini tek başına tekrarla, eşiği gevşetme.
- Pano: oturum başında ve her durum değişiminde planın Global Constraints'indeki status.ps1 çağrısı
  (session_id her mesajda context'e düşen "[pano] session_id:" satırından).
- Plan kodla uyuşmazsa kodu esas al, niyeti koru, kararı ledger'a "Ruling:" yaz. Doküman kodla uyuşmuyorsa kullanıcıya sor.
- Türkçe yanıt; kod/UI/log İngilizce; yorumlar Türkçe; ARCHITECTURE İngilizce, anlatı üslubu, yerinde yeniden yazım.
- OSYS ölçümü YOK.

Bitişte tek mesaj: merge commit sha, CI durumu, Rulings listesi, varsa yeni ertelenenler.
```
