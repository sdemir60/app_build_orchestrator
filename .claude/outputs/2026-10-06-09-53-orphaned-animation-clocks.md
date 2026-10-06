# Yetim animasyon saatleri — F (tepside koşu sonrası UI 118 M/s) kök nedeni ve düzeltme

**Tarih:** 2026-10-06 · **Taban:** develop `0bd4853` · **Çalışma branch'i:** `fix/orphaned-animation-clocks` · **Kapsam:** Konu 1 (F).
Konu 2 (B4 aktif ön plan) bu raporun sonunda plan olarak durur; ölçüm masaüstü oturumu ister.

## 1. Tek cümle

Pencere görünürken biten bir koşu, durdurulan her sonsuz animasyonun (satır nefesi, dönen halka, şerit süpürmesi, graf beads
yörüngesi) saatini WPF zamanlama ağacında **yetim** bırakıyordu: `BeginAnimation(dp, null)` ve `ApplyAnimationClock(dp, null)`
saati yalnız özellikten söker, durdurmaz; aktif bir `AnimationClock` her karede tik ister; ağaç onu zayıf referansla tutar ve
boşta duran süreçte onu toplayacak GC hiç gelmez. Gizli pencerede her tik kanalı yeniden gönderip UI thread'i ekranın tazeleme
hızında uyandırıyordu. Düzeltme: sonsuz animasyon sahipleri saati elinde tutar ve bırakırken ağaçtan çıkarır
(`DecorativeClock`, `ClockController.Remove`) — `CursorClock`'un zaten uyguladığı kural.

## 2. Kanıt zinciri

