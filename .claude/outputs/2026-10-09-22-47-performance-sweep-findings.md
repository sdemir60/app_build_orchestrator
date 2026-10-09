# Gece performans taraması — masaüstü (UI akıcılığı) + motor (Clean / Resolve / Build / Rebuild)

**Tarih:** 2026-10-09 22:21–23:10 · **Kod:** `perf/overnight-sweep-2026-10-09` = develop `dde3b670` (kod değişmedi; yalnız ölçüm
betikleri ve bu rapor) · **Ortam:** kullanıcı uzakta, masaüstü kilitsiz, keep-awake açık; OSYS temiz çalışma ağacı
(`feature/work-order-line-price-rules`, bugün 17:08–18:21 arasında 8 commit); Release build; ayar Balanced, harici kök
güncellemesi açık · **Araçlar:** `.claude/temp/perf-sweep-2026-10-09/` (`sweep.ps1`, `spike-probe.ps1`, `corespeed-watch.ps1`,
`tooltip-focus-repro.ps1`, `hover-keys.ps1`, `backup.ps1`, `frames_summary.py`, `thread_top.py`) +
`perf-2026-10-01/measure2/probe-helper.ps1` ve `perf-2026-10-06/sys-watch.ps1` · **Ham veri:** `.claude/temp/perf-sweep-2026-10-09/s1`
(sweep), `s2` (spike + izli Rebuild + örnekleyiciler + `app-rebuild-heavy.nettrace`), `s3`–`s5` (tooltip repro, temiz tepsi),
`s6` (fare konumu × F5, ekran görüntüleri).

## 1. Özet — bulgular öncelik sırasıyla

