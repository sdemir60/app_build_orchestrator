# Build · Resolve · satırdan Build — kapsam ve "sarıya dönen node" incelemesi

Tarih: 2026-09-15 · Durum: yalnız inceleme, kodda değişiklik YOK · `main` = `47d02d3`

## 1. Kısa cevap

- **Son tasarım çalışması (v1.17/v1.18) bozmadı.** İlgili kuralların hepsi daha eski:
  - kuyruk kuralı `RunViewModel.cs:224` → 2026-08-10 (`932a598b`)
  - dalga kapsamı `RunViewModel.ScopeFor` (`:880-885`) → 2026-08-27 (`510bb868`)
  - tek proje plan kesimi `RunCoordinator.cs:711-719` → 2026-09-09 (`8319fa0d`)
  Yeni sıralı teslim dalgası geçişi yumuşattığı için kusur artık daha net seçiliyor olabilir, ama kaynağı eski.
- **Üç belirtinin ortak kökü:** ekranda "sarı/amber = kuyrukta" kararı, **bu koşunun gerçek planına** değil
  satırın **genel "derlenecek mi" bayrağına** (`WillBuild`) bakıyor. Bu bayrak üç akışta da koşunun
  kapsamıyla uyuşmuyor.
- Build'de ayrıca **motor gerçekten** bir sürü projeyi yeniden derliyor: önceki koşuda hatalı bir
  bağımlılığa rağmen başarıyla derlenen projeler bir sonraki her Build'de yeniden derleniyor. Ekran da bunu
  önceden göstermiyor (dalga onları işaretlemiyor), bu yüzden "hop" diye sarı oluyorlar.

## 2. Akışların bugünkü hâli (kod gerçeği)

Ortak başlangıç (üçünde aynı, `RunViewModel.BeginRunAsync` `:730-799`):
1. Kapsam ekrandaki satırlardan okunur (`ScopeFor`), sonra tüm satırlar nötr griye iner (`NeutralizeRows`).
2. Açılış koreografisi oynar (nötr → dalga → sarı-gri an → grilerin vedası → kapsamın sıralı sönümü) ve
   son karede **tutulur**.
3. Koreografi bitince komut motora gider; motor planlar (saniyeler sürebilir), sonra `runStarted` +
   hemen ardından `buildPreview` gelir.
4. `runStarted` → `IsRunning = true` → her satıra `IsRunActive = true` itilir (`PropagateRunActive` `:1282`).

**Sarıya dönme kuralı** — `ProjectRowViewModel.Status` (`RunViewModel.cs:215-230`), satır ve graf AYNI
eşlemeyi okur (`GraphBinder.StatusOf` delege eder):

```
Pending satır, IsRunActive && WillBuild == true  →  Queued  →  amber (VisualStatuses: Queued = Amber)
```

`WillBuild` satırın genel planıdır (Sync'in önizlemesi, koşu başındaki `buildPreview`, koşu içindeki canlı
geçiş). "Bu koşunun kapsamında mı" bilgisi DEĞİLDİR.

### 2.1 Satırdan Build (tek proje)

| Adım | Ne oluyor |
|---|---|
| Dalga | Kapsam `[hedef]` — yalnız hedef yanıyor. ✔ |
| Motor planı | Plan TEK düğüme kesiliyor (`RunCoordinator.cs:711-719`, `ProjectRunScope`). |
| `buildPreview` | Yalnız hedefi taşıyor (plan tek düğüm). Diğer satırların `WillBuild`'i Sync'ten kalan değerde. |
| `runStarted` | `IsRunActive=true` → Sync'e göre "derlenecek" olan **tüm diğer satırlar Queued → amber**. ✘ |
| Koşu sonu | `IsRunActive=false` → hepsi griye döner. ✘ (senin gördüğün "bitince geri gri") |

**Kök neden A:** kuyruk kuralı koşunun kapsamını bilmiyor; tek proje koşusunda önizleme diğer satırları
güncellemediği için bayat `WillBuild` doğrudan amber'a dönüşüyor.

### 2.2 Resolve cycles

