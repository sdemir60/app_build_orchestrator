# Boşta bellek azaltma — Dalga 3 sonuç raporu

**Tarih:** 2026-10-16, 12:00–13:25.

**Ölçülen kod:** `fix/idle-memory-wave3` `47bfa9af`. Kod `437f249b`'dedir; sonraki commit'ler yalnız doküman ve not içerir.

**Taban:** aynı gün, aynı makinede Dalga 2 programı. Bu, develop `f50478dd`'nin `git archive` kopyasından alınmış Release derlemesidir. Önceki rapor `.claude/outputs/2026-10-15-06-50-idle-memory-wave2-result.md`.

**Ham veri:** `.claude/temp/mem-2026-10-13/` altında.
- Karşılaştırmada kullanılanlar: `wave3-visible-r2/`, `wave3-tray-r2/`, `wave2b-visible/`, `wave2b-tray/`, `wave3-s0-1/2/`, `wave2b-s0-1/2/`.
- İlk koşu (harici monitör açıkken, karşılaştırma dışı): `wave3-visible/`, `wave3-tray/`.

**Spec:** `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md` §1, §6.4, §9. Kabul satırları karar 43'tür: görünürde S4, tepside T1b.

## 1. Tek cümle

Hedef tuttu:
- İşsiz motor 5 dakika sonra kendiliğinden kapanıyor.
- Koşudan sonra boşta App + motor toplamı görünür pencerede **236 MB**, tepside **230 MB** oluyor; motor 0 MB. Hedef ≤ 390 MB.
- Aynı gün, aynı koşulda Dalga 2 programı motoru açık tuttuğu için **285–312 MB**'de kalıyor.
- İlk taban 845 MB'ydi.

## 2. Önce ölçüm koşulu: harici monitör belleği değiştiriyor

İlk koşular (12:00–12:18) Dalga 2'nin çok üstünde çıktı:

| Koşu | Taze açılış (S0), App private | Tepside koşu sonrası (T1), App private |
|---|---|---|
| Dalga 2 gece ölçümü | 177 MB | 203 MB |
| Dalga 3 ilk koşu | 297 MB | 373 MB |

Yönetilen yığın ise aynıydı:
- S0: 28,2 / 28,1 MB.
- T1: 34,5 / 34,4 MB.
- GC'nin ayırdığı bellek T1'de 34,2 / 34,4 MB.

Fark yerel bellekteydi. Dalga 3 koşularında 32,9 MB'lık birkaç ek blok vardı; bu boyut 4K bir ekranın tam ekran çizim tamponuna denk geliyor.

Sahip ölçüm sırasında harici monitörü düğmesinden kapattı. O andan sonra Windows yalnız dizüstünün kendi panelini görüyordu (`Screen.AllScreens`: `\\.\DISPLAY1` 1920×1200 mantıksal, 2880×1800 fiziksel). Aynı ekran durumunda iki program sırayla (ABAB) açıldı:

| Sıra | Program | S0 App private | En büyük yerel blok |
|---|---|---|---|
| 1 | Dalga 3 | 178 MB | 26,4 MB |
| 2 | Dalga 2 | 183 MB | 26,4 MB |
| 3 | Dalga 3 | 182 MB | 26,4 MB |
| 4 | Dalga 2 | 171 MB | 26,3 MB |

Sonuç:
- Açılış belleği ekran düzenine bağlı, programa değil.
- Harici 4K monitör açıkken pencere çizimi yaklaşık **+120 MB** yerel bellek tutuyor.
- Bu dalga bellek regresyonu getirmedi.
- Aşağıdaki karşılaştırmaların hepsi aynı ekran durumunda (yalnız dizüstü paneli) yapıldı.
- Dalga 1–2'nin gece ölçümleri de büyük olasılıkla bu durumda yapılmıştı.

## 3. Görünür pencere (`mem-after-build.ps1`, aynı gün)

Hücreler "App private + motor private" ve toplamdır.

