# Sync / graph / konsol düzeltmeleri — TDD planı

Kaynak: kullanıcı testleri (2026-09-19) + kod incelemesi. Branch: `fix/sync-graph-polish`.

## Global Constraints

- CLAUDE.md değişmezleri bağlayıcıdır: planlama Core'da; kopya YASAK (aynı değer/metin/primitif iki yerde
  tanımlanmaz, mevcut yardımcıyı yeniden kullan); kod/UI/log metinleri İngilizce, kod yorumları Türkçe.
- **Kırmızı test kuralı:** her düzeltmeden ÖNCE kusuru yakalayan yeni test yazılır ve KIRMIZI verdiği
  gösterilir (komut + çıktı rapora). Sonra düzeltme, sonra yeşil.
- Bilerek değişen bir kuralı pinleyen eski test sessizce silinmez/gevşetilmez: yeni kuralı pinleyecek şekilde
  yeniden yazılır, doc'una eski iddia + değişme gerekçesi yazılır. Eşik/bütçe gevşetmek YASAK.
- Yeni XAML kökü/şablonu eklenirse realize testi de eklenir.
- Bu task'larda ARCHITECTURE.md / README.md'ye DOKUNMA (Task 8'in işi). Kod içi XML doc'ları güncel tutulur.
- Dosya düzenleme: yalnız Edit/Write. `sed -i` CRLF'i bozar; PowerShell `Get-Content|Set-Content` UTF-8
  Türkçe karakterleri bozar — KULLANMA.
- Build/test (uygulama açık olabilir, Debug bin'i kilitli olabilir → Release kullan):
  - `dotnet build BuildOrchestrator.slnx -c Release`
  - `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "Category!=Acceptance"`
  - Tek sınıf: `... --filter "FullyQualifiedName~<SınıfAdı>"`
- Her task kendi commit(ler)ini atar; commit mesajı Türkçe, `fix(...)`/`feat(...)` önekli. Claude attribution
  satırı (Co-Authored-By / Generated with) EKLENMEZ.
- Task sonunda filtrelenmiş tam süit yeşil olmalı.

## Task 1: Sessiz Sync biten satırların kararını tazelesin

**Kusur:** Koşudan sonra pencereye dönüşle çalışan sessiz Sync, son koşuda `Succeeded`/`Failed`/`Skipped`
biten satırların `WillBuild`/`WillBuildReason`/`Conditional`/`DependencyRoots` alanlarını yazmıyor
(`src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` ~1883: `if (row.State is Succeeded or Failed or
Skipped) continue;`). Sonuç: arka planda değiştirilen proje (özellikle döngü üyeleri — normal Build'de hep
`Skipped` biter, Resolve cycles'ta `Succeeded` biter) yeşil `up to date` kalıyor; yalnız elle Sync düzeltiyor.
Görünür Sync satırları `NeutralizeRows` ile sıfırladığı için sorun yok (`RunViewModel.Workspace.cs` ~898).

**İstenen davranış:** Koşu SÜRMÜYORKEN gelen her önizleme (sessiz Sync dahil) terminal satırların kararını da
yazar. Kural: sessiz Sync'ten sonra bir satırın rengi (`VisualStatus`) ve karar etiketi, aynı önizlemeyle
elle Sync yapılsaydı göstereceğiyle aynıdır. Koşu içi koruma (segment 1'in canlı succeeded→clean geçişi
ezilmesin) yalnız koşu sürerken (`RunActive`) geçerli kalır.

**Testler (önce kırmızı):**
- Build koşusu bitmiş, satır `Succeeded` + yeşil; ardından sessiz Sync önizlemesi o satır için
  `WillBuild=true, Reason=SignatureChanged` getirir → satır gri/`modified`.
- Aynısı `Skipped` biten satır için (döngü üyesi senaryosu).
- Aynısı `Failed` biten satır için (önizleme artık yeşil diyorsa yeşil).
- Koşu sürerken gelen önizleme terminal satırın kararını YİNE ezmez (mevcut koruma kırılmadı).
- Sessiz Sync'in `synced · N projects changed` sayacı bu satırları da sayar (ilgili hesap varsa kontrol et).

## Task 2: Graph kamerası ekranda da gerçekten fit'e otursun

**Kusur:** `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` `ApplyGraph` (~669) yalnız
`CurrentCamera = GraphCamera.Default` yazıyor; ekrandaki `_cameraScale`/`_cameraTranslate` transform'ları
sıfırlanmıyor. Ardından `ApplyCamera` → `AnimateCameraTo` (~1697) `if (camera == CurrentCamera) return;` ile
erken dönüyor. Sonuç: graf yeniden kurulduktan sonra (yapısı farklı branch'e geçiş, gizli panele dönüş) önceki
zoom/pan ekranda kalıyor, boş alana tıklamak (`HandlePanEnd` ~1584) hiçbir şey yapmıyor; kullanıcı zoom'u
oynatınca yeniden çalışıyor. Satır ~215'teki benzer atamayı da kontrol et.

**İstenen davranış:** Hedef kamera ile ekrandaki transform hiçbir yolda ayrışmaz. Graf yeniden kurulunca ekran
fit'e (Default) oturur; boş alana tıklamak ekran Default değilse her zaman Default'a döner.

**Testler (önce kırmızı):** mevcut test yardımcılarını kullan (`MoveLiveCameraForTest` vb.; `GraphPanZoomTests`,
`ChoreographyTests` kalıbı). Testler hedefe DEĞİL ekrana uygulanan transform'a bakmalı.
- Zoom'lu canlı kamera + `SetGraph`/yeni graf → ekrandaki transform Default.
- Zoom'lu canlı kamera + yeni graf + boş alana tık (`HandlePanStart`/`HandlePanEnd`) → Default.
- Panel gizliyken gelen graf, panel görünür olunca → Default (gizli panel yolu).

## Task 3: Elle Sync ve branch değişimi ekranı baştan başlatsın

**Kullanıcı kararı:** Sync düğmesi (`SyncMode.Manual`) ve branch değişimi (`SyncMode.BranchChange`) — yani
konsolu temizleyen kipler — konsol ve akışla AYNI ANDA proje listesini ve graph'ı da temizler; Sync sonucu
gelince liste ve graph standart açılış (reveal) animasyonuyla, graph fit halde yeniden gelir — proje yapısı
değişmese bile. Sessiz (`Silent`) ve `Appended` kipler bugünkü gibi yerinde tazeler, kamerayı oynatmaz.

**Bugünkü kod:** `RunViewModel.cs` `SyncCoreAsync` (~1206; doc ~1195: "Plan yüzeyi artık Sync'te BOŞALMAZ");
reveal yalnız yapısal imza değişince (`RunViewModel.Workspace.cs` ~930-961 `TopologyChanged`), abone
`MainWindow.xaml.cs` ~189 / ~738 (`RefreshProjectGroups` + `RebuildGraph`). `ClearPlanSurface`
(`RunViewModel.ActionBar.cs`) Clean/Optimize'da kullanılıyor — ama Phase=Boot / HasTopology=false / boş durum
daveti gibi yan etkileri var.

**Kısıtlar:**
- Kural `SyncModeRules`'a TEK soru olarak eklenir (ör. `RestartsPlanSurface`), çağıranlar kipi karşılaştırmaz.
- Temizlik anında boş-durum daveti (invite / "press Sync") görünmemeli, faz Boot'a düşmemeli; Sync süresince
  ekran sadece boş/sakin dursun. Sync düşerse ya da topoloji gelmezse (motor hatası) önceki liste/graph geri
  gelmeli — ekran boş kalmamalı.
- Filtre korunur (bugünkü "filtre KORUNUR" kuralı), seçim düşer (zaten düşüyor). Reveal filtreli görünür
  kümeyle oynar.
- Satır renkleri reveal ile önizlemeden gelir; kanıtlı kırmızılar yine kırmızı gelir.
- Task 2'deki kamera düzeltmesine dayanır (fit ekranda gerçekten olur).
- Reduced motion: animasyon yok, anında görünür.

**Testler (önce kırmızı):**
- Manual Sync tıklaması → liste/graph temizlenir (VM ya da görünüm düzeyinde, hangisi uygulanırsa); yapı AYNI
  topoloji gelince reveal oynar + graph kamerası Default.
- BranchChange için aynısı.
- Silent ve Appended Sync, yapı aynıyken reveal oynatmaz, kamera yerinde kalır (mevcut
  `A_sync_with_the_same_structure_updates_in_place` testleri yeni kurala göre yeniden yazılır: Silent/Appended
  için korunur, Manual/BranchChange için yeni kural).
- Sync gönderimi düşerse önceki yüzey geri gelir.

## Task 4: Döngü grubu zaman kipinde yeşil olabilsin

**Kusur:** Zaman kipinde (proje dışarıda, ör. VS'de derlenmiş ya da defter kaydı yok) bir projenin HintPath
hedeflerinin zamanı projenin çıktısıyla kıyaslanıyor (`src/BuildOrchestrator.Core/Incremental/
IncrementalRunBinder.cs` ~142-143, `OutputEvidence.cs` ~117 `DependencyNewer`). Döngü üyelerinde bu hedefler
kardeş üyelerin dll'lerini de içeriyor; üyeler sırayla derlendiği için biri hep ötekinden yeni kalıyor ve
`ApplyCycleGroups` (`OutputEvidence.cs` ~139-150) grubu hep bayat bırakıyor. Doküman §7.6 "When all of them
are current, all read built outside this tool" diyor — pratikte ulaşılamıyor.

**İstenen davranış:** Zaman kipinde bir döngü üyesinin `DependencyNewer` kontrolü AYNI döngü grubundaki
kardeşlerin çıktılarını saymaz (döngü dışı upstream'ler sayılmaya devam eder). Grup kuralı aynı kalır: bütün
üyeler taze ise hepsi yeşil (`BuiltOutside`), biri bayatsa (kendi dosyası çıktısından yeni, döngü dışı bir
upstream daha yeni, çıktı yok…) grup bayat. Kullanıcı kararı: döngüde bir üye değişince diğerleri de gri
(`affected`) olur — bu KORUNUR.

**Testler (önce kırmızı, Core):**
- 3 üyeli döngü, üyeler sırayla dışarıda derlenmiş (dll zamanları artan), kaynaklar dll'lerden eski → üçü de
  `BuiltOutside`/taze.
- Aynı grupta bir üyenin kendi kaynağı dll'inden yeni → grubun tamamı bayat.
- Döngü dışı bir upstream'in dll'i bir üyenin dll'inden yeni → grup bayat.

## Task 5: Filtre modunda Build — graph build boyunca filtreyi yok saysın

**Kullanıcı kuralı:** Filtre açıkken Build (tam koşu, Resolve cycles, satırdan tek proje koşusu dahil):
- Proje LİSTESİ build boyunca filtreli kalır (bugün Build tıklaması chip filtrelerini düşürüyor —
  `RunViewModel.cs` ~930 `ClearSelectionAndFilter` ~1536; artık yalnız seçim düşer, filtre (chip'ler + arama
  metni) korunur).
- GRAPH build tıklamasından itibaren filtreyi yok sayar: açılış dalgası, koşu opaklıkları, bitişteki neon final
  standart (filtresiz) haliyle oynar.
- Final bittikten sonra kısa bir bekleme (mevcut adım bekleme primitifini kullan — `StepHold`/`OperationHold`
  ya da finalin kendi sonu; yeni sabit icat etme, gerekiyorsa tek yerde tanımla) ve graph filtreli görünüme
  (eşleşmeyen 0.1) mevcut filtre geçiş süresiyle (`GraphNodeOpacity.FilterFadeMs`) döner.
- Hiç derleme olmayan koşu (final yok) / Stop / motor ölümü: koşu bitince aynı şekilde filtreye döner.
- Filtre yokken davranış hiç değişmez.

**Bugünkü kod:** `GraphNodeOpacity.Resolve` (seçim > filtre > koşu > hover; filtre dalı koşuyu yeniyor),
`MainWindow.xaml.cs` ~1021 `RefreshGraphFilter` (`filtering = ActiveFilters.Count > 0 || ProjectQuery`),
`GraphView.xaml.cs` `FilterMatches` (~371), marking (~1373) ve finale (~1387, ~504-539) yolları.
`GraphNodeOpacityTests.The_filter_branch_does_not_grant_the_live_building_exception` eski kuralı pinliyor —
yeni kurala göre yeniden yazılır.

**Testler (önce kırmızı):**
- Filtre açıkken Build tıklaması filtreyi düşürmez (chip + arama korunur), seçim düşer.
- Koşu sürerken filtre dışında derlenen düğümün gövde opaklığı filtre tarafından 0.1'e bastırılmaz (koşu
  kuralı uygulanır).
- Koşu bitip final/bekleme tamamlanınca filtre dışı düğümler `Unfocused`'a döner.
- Filtre yokken koşu opaklıkları değişmedi.

## Task 6: Konsolda yalnız bizim animasyonlu imlecimiz görünsün

**Kusur:** `src/BuildOrchestrator.App/Console/ConsoleView.xaml` (~38-47) AvalonEdit editöründe yalnız
`IsReadOnly="True"` var; konsola tıklayınca TextArea odağı alıyor ve AvalonEdit'in kendi ince caret'i yanıp
sönüyor. Tek canlı imleç `CursorHop`'lu prompt caret'i olmalı (event stream'deki gibi).

**İstenen davranış:** AvalonEdit caret'i hiçbir durumda görünmez (ör. `TextArea.Caret.CaretBrush` şeffaf).
Metin seçimi, Ctrl+C ve klavyeyle seçim çalışmaya devam eder (Focusable kapatılmaz). CursorHop imleci aynen
kalır.

**Test (önce kırmızı):** STA realize testi — konsol görünümü kurulur, TextArea'ya odak verilir; AvalonEdit
caret'inin görünmez olduğu (caret fırçası şeffaf / caret katmanı çizmiyor) doğrulanır.

## Task 7: Git retleri konsolda amber, akışta animasyonlu amber satır

**Kullanıcı isteği:** Stash ayarı kapalıyken kirli ağaçta branch değiştirme reddi daha göze çarpsın:
konsolda amber (uyarı) satır, event stream'de de animasyonlu (daktilo) amber satır. İki panelin metni
farklı olabilir ama uyumlu olmalı: akışta kısa, konsolda açıklamalı.

**Bugünkü kod:** `PlanProgressLines.SwitchRefusedDirty` (`Core/Planning/PlanProgressLines.cs` ~108-110) →
`RunViewModel.Workspace.cs` ~562 `AppendRunLine`; öneksiz olduğu için `ConsoleLineClassifier.Classify`
(`App/Console/ConsoleLine.cs` ~47-73) onu Info (düz) boyuyor. Amber yalnız `warning:` önekli satırlarda.
Stream: `StreamKind { Ok, Fail, Skip, Sync, Info, Done }` (`App/ViewModels/StreamComposer.cs` ~6), renkler
`StreamEventViewModel.cs` ~94-101; uyarı türü yok. Aynı düzlükte kalan diğer git retleri: pull redleri
(`PlanProgressLines.cs` ~77-86, Supervisor `"warn"` tonuyla gönderiyor ama konsolda kayboluyor),
`SwitchFailed` (~113), `GitOperationText.StuckLock`.

**İstenen davranış:**
- Konsol: branch değiştirme reddi (kirli ağaç), checkout hatası ve pull redleri uyarı (amber) olarak boyanır —
  mevcut `warning:` sözleşmesi ya da mevcut sınıflandırıcıyı kullanan tek bir yol; yeni renk icat edilmez.
- Stream: yeni bir `Warn` türü; rengi mevcut amber/uyarı token'ından (tek kaynak), daktilo animasyonuyla gelir
  (Fail gibi anında değil). Kirli ağaç reddi ve pull reddi akışa kısa bir satır düşer. Metinler `StreamText`'te
  (tek kaynak), mevcut akış satırlarının üslubunda (küçük harf, kısa), ör. `branch switch refused — 3 uncommitted files`.
- Metinler tek kaynakta (PlanProgressLines / GitOperationText / StreamText); kopya YASAK.

**Testler (önce kırmızı):**
- Kirli ağaç reddi: konsol satırı Warn sınıfında; akışa Warn türünde bir satır düşer.
- Pull reddi: aynısı.
- Warn stream satırının rengi amber token'ı; daktiloyla gelir.
- `BranchCheckoutTests` / `PlanProgressLinesTests`'teki metin pinleri yeni metinlere göre yeniden yazılır.

## Task 8: Testlerin ve dokümanların son hali

- ARCHITECTURE.md ve README.md'yi Task 1-7'nin değiştirdiği davranışlara göre yerinde yeniden yaz (anlatı
  üslubu, changelog yok, rakam gömme yok). İlgili bölümler: §7.6 (döngü grubu zaman kipi), §10.2/§10.3 (Sync
  kipleri, plan yüzeyinin elle Sync/branch değişiminde yeniden açılması, git retlerinin konsol+akış yüzeyi),
  §12.3 (sessiz Sync kararları tazeler — artık doğru), §13.2 (akış içerik kuralı: Warn satırı; filtre Build'de
  düşmez), §13.5 (native caret gizli), §13.6 (kamera; filtre koşu boyunca yok sayılır), §13.7, §14.3, §14.5.
- Test doc'larında eski iddia + değişme gerekçesi eksik kalan varsa tamamla; kaynak guard'ları yeşil.
- `.claude/outputs/2026-09-19-14-49-sync-build-behaviour-test-guide.md` tarihseldir — dokunma.
- Filtrelenmiş tam süit yeşil.