| Adım | Ne oluyor |
|---|---|
| Dalga | Kapsam `InCycle` satırlar — yalnız döngü üyeleri yanıyor. ✔ |
| Motor kapsamı | Üyeler **+ transitif upstream** (`CycleRunScope.Of`). Upstream'in güncel olanları pre-skip, bayat olanları derlenir. Kapsam dışı her şey `not needed by a dependency cycle` gerekçesiyle pre-skip. |
| `runStarted` → `buildPreview` arası | Satırlar hâlâ Sync'in (Build moduna göre hesaplanmış) `WillBuild`'ini taşıyor → döngü dışı tüm bayat projeler bir an **amber**. ✘ |
| `buildPreview` | Kapsam içi bayat upstream `WillBuild=true` → **amber kuyruk**. ✘ (senin istediğin: gri kalsın, derlenirse yeşil/kırmızı) |
| Kapsam dışı skip'ler | Her biri `projectSkipped` → satır `Skipped` görünümüne geçiyor (gri ama "atlandı" stili, grafta skipped çerçeve). ✘ (istenen: nötr gri, işleme hiç dahil değil) |

**Kök neden A** (aynı kuyruk kuralı) + **kök neden B:** Resolve'da "kuyruk" tanımı üyelerle sınırlı değil;
ve kapsam dışı projeler "işlemin dışında" değil "bu işlemde atlandı" olarak boyanıyor.

### 2.3 Build (ikinci kez)

| Adım | Ne oluyor |
|---|---|
| 1. Build | Bazı projeler hata veriyor. Hatalı projeye bağlı olanlar **bloklanmıyor**; hatalının son başarılı çıktısıyla derleniyor ve başarılı oluyor ama `DepIssue` notu alıyor (ARCHITECTURE §8.3, `RunCoordinator.cs:1747`). |
| Koşu içi canlı geçiş | Başarılı her satır `WillBuild=false` oluyor (`RunViewModel.cs:1683`) — **dep-issue'lu başarılar da**. Etiket "up to date · just now". |
| 2. Build — dalga | Kapsam `WillBuild==true` → yalnız hatalılar yanıyor. |
| 2. Build — motor planı | `WillBuildEvaluator.EvaluateWithReason` (`:70-78`): `DepIssue` notlu her başarı **"derlenecek"** (`WillBuildReason.DepIssue`). |
| `buildPreview` | Bu projeler `WillBuild=true` → **dalgada işaretlenmemiş bir sürü node amber** oluyor ve **gerçekten yeniden derleniyor**. ✘ |

**Kök neden C (ekran ↔ motor ayrışması):** koşu içi canlı geçiş, dep-issue'lu başarıyı "güncel" sayıyor;
motor ise bir sonraki Build'de onu derliyor. Dalga ve etiket motorun yapacağını söylemiyor.

**Kök neden D (politika):** dep-issue'lu başarı, hatalı bağımlılığı **hâlâ hatalıyken** her Build'de yeniden
derleniyor. Bu derleme hiçbir şey kazandırmıyor: bağımlılık yine son başarılı (aynı) çıktısını veriyor, sonuç
aynı bayat bağlantı. Kural bir güvenlik gerekçesiyle var (ARCHITECTURE §8.3 / `WillBuildEvaluator` doc'u):
bağımlılık bir gün **kaynak değişmeden** düzelirse, bağımlı proje imzası değişmediği için sonsuza dek bayat
kalmasın. Gerekçe doğru, ama tetik yanlış yerde: "bağımlılık düzeldiğinde" değil "her Build'de".

### 2.4 Rebuild

Kapsam döngü dışı tüm projeler, motor da hepsini derliyor — dalga ile motor uyumlu. Sorun yok (A kuralı
burada zararsız çünkü kapsam = her şey).

## 3. Senin istediğin ↔ benim önerim

| # | Akış | Senin istediğin | Önerim |
|---|---|---|---|
| 1 | Satırdan Build | Tek satır yanar, süreç tamamlanır; diğerleri gri kalır. | Aynı. Kuyruk/amber yalnız **bu koşunun planındaki** satırlara. Tek proje koşusunda plan = hedef → başka hiçbir satır sarı olmaz. |
| 2 | Resolve | Yalnız döngü üyeleri amber; bağımlılıktan derlenen başka projeler gri kalsın, derlenirse yeşil/kırmızıya dönsün. | Aynı. Resolve'da **kuyruk = yalnız üyeler**. Kapsam içi upstream "derlenecek" olsa bile gri bekler; derlemeye başladığı an building (amber + beads), bitince yeşil/kırmızı. Kapsam dışı projeler **atlandı** diye boyanmaz, nötr gri kalır ve sayaçlara girmez (satırdan Build'in "kapsam dışına dokunulmaz" kuralıyla aynı dil — design §3.8). |
| 3 | Build (tekrar) | Hatalılar derlensin; bağımlısı hatalı ama kendisi derlenmiş projeler tekrar derlenmesin. | **Koşullu yeniden derleme:** dep-issue'lu başarı yalnız, hatalı bağımlılığı **bu koşuda başarıyla** derlenirse (yani ortada yeni bir çıktı varsa) derlenir. Bağımlılık yine patlarsa bağımlı proje dokunulmadan kalır (gri, "dependency still failing" gerekçesiyle). Böylece hem senin istediğin boşa derleme kalkar hem de §8.3'teki güvenlik korunur: bağımlılık düzeldiği koşuda bağımlılar zaten sırayla onun arkasından derlenir. |
| 4 | Build — ekran | Derlenecekler baştan belli olsun. | Dalga motorun yapacağını söylesin: dep-issue'lu başarı koşu sonunda "güncel" diye işaretlenmez, etiketi bağımlılığı bekleyen bir durum söyler; dalgada koşullu projeler **yanmaz** (derlenmeleri garanti değil), yalnız kesin derlenecekler yanar. |

