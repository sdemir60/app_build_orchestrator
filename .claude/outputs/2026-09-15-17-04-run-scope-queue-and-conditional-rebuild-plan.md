# Koşu kapsamı, kuyruk rengi ve koşullu yeniden derleme — uygulama planı (TDD dökümü)

**Otorite:** kullanıcı kararları (bu oturum) + inceleme raporu
`.claude/outputs/2026-09-15-16-06-run-scope-and-queued-colour-investigation.md` (kök nedenler A-D, kanıt
dosyaları §7). Tasarım paketi: `.claude/outputs/2026-09-10-10-23-design-v1.18.0/README.md`.

## Global Constraints

- Repo kuralları: kökteki `CLAUDE.md` — kırmızı test kuralı (assertion düzeyinde; derleme hatası kırmızı
  sayılmaz — yeni imza gerekiyorsa önce eski davranışla ekle, assertion'ın düştüğünü göster), davranış
  değişince eski testi YENİ kuralı pinleyecek şekilde yeniden yaz + doc'una eski iddia ve gerekçe, bütçe/eşik
  gevşetmek YASAK, kopya YASAK / tek doğruluk kaynağı, token dışında renk/ölçü YASAK, yeni XAML şablonu =
  realize testi, kod/UI metni İngilizce, kod yorumu Türkçe, ARCHITECTURE.md anlatı üslubu (changelog yok, rakam
  gömme yok). Planlama Core'da; App/Supervisor'a iş mantığı sızmaz.
- **Animasyonlar ve koreografi DEĞİŞMEZ:** açılış dalgası, sıralı teslim sönümü, beads, neon finali, renk
  tonları, süreler. Değişen yalnız "hangi node/satır hangi an hangi görsel duruma geçer" kararı.
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
  (tam süit ÖN PLANDA, uzun timeout; Debug bin kilitliyse `-c Release`). Arka planda koşup beklemeye geçme.
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` YASAK — Edit/Write.
- Commit: mesaj dosyaya + `git commit -F`; Türkçe ASCII özne (`fix(run): ...`); son satır
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. Push/merge/branch değiştirme YOK.
- Branch: `fix/run-scope-queue-and-conditional-rebuild`.

## Hedef davranış (kullanıcı onaylı)

| Akış | Olması gereken |
|---|---|
| Satırdan Build | Yalnız hedef dalgada yanar ve koşuda amber'dır; diğer her satır/node baştan sona nötr gri. |
| Resolve cycles | Koşu başında yalnız döngü üyeleri amber (kuyruk). Kapsam içi bağımlılıklar gri bekler; derlemeye başlayınca building (amber + beads), bitince yeşil/kırmızı. Döngüyle ilgisiz (kapsam dışı, `not needed by a dependency cycle`) projeler nötr gri kalır, atlandı sayacına ve atlandı filtresine GİRMEZ. |
| Build | Dalga yalnız KESİN derlenecekleri yakar; koşu başında dalganın yakmadığı hiçbir node amber'a geçmez. |
| Koşullu yeniden derleme | Önceki koşuda hatalı bir bağımlılığa rağmen başarıyla derlenmiş (dep-issue notlu) ve kendi imzası değişmemiş proje, bir sonraki Build'de ANCAK kaydedilen kök bağımlılıklarından en az biri artık başarılıysa (bu koşuda başarıyla derlendiyse ya da son sonucu başarı ise) derlenir; tüm kökler hâlâ hatalıysa dokunulmaz. |
| Satır etiketi | Tablo aşağıda. |

Etiket tablosu (sağ yuva):

| Durum | Yazan | Renk | Anlamı |
|---|---|---|---|
| Hiç derlenmemiş | `never built` | belirgin (text-secondary) | Derlenecek |
| Son derleme hata | `failed · retry` | belirgin | Yeniden denenecek |
| Hata, döngü üyesi | `failed` | belirgin | Resolve dener |
| Kendi dosyaları değişti | `modified` | belirgin | Derlenecek |
| Bağımlılık değişti | `affected` | belirgin | Derlenecek |
| **Derlendi ama bağımlılığı hatalıydı (bekliyor)** | **`affected · up to date · just now`** (yaş `just now`/`1h`…) | **soluk (text-faint), `·` kuyrukları her zaman soluk** | Şimdilik dokunulmaz; kök başarılı olursa derlenir |
| Hiçbir şey değişmedi | `up to date · 2h` | soluk | Dokunulmaz |

Bekleyen satırın native tooltip'i: `Built against a failed dependency (<kök kısa adları, virgülle>) — rebuilds when it builds successfully`.
Yuva genişliği en uzun etiketi (`affected · up to date · just now`) sığdıracak şekilde ÖLÇÜLEREK büyütülür
(Geist Mono 10.5px, tabular; mevcut 134px kuralı `up to date · just now`'ın ölçümünden gelmişti — aynı yöntem),
her satırda sabit kalır. Tasarımın 134px yuvasından ve tek parçalı etiketinden BİLİNÇLİ sapma (kullanıcı kararı).

---

### Task 1: Kuyruk rengi koşunun kendi planından (kök neden A)

Kanıt: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` `ProjectRowViewModel.Status` (~215-230):
`_ when IsRunActive && WillBuild == true => Queued`. `WillBuild` genel plan bayrağıdır; tek proje koşusunda
`buildPreview` yalnız hedefi taşır (`RunCoordinator.cs` ~711-719, ~897-921), diğer satırlar Sync'ten kalan
`WillBuild`'le koşu boyunca amber olur, koşu bitince griye döner. `runStarted` ile `buildPreview` arasında da
bir an bayat bayrakla amber görülür. Graf aynı eşlemeyi okur (`GraphBinder.StatusOf`).

Yapılacak (App):
- Satıra "bu koşunun kuyruğunda" bilgisi (ör. `InRunQueue`) eklenir. YALNIZ bu koşunun `BuildPreviewEvent`'inden
  yazılır (öğe `WillBuild == true` ise true); koşu başında (`NeutralizeRows`) ve koşu bitince (IsRunActive
  düşerken) false olur. `Status`: `IsRunActive && InRunQueue → Queued` (`WillBuild == true` yerine).
  `WillBuild` karar etiketi ve kapsam hesabı için kalır. Tek eşleme yeri korunur (satır + graf aynı `Status`).
- Tek proje koşusunda önizleme yalnız hedefi taşıdığı için diğer satırlar kendiliğinden Discovered kalır.
- Önizleme gelmeden (runStarted anı) hiçbir satır kuyruk değildir → bir anlık amber kalkar.
- Mevcut `_willBuildIds` (ilerleme paydası vb.) ile ayrışma olmasın: aynı önizlemeden, tek yerde türesin.

Testler (önce KIRMIZI): tek proje koşusunda Sync'e göre bayat bir diğer satır `runStarted`+önizleme sonrası ve
koşu boyunca `Discovered`, koşu sonunda da `Discovered`; hedef `Queued`→`Building`; `runStarted` sonrası önizleme
gelmeden bayat satır `Queued` DEĞİL; graf statüsü satırla aynı. Mevcut kuyruk testleri DEĞİŞEN KURAL olarak
yeniden yazılır.

Doküman: ARCHITECTURE.md §13.2 (satır statüsü/kuyruk) ve §14.3 (statü sözlüğü) — ilgili cümleler yerinde.

### Task 2: Resolve'da kuyruk yalnız üyeler; kapsam dışı "işlem dışı" (kök neden B)

Kanıt: Cycles modunda kapsam üyeler + transitif upstream (`Core/Planning/CycleRunScope.cs`); kapsam dışı her
proje `SkipReasons.OutOfCycleScope` ile pre-skip ve `projectSkipped` gönderilir (`RunCoordinator.cs` ~749-829);
App `OnProjectSkipped` (`RunViewModel.cs` ~1628-1638) satırı `Skipped` yapar → atlandı görünümü + sayaç + filtre.
Stream zaten bu gerekçeyi toplu bir satırda birleştiriyor (`RunViewModel.Stream.cs` ~30, ~200) — o davranış kalır.

Yapılacak (App; Task 1'in `InRunQueue` altyapısı üstüne):
- Cycles koşusunda önizlemeden `InRunQueue` YALNIZ `InCycle` satırlara yazılır. Kapsam içi bağımlılık gri
  (Discovered) bekler; `projectStarted` ile Building, bitince Succeeded/Failed (normal yol).
- `OutOfCycleScope` gerekçeli skip satırın statüsünü değiştirmez (Pending/Discovered kalır), `SkipReason`'ı
  kullanıcıya görünür bir "atlandı" durumu olarak taşımaz; `RunCounters`/atlandı çipi ve `ProjectFilter.Skipped`
  bu projeleri SAYMAZ / LİSTELEMEZ. Bitiş koreografisi (neon) etkilenmez (zaten yalnız succeeded ∪ failed).
  Kapsam İÇİ gerçekten `up to date` atlanan bir bağımlılık normal `Skipped` olarak sayılmaya devam eder.
- Satırdan Build'in "kapsam dışına dokunulmaz" davranışıyla aynı dil; ilerleme paydası/ETA kapsamla tutarlı.

Testler (önce KIRMIZI): Cycles koşusunda bayat kapsam içi bağımlılık önizleme sonrası `Discovered`, started
sonrası `Building`, done sonrası `Succeeded`; kapsam dışı satır koşu boyunca ve sonunda `Discovered`, atlandı
sayacı yalnız kapsam içi skip'leri sayar, atlandı filtresi kapsam dışını listelemez; üyeler `Queued` (amber).

Doküman: ARCHITECTURE.md §8.1 (Cycles kapsamı), §13.2.

### Task 3: Koşullu yeniden derleme (kök neden D) — Core + Supervisor + Contracts

Kanıt: `Core/Planning/WillBuildEvaluator.cs` (~63-79) `DepIssue` notlu başarıyı her Build'de `WillBuild=true`
yapar; defter yalnız `BuildState.DepIssue` (bool) tutar, kök bilgisi yok (`Contracts/Model/ProjectModels.cs`
~117-145; yazım `RunCoordinator.cs` ~1747); çalışma zamanı dep-issue hesabı `Core/Scheduling/DepIssueTracker.cs`;
pre-skip/scheduler `RunCoordinator.cs` ~741-846, `Core/Scheduling/ReadySetScheduler.cs`; ARCHITECTURE §7.4, §8.3.

Yapılacak:
- **Defter:** dep-issue notuyla birlikte KÖK bağımlılıkların proje Id'leri kaydedilir (doğrudan + miras kökler;
  tek proje koşusundaki "bayat bırakılan bağımlılık" kökleri de dahil). Eski kayıtlar (kök listesi yok) okunabilir
  kalır: kök bilinmiyorsa bugünkü davranış (derlenecek) — güvenli yön.
- **Planlama kararı (saf, Core):** dep-issue notlu, son sonucu başarı, kendi imzası kayıtlı imzayla AYNI proje
  artık "kesin derlenecek" değil **koşullu**dur. Yeni gerekçe `WillBuildReason` sözlüğüne eklenir (ör.
  `WaitingForDependency`). İmza değişmişse (`SignatureChanged`), hiç derlenmemişse ya da kendi sonucu hatalıysa
  normal kural geçerlidir. Kararı üreten tek yer `WillBuildEvaluator` kalır (kopya YASAK).
- **Koşu zamanı (Build modu):** koşullu proje, sırası geldiğinde (tüm bağımlılıkları çözülünce) değerlendirilir:
  kaydedilen köklerden EN AZ BİRİNİN güncel sonucu başarıysa (bu koşuda başarıyla derlendi, ya da bu koşuda
  derlenmedi ama defterdeki son sonucu başarı) proje normal şekilde derlenir; kök projede artık yoksa derlenir
  (güvenli yön); TÜM kökler hâlâ hatalıysa (bu koşuda patladı ya da son sonucu hata ve derlenmedi) proje yeni bir
  skip gerekçesiyle atlanır (ör. `SkipReasons` içinde `dependency still failing`), dep-issue notu ve kökleri
  KORUNUR, `BuiltSignature` değişmez. Karar saf bir Core fonksiyonunda, scheduler/koordinatör yalnız uygular.
- **Rebuild modu:** değişmez (her şeyi derler). **Satırdan Build:** hedef koşulsuz derlenir (değişmez).
  **Cycles modu:** kapsam içi projelerde aynı koşullu kural geçerli.
- **Önizleme (Contracts/IPC):** `BuildPreviewItem` koşullu projeyi ayırt edilebilir taşır (gerekçe
  `WaitingForDependency` ve/veya açık bir alan); App kuyruk kararını (Task 1 `InRunQueue`) buna göre verir —
  koşullu proje kuyrukta DEĞİL. Etiket için kök adları da taşınır (App metni kendisi üretmez).
- Başarıyla derlenen koşullu projenin notu normal kurala göre temizlenir/yenilenir (yeni dep-issue varsa yeni kökler).
- decision.log ve konsol: koşullu atlama tek satırla gerekçelenir (mevcut Decide/console deseni).

Testler (Core saf + Supervisor koordinatör; önce KIRMIZI):
1. Kök hâlâ hatalı (bu koşuda yine patlıyor) → bağımlı dispatch edilmez, `dependency still failing` skip, not ve
   kökler korunur.
2. Kök bu koşuda başarılı → bağımlı onun ARKASINDAN derlenir, not temizlenir.
3. Kök kaynak değişmeden düzelir (defterde son sonucu başarı, bu koşuda up to date atlanır) → bağımlı derlenir
   (§8.3 güvenlik senaryosu).
4. Eski kayıt (kök listesi yok) → bugünkü davranış (derlenir).
5. İmza değişmiş dep-issue'lu proje → normal derlenir (koşullu değil).
6. Önizleme koşullu projeyi `WaitingForDependency` (kuyruk değil) olarak taşır; kök adları dolu.
7. Rebuild ve tek proje koşusu etkilenmez.
Eski "dep-issue her Build'de derlenir" testleri DEĞİŞEN KURAL olarak yeniden yazılır.

Doküman: ARCHITECTURE.md §7.4 (will-build tri-state + yeni gerekçe), §8.3 (dep-issue kaydı ve koşullu tetik),
§5.3 (önizleme alanı), §16 (build-state dosyası alanı).

### Task 4: Dalga, canlı geçiş ve etiket motorla aynı şeyi söylesin (kök neden C) — App

Kanıt: `RunViewModel.cs` ~1683 başarılı her satırı `WillBuild=false` + `UpToDate` yapar (dep-issue'lu başarı dahil);
`ScopeFor(Build)` (~880-885) `WillBuild==true`; `ViewModels/DecisionLabel.cs`; `Views/ProjectRow.xaml(.cs)` sağ
yuva (MinWidth 134, design v1.16.0 ölçümü).

Yapılacak:
- Koşu içi canlı geçiş: bu koşuda dep-issue ile biten başarı (`DepIssues` dolu) `UpToDate` DEĞİL
  `WaitingForDependency` gerekçesine geçer (kökler olaydan/önizlemeden), `LastBuiltAt` şimdi; kesin
  derlenecekler kümesine girmez. Dep-issue'suz başarı bugünkü gibi `UpToDate`.
- `ScopeFor(Build)` yalnız KESİN derlenecekleri döner (koşullular hariç) → dalga = motorun kesin kuyruğu =
  Task 1 kuyruk kümesi (tek kaynaktan türesin).
- `DecisionLabel`: `WaitingForDependency` → Word `affected`, kuyruk `up to date · <yaş>` (iki `·` parçası;
  `RowDecision` gerekirse birden çok kuyruk parçası taşıyacak şekilde genişler), `Stale=false` (soluk),
  tooltip `Built against a failed dependency (<kökler>) — rebuilds when it builds successfully`. Diğer satırlar
  tabloyla birebir aynı kalır.
- `ProjectRow` sağ yuvası en uzun etiketi sığdıracak kadar ÖLÇÜLEREK büyütülür; hover'da ikon bloğuyla yer
  değiştirme ve satırlar arası hizalama kuralı korunur; ölçüm testi + realize testi (mevcut
  `ProjectRowTests`/`ListRealizationPerfTests` desenleri; perf bütçesi gevşetilmez).
- Stream/console: koşullu atlama satırı mevcut skip dilinde (`skipped — dependency still failing`); metin tek
  kaynaktan (`SkipReasons`).

Testler (önce KIRMIZI): hatalı kök + başarılı bağımlı koşusu sonrası bağımlının etiketi
`affected · up to date · just now` (soluk), tooltip kök adını içerir; ikinci Build'in dalga kümesi motorun
kesin kuyruğuna eşit (koşullu hariç); yuva en uzun etiketi kırpmadan çizer; mevcut etiket testleri tabloyla
uyumlu kalır.

Doküman: ARCHITECTURE.md §13.2 (karar etiketi tablosu, yuva genişliği), §14.3.

### Task 5: Sürüm notları + doküman taraması + tam süit

- `src/BuildOrchestrator.App/Services/ReleaseNotes.cs`: kullanıcıya görünen değişiklikler için İngilizce maddeler
  (kuyruk rengi yalnız koşunun planı; Resolve kapsam dışı projeleri saymaz; koşullu yeniden derleme; yeni etiket).
- ARCHITECTURE.md / README.md bu branch sonrası bayat kalan ifadeler (grep: `WillBuild == true`, `dep-issue`,
  `DepIssue`, `134`, `skipped` sayaç anlatısı, `every Build`) yerinde düzeltilir.
- Tam süit ÖN PLANDA yeşil; flake varsa izole + ikinci tam koşu, dürüst rapor.
