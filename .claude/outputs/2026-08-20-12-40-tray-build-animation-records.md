# Tepsi Build Animasyonu — Uygulama Kayıtları

> Bu dosya planın (`…-tray-build-animation-plan.md`) UYGULAMA kaydıdır: ne yapıldı, plandan nerede ve neden
> sapıldı, hangi karar ÖLÇÜLEREK verildi. Planı tekrar anlatmaz.

| | |
|---|---|
| **Branch** | `feat/tray-build-animation` (main'den açıldı, **merge edilmedi**) |
| **Uygulama** | 2026-08-25 → 2026-08-26 |
| **Süit** | tam süit yeşil — 2166 geçti, 1 atlandı (`Category!=Acceptance`) |
| **Açık kalan** | **Task 6 — uçtan uca gözle doğrulama yapılmadı** |

## Commit'ler

| SHA | Task | İçerik |
|---|---|---|
| `ba9eb89` | T1 | `TrayBuildIndicatorController` — görünürlük + bitiş koreografisi (WPF'siz) |
| `f4ad224` | T2 | Marka geometrisi `Resources/BrandGeometry.xaml`'de tek kaynağa taşındı |
| `6d32275` | T2 | Figürlerin kaynak koordinatlarına oturduğunu ölçen pin testi |
| `12faa9c` | T3 | `Controls/TrayBuildIndicator` — sayaçlı marka animasyonu |
| `1b1c1a4` | T4 | `Views/TrayBuildOverlayWindow` — penceresiz, odak çalmayan overlay |
| `016c391` | T5 | Kablolama: VM sinyalleri, balloon, `MainWindow` kurulumu |
| `6b9f6ef` | T7 | Dokümanlar |

---

## Plandan sapmalar ve gerekçeleri

### S-1. K-9 — motion guard'ı hakkında plan YANLIŞTI (kullanıcı onayıyla çözüldü)

Plan: *"Motion guard'ı kod-tarafı ms literal'lerini tarar; storyboard XAML'de kaldığı sürece ihlal yoktur."*

Gerçek: `NoHardcodedMotionTests.No_xaml_declares_a_literal_animation_time_instead_of_a_duration_token`
App'teki **her** XAML'ı tarar ve `Duration=`/`KeyTime=` literallerini yasaklar; istisna listesi **yoktu**.
Verbatim 3 sn'lik çizelge guard'ı kırmızıya çekiyordu.

Kullanıcıya iki seçenek sunuldu (XAML + dar istisna / kod tarafında adlandırılmış keyframe tablosu); kullanıcı
"en doğru olanı sen uygula" dedi. **Seçilen:** K-9'un kararı korundu (çizelge XAML'de verbatim), guard'a
YALNIZ `Controls/TrayBuildIndicator.xaml` için gerekçeli istisna + `The_exempt_file_really_carries_a_bespoke_timeline`
(istisna ölü satıra dönerse kırmızı verir) eklendi. Kalıp, `AntiSlopTests`'in ürün markasının gradyanına
verdiği muafiyetin birebir ikizidir. Kod tarafı yasağı bu dosya için de aynen geçerli — kodda tek ms
literali yok.

### S-2. K-5 — şerit satırı GÖRÜNÜMDE yaşıyordu, VM'de değil

Plan "VM'in o anki ribbon `Text`'i" diyordu; `RibbonText.Compose` gerçekte `Views/StickyRibbon.xaml.cs:225`'te
çağrılıyordu ve VM'de böyle bir property yoktu. İfade `RunViewModel.RibbonLine`'a taşındı, `StickyRibbon` onu
tüketiyor. `TrayIndicatorBinderTests.The_ribbon_line_is_composed_in_exactly_one_place` tek çağrı yerini pinler.
`RibbonLine.Healthy` (glyph != `"failed"`) record struct'ın kendi üyesi oldu — sağlık kuralının ikinci bir
tanımı yok.

### S-3. K-12 — kablolama `OnSourceInitialized`'da BIRAKILAMAZDI

K-12 kablajı `MainWindow.OnSourceInitialized`'a koyuyor. O metot gerçek bir tepsi ikonu kurup global kısayol
kaydettiği için süit onu **bilerek hiç çalıştırmaz** (`MainWindowRealizeTests` sınıf özeti) — yani kablaj orada
kalsaydı tek bir test göremezdi. Mantık `Services/TrayIndicatorBinder.cs`'e çıkarıldı; `MainWindow` yalnız üç
parçayı birbirine bağlıyor. Testler ne pencere ne HWND istiyor.

### S-4. Binder'da property listesi ÖLÇÜLEREK kaldırıldı

İlk hâli şeridin dinlediği on property'yi tek tek sayıyordu. `Counters` dalı çıkarıldığında **hiçbir test
kırılmadı** (uçuşta proje kalan Stopped senaryosu dahil denendi) — yani dalların bir kısmı kanıtsızdı. Daha
kötüsü liste şeridin listesinin kopyasıydı: şerit yeni bir girdi kazandığında burası sessizce eski kalırdı.
Satır zaten o property'lerin bileşimi olduğu için artık **her sinyalde** yeniden okunuyor. Maliyet bir
`Compose` çağrısıdır ve ekrandaki şerit zaten her `ElapsedMs` tick'inde aynı işi yapıyor.

### S-5. K-7 — beyaz pill'in İKİ varyantı var, koordinat uzayı açıldı

Plan beş pill + chevron'un paylaşılacağını söylüyor; gerçekte beyaz şerit `AppMark`'ta 60, sayaçlı asset'te 66
birim (deliverable v1.3 üç haneli sayaç için genişletmiş, çıkış mesafesini 88→82 yeniden hesaplamış). İkisi de
`BrandGeometry.xaml`'de: `Brand.Pill.White` (marka orantısı) ve `Brand.Pill.WhiteCounter` (gösterge).
Tek-kaynak guard'ı bozulmuyor.

Ayrıca `AppMark`, kaynak SVG'nin `translate(5.5 0)` grup dönüşümünü kendi tuval kaymasına KATLAMIŞTI; asset
katlamamıştı. Ortak kaynak için katlama açıldı (X değerleri +5.5), `AppMark`'ın iç tuvali −41 → −46.5 çekildi.
Çizim birebir aynı — ama yapısal testlerin hiçbiri bunu göremediği için
`The_mark_lands_every_figure_at_its_source_coordinates` eklendi: çizilen kutuyu ölçer.

### S-6. K-7 — süpürme kaplaması chevron'un `Clone()`'u DEĞİL

K-7 "sweep kaplaması chevron geometrisinin per-instance `Clone()`'u üzerine kurulur" diyor. Asset'in kırpma
figürü chevron'un kendisi değil, **sağ kenarı chevron siluetini izleyen geniş bir maskedir** (`M -600 -30 …`)
ve yalnız bu animasyona aittir. Kendi inline `TranslateTransform`'unu taşıdığı için paylaşılan geometri hiç
mutasyona uğramıyor → `Clone()` gerekmiyor. Figür kontrolde inline kaldı (tek kullanım, kopya yok).

### S-7. `AppTrayIcon` metodu arayüzün adını taşıyor

Plan `ShowRunFinishedNotification` diyordu; `ITrayRunNotifier.ShowRunFinished` olarak uygulandı — aksi halde
sınıfın içine tek satırlık bir adaptör yazmak gerekirdi. İkon seçimi (`healthy ? Info : Error`) ayrı ve saf bir
`RunFinishedIcon` metoduna çıkarıldı: `TaskbarIcon` headless süitte kurulamıyor, kural böylece gerçek bir tepsi
ikonu olmadan sınanıyor (planın "sarılamıyorsa çağıran koda kur" maddesinin karşılığı).

---

## Ölçülerek bulunanlar (tahmin değil)

### Ö-1. `Storyboard.Stop()` saati SÖKMÜYOR — ve kendisi `Completed` tetikliyor

Probe sonucu:

```
Begin sonrasi : chevronAnim=True  state=Stopped
Stop sonrasi  : chevronAnim=True  state=Stopped      ← animasyonlar hâlâ bağlı
Remove sonrasi: chevronAnim=True  state=uygulanmamis ← storyboard çözüldü
pump sonrasi  : chevronAnim=False                    ← bayrak bir sonraki tick'te temizlendi
```

İki sonuç: (a) sökme `Remove()` ile yapılıyor, (b) `Stop()`'un tetiklediği `Completed` döngüyü yeniden
başlatıyordu — `_running` bayrağı o yeniden-giriş kapısı. Ayrıca testler `HasAnimatedProperties`'i okumadan
önce koşula bağlı pompalıyor (`AssertClockTornDown`), yoksa doğru sökme bile true görünüyor.

### Ö-2. `Path` geometriyi kendi koordinatlarında çiziyor

`Rectangle` + `Canvas.Left` yerine `Path` + mutlak geometri kullanmak yerleşimi kaydırmıyor: öğe iç tuvalin
(0,0)'ında duruyor, mutlak konum = tuval kayması + geometri koordinatı. Ölçüldü (pill (27,5), chevron sol ucu
98.77) — eski yerleşimle birebir aynı.

