# Sonuç — gece performans taraması: kök nedenler, düzeltmeler, ölçüm

**Tarih:** 2026-10-10 00:56–02:00 · **Branch:** `perf/overnight-sweep-2026-10-09` · **Önceki rapor:**
`2026-10-09-22-47-performance-sweep-findings.md` (bulgu listesi; aşağıda hangi iddiasının yanlış çıktığı yazılı) ·
**Ham veri:** `.claude/temp/perf-sweep-2026-10-09/` (`s12`–`s20`), deney kopyası `.claude/temp/b1-exp/` (repo kaynağına girmez).

## 1. Özet

| # | Önceki rapordaki bulgu | Kök neden (kanıtlı) | Sonuç |
|---|---|---|---|
| B1 | "Fare listedeyken F5/F6/F7 çalışmıyor" | **Ölçüm aracımın ürünü — uygulama kusuru değil.** (§2) | kod değişmedi |
| K1 | "Tepside 64 M/s" | Aynı yapay durumda okundu; gerçek yolda tepsi 11,2–12,2 M/s | kod değişmedi |
| Ö1 + Ö3 | Rebuild'de 350–720 ms kare boşlukları, koşu sırasında App ~1–1,5 çekirdek | **Biten düğümlerin görünmez beads yörüngeleri koşu boyunca her karede yeniden çiziliyordu** (§3) | **düzeltildi** — `541f16fa` |
| Ö2 | Her koşu başında 115–145 ms girdi gecikmesi, 210–260 ms kare boşluğu | **Koşu başı olay patlaması (≈186 olay) tek blokta, çizim ve girdinin üstünde işleniyordu** (§4) | **düzeltildi** — `7e056259` |
| — | "Finalizer thread'i 30 s'nin 16 s'sinde CPU'da (DWrite font)" | **Yanlış:** iz örneklemesinin sınıflandırma hatası; thread cycle sayacı finalizer'ı boşta gösteriyor (0–15 M/s) | geri alındı |
| — | Doküman: Resolve koşusunda `cpuCap=70%` | Kullanıcı ayarı *Resolve cycles at full priority* kapalı (`ui-state.json`) — doğru davranış | geri alındı |
| — | Doküman: §8.3 "~98 s" bayat | Kuralın gerekçesi olarak verilmiş tarihsel ölçüm; yanlış değil | geri alındı |

## 2. B1 / K1 — ölçüm aracının ürünü

Uygulama içine (yalnız deney kopyasında) tuş yolu sondası eklendi: her F5'in hangi HWND'ye geldiği, rota üzerindeki öğeler,
`BuildCommand.CanExecute` ve kapalıysa nedeni, pencere aktivasyonu, VM meşguliyet bayrakları.

- F5 **pencereye ulaşıyor**, KeyBinding eşleşiyor; ama o anda `CanExecute=false`, neden **"a Sync is in progress"**.
- Zincir: betiğimin `Set-Foreground`'u önce `AppActivate(pid)` çağırıyor. Bu, sürecin z-sırasındaki ilk görünür üst düzey
  penceresini öne alıyor; açık bir tooltip varsa o, **tooltip'in Popup penceresi**dir → ana pencere `Deactivated` → 400 ms
  sonra `SetForegroundWindow(ana)` → `Activated` → **pencereye dönüş tetiği** (son Sync'ten 5 s'den fazla geçmiş) → sessiz
  Sync (~0,6 s) → 0,4 s sonra gelen F5 koşu kapısına takılır. Kapının Sync sırasında kapalı olması ve basışın kuyruğa
  alınmaması ARCHITECTURE §10.2 ve `CanRequestRun` doc'unda yazılı tasarımdır.
- "Yalnız UIA sorgulu oturumlarda" görülmesinin nedeni: **ilk UIA çağrısı PowerShell iş parçacığını DPI-uyumsuzdan
  sistem-uyumluya çeviriyor** (ölçüldü: `GetThreadDpiAwarenessContext` 0 → 1). Aynı imleç koordinatları öncesinde ×1,25
  ölçeklenip bir proje satırına (tooltip yok), sonrasında katman başlığına (tooltip açılır) düşüyordu.
- Gerçek kullanıcı yolu temiz: başlık üstünde tooltip açıkken global kısayolla tepsiye gizleme → tooltip kapanıyor, tepside
  UI thread 11,4–12,2 M/s (`s17`).

Not (tasarım sorusu, değiştirilmedi): pencereye dönüşten sonraki sessiz Sync süresince (bu repoda ~0,6 s) basılan F5 sessizce
yok sayılır. Kuyruğa almak ya da ekranda bir geri bildirim göstermek istenirse ayrı bir karar.

## 3. Ö1 + Ö3 — biten düğümlerin görünmez yörüngeleri

