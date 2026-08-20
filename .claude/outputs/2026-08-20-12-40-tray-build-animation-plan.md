# Tray Build Animasyonu — Uygulama Planı

> Bu plan başka bir makinede (Fable) hazırlandı; uygulamayı Opus güncel koda göre yapacak.
> **Tüm satır numaraları YAKLAŞIKTIR** — kod değişmiş olabilir. Dosya adları, kalıplar ve kararlar (K-1…K-13)
> bağlayıcıdır; satır numaraları yalnız arama ipucudur, uygulamadan önce tazelenir.

Hedef: uygulama tepsideyken (ana pencere gizli) bir derleme koşuyorsa, ekranın **sağ alt köşesinde penceresiz /
arka plansız / tıklama-geçirgen** bir logo animasyonu döner (Claude Design deliverable'ı, sayaçlı sürüm).
Derleme bitince animasyon **mevcut döngünün çıkış evresini tamamlar** — parçalar sağa süzülüp son şeridin yok
olduğu karede overlay kapanır — ve **tam o anda** bir **OS balloon bildirimi** sonucu söyler (tamamlandı /
kaç hata / durduruldu). Beyaz şeritteki sayaç (`139/248`) koşu ilerledikçe canlı güncellenir.

## Tasarım kaynağı (bağlayıcı deliverable)

| Dosya | Rol |
|---|---|
| `.claude/outputs/2026-08-05-05-06-logo-animation-v1.3.0/BuildOrchestratorIconCounter.xaml` | **Esas alınacak asset** — sayaçlı sürüm (v1.3). Storyboard zaman çizelgesi ve parça adları (`ChevronShift`, `SweepShift`, `AmberShift`, `WhiteShift`, `TopDarkShift`, `SilverShift`, `MidDarkShift`, `CountText`) buradan taşınır. |
| `.claude/outputs/2026-08-05-05-06-logo-animation-v1.3.0/README.md` | Zamanlama tablosu + **entegrasyon notları** (aşağıya kritik olanları aldım ama Opus dosyanın tamamını OKUMALI). |
| `BuildOrchestratorIcon.xaml` (aynı klasör) | Sade sürüm — **kullanılmaz** (sayaç isteniyor); yalnız referans. |
| `uploads/build_orchestrator_icon_ds_amber.svg` | Statik kaynak ikon — yalnız referans. |

README'den bağlayıcı entegrasyon kuralları:

- **`ChevronShift.X` ile `SweepShift.X` birebir aynı keyframe + KeySpline değerlerini taşımalı** — "şevron
  şeritleri siliyor/seriyor" etkisi tamamen bu senkrona bağlı; biri değişirse diğeri de değişir.
- Sahne **430 × 286**, zemin şeffaf, hiçbir yerde kırpma sınırı yok; `Viewbox` ölçekler.
- Şerit gölgeleri kırpmadan ÖNCE hesaplanmalı: Clip ve Effect **ayrı Canvas'larda** durur (aynı elemana
  konursa süpürme kenarında gölge çizgisi oluşur).
- Döngü 3.000 s, **boş kare yok**: son şerit 3.000'de kaybolur, döngü tam o anda başa döner (v1.2 kararı).
- Gölge opaklığı asset'te 0.30 — masaüstü zemini bilinmediği için görünürlüğün sigortasıdır, korunur.
- Sayaç: `CountText.Text = $"{done}/{total}"`; beyaz şerit 3 haneli sayılar için 66 birime genişletilmiş,
  çıkış mesafesi 82'ye göre yeniden hesaplanmış (sayaçlı dosyada hazır).

## Ürün kapsamı

1. **Overlay ne zaman görünür:** ana pencere gizli (tepside) **ve** faz `Starting | Running | Stopping` iken.
   İki yönlü: run koşarken `X` ile tepsiye inilirse overlay belirir; tepsideyken pencere geri getirilirse
   overlay animasyonsuz anında gizlenir. `Syncing` KAPSAM DIŞI (yalnız derleme koşuları).
2. **Bitiş koreografisi:** faz bu kümeden her çıkışta (Done / Stopped / runFailed→resting / engine died)
   overlay yeni döngü BAŞLATMAZ; içindeki döngü doğal bitişine (çıkış evresi, 3.000 s karesi) koşar, pencere
   kapanır, **sonra** OS balloon gösterilir. Balloon metni = ribbon'un o anki terminal satırı (tek kaynak,
   K-5): `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s` / `Stopped — …` / `Run failed — …` /
   engine-died metni.
3. **Balloon yalnız tepsideyken:** pencere görünürken biten koşu balloon üretmez — ribbon zaten oradadır
   (mevcut davranış değişmez).
