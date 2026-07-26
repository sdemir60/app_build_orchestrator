# It-5 tamamlandı — main'e merge edildi (v7 Part C, son iterasyon)

**Tarih:** 2026-07-26 11:33 · **Branch:** `it5-perf-dist` (merge sonrası silindi) → **`main` @ `6c173f2`**
**Süit:** 1430 passed / 2 skipped / 0 failed · build 0 error / 0 warning
**Ölçek:** 38 commit / 103 dosya / +10925 −931

---

## 1. Ne yapıldı

v7 Part C It-5 planı (`.claude/outputs/2026-07-25-13-40-it5-tdd-plan.md`) 14 task olarak yürütüldü.
Her task: taze implementer → 3-lens paralel review (spec/tasarım · WPF-threading/A13.2 · testler/yapı) →
fix wave → scoped re-review → ledger. Sonda whole-branch review + fix + re-review.

### FAZ P — perf motoru (K11 + T20)
- **P1** `JobObject.SetCpuRate/ClearCpuRate/QueryCpuRate` + `SetPriorityClass` (Query→OR→Set ile
  `KILL_ON_JOB_CLOSE` korunarak); `PerfProfile`: Full(6, ∞, Normal) / Balanced(4, %70, BelowNormal) /
  Light(2, %40, Idle).
- **P2** PerfMode uçtan uca IPC (`StartRunCommand.PerfMode`, `SetPerfModeCommand`,
  `RunStartedEvent.CpuCapPercent`); cap + priority **canlı** değişir, parallelism run başında sabitlenir.
- **P3** copy-contention (MSB302x) penceresinde cap+priority **ref-count'lu** olarak Balanced tabanına
  gevşer; cap-farkındalı backoff (×1.5).
- **T20'nin çekirdek cevabı:** cap **yalnız inner Job'a** yazılır; git/vswhere düz `Process.Start` ile
  koşup inner job'a hiç girmez → **Sync/IPC aç kalır**.

### FAZ L — liste (It-4 kabulünün #5 kalemi)
- **L1** satır kartında hover ikonları + VS-chooser **tembel** kuruluyor.
  **787,3 → 487,5 ms** (medyan, 191 satır); satır başına nesne **55 → 39**.
- **400 ms bütçesi TUTMADI** (87 ms açık). **Kullanıcı kararı: L2 (virtualization) AÇILMADI** — gerçek
  senaryo 191 satır ve bu tek seferlik ilk-realize maliyeti; virtualization sticky overlay + LayoutMetrics
  + FollowScrollController + ScrollArbiter'ı aynı anda riske atıyor ve It-5 son iterasyon.

