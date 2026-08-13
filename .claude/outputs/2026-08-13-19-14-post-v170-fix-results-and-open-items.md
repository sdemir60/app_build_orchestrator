# Saha turu düzeltmeleri — sonuç ve açık konular

Branch: `fix/post-v170-field-findings` (11 commit). **`main`'e merge EDİLMEDİ** — kullanıcı bilgisayar
başında olmadığı için açık konular birlikte değerlendirilecek.

Doğrulama: `dotnet build` temiz · tam süit **1989 geçti / 1 atlandı** (o atlama önceden beri var) ·
acceptance **3/3 geçti** (gerçek OSYS reposu, motora dokunulduğu için ayrıca koşuldu).

## Yapılanlar

| # | Bulgu | Kök neden | Durum |
|---|---|---|---|
| 1 | Konsol yazısı "iç içe, ince, okunmuyor" | CompositeFont kök elemanı `presentation` ad alanıyla yazılmış; WPF dosyayı `FileFormatException` ile tümüyle reddediyor, konsol **Segoe UI Light**'a düşüyordu | düzeltildi |
| 2 | Şerit chip'i ilk üyede donuyor | `RunCounters` record struct: döngü içinde sıra el değiştirince demet aynı kalıyor, `PropertyChanged` yayılmıyordu | düzeltildi |
| 3 | Liste derleneni takip etmiyor | `FollowFrontier` ham `Started`'ı okuyordu; grup üyeleri grup bitene dek Started kalır | düzeltildi |
| 4 | Resolve'a tekrar basmak | Yakınsamama hafızası grubu sessizce pre-skip ediyordu | artık bloklamıyor, raporluyor |
| 5 | Konsol imleci | Overlay'di, ilk çıktıda kayboluyordu | kalıcı alt satır, amber |
| 6 | Resolve başında graf titremesi | Atlanan düğüm 0.13 → 0.2 diye geri parlıyordu | atlanan düğüm kıpırdamıyor |
| 7 | Tooltip eksikleri | Döngü yolu hiçbir tooltip'te yoktu; şerit kümesinin tooltip'i hiç kurulmuyordu | eklendi (tek kaynak `CycleText`) |
| 9 | Filtre sönmesi | 280 ms, kullanıcıya hızlı geliyordu | 420 ms, iki yönde simetrik |
| 10 | **(yeni bulgu)** Boşta %133 CPU | Gizlenen `StatusGlyph`'in sonsuz nabzı + 200 ms tick'te koşulsuz metin yazımı + yetim `DispatcherTimer` | düzeltildi; yeni ölçüm **%1.4** |

Kod değişikliği gerekmeyen iki cevap: **Cycles'ta SCC içi serilik** doğruluk gereğidir (üyeler birbirinin
DLL'ini okur/yazar; bağımsız gruplar ve upstream zaten paralel), ve **döngü atlamaları koşuyu kırmızıya
çevirmez** — `Skipped` kovasına girer, progress yeşil kalır.

## Açık konular (birlikte ele alınacak)

1. **Sol şeridin kalınlığı (bulgu 8) — hangi yüzey?** "Sol border'lar bir tık ince olmuş" dedin ama iki aday
   var: proje satırının sol statü şeridi mi, yoksa sol paneller mi? Ölçtüm, ikisi de tasarımın değerinde:
   şerit 2 px / seçilide 3 px + 1 px dikey boşluk (§2.4, prototip `BuildApp.jsx:549`), panel kenarları 1 px.
   Değerler testle pinli. Tahminle spec değerini değiştirmedim — hangisini kastettiğini söylersen bakarım.

2. **CPU ölçümü yarım kaldı.** Düzeltmeler sonrası boşta %1.4 ölçtüm ama bu **taze açılış**; %133'ü üreten
   durum bir build + Resolve koşusundan sonraydı ve o duruma programatik olarak giremiyorum. Sen bir tur
   çalıştırıp bittikten sonra Görev Yöneticisi'nde CPU'ya bakarsan kesin cevabı alırız.

3. **Bellek/thread ayrı bir iş.** Ölçümde 769 MB working set ve 50 thread gördüm; CPU düzeltmeleri bunu
   açıklamıyor. Şüpheliler: konsol metnini tutan `StringBuilder`'lar (`_runText`/`_projectText` — kırpma yolu
   bulamadım) ve tekrarlanan Sync'lerde biriken nesneler. Heap snapshot ister, okumayla çözülmez.

4. **Grafta seçim çizgileri.** Bir düğüm seçiliyken akan kesikli amber çizgiler sonsuz bir animasyondur ve
   `StrokeDashOffset` WPF'te pahalı bir özelliktir. Tasarım bunu istiyor (§2.3) ve seçim bırakılınca duruyor —
   yani kusur değil; ama uzun süre seçili bırakılırsa CPU maliyeti var. Boşta bırakma kuralı koyalım mı?

5. **`ProjectSkippedEvent.CycleUnconverged` artık motor tarafından üretilmiyor.** Bayrağın yeni kaynağı
   koşunun kendi `cycleCompleted` kararı; protokol alanı ve App'in onu okuyan dalı yerinde duruyor
   (savunmacı). İstersen alanı sözleşmeden tamamen kaldırırız.

## Gözle bakılacaklar

Font (artık gerçek Geist Mono Light, satır aralığı 1.55), imleç (alt satırda, amber, hep orada), chip akışı
ve liste takibi bir Resolve koşusunda, Resolve başında grafın sakinliği, filtre geçişinin hızı, döngü
tooltip'lerinde yol satırı.
