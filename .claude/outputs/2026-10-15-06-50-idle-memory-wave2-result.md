# Boşta bellek azaltma — Dalga 2 sonuç raporu

**Tarih:** 2026-10-15 04:17–04:59 · **Ölçülen kod:** `fix/idle-memory-wave2` `1452261d` (develop `a51203fe` + Dalga 2) · **Taban:**
Dalga 1 sonuç raporu `.claude/outputs/2026-10-14-19-37-idle-memory-wave1-result.md` ve ilk ölçüm raporu
`.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md` · **Ham veri:** `.claude/temp/mem-2026-10-13/wave2-visible/`, `wave2-tray/`,
`wave2-visible-full/` · **Spec:** `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md` §1, §6.3, §6.5, §9.

## 1. Tek cümle

Hedef tuttu. OSYS Rebuild'i görünür pencerede bittikten 60 s sonra App ve Supervisor'ın toplam private belleği **349 MB'den
276 MB'ye** indi (hedef ≤ 435; Dalga 3'ün ≤ 390 hedefinin de altında; ilk taban 845). Kompakt konsol kapalıyken (Full) aynı ölçüm
**302 MB** (hedef ≤ 485).

## 2. Görünür pencere (spec §9, `mem-after-build.ps1`)

| An | Dalga 1 App WS / Private | **Dalga 2 App WS / Private** | Dalga 2 Full App WS / Private | Dalga 1 Sup Private | **Dalga 2 Sup Private** | Full Sup |
|---|---|---|---|---|---|---|
| S0 taze açılış, 45 s boşta | 271 / 179 | 267 / **177** | 258 / 170 | 43 | 41 | 41 |
| R koşu sürerken | 347 / 247 | 332 / 235 | 356 / 257 | 129 | 138 | 139 |
| **S1 koşu bitti, +60 s** | **417 / 313** | **316 / 213** | 344 / 243 | **36** | **63** | 59 |
| S2 zorlanmış tam GC sonrası | 415 / 309 | 319 / 215 | 348 / 245 | 36 | 63 | 59 |
| S3 +3 dk boşta | 413 / 306 | 323 / 218 | 351 / 249 | 36 | 63 | 59 |

- **S1 toplamı (App + Sup private): 349 → 276 MB** (taban 845; −%67). Full kipte 302 MB.
- **App private S1: 313 → 213 MB (−100 MB).** Canlı yönetilen yığın **95,8 → 35,0 MB** (Full 37,1). Anlatı metni artık diskte
  (`narrative.log`); bellekte son 2 000 satırlık pencere kalıyor.
- **Supervisor S1: 36 → 63 MB.** Canlı yığının 46 MB'ı `byte[]` (3 569 dizi; ≥ 85 KB nesneler 47,7 MB). Dalga 2 motorun koşu yoluna
  dokunmadı (yalnız proje adı yardımcısı). Aynı tutum Dalga 1'in tepsi ölçümünde de vardı (41,6 MB), görünür ölçümünde yoktu: koşudan
  koşuya değişiyor. Dalga 3'te işsiz motor 5 dk sonra kapanacağı için boşta tamamen gider; kaynağı Dalga 3 ölçümünde Supervisor yığın
  dökümüyle (T1) adlandırılacak.
- Kompakt ile Full arasındaki fark App'te 30 MB (213 / 243): kompakt kipte anlatıya proje başına başlık, hatalar, ilk 3 uyarı ve
  özet girer.

### UI donması

| Sonda | Dalga 1 | **Dalga 2** | Full | Eşik |
|---|---|---|---|---|
| `E-postrun-trim-15s` (koşu bitişi + trim) | 50,6 ms | **7,8 ms** | 5,6 ms | ≤ 300 ms (spec §1) |
| `W-window-return-20s` (pencereye dönüş: sessiz Sync + trim) | 9,7 ms | **70,5 ms** (tek olay) | 70,3 ms | — |
| Boşta (S0–S3) | ≤ 1,0 ms | ≤ 1,9 ms | ≤ 1,7 ms | — |

- Koşu sonu trimi küçülen yığınla 7 kat kısaldı.
- **Pencereye dönüş:** her iki kipte tek bir ~70 ms olay. **Kaynağı ortam, Dalga 2 değil:** aynı makinede, koşu yapmadan, pencereye üçer kez dönüldü — Dalga 1 programı (develop `a51203fe`, Release) 71 / 71 / 86 ms, Dalga 2 programı (`1452261d`) 71 / 86 / 87 ms; boşta ikisi de ≤ 1,7 ms. Dalga 1 ölçümünde (2026-10-14 19:24) bu değer 9,7 ms'ydi: makinenin durumu o günden beri değişmiş (aynı saatlerde develop'ta da düşen `StickyLayerHeaderClickTests` gibi, §5). Ham veri `wave1-wreturn/`, `wave2-wreturn/`.