| # | Öncelik | Bulgu | Kanıt |
|---|---|---|---|
| B1 | **bloklayıcı (klavye)** | **Fare projeler listesinin üstündeyken (katman başlığı ya da satır) F5 pencereye ulaşmıyor — Build başlamıyor.** Fare grafın boş alanındayken aynı tuş hemen koşu başlatıyor; fare listeden çekilince (Esc gerekmeden) düzeliyor. Global kısayol Shift+Space etkilenmiyor. Koşu sırasında liste kendi kaydığı için duran fare bir satırın/başlığın üstüne "geliyor"; koşu bitince kullanıcı F5'e basar, hiçbir şey olmaz. s1'de ayrıca `Jump to <katman>` tooltip Popup'ının UIA klavye odağını aldığı görüldü. | §6.2: 5 konum × F5, deterministik (liste üstünde 3/3 başlamadı, graf/park 3/3 başladı); §6.1 UIA odak kaydı |
| Ö1 | önemli (akıcılık) | **Build/Rebuild sırasında kare boşlukları 350–720 ms**, 5 s'lik pencerelerde 14–35 fps. UI thread'i bloklanmıyor (WM_NULL gidiş-dönüş p99 ≤ 53 ms, max ≤ 105 ms); yani tek bir uzun iş değil, **her karenin pahalılaşması** (layout/çizim) ya da render/kompozisyon hattının takılması. Hep aynı anda: OSYS.UI katmanının büyük projeleri (Reception/Workshop/CRM/PRM; Report'lar; UI.DMS döngü turu) derlenirken, olay gelmezken. | §4 tablo; iz analizi §4.2 |
| Ö2 | önemli (hissedilir) | **Her koşu başlangıcında** UI thread'inde 115–145 ms'lik tek bir iş (F5'ten ~5 s sonra, `runStarted`/önizleme anı) + 210–260 ms'lik kare boşluğu. Tıklamanın kendisi 31–55 ms. | §5 spike sondası (iki koşu, aynı desen); sweep'te 258 ms boşluk + 116 ms gecikme |
| Ö3 | önemli (kaynak) | Koşu sürerken App'in kendisi 1–1,5 çekirdek yakıyor (`sys-watch`: %64–147 of one core): UI 1,0–1,3 G cycle/s + render 1,5–1,6 G/s + **finalizer thread'i 30 s'nin 16 s'sinde CPU'da** (DirectWrite font handle'ları — kare başına metin biçimleme çöpü). Kare sondası açık ölçüldü (render payı şişer); finalizer ve UI payı sondadan bağımsız. | §3 `app threads`, §4.2 iz |
| K1 | kapandı | Tepside boşta UI thread'i temiz tekrarda 11,2 M/s (10-06 düzeltmesiyle aynı). s1'deki 64 M/s B1'in bozduğu durumdaydı. | §6.3 |
| — | araç kusuru DEĞİL | Her koşuda 3 proje düşüyor: `OSYS.Orchestration.Accounting.Common` (CS1061), `…SparePart.Common` ve `…NewSales.Sales` (CS0104 belirsiz ad) — kullanıcının bugünkü commit'lerinin derleme hataları. Bu üçü her Build'de yeniden denenir (~1,5 s). | proje logları |

Motor tarafında yeni bir darboğaz bulunmadı; sayılar önceki ölçümlerle tutarlı (§7). Doküman iddiaları kodla uyumlu (§8).

## 2. Yöntem

Tek görünür oturum (`sweep.ps1`): uygulama → 35 s → boşta sonda → F5 Build (kaynak bugün değiştiği için 60 proje derledi) → F7
Clean → Resolve cycles (UIA Invoke) → F5 Build (çıktıları geri kurar) → F6 Rebuild → F5 Build → tepsi → göster → kapat. Her
fazda eşzamanlı iki sonda: (a) **UI gecikmesi** — pencereye 30 ms'de bir `WM_NULL` `SendMessageTimeout`, gidiş-dönüş süresi
(UI thread'i mesaj işleyebiliyor mu); (b) **kare sondası** — `BO_PROBE_FRAMES` (uygulama içi, `CompositionTarget.Rendering`
aralıkları, 5 s pencere). Ayrıca thread başına cycle (UI / wpfgfx render / diğer), Supervisor çocukları, makine CPU'su.
İkinci oturum (`spike-probe.ps1`): koşu başlangıcındaki her 8 ms üstü gecikmeyi zaman damgasıyla kaydeden sonda (10 ms
aralık) + kare sondalı Rebuild'in büyük-UI-projeleri penceresinde 30 s `dotnet-trace` (sampled thread time); yanında
`sys-watch.ps1` (süreç başına CPU, Defender dahil) ve `corespeed-watch.ps1` (çekirdek başına 120 ms döngü sayısı — frekans
düşüşü / yavaş çekirdek şüphesi için). Üçüncü oturum: B1 için repro (§6.2) + temiz tepsi ölçümü.

Disk disiplini: Clean derlenemeyen projelerin eski çıktılarını siler; bu yüzden önce `backup.ps1 save` (iki bozuk projenin
bin/obj'si + `C:\OSYS\{Server,Client}\Bin` + defter dosyaları, `D:\bo-perf-snap\sweep-2026-10-09`), sonunda `restore`
(bozuk projeler aynen; paylaşılan Bin'lere yalnız Clean'in sildiği dosyalar geri eklenir, son Build'in yazdıkları kalır).

## 3. Sweep sonuçları (s1, 22:27–22:45)

| Faz | Motor süresi | Duvar (tuş→bitiş) | Derlenen / atlanan | UI gecikme p95 / p99 / max (ms) | Kare: ort. fps · en uzun boşluk · >100 ms · >250 ms |
|---|---|---|---|---|---|
| Açılış | — | pencere 861 ms | — | boşta p99 0,7 / max 1,0 | ilk 5 s: 251 ms boşluk (açılış) |
| Build (kaynak değişmiş) | 36,6 s | 44,8 s | 60 / 124 (3 hata, 1 dep-issue) | 0,5 / 2,9 / **116** | 103 · **258** · 1 · 1 |
| Clean (187 proje) | 25,8 s | 41,4 s | 187 / 0 | 6,2 / 12,9 / 66 | 95 · 115 · 1 · 0 |
| Resolve cycles (Clean sonrası) | 122,4 s | 136,7 s | 83 / 104 | 9,1 / 31,5 / 95 | 78 · 114 · 4 · 0 |
| Build (Clean sonrası) | 86,8 s | 136,9 s | 101 / 83 (3 hata) | 14,5 / 38,1 / 105 | 76 · **360** · 28 · 3 |
| Rebuild | 204,9 s | 213,4 s | 184 / 0 (3 hata) | 17,8 / **52,7** / 105 | 72 · **720** · 80 · 15 |
| Build (değişmemiş) | — | — | — | — | **koşu başlamadı (B1)** |
| Tepsi boşta | — | — | — | p99 0,7 | UI 64 M/s (K1) |

Duvar süresi = tuş → `decision.log` `finished` + sweep'in bekleme adımları; motor süresi `duration=`. Boşta (koşu yokken)
77 pencerede yalnız 2 boşluk > 100 ms (biri açılış). Koşu sürerken App thread'leri: UI 0,97–1,26 G/s, render 1,53–1,62 G/s
(sonda açık), boşta 67 / 58 M/s. Supervisor çocukları fazlara göre: Clean'de 4 MSBuild; Resolve'da 2 MSBuild + 2 csc (toplam WS
1,9 GB); Build'de 4 MSBuild + csc (tek csc WS 819 MB'a çıktı).

## 4. Kare boşlukları — nerede, ne zaman

| Pencere (5 s, bitiş) | Faz | fps | en uzun boşluk | >50 / >100 / >250 | O anda motorda |
|---|---|---|---|---|---|
| 22:33:44,8 | Build (Clean sonrası), F5+64 s | 34,6 | 360 ms | 21 / 13 / 3 | Reception, Workshop, CRM, PRM derleniyor (14–18 s'lik 4 derleme), olay yok |
| 22:33:49,8 | aynı | **18,3** | 128 ms | 54 / 10 / 0 | aynı |
| 22:33:54,8 | aynı | 57,1 | 127 ms | 27 / 1 / 0 | 22:33:52–54 dört proje bitti |
| 22:36:30,2 | Rebuild, F6+79 s | 69,6 | 224 ms | 13 / 8 / 0 | Orchestration katmanı biterken (20 s'de ~30 tamamlanma) |
| 22:36:35,2 | aynı | **13,9** | 346 ms | 41 / 11 / 3 | UsedCars.AnalyticalReport, NewSales.Print, Report.Resources derleniyor, olay yok |
| 22:36:40,2 | aynı | 30,6 | 146 ms | 34 / 5 / 0 | aynı |
| 22:37:25,5 | Rebuild, F6+134 s | 34,4 | 307 ms | 19 / 9 / 4 | UI.DMS döngü turu (17 WPF üyesi) |
| 22:37:30,5 | aynı | 34,1 | 255 ms | 32 / 3 / 1 | aynı |

Aynı pencerelerde UI gecikme sondası: p99 16–53 ms, max 74–105 ms, hiç > 250 ms yok. Yani UI thread'i 300–700 ms boyunca
bloklanmıyor; kareler yine de düşüyor. Konsol taşması değil: o anda derlenen projelerin logları küçük (Reception 443 satır / 3
uyarı; en büyük log NewSales.Report 2.679 satır, o başka bir pencerede). Tamamlanma patlaması da değil (en kötü iki pencerede
olay yok). Kalan hipotezler: (i) her karede pahalı bir iş — graf'ta "derleniyor" düğümlerinin orbit animasyonu / satır animasyonları
sayısı ve süresiyle büyüyen bir maliyet; (ii) 3–4 çok-thread'li csc + MSBuild (BelowNormal, %70 tavan) + Defender yanında App'in
UI/render thread'lerinin P-çekirdek yerine LPE çekirdeklere düşmesi ya da güç/termal frekans düşüşü (makine CPU %77–81).
(ii) için `corespeed-watch` ve `sys-watch`, (i) için 30 s'lik `dotnet-trace` alındı — §4.2.

### 4.2 İz ve örnekleyici sonuçları (s2, Rebuild 22:46:49–22:49:52, 175 s)

**Kare sondası (s2)** aynı deseni tekrarladı: 22:49:26,8 penceresi 54,7 fps · en uzun boşluk **476 ms** · 9 > 100 · 2 > 250; sonraki
pencere 27,1 fps · 58 kare > 33 ms. Yine UI.DMS döngü turu sırasında.

**`corespeed-watch` (Normal öncelikli bir thread'in çekirdek başına 120 ms'de yaptığı döngü sayısı; boşta ≈ 85–92 bin):**
koşu boyunca P-çekirdeklerde (cpu0–3) 40–65 bin, LPE'lerde (cpu4–7) 70–85 bin — yani BelowNormal + %70 tavanlı derleyiciler
yanında Normal öncelikli bir thread P-çekirdeğin yalnız **%45–70'ini** alabiliyor (öncelik tam üstünlük vermiyor). En kötü
pencere 22:49:24,8: makine %99,8, **sekiz çekirdekte de 100–2.900 döngü** (boştakinin %0,1–3'ü) — ~1 s boyunca Normal öncelikli
iş neredeyse hiç koşmadı; kare sondasının 476 ms'lik boşluğu bu pencerede. `sys-watch` aynı anda: App %147 (UI thread
tam dolu + render), MsMpEng %75, csc %68, **System %57, Memory Compression %38**. O sırada derleyicilerin WS'si küçüktü
(MSBuild 28–73 MB, csc 231 MB) ve koşu sonunda boş fiziksel bellek 13 GB, PagesPerSec 0 — kalıcı bellek baskısı yok; anlık
"System + Memory Compression" dalgası (çekirdek tarafı sayfa işi) ile makinenin tümden duraksadığı bir an. Defender koşunun
ilk 70 s'sinde 1–2 çekirdek (%85–191) kullandı; onun dışında App hep en büyük tüketici (%64–147 of one core, sonda açık).

**`dotnet-trace` (30 s, 22:47:39–22:48:10; Orchestration → UI geçişi, kareler 64–110 fps — en kötü pencereyi tutturamadı):**
UI thread'i %26,8 meşgul; en uzun kesintisiz dilim 92 ms (sonra 57, 55, 48…). Meşguliyetin **%50,7'si animasyon kaynaklı render
geçişi** (`AnimatedRenderMessageHandler`), %37,3'ü geçersizleşme kaynaklı render/layout; iş türü: render %24, **metin biçimleme
%21**, GC %17, Monitor kilidi %15, animasyon saatleri %9; uygulama kodu %4 (en büyükler `EventStreamView.MirrorWritingToPrompt`
50 ms, `ProjectRow.OnPropertyChanged`, `FixedHeightVirtualizingPanel`, `GraphView.ApplyStatuses`, `StickyRibbon.ReconcileChips`
— hepsi ≤ 16 ms). Yani koşu sırasında UI thread'inin yükü uygulama işleyicileri değil, **her karede koşan animasyonların
tetiklediği render + metin biçimleme**. Aynı izde **finalizer thread'i 30 s'nin 15,9 s'sinde CPU'da** (`System.GC.RunFinalizers`,
3,7 s'si `IDWriteFont` handle serbest bırakma) — sondalardaki "others ≈ 0,5–0,8 G/s" bu thread: kare başına metin biçimleme
DirectWrite font nesneleri yaratıp finalizer'a bırakıyor. Hangi metnin her karede yeniden biçimlendiği bu izden çıkmıyor
(uygulama çerçevesi yok); adaylar §10-2'de.

## 5. Koşu başlangıcı (s2 spike sondası)

İki ardışık Build (kaynak değişmemiş; 3 bozuk proje yeniden denendi, koşu 1,5 s):

| Koşu | F5 anı | Tıklama işi | Büyük iş | Koşu sonu işleri | Kare boşluğu (o pencere) |
|---|---|---|---|---|---|
| A | 22:45:47,770 | 54,6 ms | **145,4 ms** @ +5,19 s | 8–40 ms × 6 | 262 ms |
| B | 22:46:18,668 | 30,9 ms | **114,9 ms** @ +5,03 s | 8–29 ms × 4 | 212 ms |

Büyük iş F5'ten ~5 s sonra, `runStarted` + `buildPreview`'un geldiği anda (planlama: sync + içerik kararı ~5 s). Boşta aynı
sondada 329 örnekte max 1,0 ms. 10-06 raporu bu dilimi "koşu başı satır inşası 100–230 ms" diye adlandırmıştı; G2 (kalıcı
satırlar) sonrasında da 115–145 ms kalıyor.

## 6. B1 — tooltip klavye odağını alıyor

### 6.1 Gözlem (s1)
Rebuild bitti (22:38:43). Sweep 22:38:57'de F5 gönderdi (ön plan doğrulandı), hiçbir koşu başlamadı (log dizini, inflight,
Supervisor çocukları: yok). 22:42:44'te elle ikinci F5: yine yok. Ekran görüntüsü: pencere açık, liste `OSYS.UI` katmanında, fare
bir satırın üstünde, satırın hover aksiyonları ve `Jump to OSYS.UI` pill'i görünür. UIA:

```
focused: name='' class='Popup' type='ControlType.Window' pid=14956 (app pid)
focused rect=642,1165,140,31   <- tam olarak 'Jump to OSYS.UI' pill'inin dikdörtgeni
Build/Clean/Resolve cycles düğmeleri: enabled=True
```

22:44:10 Shift+Space (global hotkey) pencereyi gizledi — klavye yolu değil, odak yolu kırık. Kod: `StickyLayerList.xaml.cs`
`JumpTooltipConverter` → katman başlığının `ToolTip`'i (`Controls.xaml:1573` ToolTip stili); `InteractionText.JumpToLayer`.
Dokümanda (§13.2) "native `Jump to <layer>` tooltip" diye geçer; native ToolTip odak almaz — stil/şablon ya da başlığın kendisi
odak alıyor olmalı; repro bunu ayırt eder.

### 6.2 Repro (s5, s6 — `hover-keys.ps1`; ekran görüntüleri `s6\*.png`)

Taze açılış, pencere ön planda, her adımda UIA odağı = ana `Window`:

| Fare nerede | Ekranda | F5 → koşu başladı mı |
|---|---|---|
| grafın boş alanı (2070,805) | tooltip yok | **evet** (1,4 s'de bitti) |
| `EXTERNAL 10` katman başlığı, sol (60,942) | başlık vurgulu, listenin üstünde `Jump to External` pill'i | **hayır** |
| aynı başlık, orta (600,942) | aynı | **hayır** |
| bir proje satırı (300,1000) | satır hover (aksiyon ikonları) | **hayır** |
| aynı satır, sağ uç (1250,1000) | aynı | **hayır** |
| tekrar grafın boş alanı | — | **evet** |

s5'te de aynı: başlık üstünde hayır, fare çekilince evet, Esc'e gerek yok. Graf düğümü üstünde (s4, `OSYS.Types.Kafka.Suzuki`
düğümü) F5 çalışıyor — kusur grafta değil, listede. Kod tarafında `StickyLayerList`/`ProjectRow`'da F5'i yutan bir handler yok
(`OnRowKeyDown` yalnız Enter/Space; `ForwardWheelToScroll` tekerlek). Pencere kısayolları `MainWindow.InputBindings`'te
(`SetupKeyboardShortcuts`, `KeyboardShortcuts` tablosu). Olası mekanizma: liste üstünde açılan tooltip/hover Popup'ının kendi
HWND'si Win32 klavye odağını alıyor (s1'de UIA bunu `class='Popup'` olarak gösterdi); tuş o HWND'nin WPF girdi hattına gidip
pencerenin `InputBindings`'ine hiç ulaşmıyor. Kesinleştirmek için fix öncesi testte `GetFocus()`/`Keyboard.FocusedElement`
tooltip açıkken okunmalı.

### 6.3 Temiz tepsi ölçümü (K1) — kapandı

s3 ve s4'te (taze açılış → kısa Build'ler → tepsi → 15 s → 15 s sonda) UI thread'i **11,2 M/s**, render 0, diğer 0 — 10-06'daki
düzeltme sonrası değerin aynısı. s1'deki 64 M/s, B1'in bozduğu durumda (tooltip Popup'ı odakta, 5 dk bekleyen Build komutu)
okundu: tooltip/odak durumu bir saat tutuyor olabilir; B1'in testi bunu da kapsamalı (tooltip açıkken gizle → ≤ 15 M/s).

## 7. Motor

| Koşu | Süre | Not |
|---|---|---|
| Build, kaynak değişmiş (60 proje + 3 döngü grubu × 2 üye) | 36,6 s | 2'li gruplar tur başına 5,8–6,9 s, `levels=2` (üyeler sıralı); hash 1,3–1,6 s |
| Clean 187 | 25,8 s | 4 paralel `-t:Clean` |
| Resolve cycles, Clean sonrası (7 grup, 33 üye + 50 bağımlılık) | 122,4 s | UI.DMS grubu 73 s (%60): tur 1 62 s (17 WPF üye), tur 2 10,3 s (1 bayat üye: `OSYS.UI.UsedCars`, taşınan çıktı) |
| Build, Clean sonrası (kalan 101 proje) | 86,8 s | — |
| Rebuild 187 | 204,9 s | kullanıcının 10-08 koşuları (VS açık, yük altında) 448–525 s idi |
| Build, değişmemiş | 1,5 s | yalnız 3 bozuk proje yeniden denenir |

Önceki ölçümlerle tutarlı: Resolve 101–115 s (10-06) → 122 s (bugün üyeler bugünkü commit'lerle daha kirli). Çizelgeleme tarafında
yeni bir kayıp görünmüyor (10-02 raporunun "UI grubunun dışlayan en ağır kümesi = alt sınır" bulgusu geçerli).

## 8. Doküman kontrolü (kod ↔ ARCHITECTURE.md)

| İddia | Yer | Kod | Sonuç |
|---|---|---|---|
| Perf profili tablosu Full 6/Normal/—, Balanced 4/BelowNormal/70, Light 2/Idle/40 | §11.1 | `PerfProfile.For` | uyumlu |
| Resolve tam öncelik: profilin paralelliği + Full'ün cap/önceliği | §11.1 | `PerfProfile.ForRun`; decision.log `cpuCap=70%` satırı Cycles koşusunda da yazıldı (runStarted'ın taşıdığı cap mi, profilin mi — bkz. not) | **not:** `run … started: mode=Cycles … cpuCap=70%` — doküman "runStarted carries the cap actually written — none" diyor; decision.log'daki `cpuCap=` alanı 70 yazıyor. Hangi değerin yazıldığı (`runStarted.cpuCap` mı, log formatı mı) kodda doğrulanmalı |
| WorkerBudget: `WorkersPerCore`, `BytesPerWorker`, `ReserveBytes` | §11.1 | 2 / 0,5 GB / 2 GB | uyumlu |
| Kullanıcı regex'i 100 ms match timeout | §6.6 | `LayerEngine.UserRegexMatchTimeout` | uyumlu |
| 200 ms tick (canlı süreler) | §12.1 | `MainWindow._elapsedTimer` | uyumlu |
| ETA "400 ms when anything is building" | §8.4 | `EtaCalculator.BuildingOverheadMs` | uyumlu |
| §17.5 ölçüm kapıları (`BO_PROBE_TRAY`, `BO_PROBE_FILE_DIALOG`, `BO_MEASURE_OVERLAY`, `BO_MEASURE_HIDDEN`, `BO_MEASURE_PERF`, `BO_MEASURE_ROOT`/`COLD_ROOT`/`CACHE_ROOT`, `BO_PROBE_FRAMES`) | §17.5 | `Skip.IfNot` listesi | uyumlu |
| "17-member group re-pay ~98 s of rounds on every Resolve press" | §8.3 | bugün 73 s (tur 1 62 + tur 2 10) | rakam eski ölçümden; iddia yanlış değil, "~98 s" bayatlamış sayı |
| "`Jump to <layer>` native tooltip" | §13.2 | B1 | odak davranışı dokümanla çelişiyor (repro sonrası kesinleşir) |

## 9. Yapılmayanlar ve sınırlar

- **Kod düzeltmesi yapılmadı.** Süre bütçesi ölçüm + teşhise harcandı; B1 ve Ö2 için kırmızı test + fix ayrı iş (§10).
- Sweep'in son iki adımı (değişmemiş Build, tepsi) B1 yüzünden geçersiz; değişmemiş Build s2'de iki kez ölçüldü (1,5 s).
- Kare sondası açıkken WPF her karede çizer: fps ve render-thread cycle değerleri mutlak değil, A/B için geçerli. Boşluk
  (maxGap) ölçümleri geçerli (sonda kareyi *üretir*, boşluğu uzatmaz).
- Harici kök (`OSYSLogo`) her koşuda ff-only güncellendi (kullanıcının ayarı açık) — git'e yazan tek akış buydu.
- Defender dışlaması yapılmadı; `sys-watch` MsMpEng payını kaydetti (§4.2).
- **Disk geri kuruldu (22:58):** iki bozuk projenin bin/obj'si aynen (`OSYS.UI.Service.Common` 288 dosya, `…Accounting.Common`
  obj 2 dosya), paylaşılan Bin'lerde eksik dosya 0 (`verify`: fark yok). Son koşular Build/Rebuild olduğu için diğer tüm projelerin
  çıktıları ve defter (`build-state.json`) tutarlı; yalnız `OSYS.UI.Service.Common` bir sonraki koşuda "başkasının çıktısı" sayılıp
  tarihe bakılarak bir kez daha derlenebilir. Koşu logları (`run-20261009-2228*` … `2258*`) kullanıcının log kökünde kaldı.
- Ö1'in iz penceresi en kötü anı (UI.DMS turu) tutturamadı; `corespeed`/`sys-watch` tutturdu. Tekrar ölçümde iz F6+120 s'de
  başlatılmalı.

## 10. Önerilen sonraki adımlar

1. **B1 (bloklayıcı):** kırmızı test — realize edilmiş pencerede fare bir katman başlığının/satırın üstündeyken (tooltip açık)
   F5 `KeyBinding`'i `BuildCommand`'ı çağırmalı; test tooltip Popup'ının HWND'sine odak gidip gitmediğini de pinlesin
   (`Keyboard.FocusedElement` + Win32 `GetFocus`). Fix adayları: tooltip/hover Popup'larının odak almaması (ToolTip stili
   `Controls.xaml:1573` ve satır hover Popup'ı; `Focusable=False`, popup HWND'sine aktivasyon verilmemesi) ya da kısayolların
   pencere yerine `Application`/`PreviewKeyDown` düzeyinde yakalanması (tooltip HWND'sinden gelen tuş da pencereye düşsün).
   Doküman §13.2 ("native tooltip") ve §13.9 (klavye) fix'e göre. Tahmin: kısa.
2. **Ö1 (akıcılık):** §4.2'deki iz sonucuna göre: (i) ise graf/satır animasyonlarının derleme sırasında kare başına maliyetini
   düşür (orbit animasyonunu `CompositionTarget` yerine saat tabanlı tek Storyboard'a almak, "derleniyor" düğümünde yalnız
   değişen DrawingVisual'ı tazelemek); (ii) ise App'in UI/render thread'lerine P-çekirdek yakınlığı / `PROCESS_POWER_THROTTLING`
   kapalı / koşu sırasında `AboveNormal` (10-06 A/B "fark yok" demişti, ama o ölçüm Resolve'daydı ve frekans değil öncelik
   değiştirildi). Ölçüm tekrarı: kare sondası + corespeed birlikte, Rebuild'in 60–90 s aralığı. Tahmin: orta.
3. **Ö2 (koşu başı 115–145 ms):** `runStarted` + `buildPreview` işleyicilerinin UI dilimini iz ile ayrıştır (s2 izi F5
   koşularını kapsamıyor; F5'e hizalı 10 s'lik iz gerekir) ve satır güncellemelerini dilimle. Tahmin: kısa–orta.
4. **K1:** s3 sonucu 11 M/s'ye dönüyorsa kapat; 64 kalıyorsa yetim saat regresyonu — `DecorativeClock` guard'ı ile kırmızı test.

## 11. Yeniden üretim

```powershell
dotnet build BuildOrchestrator.slnx -c Release            # uygulama kapalıyken
& .claude\temp\perf-sweep-2026-10-09\backup.ps1 -Mode save
& .claude\temp\perf-sweep-2026-10-09\sweep.ps1 -Dir .claude\temp\perf-sweep-2026-10-09\s1   # ~18 dk, klavye/fareye dokunma
python .claude\temp\perf-sweep-2026-10-09\frames_summary.py s1\sweep.log s1\frames.log
& .claude\temp\perf-sweep-2026-10-09\spike-probe.ps1 -Dir s2  # + sys-watch.ps1, corespeed-watch.ps1 ayrı pencerede
& .claude\temp\perf-sweep-2026-10-09\tooltip-focus-repro.ps1 -Dir s3
& .claude\temp\perf-sweep-2026-10-09\backup.ps1 -Mode restore; ... -Mode verify
```