1. **Ölçüm (önceki rapor §2):** koşu sürerken gizlenince tepside 13 M/s; koşu görünürken bitip 3 s sonra gizlenince 118 M/s
   (+75 s'de 79). Çekirdek izi: UI thread 15,1 ms periyotlu kümelerde (~66 Hz) uyanıyor, render thread de; dotnet-trace:
   `MediaContext.AnimatedRenderMessageHandler → TimeManager.Tick → ClockGroup.ComputeTreeState`, uygulama çerçevesi yok.
2. **Sonda (`tests/App/ActiveClockInspector.cs`):** dispatcher'ın `MediaContext.TimeManager` ağacındaki kök saatleri reflection ile
   listeler — durum (`Active/Filling/Stopped`), tik isteği, `DesiredFrameRate`, ilerleme ve SAHİP (saatin `CurrentTimeInvalidated`
   dinleyicisi olan WPF `AnimationStorage`'ının hedef nesnesi + özelliği; fırça/dönüşümse `InheritanceContext` üzerinden onu taşıyan
   öğe, görsel ağaçta uygulamanın görünümüne dek yol).
3. **Kırmızı test (`HiddenShellClockTests`):** gerçek `MainWindow`, ekran dışı gösterilen pencere, hareket açık; 12 projelik koşu
   görünürken biter, final sürerken gizlenir, 3 s pompalanır. İlk koşuda listede tek yetim vardı
   (`DoubleAnimation(4000ms forever, fps=30, owner=(no handlers))` = beads yörüngesi, `GraphBeads.CycleMs`); ikinci koşuda
   30 yetim: 17 × `1400ms forever` (BuildingSpinner halkaları: 12 satır glyph'i + şerit chip'leri), 12 × `Automatic forever`
   (satır nefesi), 1 × beads. Aradaki fark GC zamanlaması — yetimlerin ancak toplanınca kaybolduğunun kendisi de kanıt.
4. **WPF kaynağı (PresentationCore 10.0.11, ilspycmd):**
   - `AnimationStorage.BeginAnimation(d, dp, null)` → `ClearAnimations()` → `DetachAnimationClock`: yalnız `CurrentTimeInvalidated`
     ve `RemoveRequested` dinleyicileri çözülür; saat ne durdurulur ne ağaçtan çıkarılır.
   - `AnimationClock.NeedsTicksWhenActive => true` (override): aktif animasyon saati her karede `_nextTickNeededTime = 0` ister;
     `DesiredFrameRate` bunu yalnız 30 fps ızgarasına yuvarlar. `Filling` saat tik istemez — yani sorun yalnız sonsuz (hiç
     dolmayan) saatlerdedir.
   - `ClockGroup.InternalRootChildren` = `List<WeakReference>`: yetim ancak GC'de düşer (`RootCleanChildren`).
   - 15,1 ms periyot: gizli pencerede present sonucu `NOPRESENT` → `MediaContext.CommitChannelAfterNextVSync` →
     `_animationRenderRate = FindNextPrime(refresh + 5)` = 60 Hz ekranda **67 Hz** (14,9 ms) — UI ve render thread'in 66 Hz'lik
     uyanma kümeleri budur; 30 fps'lik saat değeri her kareyi değiştirmese de her tik kanal gönderimi ve bekleme/uyanma maliyeti
     ödetir.
5. **Neden E yolunda (koşu sürerken gizlenince) görünmüyordu:** yetimler orada da doğar (gizlenince `ReapplyMotion`/`StopBreathing`
   sökümleri) ama koşu tepside sürerken olay/konsol trafiği GC üretir ve yetimler koşu bitmeden toplanır; F yolunda yetimler koşu
   bitiminde doğar, 3 s sonra pencere gizlenir ve boşta kalan süreç bir daha GC yapmaz.

## 3. Düzeltme

- **`src/BuildOrchestrator.App/Controls/DecorativeClock.cs` (yeni):** sahibinin elinde tuttuğu dekoratif saat. `Start(timeline)`
  kök saati kurar (zaten kuruluysa dokunmaz — ritim sıfırlanmaz), `Attach(target, dp)` bir hedefe daha uygular (graf: tek saat,
  N yörünge/kenar), `Stop()` her hedeften söker VE `Controller.Remove()` ile ağaçtan çıkarır. Görünürlük/reduced-motion kapıları
  sahipte kalır.
- **Sahipler:** `ProjectRow` (nefes), `BuildingSpinner` (halka), `StickyRibbon` (süpürme; ölçü değişiminde eski süpürme artık
  üstüne yazılıp yetim bırakılmaz, önce çıkarılır), `GraphView` (beads `_beads`, seçim kenarı akışı `_edgeFlow`; `ReleaseBeadsClock`
  / `ReleaseEdgeFlowClock` artık ağaçtan da çıkarır — bu ikisi her Sync'teki `ApplyGraph`'ta ve her seçim değişiminde de çağrıldığı
  için ön planda da yetim biriktiriyordu). `CursorClock` ve tepsi göstergesi zaten `Remove` kullanıyordu, dokunulmadı.
- Sonlu animasyonlardaki `BeginAnimation(null)` çağrıları (şerit, reveal, shake, genişlik) olduğu gibi kaldı: dolan saat tik istemez.

## 4. Testler (kırmızı → yeşil)

| Test | Pinlediği kural |
|---|---|
| `HiddenShellClockTests.Hiding_the_shell_after_a_run_that_ended_visibly_leaves_no_animation_clock_active` | Ölçülen F yolu, kabuk bütününde: gizlendikten 3 s sonra ağaçta `Active` kök saat yok ve `TimeManager.GetNextTickNeeded() < 0`. Gövde `StaThread.RunAsync` ile KENDİ STA thread'inde koşar: Xunit.StaFact thread kiralar/paylaşır (`ThreadRental`), paylaşılan dispatcher'da başka testlerin saatleri iddiaya karışırdı. |
| `HiddenDecorativeClockTests.Hiding_the_window_removes_the_beads_clock_from_the_timing_tree` | Gizlenince beads saati `Active` kalmaz. |
| `…removes_the_edge_flow_clock_from_the_timing_tree` | Gizlenince kenar akışı saati `Active` kalmaz. |
| `…The_spindown_after_the_last_build_removes_the_beads_clock…` | F yolunun sahip düzeyi: son derlemenin spin-down'ı saati ağaçtan çıkarır. |
| `…Clearing_the_selection_removes_the_edge_flow_clock…` | Seçim kalkınca akış saati ağaçtan çıkar. |

Fix öncesi beşi de kırmızı (yetim `Active` kalıyor), sonrası yeşil; motion/graf/satır/şerit/spinner sahiplerinin mevcut sınıfları
(239 test) yeşil. Tam süit: bkz. §6.

## 5. Doküman

ARCHITECTURE §14.5'e "Stopping an infinite animation means removing its clock, not just detaching it" paragrafı (mekanizma, ölçüm,
kural, kanıt testleri); §22 kod haritasına `DecorativeClock` satırı.

