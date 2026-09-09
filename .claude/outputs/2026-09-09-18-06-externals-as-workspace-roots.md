# Harici Projeler = İkinci Çalışma Alanı Kökü — TDD Dökümü

> Harici proje artık bir "derleme hedefi" değil, **taranan ikinci bir kök**. Klasördeki projeler bulunur, tek
> grafa girer, ana projelerle gerçek kenar kurar ve normal incremental kararla derlenir. Tek farkları kendi
> repolarında yaşamaları ve Build'den önce (bir bayrağa bağlı) güncellenmeleri.

| | |
|---|---|
| **Branch** | `feat/externals-as-workspace-roots` |
| **Taban** | `main` @ `35f4593` |
| **Önceki tur** | `2026-09-09-17-17-external-projects-engine-wiring.md` (yol + kaynak modeli, tek-solution hedefi) |

## Bağlayıcı kararlar

- **D1 — Harici kök taranır.** Ayarlar'daki yol bir klasörse `WorkspaceScanner` ile taranır; bir `.sln` ise o
  solution'ın projeleri; bir `.csproj` ise tek proje. Sonuç ana taramayla BİRLEŞTİRİLİR — tek `ScanResult`,
  tek `BuildPlan`.
- **D2 — Kenarlar graftan doğar.** Producer map iki kökü birden görür, dolayısıyla ana projenin HintPath'i
  harici bir projenin DLL'ine denk gelince kenar kendiliğinden oluşur. "Önce hariciler" diye bir kural YOK:
  sıra topolojiden gelir. (Bugün `ExternalOsysPlatform` sayılan bazı HintPath'ler gerçek kenara döner.)
- **D3 — Katman ZORLANMAZ.** `LayerEngine` haricilere de aynen uygulanır; eşleşmeyen `Other`'a düşer.
  **Bu, 17-17 turundaki `External` katmanından (index −1) bir DÖNÜŞTÜR** — gerekçe: müşteri projesi tipik
  olarak OSYS platform DLL'lerine bağımlıdır, yani ana projelerin ARDINDAN gelir. Index −1 her koşuda sahte
  bir "reverse layer dependency" uyarısı üretir ve dispatch tercihini yanlış yöne çevirir. Harici satırın
  işareti `ProjectNode.ExternalVcs` rozeti olarak KALIR (görsel karşılığı ayrı bir tasarım turunda).
- **D4 — İmza: haricilerde committed fingerprint yerine İÇERİK fingerprint'i.** Ana repo git blob
  hash'lerini kullanır (hızlı, tek `ls-tree`); harici kökler ana reponun ağacında değildir ve TFVC'de git hiç
  yoktur. Harici düğümün fingerprint'i, build-etkileyen dosyalarının DİSKTEKİ içeriğinden hesaplanır — aynı
  hash primitifi, aynı ayraçlar, `BuildSignature` değişmez. §4 kaynak-sinyali kuralı korunur (timestamp
  okunmaz). Yan kazanç: kir doğal olarak imzaya girer, worktree/in-place ayrımı haricileri etkilemez.
- **D5 — Güncelleme bir BAYRAĞA bağlı.** `StartRunCommand.UpdateExternals` (varsayılan `true`). Açıkken
  Build'den önce git `fetch` + `merge --ff-only` / `tf vc get` koşar ve kir kapısı koşuyu iptal eder.
  Kapalıyken hiçbir VCS komutu çalışmaz, kir kapısı da YOKTUR — harici, ana repo gibi olduğu haliyle derlenir
  (imza içerik tabanlı olduğu için karar yine doğrudur). **UI toggle bu turda YOK** (tasarım ayrı gelecek).
- **D6 — Yürütme özel değil.** Harici projeler sıradan düğümlerdir: aynı scheduler, aynı paralellik, aynı
  MSBuild argüman sözleşmesi, aynı dependent kuralı. "Bir harici patlarsa koşu iptal" kapısı KALKAR — yerini
  mevcut "başarısız bağımlılığın dependent'ları atlanır" kuralı alır. Tek istisna: **worktree modunda harici
  projeye obj izolasyonu UYGULANMAZ** (çalışma kopyası havuzda yaşamıyor).
- **D7 — Sync sayaçları haricileri de sayar.** Artık sıradan projeler; "ana workspace'i anlatır" istisnası
  kalkar.
- **D8 — Git mutasyon yüzeyi değişmez:** yalnız `Core/Externals`, yalnız ff-only. Kaynak guard'ı aynen.

## Görevler

| # | Görev | Kırmızı test |
|---|---|---|
| T1 | `StartRunCommand.UpdateExternals` (kuyruk, default true) | eski NDJSON satırı `true` çözülür; round-trip |
| T2 | `ExternalWorkspaceResolver`: yol → `ScanResult` (+ uyarı), `SolutionMapper.ProjectsOf` | klasör / .sln / .csproj / yok / boş klasör |
| T3 | `BuildPlanBuilder` düğüme rozet basar; birleşik tarama kenar üretir | harici DLL'e HintPath'li ana proje kenar kazanır; rozet yalnız haricilerde |
| T4 | `IncrementalPlanner.ComputeContentFingerprint` + `IncrementalRunBinder` harici kök farkındalığı | harici dosya değişince willBuild true; değişmeyince false; git'te olmayan dosya karar verebilir |
| T5 | Sync: çözümle + birleştir + rozetle + harici kökleri geçir | harici csproj düğüm olur; kötü yol uyarır; boş liste bayt-bayt aynı |
| T6 | `ExternalUpdater` (yalnız güncelleme) + `Program.BuildRunPlan` bayrak kapısı | bayrak kapalı → hiç process yok; kirli + kapalı → koşu sürer; kirli + açık → planFailed |
| T7 | `RunCoordinator` sadeleşmesi + worktree'de harici obj izolasyonu yok | harici düğüm sıradan derlenir; obj argümanı yok; başarısız haricinin dependent'ı atlanır |
| T8 | App: `UpdateExternals` kalıcılığı + komuta taşınması | varsayılan true; anahtar yoksa true; komut taşır |
| T9 | Ölü kod temizliği + dokümanlar | guard'lar ve tam süit yeşil |

## Silinecekler

`ExternalTargetResolver`, `ExternalNodeBuilder`, `ExternalSyncInspector` (+`ExternalInspection`),
`ExternalSignature`, `ExternalWillBuild`, `ExternalProjectsConventions`, `ExternalBuildPlan`,
`RunPlan.Externals`, `MsBuildInvokeRequest.ExternalTarget`, `MsBuildArguments.BuildExternal/RestoreExternal`,
`RunCoordinator.BuildExternalAsync/PersistExternalState` ve harici sayaçları.

## Korunanlar

`ExternalProject(Path, Vcs)` sözleşmesi, Ayarlar UI'ı ve kalıcılığı, `ExternalGitUpdater`, `TfvcService`,
`TfResolver`, `VcsDetector`, kir kapısı metinleri, kaynak guard'ı, `ProjectNode.ExternalVcs`.
