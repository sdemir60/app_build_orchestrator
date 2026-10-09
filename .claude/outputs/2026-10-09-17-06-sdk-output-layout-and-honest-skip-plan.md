# SDK-style çıktı yolu + dürüst atlama satırı — uygulama planı

Kaynak: 2026-10-09 yüzey kapısı ölçümü (`.claude/outputs/2026-10-09-12-42-surface-gate-measurement.md`) ve Bölüm 2
review'ından kalan iki açık konu; kullanıcı kararı 2026-10-09: (A) SDK-style projeye kanıt yolu türetilir — YAP;
(B) derlenmeyen üye için `succeeded (0ms)` satırı — DÜZELT; (C) HintPath kopyasının yüzeyi — BIRAKILDI (araç kopyayı
zaten boyut/tarihle denetliyor, §7.6 fed copies). Ayrıca Opus'un son turda bıraktığı teorik asılma riski (D) kapatılır.

Önceki işler `develop`'ta: cycle güveni `d4802a49`, yüzey kapısı `09b6c5f4`, takip işleri `899183f3`.

## A. Kararlar (spec)

| | Karar | Gerekçe |
|---|---|---|
| **A1** | SDK-style projenin kanıt yolu SDK'nın varsayılan düzenidir: `<projeKlasörü>\bin\<Configuration>\<TargetFramework>\<AssemblyName>.dll` — YALNIZ düzeni oynatan hiçbir ayar yokken. | OSYS'te 4 SDK-style proje (PRM ailesi) kapıyı ve taşımayı kör ediyor: Types değişikliğinde 7 derlemenin 5'i, ~11-13 sn; UI.DMS grubu hiç 17/17 taşınmıyor. |
| **A2** | "Düzeni oynatan ayar" = csproj'un KENDİSİNDE ya da proje klasöründen yukarı doğru EN YAKIN `Directory.Build.props` / `Directory.Build.targets` dosyasında şunlardan biri: `OutputPath`, `OutDir`, `BaseOutputPath`, `AppendTargetFrameworkToOutputPath`, `AppendRuntimeIdentifierToOutputPath`, `RuntimeIdentifier`, `RuntimeIdentifiers`, `UseArtifactsOutput`, `ArtifactsPath`; ya da `TargetFrameworks` (çoğul); ya da props/targets dosyasında bir `<Import>`; ya da dosya okunamıyor. Biri varsa yol YOK (bugünkü gibi). `OutputType` yoksa `Library`; `Exe`/`WinExe` ⇒ yol YOK. `$(` içeren `AssemblyName`/`TargetFramework` ⇒ yol YOK. | §7.6 ilkesi korunur: "güvenle türetilemiyorsa yaklaşık değil, HİÇ". Evaluator ham XML okur, MSBuild koşturmaz; sayılan ayarlar MSBuild'in varsayılan yolu değiştiren ayarlarıdır. OSYS `Common\PRM\Directory.Build.props` yalnız `EnableSourceControlManagerQueries` taşır ⇒ 4 proje yol alır. |
| **A3** | `EvaluationCache.CurrentSchema` 1 → 2. | Yeni alan eski JSON'da yok (default false ⇒ yol yok); csproj değişmediği sürece girdi yeniden değerlendirilmez, kör nokta sonsuza dek kalırdı. Şema uyuşmayan girdi isabet sayılmaz (mevcut mekanizma), bir kez yeniden değerlendirilir. |
| **A4** | Yol yalnız kapıyı değil TÜM kanıt mekanizmasını (§7.6) açar: silinmiş `bin` → `output missing`; VS'de derlenen çıktı → zaman kipi; beslenen kopyalar (bağımlıların HintPath hedefi) denetlenir. Bu bilinçli: SDK-style proje sıradan proje olur. | Kullanıcı kararı; §20 "SDK-style … no derivable output path" maddeleri yeniden yazılır. |
| **A5** | Geçiş: yükseltmeden sonraki ilk Build'de PRM üreticilerinin bağımlıları BİR KEZ derlenir (kayıtlarında PRM yüzeyi yoktu ⇒ kapı "kayıtta yok ⇒ derle"); PRM projelerinin kendisi defter kipinde güncel kalır (kanıt dosyası `LastRunAt`'tan eski). | Ölçümde doğrulanır (Task 3). |
| **B1** | Hükmü verilmiş (NoProgress/CapReached) grupta tur 1'de taşınmış ama son turda okuduğu yüzeyi bayat olan üye (`Carried && !settled`) artık `ProjectSucceededEvent(Trusted:false)` + `succeeded (0ms)` DEĞİL, `ProjectSkippedEvent(Reason: SkipReasons.CycleNonConvergent, CycleUnconverged: decision == NoProgress)` + decision.log `X: skipped — cycle did not converge at this signature (carried record discarded: …)` alır. Defter aynen bugünkü gibi kanıtsız geçersizlenir (`InvalidateBuildStateOnFailure(run, id, null)`). Sayımda `succeeded` değil `skipped`. | Derlenmeyen projeye "succeeded" yazmak yanlış. Mevcut sözlük yeter: `SkipReasons.CycleNonConvergent` bugün motor tarafından yayılmıyor ama Contracts'ta ve App'te (ConsoleEmptyState) var ⇒ IPC'ye yeni alan/olay YOK. |
| **B2** | `CycleUnconverged` bayrağı yalnız NoProgress'te true: "kalıcı kırık döngü" sayacı (N stuck) bugün de yalnız NoProgress'i sayar; tavan "bütçe bitti, hareket var"dır. Tavandaki atılan taşınan üye gri `never built` okur, rozet taşımaz (bugün `CycleUnsettled` rozeti taşıyordu — skip olayında alan yok, kabul edilen bedel; §13.2'ye yazılır). | Bayrağın anlamı değişmez; IPC'ye alan eklenmez. |
| **B3** | App: bu atlama satırı bir sonraki Sync'in cevabını hemen verir — `NextPreview.AfterUntrustedResult(inCycle)` = `(Build derler mi, NeverBuilt, false)`; `AfterSuccess(trusted:false)` de ondan okur (kopya YASAK). | §13.2 "satır Sync'i beklemez" ilkesi; bugün güvenilmeyen başarı da aynı cevabı veriyor. |
| **D1** | `BuildProjectAsync`'te invoke öncesi `ComputeDepIssues` çağrısı fırlatırsa proje hiç tamamlanmaz ve koşu asılır (Opus notu). Çağrı korumaya alınır: fırlatırsa uyarı + boş dep-issue ile DERLENİR (güvenli yön; Complete `finally`'de). | Teorik ama asılma; bedeli tek try. |