## 6. Doğrulama durumu

- Hedeflenen test sınıfları: 238 geçti / 1 atlandı (env kapılı ölçüm).
- Tam süit (`Category!=Acceptance`, Debug, 9,7 dk): 4654 test — 4645 geçti, 8 atlandı, 1 düştü:
  `ConsoleBatcherTests.Lines_arriving_while_the_window_is_open_share_one_flush` (5 s'lik pencere beklemesi paralel yük altında
  doldu; dokunulmayan kod). Sınıf tek başına yeniden koşuldu: 13/13 yeşil. Release derlemesi 0 uyarı.
- **Masaüstü ölçümü BEKLİYOR (kullanıcı başında):** `.claude/temp/perf-2026-10-06/f-verify.ps1 -Dir <klasör>` — önce
  `dotnet build BuildOrchestrator.slnx -c Release`. Sıra: B4 aktif ön plan boşta (20 s) → F6 Rebuild görünürken → bitince +3 s gizle →
  +15 s → tepsi boşta (20 s) → göster → ön plan koşu sonrası boşta (10 s) → F6 Rebuild → +5 s gizle → bitince +15 s → tepsi boşta
  (20 s). Kabul: her iki tepsi okumasında UI ≤ 40 M/s. ~8 dk, iki OSYS Rebuild.

## 7. Konu 2 (B4 aktif ön plan 182 M/s) — plan, karar kullanıcının

Ön planda boşta, etkin pencerede dönen tek sonsuz animasyon imleç saat çiftidir (kırpma + renk turu, 30 fps, iki imleç tek saat);
ölçülen 182'nin dağılımı UI 67 + render 72 + GPU sürücüsü 40 M/s. Açılış Sync'inin süpürme/spinner yetimleri de bu okumaya
girmiş olabilir (düzeltmeden sonra yeniden ölçülmeli — f-verify'ın ilk sondası). Öneri: önce düzeltme sonrası B4'ü oku; hâlâ > 120
ise tablo için iki ek sonda yeter — (a) imleçler durmuşken (pencere arkada: 16,9 zaten ölçüldü = taban) ve (b) reduced-motion
(OS "Animation effects" kapalı = tüm süs kapalı). Aradaki fark tamamen imleç saat çiftidir; seyreltme seçenekleri: girdi yokken N s
sonra imleci durdurmak (Windows'un kendi imleç davranışı), kare hızını düşürmek (`DesiredFrameRate` 30 → 15/20), ya da hedefi
değiştirmek. Kod bu karar gelmeden değişmez.

## 8. Dosyalar

- Yeni: `src/BuildOrchestrator.App/Controls/DecorativeClock.cs`, `tests/BuildOrchestrator.Tests/App/ActiveClockInspector.cs`,
  `tests/BuildOrchestrator.Tests/App/HiddenShellClockTests.cs`, `.claude/temp/perf-2026-10-06/f-verify.ps1`.
- Değişen: `ProjectRow.xaml.cs`, `BuildingSpinner.cs`, `StickyRibbon.xaml.cs`, `GraphView.xaml.cs`, `HiddenDecorativeClockTests.cs`,
  `ARCHITECTURE.md`.
- Decompile notları (scratchpad, geçici): PresentationCore `MediaContext`, `TimeManager`, `Clock`, `ClockGroup`, `AnimationStorage`.
