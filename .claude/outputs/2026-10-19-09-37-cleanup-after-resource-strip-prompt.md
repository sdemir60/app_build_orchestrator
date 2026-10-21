# Prompt — kaynak şeridi sonrası temizlik dalı

Yeni oturumda bu dosyanın adını yazıp "bu işi yapacaksın" de. Ana proje (`D:\Projects\Other\Apps\app_build_orchestrator`),
`develop` üzerinde, uygulama kapalıyken başla.

---

Küçük ve mekanik bir temizlik işi yapacaksın; davranış değişikliği YOK (tek istisna 1. maddedeki testlerin ölçüm biçimi). Kararlar
verildi; işin planlamak, uygulamak, test etmek, dokümanı güncellemek ve merge etmek. Önce şunları oku, sonra kök `CLAUDE.md`'yi:

1. Bu prompt — NE ve NEDEN.
2. `.claude/summaries/2026-10-17-19-56-resource-strip-and-resolve-priority-summary.md` — bu maddelerin geldiği iş (Resolve önceliği
   kuralının kaldırılması + title bar kaynak şeridi, ikisi de `develop`'ta).
3. ARCHITECTURE.md: §3.3 (ürün adı guard'ı), §13.3 (About + What's new), §14 girişi, §14.4, §17.2 (guard listesi), §17.4.

Dal: `chore/cleanup-after-resource-strip` (`develop`'tan). Planı `superpowers:writing-plans` ile KISA yaz (madde başına bir görev, toplam
5-6 görev); işler küçük olduğu için `superpowers:executing-plans` ile kendin uygula, sonda tek bir bağımsız bütün-dal incelemesi
(en güçlü model) yeterli — görev başına inceleme turu açma. Bu prompt dosyasını dalın ilk commit'ine ekle.

## Değişmeyen şeyler (dokunma)

- Kaynak şeridinin davranışı ve görünümü, Resolve/perf çipi kuralları, `memory:` satırı, motor, sürüm (`CHANGELOG.md`, `Directory.Build.props`
  → `Version` dokunulmaz; sürüm çıkarılmayacak).
- Hiçbir bütçe/eşik "yeşile boyamak için" gevşetilmez (CLAUDE.md). Bir testin ölçüm biçimi değişiyorsa gerekçesi ölçümle doc'a yazılır.

## 1. UpdatePillTests ekran ölçeğinden bağımsız olsun

- `tests/BuildOrchestrator.Tests/App/UpdatePillTests.cs`: `The_existing_title_bar_buttons_do_not_move_when_the_pill_appears` (~70) ve
  `The_pill_is_drawn_to_the_design_numbers` (~109). Bu makinede (ekran ölçeği %125/%150) her tam süitte kırmızı: WPF yerleşim yuvarlaması
  yüzünden `22` yerine `22.4`, `232` yerine `231.99…` ölçülüyor. Değişmemiş develop kopyasında da aynı düşüyor; CI (ölçek 1.0) yeşil.
- Hedef: iki test her ölçekte geçsin ve gerçek bir kaymayı yine yakalasın. Kabul edilebilir yollar: (a) "hap gelince düğmeler kaymaz"
  iddiasını mutlak sayıyla değil, hap öncesi/sonrası AYNI pencerede ölçülen konumların eşitliğiyle kurmak; (b) tasarım sayılarını DIP'te,
  o anki DPI'dan türetilen TEK cihaz pikselinden küçük bir yuvarlama payıyla pinlemek (pay sabit sayı değil `VisualTreeHelper.GetDpi`'den;
  `MainWindowRealizeTests`'teki `AlignmentSteps` ve `ResourceStripRealizeTests`'teki cihaz pikseli bütçesi bu deseni kullanıyor).
- Kanıt: bu makinede yeşil; 1 DIP'lik gerçek bir tasarım sapması (mutasyon: ör. hap kenar boşluğunu 1 artır) kırmızı; doc'ta eski iddia
  + ölçüm (hangi ölçekte hangi değer). Bu iki koşul birlikte sağlanamıyorsa DUR ve kullanıcıya sor.

## 2. Ürün adı her yerde tek kaynaktan (`AppIdentity.Product`)

