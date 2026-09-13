# TFVC desteğini kaldır + graf kimliğini Id'ye çevir

Branch: `feat/git-only-vcs` · worktree: `D:\Projects\Other\Apps\app_build_orchestrator-ai`

Kullanıcı kararı (2026-09-12): **TFVC desteği tamamen kalkıyor** — UI'dan da arka plandan da. Yalnız git
kalacak. Ayrıca harici kart/graf kusurunun kök nedeni olan "düğüm kimliği = ad" kuralı **Id**'ye çevrilecek.

## Neden (ölçüm)

Kullanıcı bir git (`CustomerProject\DoganTrend`) ve bir TFVC (`Rent\OSYS.RentACar`) harici kart ekledi. TFVC
kartının projeleri grafta görünmedi, yerlerinde boşluk açıldı. Tanı (`ExternalRootsDiagnosticsTests`,
kullanıcının gerçek `ui-state.json`'ı ile) şunu gösterdi:

- Core temiz: 191 csproj, 14 harici düğüm, `External` katmanı (index −1), **planda olmayan 0**.
- Kayıp UI'da: repo kökü (`D:\Projects\Delta\OSYS`) `OSYS\Rent\OSYS.RentACar`'ı ZATEN içeriyordu, harici kart
  aynı solution'ın ikinci kopyasını getirdi → **7 çakışan AssemblyName**.
- `QuietGraphLayout.Compute` bant başına hücre ayırıyor ama konumu **ada** yazıyor → ikinci düğüm birincinin
  üstüne biniyor, ayrılan hücre boş kalıyor. `GraphView._slots` de ada göre anahtarlı → biri statü/seçim almaz.
- Yan etki: aynı ad 7 DLL'i `ProducerMap`'te belirsiz yapıyor → o DLL'lere HintPath ile bağlanan her projenin
  kenarı düşüyor. (Bu kullanıcıya HİÇ bildirilmiyor — ayrı bulgu, bu işin kapsamı dışında.)

TFVC tarafında ayrıca: `D:\Projects\Delta\Rent\OSYS.RentACar` üstünde `$tf` yok, yani `tf vc get` hiç
koşmuyordu. Kullanıcı TFVC'yi tümden kaldırmayı seçti.

## Görev A — TFVC'yi kaldır (git-only)

Sözleşme değişiklikleri:

| Yer | Bugün | Sonra |
|---|---|---|
| `Contracts/Model/ProjectModels.cs` | `enum VcsKind`, `static VcsKinds` | **silinir** |
| ” | `ExternalProject(string Path, VcsKind Vcs)` | `ExternalProject(string Path)` |
| ” | `ProjectNode.ExternalVcs : VcsKind?` | `ProjectNode.IsExternal : bool` |
| `Core/Externals/TfvcService.cs`, `TfResolver.cs` | var | **silinir** |
| `Core/Externals/VcsDetector.cs` | `FindRoot(dir, VcsKind)` | `FindRoot(dir)` — yalnız `.git` |
| `Core/Externals/ExternalWorkspaceResolver.cs` | `VcsByProjectId : IReadOnlyDictionary<string,VcsKind>` | `ExternalProjectIds : IReadOnlySet<string>` |
| `Core/Externals/ExternalUpdater.cs` | `UpdateTfvcAsync`, `ResolveTfAsync`, `tfResolver` ctor param, `ExternalPreparationException.Tfvc` | **silinir** |
| `Core/Externals/ExternalRevisionReader.cs` | git/TFVC ayrımı, `revisionByWorkingCopy` gerekçesi | yalnız git; güncellemeden gelen revizyon harmanı KALIR (ff sonrası hâli anlatır) |
| `Core/Planning/PlanProgressLines.cs` | `ExternalNoWorkingCopy(name, vcs)` | `ExternalNoWorkingCopy(name)` |
| `App/ViewModels/SettingsDraftViewModel.cs` | `ExternalRowViewModel.Vcs` | alan kalkar |
| `App/Views/SettingsDialog.xaml` | `SOURCE` kolonu + Git/TFVC `Ds.Select` | kolon kalkar, PROJECT PATH tam genişlik |
| `App/AccessibilityNames.cs` | `ExternalProjectSource` | silinir |

**Geriye dönük uyumluluk (kritik):** `settings.json` dışa/içe aktarma biçiminde ve `ui-state.json`'da `vcs`/
`Vcs` anahtarı yazılı dosyalar VAR. Kural: **okuyan taraf anahtarı görmezden gelir, yazan taraf artık
yazmaz.** Eski bir dosya yüklenince kart sıradan (git) bir kart olur; `.git` bulunamazsa mevcut
"no git working copy found above its path — building as-is" uyarısı çalışır. Kullanıcının listedeki eski TFVC
kartını kendisi kaldıracak (kendi ifadesi).

CLAUDE.md değişmezi de güncellenir: `tf vc get` istisnası cümleden çıkar.

## Görev B — Graf kimliği: ad → Id

- `GraphNode(string Id, string Name, int Layer, GraphStatus, VisualStatus)` — `Name` yalnız ETİKET.
- `GraphEdge.From/To` = Id (çeviri kalkar; `GraphBinder.Edges` ara haritaya gerek duymaz).
- `QuietGraphLayout.Positions` Id ile anahtarlanır (comparer OrdinalIgnoreCase — Id bir Windows yolu).
- `GraphView`: `_slots`, `_deps`, `_dependents`, `_endOrder`, `_focusSet`, `_markedNodes`, `_builtNodes`,
  `_hoveredNode`, `_filterMatches`, `SelectedNode`, `SelectionChanged` → Id. Comparer'lar OIC.
  Etiket kalan yerler: tooltip metni, seçim etiketi, `AutomationProperties.SetName` → `slot.Model.Name`.
- `MainWindow`: `_graphIdByName`/`_graphNameById` çeviri katmanı **silinir** (seçim artık doğrudan Id).
  `RefreshGraphFilter` → `VisibleProjects.Select(p => p.Id)`.
- `OperationChoreographer.PushToGraph` ve `RunViewModel.BuiltInThisRun()` → `r.Id`.

## Test planı (kırmızı önce)

1. `QuietGraphLayoutTests.Two_projects_that_share_a_name_get_two_distinct_cells` — aynı adlı iki düğüm iki
   ayrı konum alır. Bugünkü ada göre anahtarlamada `Positions.Count == 1` → KIRMIZI.
2. `GraphRenderTests` (realize) — aynı adlı iki proje iki ayrı hücre olarak çizilir ve ikisinin de statüsü
   ayrı güncellenir.
3. `GraphBinderTests` — `Edges` uçları Id; çakışan adda kenar DOĞRU örneğe bağlanır.
4. TFVC kaldırma testleri: mevcut `TfvcServiceTests`, `TfResolverTests` **silinir** (davranış kalktı).
   `VcsDetectorTests` git-only'ye yeniden yazılır. `ExternalWireShapeTests`/`SettingsPortabilityTests`/
   `UiStateStoreTests`: "eski `vcs` anahtarı taşıyan dosya yüklenir, anahtar yok sayılır, yazarken yazılmaz"
   iddiasıyla yeniden yazılır (eski iddia + gerekçe doc'a).
5. `ExternalUpdaterTests` — TFVC dalları silinir; git dalları aynen kalır.

## Doküman

- `ARCHITECTURE.md` (22 geçiş): §7 git yüzeyi, §10.x harici projeler, §13/§14 Settings kartı, §21/§22.
- `README.md` (6 geçiş), `CLAUDE.md` değişmezi (2 geçiş), `App/Services/ReleaseNotes.cs`.
- Anlatı üslubu: "TFVC kaldırıldı" YAZILMAZ; ilgili bölüm git-only anlatacak şekilde yerinde yeniden yazılır.

## Kapsam dışı (ayrıca konuşulacak)

- Çakışan AssemblyName kullanıcıya bildirilmiyor (`ProducerMap.AmbiguousDlls` hesaplanıp atılıyor).
- Kullanıcının harici kart listesindeki yanlış kartı kendisi düzeltecek.
