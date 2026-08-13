# Cycles UX — Analiz ve Yeniden Kurgu Planı

Kullanıcı şikâyeti: "Dairesel bağımlılık hiç doğru çalışmıyor — bazen bir anda derliyor, bazen aşırı uzun
sürüyor; satırlar animasyonlu yanıp sönüyor ama saat işareti var, yanında ünlem; grafikte turuncu düğümler;
Cycles + Build birlikte karmaşa/kaos."

Bu doküman üç şeyi yapar: (1) her algılanan sorunun kod karşılığını gösterir, (2) sahne sahne (Sync →
Cycles → Build) hedef UX'i kurgular, (3) geliştirme planını çıkarır.

---

## 1. Analiz — algı ↔ kod eşlemesi

### 1.1 "Satırlar yanıp sönüyor ama saat işareti var" — GERÇEK KUSUR

`IsCompiling` predicate'i (RunViewModel.cs:137) üç yüzeye bağlandı (Status, RunCounters, ribbon chip'leri)
ama İKİ yüzey unutuldu; ikisi de ham `State == Started` okuyor:

| Yüzey | Yer | Sonuç |
|---|---|---|
| Nefes katmanı | `ProjectRow.ApplyBreathing` — ProjectRow.xaml.cs:475 | Sırasını bekleyen HER üye saat (Queued) glyph'i gösterirken satır zemini amber "derleniyor nefesi"yle yanıp sönüyor. 15 üyeli grupta 15 satır aynı anda nefes alıyor — kullanıcının gördüğü tam olarak bu. |
| Süre sütunu | `ProjectRow.ApplyDuration` — ProjectRow.xaml.cs:316 | Bekleyen üye canlı elapsed sayıyor; üstelik `_projectStartedAtMs` her turda yeniden yazıldığı için (RunViewModel.cs:1049) sayaç her turda sıfırlanıp yeniden koşuyor. |

"Yanında ünlem" = dep-slot'taki turuncu döngü rozeti (`CycleMembershipTooltip`) — o doğru; sorun rozet değil,
nefes + saatin çelişkisi.

### 1.2 "Bazen bir anda derliyor, bazen aşırı uzun sürüyor" — DOĞRU DAVRANIŞ, ANLATILMIYOR

Üç ayrı yol var ve ekran hangisinin koştuğunu söylemiyor:

1. **Hepsi güncel** → bileşik imza temiz, tüm üyeler pre-skip → koşu ~1 sn'de biter ("bir anda derledi" algısı).
2. **Non-convergence memory** → önceki koşu NoProgress'le bittiyse aynı imzada üyeler hiç denenmeden atlanır
   ("cycle did not converge at this signature") → yine anında biter ama NEDENİ bambaşka.
3. **Kirli grup** → önce kirli transitif upstream (paralel), sonra her üye tur tur SIRALI derlenir; yakınsama
   iki ardışık yeşil tur ister (CycleRoundPolicy.BaselineRounds=2) → 15 üyeli grup = en az 30 sıralı MSBuild
   invoke ("aşırı uzun" algısı — gerçek maliyet bu, kusur değil).

Ekranda bu üç yolu ayırt ettiren TEK sinyal yok. Ek olarak grup kararı (converged / no progress / cap
reached) yalnız `decision.log`'a yazılıyor (RunCoordinator.RecordCycleOutcome) — UI'a hiç taşınmıyor.
Kullanıcının "tam anlayamadım" demesinin ana nedeni bu görünmezlik.

### 1.3 Stream skip satırı gerekçeyi YUTUYOR — GERÇEK KUSUR

`StreamText.Skipped` (StreamText.cs:29) her skip için sabit "skipped — up to date" basıyor;
`ProjectSkippedEvent.Reason` alanı stream'e hiç ulaşmıyor. Sonuç:

- Cycles koşusunda kapsam dışı ~150+ proje "up to date" diye akıyor (gerçek gerekçe: "not needed by a
  dependency cycle") — hem YANLIŞ hem fırtına.
- Build'de döngü üyeleri "in dependency cycle" gerekçesiyle atlanıyor ama stream yine "up to date" diyor —
  kullanıcı "Build döngülere dokunmaz" kuralını ekrandan hiç okuyamıyor.
- "cycle did not converge at this signature" pre-skip'i de görünmez.

### 1.4 Grafikte turuncu düğümler — TASARIM DEĞİŞİKLİĞİ İSTEĞİ

Bugün `GraphStatus.Cycle` düğümü komple turuncuya boyuyor (GraphView.ApplyNodeStatus:686 — turuncu çerçeve +
zemin + ikon). Koşudan sonra düğüm kırmızı/yeşil olunca üyelik graftan TAMAMEN kayboluyor. Kullanıcının
istediği: düğüm HER ZAMAN standart statü tasarımında kalsın; döngü üyeliği düğümün köşesinde kalıcı, kibar,
minik bir işaretle anlatılsın — her sahnede görünür, statüyle yarışmaz.

### 1.5 Süreç mantığı — DOĞRU, DEĞİŞMEYECEK

Kullanıcının istediği akış bugün zaten motorda böyle: Build döngü üyelerini asla derlemez; Cycles yalnız
üyeleri + kirli transitif upstream'i derler; başarılı yakınsama imza persist eder, sonraki Build/Cycles
onları güncel sayar; kaynak değişince imza kirlenir ve Cycles yeniden derler. Motor tarafında davranış
değişikliği YOK — bu iş tamamen sunum katmanı.

---

## 2. Hedef UX — sahne sahne

### Sahne 0 · Sync sonrası (idle)

- **Liste (değişmez):** will-build noktaları; döngü üyelerinde turuncu üçgen glyph (koşu-öncesi ifade) —
  kullanıcı bu hâli beğendi.
- **Graf (değişir):** döngü düğümü artık turuncuya boyanmaz. Standart discovered (kesikli çerçeve) görünümü +
  köşede kalıcı **mini döngü rozeti** (Icon.StatusCycle geometrisi, ~8-9px, turuncu). Rozet `InCycle`
  düğümlerde HER statüde durur — discovered'da da, building'de de, succeeded/failed'da da.
- **Buton (değişmez):** Cycles butonu döngü varsa etkin; tooltip zenginleşebilir ("2 cycles · 17 projects").

### Sahne 1 · Cycles'a tıklama

- **Stream açılışı** kapsamı kırılımıyla söyler:
  `Cycles started — 17 cycle members · 5 prerequisites · up to 3 rounds`
  (üye sayısı `_cycleGroups`'tan, prerequisite = willBuild ∖ üyeler; App'te hazır veri).
- **Kapsam dışı skipler stream'de TEK satıra toplanır:**
  `154 outside cycle scope — skipped`
  (motor eventleri değişmez — satırlar/sayaçlar yine proje başına işler; yalnız stream katmanı
  "not needed by a dependency cycle" gerekçeli skipleri sayıp özetler).
- **Diğer skipler gerçek gerekçesiyle satır satır:** `{name} skipped — up to date` /
  `{name} skipped — cycle did not converge at this signature`.

### Sahne 2 · Koşu sırası

- **Liste:** yalnız GERÇEKTEN derlenen üye nefes alır + spinner (IsCompiling); bekleyen üye saat glyph'i,
  nefes YOK, süre `—` (canlı elapsed yalnız IsCompiling'de). Terminal olunca gerçek toplam süre yazılır
  (turların toplamı — motor zaten öyle raporluyor).
- **Stream tur satırı:** `cycle round 1/3 — 15 members` (lider adı yerine üye sayısı; lider adı tıklanabilir
  ProjectId olarak kalır).
- **Aktif satır:** `{name} building… · member 3/15 · round 1/3` — RunViewModel cycles koşusunda
  CycleRoundStartedEvent + ProjectStartedEvent sayarak indeksi türetir.
- **Graf:** aktif üye parlak + beads; bekleyenler queued-dim (mevcut opaklık sistemi); mini rozet hep görünür.

### Sahne 3 · Koşu bitti

- **Liste/graf:** üyeler kırmızı/yeşil, standart ikonlar; üyelik listede dep-slot rozeti, grafta köşe
  mini-rozeti. (Liste tarafı bugün böyle; graf rozeti yeni.)
- **Stream grup karar satırı (YENİ):** grup biter bitmez tek satır:
  - `cycle converged — 15 members · 2 rounds · 1m 40s` (Ok/yeşil)
  - `cycle failed — no progress, same 3 members failing · 2 rounds` (Fail/kırmızı)
  - `cycle round cap reached — output may be one generation behind · 3 rounds` (Info/amber)
  Bunun için yeni IPC eventi gerekir: `CycleCompletedEvent(RunId, ProjectId lider, Outcome, MemberCount,
  Rounds, DurationMs)` + Contracts'a `CycleOutcome` enum'u (camelCase metin — wire-safe append).
  Aynı karar `console(...)` kanalıyla run konsoluna da düşer (decision.log'daki satırın eşi).
- **Completed satırı (değişmez):** `Completed — 15 succeeded · 154 skipped · 1m 12s`.

### Sahne 4 · Sonrasında Build

- **Motor değişmez.** Döngü üyeleri "in dependency cycle" ile atlanır; stream artık bu gerekçeyi GÖSTERİR:
  `{name} skipped — in dependency cycle` → kullanıcı ayrımı her Build'de okur.
- **Yönlendirme ipucu (nice-to-have):** Build bittiğinde dirty döngü üyesi varsa (`InCycle &&
  WillBuild == true`) stream'e tek Info satırı: `2 cycle projects have pending changes — run Cycles`.
  Kullanıcının "hata aldım, döngüye bağlıymış, tekrar çöz demem lazım" senaryosunun ekrandaki karşılığı.

---

## 3. Bulgular — öncelik sırası

**Bloklayıcı (kaos hissinin doğrudan kaynağı):**
- B1. Nefes + süre yüzeyleri `IsCompiling` okumalı (ProjectRow.ApplyBreathing / ApplyDuration).
- B2. Stream skip satırı `Reason`'ı basmalı; Cycles'ta kapsam-dışı skipler tek özet satıra toplanmalı.

**Önemli (görünürlük/anlatı):**
- Ö3. `CycleCompletedEvent` + stream grup karar satırı + konsol satırı.
- Ö4. Cycles açılış satırı kırılımı (members/prerequisites), tur satırı `N members` formu, aktif satırda
  `member i/N · round r/cap`.
- Ö5. Graf: `GraphStatus.Cycle` düğüm boyaması kalkar (standart discovered görünümü); `InCycle` düğümlere
  kalıcı köşe mini-rozeti.

**Kozmetik:**
- K6. Build sonu "run Cycles" ipucu satırı.
- K7. Cycles butonu tooltip'ine döngü/üye sayısı.

## 4. Geliştirme planı

Branch: `fix/cycles-ux-clarity`. Her madde kırmızı test kuralına tabidir; davranış değiştiren her madde eski
testi yeni kuralı pinleyecek şekilde yeniden yazar (eski iddia + gerekçe doc'una).

| Task | İş | Test |
|---|---|---|
| T1 | ProjectRow nefes + süre → `IsCompiling` | Kırmızı: CycleWaiting satırda BreathLayer Visible / elapsed sayıyor; yeşil: yalnız IsCompiling'de |
| T2 | `StreamText.Skipped(name, reason)`; RunViewModel stream katmanında Cycles kapsam-dışı skip toplayıcı (RunCompleted'da/ilk build satırından önce tek özet satır) | EventStreamTests: reason birebir; 154 skip → 1 satır |
| T3 | Contracts: `CycleOutcome` + `CycleCompletedEvent`; RunCoordinator RecordCycleOutcome yayını + console satırı; RunViewModel.Stream karar satırı | IpcMessagesTests roundtrip; CycleRoundsTests event sırası; EventStreamTests üç varyant metin |
| T4 | CyclesStarted kırılımı + CycleRound "N members" + aktif satır member/round indeksi | EventStreamTests + StreamComposer/aktif satır testleri |
| T5 | Graf mini-rozet: GraphNode.InCycle; ApplyNodeStatus Cycle→discovered görünümü; köşe rozeti her statüde | GraphView testleri + realize testi (yeni görsel öğe) |
| T6 | Build sonu ipucu satırı + Cycles buton tooltip sayıları | EventStreamTests / ActionBarTests |
| T7 | Dokümanlar: ARCHITECTURE §8/§13/§14 (graf rozeti, stream satırları, karar eventi), README kısayol/akış | Tam süit yeşil |

Bitiş: tam filtreli süit yeşil, `main`'e merge + push, branch silinir.