4. **Tıkla-aç:** çizili logo piksellerine (şeritler/şevron/sayaç) sol tık ana pencereyi geri getirir —
   tray ikonuna tıklamakla aynı davranış. Logonun ETRAFINDAKİ şeffaf alan tıklamayı altındaki pencereye
   geçirmeye devam eder (K-2). Pencere geri gelince overlay zaten görünürlük kuralıyla anında gizlenir.
5. **Reduced motion:** OS sinyali kapalıysa (animasyonlar kısıtlı) döngü hiç başlatılmaz — statik işaret +
   canlı sayaç gösterilir; bitişte animasyonsuz gizlenir; balloon aynen gelir.
6. **Gelecek (bu işte YAPILMAZ, mimari hazır olur):** tepsideyken global kısayolla arka planda build
   başlatmak. Overlay faz-güdümlü olduğu için o gün SIFIR ek işle çalışacak — plana yalnız bu cümle doc
   notu olarak girer, kısayol işi yapılmaz.

**Dokunulmayanlar:** tray menüsü (Stop/Exit), `X`→tepsi davranışı, ilk-kapanış balloon'u, ribbon metin
mantığı, ana pencere kabuğu, engine/IPC yüzeyi (Supervisor tarafında SIFIR değişiklik — bu iş tamamen App).

## 0. Bağlayıcı kararlar (K-1…K-13)

**K-1. Overlay ayrı bir top-level `Window`'dur; ana pencereye `Popup`/`Adorner` DEĞİL.**
Ana pencere `Hide()` edilmişken görünür kalması gereken tek yüzey olduğundan kendi HWND'i şarttır.
`Views/TrayBuildOverlayWindow.xaml`: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`,
`ShowInTaskbar=False`, `Topmost=True`, `ShowActivated=False`, `Focusable=False`, `ResizeMode=NoResize`,
`SizeToContent=Manual`. İçerik = `Controls/TrayBuildIndicator` (K-7).

**K-2. Logo pikselleri tıklanabilir (tıkla-aç), şeffaf alan geçirgen; odak çalmaz + Alt-Tab'da görünmez.**
`OnSourceInitialized`'da HWND'e `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` eklenir (`GetWindowLong/
SetWindowLong GWL_EXSTYLE` — `Shell/Win32.cs`'e yoksa eklenir; P/Invoke mevcut kalıpla).
`WS_EX_TRANSPARENT` BİLEREK EKLENMEZ: `AllowsTransparency` pencereyi layered yapar ve OS per-pixel
hit-test uygular — **alpha=0 pikseller tıklamayı alttaki pencereye kendiliğinden geçirir**, yalnız çizili
logo pikselleri tıklama alır. Yani ek bir hit-alanı hack'i (`#01000000` zemin vb.) YAZILMAZ. Kontrol
köküne `Cursor=Hand`; sol tık (`MouseLeftButtonUp`) overlay penceresinde `RestoreRequested` event'ini
tetikler — davranış tray ikonunun sol tıkıyla AYNI (kablolama K-12/T5). `WS_EX_NOACTIVATE` mouse
event'lerini engellemez, yalnız overlay'in kendisinin aktive olmasını önler; asıl aktivasyonu
`ShowFromTray` yapar. Overlay çalışma alanının İÇİNDE, taskbar'ın üstünde durur — tray ikonunun kendisiyle
çakışmaz, tıklama davranışı (restore) değişmeden çalışır.

**K-3. Konum ve boyut: birincil ekran çalışma alanının sağ alt köşesi.**
`SystemParameters.WorkArea` (DIP, birincil ekran; taskbar hariç) → `Left = Right - W - 12`,
`Top = Bottom - H - 12`. Sahne oranı 430:286 korunur: **H = 96 DIP, W = 144 DIP** (Viewbox ölçekler), kenar
payı 12 DIP. Pencere her `Show()`'da yeniden konumlanır (çalışma alanı değişmiş olabilir). Bilinçli sınır:
tepsi birincil taskbar'dadır — çok monitörde overlay her zaman birincil ekranda kalır (doc'a yazılır).
DPI: `Left/Top/Width/Height` DIP'tir, PerMonitorV2 altında WPF dönüşümü yapar; ek Px hesabı YAZILMAZ.

**K-4. Görünürlük/terminal kararı SAF bir controller'da: `TrayBuildIndicatorController`.**
`App/Services/TrayBuildIndicatorController.cs` — WPF tipi taşımaz, iki seam'e konuşur:

