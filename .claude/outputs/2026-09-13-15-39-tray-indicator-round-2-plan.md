# Tepsi Göstergesi — 2. Tur (Görsel Test Bulguları) TDD Dökümü

> Kullanıcı Task 6 gözle doğrulamasını yaptı ve altı gözlem bildirdi. Bu dosya bulguları kanıtla sıralar,
> kararları/varsayılanları yazar ve işi task'lara böler. Uygulama `fix/tray-indicator-round-2` branch'inde,
> ana projede yürür; **merge edilmez** — kullanıcı yeniden test edecek.

| | |
|---|---|
| **Branch** | `fix/tray-indicator-round-2` (ana proje), taban `main` @ `bb8adfc` |
| **Önceki kayıtlar** | `2026-08-20-12-40-tray-build-animation-plan.md` (K-1…K-14) · `…-results.md` (S-1…S-7) · `2026-09-12-10-31-tray-build-animation-v2-results.md` |
| **Ölçüm** | `tests/App/TrayOverlayMeasurementTests.cs` (`Category=Measurement`, varsayılan süitte YOK) — commit `32b3d03` |
| **Çalışma kısıtı** | Kullanıcı uygulamayı ana projenin **Debug** çıktısından açık tutuyor ve Visual Studio DLL'leri kilitliyor → **her derleme/test `-c Release`** ile. Uygulama bizden BAŞLATILMAZ (single-instance kapısı). |

---

## Bulgular (bloklayıcı → önemli → kozmetik) ve kanıt

| # | Gözlem (kullanıcı) | Kanıt | Karar |
|---|---|---|---|
| B1 | "Arada donmalar oluyor, ekran da animasyon da donuyor" | Ölçüm (Release, boş makine): statik 8.0 % / döngü 11.7 % çekirdek → döngünün kendi payı **+3.7 puan**, +30 kare/sn. `%LOCALAPPDATA%\BuildOrchestrator\settings.json`: `"performance": "FullPower"` = 6 paralel MSBuild, **Normal** öncelik, CPU tavanı **yok** (ARCHITECTURE §11.1). | Kod kusuru DEĞİL. Gösterge yükü görünür kılıyor. **Soru A** kullanıcıya: pencere açıkken de donuyor mu, Balanced'da deneyebilir mi. |
| Ö1 | Sayaç okunmuyor, kaldıralım | — | **Kaldır** (T2). Tasarım README'sinin "sade sürüm" asset'i esas. Testler yeni kuralı pinleyecek şekilde yeniden yazılır. |
| Ö2 | Çok küçük, büyütelim | Tasarımcının önizlemesi (`Build Orchestrator Tray Indicator.dc.html`): `<svg width=375 height=134 viewBox="-30 76 375 134">` — sahneyi logonun yatay BANDINA kırpıyor ve 1:1 gösteriyor. Bizde sahne 430×286 → 144×96 (ölçek 0.335; şerit 7 px). | **Band + 2× ölçek** (T3): sahne bandı 375×134 (inner koordinat −30..345 × 76..210), ölçek 2/3 → pencere 250×89, şerit ~14 px. Tek sabit; kullanıcı büyüt/küçült diyebilir. Kırpma logoyu görev çubuğunun hemen üstüne oturtur. |
| Ö3 | Şeritler sağa giderken "kesilmiş gibi" kayboluyor | Pill Rect'leri, chevron path'i, maske figürü ve tüm KeyTime/KeySpline değerleri asset ile **birebir aynı** (BrandGeometry ↔ BuildOrchestratorIcon.xaml). Çıkışta maske şevronun 2.36 katı hızla kaçıyor; t=2.46'da (şevron solmaya başlarken) maske kenarı şerit ucunun ≥44 birim önünde — matematiksel kesilme YOK. | Geometrik kusur bulunamadı. İki katkı: 7 px şeritte 340 ms solma algılanmıyor; yük altında kare düşmesi hareketi basamaklı gösteriyor. T3 + Soru A sonrası yeniden değerlendir; gerekirse ölçüm testi yüksüz önizleme olarak kullanılır. |
| Ö4 | Bildirime tıklayınca uygulama açılsın | H.NotifyIcon 2.4.1 `TaskbarIcon.TrayBalloonTipClicked` var. | **Ekle** (T4): aynı `RestoreRequested` yolu (ikinci restore yolu yazılmaz — K-2). |
| Ö5 | Bildirim animasyon bitip kaybolduktan SONRA mı geliyor, animasyon yarıda kesiliyor mu | `TrayBuildIndicatorController.StartExit → view.BeginExit → RequestFinish` bayrak; `OnIterationCompleted` turu bitirip callback; `CompleteExitAsync`: HideNow → `ExitBreath` (Duration.Slow) → balloon. Pinler: `Terminal_while_hidden_plays_the_exit_then_notifies`, `The_notification_waits_for_the_exit_breath`, `A_requested_finish_lands_at_the_end_of_the_iteration_not_in_the_middle`. | Doğru çalışıyor; değişiklik yok. Bilinçli bedel: koşu giriş evresinde biterse bildirim ≤3 s bekler. |
| K1 | Bildirim daha estetik olsun: ikon, başlık, içerik, logo | OS balloon → elimizdeki üçlü: ikon / başlık / gövde. `ShowNotification(title, message, icon, customIcon:HICON?, largeIcon, …)`. Bugün: başlık = ürün adı (toast zaten başlıkta ürün adını gösteriyor → tekrar), ikon = OS Info/Error, gövde = şerit satırı. | **T5:** sol tarafta uygulamanın büyük ikonu (`Assets/app-icon.ico` 48 px karesi); başlık = şerit satırının kendi BAŞI (`Completed` / `Stopped` / `Run failed`), gövde = geri kalanı. Metin tek kaynak, yalnız ` — ` ayırıcısından ikiye bölünür; ayırıcı yoksa (engine-died) başlık ürün adı, gövde satırın tamamı. **Soru B:** başarısız koşuda da uygulama ikonu mu, OS hata ikonu mu? Varsayılan: uygulama ikonu (sağlık, başlık/gövde sözcüklerinde). |
| K2 | — (inceleme bulgusu) | K-4: çıkış sırasında pencere geri getirilirse balloon YİNE gelir ("taahhüt korunur"). Kullanıcı şeride bakıyorken ikinci kez söylenir. | **Soru C:** pencere açıksa bildirim yutulsun mu? Cevapsız → dokunulmaz (bilinçli plan kararı). |

