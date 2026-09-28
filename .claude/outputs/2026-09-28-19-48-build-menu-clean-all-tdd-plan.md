# Build ▾ → Clean (tüm projeler) — TDD dökümü

- **Tarih:** 2026-09-28 · **Taban:** `main` (`35ddc41`) · **Branch:** `feat/build-menu-clean-all` (worktree `-ai`)
- **Kaynak:** `.claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md` (P2), envanter §9 #60 / §14-1

## Tasarım kararı (motor)

**Clean'in bağımlılık anlamı yoktur.** Tek yerde, Core'da: `Core/Planning/CleanRunScope.Of(plan)` Clean
koşusunun planını kurar — her düğümde kenarlar düşer (`Dependencies = []`), döngü işareti düşer
(`InCycle = false`, `Cycles = []`), `WillBuild = true` (önizleme BU koşunun işini anlatır: hepsi temizlenecek),
gerekçe (`WillBuildReason`) disk olgusu olarak korunur; sıra uyarıları (katman/üretici) düşer.
`RunCoordinator` bunu kapsamdan ÖNCE uygular → satır Clean'i de (tek düğüm) aynı kuraldan geçer.

Sonuç: döngü üyesi pre-skip edilmez, dairesel kenar kalmadığı için A6 kilitlenmesi yapısal olarak imkânsız,
her proje ilk anda hazır (paralellik tavanına kadar eşzamanlı), dep-issue hesaplanacak kenar yok.

## Bulgular (bloklayıcı → önemli → kozmetik)

| # | Seviye | Bulgu | Konum |
|---|---|---|---|
| B1 | bloklayıcı | Menü maddesi pasif, komuta bağlı değil | `BuildMenu.xaml.cs:151-159`, `:168-173` |
| B2 | bloklayıcı | Kapsamsız Clean'de döngü üyeleri "in dependency cycle" diye atlanıyor | `ReadySetScheduler.cs:95-99` (`groups` null) |
| B3 | önemli | `ScopeFor(Clean)` yok → Build'in kümesi (koreografi yanlış küme) | `RunViewModel.cs:1080-1085` |
| B4 | önemli | Kapsamsız Clean'de bağımlılık sırası bekleniyor; hatalı bağımlılık dependent'a dep-issue yapıştırıyor | `RunCoordinator.RunSegmentAsync` / `ComputeDepIssues` |
| B5 | önemli | Satır Clean'i bayat bağımlılığı dep-issue yapıyor (uyarı satırı + ▲) | `ProjectRunScope.Of` → `staleDependenciesById` |
| B6 | önemli | Clean önizlemesi plan bayrağına yazıyor: temizlenen döngü üyesi / dokunulmamış satır bir sonraki Build dalgasında yanar | `RunViewModel.OnBuildPreview:1933-1937` |
| B7 | kozmetik | Akış satırı Clean'de "Build started — N projects" diyor | `RunViewModel.Stream.cs:168` |
| B8 | kozmetik | Tooltip "on every solution" diyor; motor proje başına koşar | `AccessibilityNames.cs:74-75` |

## Testler (önce kırmızı)

| T | Bulgu | Test | Dosya |
|---|---|---|---|
| T1 | B2/B4 | `CleanRunScope.Of` kenarsız, döngüsüz, WillBuild=true, gerekçe korunur | `Planning/CleanRunScopeTests.cs` (yeni) |
| T2 | B2 | Tam Clean grafın her projesini temizler: sıradan, harici, döngü üyesi | `Supervisor/FullCleanRunTests.cs` (yeni) |
| T3 | B2 | Temizlenen her projenin defter kaydı silinir (döngü üyesi + harici dahil) | aynı |
| T4 | B4 | Tam Clean bağımlılık beklemez (dispatch plan sırası) | aynı |
| T5 | B4 | Hatalı Clean dependent'a dep-issue yapıştırmaz | aynı |
| T6 | B3 | Önizleme her projeyi bu koşunun işi sayar (kuyruk/payda) | aynı |
| T7 | B5 | Satır Clean'i bayat bağımlılığı dep-issue yapmaz | `Supervisor/SingleProjectRunTests.cs` |
| T8 | B1 | Madde etkin + hover; tooltip "not available yet" demez (eski pasif testi YENİDEN yazılır) | `App/BuildMenuTests.cs` |
| T9 | B1 | Menü maddesi kapsamsız `Mode=Clean` gönderir; pill CLEAN | `App/CleanAllCommandTests.cs` (yeni) |
| T10 | B1 | Kapı Build ile aynı (CanExecute + CanExecuteChanged) | aynı |
| T11 | B3 | `ScopeFor(Clean)` = tüm satırlar → koreografi hepsini işaretler | aynı |
| T12 | B6 | Temizlenen döngü üyesi sonraki Build dalgasına girmez; durdurulan Clean dokunmadığı satırın bayrağını bozmaz | aynı |
| T13 | B7 | Akış açılış satırı "Clean started — N projects, parallelism P" | aynı |
| T14 | B8 | Tooltip "every project" der | `App/BuildMenuTests.cs` |

**Doğrulanan (değişiklik gerekmez):** `ExternalUpdater.ShouldUpdate(Clean)=false` (mevcut test),
`ConditionalRebuild.AppliesTo` Clean'de false, `FailureEvidenceSignature` Clean'i dışlar (mevcut test),
Clean restore koşmaz (mevcut test).

## Doküman

ARCHITECTURE §8.1 (mod tablosu + "Clean is the third target"), §13.2 (Build menüsü paragrafı), pill satırı;
README Build/Rebuild adımı; Contracts `RunMode.Clean` dokümanı; `CleanWorkspaceCommand` dokümanındaki
`-t:Clean`/OutDir gerekçesinin yalnız bakım Clean'ine ait olduğu. `ReleaseNotes.cs`'e dokunulmaz.