### Heap tip dökümü, S1 (App, ilk 10 satır)

| Dalga 1 | **Dalga 2 (kompakt)** |
|---|---|
| 59,38 MB · 108 107 × `System.String` | 3,99 MB · 26 275 × `EffectiveValueEntry[]` |
| 4,09 MB · 28 179 × `EffectiveValueEntry[]` | **2,80 MB · 27 058 × `System.String`** |
| 2,26 MB · 7 755 × `System.Byte[]` | 1,61 MB · 7 772 × `System.Byte[]` |
| 1,15 MB · 975 × `System.String[]` | 1,11 MB · 17 348 × `System.EventHandler` |
| 1,12 MB · 17 511 × `System.EventHandler` | 0,82 MB · 2 104 × `Grid` |
| 0,83 MB · 34 452 × `System.Double` | 0,79 MB · 32 940 × `System.Double` |
| 0,82 MB · 2 099 × `Grid` | 0,66 MB · 13 831 × `ModifiedValue` |
| 0,72 MB · 15 047 × `ModifiedValue` | 0,66 MB · 8 291 × `DoubleCollection` |
| 0,66 MB · 8 264 × `DoubleCollection` | 0,65 MB · 8 140 × `ResourceReferenceExpression` |
| 0,65 MB · 8 124 × `ResourceReferenceExpression` | 0,56 MB · 1 207 × `TextBlock` |

Okuma: string'ler 59,4 MB'den 2,8 MB'ye indi — anlatının satırları diske taşındı. Geri kalan ilk 10 artık yalnız WPF'in kendi görsel
ağacı (özellik girdileri, ızgaralar, metin blokları).

## 3. Tepsi yolu (spec §9, `mem-tray-run3.ps1`)

| An | Dalga 1 App WS / Private | **Dalga 2 App WS / Private** | Dalga 1 Sup | **Dalga 2 Sup** |
|---|---|---|---|---|
| **T1 koşu tepside bitti, +60 s** | 360 / 258 | **302 / 203** | 60 | **52** |
| **T2 pencere yeniden gösterildi** | 387 / 280 | **344 / 241** | 46 | 54 |

- T1 toplamı **318 → 255 MB** (taban 490). Canlı yığın 79,3 → 34,4 MB.
- Pencereyi göstermenin bedeli +22 → +38 MB (T1 → T2 App private).

## 4. Koşu süresi notu (dalga dışı)

| | Taban | Dalga 1 | Dalga 2 |
|---|---|---|---|
| Görünür Rebuild | 221 s | 529 s | 361 s (Full kip 338 s) |
| Tepsi Rebuild | 280 s | 397 s | 345 s |

Ölçüm gece (04:17) serin makinede yapıldı; Dalga 1'deki 3 katlık yavaşlığın büyük kısmı geri döndü. Dalga 2 derleme yoluna dokunmuyor.

## 5. Diğer kanıtlar