**Önerinin tek bedeli (3):** bağımlı proje, hatalı bağımlılık düzelene kadar son başarılı ama bayat çıktıya
bağlı kalır — bu zaten bugün de böyle (her Build'de yeniden derlense de aynı bayat çıktıya bağlanıyor). Yeni
bir risk eklemiyor, yalnız boşa derlemeyi kaldırıyor.

## 4. Plan (uygulama sırası — her madde kırmızı testle)

Kural: animasyonlar ve koreografi (dalga, sıralı teslim, beads, neon finali) DEĞİŞMEZ; değişen yalnız
"hangi node hangi renge ne zaman geçer" kararı.

**P1 — Kuyruk rengi koşunun planından gelsin (kök neden A; 1 ve 2'nin görsel yarısı)** · App
- Satıra "bu koşunun planında kuyrukta" bilgisi (ör. `InRunQueue`) eklenir; yalnız o koşunun `buildPreview`'ından
  yazılır, koşu başında (`NeutralizeRows`) ve koşu sonunda düşer. `Status`: `IsRunActive && InRunQueue → Queued`
  (bugünkü `WillBuild == true` yerine). `WillBuild` satırın karar etiketi için kalır.
- Tek proje koşusunda önizleme yalnız hedefi taşıdığı için diğerleri kendiliğinden gri kalır.
- `runStarted` ile `buildPreview` arasındaki bir anlık amber kaybolur (bilgi önizleme gelene kadar yok).
- Testler: tek proje koşusunda bayat bir diğer satır koşu boyunca `Discovered`; koşu sonu değişmez; graf aynı
  kararı okur.

**P2 — Resolve'da kuyruk yalnız üyeler, kapsam dışı "işlem dışı"** (kök neden B) · App (+ gerekirse Contracts)
- Cycles koşusunda `InRunQueue` yalnız döngü üyelerine yazılır; kapsam içi upstream gri bekler, `projectStarted`
  ile building'e geçer.
- `not needed by a dependency cycle` gerekçeli skip satırı `Skipped` görünümüne geçirmez, `Discovered` bırakır;
  sayaçlara/`N skipped`e girmez (stream'deki mevcut toplu satır davranışı korunur).
- Testler: Resolve'da bayat upstream koşu başında gri, başlayınca amber/building, bitince yeşil/kırmızı;
  kapsam dışı satır koşu boyunca ve sonunda nötr gri; sayaçlar kapsamla sınırlı.

**P3 — Koşullu yeniden derleme (kök neden D)** · Core + Supervisor + Contracts
- Defterdeki dep-issue notu yanında kök bağımlılık(lar) da tutulur (bugün `BuildState.DepIssue` yalnız bool).
- Planlama: dep-issue'lu başarı "kesin derlenecek" değil **"koşullu"** sayılır.
- Scheduler: koşullu proje, kök bağımlılığı bu koşuda başarıyla derlenmişse normal sırayla derlenir; değilse
  yeni bir gerekçeyle (ör. `dependency still failing`) atlanır ve not korunur.
- Önizleme koşullu projeleri kuyruk olarak göstermez (P1 ile gri kalırlar).
- Testler (Core, saf): kök hâlâ hatalı → bağımlı derlenmez, not kalır; kök bu koşuda başarılı → bağımlı onun
  arkasından derlenir, not düşer; kök kaynak değişmeden düzelirse (§8.3 senaryosu) bağımlı yine derlenir.

**P4 — Dalga ve etiket motorla aynı şeyi söylesin (kök neden C)** · App
- Koşu içi canlı geçiş (`RunViewModel.cs:1683`) dep-issue'lu başarıyı "up to date" yapmaz; etiket bekleyen
  durumu söyler (sözcük seçimi senin kararın — ör. `affected`, ya da `waits for <proje>`).
- `ScopeFor(Build)` yalnız kesin derlenecekleri işaretler (koşullular dalgada yanmaz).
- Testler: hatalı + dep-issue'lu senaryoda ikinci Build'in dalga kümesi, motorun kesin derleyeceği kümeye eşit.

**P5 — Doküman** · ARCHITECTURE §7.4 (will-build), §8.1/§8.3 (dep-issue, Cycles kapsamı), §13.2 (satır/kuyruk
görünümü), §14.3 (statü sözlüğü); README gerekiyorsa. Anlatı üslubu, changelog değil.

Önerilen sıra: P1 → P2 (yalnız görsel, düşük risk, en çok göze batan) → P3 → P4 (motor politikası, birlikte
ele alınmalı) → P5.

## 5. Karar vermen gerekenler

1. **P3 politikası:** koşullu yeniden derleme mi (önerim), yoksa "dep-issue'lu başarı hiç yeniden derlenmez"
   mi? İkincisi §8.3'teki güvenlik açığını geri açar (bağımlılık kaynak değişmeden düzelirse bağımlı sonsuza
   dek bayat kalır).
2. **P4 etiketi:** bekleyen bağımlı satırda ne yazsın? (`affected` mevcut sözlükte var; yeni sözcük sabit
   sözlüğü genişletir.)
3. **P2 sayaçlar:** Resolve'da kapsam dışı projeler `skipped` sayacına hiç girmesin mi? (önerim: girmesin,
   satırdan Build ile aynı.)

## 6. Doküman ↔ kod uyuşmazlıkları (bilgi)

- Tasarım README §3.8 satırdan Build kapsamını "hedef + bayat bağımlılıklar (transitif)" diye anlatıyor; kod
  (`ProjectRunScope`) ve tasarımın 1.11.0 sürüm notu "yalnız hedef" diyor. Senin tarifin de "tek satır" — kodla
  uyumlu; tasarım README'sinin o paragrafı eski kalmış görünüyor.
- Tasarım README §3.7 Resolve kapsamını "üyeler + bayat bağımlılık kapanışı" diye anlatıyor; kod "üyeler +
  transitif upstream, güncel olanlar pre-skip" yapıyor — fiilen derlenen küme aynı. Aynı paragraftaki
  "ardışık (paralellik 1)" iddiasını kodda doğrulamadım.

## 7. Kanıt dosyaları

| Konu | Yer |
|---|---|
| Kuyruk → amber kuralı | `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:215-230` |
| Queued = amber | `src/BuildOrchestrator.App/Controls/VisualStatus.cs:53-91` |
| Dalga kapsamı | `RunViewModel.cs:880-885` (`ScopeFor`), `:740-741` |
| Koşu aktifliği satırlara | `RunViewModel.cs:1282-1286` |
| Önizleme uygulaması | `RunViewModel.cs:1587-1606` (`OnBuildPreview`) |
| Canlı "güncel" geçişi | `RunViewModel.cs:1683` |
| Tek proje plan kesimi | `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:711-719`, `Core/Planning/ProjectRunScope.cs` |
| Cycles kapsamı ve pre-skip | `RunCoordinator.cs:749-829`, `Core/Planning/CycleRunScope.cs` |
| Önizleme yayını | `RunCoordinator.cs:897-921` |
| Dep-issue → derlenecek | `Core/Planning/WillBuildEvaluator.cs:63-79` |
| Skip → Skipped görünümü | `RunViewModel.cs:1628-1638` (`OnProjectSkipped`) |