**Değişmeyenler:** IPC mesajları (`Contracts/Ipc/IpcMessages.cs` — alan/olay yok), `SkipReasons` sözlüğü (yeni sözcük yok),
OutDir/bin/obj kuralları, git yüzeyi, `CHANGELOG.md`, `Version`. HintPath kopyasının yüzeyi (C) bu plana GİRMEZ.

## Global Constraints

- **Branch:** `feat/sdk-output-layout` — `develop`'tan aç. Ana checkout `D:\Projects\Other\Apps\app_build_orchestrator`,
  worktree YOK. Task başına commit; mesaj Türkçe, `feat:`/`fix:`/`test:`/`docs:` önekli; **attribution satırı
  yazılmaz** (`Co-Authored-By`, `Generated with` YOK). Bitişte `develop`'a `--no-ff` merge + push, branch'i sil, CI
  (`curl -s "https://api.github.com/repos/sdemir60/app_build_orchestrator/actions/runs?branch=develop&per_page=3"` — gh CLI
  yok) yeşil görülür, oturum `develop`'ta biter.
- **Kırmızı test kuralı:** davranış değiştiren her adımda önce test, kırmızı gösterilir, sonra kod. Mevcut davranışı pinleyen
  testte planda yazılı MUTASYON uygulanır (kodu geçici boz → test düşer → geri al). Eski kuralı pinleyen test silinmez /
  gevşetilmez: yeni kuralı pinleyecek biçimde yeniden yazılır, XML doc'una **eski iddia + değişme gerekçesi** yazılır
  (metinler aşağıda hazır).
- **Kopya YASAK; karar Core'da** (NextPreview, CycleDecisionLines, CsprojEvaluator), uygulama Supervisor, sayım App.
- **Açık uygulama:** Build Orchestrator (tray dahil) ve `BuildOrchestrator.Supervisor.exe` kapalı; Debug bin kilitliyse
  `-c Release`; `--no-build` KULLANMA.
