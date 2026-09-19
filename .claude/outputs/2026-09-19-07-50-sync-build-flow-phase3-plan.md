# Faz 3 — Dışarıdan derleme kredisi: uygulama planı (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-18-07-56-sync-build-flow-spec.md` §1 (kararlar 3-6, 20), §4
(etiket ve tooltip'ler), §5 (5.1-5.6), §7 (kaçak analizi), §9 son madde, §11 Faz 3. Faz 1 planının sonundaki görev
listesinin Faz 3 maddeleri (a-f) bu dökümün girdisidir. Faz 1 ve Faz 2 kararları (`.claude/summaries/2026-09-18-23-34-…`,
`2026-09-19-07-06-…`) geçerlidir.

**Hedef:** VS'de (ya da araç dışında herhangi bir yolla) derlenmiş bir projenin çıktısı bütün girdilerinden yeniyse
satır yeşil olur ve Build onu atlar; araç o çıktıyı deftere yazmaz, her Sync yeniden kanıtlar. Aracın kendi derlediği
projede karar bugünkü gibi içerik imzasıdır; iki ek veto gelir: derleme çıktısı yoksa `never built`, havuza kopyası
değişmişse `affected`.

**Mimari:** Kanıt yolları (derleme çıktısı, beslenen çıktılar, girdi klasörleri, HintPath hedefleri) Core'da saf
dosya okumasıdır (`Core/Incremental/OutputEvidence.cs`). Hangi kipte olunduğu (defter / zaman) ve kanıtın sonucu
proje başına bir `OutputCheck` değeridir; binder onu üretir, `WillBuildEvaluator` gerekçeye çevirir. Karar tek
yerden geçtiği için Sync önizlemesi ile Build'in pre-skip'i ayrışamaz. Beslenen çıktılar yalnız aracın kendi başarılı
derlemesinden öğrenilir ve deftere (`BuildState.FedOutputs`) yazılır.

## Preflight — kod ile plan / spec karşılaştırması (2026-09-19)

Referanslar `sync-build-flow` (`cb32ebe`) üzerinde tek tek doğrulandı.

| # | Ne | Kod ne diyor | Karar |
|---|---|---|---|
| P1 | CLAUDE.md değişmezi "DLL/bin timestamp asla okunmaz; boyut+mtime yalnız özet önbelleğinin anahtarıdır" (`CLAUDE.md:35-37`) | Spec §9 yalnız ARCHITECTURE ve README'yi sayıyor; Faz 3 tam olarak çıktı zamanını okuyor | **Kullanıcı kararı (2026-09-19):** değişmez Task 9'da yeniden yazılır: "Araç kendi derlediği projede yalnız kaynak içeriğine bakar; başkasının derlediği çıktıda tarihlere bakılır; çıktının tarihi tek başına 'güncel' demeye asla yetmez" |
| P2 | Spec "ARCHITECTURE §4 / §7.1 / §9.4'ün 'çıktı okunmaz' cümlesi" | Bugünkü §4 süreç topolojisi. Kural `ARCHITECTURE.md:39` (§1.2 tablosu), `:846`, `:903`, `:1235` ("§4 forbids…" eski numara), `:1366` (§9.4 "never touched and never read") | Task 9 bu beş yeri yeniden yazar; "§4" atıfları doğru bölüme çevrilir |
| P3 | `EvaluatedProject`'e yeni alan | `EvaluationCache` kaydı `EvaluatedProject`'i JSON'da saklar (`EvaluationCache.cs:16`), csproj değişmedikçe yeniden değerlendirmez (`:47-52`); `ResourceFiles` eski kayıtta boş gelir (`CsprojEvaluator.cs:32-35`) | Aynı yol burada **sessiz kayıptır**: eski kayıt kanıt yolu taşımaz → proje hiç kredi almaz, çıktı-yok vetosu hiç çalışmaz. `Entry`'ye SONA `int Schema = 0`; `Schema != Current` isabet sayılmaz, yeniden değerlendirilir |
| P4 | Spec §5.2 "defterdeki son **başarılı** aracın derlemesinden (`LastRunAt`)" | `LastRunAt` başarısızlıkta da yazılır (`BuildStateStore.cs:179`, `RunCoordinator.cs:1633`, `:1913`); §5.5 çökme kurtarması tam buna dayanır ("derleme kanıtı `LastRunAt`'tan eski kalır") | Karşılaştırma sonuca bakmadan `LastRunAt`'a karşıdır. §5.2'nin gerekçe cümlesi ("aracın kendi çıktısı her zaman `LastRunAt`'tan eskidir") iki durumda da doğrudur; §5.5 ancak böyle çalışır. `LastRunAt == null` ⇒ zaman kipi (kanıtlanamayan sahiplik) |
| P5 | "SDK-style / multi-target projede yol güvenle türetilemiyorsa kanıt yok" | OSYS: 177 csproj'un 173'ü legacy, hepsi `'$(Configuration)\|$(Platform)' == 'X\|AnyCPU'` + `bin\X\`; 4'ü SDK-style; 3'ünde `OutputType` yok | SDK-style ⇒ kanıt yok. `OutputType` yok ⇒ kanıt yok (MSBuild varsayılanı `exe`'dir ama yanlış uzantı "çıktı yok" vetosuyla sonsuz derletirdi). Çözülemeyen koşullu grupta `OutputPath` varsa ⇒ kanıt yok. `$(` içeren yol / ad ⇒ kanıt yok |
| P6 | Enum `ExternalOsysPlatform` → ürün-bağımsız ad (karar 20) | `HintPathClassifier.Classify` üründe çağrılmıyor (yalnız testler); `HintPathClass` hiçbir dosyaya ya da IPC'ye yazılmıyor | Yeniden adlandırma güvenli: `ExternalPlatformBin`, `ClassificationReport.OsysPlatformCount` → `PlatformBinCount`. `ProjectModelsTests` JSON değeri yeni ada göre yeniden yazılır |
| P7 | Kabul testi `OsysIncrementalAcceptanceTests` Run 1: "state YOK → derlenebilir her şey derlenir" | Spec §7 satır 39: kayıt yok ⇒ zaman kipi ⇒ VS'nin önceden derlediği projeler **atlanır** | Davranış bilerek değişti: Run 1 `RunMode.Rebuild` olur (her şeyi derler ve deftere yazar), Run 2 aynen "hepsi atlanır" iddiasını taşır. Doc'a `[DEĞİŞEN KURAL — spec §5.2 / §7-39]` |
| P8 | Zaman kipinde yeşil satırın yaşı "derleme kanıtının zamanından" | `BuildPreviewItem.LastBuiltAt` "aracın son başarılı derlemesi"dir ve proje sayfası onu sha ile birlikte yazar (`ConsoleEmptyState.cs:152-160`: "Last successful build: 2h ago (a3f81c2)") | Yeni alan `OutputBuiltAt` (SONA, default `null`), yalnız `BuiltOutside`'ta dolu. `LastBuiltAt`'ın anlamı değişmez; aksi hâlde sayfa VS'nin derleme saatini aracın commit'iyle yan yana yazardı |
| P9 | Spec §7 satır 16 "VS'de derleme geçti, havuza kopya patladı → gri `affected`" | Spec'in "shared folder was replaced" tooltip'i yalnız §5.3'te (defter kipi) | Beslenen çıktı bozukluğu iki kipte de tek gerekçedir (`OutputReplaced`, `affected`); tooltip nötr yazılır: "Its copy in the shared folder does not match its build output" (spec §4'te bu gerekçenin tooltip'i yok; §5.3'teki cümle yalnız bir durumu anlatıyor) |
| P10 | İki kabul sınıfı aynı ağacı paralel derliyor (Faz 1 açığı, MSB3026) | Faz 3'te çıktı zamanı karara girer: paralel Rebuild, Incremental Run 2'nin projelerini zaman kipine iter | İki sınıf ortak `[Collection("OSYS acceptance (serial)")]`'a alınır (Task 10) |

## Claude kararları (itiraz edilebilir)

- **Kip:** kanıt yolu biliniyor ∧ (kayıt yok ∨ `LastRunAt` yok ∨ kanıt dosyası var ve zamanı `LastRunAt`'tan yeni) ⇒
  **zaman kipi**; kanıt yolu biliniyor ve geri kalan her durumda ⇒ **defter kipi**; kanıt yolu bilinmiyor ⇒ **kanıtsız**
  (bugünkü karar, hiçbir veto yok).
- **Gerekçeler (enum'a SONA dört değer):** `BuiltOutside` (zaman kipi, güncel, yeşil) · `OutputStale` (zaman kipi,
  girdi çıktıdan yeni — `modified` ya da `affected`) · `OutputMissing` (derleme kanıtı yok — `never built`) ·
  `OutputReplaced` (beslenen çıktı eksik, boyutu farklı ya da derleme kanıtından eski — `affected`).
- **Zaman kipi sırası:** kanıt yok → `OutputMissing`; kendi girdisi (dosya ya da taranan klasör) yeni → `OutputStale`
  (own); yalnız kendi HintPath hedefi yeni → `OutputStale` (dependency); beslenen çıktı bozuk → `OutputReplaced`;
  aksi `BuiltOutside`. Kırmızı yok; defter notları (`DepIssue`, `FailedSignature`) zaman kipinde okunmaz.
- **Defter kipi sırası:** bugünkü karar önce. `LastFailed` ve `NeverBuilt` aynen kalır. Diğerlerinde kanıt yoksa
  `OutputMissing`; `UpToDate`/`WaitingForDependency` iken beslenen çıktı bozuksa `OutputReplaced`. İçerik değiştiyse
  (`SignatureChanged`, `DepIssue`) gerekçe değişmez — zaten derlenecek, `modified`/`affected` ayrımı bugünkü kaynaktan.
- **`modified` ↔ `affected` zaman kipinde** kanıttan gelir (kendi girdisi yeni ⇒ `modified`); defter kipinde bugünkü
  kaynaktan. Tek yardımcı: `OutputEvidence.OwnFilesChanged(check, ledgerAnswer)` — Sync ve koşu önizlemesi aynı
  fonksiyonu çağırır. Sync'in "N changed" sayacı da aynı cevaptan sayılır.
- **Sync'in Fast geçişi** (yalnız "kendi dosyası değişti mi" ölçümü) kanıtsız bağlanır; kanıt yalnız Safe geçişine ve
  Build'in planına girer. Aksi hâlde havuzu bozulmuş bir proje "changed" sayılırdı.
- **Döngü grubu (§5.6):** grupta zaman kipinde bir üye varsa grubun tüm üyeleri zaman kontrolünden geçer; hepsi
  tazeyse hepsi `BuiltOutside`, değilse kendi kontrolünü geçemeyen üye kendi gerekçesini, geçen üye
  `OutputStale` (dependency) alır. Grubun hiçbir üyesi zaman kipinde değilse üyeler tek tek defter kipindedir.
- **HintPath hedefi çözümü:** mutlaksa aynen, göreliyse projenin klasörüne göre; `$(` içeriyorsa atlanır (çözülemez).
- **Beslenen çıktı adayları:** projeye grafta bağımlı olan (plan `Dependencies`'inde bu projeyi taşıyan) her projenin,
  dosya adı derleme kanıtının adıyla aynı HintPath hedefleri (tekil). Belirsiz üreticide kenar olmadığı için aday da
  yoktur (spec §7-44).
- **Öğrenme:** başarılı Build/Rebuild hedefinde (Clean değil), `PersistBuildStateOnSuccess` içinde; aday var, boyutu
  derleme kanıtıyla eşit, zamanı ondan en çok 2 s farklı ⇒ `FedOutputs`'a yazılır. Kanıt yoksa liste `null`.
  Başarısızlık yolları (`with` ile kısmi birleşim) listeyi korur.
- **Karşılaştırma kuralları:** "yeni" = kesin büyük (`>`); zaman kipinde kanıt girdiye **eşitse** tazedir. Klasör
  zamanı `Directory.GetLastWriteTimeUtc`. Okunamayan dosya/klasör yok sayılır (girdide) ya da "kanıt yok"tur (derleme
  kanıtında).
- **Maliyet:** girdi dosyalarının zamanı yalnız zaman kipindeki (ya da zaman grubundaki) projeler için okunur;
  defter kipinde yalnız derleme kanıtı ve beslenen çıktılar stat edilir.
- **`OutputBuiltAt`** (P8) yalnız `BuiltOutside`'ta dolar. Koşuda başarıyla biten satırda `null`'a çekilir (artık
  aracın çıktısı).
- **Proje sayfası:** `BuiltOutside` → "Up to date — built outside this tool." + kanıt satırı "Built outside this tool:
  5m ago"; `OutputMissing` → "{head} — its build output is missing."; `OutputReplaced` → "{head} — its copy in the
  shared folder does not match its build output."; `OutputStale` → "{head} — its files are newer than its build
  output." (own) / "{head} — a dependency's output is newer than its build output." (dependency).
- **Configuration değişimi** (`NextPreview.AfterConfigurationChange`): `OutputMissing` → `NeverBuilt`; diğer yeni
  gerekçeler → `SignatureChanged` (bugünkü düşüş). Bir sonraki Sync yeni configuration'ın kanıtını okur.
- **Tooltip metinleri** (spec §4 ve P9): "Up to date — built outside this tool 5m ago" · "Its own files are newer than
  its build output" · "Its own files are unchanged — a dependency changed" (zaman kipinde de aynı cümle) · "No build
  output known to this tool" (`OutputMissing` da) · "Its copy in the shared folder does not match its build output".

## Global Constraints

- Proje kuralları `CLAUDE.md`: **kırmızı test kuralı** — kod değişikliğinden ÖNCE testin KIRMIZI verdiği gösterilir;
  kırmızı **derleme hatası değil assertion hatası** olmalıdır (yeni API gerekiyorsa önce `throw new
  NotImplementedException()` gövdeli ya da nötr değer dönen iskelet derlenir, test assertion ile düşer). Davranış
  değişince eski test YENİ kuralı pinleyecek şekilde yeniden yazılır, doc'una `[DEĞİŞEN KURAL — spec 2026-09-18 §…]` +
  eski iddia + gerekçe. Eşik/bütçe gevşetmek YASAK. **Kopya YASAK / tek doğruluk kaynağı.** Kod/UI metinleri
  İngilizce, yorumlar Türkçe.
- Zaman testleri gerçek geçici dosyalarla ve `File.SetLastWriteTimeUtc` / `Directory.SetLastWriteTimeUtc` ile açık
  zamanlar verilerek yazılır — sleep yok, duvar saatine bağlı eşik yok (D8).
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıksa Debug bin kilitlenir → `-c Release`. Uzun testler (tam süit, Acceptance) **ön planda** koşulur.
  `StickyLayerHeaderClickTests` ekran kilitliyken düşer (ortam); ekran açıkken yeşil olmalı.
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` KULLANMA (UTF-8/CRLF bozar) — Edit/Write.
- Commit mesajı dosyaya yazılıp `git commit -F` ile atılır; Türkçe, ASCII. **Attribution satırı eklenmez.** Task
  başına commit.
- **Branch:** çatı `sync-build-flow`. Bu faz `sync-build-flow-phase3`'te yürür (çatıdan açılır, push'lanır). Faz yeşil
  olunca (tam süit + Acceptance) `--no-ff` ile çatıya merge + push; doğrulanınca faz branch'i local ve remote'tan
  silinir. **`main`'e merge YOK.** Oturum `main`'de biter.
- Sözleşme değişiklikleri: yeni alan **SONA, default'lu**; enum'a yeni değer **sona** (metin olarak yazılır).
  Eşitlik/hash'i elle yazılmış record'larda (`BuildState`, `BuildPreviewItem`) yeni alan `Equals` ve `GetHashCode`'a
  da girer.
- ARCHITECTURE.md anlatı üslubu: yerinde yeniden yazım, changelog yok, sayı gömme yok.
- Sıra: **bloklayıcı:** T1-T3 (zemin). **Önemli:** T4-T7. **Orta:** T0, T8. **Kapanış:** T9-T10.

---

### Task 0: Tasarım sürümü v1.22.0 (README metni)

**Dosyalar:** `.claude/outputs/2026-09-18-23-55-design-v1.21.0/README.md` kopyalanarak
`.claude/outputs/<tarih>-design-v1.22.0/README.md` (prototip kopyalanmaz; başta "prototip v1.19.0'dır, bu sürümde
yalnız metin değişti" notu).

**Değişen bölümler (yerinde):** §2.3 renk kuralı (yeşil = aracın derlediği ve içeriği aynı, **ya da** başkasının
derlediği ve çıktısı bütün girdilerinden yeni), §2.4 madde 4 karar etiketi (`up to date · 5m` VS'de derlenmişte de;
`never built` = "diskte çıktı yok ya da bu araç hiç derlemedi"; `affected` = bağımlılığı değişti **ya da** havuzdaki
kopyası çıktısıyla uyuşmuyor; tooltip örneklerine "Up to date — built outside this tool 5m ago", "Its own files are
newer than its build output", "Its copy in the shared folder does not match its build output"), §5 statü tablosu
(yeşil satırının kaynağı), §8 (v1.20.0 kararına ek: "çıktının tarihi tek başına güncel demeye yetmez; araç kendi
derlediğinde içerik, başkasının derlediğinde zaman kanıtı"), §9 `## v1.22.0` sürüm notu (neden: VS'de derlenen proje
araçta gri kalıyordu — kullanıcı isteği 2026-09-17).

Test yok (doküman). Commit: `docs(design): v1.22.0 - disaridan derleme kredisi ve cikti vetolari`.

### Task 1: Kanıt yolu — `OutputPath` / `OutputType`, HintPath hedefleri, önbellek şeması (Core)

**Mevcut kod:** `Core/Discovery/CsprojEvaluator.cs:18-36` (`EvaluatedProject`, `ResourceFiles` init-property
deseni), `:52-106` (`Evaluate`; `PropertyGroup` okuma `:60-70`, HintPath `:88-93`), `:109-112` (`Elements`/`Items`).
`Core/Discovery/EvaluationCache.cs:16` (`Entry`), `:47-52` (isabet). Testler: `tests/.../Discovery/CsprojEvaluatorTests.cs`,
`EvaluationCacheTests.cs`.

**Kural:**
- `EvaluatedProject`'e init-property'ler (eski JSON'da yoksa boş/null): `string? OutputType`, `string? DefaultPlatform`
  (`<Platform Condition=" '$(Platform)' == '' ">X</Platform>`), `IReadOnlyList<ConditionalOutputPath> OutputPaths`
  (`record ConditionalOutputPath(string? Configuration, string? Platform, string Path)`, belge sırasında; koşulsuz
  grup ⇒ ikisi `null`), `bool OutputPathUndecidable` (çözülemeyen bir koşulun altında `OutputPath` var).
- Koşul biçimleri (boşluk/tırnak toleranslı, büyük-küçük harf duyarsız): `'$(Configuration)|$(Platform)' == 'C|P'`,
  `'$(Configuration)' == 'C'`. Başka her biçim `OutputPathUndecidable`.
- `string? OutputFileFor(string configuration)` (tek hesap yeri): SDK-style / `OutputType` yok / tanınmayan
  `OutputType` / `OutputPathUndecidable` / `$(` ⇒ `null`. Platform = `DefaultPlatform ?? "AnyCPU"`. Belge sırasıyla
  uyan son `OutputPath` kazanır (MSBuild son-yazan-kazanır); hiçbiri uymazsa `bin\<cfg>\`. Uzantı: `Library` → `.dll`,
  `Exe`/`WinExe` → `.exe` (harf duyarsız). Sonuç `<proje klasörü>\<OutputPath>\<AssemblyName><uzantı>`, tam yol.
- `IReadOnlyList<string> HintPathTargets()` (tek çözüm yeri): her `RawHintPath.Raw` için mutlak yol (göreliyse proje
  klasörüne göre), `$(` içeren atlanır, tekil, sıralı.
- `EvaluationCache.Entry`'ye SONA `int Schema = 0`; `const int CurrentSchema = 1` (tek tanım); isabet
  `e.Schema == CurrentSchema` ister; yeni kayıt `CurrentSchema` ile yazılır.

**Testler (önce KIRMIZI; iskelet `OutputFileFor => null`, `HintPathTargets => []`):** `CsprojEvaluatorTests`:
`A_legacy_debug_group_gives_bin_debug_dll`, `The_release_group_is_picked_for_release`,
`An_unconditional_output_path_applies`, `A_later_matching_group_wins`, `No_output_path_falls_back_to_bin_configuration`,
`A_platform_other_than_the_default_is_ignored`, `Exe_and_WinExe_give_an_exe`, `No_output_type_gives_no_evidence`,
`Sdk_style_gives_no_evidence`, `An_unreadable_condition_carrying_an_output_path_gives_no_evidence`,
`A_property_in_the_output_path_gives_no_evidence`, `Hint_path_targets_resolve_relative_and_skip_properties`.
`EvaluationCacheTests`: `An_entry_from_an_older_schema_is_evaluated_again` (elle yazılmış eski JSON kaydı → evaluate
çağrılır, yeni alanlar dolu).

Doküman: Task 9. Commit: `feat(discovery): csproj cikti yolu ve HintPath hedefleri; onbellek semasi`.

### Task 2: Girdi klasörleri (Core)

**Mevcut kod:** `Core/Incremental/ProjectInputs.cs:43-71` (`Collect`), `:79-108` (`SweepFolder`),
`Core/Incremental/IncrementalRunBinder.cs:30`, `:55-59` (girdi toplama). Testler: `ProjectInputsTests`.

**Kural:** `SweepFolder` gezdiği klasörleri de toplar (proje klasörü dahil; `obj`/`bin` hariç, yürüyüş aynı —
ikinci bir yürüyüş YAZILMAZ). `ProjectInputs.CollectWithFolders(...) → (IReadOnlyList<ProjectInput> Files,
IReadOnlyList<string> Folders)`; `Collect` onun `Files`'ını döner (imza terimi değişmez). Binder klasörleri de tutar:
`FoldersOf(projectId)`.

**Testler (önce KIRMIZI):** `ProjectInputsTests`: `The_swept_folders_include_the_project_folder_and_subfolders_but_not_bin_or_obj`,
`Collect_still_returns_the_same_files` (mevcut testler aynen yeşil — imza değişmez).

Doküman: Task 9. Commit: `feat(incremental): girdi klasorleri ayni yuruyusten toplanir`.

### Task 3: Kanıt ve iki kip — `OutputEvidence` (Core)

**Mevcut kod:** `Contracts/Model/ProjectModels.cs:124-211` (`BuildState`), `Core/State/BuildStateStore.cs:101-103`
(`LastBuiltAtOf` deseni). Task 1-2 API'leri.

**Kural (`Core/Incremental/OutputEvidence.cs`, saf, yalnız dosya sistemi okur):**
- `record ProjectOutputs(string Evidence, IReadOnlyList<string> FedCandidates)`;
  `static ProjectOutputs? Locate(EvaluatedProject? project, string configuration, IEnumerable<EvaluatedProject> dependents)`
  — kanıt yolu `OutputFileFor`; adaylar bağımlıların `HintPathTargets()`'ından dosya adı eşit olanlar.
- `enum EvidenceMode { None, Ledger, Time }`; `enum TimeVerdict { Fresh, Missing, OwnNewer, DependencyNewer, FedBroken }`;
  `record OutputCheck(EvidenceMode Mode, bool EvidenceMissing, bool FedIntact, TimeVerdict? Time, DateTimeOffset? EvidenceAt)`.
- `static OutputCheck Inspect(ProjectOutputs? outputs, BuildState? state, IReadOnlyList<string> inputFiles,
  IReadOnlyList<string> inputFolders, IReadOnlyList<string> hintTargets)` — kip ve kontroller "Claude kararları"ndaki
  gibi; `FedIntact` = `state?.FedOutputs`'taki her dosya var ∧ boyutu kanıta eşit ∧ zamanı kanıttan eski değil.
- `static OutputCheck TimeCheck(...)` — `Inspect`'in zaman kolu, döngü grubu için ayrıca çağrılabilir (tek gövde).
- `static IReadOnlyDictionary<string, OutputCheck> ApplyCycleGroups(IReadOnlyDictionary<string, OutputCheck> checks,
  IReadOnlyList<IReadOnlyList<string>> cycles, Func<string, OutputCheck> timeCheckOf)`.
- `static IReadOnlyList<string>? LearnFedOutputs(ProjectOutputs? outputs)` — 2 s penceresi tek sabit
  (`FedOutputWindow`).
- Önizleme yardımcıları: `static bool? OwnFilesChanged(OutputCheck? check, bool? ledgerAnswer)`,
  `static DateTimeOffset? OutputBuiltAt(OutputCheck? check)` (yalnız zaman kipi ve `Fresh`).
- Bu task'ta `BuildState.FedOutputs` yoktur; test için Task 4'ten önce gerekiyorsa alan bu task'ta eklenir (Contracts,
  SONA, default `null`, `Equals`/`GetHashCode`) — persist Task 4'tedir.

**Testler (önce KIRMIZI; iskelet `Inspect` → `Mode=None`):** `OutputEvidenceTests` — her biri spec §7 satırını adıyla
taşır: `Built_by_the_tool_and_untouched_is_ledger_mode` (1), `Built_elsewhere_after_the_tool_is_time_mode_and_fresh`
(3, 4), `A_reverted_file_newer_than_the_output_is_own_newer` (5), `A_failed_build_elsewhere_leaves_the_old_output_stale`
(15), `A_failed_copy_to_the_shared_folder_breaks_the_fed_output` (16), `A_release_build_in_the_shared_folder_breaks_the_fed_output`
(17, 18), `A_new_file_touches_the_folder` (21, 22), `A_newer_dependency_output_is_dependency_newer` (23 zaman kipi),
`An_output_written_before_the_crash_recovery_stays_ledger_mode` (32, 33: kanıt < `LastRunAt`), `No_record_is_time_mode`
(39, 40), `A_missing_output_under_a_record_is_missing`, `An_unknown_evidence_path_is_mode_none`,
`A_null_last_run_is_time_mode`, `Equal_times_are_fresh`. `ApplyCycleGroups`: `One_member_built_elsewhere_puts_the_group_in_time_mode`
(43), `A_fully_fresh_group_is_fresh`, `A_group_with_no_time_member_is_left_alone`. `Locate`: `Candidates_come_from_dependents_hint_paths_with_the_same_file_name`,
`An_ambiguous_producer_has_no_candidates` (44). `LearnFedOutputs`: `Same_size_and_time_within_two_seconds_is_learned`,
`A_different_size_or_time_is_not_learned`, `No_evidence_learns_nothing`.

Doküman: Task 9. Commit: `feat(incremental): cikti kaniti, iki kip ve dongu grubu`.

### Task 4: Beslenen çıktıların öğrenilmesi (Contracts + Supervisor)

**Mevcut kod:** `Supervisor/RunCoordinator.cs:42-47` (`IncrementalPlan`), `:1281-1284` (persist çağrısı),
`:1820-1842` (`PersistBuildStateOnSuccess`), `:1855-1937` (`InvalidateBuildStateOnFailure`, kısmi birleşim).
`Supervisor/Program.cs:171-209` (`ComputeIncremental`). `Core/Incremental/IncrementalRunBinder.cs:39-60`.

**Kural:**
- Binder her düğüm için `ProjectOutputs` hesaplar (`OutputsById`, plan `Dependencies`'inden bağımlı listesi) — Task
  3'ün `Locate`'i, ikinci bir hesap yok.
- `IncrementalPlan`'a SONA `IReadOnlyDictionary<string, ProjectOutputs>? OutputsById = null` (Task 5 aynı kayda
  `ChecksById` ekler).
- `PersistBuildStateOnSuccess`: `FedOutputs: OutputEvidence.LearnFedOutputs(inc.OutputsById?.GetValueOrDefault(projectId))`.
  Clean dalı değişmez (kayıt silinir).
- `BuildState.FedOutputs` (Task 3'te eklenmediyse burada) SONA, default `null`, `Equals`/`GetHashCode`.

**Testler (önce KIRMIZI):** `RunCoordinatorTests` (FakeInvoker başarı sırasında kanıt dosyasını ve bir havuz kopyasını
aynı zamanla yazar): `A_success_records_the_shared_copies_it_fed`, `A_copy_of_another_size_is_not_recorded`,
`A_failure_keeps_the_recorded_copies`, `A_clean_forgets_them_with_the_record`. `BuildStateStoreTests`:
`Fed_outputs_round_trip_and_an_old_record_reads_null`. `ProjectModelsTests`: `BuildState` eşitliği listeyi içerikle
karşılaştırır.

Doküman: Task 9. Commit: `feat(engine): basarili derleme havuza besledigi kopyalari deftere yazar`.

### Task 5: Kararın kendisi — gerekçeler, değerlendirici, planlayıcı (Contracts + Core)

**Mevcut kod:** `Contracts/Model/ProjectModels.cs:103-122` (`WillBuildReason`), `Core/Planning/WillBuildEvaluator.cs:72-95`,
`Core/Planning/BuildPreview.cs:11-21`, `Core/Incremental/IncrementalPlanner.cs:81-87,95-210`,
`Core/Incremental/IncrementalRunBinder.cs:97-103`. Testler: `WillBuildTests`, `IncrementalPlannerTests`,
`IncrementalRunBinderTests`, `IpcMessagesTests`.

**Kural:**
- `WillBuildReason`'a SONA: `BuiltOutside`, `OutputStale`, `OutputMissing`, `OutputReplaced` (doc'ları "Claude
  kararları"ndaki anlamla).
- `WillBuildEvaluator.EvaluateWithReason(..., OutputCheck? output = null)`: `null` / `Mode=None` ⇒ bugünkü karar;
  `Ledger` ⇒ bugünkü karar + iki veto; `Time` ⇒ zaman kararı. `WillBuild` = `reason != UpToDate && reason !=
  BuiltOutside` (kapsam dışı döngü üyesi yine `false`). `Evaluate` delege etmeye devam eder.
- `BuildPreview.ComputeWillBuild(..., Func<string, OutputCheck?>? outputOf = null)`,
  `IncrementalPlanner.ComputeWillBuild[WithSignatures](..., IReadOnlyDictionary<string, OutputCheck>? outputs = null)`
  — yalnız aktarır; imza hesabı DEĞİŞMEZ (kanıt imzaya girmez).
- `IncrementalRunBinder`: `IReadOnlyDictionary<string, OutputCheck> ChecksFor(IReadOnlyDictionary<string, BuildState> state)`
  (Task 3'ün `Inspect` + `ApplyCycleGroups`; zaman kolu yalnız gereken projelerde girdi stat eder) ve
  `Bind(state, buildCycles, mode, IReadOnlyDictionary<string, OutputCheck>? outputs)`.

**Testler (önce KIRMIZI):** `WillBuildTests`: `A_ledger_record_without_its_output_is_output_missing`,
`A_broken_shared_copy_turns_up_to_date_into_output_replaced`, `A_proven_failure_stays_red_even_without_output`,
`A_changed_signature_is_not_overridden_by_the_vetoes`, `Time_mode_fresh_is_built_outside_and_skipped`,
`Time_mode_never_reads_red` (kayıtta eşleşen `FailedSignature` olsa da), `Time_mode_own_newer_is_output_stale`,
`Mode_none_is_todays_decision` (mevcut tablo testleri kanıtsız çağrıyla aynen yeşil). `IncrementalPlannerTests`:
`The_evidence_never_enters_the_signature`, `A_cycle_group_in_time_mode_is_decided_as_one`.
`IncrementalRunBinderTests`: `Input_times_are_read_only_for_time_mode_projects` (sayaçlı stat sahtesi yerine: zaman
kipindeki projede girdi değişince sonuç değişir, defter kipindekinde değişmez). `IpcMessagesTests`: dört yeni değer
metin olarak yazılır ve okunur.

Doküman: Task 9. Commit: `feat(planning): disaridan derleme kredisi ve cikti vetolari karara girdi`.

### Task 6: Sync ve Build aynı kararı okur (Contracts + Core + Supervisor)

**Mevcut kod:** `Core/Workspace/SyncWorkspaceService.cs:265-342` (`ComputeWillBuild`: Safe/Fast `:291-292`, own
changed `:298-301`), `:159-170` (önizleme). `Supervisor/Program.cs:187-199`, `RunCoordinator.cs:798-820` (pre-skip —
DEĞİŞMEZ, `WillBuild==false` okur), `:909-922` (koşu önizlemesi). `Contracts/Ipc/IpcMessages.cs:514-552`
(`BuildPreviewItem`). Kabul: `tests/.../Integration/OsysIncrementalAcceptanceTests.cs:64-…` (Run 1).

**Kural:**
- `BuildPreviewItem`'a SONA `DateTimeOffset? OutputBuiltAt = null` (+ `Equals`/`GetHashCode`).
- Sync: `checks = binder.ChecksFor(state)`; Safe geçişi `checks` ile, Fast geçişi `null` ile bağlanır. `OwnChanged` =
  `OutputEvidence.OwnFilesChanged(check, fast.WillBuild == true) == true`. Önizleme `OwnFilesChanged` ve `OutputBuiltAt`'ı
  aynı yardımcılardan yazar.
- Supervisor: `ComputeIncremental` `checks`'i planla bağlar; `IncrementalPlan`'a SONA `ChecksById`. Koşu önizlemesi
  `OwnFilesChanged: OutputEvidence.OwnFilesChanged(check, BuildStateStore.OwnFilesChanged(...))`,
  `OutputBuiltAt: OutputEvidence.OutputBuiltAt(check)`.
- Zaman kipinde güncel proje Build'de `SkipReasons.UpToDate` ile atlanır ve deftere YAZILMAZ (pre-skip yolu zaten
  yazmaz — pinlenir).
- `OsysIncrementalAcceptanceTests` Run 1 → `RunMode.Rebuild` (P7), doc'a `[DEĞİŞEN KURAL]`.

**Testler (önce KIRMIZI):** `SyncWorkspaceServiceTests` (geçici repo, legacy csproj, elle yazılan `bin\Debug\X.dll`):
`A_project_built_elsewhere_reads_built_outside_with_its_output_time`, `A_project_built_elsewhere_is_not_counted_as_changed`,
`A_recorded_project_whose_output_was_deleted_reads_output_missing`, `A_project_without_derivable_output_is_decided_as_today`.
`RunCoordinatorTests` / `SupervisorIpcTests`: `A_project_built_elsewhere_is_skipped_as_up_to_date_and_not_recorded`,
`The_run_preview_carries_the_output_time`. `IpcMessagesTests`: `OutputBuiltAt` round-trip; eski satır alansız çözülür.

Doküman: Task 9. Commit: `feat(sync): sync ve build ayni cikti kanitini okur`.

### Task 7: Satırın dili (App)

**Mevcut kod:** `App/Controls/StandingStatus.cs:30-41`, `App/ViewModels/DecisionLabel.cs:79-121`,
`App/Console/ConsoleEmptyState.cs:54-57,113-129,152-160`, `Core/Planning/NextPreview.cs:80-83`,
`App/ViewModels/RunViewModel.cs:96-101` (row alanları), `:1557-1566` (`SetConfiguration`), `:1853-1869`
(`OnBuildPreview`), `:2058-2062` (başarı), `App/Views/ProjectRow.xaml.cs:299-301,494-497`,
`App/ViewModels/RunViewModel.Workspace.cs:705-708` (`DecisionKeys`).

**Kural:**
- `StandingStatuses.From`: `BuiltOutside` → `Current`; diğer üç yeni gerekçe → `Stale` (varsayılan dal; açıkça
  yazılır).
- `DecisionLabel.For(..., DateTimeOffset? outputBuiltAt = null)`: `BuiltOutside` → `up to date` + yaş (`outputBuiltAt`),
  tooltip "Up to date — built outside this tool {age} ago"; `OutputMissing` → `never built`, "No build output known to
  this tool"; `OutputStale` → own ise `modified`/`modified · local` "Its own files are newer than its build output",
  değilse `affected` "Its own files are unchanged — a dependency changed"; `OutputReplaced` → `affected` "Its copy in
  the shared folder does not match its build output". İki çağıran (`ProjectRow`, `DecisionKeys`) yeni argümanı geçer.
- `ProjectRowViewModel.OutputBuiltAt` (`[ObservableProperty]`); `OnBuildPreview` yazar; başarıda `null`; `ProjectRow`
  değişim listesine girer.
- `ConsoleEmptyState`: "Claude kararları"ndaki cümleler; `RepeatsReason` `OutputMissing`'i de kapsar.
- `NextPreview.AfterConfigurationChange`: `OutputMissing` → `NeverBuilt`.

**Testler (önce KIRMIZI):** `StandingStatusTests`: dört yeni gerekçe. `DecisionLabelTests`: her yeni gerekçenin sözcük,
kuyruk, tooltip ve `Stale` değeri; `Built_outside_age_comes_from_the_output_time_not_the_last_build`.
`ConsoleModesTests`: yeni cümleler; `A_row_built_outside_says_so_instead_of_the_last_build`.
`NextPreviewTests`: `A_missing_output_stays_never_built_after_a_configuration_change`. `RunViewModelTests`:
`A_preview_with_an_output_time_reaches_the_row`, `A_successful_build_clears_the_output_time`. Realize testi gerekmez
(yeni XAML yok).

Doküman: Task 9. Commit: `feat(ui): built outside etiketi, cikti vetolarinin sozcukleri`.

### Task 8: Ürün-bağımsız enum adı (Contracts + Core)

**Mevcut kod:** `Contracts/Model/ProjectModels.cs:9`, `Core/Graph/HintPathClassifier.cs:8,11-13,17,25,35,46,68-76`,
`tests/.../Contracts/ProjectModelsTests.cs:24-25`, `tests/.../Graph/HintPathClassifierTests.cs:19,26`.

**Kural:** `ExternalOsysPlatform` → `ExternalPlatformBin`; `OsysPlatformCount` → `PlatformBinCount`; yorumlardaki
"OSYS platform DLL'i" → "havuzdaki platform DLL'i" (`HintPathClassifier.cs:69-70`, `OptimizeWorkspaceService.cs:272`).

**Testler (önce KIRMIZI):** `ProjectModelsTests`: JSON değeri `"externalPlatformBin"` (`[DEĞİŞEN KURAL — spec §1-20]`:
eski iddia `"externalOsysPlatform"`; ad ürün adı taşıyordu). Kaynak guard'ı `NoProductNameInCodeTests`: `src`'de
`Osys` geçen tanımlayıcı yok (yorum ve test verisi hariç; regex yalnız `enum`/sınıf/üye adlarına bakar) — önce
kırmızı.

Commit: `refactor(graph): HintPath sinifi urun adindan arindi`.

### Task 9: Dokümanlar, CLAUDE.md ve yorum süpürmesi

- **ARCHITECTURE.md** (yerinde yeniden yazım): §1.2 tablo satırı (`:39` — "karar kaynaktan; aracın çıktısı için defter,
  başkasının çıktısı için zaman kanıtı; çıktının tarihi tek başına güncel demez"), §6.2 (çıktı yolu okuması, önbellek
  şeması), §6.4 (`:581` sınıf tablosu yeni adla), §7 (yeni alt bölüm **Output evidence**: iki kip, kanıt dosyaları,
  beslenen çıktılar, döngü grubu, vetolar; §7.4 tri-state'e yeni gerekçeler), §7.5 (`FedOutputs`), §8.1 (`:846`,
  `:903` "§4 forbids" cümleleri), §8.8 (`:1235`), §9.4 (`:1366` "never read" → OutDir'e dokunulmaz ve MSBuild'e
  geçilmez; araç yalnız csproj'un kendi çıktı yolundaki dosyanın zamanını ve boyutunu okur), §14.3 (etiket ve
  tooltip'ler), §16 (`build-state.json` alanı, `evaluation-cache.json` şeması), §20 (spec §7 sınırları 19, 20, 31, 47),
  §22 kod haritası (`OutputEvidence.cs`).
- **README.md:** `:5` ve `:63` ("never from output timestamps"), adım 2 Sync renk paragrafı (`:174-182`: VS'de
  derlenen proje yeşil, `never built`/`affected`'ın yeni anlamları), "Reading the list" (`:277`, "built outside this
  tool").
- **CLAUDE.md değişmezleri** (`:35-37`, P1): "Değişti mi" cümlesi → "Araç kendi derlediği projede yalnız kaynak
  içeriğine bakar; başkasının (ör. VS'nin) derlediği çıktıda tarihlere bakılır; çıktının tarihi tek başına 'güncel'
  demeye asla yetmez. Sürüm kontrolü karara girmez; kaynak dosyanın boyut+mtime bilgisi içerik kararında yalnız özet
  önbelleğinin anahtarıdır."
- **Yorum süpürmesi:** `git grep -n -i "timestamp" -- src` ve `git grep -n "§4" -- src`: çıktı zamanının hiç
  okunmadığını iddia eden yorumlar yeni kurala göre yazılır (`WillBuildEvaluator.cs:21`, `BuildStateStore.cs:13,146`,
  `IncrementalPlanner.cs:221`, `IncrementalRunBinder.cs:18`, `SourceHashCache.cs:13`, `RunCoordinator.cs:60`, test
  yorumları `RunCoordinatorTests.cs:1534`, `CycleRoundsTests.cs:154,433`, `BuildStateStoreTests.cs:32`).

Commit: `docs: disaridan derleme kredisi dokumanlara islendi`.

### Task 10: Kapanış — guard'lar, tam süit, Acceptance, merge

- Kaynak guard'ları: `NoGitMutationOutsideTheWriterTests`, `NoWorktreeSurfaceTests`, `NoProductNameInCodeTests`,
  token/motion/D8, `NoTurkishUserText` yeşil.
- İki kabul sınıfı ortak `[Collection("OSYS acceptance (serial)")]` (P10; tanım testlerin ortak collection
  dosyasında, tek yerde).
- Tam süit ön planda: `dotnet test … --filter "Category!=Acceptance"`; ardından `--filter "Category=Acceptance"`
  (uygulama kapalıyken). Kabul kanıt dosyasına zaman kipinde atlanan proje sayısı yazılır (bilgi, iddia değil).
- Final branch review (bütün faz), bulgular tek fix dispatch'i.
- Merge: `sync-build-flow-phase3` → çatı `sync-build-flow` (`--no-ff`), push; doğrulanınca faz branch'i local ve
  remote'tan silinir. `main`'e merge YOK. Kullanıcıya çatıda neyi deneyeceğinin kısa listesi. Oturum `main`'de biter.