**Kanıt:** Rebuild'in ağır penceresinde (F6+110 s) alınan tahsis izinde App tahsislerinin **%15'i** (30 s'de 125 MB
`EffectiveValueEntry[]`) ve ilk beş tipin çoğu tek yoldan geliyordu: `Shape.GetPen ← Rectangle.OnRender ← Grid.ArrangeOverride ←
Canvas.ArrangeOverride` (+ `DashStyle`, `Pen`, `DoubleCollection`, UIA ata geçersizlemeleri). Graf düğümünün beads yörüngesi
(kesikli `Rectangle`, `StrokeDashOffset` paylaşımlı saatle sürülür) düğüm ilk derlendiğinde kuruluyor, **bir daha sökülmüyordu** —
yalnız opaklığı 0'a iniyor, saate bağlı kalıyordu. Saat herhangi bir düğüm derlenirken döndüğü için koşu ilerledikçe biten her
düğümün görünmez yörüngesi saniyede 30 kez yeni bir kalemle yeniden çiziliyordu; maliyet derlenmiş düğüm sayısıyla büyüyordu
(en kötü kare boşlukları koşunun sonunda).

**Düzeltme (`541f16fa`):** çıkış animasyonu (640 ms) bitince yörünge saatten sökülür ve `Collapsed` olur; düğüm yeniden
derlenirse aynı yörünge geri gelir; yeniden kurulan saat yalnız ekrandaki yörüngeleri bağlar. "Noktalar dönerken söner" kuralı
korunur. Testler: `GraphBeadsLifecycleTests` (4; kırmızı: "Expected Collapsed, Actual Visible"). ARCHITECTURE §13.6.

## 4. Ö2 — koşu başı olay patlaması

**Kanıt:** F5'e hizalı izde (`s18` T1) F5+5,3 s'de UI thread'i 312 ms aralıksız: `RunViewModel.OnEvent` 119 ms
(`OnProjectSkipped` 68, `RefreshRunSurface` 49, sayaçlar 33, akış satırları 30, graf statüsü 25) + toplu yerleşim/çizim 145 ms.
Motor koşu başında `runStarted` + `buildPreview` + atlanan her proje için bir `projectSkipped` gönderiyor; pencere her olayı ayrı
bir `Dispatcher.InvokeAsync` ile **Normal** öncelikte taşıyordu — Normal çizimin ve girdinin üstünde olduğu için hepsi tek blokta.

**Düzeltme (`7e056259`):** `EngineEventPump` — tek sıralı kuyruk; ilk dilim hemen (Normal), 8 ms dolunca kalan iş girdi ve
çizimin altındaki önceliğe (Background) devredilir; sıra korunur. `projectLog` hızlı yolu değişmedi. Testler:
`EngineEventBurstTests` (kırmızı: "girdi 177/177 atlanan olaydan sonra koştu"; sıra koruması). ARCHITECTURE §12.1 + kod haritası.

## 5. Ölçüm — önce / sonra (aynı makine, OSYS 187 proje, Balanced)

**App CPU, görünür Rebuild, kare sondası KAPALI** (`trace-session.ps1`, thread cycle sayaçları):

| Pencere | Önce (`s18`) | Sonra (`s19`) | Değişim | render thread | UI thread |
|---|---|---|---|---|---|
| F6+5…50 s | 2,48 G/s | 2,07 G/s | −17 % | 1,07 → 0,81 | 0,72 → 0,68 |
| F6+50…95 s | 2,38 G/s | 1,44 G/s | −40 % | 1,09 → 0,56 | 0,77 → 0,58 |

**Tahsis, F6+110 s'den 30 s:** 27,6 → 15,2 MB/s · GC 128 → 68 · GC duraksaması 499 → 268 ms. Yörünge kaynaklı tipler listeden çıktı.

**Kare boşlukları, Rebuild boyunca, kare sondası AÇIK** (`BO_PROBE_FRAMES`, 5 s pencereler):

| | Önce `s1` (213 s) | Önce `s2` (175 s, izli) | Sonra `s20` (167 s) |
|---|---|---|---|
| ortalama fps | 72 | 85 | **102** |
| en uzun boşluk | 720 ms | 476 ms | **204 ms** |
| >250 ms / >100 ms / >50 ms | 15 / 80 / 368 | 2 / 18 / 90 | **0 / 12 / 37** |
| 45 fps altı pencere | 8 | 1 | **0** |

Kalan en kötü pencere (204 ms) UI döngü grubunun hemen ardından büyük UI projeleri derlenirken, makine doymuşken.

**Koşu başı girdi gecikmesi** (WM_NULL, 10 ms örnekleme, değişmemiş Build): önce 145 / 115 ms → sonra **63,5 / 46,0 ms**.

Uyarı: "önce" ölçümleri 22:27–22:50, "sonra" 01:37–01:47 arasında alındı; ikisi de aynı Rebuild'in aynı repoda koşusu ama makinenin
arka plan yükü (Defender, diğer uygulamalar) birebir aynı değildi. CPU ve tahsis ölçümleri (aynı betik, iz pencereleri aynı fazda)
en güvenilir karşılaştırmadır.

## 5b. Bağımsız kod incelemesi ve ardından yapılanlar