### Ö-3. `ElapsedMs` fazdan ÖNCE kesinleşiyor

`OnRunCompleted` önce `ElapsedMs = e.DurationMs` yazıp sonra `Phase`'i çeviriyor; `Counters` tazelemesi
(`RefreshRunSurface`) fazdan SONRA geliyor. Bildirim çıkış evresinin sonuna ertelendiği için metin o ana kadar
tamamlanıyor — bu yüzden metin faz anında YAKALANMIYOR, balloon anında OKUNUYOR.

---

## Bulunan ve düzeltilen gerçek kusur

**`StopNow()` bekleyen bitiş taahhüdünü düşürüyordu.** Çıkış evresi koşarken kullanıcı pencereyi geri
getirirse controller `HideNow()` çağırıyor; gerçek kontrolde bu saati söküyor ve `Completed` bir daha
gelmiyordu — yani koşu **sessizce bildirimsiz** kapanıyordu. Saf controller testi bunu göremez (orada view
sahtedir ve callback'i kendi elinde tutar); `TrayBuildOverlayWindowTests` yazılırken çıktı.
Pin: `Stopping_while_a_finish_is_pending_still_reports_it` + `Stopping_reports_a_pending_finish_only_once`.

---

## Mevcut koda dokunulan yerler (plan dışı ama gerekli)

| Dosya | Ne | Neden |
|---|---|---|
| `Controls/MotionTokens.cs` | `DecorativeFrameRate` sabiti + `ResolveSlow` | Aynı `30` beş tipte ayrı tanımlıydı (altıncı kopya eklenmeyecekti); `Duration.Slow` çözümü iki tüketiciye açıldı |
| `Controls/BuildingSpinner.cs`, `StatusGlyph.cs`, `Graph/GraphView.xaml.cs`, `Views/ProjectRow.xaml.cs`, `Views/StickyRibbon.xaml.cs` | Kopya sabitler silindi | Tek kaynağa bağlandı |
| `Views/StickyRibbon.xaml.cs` | `Compose` çağrısı `_vm.RibbonLine`'a döndü | S-2 |
| `Shell/Win32.cs` | `GWL_EXSTYLE`, `WS_EX_*`, `OverlayExStyle`, `Get/SetWindowLong` | Overlay ex-style'ı |
| `Resources/Tokens.xaml` | `Brush.Brand.CounterText` | Sayaç mürekkebi (metin rampası açık şerit için ayarlı değil) |
| `App.xaml` | `BrandGeometry.xaml` merge zincirine (Icons ile Controls arasına) | Token'lardan sonra, kontrol kütüphanesinden önce |

## Güncellenen guard'lar (hepsi gerekçeli ve DAR)

| Guard | Değişiklik |
|---|---|
| `AppMarkTests` geometri tek-kaynak | Taşıyıcı dosya `AppMark.xaml` → `Resources/BrandGeometry.xaml`; imza `M151 83` → `M156.5 83` |
| `AppResourcesMergeTests` | Dört sözlük → beş |
| `AntiSlopTests` gradient muafiyeti | `AppMark.xaml` → `BrandGeometry.xaml` (sayıca büyümedi, taşındı) |
| `AntiSlopTests` gölge allowlist'i | `Controls/TrayBuildIndicator.xaml` eklendi (tanım gereği floating overlay) |
| `NoHardcodedMotionTests` | XAML süre yasağına tek dosyalık istisna + bayatlama testi (S-1) |
| `NoHardcodedColorTests` | Win32 stil bitleri (`00000080`/`08000000`/`00000020`) DEĞER bazında izinli; izinsiz bir Win32 sabitinin YİNE yakalandığı ayrıca pinlendi |
| `NoSleepPollTests` | `MainWindow.xaml.cs` = 1 (nefesin üretim varsayılanı; dikiş enjekte edilebilir) |
| `ReducedMotionCoverageTests` | Test DEĞİL, işaretçi yorumu — kapsam üç ayrı sınıfta, dördüncü kopya yazılmadı |

## Mutasyon kanıtları

İlk koşuda yeşil çıkan test kümeleri ayırt edicilik için mutasyona uğratıldı ve hepsi kırmızı verdi:

- **T4:** tıkla-aç kablosu koparıldı · `Cursor="Hand"` kaldırıldı · yerleşim çalışma alanı yerine ekran
  boyutundan · `WS_EX_TRANSPARENT` eklendi · bitiş taahhüdü düşürüldü → 5/5 yakalandı.
- **T5:** sayaç çifti ters çevrildi · `Healthy` hep true · metin yalnız faz anında itildi → 3'ü de yakalandı
  (üçüncüsü hiçbir testi kırmadı → S-4'e yol açtı).

---

## Açık kalan — Task 6

Uygulama başlatıldı, senaryo listesi kullanıcıya verildi ama **gözle doğrulama henüz yapılmadı**. Merge'den
ÖNCE koşulması gereken senaryolar merge promptunda listelidir. Doğrulanmamış olanlar özellikle:

- tıkla-aç ve şeffaf alanın geçirgenliği (per-pixel hit-test gerçek HWND'de sınanabilir),
- bitiş koreografisinin gözle görünen ritmi (çıkış evresi → kaybolma → nefes → balloon),
- reduced-motion yolunda statik kare + canlı sayaç,
- sayaç rakam geçişinin yumuşaklığı ve şeridin oynamaması.