```csharp
public interface ITrayBuildIndicatorView
{
    void ShowLoop();                    // overlay'i konumla + göster + döngüyü başlat
    void ShowStatic();                  // reduced motion: statik işaret + sayaç
    void UpdateCounter(int done, int total);
    void BeginExit(Action onFinished);  // yeni döngü başlatma; mevcut döngü 3.000s karesine koşsun, sonra callback
    void HideNow();                     // animasyonsuz anında gizle
}
public interface ITrayRunNotifier
{
    void ShowRunFinished(string message, bool healthy); // AppTrayIcon'a delege
}
```

Girişler: `SetMainWindowVisible(bool)`, `SetPhase(AppPhase)`, `SetAnimationsEnabled(bool)`,
`SetCounter(int done, int total)`, `SetTerminalText(string text, bool healthy)` (ribbon satırı, K-5).
Kurallar:

- Aktif küme: `Starting | Running | Stopping`. Overlay istenen durum = `!mainVisible && phase ∈ aktif küme`.
- Aktifken pencere görünür olursa → `HideNow()` (çıkış animasyonu YOK — kullanıcı zaten ekrana döndü).
- Aktif kümeden çıkış, overlay görünürken yakalanırsa → `BeginExit(...)`; callback'te `HideNow()` +
  `ShowRunFinished(terminalText, healthy)`. Reduced-motion modunda `BeginExit` yerine doğrudan
  `HideNow()` + balloon.
- Terminal, pencere GÖRÜNÜRKEN yakalanırsa balloon YOK (ribbon görünür durumda).
- `BeginExit` koşarken pencere geri getirilirse: `HideNow()` çağrılır ama balloon taahhüdü korunur —
  terminal anında tepsideydik, bildirim bir kez yine gösterilir (çift balloon YASAK: tek bayrakla korunur).
- `Starting → Idle` geri dönüşü (BeginRunAsync yerel hata, `RunViewModel.cs` ~:595) de aktif kümeden
  çıkıştır: overlay kapanır, balloon o anki ribbon metnini taşır (bir hata satırıdır) — özel dal YAZILMAZ.
- Sayaç yalnız overlay görünürken view'a itilir (görünmezken UI invalidation üretme — §14.5 disiplini).

