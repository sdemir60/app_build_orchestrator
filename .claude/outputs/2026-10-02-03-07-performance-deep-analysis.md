# Derin performans analizi — Build Orchestrator

Tarih: 2026-10-02 · Sürüm: 1.7.0 (`develop` @ f624f23) · Bu bir **analiz ve ölçüm** dokümanıdır; kod değişmedi.

Hedef: uygulamayı kullanacak arkadaşların makineleri zayıf (az çekirdek, az RAM). Birincil senaryo: pencere
**tepside** dururken geliştirici Visual Studio'da çalışır, hata alınca global kısayolla Build başlatır. O anda
yalnız derleme çalışmalı; pencere geri gelince ekran durumla tutarlı olmalı.

---

## 0. Okuma kılavuzu

| Etiket | Anlamı |
|---|---|
| **[ölçüldü]** | Bu çalışmada, bu makinede alınmış sayı |
| **[kod]** | Kodun yapısından kesin olan (dosya + satır verilir) |
| **[tahmin]** | Ölçülmüş bir dağılımdan türetilen ya da henüz ölçülmemiş beklenti |

- Bulgu kimlikleri (`D5-1`, `N1-3` …) ek dosyadaki kataloğa gider:
  `.claude/outputs/2026-10-02-03-07-performance-deep-analysis-findings-catalog.md`. Orada her bulgunun tam konumu,
  kanıtı, çözümü, test fikri ve doğrulayıcı notu var.
- 16 boyutta 154 bulgu çıkarıldı ve hepsi ikinci bir ajan tarafından koda ve ölçüme karşı **çürütülmeye çalışılarak**
  doğrulandı: 100 doğrulandı, 50'sinin önemi düşürüldü ya da sayısı düzeltildi, 3'ü yükseltildi, 1'i çürütüldü.
- Ölçüm ortamı: Intel Core Ultra 7 258V (8 mantıksal işlemci), 32 GB, Windows 11, Release build, profil Balanced
  (4 işçi, BelowNormal, %70 CPU tavanı), OSYS 177 proje + 2 harici kök = 187 proje. Ölçüm sırasında makine boştaydı
  (toplam CPU %4-10).
- Birim: **Mcycles/s** = saniyede milyon işlemci döngüsü (`QueryProcessCycleTime` / `QueryThreadCycleTime`).
  Bu makinede tek çekirdeğin tamamı ≈ 3.300 Mcycles/s.

### Ölçüm yöntemleri

| Yöntem | Ne verir | Ham kayıt (`.claude/temp/perf-2026-10-01/`) |
|---|---|---|
| Süreç ve thread başına döngü sayacı, 10-25 s pencereler | CPU'yu kim yakıyor: UI thread'i, WPF render thread'i, diğerleri | `measure2/measure2.log`, `measure3.log` |
| UI gecikme sondası: pencereye 30 ms'de bir `WM_NULL`, gidiş-dönüş süresi | UI thread'i ne kadar süre mesaj pompalamıyor (donma) | aynı loglar, `ui latency` satırları |
| `dotnet-trace` (thread zamanı örneklemesi) + kendi çözümleyicimiz | UI thread'inin meşgul süresi hangi işe gidiyor | `measure2/*.attrib.txt`, `speedscope_analyze.py` |
| `dotnet-gcdump` | Canlı managed heap; zorla GC öncesi/sonrası bellek | `measure2/measure2.log` |
| Süreç yakınlığı (affinity) 4 ve 2 mantıksal işlemci | Az çekirdekli makine benzetimi | `measure2/measure3.log` |
| Ortam değişkeniyle GC ayarı denemeleri | Kod değişmeden GC ayarlarının etkisi | `measure2/measure5.log` |
| Projedeki kapılı ölçüm testleri | Gösterge, UI bütçesi, perf profili | `measure2/measure4.log` |
| 1640 koşu klasörünün taranması | Build/Resolve süreleri, restore payı, log hacmi | `logmine-stats.md` |

Sınırlar: düşük çekirdek benzetimi CPU tavanını ve RAM'i küçültmez; gerçek zayıf makine bundan daha kötü davranır.
Trace örneklemesindeki "GC bekleme" ve "kilit" payları duvar saati örnekleridir, kesin duraklama süresi değildir
(ayrıntı §5.3). CPU için tek doğru kaynak döngü sayaçlarıdır.

---

## 1. Özet

### 1.1 En önemli sonuçlar

| # | Sonuç | Sayı | Kanıt |
|---|---|---|---|
| 1 | Pencere tepsideyken derleme sırasında arayüz thread'i, pencere görünürken olduğundan **daha çok** çalışıyor. | Tepside 1.385, görünürken 1.038 Mcycles/s (aynı iş) | ölçüldü |
| 2 | Bu işin büyük kısmı kimsenin görmediği pencere için yapılan yerleşim (layout) ve çizim. | UI thread meşgul süresinin %70'i layout, %14'ü render | ölçüldü |
| 3 | Sağ alttaki gösterge aynı thread'de döndüğü için derleme sırasında takılıyor. | 25 s'de 27 kez 50 ms üstü, en uzun 327 ms kesinti | ölçüldü |
| 4 | Kısayola basıldıktan MSBuild başlayana kadar 5,7 s geçiyor; bunun 3,5 s'i gizli pencerede oynayan açılış animasyonu. | 5,68 s (animasyon 3,54 s nominal) | ölçüldü + kod |
| 5 | Ön planda derleme sırasında uygulamanın kendisi bir çekirdeğin tamamını yakıyor. | 2.535-3.427 Mcycles/s (%75-105) | ölçüldü |
| 6 | 2 çekirdekli makine benzetiminde aynı iş ön planda 66 s, tepside 51 s. Profil yine 4 işçi çalıştırıyor. | 66,4 s / 51,0 s (8 çekirdekte 29 / 26 s) | ölçüldü |
| 7 | Motorun (Supervisor) canlı verisi 2 MB, ama 150-336 MB bellek tutuyor. | Canlı heap 2,0 MB; Private 150-336 MB | ölçüldü |
| 8 | Uygulama iki derlemeden sonra 158 MB'tan 433 MB'a çıkıyor ve kendiliğinden geri vermiyor. | Zorla GC ile 319 MB; canlı heap 74 MB | ölçüldü |
| 9 | Her Sync ve her Build, 12 MB'lık iki JSON defterini baştan okuyup değişmese de baştan yazıyor. | 5,9 + 6,26 MB okuma ve yazma | kod |
| 10 | İşçi sayısı makineye göre ölçeklenmiyor: 2 çekirdek ya da 8 GB RAM'de de Balanced 4 işçi açıyor. | Gerçek derleyiciyle işçi başına ~2 GB commit | kod + ölçüldü |
| 11 | MSBuild süresinin %7'si, paketi değişmemiş projelerde her koşuda yeniden çalışan restore'a gidiyor. | 60 koşuda 564 restore, 539 s | ölçüldü |
| 12 | Log satırlarının yarısı tek bir uyarı; özet bloğu uyarıları bir kez daha yazıyor. | MSB3277 %49,5; Resolve'da satırların %29'u tekrar | ölçüldü |
| 13 | Koşu logları hiç silinmiyor. | 1.640 klasör, 1,5 GB | ölçüldü |
| 14 | Kapanış temiz: uygulama ölünce motor ve çocukları hemen gidiyor. | 77-200 ms, 11 ölçümde artık süreç yok | ölçüldü |
| 15 | 27 Eylül'deki Resolve iyileştirmesi işe yaramış. | Medyan 241 s → 161 s; eşzamanlılık 1,24 → 2,12 | ölçüldü |

### 1.2 İlk yapılacaklar (kazanç / risk sırasıyla)