| An | Dalga 2 | **Dalga 3** |
|---|---|---|
| S0 taze açılış, 45 s boşta (ABAB ortalaması) | 177 + 41 | 180 + 41 |
| S1 koşu bitti, +60 s | 233 + 74 = 307 | 232 + 56 = **288** |
| S2 zorlanmış tam GC sonrası | 234 + 74 = 308 | 234 + 56 = 290 |
| S3 +3 dk boşta | 238 + 74 = 312 | 239 + 56 = 295 |
| W pencereye dönüş | 231 + 54 = 285 | 233 + 53 = 286 |
| **S4 motor uyudu** (pencereye dönüşten 301 s sonra) | (uyku yok, motor kalır) | **236 + 0 = 236** |

- **Boşta kalan uygulamanın toplamı 285–312 MB'den 236 MB'ye iniyor.** Motorun 53–74 MB'ı tamamen gidiyor; App yerinde kalıyor.
- Koşu sonrası motor bugün 56 MB (Dalga 2: 74). Fark koşudan koşuya değişen bir tutum; Dalga 3 motorun koşu yoluna dokunmadı.
- App'in canlı yığını S4'te 39,3 MB, S1'de 37,0 MB.

### Motorun koşu sonrası yığını (Dalga 2 raporunun açık sorusu Q12)

- S1'de canlı yığın 45,0 MB. Bunun 38,2 MB'ı 3 569 adet `System.Byte[]`, 4,8 MB'ı 100 adet `System.Char[]`. 85 KB'tan büyük nesneler toplam 40,9 MB tutuyor.
- Tepsi koşusunda (T1) aynı tablo: 55,7 MB, bunun 47,1 MB'ı `byte[]`.
- Yani koşudan sonra motorun elinde kalan, büyük ölçüde okuma ve yazma tamponları.
- Uyku bunların hepsini bırakıyor; ayrı bir düzeltme gerekmiyor.

## 4. Uyanma

| | Süre |
|---|---|
| F5 → yeni motor süreci doğdu (uykudan sonra ilk Build, W2) | **96 ms** |
| F5 → koşu klasörü, uykudan sonra (W2) | 5,9 s |
| F5 → koşu klasörü, motor uyanık (W3) | 4,6 s |
| Uyanmanın ek maliyeti | **~1,3 s** |

- İki ölçümde de 195 projenin hepsi güncel olduğu için koşu yalnız karar adımıydı.
- Uyanma sırasında şerit `▸ Starting the engine…` yazıyor.
- **UI donması:** uyanırken en uzun 41,4 ms, uyanık Build başlarken 48,7 ms. Uyanma pencereyi ayrıca dondurmuyor; bu, son incelemenin M-2 sorusunun cevabı.

## 5. Tepsi yolu (`mem-tray-run3.ps1`, aynı gün, aynı ekran)

| An | Dalga 2 | **Dalga 3** |
|---|---|---|
| T1 koşu tepside bitti, +60 s | 220 + 67 = 287 | 219 + 68 = 287 |
| T2 pencere yeniden gösterildi | 238 + 72 = 310 | 240 + 72 = 312 |
| **T1b tepside motor uyudu** | (motor kalır) | **230 + 0 = 230** |

- Motor tepside koşu bitiminden 5 dakika sonra uyudu: koşu sonu 13:19:26, motor 13:24:27'de yoktu.
- Tepside boşta toplam **~310 → 230 MB** oldu.

## 6. UI donması

| Sonda | Dalga 2 (aynı gün) | **Dalga 3** | Eşik |
|---|---|---|---|
| `E-postrun-trim-15s` (koşu bitişi ve trim) | 22,1 ms | **2,6 ms** | ≤ 300 ms (spec §1) |
| `W-window-return-20s` (pencereye dönüş) | 1,4 ms | 1,5 ms | — |
| Boşta (S0–S4, T1–T1b) | ≤ 1,8 ms | ≤ 2,1 ms | — |

- Dalga 2 raporundaki pencereye dönüş donması (~70 ms) bugün iki programda da yok. Ortamdan geldiği o raporda gösterilmişti.

## 7. Koşu süresi notu (dalga dışı)

| | Dalga 2 (aynı gün) | Dalga 3 |
|---|---|---|
| Görünür Rebuild | 135 s | 188 s |
| Tepsi Rebuild | 134 s | 143 s |