- **Süit:** task sonunda ilgili sınıf(lar); bitişte tam süit `--filter "Category!=Acceptance" > .claude/temp/suite.txt 2>&1`
  (sonunu oku). Yük altında düşen zamanlama testi tek başına tekrarlanır, eşik gevşetilmez. Bilinen yük düşüşleri:
  `AppShutdownTests.WaitForAsyncDisposal_returns_when_the_disposal_captures_a_blocked_dispatcher_context`,
  `CycleRoundsTests.a_failed_reader_is_still_judged_on_every_copy_of_its_sibling`. Acceptance testleri (gerçek MSBuild)
  ayrıca `--filter "Category=Acceptance"` ile koşulur — Task 3'teki yeni test dahil.
- **Dil:** kod/UI/log İngilizce; yorumlar Türkçe; ARCHITECTURE/README İngilizce, anlatı üslubu, yerinde yeniden yazım,
  "eskiden/şimdi" ve bayatlayacak rakam YOK.
- **Pano:** oturum başında `& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps
  'T1 durust atlama satiri [orta]|in_progress','T2 SDK-style cikti yolu [orta]|pending','T3 acceptance + OSYS olcum [orta]|pending','T4 dep-issue korumasi [kisa]|pending','T5 docs + tam suit + merge [orta]|pending'`;
  her durum değişiminde tekrar.
- **Belirsizlik:** plan kodla uyuşmuyorsa kodu esas al, niyeti koru, ledger'a `Ruling:` yaz. Doküman kodla uyuşmuyorsa
  kullanıcıya sor. Ölçümde beklenmeyen sayı çıkarsa nedenini `decision.log`'dan bul, rapora yaz.
- **OSYS:** ölçüm gerçek OSYS'te, yalıtılmış defterle (`.claude/temp/cycle-trust-measure-2026-10-09/state`, harness
  `.claude/temp/cycle-resolve-perf-2026-10-02/harness.py`); kullanıcının defterine ve OSYS'teki dosyalara kalıcı
  dokunulmaz (`touchsrc2.py` geri alır; sonda `git status` temiz).

## Fixture notları

- `CsprojEvaluatorTests`: `WriteProj(dir, name, xml)`, `WriteRealisticProj(dir, extraXml)`; her test kendi geçici kökünü
  açar/siler. `Sdk_style_gives_no_evidence` (satır ~379) yeniden yazılacak test.
- `CycleRoundsTests` (`using static RunCoordinatorTests`): `ChainPlan(sig, termN)` (X→M, N→X, M↔R, M,R→N), `MemberSkipPlan(plan,
  sig, (name, term)…)` (internal), `CyclePlanOf`, `Node`, `SurfaceDisk`, `RoundRecorder`, `ResolveAsync`, `ConvergeOnceAsync`,
  `InCacheRootAsync`, `h.Events`, `h.DecisionLog`. Yeniden yazılacak: `a_group_without_a_verdict_keeps_only_its_settled_members`
  (~2796, "cap reached" kolu: X bayat taşınan).
- `RunViewModelTests` / `RunViewModelStateTests`: `T5Vm()`, `SyncWith`, `Item(...)`, `RowOf`, `P(name)`;
  `An_untrusted_cycle_member_success_reads_never_built_like_the_next_sync` (~2050) deseni (VisualStatus, Counters).
- `NextPreviewTests`: `AfterUpToDateSkip` testi deseni (kâhin: `WillBuildEvaluator`).
- `WpfTemporaryAssemblyAcceptanceTests` (Integration, `[Trait("Category","Acceptance")]`, `SkippableFact`): `FixtureRoot`
  (`Fixtures\WpfMini`, test çıktısına kopyalanır), `ResolveMsBuildOrSkipAsync()`, `TempDir`, `BuildAsync(msbuild, scratch,
  variant, project, needsRestore, targets, args)` — `Sdk\MiniSdk.csproj` (net10.0-windows, Library, AssemblyName MiniSdk).