| Sıra | İş | Beklenen kazanç | Risk |
|---|---|---|---|
| 1 | Pencere gizliyken açılış koreografisini ve bitiş finalini atla | Kısayol → derleme 5,7 s → ~2,1 s **[kod + ölçüldü]** | düşük |
| 2 | Pencere gizliyken konsol batch'lerini belgeye basma; dönüşte tek seferde kur | Tepside UI thread meşgul süresinin %18,6'sı ve metin biçimlemenin %64'ü **[ölçüldü]** | düşük |
| 3 | Pencere gizliyken görünüm beslemelerini durdur (şerit chip'leri, menü satırları, liste, akış, graf), dönüşte yeniden senkron | Layout geçişlerinin kaynağı kesilir: tepside UI thread 1.385 → ~200-250 Mcycles/s **[tahmin]** | orta |
| 4 | Defterlerde "değişmediyse yazma" bayrağı + akışla okuma/yazma | Her Sync ve Build'de 12 MB yazım ve ~48 MB geçici string kalkar **[kod]** | düşük |
| 5 | `BuildMenu.RefreshRows` yalnız toplam değişince; `TopologicalDepths` önbelleği | 25 s'lik koşuda 185 + 175 ms UI işi ve 233 ms'lik bir kesinti **[ölçüldü]** | düşük |
| 6 | İşçi sayısını çekirdek ve boş RAM'e göre kıs (önce 2 çekirdekte 1-2 işçiyi ölç) | Zayıf makinede aşırı yüklenme ve paging riski kalkar | orta, karar gerekir |
| 7 | Uygulama log tamponlarını koşu bitince bırak; koşu bitti + pencere gizliyken tek seferlik bellek rahatlatma | App 433 → ≤319 MB **[ölçüldü: zorla GC]** | orta |
| 8 | Koşullu restore; `-clp:Summary` tekrarını kaldır | MSBuild süresinin %7'si; log hacminin %5-29'u **[ölçüldü]** | düşük, karar gerekir |

---

## 2. Ölçüm sonuçları

### 2.1 Durum bazında CPU

Hepsi 8 mantıksal işlemcide, makine boşken. "No-op Rebuild" = çıktılar güncel, 152 MSBuild çağrısı derleme
yapmadan dönüyor (uygulamanın olay, log ve animasyon yolu aynı). UI gecikmesi p95 / p99 / en çok.

| Durum | App Mcycles/s | Tek çekirdek | UI thread | Render thread | Diğer | Supervisor | UI gecikmesi ms |
|---|---|---|---|---|---|---|---|
| Ön planda boşta (açılıştan sonra) | 181 | %4,4 | 68 | 71 | 41 | 0,2 | 0,6 / 0,9 / 1,2 |
| Tepside boşta (koşudan önce) | 37 | %1,6 | 33 | 3 | 1 | 0 | 0,6 / 0,7 / 0,9 |
| Ön planda gerçek Rebuild (141 proje derlendi, 81 s) | 2.535 | %75 | 774 | 921 | 841 | 381 | 4,1 / 9,0 / 34 |
| Ön planda no-op Rebuild (28-30 s) | 3.427 | %105 | 1.038 | 1.246 | 1.143 | 456 | 7,5 / 16,3 / 70 |
| **Tepside no-op Rebuild (25-26 s)** | **1.853** | **%57** | **1.385** | 248 | 220 | 372 | 13,1 / 26,9 / 55-86 |
| **Tepsiden kısayolla Build (5 s koşu)** | 1.054 | %34 | 758 | 170 | 127 | 466 | 8,1 / 42,4 / 83 |
| Ön planda F5 Build (5,7 s koşu) | 1.850 | %58 | 637 | 824 | 389 | 442 | 2,6 / 17,6 / 59 |
| Koşudan sonra ön planda boşta | 201 | %7,0 | 101 | 61 | 38 | 1,1 | 0,6 / 0,9 / 1,1 |
| Koşudan sonra tepside boşta | 136 | %4,4 | 129 | 6 | 0 | 2,4 | 0,6 / 0,8 / 0,9 |

Okuma:

- Ön planda boşta yanan 181 Mcycles/s'in tamamı sürekli çizimden geliyor: UI thread'i, WPF render thread'i ve ekran
  kartı sürücüsünün thread'leri. Kaynağı iki yanıp sönen imleç (§6).
- Motor (Supervisor) boşta hiç CPU harcamıyor; derlemede de tek çekirdeğin %8-14'ü. CPU sorunu uygulama tarafında.
- Koşudan sonra tepside UI thread'i 129 Mcycles/s yakıyor, koşudan önce 33. Bu farkın kaynağı kodda bulunamadı
  (bitiş finali bu pencerenin dışında). Gizli mod kapıları büyük olasılıkla kapatır; ayrıca ölçülmeli.

### 2.2 Tepside derleme: arayüz thread'i ne yapıyor

Tepside no-op Rebuild, 25 s trace. UI thread'i sürenin **%45,6**'sında meşgul (11,4 s).

| Kalem (kapsayıcı süre) | ms / 25 s | Meşgul sürenin payı |
|---|---|---|
| Layout geçişi toplamı (`ContextLayoutManager.UpdateLayout`) | 8.030 | **%70** |
| — Ölçüm (`UIElement.Measure`) | 3.986 | |
| —— Konsol belgesi (AvalonEdit `TextView.MeasureOverride`) | 2.122 | %18,6 |
| —— Proje listesi paneli (`FixedHeightVirtualizingPanel`) | 682 | |
| —— `TextBlock` ölçümleri | 533 | |
| — UI Automation eş ağacı eşitlemesi (`fireAutomationEvents`) | 1.944 | %17 |
| — Yerleştirme (`UIElement.Arrange`) | 1.879 | %16,5 |
| Çizim (`MediaContext.Render`) | 1.596 | %14 |
| Motor olaylarının işlenmesi (`RunViewModel.OnEvent`) | 890 | %7,8 |
| Animasyon saatleri (`TimeManager.Tick`) | 390 | %3,4 |
| Şerit chip'lerinin yeniden kurulması (`StickyRibbon.RebuildChipsIfChanged`) | 301 | |
| Graf beslemesi (`PushGraphStatuses`; 175 ms'i `TopologicalDepths`) | 185 | |
| Kapalı Build menüsünün satırları (`BuildMenu.RefreshRows`) | 185 | |
| 200 ms'lik tick gövdesi | 83 | |
| Olay akışı satırları ve daktilo | 70 | |

İşi başlatan: meşgul sürenin %48,5'i "geçersizleşme kaynaklı layout/render geçişi", %30,9'u "animasyon kaynaklı
render geçişi". İkisinde de yığında uygulama metodu yok: uygulama bir özelliği değiştiriyor, WPF de görünmeyen
pencere için yerleşimi ve çizimi yeniden yapıyor. `Window.Hide()` layout'u durdurmuyor.

Aynı iş pencere görünürken: UI thread'i %37,3 meşgul, layout 5.302 ms (%57). Yani gizli pencerede daha çok layout
çalışıyor. Görünürken en uzun kesinti 99 ms.

### 2.3 Akıcılık: arayüz thread'inin kesintisiz meşgul kaldığı dilimler

| Senaryo (25 s) | En uzun dilimler | 50 ms üstü | 100 ms üstü | 250 ms üstü |
|---|---|---|---|---|
| Tepside rebuild (gösterge açık) | 327, 261, 233, 178, 168 ms | 27 | 7 | 2 |
| Ön planda rebuild | 99, 91, 87, 61, 59 ms | 8 | 0 | 0 |
| Boşta (ön plan ve tepsi) | 2-4 ms | 0 | 0 | 0 |

Gösterge 30 kare/s hedefliyor (kare başına 33 ms). Tepside saniyede yaklaşık bir kare kaçırıyor, 25 s'de 7 kez üç
kare ve üstü duruyor. En uzun üç dilimin ilk işi sırasıyla animasyon kaynaklı render geçişi, layout geçişi ve
`BuildMenu.BuildRow`.

### 2.4 Kısayoldan derlemeye gecikme

| Senaryo | Tuş → `runStarted` | Koşunun kendisi |
|---|---|---|
| Tepsiden Ctrl+Shift+Space (Build) | **5,68 s** | 5,0 s (13 hatalı proje) |
| Ön planda F5 (Build) | 5,58 s | 5,7 s |
| F6 (Rebuild), ön plan ya da tepsi | 6,7-7,3 s | 25-30 s |

Dağılım **[kod]**:

- Açılış koreografisi: `2060 ms + dalga`. Tek projede 2,44 s; 11 ve üzeri projede ~3,54 s
  (`MarkingChoreography.TotalMs`). `startRun` komutu koreografi **bitince** gönderiliyor ve bu, pencere gizliyken de
  bekleniyor. Yani 5,68 s'nin ~3,5 s'i kimsenin görmediği animasyon.
- Kalan ~2,1 s motorun hazırlığı, baştan sona seri: harici köklerin güncellenmesi (kök başına 5 git süreci ve bir ağ
  turu; `git ls-remote origin` tek başına 1,2 s ölçüldü), iki defterin yüklenmesi (12 MB), tarama, değerlendirme,
  koşulsuz 5,9 MB yazım, graf, projelerin ikinci kez okunması, iki tanı amaçlı git çağrısı, 24.500 dosyanın stat
  turu, koşulsuz 6,26 MB yazım.

### 2.5 Bellek

| Süreç | Durum | Private MB | Working set MB | Canlı managed heap |
|---|---|---|---|---|
| App | Açılış Sync'inden sonra | 158-197 | 252-287 | 15,5 MB |
| App | Bir gerçek Rebuild'den sonra | 358 | 462 | |
| App | İki Rebuild'den sonra, tepside | 433 | 530 | |
| App | Aynı an, zorla tam GC'den sonra | 319 | 417 | 74,1 MB |
| Supervisor | Açılış Sync'inden sonra | 150-152 | 153-155 | 2,0 MB |
| Supervisor | Koşu + koşu sonu Sync'inden sonra | 302 | 279 | |
| Supervisor | Dört Rebuild'den sonra | 336 | 325 | 2,0 MB |
| Supervisor | Zorla tam GC'den sonra | 112-148 | 117-163 | 2,0 MB |
| MSBuild + csc çocukları | Gerçek derleme, 4 işçi | | 200-1.850 (tepe) | |

- Pencereye altı kez dönüş (her biri sessiz Sync): Supervisor Private 243 → 324 → 226 → 282 → 261 → 297 → 309 MB.
  Sürekli büyüyen bir sızıntı yok, ama bellek 220-320 MB bandında kalıyor.
- App'in canlı heap'indeki en büyük tek nesne 13,7 MB'lık bir `char[]` (koşu metni tamponu); kalan büyüme çok
  sayıda küçük nesne (log satırı kopyaları).
- Projedeki gerçek derleyici ölçümü (yapay büyük kaynak, bu makine): Light 2 işçi → commit +4,4 GB, boş RAM 16,8 →
  12,5 GB; Balanced 4 işçi → +8,4 GB, boş RAM 8,8 GB; Full 6 işçi → +12,2 GB, boş RAM 5,2 GB, UI gecikmesi en çok
  56 ms. İşçi başına yaklaşık 2 GB commit.

GC ayarı denemeleri (kod değişmeden, ortam değişkeniyle; Private MB):

| Varyant | Supervisor: açılış → 3 dönüş → rebuild sonrası | App: açılış → rebuild sonrası |
|---|---|---|
| Varsayılan | 151 → 250 / 253 / 215 → 229 | 197 → 306 |
| `GCConserveMemory=7` | 151 → 163 / 196 / 242 → 121 | 197 → 297 |
| `GCgen0MaxBudget=16 MB` | 165 → 183 / 234 / 288 → 329 | 181 → 290 |
| İkisi birlikte | 145 → 150 / 207 / 220 → 188 | 168 → 274 |
| Concurrent GC kapalı | 235 → 278 / 402 / 390 → 387 | 186 → 318 |

Tek bir GC ayarı sorunu çözmüyor: sayılar koşudan koşuya ±50 MB oynuyor. Concurrent GC'yi kapatmak motoru belirgin
biçimde büyüttü (235-402 MB). Sorunun kaynağı ayar değil, her Sync'in ürettiği çöp (§7).

### 2.6 Az çekirdekli makine benzetimi

| Mantıksal işlemci | Ön planda rebuild | Tepside rebuild | App ön plan / tepsi Mcycles/s | UI gecikmesi en çok |
|---|---|---|---|---|
| 8 | 28,4-29,7 s | 25,4-26,3 s | 3.427 / 1.853 | 70 / 55-86 ms |
| 4 | 32,7 s | 28,6 s | 2.726 / 1.262 | 20 / 82 ms |
| 2 | **66,4 s** | **51,0 s** | 1.836 / 912 | 9 / 88 ms |

- 2 işlemcide arayüzün görünür olması aynı derlemeyi %30 uzatıyor (51 → 66 s). Uygulama Normal öncelikte ve job
  dışında; derleme BelowNormal. Az çekirdekte arayüz işi derlemeden CPU alıyor.
- 2 işlemcide de 4 MSBuild aynı anda çalıştı (profil sabit).
- Benzetim CPU tavanını ve RAM'i küçültmedi; gerçek 2 çekirdek / 8 GB makine daha yavaş olur.

### 2.7 Projedeki ölçüm testleri

| Test | Sonuç |
|---|---|
| Gösterge tek başına (boş makine) | Statik kare: 32 kare/s, tek çekirdeğin %3,7'si. Döngü: 59,5 kare/s, %10,2. Fark %6,5 (testin kendi aboneliği kare hızını yükselttiği için üst sınır). |
| UI bütçesi: koşu olayları | 177 proje × 2 olay toplam 545 ms; en kötü tek olay 10,0 ms |
| UI bütçesi: Sync | Topoloji handler 36,8 ms + layout 56,7 ms; filtre kapat 43,1 ms |
| Liste realize | 191 satır 58,4 ms; 500 satır 75,8 ms |
| Perf profili, yapay CPU yükü | UI gecikmesi her profilde 1 ms altı |

Bütçe testleri handler süresini ölçüyor; gizli pencere senaryosu için hiçbir test yok (N3-5).

### 2.8 Koşu logları (17 Temmuz - 2 Ekim, 1.640 klasör)

| Ölçü | Değer |
|---|---|
| Bitmiş OSYS koşusu | 375 (Build 282, Cycles 59, Rebuild 33, Clean 1) |
| Test süitinin bıraktığı klasör | en az 741 (`run r1 … projects=2`) |
| Build süresi | medyan 23,0 s; p90 107 s; en çok 910 s |
| Resolve süresi (20+ üye derlenen koşular) | 27 Eylül 16:30 öncesi medyan 241 s (34 koşu); sonrası 161 s (6 koşu) |
| Resolve eşzamanlılık ortalaması | 1,24 → 2,12 (4 slot) |
| Derleme yapmayan MSBuild çağrısı (taban maliyet) | medyan 0,44 s; p90 0,89 s |
| Derleme yapan çağrı | medyan 1,52 s; p90 7,9 s; en çok 61,6 s |
| Restore çağrıları (son 60 koşu) | 564 çağrı, 539 s; MSBuild süresinin %7,1'i; çağrı medyanı 0,76 s |
| Build'de eşzamanlılık ortalaması | medyan 2,89 (4 slot) |
| Log hacmi | koşu başına medyan 10.383 satır; MSB3277 uyarısı tüm satırların %49,5'i |
| Günlük kullanım | medyan 7 koşu, toplam 7,9 dk; en yoğun gün 36 dk |

### 2.9 Kapanış

Uygulama süreci öldürüldüğünde (Görev Yöneticisi yolu) Supervisor 77-200 ms'de gitti; 3 s sonra MSBuild, csc ya da
Supervisor kalmadı (11 ölçüm). Koşu bittikten sonra Supervisor'ın çocuğu olarak yalnız kendi `conhost.exe`'si kalıyor
(7 MB). Tepsi menüsündeki Exit yolu ayrıca ölçülmedi.

---

## 3. Birincil senaryo: tepside derleme

### 3.1 Bugünkü durum

Pencere gizliyken bir Build'in başından sonuna uygulamada koşan işler **[kod]**:

| İş | Gizliyken çalışıyor mu | Gerekli mi | Konum |
|---|---|---|---|
| Açılış koreografisi (komut bunu bekler) | evet, 2,4-3,5 s | hayır | `MainWindow.xaml.cs:66-71, 1305-1317`; `RunViewModel.cs:1003-1008`; `OperationChoreographer.cs:90-95` |
| Konsol: her log satırı pompaya, 50 ms'de bir AvalonEdit belgesine | evet | hayır (log diskte, model tamponu var) | `RunViewModel.cs:2694-2716`; `MainWindow.xaml.cs:576-606` |
| Koşu başında konsol temizliği ve zorla `UpdateLayout` | evet | hayır | `MainWindow.xaml.cs:285-288`; `ConsoleView.xaml.cs:341-362` |
| 200 ms tick: süre, ETA, satır sayacı, graf beslemesi, liste takibi | evet | yalnız motor sessizlik bekçisi | `MainWindow.xaml.cs:352-364`; `RunViewModel.cs:1850-1902` |
| Olay başına: şerit chip'leri, Build menüsü satırları, sayaçlar, liste | evet | durum biriktirme evet, görünüm hayır | `StickyRibbon.xaml.cs:184, 219-231, 432-470`; `BuildMenu.xaml.cs:67-70, 98-106` |
| Olay akışı satırları ve daktilo (24 ms saat) | evet | hayır | `EventStreamView.xaml.cs:169-205, 768-779` |
| Satır nefesi, şerit süpürmesi, graf beads ve seçim kenarı akışı | evet | hayır | `ProjectRow.xaml.cs:621-633`; `StickyRibbon.xaml.cs:391-414`; `GraphView.xaml.cs:1084-1090, 1150-1166, 1426-1439` |
| Bitiş finali (154 projede 5,68 s) | evet | hayır | `MainWindow.xaml.cs:938-942`; `GraphView.xaml.cs:535-566` |
| İmleç saatleri, `BuildingSpinner` | hayır (kapılı) | | `ConsoleView.xaml.cs:577`; `EventStreamView.xaml.cs:107-111, 419`; `BuildingSpinner.cs:127` |

Tek bir "yüzey gizli" sinyali yok; görünürlük dört ayrı yerde üç farklı tanımla okunuyor (N1-6).

### 3.2 Yapılacak: "gizli mod"

İlke: durumun tek kaynağı `RunViewModel`. Pencere gizliyken model birikmeye devam eder, görünümlere dokunulmaz.
Pencere görününce görünümler modelden **tek seferde** yeniden kurulur. Adımlar doğrulayıcı tarafından tek tek kodda
kontrol edildi.

| # | Adım | Değerlendirme | Dönüşte yeniden senkron |
|---|---|---|---|
| 1 | Koreografi ve finali gizliyken atla; komut hemen gitsin | Güvenli. Mevcut azaltılmış-hareket dalı aynı son hâli kuruyor. Kurulum alan başlatıcısından ctor'a taşınmalı (lambda `this` yakalayamaz). Koşu ortasında gizlenirse `_choreographer.Cancel` ve `CancelEndFinale`. | Sonradan oynatılmaz |
| 2 | Konsol batch'lerini belgeye basma; koşu başındaki temizliği de erteleme | Güvenli, en büyük kazanç. Kapı kabukta: `AppendConsoleBatch` batch'i düşürür ve konsolu "bayat" işaretler. | `SeedRunDocument` / `SeedProjectDocument` (mevcut); eksik tek parça animasyonsuz yeniden kurma çağrısı |
| 3 | Olay akışı satırlarını ve daktiloyu oluşturma | Güvenli, kazanç küçük | Mevcut `RebuildRows()`; daktilo geriye dönük oynamaz |
| 4 | Graf push'larını atla | Güvenli. `PushGraphStatuses` başına tek erken dönüş. | `PushGraphRunPhase` + `PushGraphStatuses` + `PushGraphSelection` |
| 5 | Şerit chip'leri, Build menüsü satırları, liste takibi, satır süre yazımları | Güvenli | `RefreshAll` benzeri tek geçiş + `ApplyProjectGroups(reveal: false)` |
| 6 | 200 ms tick gövdesini kapıla | **Dikkat:** timer durdurulamaz. Motor sessizlik bekçisi bekleyen çıkışı da serbest bırakıyor (`RunViewModel.Exit.cs:75-88`); timer durursa tepsiden Exit + susmuş motor = uygulama kapanmaz. Gizliyken yalnız bekçi koşmalı. | Görünür olunca tick gövdesi bir kez |
| 7 | Satır nefesi, şerit süpürmesi, beads, seçim kenarı akışı | Güvenli, kazanç küçük (saatler meşgul sürenin %3,4'ü). **Dikkat:** sinyal `MotionGate`'e eklenirse göstergenin iki okuması (`MainWindow.xaml.cs:1240, 1252`) ham sinyalde kalmalı, yoksa gösterge de durur. | Mevcut `ReapplyMotion` / `AnimationsEnabledChanged` yolları |
| 8 | Kök içeriği `Collapsed` yapmak | Tek başına **yetmez**: metni değişen yaprak öğeler Collapsed ata altında da ölçülüyor, yalnız yukarı yayılım kesiliyor (sonda .NET Framework WPF'te yapıldı; .NET 10'da ayrıca doğrulanmalı). 2-5. adımlar asıl kaldıraç. Ayrıca gizliyken layout okuyan kodlar var (konsol pini, `StickyLayerList.SetGroups`). | Tek layout turu + kaydırma pinleri layout'tan sonra |

Bütünlük için dikkat edilecekler:

- Konsolun yeniden kurulumu pencere görünür olduktan **sonra** yapılmalı (pin kodu layout okuyor).
- Tepsideyken sessiz Sync topoloji getirirse liste satırları kurulmayabilir; dönüşte `ApplyProjectGroups` şart.
- Uçuştaki konsol batch'lerini mevcut nesil damgası düşürüyor; yeni bir tampon yolu açmaya gerek yok.

Beklenen etki:

| Ölçü | Bugün | Gizli moddan sonra | Kanıt |
|---|---|---|---|
| Kısayol → `runStarted` | 5,68 s | ~2,1 s | kod + ölçüldü |
| Tepside UI thread'i | 1.385 Mcycles/s | ~200-250 Mcycles/s | tahmin: layout %70 + render %14 kalkar |
| Tepside App toplamı | tek çekirdeğin %57'si | ~%20 | tahmin |
| Göstergede 100 ms üstü kesinti | 25 s'de 7 | 0'a yakın | tahmin: en uzun dilimlerin ilk işi kalkan işler; kalan olay işi en kötü 10 ms ölçüldü |

İlk kalıcı test: "pencere gizliyken koşu olayları ve konsol batch'leri layout geçişi başlatmaz"
(`HiddenCursorClockTests` deseninde). Sonra aynı ölçüm script'iyle yeniden ölçüm.

### 3.3 Motor tarafı: komut gittikten sonraki ~2,1 s

| Adım | Bugün | Yapılabilecek |
|---|---|---|
| Harici kökleri güncelle | Kökler arası seri; ağ turu ~1,2 s | Kökler birbirinden bağımsız, aralarında paralel koşabilir. Taramadan önce olmak zorunda (belgeli karar). Seyrekleştirme ya da zaman aşımı kullanıcı kararı. |
| İki defteri yükle (12 MB) | Ağ beklemesinden sonra | Ağ beklenirken yüklenebilir |
| Değerlendirme + koşulsuz 5,9 MB yazım | Her koşu | Değişiklik yoksa yazma |
| Projelerin ikinci kez okunması | Her koşu | İlk sonucun yeniden kullanımı |
| İki tanı amaçlı git çağrısı | Seri | Stat turuyla paralel |
| Koşulsuz 6,26 MB yazım | `runStarted`'dan önce | Değişiklik yoksa yazma, ya da `runStarted`'dan sonraya |

Aynı hesabın büyük kısmı bir önceki Sync'te yapılmıştı; motor hiçbir sonucu tutmadığı için Build baştan hesaplıyor.
Sonuç tutmak RAM ile takas (§7.2), bu yüzden önce yazımları ve çift okumayı kaldırmak doğru sıra.

Ayrıca **[kod]**: `git fetch` 30 s tavanla ve `GIT_TERMINAL_PROMPT` kapatılmadan çalışıyor (`GitService.cs:72, 200`).
Ağ ya da kimlik sorunu olduğunda tepsiden Build bu süre kadar bekleyebilir.

### 3.4 Tepside geri bildirim boşlukları (N1-8, N2-3)

- Sync sürerken kısayola basılırsa istek kuyruğa alınır ama gösterge çıkmaz; kapı kapalıysa basış sessiz kalır.
- Çıkış evresi sürerken yeni koşu başlarsa gösterge koşu ortasında kapanır, balon yanlış satırı taşır, ikinci
  koşunun sonuç balonu hiç gelmez (`TrayBuildIndicatorController.cs:94-103, 124-131`).

İkisi de performans değil davranış konusu. İlki ARCHITECTURE §12.3'teki "Syncing kapsam dışı" kararına dokunur,
karar gerekir.

---

## 4. Sağ alttaki gösterge animasyonu

Yapı **[kod]**: tek pencere örneği, tembel kurulum, her koşuda yeni pencere açılmıyor. Ana pencere görünürken
gösterge hiç yok; pencere geri gelince anında gizleniyor ve saati sökülüyor. Koşu dışında gösterge için çalışan
zamanlayıcı yok. Bu kısım temiz.

| Konu | Durum | Yapılacak |
|---|---|---|
| Akıcılık | Ana UI thread'inde dönüyor; tepside 25 s'de 7 kez 100 ms üstü kesinti **[ölçüldü]** | Gizli mod (§3.2). Ayrı dispatcher thread'i şimdi gerekli değil: kesintilerin kaynağı gizli modda kalkan işler; GC duraklamasını ayrı thread de çözmez. Gizli moddan sonra kare aralığı yeniden ölçülmeli. |
| Kendi maliyeti | Döngü tek çekirdeğin en çok %6,5'i; tepside render thread'i 248 Mcycles/s **[ölçüldü]** | Önce ölçümü düzelt: test bugünkü ölçeği ve yalnız UI + render thread'ini ölçmeli (N2-2). Gölgeyi sarmalayıcıya taşımak işe yaramaz (girdi her karede değişiyor). |
| Yazılım çizimi (RDP, VM, eski ekran kartı) | `RenderCapability.Tier` hiç okunmuyor; döngü aynen oynuyor | Ölçülürse: mevcut statik kare yolunu Tier 0'da kullan (N2-4) |
| Çalışma alanı değişimi | Konum yalnız gösterildiği anda hesaplanıyor | Gösterge görünürken `WorkArea` değişimine abone ol (N2-5) |
| Tam ekran uygulama, sunum | Gösterge yine çıkıyor | `SHQueryUserNotificationState` kontrolü; ürün kararı (N2-6) |
| İlk gösterim | Pencere kısayolla Build anında kuruluyor | Önce ölç; 30 ms'yi aşıyorsa tepsiye ilk inişte önceden kur (N2-9) |

---

## 5. Arayüz akıcılığı

### 5.1 Ölçülen durum

Bu makinede ön planda derleme sırasında donma yok: UI gecikmesi p99 9-16 ms, en çok 34-70 ms, 100 ms üstü kesinti
yok. Sorun tepside (§2.3) ve az çekirdekte arayüzün derlemeden CPU alması (§2.6).

### 5.2 Görünür pencerede de geçerli olan işler

| İş | Ölçülen | Konum | Çözüm |
|---|---|---|---|
| Kapalı Build menüsünün üç satırı her sayaç değişiminde yıkılıp kuruluyor | 185 ms / 25 s; 233 ms'lik bir kesintinin ilk işi | `BuildMenu.xaml.cs:67-70, 98-106` | Yalnız toplam proje sayısı değişince kur (D6-8) |
| `TopologicalDepths` her statü itişinde baştan hesaplanıyor | 175 ms / 25 s | `GraphBinder.cs:37-39`; `MainWindow.xaml.cs:832-856` | Topoloji başına bir kez önbellekle |
| Şerit chip'leri derlenen küme her değiştiğinde sıfırdan kuruluyor | 301 ms / 25 s | `StickyRibbon.xaml.cs:432-470` | Artımlı güncelle (ekle/çıkar) |
| Konsol batch'i: ekleme + baştan kırpma + sona kaydırma | Ön planda 1.334 ms / 25 s | `ConsoleView.xaml.cs:274-319` | Anlatı modunda ham satır akışını seyrelt; ölü kopya bloğunu sil (D6-5) |
| Liste paneli realize ve temizleme | Ön planda 361, tepside 916 ms | `FixedHeightVirtualizingPanel.cs` | Gizli mod; tetikleyici ayrıca atfedilmeli |
| Filtre kutusu her tuşta listeyi sıfırlıyor | Tuş başına 43-58 ms | `ShellRoot.xaml:69` | `Binding.Delay` (tek satır) (N3-3) |
| Proje logu açma / Back tek dispatcher turunda | Ölçülmedi | `ConsoleView.xaml.cs:341-385, 714-768` | Önce ölç (N3-2, D7-6) |

### 5.3 Trace etiketlerinin gerçekte ne olduğu

- "Kilit bekleme" (meşgul sürenin %19-23'ü): bloklanma değil, kilit alma maliyeti. Çağıranlar `Visual.GetDpi`,
  `ResourceDictionary.GetValue`, `AutomationPeer.UpdateSubtree`. Layout hacmiyle orantılı.
- "GC bekleme" (%19-23): thread'in durdurma isteğine yanıt verdiği noktalar; trace'in kendi örneklemesini de içerir.
  Gerçek GC duraklama süresi ölçülmedi. Ölçülmesi gerekir (`GC.GetTotalPauseDuration`).
- Finalizer thread'i WPF metin biçimlemesinin DWrite sarmalayıcılarını bırakmakla uğraşıyor (25 s'de 5 s). Bu,
  metin biçimleme hacminin yan ürünü; hacim düşünce düşer.
- UI Automation: yığında Automation geçen toplam süre tepside 2.907 ms (%25,5), ön planda 2.144 ms (%23). Yalnız bir
  UIA istemcisi dinlerken oluşur; ölçüm script'leri UIA kullanmıyor, makinede başka bir istemci dinliyor. Kaynağı her
  layout geçişinden sonraki eş ağacı eşitlemesi. Layout azalınca bu da azalır. Konsol ve şeridi erişilebilirlik
  ağacından çıkarmak mümkün ama belgeli erişilebilirlik kararlarıyla çatışır.

### 5.4 Diğer donma adayları (ölçülmedi, kodda var)

- Pano kilitliyken Copy log UI thread'inde `Thread.Sleep` ile bekliyor (`ClipboardRetry.cs:17-18, 41-45`) (N3-4).
- Her pencere aktivasyonunda UI thread'inde git dizini yoklaması; her kapatmada `ui-state.json` iki kez okunuyor
  (N3-7, D8-6).
- Motor olayları UI thread'ine Normal öncelikle, birleştirilmeden giriyor (`MainWindow.xaml.cs:339-343`). Ölçülen
  girdi gecikmesi küçük; asıl kaldıraç türev güncellemelerin olay başına değil boşaltma başına bir kez yapılması (N3-1).

---

## 6. Boşta CPU

| Durum | Ölçülen | Kaynak | Çözüm |
|---|---|---|---|
| Ön planda boşta | 181 Mcycles/s (%4,4) | İki imleç, dört sonsuz saat (2 × 30 kare/s kırpma + 2 × 20 kare/s renk turu) | Pencere aktif değilken imleci durdur (Windows geleneği); iki imleci tek saate bağla (D1-2). Tasarım kararı. |
| Pencere başka pencerenin arkasında ya da simge durumunda | Ön plandakiyle aynı (kapı yok) | `IsVisible` simge durumunda da true | Aynı kapıya `WindowState == Minimized` ekle; önce ölç (D7-2) |
| Seçili proje varken | Seçim kenarı akışı sonsuza dek döner, tepside de | `GraphView.xaml.cs:1385-1439` | Gizli mod kapısı (D1-1) |
| Tepside boşta | 37 Mcycles/s (%90'ı UI thread'i) | 200 ms tick ve dispatcher zamanlayıcı yönetimi | Boşta tick'e gerek yok: yalnız koşu ya da bekleme varken çalışsın (D1-5; bekçi koşulu korunarak) |
| Koşudan sonra tepside boşta | UI thread'i 129 Mcycles/s | **Bulunamadı** | Gizli moddan sonra yeniden ölç; kalırsa trace ile ayır |
| Pencereye her dönüş | Supervisor ~1,8 çekirdek-saniye + App ~1,2; Supervisor +36-91 MB | 5 s eşiğiyle her aktivasyonda tam sessiz Sync; kendi diyalogları kapanınca da tetikleniyor | Önce Sync'i ucuzlat (§7.2). "Değişiklik yoksa atla" §10.3 kararıyla çatışır, karar gerekir (D1-8, D11-1). |

2026-09-14'te tepside boşta ~5 Mcycles/s ölçülmüştü; bugün 37. Aradaki fark o tarihten sonra eklenen işlerden.

---

## 7. Bellek

### 7.1 Uygulama (App)

Ölçülen: 158 → 433 MB (iki Rebuild). Zorla GC 114 MB'ı geri veriyor; canlı managed büyüme 58,6 MB.

| Kaynak | Kanıt | Çözüm |
|---|---|---|
| Koşu metni üç-dört kopya tutuluyor ve bir sonraki işleme kadar bırakılmıyor (`_runText`, `_liveLines`, konsol `_backlogLines`, `_projectText`) | **[kod]** `RunViewModel.cs:2694-2716, 2740-2754, 2798-2843`; **[ölçüldü]** canlı heap 74 MB, tek 13,7 MB `char[]` | `_liveLines`'ta olay nesnesi yerine (satır no, metin) sakla; koşu bitince bırak; seçimi kalkan projenin `_projectText`'ini bırak (D2-1, D6-1, D10-4, N5-6) |
| Toplanmamış çöp ve geri verilmeyen commit | **[ölçüldü]** 433 → 319 MB zorla GC ile | Koşu bitti + pencere gizli + gösterge oynamıyorken tek seferlik toplama. Önce tampon bırakma, sonra ölçüm. |
| Tahsis hacmi (metin biçimleme, olay başına koleksiyonlar) | **[ölçüldü]** §2.2 | Gizli mod ve §5.2 |

**Dikkat (yarış):** "proje bitince `_liveLines`'ı sil" tek başına güvenli değil. Proje logu yanıtı olay kanalını
atlayıp doğrudan yazılıyor (`SupervisorHost.cs:376-381`); canlı satır, sonuç olayı ve chunk sırası garanti değil.
Bırakma, dikiş mantığıyla birlikte tasarlanmalı.

### 7.2 Motor (Supervisor)

Ölçülen: canlı 2 MB; Private 150-336 MB; zorla GC sonrası 112-148 MB.

Kök neden **[kod]**: hiçbir defter örneği süreç boyu tutulmuyor (`SupervisorHost.cs:43-57`, `Program.cs:94-97`). Her
Sync ve her Build planlaması iki dosyayı `File.ReadAllText` ile UTF-16 string'e çeviriyor (~12 + ~12,5 MB),
nesne grafını kuruyor, sonra koşulsuz yeniden serialize edip yazıyor (`BuildPlanBuilder.cs:43`, `Program.cs:201`,
`SyncWorkspaceService.cs:324`). Sync başına ~48 MB büyük nesne yığını string'i + iki nesne grafı çöp oluyor.

| Sıra | Seçenek | Etki | Not |
|---|---|---|---|
| 1 | Kirli bayrağı + akışla okuma/yazma (`Deserialize(Stream)`, `Serialize(Stream)`) | Sync/Build başına 12 MB yazım ve ~48 MB geçici string kalkar **[kod]**. Kalıcı bellek +0. | En düşük risk; GC davranışından bağımsız kazanç. `SourceHashCache`'te yazılmayan "racy" girişler için bayrak kirli kalmalı. |
| 2 | İş bitiminde rahatlama: `GC.Collect(2, Aggressive, blocking, compacting)` | **Ölçülmeli.** Sıradan zorla GC yalnız 6-38 MB geri verdi; commit'i geri vermeyi hedefleyen tek API `Aggressive`. | Önce tanı: `GC.GetGCMemoryInfo().TotalCommittedBytes` ile Private'ı yan yana yaz; kalan belleğin GC commit'i mi yerel bellek mi olduğu ayrılır. |
| 3 | GC ayarları (`ConserveMemory`, gen0 bütçesi, concurrent kapalı) | Deneyde tutarlı kazanç yok; concurrent kapalıyken motor 235-402 MB'a çıktı **[ölçüldü]** | Tek başına çözüm değil. `Concurrent=false` açılmamalı. |
| 4 | Talep üzerine motor: boşta kapat, komutta yeniden başlat | Boşta 150-336 MB → 0 | Uzun iş; bugünkü kodda her `engineReady` Sync başlatıyor, kapalı motora komut yolu yok. ARCHITECTURE §4.6'yı değiştirir, karar gerekir. 1-2 hedefi tutarsa gerekmez. |
| — | Süreç boyu tek defter örneği | Sync başına çöp kalkar ama boşta kalıcı ~24 MB **ekler** | RAM önceliğiyle çatışır; önerilmez |

### 7.3 Ölü defter girişleri ve test artıkları

- `evaluation-cache.json` 5,9 MB; 2.761 girişin 2.558'i test süitinin `Temp\bo-vm-*` artığı. Hepsi eski şemalı.
  Optimize yalnız kök altını budadığı için hiç silinmiyor **[ölçüldü]**.
- Log klasöründe en az 741 klasör aynı testten (`run r1 … projects=2`).
- Kaynak: `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs:1304` civarı gerçek motoru izole etmeden koşturuyor
  (kesin yol doğrulanmalı). ARCHITECTURE §17.2'deki izolasyon guard'ı bu yolu kaçırıyor.
- Çözüm: Optimize şeması eski girişleri kökten bağımsız budasın (D9-2); testin motoru `--logs` ile izole edilsin.
  Bu yalnız geliştirici makinesini etkiler; son kullanıcıda test koşmaz.

---

## 8. Build motoru

| Bulgu | Ölçülen | Çözüm | Karar |
|---|---|---|---|
| Restore prologu `packages.config`'li projede her koşuda çalışıyor (D4-5) | MSBuild süresinin %7,1'i; çağrı başına 0,76 s; koşu başına 13-20 çağrı | `packages.config` içerik özeti başarılı restore'da saklansın; değişmediyse ve paket klasörü yerindeyse atla | yok |
| `-clp:Summary` uyarıları koşu sonunda ikinci kez yazdırıyor (D4-1) | Resolve'da satırların %29'u, Rebuild'de %10'u tekrar | `-clp:NoSummary` | Log sonundaki toplu hata listesi de kalkar; §9.2 değişir |
| MSB3277 uyarı seli (D10-6) | Tüm satırların %49,5'i | OSYS'te referans hizalama, ya da araçta `-warnAsMessage:MSB3277` | §9.2 argüman sözleşmesi |
| Zamanlayıcı yalnız build sırasına bakıyor (D4-3) | Bir koşuda en çok %9; homojen koşuda 0 | Kritik yol önceliği | Düşük öncelik |
| MSBuild çağrısının taban maliyeti (D4-7) | Derleme yapmayan çağrı medyan 0,44 s | Büyük ölçüde araç dışı | — |
| `build-state.json` her proje sonucunda baştan okunup yazılıyor (D2-6, D9-5) | 152 projede ~45 MB IO; süre ölçülmedi | Bellek içi harita + yalnız serialize; sıralama garantilerine dikkat | Düşük öncelik |
| Proje logunda satır başına flush (D9-6) | Koşu başına 23-112 bin `WriteFile` | Tamponlu yazım | Kesilmede son satırlar kaybolabilir; karar |
| Her MSBuild çağrısı gizli konsol açıyor (D3-5) | Koşuda 5-9 `conhost.exe` | Yalnız spike; `DETACHED_PROCESS` görünür konsol riski taşıyor | Ölçmeden yapma |
| 32-bit `MSBuild.exe` seçiliyor (D4-6) | Fark ölçülmedi | Ölçmeden dokunma | — |
| WPF projelerinde çağrı başına iki csc koşusu (D5-11) | 149 `_wpftmp` derlemesi / 60 koşu | Araç tarafından kaçınılamaz | — |

Paylaşımlı derleyici (`UseSharedCompilation`) kararı §9.2'de gerekçeli olarak kapalı; yeni bir kanıt yok, yeniden
önerilmiyor.

---

## 9. Resolve cycles

2026-10-01 01:04 koşusu: 184 s; 7 grup, 33 üye. 17 üyeli UI grubu tur 1 ~81 s + tur 2 ~79 s.

| Bulgu | Durum | Çözüm |
|---|---|---|
| UI grubunda 2. tur 17/17 koştu (D5-1) | **[ölçüldü]** 79 s, koşunun %43'ü. Yüzey kanıtı grup başında sessizce kapanmış: tek okunamayan dosya tüm grubun kanıtını iptal ediyor ve başlangıç dalı log yazmıyor (`RunCoordinator.cs:1527-1533`). 27 Eylül sonrası 5 UI koşusunun 1'inde oldu; diğer üçü tek turda, biri seçici turla bitti. | **Önce yalnız log**: hangi üretici, hangi dosya, neden. Kök neden görülmeden davranış değişikliği yapılmamalı. |
| `decision.log` tur kararının nedenini yazmıyor (D5-6) | **[kod]** | Bayat üyeler, hareket eden dosya, seviye planı, tur süresi, hash süresi |
| Yüzey hash'i kritik yolda ve önbelleksiz (D5-5) | **[ölçüldü]** UI grubu başında 5,93 s; koşuda toplam 7,7 s (%4,2) | Grup başı hash'i üreticiler arasında paralel; derleme sonrası hash'i slot bırakıldıktan sonra |
| Restore tur 1'de kritik yolda (D5-9) | **[ölçüldü]** ~2,9 s | §8'deki koşullu restore |
| Üye düzeyinde artımlılık yok (D5-3) | **[kod]** grup kirliyse tur 1 her üyeyi derler | Büyük tasarım; §7.3'ü değiştirir. Önce birkaç gerçek koşuda "kaç üyenin içeriği değişmişti" sayılmalı (defterde alan var). O koşuda 7 grubun 7'si kirliydi; bu tasarım orada hiçbir şey kazandırmazdı. |
| Bariyerli seviyeler yerine ready-set (D5-4) | Gerçek sürelerle benzetim: 81,0 → 80,6 s | **Yapma** |
| ETA cycle terimi (D5-2) | İddia çürütüldü. Gerçek sapma ters yönde: ETA ~1,8 kat az gösterdi. | ARCHITECTURE §8.4 ile kod uyuşmuyor (aşağıda §14) |
| Kesim önerisi raporu (D5-7) | Yeni özellik; kazanç OSYS'te kenar kesilirse oluşur | Performans işi değil |

Sıra: log (D5-6, D5-1) → hash paralelliği (D5-5) → koşullu restore → ölçüm → gerekirse üye düzeyi tasarım.

---

## 10. Disk ve durum dosyaları

| Bulgu | Ölçülen | Çözüm |
|---|---|---|
| Koşu loglarında saklama süresi yok (D9-1) | 1.640 klasör, 1,5 GB, ayda ~1 GB | Sayı / gün / toplam boyut sınırı; motor açılışında arka planda. ARCHITECTURE §10.3 ve §16 aynı işte güncellenir. İlk temizlik ~25 bin dosya siler; parça parça yapılmalı. |
| İki defterin koşulsuz yazımı (D9-3, D11-3) | Sync ve Build başına 12 MB | §7.2 madde 1 |
| `evaluation-cache.json` faydalı yükünün %91'i dosya yolu listeleri (D9-4) | 4,26 MB → ~0,3 MB olabilir | Klasör taramasının zaten bulduğu yolları saklama; düşük öncelik |
| Durum klasöründe okunmayan eski dosyalar (D9-10) | `config.json`, `settings.json`, `dependency-graph.json` | Temizlik ya da dokümana not |
| Antivirüs dışlama önerisi dokümanda yok (D9-9, N4-8) | Etkisi ölçülmedi | README'ye öneri: repo `bin`/`obj`, durum klasörü, `MSBuild.exe`, `csc.exe` |

---

## 11. Kapanış ve durdurma

| Bulgu | Durum | Not |
|---|---|---|
| Zorla kapanışta artık süreç kalmıyor | **[ölçüldü]** 77-200 ms | Temiz |
| Stop yalnız graceful: en yavaş projeyi bekliyor ve drain boyunca CPU tavanı kalkıyor (D3-1) | **[kod]** `RunViewModel.cs:1560`; `RunCoordinator.cs:295, 328` | "Durdurdum, hemen rahatlasın" beklentisinin tersi. İkinci Stop = sert durdurma ya da drain'de tavanı koruma §4.5 kararını değiştirir; karar gerekir. Zayıf makinede 4 MSBuild + csc tavansız koşar. |
| Planlama penceresinde Stop işlemiyor (D3-4) | **[kod]** planner iptal token'sız | Süre ölçülmedi |
| Exit beklerken motor 90 s sessiz kalırsa drain sert kesilir (D3-7) | **[kod]** | Derleme sırasında da heartbeat |
| Koşu sonunda inner job'ın boş olduğu doğrulanmıyor (D3-2) | Ölçümde kalıntı yok | Yalnız tanı satırı |
| Supervisor ömür boyu bir `conhost.exe` taşıyor | **[ölçüldü]** 7 MB | Düşük öncelik |

---

## 12. Zayıf donanım uygunluğu

| Konu | Durum | Yapılacak |
|---|---|---|
| İşçi sayısı (N4-1) | **[kod]** `PerfProfile.cs:30-36` sabit 6/4/2; `RunCoordinator.cs:845` üst sınır uygulamıyor. **[ölçüldü]** 2 işlemcide 4 işçi, 66 s. | `min(profil, f(çekirdek), f(boş RAM))`. Kısma motorda yapılırsa ETA kendiliğinden doğru kalır; gösterim fiili sayıya bağlanmalı. Eşik için önce 2 işlemcide 1 ve 2 işçiyle koşu ölçülmeli. §11.1 "üç sabit profil" kararını değiştirir. |
| Bellek koruması (N4-3) | Koşu, boş belleğe bakmadan başlıyor. Bellek yetersizliğinden çıkan derleyici "kaynak bozuk" kanıtı sayılıyor. | Koşu başında düşük bellek uyarısı ve işçi azaltma. Sınıflandırma için önce gerçek bir OOM çıktısı yakalanmalı. |
| Arayüzün derlemeden CPU alması | **[ölçüldü]** 2 işlemcide ön plan %30 daha yavaş | Gizli mod ve §5.2 |
| İçerik kararı IO'su sabit 16 yollu (N4-4) | **[kod]** aynı sayı beş yerde | Şimdi: tek sabite çek (kopya yasağı). Değer değişimi ölçümden sonra. |
| Yazılım çizimi (N4-5, D7-9) | `RenderCapability.Tier` okunmuyor | Tier 0'da ölç; anlamlıysa azaltılmış hareket yoluna ikinci sinyal |
| ReadyToRun (N4-7, D8-3) | Yok. Açılış 832-1.704 ms ölçüldü; fark soğuk/sıcak durumdan. | Kazanç ölçülmeden eklenmemeli (zayıf makinede ya da 2 işlemci yakınlığıyla karşılaştır) |
| Pil ve ısı (N4-9) | Boşta sürekli uyanıklık (§6) | §6'daki işler |

---

## 13. Yol haritası

| Aşama | İş | Bulgular | Boyut | Risk | Kazanç |
|---|---|---|---|---|---|
| **A. Tepside derleme** | Koreografi + finali gizliyken atla | N1-1, N1-5 | kısa | düşük | Kısayol → derleme 5,7 → ~2,1 s |
| | Konsol batch kapısı + dönüşte yeniden kurma | N1-3, D10-5 | kısa | düşük | UI meşgul süresinin %18,6'sı |
| | Görünüm beslemeleri kapısı + tek seferlik yeniden senkron | N1-4, N1-6, D6-3, D7-3 | orta | orta | Layout geçişlerinin kaynağı |
| | Sonsuz saat kapıları | D7-1, D6-4, D1-1 | kısa | düşük | Saatler %3,4 + doküman uyumu |
| | Gizli pencere için kalıcı test + ölçüm testi | N3-5, N2-2 | kısa | düşük | Geri dönüşü önler |
| **B. Ucuz ve risksiz** | Build menüsü satırları; `TopologicalDepths` önbelleği; şerit chip'leri | D6-8, N1-2 | kısa | düşük | 25 s'de ~660 ms UI işi |
| | Defterlerde kirli bayrağı + akışla okuma/yazma | D2-2, D9-3, D11-3, N5-2 | orta | düşük | 12 MB yazım + ~48 MB geçici string / Sync |
| | Filtre kutusu gecikmesi; ölü konsol kopyası; tek yazımda NDJSON | N3-3, D6-5, D10-2 | kısa | düşük | küçük |
| **C. Bellek** | Motor: rahatlama adımı (önce ölçüm) | N5-1, N5-9, D2-3 | kısa | düşük | Ölçülmeli |
| | App: log tamponlarının bırakılması | N5-6, D2-1 | orta | orta | ~58 MB canlı + çöp |
| | App: koşu bitti + gizliyken tek seferlik toplama | N5-5 | kısa | orta | 433 → ≤319 MB |
| | Log saklama süresi; eski şemalı girişlerin budanması; test izolasyonu | D9-1, D9-2 | orta | düşük | Disk |
| **D. Zayıf donanım** | 2 işlemcide 1-2 işçi ölçümü, sonra işçi kısma | N4-1, N4-2 | orta | orta | Paging ve aşırı yüklenme |
| | Düşük bellek uyarısı; 16 sabiti | N4-3, N4-4 | kısa | düşük | |
| **E. Build ve Resolve** | Motor hazırlığı: çift okuma, koşulsuz yazım, paralel harici kök | D8-1, D11-5 | orta | orta | ~2,1 s'nin bir kısmı |
| | Koşullu restore | D4-5, D5-9 | orta | düşük | MSBuild süresinin %7'si |
| | `NoSummary`; MSB3277 | D4-1, D10-6 | kısa | karar | Log hacmi %5-50 |
| | Resolve: karar logu, hash paralelliği | D5-6, D5-1, D5-5 | kısa | düşük | ~7,7 s + teşhis |
| **F. Boşta** | İmleç kapısı ve tek saat; boşta tick | D1-2, D1-5 | kısa | karar | Ön planda boşta 181 → ~40 Mcycles/s |

Çalışma kuralı gereği her iş kırmızı testle başlar; A aşamasından sonra aynı script'lerle yeniden ölçüm alınır
(§16).

---

## 14. Karar gerektirenler

Doküman ile kod uyuşmuyor; hangisinin doğru olduğunu senin söylemen gerekiyor:

| # | Doküman | Kod |
|---|---|---|
| 1 | §14.5: "every infinite animation is gated on IsVisible" | Satır nefesi, şerit süpürmesi, graf beads ve seçim kenarı akışı kapısız |
| 2 | §14.5: "only transform and opacity are animated, never layout" | Şerit ve ilerleme çubuğunda `Width` animasyonu (D7-7) |
| 3 | §8.4: tahmin `BuildState.LastDurationMs`'ten gelir | ETA koşu içi ortalamayı kullanıyor (`RunViewModel.cs:2398-2413`) |
| 4 | §16: değişiklik yoksa dosya yeniden yazılmaz | Sync ve koşu yolunda koşulsuz `Flush` |

Bilinçli bir kararı değiştiren öneriler:

| # | Öneri | Dokunduğu karar |
|---|---|---|
| 5 | Gizli pencerede koreografiyi atlamak | §14.5 "ya her zaman oynar ya hiç" |
| 6 | İşçi sayısını donanıma göre kısmak | §11.1 "üç sabit profil" |
| 7 | İkinci Stop = sert durdurma, ya da drain'de tavanı korumak | §4.5 |
| 8 | Pencereye dönüşte değişiklik yoksa Sync'i atlamak | §10.3 |
| 9 | Log saklama süresi | §10.3 "never deleted", §16 |
| 10 | `-clp:NoSummary`, `-warnAsMessage` | §9.2 argüman sözleşmesi |
| 11 | İmleç yalnız pencere aktifken yanıp sönsün | Tasarım |
| 12 | Kuyruktaki Build için gösterge | §12.3 "Syncing kapsam dışı" |
| 13 | Proje logunda tamponlu yazım | Kesilmede son satırların kaybı |
| 14 | Talep üzerine motor | §4.6 |
| 15 | Resolve'da üye düzeyi artımlılık | §7.3 |

---

## 15. Temiz bulunan alanlar

- Kapanış kaskadı: uygulama ölünce her şey 77-200 ms'de gidiyor, artık süreç yok.
- Motor boşta hiç CPU harcamıyor; koşu sırasında belleği büyümüyor (sınırsız olay kanalı bu yükte birikmiyor).
- Build modunda her şey güncelken hiç MSBuild çağrılmıyor.
- MSBuild çağrıları `-m` taşımıyor; aracın kendi paralelliği dışında gizli paralellik yok.
- Log satırları UI thread'ine satır başına taşınmıyor; batch başına tek çağrı.
- Konsol pompası boşta uyuyor; imleç saatleri ve `BuildingSpinner` görünürlüğe kapılı.
- Gösterge: tek pencere, tembel kurulum, koşu dışında zamanlayıcısı yok.
- UI thread'inde `.Result` / `.Wait()` yok; "Open in Visual Studio" UI'yı bloklamıyor.
- Server GC açık değil (zayıf makine için doğru varsayılan).
- IPC satır taşıması ucuz: uygulamanın okuma tarafı 25 s'de onlarca ms.

---

## 16. Ölçümün yeniden üretimi

Hepsi `.claude/temp/perf-2026-10-01/` altında (git'e girmez).

| Dosya | İş |
|---|---|
| `measure2/probe-helper.ps1` | Thread başına döngü, UI gecikme sondası, makine CPU'su, uygulamayı açma/kapama |
| `measure2/measure2.ps1` | Boşta, tepside, ön planda rebuild, bellek dökümü, trace |
| `measure2/measure3.ps1` | Süre karşılaştırmaları, kısayol gecikmesi, aktivasyon serisi, 4 ve 2 işlemci |
| `measure2/measure4.ps1` | Projedeki kapılı ölçüm testleri |
| `measure2/measure5.ps1` | GC ayarı denemeleri |
| `measure2/speedscope_analyze.py` | Trace'ten UI thread atfı |
| `logmine.py` | Koşu logları istatistiği |

```powershell
dotnet build BuildOrchestrator.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .claude\temp\perf-2026-10-01\measure2\run-all.ps1
```

Koşul: uygulama kapalı olmalı, makine boşta olmalı. Script'ler uygulamayı gerçek OSYS üzerinde açar, F6 ile Rebuild
başlatır ve sonunda süreci öldürür. Gereken araçlar: `dotnet-trace`, `dotnet-gcdump` (global dotnet araçları).

Bu çalışmada OSYS üzerinde 20 Rebuild ve 2 Build koşturuldu; ikisi gerçek derlemeydi (152 ve 141 proje), diğerleri
derleme yapmadan döndü. 1 Ekim koşularında 2, 2 Ekim koşularında 13 proje hata verdi; bu, çalışma kopyasının o
günkü durumu.