- **Tam süit** (`Category!=Acceptance`, Debug, 2 derleme düğümü, 2 test iş parçacığı, HEAD `1452261d`): 5 173 geçti / 8 düştü / 10
  atlandı. Düşenler: `UpdatePillTests` ×2 (bilinen DPI farkı) ve `StickyLayerHeaderClickTests` ×6 ("basış yakalamayı almadı", "liste
  hiç kaymadı"). **Aynı 6 test develop'un dalga 2'siz kopyasında (`a51203fe`, git archive) da düşüyor**; o kod Dalga 1 kapanışında bu
  testleri geçmişti → ortam (ekran/fare durumu), dalga değil. Son düzeltmelerden sonra süit iki kez daha koştu: `d4b48a49`'te 5 180 geçti / aynı 8 ortam düşüşü; **son HEAD `244bec9c`'te 5 187 geçti / 2 düştü / 10 atlandı** — düşen yalnız `UpdatePillTests` ×2 (bilinen 0,4 px DPI farkı: 22,4 / 22); `StickyLayerHeaderClickTests` bu koşuda 19/19 geçti, düşüşlerinin ortamdan geldiğini doğruluyor.
- **Bellek bütçe testi** (`BO_MEASURE_MEMORY=1`, spool açık, kompakt kapalı): 100 k satırlık sentetik koşudan sonra yönetilen bellek
  büyümesi **1,8–1,9 MB** (bütçe 8 MB; Dalga 1'de spool yokken 26,5 MB). Kompakt açık 100 k uyarılı koşuda anlatı 5 satır.
- **İncelemeler:** üç görev birimi (anlatı diske, ayar, süzgeç + bağlantı) kendi incelemesinden geçti; süzgeç biriminde bir önemli bulgu çıktı (sınıflandırıcının önceliği yüzünden `MSBUILD : error …` ve "Warning As Error" taşıyan NU1605 gibi gerçek hata satırları kompakt kipte gizlenebiliyordu — MSBuild'in kanonik tanı biçimi önce okunacak şekilde düzeltildi; makinedeki 1 514 gerçek logun 528 854 tanı satırında sınıf değişimi 0). Bütün branch opus ile genel incelemeden geçti: kritik 0, önemli 0, küçük 4, düzen 7, not 5; hepsi tek düzeltme dalgasında (20 madde) ve onu izleyen küçük turlarda kapandı. Bu turlarda dalgadan önce de var olan dört konsol kusuru bulunup düzeltildi: gerçek bir Ctrl+Home eski satırları diskten hiç yüklemiyordu; Ctrl+Home, ilk PageUp ve eşiği aşan tekerlek çentiği okuyucuyu dipten ayırmıyordu (`⌄ latest` çıkmıyordu, 5 sn dönüşte de ekranda kalıyordu); ← Back / tepsi dönüşündeki belge takası boşuna bir dilim okuyup kırpıyordu. Hepsi gerçek pencerede, gerçek tuşla önce kırmızı gösterildi.
- Commit mesajlarında attribution satırı yok; CHANGELOG ve Version'a dokunulmadı.

## 6. Ölçüm koşulları

- Uygulama ve Visual Studio kapalı, makine boşta (CPU %13; yalnız VS Code'un boştaki derleme sunucusu). Uygulama `bin\Release`'ten
  (`dotnet build … -c Release -m:2`, 04:17).
- Sıra: görünür (kompakt, varsayılan) → 5 dk soğuma → tepsi → 5 dk soğuma → görünür Full. Full koşusu için kullanıcının
  `ui-state.json`'ına geçici `"CompactConsole": false` yazıldı ve koşudan sonra bayt bayt geri yüklendi (özet eşleşti).
- Ölçülen kod `1452261d`. Sonraki commit'ler (`76f258b1` … `244bec9c`) yalnız konsolun kaydırma/takip kararlarını, testleri ve dokümanları değiştirdi; bellek yoluna dokunmadı.

## 7. Açık konular

Hiçbiri merge'ü engellemez; karar sahibe.

1. **Doküman ↔ kod (dalga öncesinden):** uzun bir proje logu (200 satırdan uzun) sayfası logun başında değil son 200 satırın tepesinde açılıyor; README ve ARCHITECTURE "baştan açılır, ilk hata görünür" diyor.
2. **Doküman ↔ kod (dalga öncesinden):** proje sayfasında "dibe kendin inersen takip geri gelir" yazıyor; kod proje kipinde takibi hiç geri vermiyor (yalnız `⌄ latest` tıklaması).
3. **Davranış değişikliği (onay):** uzun bir proje sayfası tepesinden açılınca `⌄ latest` artık görünüyor (genel kural: "pill dibe uzaklığı izler"; dokümanlar proje sayfası için sessiz). Önceden açılıştan önceki kaydırma konumuna göre bazen görünür bazen gizliydi.
4. **Dalga öncesinden:** konsolda PageUp/PageDown gizli imleci taşıyor; dipteki ilk PageUp bir sayfa yerine 200 satırlık dilimin tepesine sıçrıyor.
5. **Küçük doküman hataları (dalga öncesinden):** §8.3 "log bir uyarı satırıyla açılır" (ilk satır MSBuild komut satırı); §16 "hotkey" (iki kısayol tutuluyor).
6. **Takip önerileri (kapsam):** General ayarlarının varsayılanı ayar başına 5–6 yerde yazılı (Compact console tek kaynağa alındı, ötekiler eski desende); testlerde ~27 özel `NeverTickingBatcher` kopyası.
7. **Supervisor koşu sonrası `byte[]` tutumu** (§2): Dalga 3'ün motor uykusu 5 dk sonra bırakır; kaynağı Dalga 3 ölçümünde Supervisor yığın dökümüyle adlandırılacak.