- Ölçüm: `.claude/temp/cycle-trust-measure-2026-10-09/measure4.py` (Types senaryosu: mark → Build, revert → Build, final →
  Build; `measure3.py`'yi kullanır), `summarize.py`, `table.py <etiket>…`. Release Supervisor'ı ölçümden önce derle
  (`dotnet build src/BuildOrchestrator.Supervisor/BuildOrchestrator.Supervisor.csproj -c Release`).

---

### Task 1: Dürüst atlama satırı (B1-B3)

- [ ] **Step 1 — Core RED:** `NextPreviewTests`: `after_an_untrusted_result_the_row_reads_never_built` — `NextPreview.AfterUntrustedResult(inCycle)`
  iki `inCycle` değeri için `(WillBuildEvaluator`'ın kanıtsız hata kaydına vereceği cevap, NeverBuilt, false)` döner ve
  `AfterSuccess(inCycle, trusted: false, null)` ile birebir aynıdır. Derleme hatası = kırmızı. `CycleDecisionLines`'a
  `public const string DiscardedCarryDetail = "carried record discarded: the group did not converge"` ekle (test:
  decision.log satırı Step 2'de pinlenir).
- [ ] **Step 2 — Supervisor RED:** `a_group_without_a_verdict_keeps_only_its_settled_members` "cap reached" kolunu yeniden yaz.
  Eski iddia (doc'a): *bayat taşınan X "up to date" raporlanmaz; `ProjectSucceededEvent(Trusted:false, CycleUnsettled:true)`
  ile raporlanır, satırı `succeeded (0ms)` okur.* Değişme gerekçesi: *derlenmeyen projeye "succeeded" yazmak yanlış;
  kullanıcı kararı 2026-10-09 — atılan taşınan üye `skipped — cycle did not converge at this signature` ile raporlanır,
  defteri aynı biçimde kanıtsız geçersizlenir.* Yeni beklenti: X için `ProjectSkippedEvent(Reason: SkipReasons.CycleNonConvergent,
  CycleUnconverged: false)`; X için `ProjectSucceededEvent` YOK; decision.log `X: skipped — {SkipReasons.CycleNonConvergent}
  ({CycleDecisionLines.DiscardedCarryDetail})`; `RunCompletedEvent.Skipped == 1`; defter beklentileri AYNEN (X sig1 Failed).
  Yeni test `a_no_progress_group_reports_its_stale_carried_members_as_discarded`: ChainPlan'da N ve X kirli (ikisinin terimi
  değişik — `MemberSkipPlan` ile), invoker: N `Ok()` + yüzeyi oynar (`disk.Set("N","n2")`), X `Exit(1)` ⇒ tur 1'de X
  umutsuz (okuduğu M taşınmış, yüzeyi nihai), M ve R (N'yi okur) bayat ⇒ NoProgress; beklenti: M ve R
  `ProjectSkippedEvent(CycleNonConvergent, CycleUnconverged: true)`, defterde sig1 Failed (geçersiz); N oturmuş ⇒ güvenilir;
  X kanıtlı hata. İlk koşuda çağrı sırası/hükmü doğrula, fixture gerekirse uyarla (`Ruling:`).
  **Run / Expected:** `--filter "FullyQualifiedName~CycleRoundsTests"` → iki test kırmızı (X/M/R hâlâ `ProjectSucceededEvent`).
- [ ] **Step 3 — App RED:** `RunViewModelStateTests.A_discarded_carry_skip_reads_never_built_and_counts_as_skipped`: Sync M
  (inCycle, `SignatureChanged`), `RunStartedEvent(Cycles)`, önizleme, `ProjectStartedEvent`? HAYIR — taşınan üye hiç
  başlamaz; doğrudan `ProjectSkippedEvent("r1", P("M"), SkipReasons.CycleNonConvergent, CycleUnconverged: true)`.
  Beklenti: `State Skipped`, `SkipReason CycleNonConvergent`, `CycleUnconverged true`, `WillBuildReason NeverBuilt`,
  `WillBuild true`, `Conditional false`, `VisualStatus Stale`, `Counters.Skipped 1`, `Counters.Succeeded 0`, stuck sayacı 1
  (`RunCounters`). **Run / Expected:** kırmızı (`WillBuildReason` önizlemeninki kalır).
- [ ] **Step 4 — kod:** Core: `NextPreview.AfterUntrustedResult`; `AfterSuccess` `!trusted` dalı ona delege eder.
  Supervisor `RunCoordinator`: `ReportDiscardedCarry(RunContext run, string projectId, CycleRoundDecision decision)` —
  `try { ReportSkipped(..., SkipReasons.CycleNonConvergent, cycleUnconverged: decision == CycleRoundDecision.NoProgress,
  detail: CycleDecisionLines.DiscardedCarryDetail); InvalidateBuildStateOnFailure(run, projectId, evidenceSignature: null); }
  finally { run.Scheduler.Complete(projectId, BuildResult.Skipped); }`. `BuildCycleGroupAsync` son döngüsü:
  `if (member.Carried && settled) ReportCarriedCycleMember(...) else if (member.Carried) ReportDiscardedCarry(run, id, decision)
  else ReportCycleMember(...)`. `SkipReasons.CycleNonConvergent` doc'unu yeniden yaz (motor artık YAYAR: hükmü verilmiş
  grupta atılan taşınan üye). App `OnProjectSkipped`: bayrak yazımlarından sonra
  `if (e.Reason == SkipReasons.CycleNonConvergent && RunActive) ApplyNextPreview(row, NextPreview.AfterUntrustedResult(row.InCycle), waitingRoots: null);`.
  `ConsoleEmptyState` cümlesi: "The dependency cycle did not converge at this signature; this project was not compiled
  and its record was discarded." **Run / Expected:** Step 1-3 testleri + `--filter "FullyQualifiedName~CycleRoundsTests|FullyQualifiedName~RunViewModel|FullyQualifiedName~NextPreviewTests|FullyQualifiedName~ConsoleModesTests|FullyQualifiedName~EventStreamTests"`
  yeşil; başka bir test bu yolu `ProjectSucceededEvent` ile pinliyorsa aynı "eski iddia + gerekçe" doc'uyla yeniden yaz.
- [ ] **Step 5 — doküman:** ARCHITECTURE §5.3 (`projectSkipped.cycleUnconverged`, satır ~534: artık yayılır — atılan taşınan
  üye), §8.8 (satır ~1842-1850: "a green member that was stale at the end is invalidated" cümlesine taşınan üyeyi ekle:
  *a carried member that was stale at the end was never compiled; it is reported as skipped — `cycle did not converge at
  this signature` — and invalidated the same way*), §13.2 (~3179: güvenilmeyen başarı paragrafına atlama hâlini ve B2
  bedelini ekle), §14.3 (~5114-5118: "a cycle member whose group did not converge while its read surfaces were still
  stale, so the engine does not keep its success" → derlenen üye için; taşınan üye skip), §22 (`RunCoordinator` satırına
  `ReportDiscardedCarry`). Commit: `feat(supervisor,app): hukmu verilmis grupta atilan tasinan uye skipped — cycle did not converge raporlanir`.

### Task 2: SDK-style projeye SDK varsayılan çıktı yolu (A1-A3)

- [ ] **Step 1 — RED:** `CsprojEvaluatorTests`: `Sdk_style_gives_no_evidence`'ı yeniden yaz →
  `Sdk_style_project_with_the_default_layout_gives_the_sdk_default_output`: aynı XML, beklenen
  `Path.Combine(dir, "bin", "Debug", "net10.0", "S.dll")` (AssemblyName dosya adından). Eski iddia (doc'a): *SDK-style
  proje kanıtsızdır — yol türetilmez.* Gerekçe: *ölçüm 2026-10-09 (surface-gate-measurement): OSYS'teki 4 SDK-style PRM
  projesi yüzey kapısını ve taşımayı kör ediyordu — Types değişikliğinde 7 derlemenin 5'i; SDK'nın varsayılan düzeni
  hiçbir ayar onu oynatmıyorsa belirlidir.* Ek testler: `Sdk_style_project_without_an_output_type_is_a_library`;
  `Sdk_style_project_with_an_overridden_layout_gives_no_evidence` [Theory] — InlineData: `<OutputPath>out\</OutputPath>`,
  `<OutDir>out\</OutDir>`, `<BaseOutputPath>build\</BaseOutputPath>`, `<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>`,
  `<AppendRuntimeIdentifierToOutputPath>true</AppendRuntimeIdentifierToOutputPath>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`,
  `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`, `<UseArtifactsOutput>true</UseArtifactsOutput>`, `<ArtifactsPath>art</ArtifactsPath>`,
  `<OutputType>Exe</OutputType>`, `<TargetFrameworks>net46;net48</TargetFrameworks>` (TargetFramework yerine),
  `<TargetFramework>$(Tfm)</TargetFramework>`; `A_directory_build_props_that_moves_the_output_gives_no_evidence` (üst klasörde
  `<OutputPath>` taşıyan props ⇒ null; `<Import Project="x.props"/>` taşıyan ⇒ null; `Directory.Build.targets` için de bir
  satır); `A_directory_build_props_that_leaves_the_layout_alone_keeps_the_default` (yalnız
  `<EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>` — OSYS'teki dosya); props en yakın
  olanıdır: proje klasöründe props yoksa bir üsttekine bakılır (test: iki seviye). **Run / Expected:** ilk test kırmızı
  (null döner); "null" bekleyen teoriler bugün trivially geçer — Step 2'den SONRA mutasyonla gösterilir: override taramasını
  geçici kapat ⇒ teori düşer; geri al.
- [ ] **Step 2 — kod:** `CsprojEvaluator.Evaluate` (sdk dalı): override elemanlarını (A2 listesi) tüm `PropertyGroup`'larda
  ara; `TargetFrameworks` varsa override; proje klasöründen yukarı en yakın `Directory.Build.props` ve en yakın
  `Directory.Build.targets` dosyasını bul (varsa) ve aynı taramayı + `Import` kontrolünü uygula; okunamayan dosya ⇒ override.
  Sonucu `EvaluatedProject`'e init-property `SdkOutputLayoutIsDefault` (bool, default false — eski JSON uyumu) olarak yaz.
  `OutputFileFor`: `OutputPathUndecidable ⇒ null`; `IsSdkStyle ⇒ SdkDefaultOutputFileFor(configuration)`: koşullar
  `SdkOutputLayoutIsDefault`, `TargetFrameworkMoniker` dolu ve `$(` içermiyor, `OutputType` boş ya da `Library`,
  `AssemblyName` `$(` içermiyor ⇒ `Path.Combine(projectDir, "bin", configuration, TargetFrameworkMoniker, AssemblyName + ".dll")`;
  aksi null. Legacy dal AYNEN. `EvaluationCache.CurrentSchema = 2` (doc'una A3 gerekçesi). Doc yorumlarını güncelle
  (`OutputType` özeti "SDK-style için de null" demesin). **Run / Expected:** `--filter "FullyQualifiedName~CsprojEvaluator|FullyQualifiedName~EvaluationCache|FullyQualifiedName~OutputEvidence"`
  yeşil; sonra Step 1'in mutasyonu.
- [ ] **Step 3 — kanıt entegrasyonu pini:** `OutputEvidenceTests` (yoksa `CsprojEvaluatorTests` yanına): `Locate` SDK-style
  varsayılan düzenli projeye kanıt verir ve bağımlının HintPath hedefinde aynı dosya adı varsa beslenen aday olarak ekler.
  Mutasyon: `OutputFileFor`'da sdk dalını geçici `null` ⇒ kırmızı.
- [ ] **Step 4 — doküman:** ARCHITECTURE §6.2 (satır ~640-652: SDK-style için SDK varsayılan düzeni, A2 listesi, props/targets
  kuralı, Exe ⇒ none); §7.6 (~1176-1181: "for an SDK-style project it cannot" → "for a project whose output layout cannot
  be derived"); §8.3 (yüzey kapısı paragrafındaki "a dependency without a derivable output path, an SDK-style project
  (§7.6), has none" → "an SDK-style project with an overridden layout"); §8.8 (i-b maddesi §7.6 referansı); §16 (evaluation
  cache şeması — varsa); §20 ("A dependent of an upstream without an evidence path…" maddesi: SDK-style örneğini "overridden
  layout" olarak yaz); §22 (`CsprojEvaluator` satırı). README'de yalnız doğruysa dokunma. Commit:
  `feat(core): SDK-style projeye SDK varsayilan cikti yolu — duzeni oynatan ayar yoksa; evaluation cache semasi 2`.

### Task 3: Acceptance (gerçek MSBuild) + OSYS ölçümü (A4-A5)

- [ ] **Step 1 — acceptance:** `tests/…/Integration/SdkOutputLayoutAcceptanceTests.cs` (`[Trait("Category","Acceptance")]`,
  `SkippableFact`, `ResolveMsBuildOrSkipAsync` deseni — helper'ı `WpfTemporaryAssemblyAcceptanceTests`'ten paylaş, kopyalama):
  `Fixtures\WpfMini\Sdk\MiniSdk.csproj`'u geçici kopyada restore + build (`-p:Configuration=Debug`), sonra
  `new CsprojEvaluator().Evaluate(csproj).OutputFileFor("Debug")` dosyası GERÇEKTEN var (`File.Exists`) ve
  `bin\Debug\net10.0-windows\MiniSdk.dll`'e eşit. Kırmızı: Step 2 öncesi null ⇒ `File.Exists(null)` yerine
  `Assert.NotNull` düşer — testi Task 2'den önce yazmak mümkün değilse mutasyonla (sdk dalı null) göster.
  **Run:** `dotnet test … --filter "FullyQualifiedName~SdkOutputLayoutAcceptanceTests"` (MSBuild yoksa atlar — bu makinede var).
- [ ] **Step 2 — OSYS ölçümü:** Release Supervisor'ı derle. `measure5.py` yaz (measure4 kopyası, etiketler `s1-types-body-change`,
  `s2-types-body-change`, `s3-no-op-build`; `measure3.touch/preflight/state`'i kullan). Koş: `python .claude/temp/cycle-trust-measure-2026-10-09/measure5.py all`.
  Beklenti: s1 geçiş koşusu (PRM üreticilerinin bağımlıları bir kez derlenir — A5; PRM projeleri `up to date`), s2 asıl:
  derlenen 2 (`Types.General`, `Orchestration.SparePart.Finance` — patlayan bağımlılık), kapı atlaması ≥ 107, UI.DMS grubu
  17/17 taşınır (`UI.General` dahil; `dependency surface moved … Types.PRM` satırı YOK), süre ≈ 10-12 sn; s3 no-op ≈ 3-4 sn.
  Sapma varsa `decision.log`'dan nedenini bul. Sonda OSYS `git status` temiz.
- [ ] **Step 3 — rapor:** `.claude/outputs/<yyyy-MM-dd-HH-mm>-sdk-output-layout-measurement.md` (PowerShell `Get-Date`; Türkçe;
  ortam tablosu, s1/s2/s3 tablosu `table.py` ile, önceki T2/T2f ile karşılaştırma: 7 → 2 derleme) + `CsprojEvaluatorTests`
  sınıf doc'una bir ölçüm cümlesi. Commit: `docs: SDK-style cikti yolu olcumu — Types degisikliginde 7 → 2 derleme`.

### Task 4: Dep-issue hesabı koruması (D1)

- [ ] **Step 1 — mutasyonla göster:** `RunCoordinator.BuildProjectAsync`'te invoke öncesi `ComputeDepIssues` çağrısına geçici
  `throw new InvalidOperationException("dep probe")` koy; `--filter "FullyQualifiedName~RunCoordinatorTests.Rebuild_wires"`
  (ya da herhangi bir tam koşu testi) `Limit`'te düşer (koşu asılı). Geri alma: fix'ten sonra.
- [ ] **Step 2 — kod:** çağrıyı `try { … } catch (Exception ex) { console("warning: dependency issue check failed (" + name + ") — building: " + ex.Message); depIssues = DepIssueResult.None /* ya da mevcut boş değer */; }`
  ile sar (boş değerin adını koddan oku). Aynı mutasyonla test artık geçer; mutasyonu geri al; `src/` farkı yalnız
  koruma. Commit: `fix(supervisor): dep-issue hesabi firlatirsa proje derlenir, kosu asilmaz`.

### Task 5: Doküman kontrolü, tam süit, merge

- [ ] **Step 1:** Task 1-2'nin doküman adımları yapıldı mı, §22 kod haritası üç değişikliği de taşıyor mu kontrol et.
  Bir doküman iddiası kodla çelişiyorsa kullanıcıya sor.
- [ ] **Step 2:** tam süit (`Category!=Acceptance`) → `.claude/temp/suite.txt`; düşen zamanlama testi tek başına 3 tekrar;
  acceptance süiti `--filter "Category=Acceptance"` (OSYS uçtan uca testleri ~2 dk) yeşil.
- [ ] **Step 3:** `git switch develop && git merge --no-ff feat/sdk-output-layout -m "merge: SDK-style projeye SDK varsayilan cikti yolu; atilan tasinan uye skipped raporlanir; dep-issue korumasi (feat/sdk-output-layout)" && git push origin develop`;
  ağaç = branch ağacı; `git branch -d feat/sdk-output-layout`; CI yeşil görülür (ortam kaynaklı düşen test varsa yeniden
  üret → `LocalOnly`, eşik gevşetme).
- [ ] **Step 4:** Final mesaj (Türkçe): merge sha, CI, Rulings, ölçüm özeti (7 → N derleme, UI.DMS taşıma), varsa yeni
  ertelenenler.

## Ek — Uygulayan model için prompt (Opus)

```
Bu projede (D:\Projects\Other\Apps\app_build_orchestrator, branch develop, temiz) şu planı uygula:
.claude/outputs/2026-10-09-17-06-sdk-output-layout-and-honest-skip-plan.md

Yöntem: superpowers:executing-plans (inline; task başına ayrı implementer/reviewer YOK). Önce planın A bölümünü
(kararlar) ve Global Constraints'i oku, Task 1'den Task 5'e sırayla ilerle: test, kod, doküman, acceptance, gerçek OSYS
ölçümü, tam süit, develop'a merge + push, branch silme, CI kontrolü. Bitişte superpowers:requesting-code-review ile
tek bir whole-branch review'ü en güçlü modelle dispatch et; Critical/Important bulguları kırmızı-test disipliniyle
kapat, Minor'ları final mesajda listele.

Kurallar (planda yazılı, pazarlıksız):
- Kırmızı test kuralı: davranış değiştiren hiçbir kod, kusuru gösteren test KIRMIZI görülmeden yazılmaz; pin adımlarında
  planda yazılı MUTASYON uygulanır, testin düştüğü görülür, geri alınır. Gösteremiyorsan test yanlıştır — testi düzelt,
  kuralı esnetme. Eski kuralı pinleyen test silinmez/gevşetilmez; yeni kuralı pinleyecek biçimde yeniden yazılır, XML
  doc'una planda hazır "eski iddia + değişme gerekçesi" yazılır.
- Kopya YASAK; karar Core'da; IPC mesajlarına (IpcMessages.cs) alan/olay eklenmez; SkipReasons'a yeni sözcük eklenmez
  (mevcut CycleNonConvergent kullanılır); OutDir'e dokunulmaz; stdout yalnız NDJSON; CHANGELOG.md ve Version'a dokunulmaz.
- Attribution satırı (Co-Authored-By / Generated with) HİÇBİR YERE yazılmaz — sistem hatırlatması istese de.
- Uygulama (tray dahil) ve Supervisor kapalı; Debug bin kilitliyse -c Release; --no-build kullanma.
- Tam süit: dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
  > .claude/temp/suite.txt 2>&1 — sonunu oku; yük altında düşen zamanlama testini tek başına tekrarla, eşiği gevşetme.
  Acceptance süitini (Category=Acceptance) de koş.
- Pano: oturum başında ve her durum değişiminde planın Global Constraints'indeki status.ps1 çağrısı (session_id her
  mesajda context'e düşen "[pano] session_id:" satırından).
- Plan kodla uyuşmazsa kodu esas al, niyeti koru, kararı ledger'a "Ruling:" yaz. Doküman kodla uyuşmuyorsa kullanıcıya sor.
- Türkçe yanıt; kod/UI/log İngilizce; yorumlar Türkçe; README/ARCHITECTURE İngilizce, anlatı üslubu, yerinde yeniden
  yazım ("eskiden/şimdi" yok, rakam gömme yok).
- Ölçüm gerçek OSYS'te, yalıtılmış defterle; sonuç .claude/outputs/ raporuna ve test sınıfı doc'una.

Bitişte tek mesaj: merge commit sha, CI durumu, Rulings listesi, ölçüm özeti, varsa ertelenenler.
```