### FAZ G — graf
- **G1** sentetik büyük graf + tekrarlanabilir ölçüm zemini; `CanvasWidth` sabit 880'den düğüm sayısına
  göre türetiliyor (36 düğümde tam 880'de kalır — eşik n=24→25). **Perf açısından nötr**, teslim ettiği
  şey doğruluk + ölçüm zemini.
- **G1'in maliyet kırılımı G2'nin nişanını belirledi:** `SetGraph` görsel-ağaç kurulumu %64-72 ·
  Measure/Arrange %28-36 · saf layout aritmetiği **%0,03**. → **STOP GATE kapatıldı: `DrawingVisual`
  göçü YAPILMADI** (göç çizimi hedefler, darboğaz nesne kurulumu).
- **G2** viewport cull + tembel rozet + **ölçülen genişlikten türetilen etiket LOD** + tooltip.
  1000 düğüm **934,8 → 136,0 ms** (ilk görünür alan, 6,9×) / **469,1 ms** (tüm graf gezildiğinde, 2,0×);
  500 düğüm 394,1 → 91,5 (4,3×) / 206,2 (1,9×). Düğüm başına nesne **17 → 9**.

### FAZ W
- **W1** proje başına commit sha uçtan uca (`BuildPreviewItem.BuiltCommit`). Sıra hatası yamayla değil
  **veri yönü tersine çevrilerek** kapatıldı (pull → push), böylece olay sırası önemsizleşti.
- **W2** motion seam'leri katlandı: `App.Motion?.AnimationsEnabled ?? false` **9 kopya → 1**,
  subscribe-once **5 → 1**, reveal muhasebesi **2 → 1**, popover iskeleti **2 → 1**. Davranış nötr
  (fold'dan önce pin yazılıp eski kodda yeşil doğrulandı).

### FAZ D — dağıtım
- **D1** `dotnet publish` **kırıktı** — `supervisor\` publish çıktısına hiç girmiyordu, engine başlamıyordu
  ve kullanıcıya hata bile verilmiyordu. Düzeltildi + engine eksikken görünür hata + `supervisor` adı tek
  kaynağa indirildi. **`scripts/verify-publish.ps1`** (16 check + ön koşul) uçtan uca doğruluyor;
  **§3 cascade ölçülmüş kanıt** (App pid öldürülür, supervisor child'a dokunulmaz, kendiliğinden ölür).
  Ayrıca **77 Türkçe kullanıcı metni İngilizce'ye çevrildi**.
- **D2** `docs/TRUST-BOUNDARY.md` (v7 T17).
- **D3** `README.md` (repoda ilk kez).

### T1 — T49 token FINAL PASS + hijyen
- **Gerçek drift = 1 adet** (`FontAbWindow.xaml.cs:34` `#2a2a30` → `#3a3a42`). Token katmanı otoriteyle
  birebirdi (56/56 brush, 7/7 motion); kusur drift'in **görünmemesiydi**.
- Realize testleri (**`MainWindow` dahil**); renk/motion/**D8 sleep-poll** kaynak guard'ları;
  `BuildStateStore`'daki `Thread.Sleep` → enjekte gecikme seam'i.

### V1-V3
- **V1** T44 doğrulandı (yeniden yazılmadı) + anti-drift kilidi. **V2** T33 kararı: shared compilation
  **KAPALI** (torn-DLL riski). **V3** kabul kaydı + görsel kontrol listesi.

---

## 2. Üretilen belgeler

| Dosya | İçerik |
|---|---|
| `README.md` | Repoda ilk kez; mimari, komutlar, publish, K11 perf modları (üç dürüst nitelemesiyle) |
| `docs/TRUST-BOUNDARY.md` | Process/IPC/dosya sistemi/git/CPU sınırları + "ne korunmuyor" |
| `.claude/outputs/2026-07-26-10-17-it5-records.md` | It-5 kabul kaydı (her kalem kanıtıyla) + park listesi |
| `.claude/outputs/2026-07-26-10-17-visual-check-walkthrough.md` | **Kullanıcının yürüyeceği görsel kontrol listesi** |
| `.claude/outputs/2026-07-26-07-38-t33-decision.md` | T33 karar kaydı (KAPALI + yeniden açılma koşulları) |
| `scripts/verify-publish.ps1` | Publish çıktısını uçtan uca doğrulayan script |

---

## 3. Kullanıcı kararları

1. **L2 virtualization AÇILMADI** — 487 ms kabul edildi (bütçe tutmadı), gerekçesiyle kayda geçti.
2. **`CLAUDE.md`'deki 3 bayat ifade düzeltilmedi** — ayrı ele alınacak; karşılaştırma tablosu ve kanıtlar
   playbook'a **`A11`** adımı olarak eklendi (`dotnet build` → gerçekte `MSBuild.exe` · tests TFM
   `net10.0` → `net10.0-windows`+UseWPF · "Kod henüz yoktur" satırı).

---

## 4. Final whole-branch review

**Karar 1: `MERGE WITH FIXES`** (0 Critical / 2 Important) → fix `1546783` → **Karar 2: `READY TO MERGE`**
(0 Critical / 0 Important).

İki Important da tek-task review'ının göremeyeceği cinstendi:
1. **`EffectivePriorityLocked` drain'e bakmıyordu** → §3'ün "torn DLL yok" penceresinin yalnız yarısı
   korunuyordu. **Bu kalem P3'te Minor diye park edilmişti**; park kararından *sonra* yazılan
   TRUST-BOUNDARY + README garantiyi kodun verdiğinden geniş anlatınca yanlışa döndü.
   → **Ders (ledger'da):** park edilmiş bir kalem, sonradan yazılan dokümantasyon onu iddia haline
   getirdiğinde yeniden açılmalıdır.
   Çözüm taslağımdan iyi çıktı: hard-pin yerine **clamp** — cap'in nötrü "cap yok", priority'ninki
   "tabandan kötü değil", böylece `Full`'ün `Normal`'i korunur, `Light`'ın `Idle`'ı yazılamaz.
2. **`RestartEngineAsync` D1'in kendi değişmezini bozuyordu** (generic catch `EngineUnavailableException`'ı
   ayırt etmiyordu) → eksik kurulumda "Restart engine" sonsuza dek sunuluyor, komutlar açık kalıyordu.

**Park kalemlerinin birleşik riski:** tek gerçek küme **a11y** (graf düğümlerinde
`AutomationProperties.Name` yok + etiket LOD'un tek yedeği fare-hover tooltip + düğümler klavyeyle
gezilemiyor). It-5'in getirdiği gerileme değil, LOD'un görünür kıldığı ürün-seviyesi boşluk; merge'i
bloklamaz.

---

## 5. Süreç dersleri (ledger'da kayıtlı)

- **Aynı worktree'de iki implementer paralel koşturulmaz.** It-5'te W2-fix ile D1 paralel koştu; W2
  implementer'ı `git add -A` ile D1'in yarım csproj değişikliklerini kendi commit'ine kattı, ağaç bir süre
  derlenmez kaldı. Read-only reviewer'lar paralel sorunsuz; **implementer'lar seri**.
- **"İddiayı kurtarma, ne çıkarsa onu yaz"** talimatı iki kez guard'ı gerçekten güçlendirdi: T1'de
  implementer kendi "3/3 kırmızı" iddiasını ölçerek çürüttü (`Window.Measure/Arrange` HWND'siz içeriğe
  inmiyor) ve realize testleri `window.Content` üzerine alınarak gerçekten güçlendi.
- **Guard'ın yeşil olması bir şeye baktığı anlamına gelmiyor:** T33 "tek kaynak" pini repo kökünü
  taramadığı için **sıfır dosya** tarıyordu; artık sıfır-dosya durumu RED veriyor.

---

## 6. Kalan (bilinçli, kayıtlı)

L2 açılmadı · `DrawingVisual` göçü yapılmadı · W2'de guard'ın 4 + primitifin 3 kopyası katlanmadı ·
`Show()` başlatma yolu realize kapsamı dışı · `debugSpawnChildren` üretimde dinleniyor · a11y kümesi ·
CLAUDE.md'nin 3 bayat ifadesi (playbook `A11`) · ~60 minor (tam liste `it5-records.md` park tablosunda).

**GÖZLE KONTROL PASI KULLANICIYA AİT** — harness ekran görüntüsü alamaz.
Liste: `.claude/outputs/2026-07-26-10-17-visual-check-walkthrough.md` (81/81 kalem, D4 zorunlu,
design-v1 §2.1-§2.9 yan yana karşılaştırma, It-5'in kendi görsel kalemleri).