- Dalga 3'ün görünür Rebuild'i sahip henüz makinedeyken (12:28) koştu.
- İlk tepsi koşusu da sahip makinedeyken koştu ve 171 s sürdü.
- Dalga 3 derleme yoluna dokunmuyor. Fark makinenin o anki yükü.

## 8. Diğer kanıtlar

- **Tam süit** (`Category!=Acceptance`, Debug, 2 derleme düğümü, 2 test iş parçacığı, `437f249b` kodu): **5 521 geçti / 1 düştü / 11 atlandı.**
  - Düşen test `HeadWatcherTests.In_a_reftable_repository_a_head_change_before_the_first_commit_fires_nothing`. Hata iletisi "the HEAD change never closed a window — the test saw nothing to judge"; testin kendi ön koşulu tutmamış.
  - Dalga 3 git koduna hiç dokunmadı. Test tek başına 3 kez üst üste 13/13 geçti.
  - Düşüş 17 dakikalık paralel koşunun yükünden.
- **İncelemeler:** üç parti (motor kararları, konsol kararları, hijyen) ve tek bir son toparlama geçişi incelemeden geçti. Her partinin son incelemesinde kritik 0, önemli 0.
- **Dokümanlar:**
  - ARCHITECTURE'a yeni §4.7 (motor uykusu ve uyanma) eklendi ve yaklaşık 25 bölüm güncellendi.
  - README güncellendi.
  - Doküman incelemesinin iki önemli bulgusu (iki yanlış iddia) düzeltildi.
- Commit mesajlarında attribution satırı yok. CHANGELOG ve Version'a bu işte dokunulmadı.

## 9. Ölçüm koşulları

- Uygulamalar `bin\Release`'ten çalıştı, `dotnet build … -c Release -m:2` ile derlendi.
- Kurulu uygulama kapalıydı. Visual Studio Code ve boştaki derleme düğümleri açıktı; sahip gereksiz uygulamaları kapattı.
- Masaüstü kilitli değildi; `keepawake` ekranı ve sistemi uyanık tuttu.
- `StopEngineWhenIdle` açıktı; kullanıcının dosyasında anahtar yok, kataloğun varsayılanı geçerli.
- Sıra:
  1. 12:00 ilk koşu, harici monitör açık. Görünür kısım başarısız oldu: F6, açılış Sync'i sürerken basıldı ve düştü.
  2. 12:27 görünür pencere tekrar ölçüldü; F6 30 s arayla 3 kez denenecek şekilde düzeltilmişti.
  3. 12:46 Dalga 2 görünür ölçümü.
  4. 12:59 Dalga 2 tepsi ölçümü.
  5. 13:07 ABAB açılış ölçümü.
  6. 13:15 Dalga 3 tepsi ölçümü tekrar.
- Ölçüm betikleri Dalga 3 için değişti; yedekler `pre-wave3/` altında.
  - Motor süreci üst süreç kimliğinden bulunuyor: uykudan sonra eski kimlik başka bir sürece verilebilir.
  - S4, W2/W3 ve T1b adımları eklendi.
  - T1'de motor yığını alınıyor.
  - Uykusuz bir program için taban kipi var (`BO_MEASURE_BASELINE=1`).

## 10. Açık konular

Hiçbiri merge'ü engellemez; karar sahibin.

1. **Harici 4K monitör açıkken App ~+120 MB yerel bellek tutuyor.** Bunlar pencerenin tam ekran çizim tamponları. Bu dalganın konusu değil; ileride WPF çizim yüzeyleriyle ayrı bir iş olabilir.
2. **Sahibin takip listesi** (T22 planı §5):
   - proje sayfasında logun iki kopyası;
   - 8 yerde kopyalanmış yol normalleştirme;
   - motorun ortak komut sınırının olmaması;
   - testlerde yaklaşık 35 görünüm modeli fabrikası kopyası;
   - beş ayarın elle sayıldığı yedi yer (tasarım kararı);
   - motor çıktı kanalı kapalıyken hata fırlatabilen birkaç tanı satırı;
   - motor öldükten sonra About'ta ölü sürecin PID'inin kalması.
3. **Bellek göstergesi:** sahip, RAM/CPU göstergesini sağ alttaki performans hapında minimal istiyor; aday ölçüler son özette.