**K-5. Balloon metni TEK KAYNAK: VM'in ribbon satırı. Yeni özet formatlayıcı YAZILMAZ.**
`RibbonText.Compose` terminal fazlarda tam istenen metni zaten üretir (§14.6'nın örnek cümlesi:
`Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`; ayrıca `Stopped — …`, `Run failed — …`,
engine-died kalıcı metni — öncelik sırası `RibbonText.Compose` ~:52-79'da hazır). Controller'a terminal
geçişte VM'in O ANKİ ribbon `Text`'i + sağlık bayrağı verilir. `healthy` = ribbon glyph'inin
`"failed"` OLMAMASI (glyph zaten tek kaynaklı statü sinyalidir; failed sayısı>0, runError, syncError,
engineDied hepsi `"failed"` glyph'i taşır). Balloon: `healthy ? NotificationIcon.Info : NotificationIcon.Error`,
başlık `AppIdentity.Product` (guard: ürün adı literal YAZILMAZ). `AppTrayIcon`'a
`ShowRunFinishedNotification(string message, bool healthy)` eklenir — mevcut `ShowNotification`
Warning'e sabit, ona dokunulmaz (ikinci-instance kullanıcısı var).

**K-6. Sayaç değerleri ribbon'la AYNI kaynaktan: `finishedOfWillBuild / willBuild`.**
Ribbon'un running satırının kullandığı sayı çifti neyse sayaç da onu gösterir (kopya/ikinci hesap YASAK).
`RunViewModel` bu değerleri `RibbonText.Compose`'a zaten veriyor (~:1210 civarı completed hesabı); dışarıya
yalnız-okunur property olarak açık değilse `int CounterDone` / `int CounterTotal` (PropertyChanged'li) açılır
ve Compose çağrısını besleyen İFADENİN KENDİSİ kullanılır (ifade iki yerde yeniden yazılmaz — gerekiyorsa
private helper'a çıkar). Sayaç `Starting`'de `0/0` olabilir — design'daki metin küçüktür, `0/0` kabul;
willBuild belli olunca gerçek değerler akar.

**K-7. Marka geometrisi TEK KAYNAĞA çıkarılır: `Resources/BrandGeometry.xaml`; AppMark + yeni kontrol ikisi de oradan tüketir.**
Guard "each geometry appears in exactly one source file" der (ARCHITECTURE §14.4; `AppMarkTests` /
`IconGeometryTests` — Opus güncel adlarıyla bulur). Beş pill + chevron path'i `AppMark.xaml`'de duruyor
(~:23-36) ve animasyonlu kontrol AYNI geometriyi ister → geometri paylaşılan bir `ResourceDictionary`'e
taşınır: beş `RectangleGeometry` (RadiusX/Y'li, x:Key başına bir pill) + bir `PathGeometry` (chevron;
`x:Shared`/Freeze varsayılanları korunur). İki kontrol de pill'leri `Path Data={StaticResource …}` ile
çizer. **Sweep kaplaması** chevron silüetinin per-instance **`Clone()`**'unu kullanır (frozen paylaşılan
geometry'ye per-instance `Geometry.Transform` animasyonu takılamaz — README'nin sweep tekniği clone
üzerinde kurulur). `AppMark` görsel çıktısı BİREBİR aynı kalır; `AppMarkTests` pinleri yeni yapıya göre
yeniden yazılır (renk/geometri iddiaları aynı, kaynak dosya değişti — gerekçe test doc'una). Tek-kaynak
guard'ı yeni dosyayı gösterecek şekilde güncellenir — bu bir gevşetme değil kaynak taşımadır.

**K-8. Renkler TOKEN'dan; SVG'nin çok-duraklı gradyanları PORT EDİLMEZ.**
Uygulama-içi marka arayüz paletiyle konuşur (§14.4): dolgular `AppMark`'takiyle AYNI token'lar
(`Brush.Brand.StripDim`, `Brush.Amber`, `Brush.Neutral700`, `Brush.TextPrimary`, `Brush.TextSecondary`;
chevron 3-durak `Color.AmberBright/Amber/Brand.ChevronDeep`, MappingMode=Absolute aynı koordinatlar).
Design asset'indeki 5-9 duraklı SVG gradyanları ve hex'ler ATILIR. Sayaç metni (design: `#5A5A63` %72,
beyaz kabartma gölgesi): ramp'te birebir karşılığı yoksa `Tokens.xaml`'e mevcut "markaya ait ara ton"
kalıbıyla gerekçeli tek token eklenir (`Brush.Brand.CounterText` gibi); %72 opaklık ve 0.7px beyaz
DropShadow kontrolde kalır. Anti-slop guard'ları güncellenir: gradient istisnası artık chevron'un
yaşadığı dosya(lar)ı kapsar; drop-shadow allowlist'ine overlay dosyaları eklenir (sınıf zaten
"floating overlay'ler" — tanıma birebir uyar).

**K-9. Zaman çizelgesi design deliverable'dan BİREBİR, saf-XAML Storyboard'da yaşar.**
README zamanlama tablosu (giriş 0–1.34 s, duruş, çıkış 2.10–3.00 s; parça başına KeyTime/KeySpline/çıkış
mesafeleri) sanat eserinin parçasıdır ve `AppMark` paleti gibi VERBATIM taşınır — `Duration.*`
token'larına bağlanamaz (80–280 ms rampası değildir). Motion guard'ı **kod-tarafı** ms literal'lerini tarar;
storyboard XAML'de kaldığı sürece ihlal yoktur — kod tarafına tek bir `TimeSpan`/ms literal'i YAZILMAZ
(bekleme/koşullar storyboard `Completed` event'i üzerinden akar). Bu bilinçli istisna (bespoke marka
zaman çizelgesi) kontrolün başlık yorumuna ve ARCHITECTURE §14.5'e yazılır. Ayrıca:
`Timeline.DesiredFrameRate=30` (decorative-infinite kuralı), yalnız transform + opacity animasyonu
(asset zaten öyle), layout animasyonu YOK.

**K-10. Döngü `RepeatBehavior=Forever` DEĞİL; tek iterasyon + `Completed`'da koşullu yeniden başlatma.**
"Bitince mevcut döngü çıkışını tamamlasın" gereksinimi ancak iterasyon sınırında karar vererek sağlanır:
storyboard tek geçiş koşar; `Completed`'da kontrol "devam mı, bitiş mi" sorar — devam ise `Begin` (v1.2
gereği 3.000 s karesi boş değildir, dikiş görünmez), bitiş ise pencere gizlenir ve `onFinished` callback'i
çağrılır. Böylece `BeginExit` = yalnız bir bayrak; yarım döngüde kesme, seek, hız değiştirme YOK.
**Görünürlük kapısı:** `IsVisibleChanged`'de görünmez → `Stop` (§14.5: "an infinite animation must stop
being visible before it stops running"; Forever kaçağı zaten yapısal olarak yok).

**K-11. Reduced motion canlı: başlangıçta TAZE okunur + değişim dinlenir.**
`IMotionSettings.AnimationsEnabled` (statik singleton `MotionSettings` — App.xaml.cs kayıtlı) her
`ShowLoop` kararında taze okunur (tüketim sözleşmesi, `IMotionSettings.cs` doc'u); kapalıysa controller
`ShowStatic()` seçer. `AnimationsEnabledChanged` overlay görünürken tetiklenirse: açık→kapalı = `Stop` +
statik kare; kapalı→açık = döngü başlar. Statik kare = duruş evresi kompozisyonu (tüm parçalar yerinde,
sayaç okunur).

**K-12. Kablolama `MainWindow.OnSourceInitialized`'da, tray kurulumunun yanında (~:816).**
Controller orada kurulur: `IsVisibleChanged` → `SetMainWindowVisible`; `_vm.PropertyChanged`
(`Phase`, sayaç property'leri) → `SetPhase`/`SetCounter`; terminal geçişte VM'den o anki ribbon satırı
çekilir → `SetTerminalText`. View implementasyonu `TrayBuildOverlayWindow`; notifier `AppTrayIcon`.
Overlay'in `RestoreRequested`'ı tray ikonununkiyle AYNI handler'a bağlanır: `+= ShowFromTray` (~:817
kalıbı — ikinci bir restore yolu YAZILMAZ).
Overlay penceresi lazy yaratılır (ilk `ShowLoop`/`ShowStatic`), `MainWindow.OnClosed`'da dispose zinciri:
`_tray?.Dispose()` yanına overlay `Close()`. Autostart yolu (`StartInTray`, ~:857) otomatik kapsanır:
pencere hiç görünmediği için `IsVisibleChanged` hiç "visible" demez — ilk run'da overlay tepside belirir.

**K-13. Yeni XAML kökleri REALIZE testi ister (headless süit runtime çözümlemesini görmez).**
İki yeni kök var: `TrayBuildIndicator` (UserControl) ve `TrayBuildOverlayWindow`. Mevcut realize kalıbıyla
(STA collection; `Window.Content` üzerinde Measure/Arrange — HWND'siz içeriğe inilmez) ikisi de realize
edilir; `BrandGeometry.xaml`'e geçen `AppMark` realize'ı zaten `AppMarkTests`'te var, yeşil kalmalı.

---

## Task 0 — İş branch'i aç

`feature/tray-build-indicator` gibi bir branch; task başına commit; sonda main'e merge + push + branch
temizliği; oturum main'de biter.

## Task 1 — Controller (saf mantık) + testleri

**Dosyalar:**
- Yeni: `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` (+ aynı dosyada iki interface)
- Test: `tests/BuildOrchestrator.Tests/App/TrayBuildIndicatorControllerTests.cs`

**Önce KIRMIZI testler** (fake view + fake notifier; WPF'siz düz sınıf):

- `Overlay_shows_when_a_run_is_active_and_the_window_is_hidden` — visible=false + `SetPhase(Running)` →
  `ShowLoop` tam bir kez.
- `Overlay_appears_when_the_window_hides_mid_run` — phase=Running, sonra visible=false → `ShowLoop`.
- `Overlay_hides_instantly_when_the_window_returns` — görünürken visible=true → `HideNow`, `BeginExit` YOK,
  balloon YOK.
- `Syncing_and_idle_phases_never_show_the_overlay`.
- `Terminal_while_hidden_plays_the_exit_then_notifies` — Running→Done (visible=false) → `BeginExit`;
  callback koşunca `HideNow` + `ShowRunFinished(text, healthy)` sırayla.
- `Terminal_while_visible_shows_no_balloon` — Running→Done (visible=true) → balloon YOK.
- `Window_restore_during_exit_still_notifies_exactly_once` — `BeginExit` bekliyor, visible=true →
  `HideNow`; callback yine koşunca balloon TEK.
- `Reduced_motion_shows_the_static_frame_and_skips_the_exit_animation` — AnimationsEnabled=false →
  `ShowStatic`; terminalde `BeginExit` DEĞİL `HideNow` + balloon.
- `Motion_setting_flips_live_while_the_overlay_is_up` — açık→kapalı → `ShowStatic`'e geçiş; kapalı→açık →
  `ShowLoop`.
- `Counter_updates_flow_only_while_the_overlay_is_shown` — gizliyken `SetCounter` view'a İTİLMEZ; overlay
  açılınca son değer bir kez itilir.
- `Starting_reverting_to_idle_closes_the_overlay_and_reports` — Starting (hidden) → Idle → çıkış + balloon
  (K-4'ün son maddesi).
- `Healthy_flag_reaches_the_notifier` — healthy=false → `ShowRunFinished(..., false)`.

**Implementasyon:** K-4 kural seti; durum = üç bool + faz + tek `_exitPending`/`_notified` bayrağı.
Balloon metni controller'da SAKLANMAZ; terminal anında `SetTerminalText` ile gelen değer kullanılır.

**Kabul:** yeni süit yeşil; hiçbir WPF tipi referans edilmez (derleme App projesinde ama `using System.Windows` yok).

## Task 2 — `BrandGeometry.xaml`: geometri tek kaynağa taşınır, `AppMark` ona geçer

**Dosyalar:**
- Yeni: `src/BuildOrchestrator.App/Resources/BrandGeometry.xaml` (App.xaml merged dictionaries'e eklenir)
- Değişen: `src/BuildOrchestrator.App/Controls/AppMark.xaml` (~:23-44)
- Test: `tests/BuildOrchestrator.Tests/App/AppMarkTests.cs` + geometri tek-kaynak guard'ının dosyası
  (Opus bulur: "exactly one source file" iddiasını taşıyan test)

**Önce KIRMIZI:** guard testi yeni kaynağa göre yeniden yazılır (davranış-değişimi kuralı: eski pin
silinmez, YENİ kuralı — "pill+chevron geometrisi yalnız `BrandGeometry.xaml`'de tanımlı, `AppMark` ve
`TrayBuildIndicator` tüketici" — pinleyecek şekilde güncellenir ve önce kırmızı gösterilir; gerekçe test
doc'una: animasyonlu tray göstergesi aynı geometriyi paylaşıyor).

**Implementasyon:** K-7. Beş pill `RectangleGeometry` + chevron `PathGeometry` anahtarlı kaynaklar;
`AppMark` `Rectangle` yerine `Path Data={StaticResource Brand.Pill.*}` çizer, dolgular/gradient aynen
token'dan. Görsel eşdeğerlik: `AppMarkTests`'in renk/yapı pinleri (chevron 3 durak, token renkleri,
~:72-77) yeni ağaçta yeşil kalır — Path'e geçiş pinlerin element tipini değiştiriyorsa pinler aynı iddiayı
yeni tipte kurar.

**Kabul:** AppMark realize + guard süitleri yeşil; title bar / About görünümü değişmedi (token ve geometri
değerleri birebir).

## Task 3 — `TrayBuildIndicator` kontrolü (asset portu: sayaçlı animasyon)

**Dosyalar:**
- Yeni: `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml` + `.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/TrayBuildIndicatorTests.cs`

**Önce KIRMIZI testler** (realize kalıbı + eleman pinleri):

- `Indicator_realizes_without_a_hwnd` — realize testi (K-13; yeni XAML kökü kuralı).
- `Sweep_and_chevron_share_the_exact_same_keyframes` — `ChevronShift.X` ve `SweepShift.X` animasyonlarının
  KeyTime + KeySpline dizileri BİREBİR eşit (README'nin bağlayıcı senkron kuralını pinler — bu test
  gelecekte zamanlamayı tek taraflı değiştireni yakalar).
- `All_fills_come_from_tokens` — kontrolde hex literal yok; beş pill + chevron dolguları K-8'deki token
  kümesi.
- `The_loop_is_a_single_iteration_not_forever` — storyboard `RepeatBehavior` Forever DEĞİL (K-10 yapısal
  pin).
- `Desired_frame_rate_is_capped_for_the_decorative_loop` — `Timeline.DesiredFrameRate` 30 (mevcut motion
  guard sabiti neredeyse oradan okunur — literal İKİNCİ kez yazılmaz).
- `Counter_text_renders_done_over_total` — `SetCounter(139, 248)` → `CountText.Text == "139/248"`
  (InvariantCulture).
- `Hiding_the_control_stops_the_clock` — `IsVisibleChanged` görünmez → storyboard clock'u durur (K-10 kapısı).

**Implementasyon:** asset XAML'i taşınır; dolgular token'a, geometriler `BrandGeometry`'ye bağlanır (sweep
= chevron clone, K-7); `CountText` public API: `void SetCounter(int done, int total)`. Kod-tarafı üyeler:
`BeginLoop()`, `ShowStaticFrame()`, `RequestFinish(Action onFinished)` (K-10 bayrağı), `StopNow()`.
Kod tarafına ms literal'i yazılmaz (K-9).

**Kabul:** yeni testler + AntiSlop/motion/token guard süitleri yeşil (gradient/gölge allowlist
güncellemeleri bu task'ta — K-8).

## Task 4 — `TrayBuildOverlayWindow` (penceresiz kabuk)

**Dosyalar:**
- Yeni: `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml` + `.xaml.cs`
- Değişen (gerekirse): `src/BuildOrchestrator.App/Shell/Win32.cs` (GWL_EXSTYLE sabitleri/P-Invoke yoksa)
- Test: `tests/BuildOrchestrator.Tests/App/TrayBuildOverlayWindowTests.cs`

**Önce KIRMIZI testler:**

- `Overlay_window_realizes_without_a_hwnd` — realize (window.Content üzerinde).
- `Overlay_window_declares_the_non_intrusive_shell` — XAML/CLR pinleri: `WindowStyle=None`,
  `AllowsTransparency`, `ShowInTaskbar=false`, `Topmost`, `ShowActivated=false`, `Focusable=false`.
- `Clicking_the_overlay_requests_a_restore` — içerik köküne `MouseLeftButtonUpEvent` RaiseEvent edilir →
  `RestoreRequested` tam bir kez tetiklenir; kök `Cursor == Cursors.Hand` (K-2). Ayrıca negatif pin:
  ex-style kurulumunda `WS_EX_TRANSPARENT` YOK (per-pixel geçirgenlik layered pencereden gelir — bit
  eklenirse logo tıklanamaz olur; kurulan bayrak kümesi test edilebilir bir sabitten okunur).
- `Overlay_positions_into_the_bottom_right_of_a_given_work_area` — konum matematiği saf statik helper'a
  çıkarılır (`static (double Left, double Top) Place(Rect workArea, double w, double h, double margin)`)
  ve kenar payıyla birlikte pinlenir (K-3; WorkArea'yı test enjekte eder).

**Implementasyon:** K-1 + K-2 + K-3. `ITrayBuildIndicatorView`'ı implemente eder: `ShowLoop` → konumla +
`Show()` + `BeginLoop`; `ShowStatic` → konumla + `Show()` + `ShowStaticFrame`; `BeginExit` →
`RequestFinish`; `HideNow` → `StopNow` + `Hide()`. WS_EX bayrakları `OnSourceInitialized`'da. Ek üye:
`public event Action? RestoreRequested` (tray ikonundaki adlandırma kalıbı) — `MouseLeftButtonUp`'ta
tetiklenir.

**Kabul:** yeni testler yeşil; AntiSlop drop-shadow allowlist'i overlay'i kapsıyor.

## Task 5 — Kablolama: VM sinyalleri + `AppTrayIcon` bildirimi + `MainWindow` kurulumu

**Dosyalar:**
- Değişen: `src/BuildOrchestrator.App/MainWindow.xaml.cs` (~:804-899 bölgesi),
  `src/BuildOrchestrator.App/Shell/AppTrayIcon.cs`,
  `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (yalnız K-6 property açılımı gerekiyorsa)
- Test: `tests/BuildOrchestrator.Tests/App/` mevcut VM/tray test dosyalarına ekler

**Önce KIRMIZI testler:**

- `Counter_properties_track_the_ribbon_inputs` — VM'e event akışı verildiğinde `CounterDone/CounterTotal`
  ribbon'daki `fin/wb` ile aynı değerleri raporlar (tek kaynak pini; K-6).
- `Terminal_transition_hands_the_current_ribbon_line_to_the_tray_controller` — Running→Done'da controller'a
  giden metin == VM'in o anki ribbon `Text`'i, healthy == (glyph != "failed") (K-5; VM ya da kablolama
  seviyesinde, hangisi dikişsizse — metin İKİNCİ kez compose EDİLMEZ).
- `Tray_icon_run_finished_notification_uses_info_or_error` — `AppTrayIcon.ShowRunFinishedNotification`
  healthy=true→Info, false→Error; başlık `AppIdentity.Product` (mevcut tray test kalıbı varsa oraya, yoksa
  yeni dosya; H.NotifyIcon'u sarmak gerekiyorsa mevcut testlerin yaklaşımı izlenir — sarılamıyorsa bu pin
  AppTrayIcon'a değil çağıran koda kurulur).

**Implementasyon:** K-12. Ek: engine-died yolunda (`ReleaseAfterEngineLoss` ~:1368 bölgesi) faz zaten
Stopped'a çekiliyor — controller'ın terminal dalı otomatik kapsar, ÖZEL DAL YAZILMAZ; test yalnız akışı
pinler (Running'de engine ölür → balloon engine-died metnini taşır, healthy=false).

**Kabul:** yeni testler + mevcut MainWindow/VM süitleri yeşil.

## Task 6 — Uçtan uca gözle doğrulama (derlenen makinede) + tam süit

1. `dotnet test … --filter "Category!=Acceptance"` — TAM süit yeşil (token/motion/D8/AntiSlop guard'ları
   dahil).
2. Elle senaryo listesi (uygulamayı kapatmadan Supervisor kilidi notuna dikkat):
   - Run başlat → `X` ile tepsiye → overlay sağ altta döngüde, sayaç ilerliyor; logonun ETRAFINDAKİ
     şeffaf alana tıklama alttaki pencereye geçiyor, tray ikonu tıklanabilir kalıyor.
   - Logonun kendisine (şerit/şevron) tıkla → ana pencere geri geliyor, overlay anında kayboluyor; imleç
     logo üzerinde el (Hand) oluyor.
   - Koşu biterken izle: döngü çıkış evresini tamamlıyor, son karede kaybolup balloon geliyor; metin
     ribbon'la aynı.
   - Balloon'lu bitişten sonra pencereyi aç: ribbon aynı terminal metnini gösteriyor.
   - Hatalı proje ile koş → balloon Error ikonlu, "N failed" metinli.
   - Tepsi menüsü → Stop → drain sonrası `Stopped — …` balloon'u.
   - Koşu ortasında pencereyi geri getir → overlay anında yok; tekrar `X` → overlay geri.
   - Windows "animasyon efektleri" kapalıyken: statik işaret + sayaç; bitişte animasyonsuz kaybolma + balloon.
   - Autostart (`--autostart`) + tepsiden hiç açmadan run tetikleme mümkünse (bugün UI'sız — Sync/Run
     penceresiz tetiklenemiyorsa bu senaryo atlanır, doc'taki gelecek-kısayol notuyla birlikte kalır).

## Task 7 — Doküman güncellemeleri (aynı işte)

- **ARCHITECTURE §12.2:** "`AllowsTransparency` is never used" cümlesi ana pencere kabuğuna daraltılır
  (tray overlay bilinçli istisna, gerekçesiyle).
- **ARCHITECTURE §12.3:** tepsi bölümüne overlay davranışı: ne zaman görünür, tıkla-aç (logo pikselleri
  restore eder, şeffaf alan per-pixel geçirgen)/no-activate, bitiş koreografisi + balloon'un ribbon satırını taşıması, birincil-ekran sınırı, gelecek kısayol notu
  (tek cümle: gösterge faz-güdümlüdür, arka plan tetikleme eklendiğinde ek iş gerektirmez).
- **ARCHITECTURE §14.4:** marka bölümü — geometri artık `BrandGeometry.xaml`'de tek kaynak, tüketiciler
  AppMark + TrayBuildIndicator; sayaç ara tonu eklendiyse token gerekçesi.
- **ARCHITECTURE §14.5:** bespoke marka zaman çizelgesi istisnası (verbatim-artwork ilkesinin motion
  karşılığı) + tek-iterasyon/Completed kalıbı + DesiredFrameRate.
- **ARCHITECTURE §22:** kod haritasına yeni dosyalar (controller, kontrol, overlay window, BrandGeometry).
- **README:** kullanım bölümüne iki cümle: tepsideyken koşan derleme sağ altta göstergeyle izlenir; bitince
  OS bildirimi gelir.
- Anlatı üslubu: "eskiden/şu oturumda" YOK; bayatlayacak rakam gömme (96/144/12 DIP gibi ölçüler kontrol
  doc'unda yaşar, ARCHITECTURE'a yalnız davranış yazılır).

---

## Bağımlılık şeması

**T1 ‖ T2 → T3 → T4 → T5 → T6 → T7** (T1 ile T2 bağımsız, paralel koşabilir; T3, T2'nin geometri
dictionary'sine ve T1'in view interface'ine bakar).

## Riskler / bilinçli sınırlar

- **`AllowsTransparency` + animasyon = software render** o pencere için; 144×96 DIP'lik sahnede maliyet
  önemsizdir, ama DropShadowEffect'ler pahalanırsa gölge opaklığı/blur'u düşürmek serbesttir (görsel
  eşik: açık renk masaüstünde beyaz/gümüş şeritler seçilebilir kalmalı).
- **H.NotifyIcon balloon davranışı** Windows bildirim ayarlarına tabidir (odak yardımı/bildirim kapalıysa
  OS bastırabilir) — bilinçli sınır, doc'a yazılır; uygulama-içi toast fallback'i YASAK (§14.7).
- **Tek balloon garantisi** controller bayrağıyladır; Supervisor/VM çift terminal event üretirse (bilinen
  davranış değil) ikinci balloon yine bastırılır (`_notified` reset'i yalnız yeni run başlangıcında).
- **Tıklama hedefi hareketli:** giriş/çıkış evrelerinde parçalar kayarken tık ancak çizili piksele denk
  gelirse yakalanır; duruş evresi döngünün büyük kısmı olduğu için pratikte sorun değil (bilinçli sınır —
  tam-dikdörtgen görünmez hit alanı BİLEREK yok: o alan altındaki tıklamaları çalar).
- **Sayaç 4+ hane** (done/total 1000+) design'da öngörülmedi — OSYS ölçeği (~250) için sorun değil;
  taşarsa `Viewbox` şeridi taşırmaz ama metin sıkışır (bilinçli sınır, kontrol doc'una).