Kapsam dışı / dokunulmayan: Supervisor/Core/Contracts, tray menüsü, `X`→tepsi, ilk-kapanış balloon'u, şerit mantığı, `Clean`/`Sync`'in göstergeyi açmaması (bilinçli sınır), 30 fps dekoratif tavanı (§14.5 sözleşmesi; T3 sonrası hareket basamaklı görünürse AYRI karar).

---

## Kurallar (her task için)

- **Kırmızı test önce**; davranış değişen yerde eski pin SİLİNMEZ, yeni kuralı pinleyecek şekilde yeniden
  yazılır ve test doc'una eski iddia + değişme gerekçesi yazılır (`[DEĞİŞEN KURAL]` kalıbı).
- Derleme/test: `dotnet build BuildOrchestrator.slnx -c Release` · `dotnet test … -c Release --filter "Category!=Acceptance"`.
- Kopya YASAK: ölçü/sabit tek yerde (`x:Static` ile XAML'e taşınır), metin tek kaynaktan (şerit satırı),
  ürün adı `AppIdentity.Product`. Kod tarafına ms literali YAZILMAZ. UI metni İngilizce.
- Doküman aynı task'ta: anlatı üslubu, changelog dili yok, bayatlayacak rakam yok.
- Task başına commit; mesaj sonu `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## Task 2 — Sayacı kaldır (Ö1)

**Kırmızı (yeniden yazılan pinler):**
- `TrayBuildIndicatorTests`: `Counter_text_renders_done_over_total`, `Counter_ignores_a_write_with_the_same_value`,
  `Counter_change_runs_a_soft_swap_when_motion_is_on`, `Counter_change_snaps_with_no_clock_under_reduced_motion`,
  `The_counter_slot_never_moves_when_the_digits_change` → KALDIRILIR; yerine
  `The_white_strip_is_the_marks_own_pill_and_carries_no_text` (kontrolde hiç `TextBlock` yok; beyaz şeridin
  `Data`'sı `Brand.Pill.White` kaynağının KENDİSİ — AppMark ile aynı instance) ve
  `The_white_strip_leaves_on_the_plain_assets_distance` (`WhiteShift` X son keyframe `Value == 88` — README:
  60 birimlik şerit için 88; 66/82 sayaç sürümüne aitti). Sınıf doc'una: eski iddia (sayaç okunur, yumuşak
  takas) + gerekçe (kullanıcı: overlay boyutunda okunmuyor; sade asset esas).
- `TrayBuildIndicatorControllerTests.Counter_updates_flow_only_while_the_overlay_is_shown` → kaldır; `FakeView`
  `UpdateCounter` düşer (arayüz daralıyor, derleme kırmızısı = kanıt).
- `TrayIndicatorBinderTests.Counter_properties_track_the_ribbon_inputs`,
  `The_counter_reaches_the_indicator_without_the_window_being_involved` → kaldır; `SpyView` daralır.
- `TrayBuildOverlayWindowTests.The_overlay_forwards_the_counter_to_the_indicator` → kaldır.
- `ReducedMotionCoverageTests` işaretçi yorumu: `Counter_change_snaps_with_no_clock_under_reduced_motion`
  satırı silinir.
- Token/geometri: `Brand.Pill.WhiteCounter` ve `Brush.Brand.CounterText` kaldırılır — bunları sayan/okuyan
  test varsa (`DsResources`, token envanteri) güncellenir.

**Uygulama:** XAML'de beyaz şerit diğerleri gibi düz `Path` (`StripWhiteRect`, `WhiteShift`), `StripWhiteGroup`/
`CounterSlotHost`/`CountText` ve efekti gider, `WhiteShift` çıkış 82→88, başlık yorumu sade asset'i gösterir
(`BuildOrchestratorIcon.xaml`). Code-behind: `SetCounter`/`WriteCounter`/`OnCounterFadedOut`/`PlaceCounterSlot`,
sayaç alanları ve `Counter`/`CounterSlot`/`CounterWrites` erişimcileri gider; `using System.Globalization` gider.
`ITrayBuildIndicatorView.UpdateCounter`, controller `SetCounter`+alanlar+`Show()`'daki itme, binder
`PushCounter`+`Route` dalı, overlay `UpdateCounter`, `MainWindow.LazyOverlayView.UpdateCounter` gider.
`BrandGeometry.xaml`'den `WhiteCounter` + yorumu, `Tokens.xaml`'den `Brush.Brand.CounterText` gider.
`MotionTokens.ResolveFast` KALIR (üç başka tüketici).

**Doküman:** ARCHITECTURE §12.3 (1532–1537: "carrying the same … counter" cümlesi), §14.4 (3024–3026 iki
varyant paragrafı; 3038–3040 sayaç mürekkebi cümlesi), §14.5 (3210–3218: "Two seams" → tek dikiş: nefes),
§22 satırları ("line, counter, phase" → "line, phase"; "loop, counter, static frame" → "loop, static frame");
README 351–356 sayaç cümlesi; `ReleaseNotes.cs` tray maddesi (sayaç ibaresi çıkar — tıklama ibaresi T4'te).

**Kabul:** Release derleme 0 hata; tray test dosyaları + AntiSlop/NoHardcoded*/AppMark/AppResourcesMerge yeşil.

## Task 3 — Band + 2× ölçek (Ö2)

**Kırmızı:**
- `TrayBuildIndicatorTests.The_stage_is_the_designers_band`: realize sonrası Viewbox çocuğu Canvas'ın
  `Width/Height == TrayBuildIndicator.StageWidth/StageHeight` (375/134) ve iç canvas `Canvas.Left == 30`,
  `Canvas.Top == -76` (band orijini inner koordinatta −30,76).
- `TrayBuildOverlayWindowTests.The_overlay_keeps_the_scene_aspect_ratio` → `[DEĞİŞEN KURAL]`: oran artık
  430:286 değil bandın oranı; `The_overlay_is_sized_from_the_stage_and_one_scale`:
  `OverlayWidth == StageWidth * Scale`, `OverlayHeight == StageHeight * Scale`, `Scale == 2.0/3.0`.

**Uygulama:** `TrayBuildIndicator`: `public const double StageWidth = 375, StageHeight = 134;` XAML dış Canvas
`Width="{x:Static controls:TrayBuildIndicator.StageWidth}"` vb.; iç Canvas `Canvas.Left="30" Canvas.Top="-76"`
(eski 72/0 ve 430×286 gider; yorum tasarımcının viewBox'ını anlatır; şevron −24..339.5 ve şeritler 30.5..250.5
bandın içinde, gölge payı üst/alt 7 birim). `TrayBuildOverlayWindow`: `internal const double Scale = 2.0/3.0;
OverlayWidth = TrayBuildIndicator.StageWidth * Scale; OverlayHeight = … * Scale;` (144/96 literalleri gider);
`EdgeMargin` kalır.

**Doküman:** ARCHITECTURE §12.3 ölçü yazmıyor → dokunma; kontrol/overlay doc yorumları band + tek ölçek.

## Task 4 — Balloon tıkı pencereyi getirir (Ö4)

**Kırmızı:** `TrayIndicatorBinderTests` (AppTrayIcon pinlerinin yanı):
`Clicking_a_balloon_takes_the_same_restore_path_as_the_tray_icon` — `Shell/AppTrayIcon.cs` kaynağında
`TrayBalloonTipClicked` aboneliğinin `RestoreRequested`'ı tetiklediği pinlenir (TaskbarIcon headless kurulamaz;
`RunFinishedIcon` pininin gerekçesiyle aynı: kaynak pini).

**Uygulama:** `_icon.TrayBalloonTipClicked += (_, _) => RestoreRequested?.Invoke();` `RestoreRequested` doc'u:
sol tık / çift tık / balloon tıkı. **Doküman:** README 351–356 ("click the notification to bring the window
back"); ARCHITECTURE §12.3 1546–1551 paragrafına bir cümle; `ReleaseNotes.cs` tray maddesine tıklama ibaresi.

## Task 5 — Bildirim anatomisi (K1)

**Kırmızı:**
- `RibbonLine.Head` / `RibbonLine.Detail` (ViewModels/RibbonText.cs — `Healthy`'nin yanında, saf veri):
  `"Completed — 3 failed · …"` → Head `Completed`, Detail `3 failed · …`; `"Engine stopped unexpectedly (exit 1)"`
  → Head `null`, Detail `null`. Ayırıcı `" — "` şeridin kendi yazımıdır; pin: Compose'un Completed / Stopped /
  Run failed / Sync failed satırları Head taşır (sürüklenme kilidi).
- `ITrayRunNotifier.ShowRunFinished(RibbonLine line)` — controller `SetTerminalLine(RibbonLine)` (metin+sağlık
  çifti yerine satırın kendisi; `_terminalText/_terminalHealthy` → `_terminal`). Controller/binder testleri
  sahte notifier'ı buna göre daraltır; `Healthy_flag_reaches_the_notifier` → satırın kendisi ulaşır.
- `AppTrayIcon`: `RunFinishedIcon(bool)` pini → `[DEĞİŞEN KURAL]` (eski: Info/Error; yeni: uygulamanın büyük
  ikonu — Soru B'nin varsayılanı; başarısızlık başlık/gövde sözcüklerinde). Başlık/gövde seçimi saf statik
  yardımcıda (`RunFinishedTitle(RibbonLine) => line.Head ?? AppIdentity.Product`,
  `RunFinishedBody(line) => line.Detail ?? line.Text`) ve pinlenir.

**Uygulama:** `AppTrayIcon`: `System.Drawing.Icon` ile `Assets/app-icon.ico` 48 px karesi bir kez yüklenir
(alan; `Dispose` ile bırakılır), `ShowNotification(title, message, icon: NotificationIcon.None,
customIcon: _largeIcon.Handle, largeIcon: true)`. `IconGeometryTests` 48 karesini zaten pinliyor.

**Doküman:** ARCHITECTURE §12.3 1546–1551: "The icon follows the line's status glyph" → uygulamanın ikonu,
başlık satırın başı, gövde geri kalanı, tek kaynak bölünür; README aynı cümle.

## Task 6 — Kayıt + tam süit + push

`2026-09-13-15-39-tray-indicator-round-2-results.md`: ne yapıldı, Soru A/B/C cevapları (geldiyse), ölçüm
sayıları, süit sonucu. Tam süit `-c Release`, `Category!=Acceptance`. Branch push; **merge YOK**.

**Sıra:** T2 → T3 → T4 → T5 → T6 (T2/T3 aynı XAML'e dokunur, sıralı; T4 küçük, T5 notifier arayüzünü değiştirir).
