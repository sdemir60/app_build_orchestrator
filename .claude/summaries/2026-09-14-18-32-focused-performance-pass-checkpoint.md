# Odaklı performans geçişi — ara kayıt (yarım)

Tarih: 2026-09-14 · main @ 03e6384 · kod DEĞİŞMEDİ (tüm deneyler geri alındı, ağaç temiz)

## Bağlam
- 2026-08-26 planı (`outputs/2026-08-26-08-54-performance-analysis-plan.md`) hiç yürütülmemişti; rapor YOKTU
  (tüm transcript'ler tarandı). Kullanıcı kararı: **"Odaklı analiz + uygula"** — yalnız doğrulanmış, güvenli,
  nokta atışı düzeltmeler; kırmızı test kuralı; ARCHITECTURE.md'deki bilinçli kararlara (kırmızı çizgiler)
  dokunulmaz.
- Kırmızı çizgiler (dokunulmaz): UseSharedCompilation=false (§9.2) · ObservableCollection Reset yasağı ·
  renk geçişleri anlık (§14.5) · Supervisor stderr drain (§4.3) · graceful stop (§4.5) · DLL timestamp okunmaz ·
  eval-cache mtime+length · scheduler tek lock · graph culling yok · App job dışı · console pin tekrarlı measure.

## Ölçümler (bu makine)
| Ölçüm | Değer |
|---|---|
| Debug build (sıcak) | 19 sn · Release build 12 sn |
| Tam süit (Category!=Acceptance) | 2803 başarılı · 0 başarısız · 6 atlanan · 4 dk 37 sn (taban çizgisi) |
| Açılış (MainWindowHandle) | 1,5 sn; Supervisor aynı anda spawn |
| Kill cascade (App Stop-Process) | Supervisor 84 ms'de öldü, artık yok |
| Boşta görünür (faz Boot) | App ~205 M döngü/sn; render döngüsü ~55-60/sn sürekli |
| Boşta tray | App ~81 M döngü/sn (main ~65 M) — taban |

Ölçüm yöntemi notu: `TotalProcessorTime` 15,6 ms kuantumlu → gürültülü; güvenilir olan
`QueryProcessCycleTime/QueryThreadCycleTime` (scratchpad `measure-cycles.ps1`). Görünür değer koşudan koşuya
±%20 oynar (maximize açılan pencerenin altındaki gerçek fare hover geçişleri tetikliyor).

## Doğrulanan bulgular
1. **Konsol + event-stream imleç saatleri IsVisible kapısız** (blink `CreateBlinkAnimation` Forever 30fps +
   `CursorHop` renk turu Forever 20fps). Pencere tray'e gizlenince de döner. Başlatıcılar olaylarla yeniden
   çağrılıyor (konsol: `VisualLinesChanged → RefreshPrompt → StartBlink`, ConsoleView.xaml.cs ~123/455-519;
   stream: `UpdateActiveLine → StartCursorBlink` koşulsuz, EventStreamView.xaml.cs ~264-272/404-422).
   Doküman §14.5 "every infinite animation is gated on IsVisible" → **kod kurala uymuyor**.
   A/B (döngü sayacı, tray): taban proc 80,9 / main 65,5 M → başlatıcı kapılı (C) proc 67,1 / main 52,9 M.
   Doğru düzeltme şekli: `Start*` içinde `!IsVisible` ise durdur+dön, `IsVisibleChanged`'de yeniden değerlendir
   (yalnız "gizlenince durdur" YETMEZ — yeniden başlatılıyor).
2. **ConsoleBatcher 50 ms pompası boşta da döner** (ConsoleBatcher.cs:105-110, App.xaml.cs:105). dotnet-trace
   tray profili: TimerQueue thread ~160 ms + threadpool ~58 ms CPU / 20 sn — main thread kadar. Düzeltme adayı:
   tick'ten önce `reader.WaitToReadAsync` ile veri bekle (boşta sıfır uyanma). ConsoleBatcherTests tick-first
   deterministik sözleşmesini kontrol et. Kazanç henüz A/B ile ölçülmedi.
3. Gizlemeden sonra render döngüsü **~8 sn daha sürüp duruyor** (2 sn çözünürlüklü zaman çizelgesi), sonra tray
   sessiz: yalnız 200 ms tick (5/sn). Kalıcı sızıntı DEĞİL. Gizli anda TimeManager kök saatlerinin hepsi Filling,
   `CompositionTarget.Rendering` abonesi yok. 8 sn kuyruğun kaynağı belirlenmedi (düşük öncelik).

## Temiz çıkanlar (bulgu değil)
- MainWindow 200 ms tick: SetLineCount / EngineOverdueMessage değişmediyse yazmıyor.
- OnProjectLog satır başı O(1). Tray göstergesi disiplinli. BuildingSpinner/StatusGlyph kapılı. Açılış hafif.
- VM boşta hiç PropertyChanged yaymıyor.

## Aynı sınıftan, henüz ölçülmemiş adaylar
- ProjectRow nefes animasyonu (ProjectRow.xaml.cs:577-589) — build sürerken pencere tray'deyse döner.
- StickyRibbon belirsiz süpürme (StickyRibbon.xaml.cs:367-391) — yalnız Syncing/Starting.
- GraphView beads/edge-flow — kendi Visibility'sine bakar, pencere gizlenmesine değil (headless gerekçesi
  GraphView.xaml.cs:622-626). Müdahale daha invaziv → rapora "karar gerektiren".
- Görünür boşta 55-60 render/sn: imleç blink'leri tasarım gereği → "karar gerektiren", uygulanmaz.
- Kozmetik, uygulanmayacak: NdjsonWriter mesaj başı 2 write+flush (NdjsonFraming.cs:24-27).

## Kalan iş
1. Branch `perf/focused-pass` aç.
2. Bulgu 1: kırmızı test (IdleClockTests deseni — realize + Visibility.Collapsed → imleç saati durmalı; tekrar
   görünür → geri gelmeli; olay yeniden tetiklese de gizliyken başlamamalı) → fix.
3. Bulgu 2: kırmızı test (boş kanalda tick çağrılmamalı) → fix → döngü sayacıyla A/B.
4. ProjectRow nefesi + ribbon süpürmesi için aynı kapı (ayrı testler).
5. Tam süit yeşil + döngü sayacıyla son ölçüm → rapor `outputs/<zaman>-focused-performance-pass-report.md` →
   gerekirse ARCHITECTURE.md §14.5 → merge + push.

Araçlar (scratchpad, oturuma özel — başka makinede yok): `measure-cycles.ps1`, `measure-cswitch.ps1`,
`timeline.ps1`, dotnet-trace (tool-path kurulumu).
