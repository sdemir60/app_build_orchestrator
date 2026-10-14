# Boşta bellek azaltma — Dalga 1 sonuç raporu

**Tarih:** 2026-10-14 19:10–19:36 · **Kod:** `fix/idle-memory-wave1` `db3a3cee` (develop `490880ee` + 36 commit) · **Taban:** ölçüm raporu
`.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md` (koşu 1 görünür, koşu 4 tepsi; kod `490880ee`) · **Ham veri:**
`.claude/temp/mem-2026-10-13/wave1-visible/` ve `wave1-tray/` · **Spec:** `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md` §1, §9.

## 1. Tek cümle

Hedef tuttu. 195 projelik OSYS Rebuild'i görünür pencerede bittikten 60 s sonra App ve Supervisor'ın toplam private belleği
**845 MB'den 349 MB'ye** indi (hedef ≤ 500; Dalga 3'ün ≤ 390 hedefinin de altında). Koşu sonrası en uzun UI donması **51 ms**
(hedef ≤ 300 ms).

## 2. Görünür pencere (spec §9, `mem-after-build.ps1`)

| An | Taban App WS / Private (MB) | Dalga 1 App WS / Private (MB) | Taban Sup WS / Private | Dalga 1 Sup WS / Private |
|---|---|---|---|---|
| S0 taze açılış, 45 s boşta | 401 / 313 | 271 / **179** | 120 / 83 | 82 / **43** |
| R koşu sürerken (+30 s) | 607 / 508 | 347 / **247** | 105 / 60 | 162 / 129 |
| **S1 koşu bitti, +60 s boşta** | **898 / 798** | **417 / 313** | **95 / 47** | **86 / 36** |
| S2 zorlanmış tam GC sonrası | 847 / 746 | 415 / 309 | – | 86 / 36 |
| S3 +3 dk boşta | 847 / 746 | 413 / 306 | 96 / 47 | 86 / 36 |

- **S1 toplamı (App + Sup private): 845 → 349 MB (−496 MB, −%59).** S3'te 793 → 342 MB.
- **Canlı yönetilen yığın (S1, HeapTool):** 398,8 MB / 1,37 M nesne → **95,8 MB / 595 k nesne**. Supervisor 31,9 → 21,8 MB.
- **Zorlanmış tam GC artık bir şey kazandırmıyor** (S1 → S2: 313 → 309). Toplama koşu sonunda zaten yapılmış.
- **S0'daki düşüş** (313 → 179, Supervisor 83 → 43): açılış Sync'i de artık bir işlem bitişi. Sync bitince App ve Supervisor
  bir kez toplar. Taban S0'da açılış Sync'inin çöpü ve boş bölgeleri duruyordu.

### UI donması (yeni sondalar)

| Sonda | Ne ölçer | En uzun gecikme | Eşik |
|---|---|---|---|
| `E-postrun-trim-15s` | koşu bitişinden sonraki 15 s: final + 1,5 s pay + trim | **50,6 ms** (bir olay > 16 ms) | ≤ 300 ms (spec §1) |
| `W-window-return-20s` | S3'ten sonra pencereye dönüş: sessiz Sync + trim | **9,7 ms** | açık soru I2 için veri |
| S0 / S1 / S2 / S3 boşta | boştaki pencere | ≤ 1,0 ms | — |

### Heap tip dökümü, S1 (ilk 10 satır)

| Taban (`run1`) | Dalga 1 (`wave1-visible`) |
|---|---|
| 151,65 MB · 355 188 × `System.String` | 59,38 MB · 108 107 × `System.String` |
| 122,32 MB · 57 240 × `Free` | 4,09 MB · 28 179 × `EffectiveValueEntry[]` |
| 64,29 MB · 18 911 × `System.Char[]` | 2,26 MB · 7 755 × `System.Byte[]` |
| 4,49 MB · 33 686 × `EffectiveValueEntry[]` | 1,15 MB · 975 × `System.String[]` |
| 3,97 MB · 82 791 × `ProjectLogEvent` | 1,12 MB · 17 511 × `System.EventHandler` |
| 2,57 MB · 8 853 × `System.Byte[]` | 0,83 MB · 34 452 × `System.Double` |
| 2,38 MB · 74 417 × `NativePointerWrapper<DWRITE_SCRIPT_ANALYSIS>` | 0,82 MB · 2 099 × `Grid` |
| 2,38 MB · 74 417 × `NativeIUnknownWrapper<IDWriteNumberSubstitution>` | 0,72 MB · 15 047 × `ModifiedValue` |
| 1,95 MB · 25 889 × `System.Int16[]` | 0,66 MB · 8 264 × `DoubleCollection` |
| 1,64 MB · 10 773 × `GlyphRun` | 0,65 MB · 8 124 × `ResourceReferenceExpression` |

Okuma:
- Anlatının ikinci kopyası (`Char[]` 64 MB: VM'deki `StringBuilder`) ve görünüm tarafındaki satır listesi kalktı. 108 k string, store'un
  tek kopyası.
- LOH boşlukları (`Free` 122 MB) ve bekleyen 82 791 log olayı kayboldu.
- DirectWrite sarmalayıcıları (her biri 74 k) artık ilk 10'da değil. Görünüm 200 satırlık dilimine döndüğü için metin
  biçimleme artıkları da gitmiş.

## 3. Tepsi yolu (spec §9, `mem-tray-run3.ps1`)

| An | Taban App WS / Private | Dalga 1 App WS / Private | Taban Sup Private | Dalga 1 Sup Private |
|---|---|---|---|---|
| **T1 koşu tepside bitti, +60 s** | 541 / 444 | **360 / 258** | 46 | **60** |
| **T2 pencere yeniden gösterildi** | 670 / 571 | **387 / 280** | 49 | 46 |

- T1 toplamı 490 → 318 MB. Uygulamanın GC committed'ı 84,8 → 78,5 MB; parçalanma ~0 (LOH 3,8 MB).
- **Pencereyi göstermenin bedeli** +127 MB'den **+22 MB**'ye indi. Belge artık tam metnin `ToString()`'iyle değil, store'un son
  dilimiyle kuruluyor.
- **Gözlem: Supervisor T1'de 60 MB** (taban 46). Trim sonrası canlı LOH 41,6 MB; görünür koşudaki aynı ölçüm 18,7 MB, private 36
  MB. Koşudan koşuya değişen, koşu sonrasında canlı kalan büyük bir yapı; Dalga 1 motorun koşu yoluna dokunmadı (yalnız sondaki
  trim ve proje logu istek kimliği). Dalga 3'te işsiz motor kapanacağı için bu bellek boşta tamamen gider. Yine de kaynağı
  Dalga 3'ün ön taramasında Supervisor yığın dökümüyle belirlenecek.

## 4. Koşu süresi notu (dalga dışı)

| | Taban | Dalga 1 |
|---|---|---|
| Görünür Rebuild süresi | 221 s | 529 s |
| Tepsi Rebuild süresi | 280 s | 397 s |
| Proje başına MSBuild sürelerinin toplamı (görünür, 195 proje) | 557 s | 1 727 s (×3,1) |
| Koşu sırasında App / Supervisor CPU | 2 765 / 332 Mcycles/s | 1 092 / 173 Mcycles/s |
| Makine CPU (tüm çekirdekler) | %67 | %96 |

Yavaşlık MSBuild çocuk süreçlerinin kendisinde: her proje kendi içinde 3 kata yakın uzun sürmüş. Uygulama ve motor ise koşu
boyunca daha az CPU harcamış. Dalga 1 derleme yoluna (plan, argüman, işçi sayısı, MSBuild süreçleri) dokunmuyor.

Ölçüm, makinenin sabah 09:09'da ısınıp kapanmasından birkaç saat sonra yapıldı. Ortam: şarj takılı, Dengeli güç planı, bütün
çekirdekler %96. En olası açıklama ısıl kısılma ya da güncelleme sonrası arka plan işleri. Makine serinken derleme süresinin
ayrıca ölçülmesi önerilir.

## 5. Diğer kanıtlar

- **Tam süit** (`Category!=Acceptance`, HEAD `c6d366c2`): 5071 geçti / 0 düştü / 10 atlandı. Taban 4915 / 0 / 9; fazladan atlanan, ortam
  kapılı bellek ölçüm testi. Süit ısınma yüzünden 2 derleme düğümü ve 2 test iş parçacığıyla koşturuldu.
- **Bellek bütçe testi** (`BO_MEASURE_MEMORY=1`, HEAD `db3a3cee`): 100 k satırlık sentetik koşudan sonra yönetilen bellek büyümesi
  26,48 MB, metin 22,93 MB, **oran 1,155** (bütçe 1,2). Konsol bağı kaldırılınca, yani ikinci kopya geri gelince, aynı ölçüm 2,30.
- **İncelemeler:**
  - 10 görev tek tek incelendi.
  - Bütün branch opus ile genel incelemeden geçti: kritik 0, önemli 2. İkisi ve ertelenen 30 madde düzeltildi.
  - Üç düzeltme grubu, son temizlik ve dokümanlar kendi kapsamı dar incelemesinden geçti.
  - Commit mesajlarında attribution satırı yok.

## 6. Ölçüm koşulları ve betik değişiklikleri

- Uygulama ve Visual Studio kapalı, makine boşta (CPU %9). Uygulama `bin\Release`'ten çalıştı: `dotnet build … -c Release -m:2`,
  11:39, Supervisor `supervisor\` alt klasöründen.
- `mem-after-build.ps1` betiğine iki sonda eklendi:
  - `E-postrun-trim-15s`: koşu bitişinden hemen sonra. S1 yine +60 s'de.
  - `W-window-return-20s`: S3'ten sonra.

  S0–S3 tabanla karşılaştırılabilir kaldı.
- Gözle görülen ama ölçülmeyen: koşu 1 failed (taban 2). Kullanıcının OSYS kopyası dünden bu yana değişmiş olabilir; bellek
  ölçümünü etkilemez.

## 7. Açık konular (Dalga 1 merge'ünü engellemez)

- **I2:** Pencereye dönüşteki sessiz Sync de trim istiyor. Ölçülen en uzun donma 9,7 ms; kullanıcının kararı bekleniyor.
- **Görsel kontrol:** Hakkında → Environment sekmesinde bellek satırları PATHS grubunun son satırlarını kaydırmaya itti.
- **Supervisor T1 LOH** (§3) ve **derleme süresi** (§4): Dalga 3 ön taramasında ve makine serinken yeniden bakılacak.