İnceleme hükmü "düzeltmelerle merge edilebilir"di; TDD dökümü `2026-10-10-02-05-perf-fixes-review-tdd-plan.md`. Uygulananlar:

| Bulgu | Test | Düzeltme | Commit |
|---|---|---|---|
| Motor çıkışı (Send önceliği) pompada bekleyen olayların önüne geçiyordu — geç uygulanan `runStarted` ölü motorla koşuyu yeniden açar (eski yarış; pompa pencereyi uzatmıştı) | kırmızı → yeşil | çıkış işleyicisi önce `DrainNow` | `f6784633` |
| Pompa bayrağı `Volatile.Write` ile sıfırlanıyordu — x64 mağaza tamponu kayıp uyandırmaya izin verir | **deterministik kırmızı verilemez** (işlemci bellek modeli yarışı); gözlenen sözleşme yeni birim testleriyle pinli | `Interlocked.Exchange` (tam bariyer) | `f6784633` |
| Patlama testi ortak fikstürü ve `OsysProjectCount`'u kopyalıyordu | — | `NewWithProjects` kullanılır | `f6784633` |
| Pompa birim testleri yoktu | 4 yeni test (dilim arası girdi, çok üretici sırası, fırlatan işleyici, `DrainNow`) | — | `f6784633` |
| Değiştirilen eski sönüşün `Completed`'ı hâlâ ateşleniyor: bitir–başla–bitir 640 ms içinde olursa ikinci sönüş yarıda kesiliyordu | kırmızı → yeşil | düğüm başına sönüş nesli | `bcc00efb` |
| Spin-down penceresinde panel boyutu değişince boş saat bir sonraki koşuya dek dönüyordu (eski kusur) | kırmızı → yeşil | spin-down yeniden kurulur | `bcc00efb` |
| "Yörünge durdu" iddiası tik gelmezse boşuna geçebilirdi | — | dönen yörüngeyle aynı pompa penceresinde ölçülür | `bcc00efb` |
| Doküman/yorum kesinliği (§12.1 "en fazla 8 ms", istisna yolu, kod haritası, üç eski yorum) | — | yerinde yeniden yazım | `f6784633` |

Uygulanmayan öneri: girdi altında Background devamını belirli bir süre sonra yükseltmek — sürekli sürüklemede bile fare hareketleri
arasında boşluk kalıyor, ölçülmüş bir sorun yok.

## 6. Doküman kontrolü (kod ↔ ARCHITECTURE)

Perf profili tablosu (§11.1), Resolve tam öncelik kuralı, `WorkerBudget` sabitleri, regex 100 ms zaman aşımı, 200 ms tick, ETA
400 ms, §17.5 ölçüm kapıları: kodla uyumlu. Bu işte değişen davranışlar yerinde yeniden yazıldı: §13.6 (yörünge yaşam döngüsü),
§12.1 (olay pompası), §22 kod haritası.

## 7. Disk ve ortam

- Rebuild/Clean koşuları derlenemeyen projelerin (bugünkü commit'lerle bozulan `Orchestration.Accounting.Common`,
  `SparePart.Common`, `NewSales.Sales`) çıktılarını siler. Hedefli yedek (`D:\bo-perf-snap\sweep-2026-10-09`) her oturumdan sonra
  geri yüklendi ve doğrulandı (`verify`: paylaşılan Bin'lerde eksik dosya 0). Bu projeler doğrudan `C:\OSYS\Server\Bin`'e yazar;
  son iyi DLL'leri oradadır.
- OSYS kaynak ağacına dokunulmadı (`git status` temiz). Harici kök (`OSYSLogo`) koşularda kullanıcı ayarı gereği ff-only güncellendi.
- Koşu logları kullanıcının log kökünde (`run-20261009-22*` … `run-20261010-01*`).

## 8. Kalanlar (karar sizin)

- **Canlı ekranda gözle kontrol edilmedi:** koşu başındaki atlanmaların artık birkaç karede uygulanması, tasarımın kaldırdığı
  "dalga" gibi görünür mü? 02:25'teki ekran yakalama denemesi OLED koruma ekran koruyucusuna takıldı (masaüstü erişilemezdi;
  ekran koruyucusu bilerek kapatılmadı). Ölçüme dayalı beklenti: graf statüsü 200 ms'lik tick'le itildiği için patlama (~0,4 s)
  en fazla 2–3 graf güncellemesinde oturur; listede ekrandaki ~20 satır 1–2 dilimde güncellenir. Bir sonraki koşuda gözle bakmanızı
  öneririm.
- Pencereye dönüşteki sessiz Sync süresince F5'in sessizce yok sayılması (§2 not).
- Koşu sırasında render thread hâlâ 0,56–0,81 G döngü/s (sürekli animasyonlar: derlenen düğümlerin yörüngeleri, satır ve şerit
  animasyonları, konsol). Kalan en büyük tahsis kalemleri konsol metni ve UI Automation peer'ları (makinede UIA istemcisi —
  Logi Options+ — açık). Ayrı ölçümsüz bir iyileştirme önerilmiyor.
