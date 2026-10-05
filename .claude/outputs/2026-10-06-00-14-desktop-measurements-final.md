# Masaüstü ölçümleri — nihai (PERF/RESOLVE kapanışının bekleyen satırları)

**Tarih:** 2026-10-05 23:20 – 2026-10-06 00:14 · **Kod:** develop `700971e` (Release, 0 uyarı) · **Ortam:** kullanıcı başında, klavye/fareye
dokunmadan; ekran %100; keep-awake (ES_DISPLAY_REQUIRED) açık; OSYS çalışma ağacı temiz (HEAD `92f67411f`, 2026-10-05 17:00 merge —
kaynak Faz A ölçümünden beri değişti, Rebuild işi aynı: 187 proje, 153 derlenir, 1 bilinen hata) · **Araçlar:**
`.claude/temp/perf-2026-10-01/measure2/` zinciri (`run-measure.ps1 -Scripts measure2,measure3,measure-behind,measure4 -T23`) +
`.claude/temp/perf-2026-10-03/wpr-scenario.ps1` / `wpr-elevated.ps1` (çekirdek izi) · **Çıktılar:** `.claude/temp/perf-2026-10-03/final-desktop/`
(loglar, speedscope/attrib dosyaları, gcdump'lar, `f-tray-idle.etl`, `etl-analysis.txt`); tablo `desktop-summary.py baseline-100 after-a final-desktop`.

## 1. Kabul tablosu (kapanış raporunun ⏳ satırları dahil)

| Ölçü | Taban (%100) | Faz A | **Nihai** | Hedef | Durum |
|---|---|---|---|---|---|
| Tepsiden kısayol → koşu klasörü (J) | 4,44 s | 2,78 s | **2,43 s** | ≤ 2,5 s | ✅ (ilk kez) |
| Tepside Rebuild, UI thread (E) | 1.254 Mcycles/s | 144 | **116** | ≤ 300 | ✅ |
| Tepside Rebuild, App toplam (E) | %49,1 | %15,7 | **%14,8** | ≤ %25 | ✅ |
| Göstergede 100 ms üstü kesinti / 25 s (G izi) | 2 (en uzun 300 ms) | 0 (9 ms) | **0 (6 ms)** | ≤ 1 | ✅ |
| Koşudan sonra tepside boşta, UI thread (F) — koşu sürerken gizlenince | 81,6 | 95,1 | **13,2** | ≤ 40 | ✅ |
| Koşudan sonra tepside boşta, UI thread (F) — pencere açıkken biten koşudan sonra gizlenince | — | — | **118** (+75 s'de 79) | ≤ 40 | ❌ → §2 |
| Ön planda boşta, pencere AKTİF (B4) | 179 | 178 | **182** | ≤ 120 | ❌ (süs animasyonları etkin pencerede çalışır; değişmedi) |
| Ön planda boşta, pencere ARKADA (B4) | 181 | — | **16,9** | ≤ 50 | ✅ |
| Supervisor Private, 6 aktivasyon dönüşü sonrası (S) | 334 MB; +6,2/dönüş | 291; +3,7 | **85 MB; +1,7/dönüş (tepe 105)** | ≤ 200; +≤ 10 | ✅ |
| App Private, iki Rebuild sonrası tepside (E gcdump) | 283 MB | 396 | **215 MB** (zorla GC sonrası 216) | ≤ 300 | ✅ |
| 2 işlemci, tepside Rebuild (T3) | 51 s | — | **motor 44,8 s / duvar 52,2 s** | D1 arayüzsüz en iyi ~42 s | ≈ (arayüz de aynı iki işlemciyi paylaşıyor) |

Bilgi satırları (hedefsiz): 4 işlemci tepside Rebuild 33,4 s (ön plan 38,8); 8 işlemci Rebuild 24–27,5 s (ön plan 36,0 / tepsi 32,3 duvar);
ön planda boşta UI 66 (taban 66,5); tepside boşta, koşu öncesi UI **13** (taban 46,8); ön planda koşu sonrası boşta UI 103 (taban 93);
F5 → koşu klasörü 4,2 s (ön plan; tepsiden kısayolla 2,43 — fark pencerenin kendi hazırlığı); `measure4` dört ölçüm testi 4/4 geçti
(gösterge döngüsü 58,7 kare/s, tek çekirdeğin %12,3'ü; statik kare %4,1).

## 2. F araştırması: koşudan sonra tepside UI thread neden çalışıyor

İki araç aynı durumda alındı (Rebuild pencere açıkken bitti → 3 s sonra gizlendi → 15 s bekle → 20 s ölçüm):

- **Çekirdek izi (WPR `CPU` profili, `etl-analysis.txt`):** UI thread saniyede ~430 kez uyanıyor; uyandıranların hiçbiri DPC/timer değil
  (Flag 0, AdjustReason 1 = bir thread'in sinyali). Uyanmalar **15,1 ms periyotlu kümeler (~66 Hz) halinde, küme başına ~7 uyanma**;
  render thread (wpfgfx) de aynı 15 ms periyotla uyanıyor. UI örneklerinin %43'ü çekirdek (bekle/uyan maliyeti), %41'i managed kod,
  %7'si win32k (ileti pompası).
- **dotnet-trace atfı (`app-tray-idle.attrib.txt`, aynı durum):** `MediaContext.AnimatedRenderMessageHandler` → `TimeManager.Tick` →
  `ClockGroup.ComputeTreeState`: **canlı bir WPF animasyon saati** her karede render geçişi istiyor; uygulama çerçevesi görünmüyor (saat
  tik'leri uygulamanın metodunu taşımaz). Geri kalanı o kare döngüsünün pompa maliyeti (`GetMessage`, `WndProc`, `ProcessQueue`,
  `RequestBackgroundProcessing`).
- **Ayırt edici koşul:** koşu **sürerken** gizlenirse (measure2 E/F yolu) tepsi boşta 13 M/s — döngü yok. Koşu **pencere açıkken bitip**
  sonra gizlenirse 118; 75 s sonra 79 — sönümleniyor, yani sonlu ama uzun bir animasyon (ya da zamanla biten birkaç tanesi) gizlenince
  durmuyor. Ön planda koşu sonrası boşta da 103 (koşu öncesi 66): aynı iş görünür pencerede de var, orada beklenen süs sayılıyor.

**Sonuç:** F hedefi (≤ 40) koşu sürerken gizlenen pencere için tutuyor; pencere açıkken biten koşunun ardından gizlenen pencere için
tutmuyor. Düzeltme ayrı bir iştir: koşu bitiminde (pencere görünürken) başlayan ve gizlenince durmayan storyboard/clock'u bulup
IsVisible kapısına almak. Yeniden üretim artık kesin ve ~3 dk: `wpr-scenario.ps1` senaryosu (WPR'siz de olur; yalnız probe satırı yeter).

## 3. Aksilikler ve ölçüm notları

- İzleme komutunun `tail -F` ile açık tuttuğu log dosyasına PowerShell `Add-Content` yazamadı: measure2 bloğunun ilk koşusunun sayıları
  kayboldu, measure3'ün ilk ~70 s'lik satırları (başlangıç belleği) eksik. İzleme kapatıldı, measure2 zincirin sonunda yeniden koşuldu
  (tablo bu koşudandır). Hafıza notu: `tail-breaks-powershell-log-writes`.
- WPR `-filemode` tamponu 743.138 olay düşürdü; oranlar ve periyot analizi için yeterli, mutlak örnek sayıları eksik. ETL 571 MB
  `final-desktop/f-tray-idle.etl` (WPA ile açılabilir); CSV dökümü (1,6 GB) silindi.
- Ölçüm kullanıcının canlı OSYS'inde Rebuild koşturur (Faz A ölçümüyle aynı); çalışma ağacı temiz olduğu için çıktılar HEAD'in Debug
  derlemesidir.

## 4. Durum

PERF ve RESOLVE planlarının tüm fazları, Faz 4 (Resolve tam öncelikte, varsayılan açık) ve üç görünürlük düzeltmesi (işçi kırpma notu,
bayat obj + ters katman uyarıları, README saat damgası) develop'ta; CI yeşil. Kapanış raporundaki bekleyen masaüstü ölçümleri bu raporla
kapandı. Açık kalanlar: F (yalnız "pencere açıkken biten koşu → gizle" yolu) ve B4 aktif ön plan (182 > 120) — ikisi de ayrı karar/iş.