- Hâlâ elle yazılı dört yer: `src/BuildOrchestrator.App/ViewModels/SettingsFile.cs:28` (`FileFilter`, dosya diyaloğunda görünür),
  `src/BuildOrchestrator.App/Views/NotesDialog.xaml:34` ("Release notes for Build Orchestrator." — What's new'de görünür),
  `src/BuildOrchestrator.App/Spikes/FontAbWindow.xaml.cs:85` (geliştirici spike penceresi), `src/BuildOrchestrator.Core/MsBuild/WpfTemporaryAssemblyTargets.cs:40`
  (üretilen MSBuild dosyasındaki yorum; Core `AppIdentity`'yi göremez, kullanıcıya görünmez).
- Görünen ikisi `AppIdentity.Product`'tan okunur. Spike ve MSBuild yorumu için karar: ya ad oradan da gelir ya da gerekçeli istisna olarak
  guard'ın istisna listesine yazılır (kullanıcıya görünmeyen metin).
- Guard: `tests/BuildOrchestrator.Tests/App/AppIdentityTests.cs` → `NameFreeSurfaceFiles` (~138) şu an opt-in bir dosya listesi; YENİ bir
  dosya adı cümleye gömerse görünmez. Opt-out'a çevir: bütün `src`'yi tara (`SourceGuard.ScanSrcLiterals` `allowedFiles` alıyor), yalnız
  gerekçeli istisnaları muaf tut; guard'ın kendi kanıtı (yeni bir dosyaya gömülü sahte ad yakalanır) korunur.

## 3. `—` (okunamayan/bilinmeyen değer) tek sabit

- Üç ayrı tanım: `src/BuildOrchestrator.Core/Formatting/DurationFormat.cs:17` (literal), `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs:82`
  (`Unknown`), `src/BuildOrchestrator.Core/Diagnostics/ResourceStripText.cs:29` (`Unreadable`). Core'da tek bir public sabit; diğerleri ona
  bağlanır (App'teki `Unknown` adı kalabilir, değeri Core'dan gelir). Spike'taki örnek metin istisna olabilir.

## 4. Test kopyaları ve iki eksik pin

- `tests/BuildOrchestrator.Tests/App/ResourceSamplerTests.cs`: "job + completion port + kapılı çocuk + `more` doğumunu bekle" kurulumu
  3-4 testte birebir → tek yardımcı.
- `private const long Mb` sekiz test dosyasında ayrı ayrı → tek ortak test sabiti.
- `tests/BuildOrchestrator.Tests/MsBuild/MsBuildInvokerTests.cs` ~108, ~120, ~257 kendi `WaitNext(...) ?? throw` döngülerini taşıyor →
  `tests/BuildOrchestrator.Tests/ProcessControl/JobBirths.cs`'in tek `Pump` döngüsüne (gerekirse `UntilExit(pid)` ekle; `Until` davranışı
  birebir korunur).
- "TitleBarRow.Child bir Grid, ilk çocuğu DockPanel" bilgisi `MainWindowRealizeTests` (~225) ve `ResourceStripRealizeTests` (~183, ~247-251)
  içinde üç kez → `MainWindowHost` altında küçük bir yardımcı.
- `tests/BuildOrchestrator.Tests/App/SourceGuard.cs` → `AssertScansEveryProductionProject` (~214-226) kendi başarısızlık yolunu pinleyen
  testten yoksun → saf bir çekirdek + sahte (eksik proje) girdiyle kendi kanıtı.
- `ResourceStripGuardTests`'teki yerel çağrı listesi `Win32.WM_LBUTTONUP`'u içermiyor (hücreden sürüklemenin kapanış mesajı) → ekle.
- Hepsi davranış değiştirmeyen düzenleme: testler önce ve sonra yeşil; her yardımcı en az iki yerden kullanılmalı.

## 5. Doküman

- ARCHITECTURE.md ~6106 (§14.4): "no other amber element belongs in that region" diyor, ama §13.3 What's new düğmesindeki amber "görülmemiş
  sürüm" noktasını anlatıyor (kodda `MainWindow.xaml` → `UnseenNotesDot`, `Brush.Amber`; tasarım v1.13.0). Cümleyi o tek istisnayı
  adıyla anacak biçimde düzelt; kaynak şeridinin amber taşımadığı cümlesi aynen kalır.
- §14 girişi "bileşen ölçüleri kontrolde sabit olarak durur" der; kaynak şeridinin dört ölçüsü `Tokens.xaml`'de (şeridin XAML'inde sayı
  literal'i guard'la yasak). Girişe tek cümle.
- Değişen guard ve yardımcılar için §17.2/§17.4 satırları; §3.3'teki ürün adı guard'ı anlatımı opt-out'a göre.

## 6. Yalnız ölç ve sor (değiştirme)

- About → Environment: içerik gövdeyi (`AboutDialog.xaml` sabit 284 px) ~11-15 px aşıyor olabilir; o zaman kaydırılacak bir şey yokken
  kaydırma çubuğu görünür. Realize testiyle ya da canlı ölç; çubuk görünüyorsa sayıları ve bir ekran görüntüsünü kullanıcıya göster,
  değiştirmeden önce sor (tasarım kararı).

## Bitiş

- Tam süit (`--filter "Category!=Acceptance"`, Release, çıktı dosyaya). Yük altında düşüp tek başına geçen bilinen testler:
  `SafeExitProcessTests`, `KillMidBuildTests`, `ConsoleReaderNavigationTests` — düşerlerse tek başına tekrar koş, değişikliğe bağlama.
  1. madde bittikten sonra `UpdatePillTests` artık bu makinede de yeşil olmalı.
- `develop`'a `--no-ff` merge + push, merge doğrulanınca dal local + remote silinir, CI (`ci.yml`) kontrol edilir.
- Commit ve PR metinlerinde Claude attribution satırı YOK. Oturum `develop` üzerinde biter; `.claude/summaries/` altına kısa özet.
