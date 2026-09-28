# Eksikler — Kararlar ve Uygulama Promptları

- **Tarih:** 2026-09-28 · **Taban:** `main` (`35ddc41`)
- **Kaynak:** `.claude/outputs/2026-09-28-12-33-ui-function-inventory.md` (işlev envanteri, §14-§16 eksikler)
- **Amaç:** Kullanıcının eksik listesi üzerindeki kararlarını ve her iş için yeni bir oturuma yapıştırılacak
  promptları tek yerde tutmak.

## 1. Kararlar

| # | Madde | Karar | Prompt |
|---|---|---|---|
| 1 | Close to tray | Ayara göre çalışacak. Kapalıyken × uygulamayı tamamen kapatır. Tam kapanışta arkada yarım iş kalmaz | P3 |
| 2 | Close to tray kapalı + derleme sürerken × | **a)** önce derlemeyi durdur, sonra kapat | P3 |
| 3 | Show notifications | **Bütün** bildirimler (OS balonları) bu ayara uyar | P3 |
| 4 | Start with Windows | Çalışacak; Windows tarafında gereken her şey yapılacak | P4 |
| 5 | Start minimized to tray | Windows ile açılışta ayar açıksa tepside (sağ alt) gizli gelir, kapalıysa pencere açılır | P4 |
| 6 | Build ▾ → Clean | **A:** satırdaki Clean ne yapıyorsa tüm projeler için aynısı (`msbuild -t:Clean`) | P2 |
| 7 | What's new notları | Ayrı bir çalışma: notların nerede ve nasıl yazılacağı tasarlanacak; içerik koda göre düzeltilecek. Bugünkü notlar örnek | P5 |
| 8 | Alt+B kısayol ayarı | Kapsam dışı — kullanıcı kısayolları ayrı çalışıyor | — |
| 9 | README General paragrafı | Bugün çalışan yapıya göre düzeltilecek | P1 |
| 10 | Proje listesi `⌄ latest` pill'i | Listede gerek yok → kaldırılacak | P1 |
| 11 | `ChangeRepositoryAsync` | Gereksiz → temizlenecek (ilk kurulum ekranındaki Open settings / Import settings… onu kullanmıyor) | P1 |
| 12 | Bayat kod yorumları | Düzeltilecek | P1 |
| 13 | Tepsi menüsündeki Stop | Derleme yokken pasif | P1 |
| 14 | Test derleme uyarıları | Temizlenecek | P1 |
| 15 | Zorla durdurma düğmesi | Gerek yok (Stop düzgün çalışıyor) | — |

## 2. Kullanıcı ilkesinden türetilen varsayılanlar (promptlara yazıldı)

Kullanıcı itiraz ederse ilgili prompt değiştirilir.

- **Tepsi → Exit de aynı güvenli yoldan geçer.** Derleme sürüyorsa önce durdurulur, bitince kapanılır; bugün
  derlemeyi Job Object ile anında öldürüyor. Pencere gizliyken basıldıysa, kullanıcı neden beklendiğini görsün
  diye öne gelir.
- **Kapanışta Sync / Clean / Optimize / checkout / pull sürüyorsa bitmesi beklenir.** Yarıda kesilen bir Clean
  ya da git işlemi diskte yarım iş bırakır.
- **Motor yanıt vermezse sonsuza dek beklenmez.** Çıkılır, Job Object kalan her şeyi kapatır. Windows oturumu
  kapanırken bugünkü anında çıkış korunur.
- **Show notifications kapalıyken** "zaten açık" uyarısı dahil tüm balonlar kapanır. Sağ alttaki animasyonlu
  tepsi logosu bildirim değil gösterge olduğu için bu ayardan etkilenmez.
- **Dört General anahtarı Export/Import dosyasına da girer** (tooltip zaten "the whole form" diyor).
- **Build ▾ → Clean** onay sormaz, bitince Sync zincirlemez (satır Clean'i gibi). Harici projeler ve döngü
  üyeleri dahildir.
- **Elle açılış her zaman pencereyi gösterir.** Start minimized yalnız Windows ile başlamayı etkiler. Windows ile
  açılışta da, bugünkü gibi, motor hazır olunca bir Sync (git fetch + analiz) koşar.

## 3. Sıra

Promptlar **sırayla** çalıştırılır; her biri kendi branch'inde biter ve bir sonrakinden önce `main`'e merge
edilir (aynı dosyalara dokunuyorlar).

**P1 temizlik → P2 Build ▾ Clean → P3 Close to tray + güvenli kapanış + bildirimler → P4 Windows ile başlama →
P5 What's new**

P5 en sonda, çünkü P2-P4 bitince bazı notlar doğru, bazıları yanlış hâle gelecek.

---

## 4. Promptlar

### P1 — Küçük eksikler ve temizlik

```text
Küçük ve birbirinden bağımsız bir temizlik turu yapacağız. Bağlam: .claude/outputs/2026-09-28-12-33-ui-function-inventory.md (§14-§16) ve .claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md.

1. Proje listesindeki `⌄ latest` pill'ini kaldır — karar: listede gerek yok. Hiç görünür olmuyor ve tıklaması bağlı değil: ShellRoot.xaml'deki PART_ProjectsPill, ShellRoot.xaml.cs'teki AccessibleName ataması, AccessibilityNames.LatestProjects ve onu pinleyen AccessibilityTests satırları. Hiç çağıranı olmayan StickyLayerList.ResumeFollow da gider. IsFollowSuppressedByUser'ı testler prob olarak kullanıyor, gerekmedikçe dokunma. Konsol ve event stream'deki pill'ler çalışıyor, onlara dokunulmaz.

2. RunViewModel.ChangeRepositoryAsync'i kaldır: üretimde çağıranı yok, kaldırılmış "Choose Folder" düğmesinden kalma. İlk kurulum davetindeki "Open settings" / "Import settings…" onu kullanmıyor; kök Settings → Save → ApplySettingsAsync ile uygulanıyor. Onu süren testler: BranchInventoryTests.Changing_the_repository_re_asks_for_the_inventory_with_the_new_root, RunViewModelTests.A_root_change_forgets_the_last_sync_head ve SettingsDialogTests'teki üç test (ChangeRepositoryAsync'i çağıranlar). Her testin pinlediği davranış ApplySettingsAsync yolunda da varsa testi o yola taşı; zaten başka bir testte kapsanıyorsa sil. Kapsam kaybı olmasın. Metoda atıf yapan yorumları da güncelle: SyncCoreAsync, ApplySettingsAsync ve SyncAfterRootChangeAsync özetleri, MainWindow'daki "Choose Folder" notu, RunViewModelStateTests ve StartupPathTests yorumları.

3. Tepsi menüsündeki Stop, derleme durdurulamazken (StopCommand.CanExecute false) pasif görünsün. Bugün her zaman tıklanabiliyor ve derleme yokken hiçbir şey yapmıyor. İlgili kod: AppTrayIcon ve MainWindow.OnSourceInitialized'daki StopRequested kablosu.

4. Bayat kod yorumlarını koda göre düzelt; davranış değişmez:
   - RunViewModel.BuildProjectAsync özeti "güncelse up to date atlanır" diyor. Gerçekte hedef her zaman derlenir (Core/Planning/ProjectRunScope, WillBuild = true; ARCHITECTURE §8.1). Contracts IpcMessages → StartRunCommand'ın ScopeProjectId dokümanındaki "Build modunda incremental kural" ifadesi de aynı şekilde düzeltilsin.
   - SettingsDialog.xaml General yorumu: "yalnız Pull before build bağlı, diğer dört anahtar taslak" → Pull before build ve Stash and switch branches bağlı, dört anahtar taslak.
   - UiStateStore: SeenVersion dokümanı (nokta artık ⓘ'de değil ✨ What's new düğmesinde, notlar NotesDialog'da) ve "ileride Settings/action-bar task'larının bağlayacağı yüzey" başlığı.
   - LatestPill ve BottomAnchorBehavior'daki "ileride event stream" ifadeleri; event stream bunları zaten kullanıyor.
   - "Açılışta otomatik Sync yok" diyen yorumlar: App.xaml.cs'teki autostart yorumu, MainWindow.StartInTray özeti, StartupArgs.StartInTray. Gerçekte motor hazır olunca Sync koşuyor (RunViewModel.OnEngineReady; ARCHITECTURE §12.1).
   - Kaldırılmış Continue'yu hâlâ anlatan yorumlar: MainWindow'daki F5 kablosu ve OnF5Pressed özeti, ActionBar.RefreshBuildArea, RunViewModel'deki "Stop/Continue butonları" notu. `Continue` için grep yap; yalnız bugünkü davranışı yanlış anlatanları düzelt, "[DEĞİŞEN KURAL]" tarihçe notlarına dokunma.

5. README.md → Settings adımındaki General paragrafını bugünkü gerçeğe göre düzelt: dört grup var (Startup, Build, Branches, Notifications); Pull before build ve Stash and switch branches çalışıyor; Start with Windows, Start minimized to tray, Close to tray ve Show notifications henüz bağlı değil. ARCHITECTURE §13.3 zaten doğru: kontrol et, doğruysa dokunma.

6. Test projesindeki 11 derleme uyarısını temizle: CS0028 (ExternalLayerTests.Main imzası), CS8600/CS8602 (ActionBarTests ~929), CS0219 retryChanged (RunViewModelStateTests ~1065), CS0067 (DsControlTemplateTests ~199), xUnit2013 (AboutDialogTests ~286), xUnit2029 (OsysIncrementalAcceptanceTests ~287-288, NotesDialogTests ~288), xUnit2031 (OptimizeDispatchTests ~169), xUnit1031 (EngineHostTests ~48). Uyarıyı pragma ya da NoWarn ile susturma, kodu düzelt.

Kurallar: CLAUDE.md geçerli. 3. madde davranış değiştirir, önce kırmızı testini göster. Kaldırmalarda onları pinleyen testler de birlikte güncellenir. 6 madde olduğu için önce kısa bir TDD dökümü yaz (.claude/outputs/). ReleaseNotes.cs'e dokunma; What's new ayrı bir çalışmada ele alınacak. Bitişte tam süit yeşil ve build uyarısız olmalı (tests dahil). Git: CLAUDE.md akışı, branch önerisi chore/ui-gap-cleanup.
```

### P2 — Build ▾ → Clean (tüm projeler)

```text
Alt bardaki Build split-button menüsünün Clean maddesini çalışır hâle getireceğiz. Bugün pasif çiziliyor ve tooltip'i "… — not available yet" diyor (BuildMenu.BuildRow/Invoke, AccessibilityNames.CleanSolutionTooltip / NotAvailableSuffix). Bağlam: .claude/outputs/2026-09-28-12-33-ui-function-inventory.md (§9 #60, §14-1) ve .claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md.

Karar (kullanıcı): satır menüsündeki Clean bir proje için ne yapıyorsa, Build menüsündeki Clean aynısını TÜM projeler için yapar:
- her projede msbuild -t:Clean çalışır, hiçbir şey derlenmez, cache'lere dokunulmaz;
- temizlenen her projenin build-state kaydı silinir, sonraki Build onu baştan derler;
- kapsam grafta ne varsa odur: harici projeler ve döngü (SCC) üyeleri dahil.
Kullanıcı -t:Clean'in ortak OutDir'e yazılmış izli çıktıları da sildiğini biliyor ve kabul etti (Visual Studio "Clean Solution" paritesi). Bakım kutusundaki Clean (bin/obj silen, MSBuild çağırmayan workspace reset) ayrı bir iş olarak aynen kalır.

Bilinen işler:
- App: menü maddesi yeni bir komuta bağlanır: StartRunCommand, Mode = RunMode.Clean, ScopeProjectId = null. Kapısı Build/Rebuild ile aynıdır (CanRebuildOrRetry). RunViewModel.ScopeFor'da Clean dalı yok, bugün Build'in kümesine düşüyor; açılış koreografisi ve kuyruk renkleri tüm projeleri kapsamalı. Pill "CLEAN" yazar (OperationLabel zaten var). Madde etkin çizilir ve hover alır; tooltip'ten "not available yet" kalkar. Artık kullanıcısı kalmayan NotAvailableSuffix de silinir.
- Motor: kapsamsız bir Clean'de döngü üyeleri bugün "in dependency cycle" diye önden atlanıyor (ReadySetScheduler ctor: _groups null iken InCycle pre-skip). Clean'de atlanmamalılar ve A6 kilitlenmesine de düşmemeliler. Clean hiçbir şey derlemediği için bağımlılık sırası gerekmez; nasıl çözüleceğini (ör. Clean modunda kenarları yok saymak) sen öner. Dep-issue yayılımı gibi derlemeye özgü kuralların Clean'e sızmadığını doğrula. Harici çalışma kopyaları Clean'de güncellenmez (ExternalUpdater Clean'i zaten hariç tutuyor, doğrula).
- Onay dialogu yok (satır Clean'i ve Visual Studio da sormuyor). Bitince Sync zincirlenmez; satırlar sonucu satır Clean'indeki gibi okur (never built). Koşu sırasında Stop çalışır.

Test: önce kırmızı. Pinlenecekler: menü maddesinin kapsamsız Mode=Clean komutu göndermesi, döngü üyesinin ve harici projenin de temizlenmesi, defter kaydının silinmesi, kapının Build ile aynı olması. Maddenin pasif olduğunu pinleyen mevcut testler davranış değiştiği için yeniden yazılır; doc'larına eski iddia ve değişme gerekçesi yazılır.
Doküman: ARCHITECTURE §13.2 ("the one surface in the bar whose engine is not written" cümlesi), §8.1 ve komutların anlatıldığı yerler; README'deki Build/Rebuild adımı ("No engine behind it yet…"); Contracts IpcMessages'taki RunMode.Clean dokümanı ("Bugün yalnız satır menüsünden…"); CleanWorkspaceCommand dokümanındaki -t:Clean/OutDir gerekçesinin yalnız bakım Clean'ine ait olduğu netleşsin. ReleaseNotes.cs'e dokunma. Git: CLAUDE.md akışı, branch önerisi feat/build-menu-clean-all.
```

### P3 — Close to tray, güvenli tam kapanış ve bildirimler

```text
Settings → General'daki iki anahtarı gerçekten çalışır hâle getireceğiz: Close to tray ve Show notifications. Bugün ikisi de yalnız taslakta yaşıyor (GeneralSettings.cs, SettingsDraftViewModel.GeneralGroups): kaydedilmiyor, dosyaya yazılmıyor, hiçbir davranışı etkilemiyor. Bağlam: .claude/outputs/2026-09-28-12-33-ui-function-inventory.md (§10 #64/#67, §13, §14-3) ve .claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md.

Kararlar (kullanıcı):

1. Close to tray (varsayılan açık)
   - Açık → bugünkü davranış: × pencereyi tepsiye gizler.
   - Kapalı → × uygulamayı tamamen kapatır. Alt+F4 ve sistem menüsü Kapat da aynıdır (hepsi MainWindow.OnClosing'den geçer).

2. Güvenli tam kapanış. Kullanıcının sözü: "uygulama exe tamamen kapanırken arka planda hiçbir şey yarım çalışıyor durumda kalmamalı". Bu kural kullanıcının başlattığı her tam çıkış için geçerli: × (anahtar kapalıyken) ve tepsi → Exit.
   - Derleme sürüyorsa önce graceful Stop yapılır (yeni proje başlamaz, uçuştakiler post-build copy dahil biter), drain bitince çıkılır. Bugün tepsi → Exit derleme sürerken MSBuild'leri anında öldürüyor (App.OnExit → AppShutdown 2 sn → KILL_ON_JOB_CLOSE); bu değişecek.
   - Sync / Clean / Optimize / checkout / pull sürüyorsa bitmesi beklenir; yarıda kesilen bir Clean ya da git işlemi diskte yarım iş bırakır.
   - Beklerken pencere görünür kalır. Tepsi → Exit gizliyken basıldıysa pencere öne gelir. Şerit "Stopping…" gösterir, konsola uygulamanın iş bitince kapanacağını söyleyen tek satır düşer; iş bitince uygulama kendiliğinden kapanır. İkinci × ya da ikinci Exit ikinci bir durdurma üretmez.
   - Motor yanıt vermezse (mevcut sessizlik bekçisi) sonsuza dek beklenmez: çıkılır ve Job Object kalan her şeyi kapatır.
   - Çıkıştan sonra hiçbir BuildOrchestrator, Supervisor ya da MSBuild process'i kalmaz; bunu bir testle ya da ölçümle göster.
   - Windows oturumu kapanırken (SessionEnding) bugünkü anında çıkış korunur.

3. Show notifications (varsayılan açık). Kapalıyken HİÇBİR OS balonu gösterilmez:
   - koşu bitti (AppTrayIcon.ShowRunFinished, TrayBuildIndicatorController),
   - ilk × kapatmadaki "hâlâ tepside" balonu (ShowClosedToTrayNotification),
   - ikinci açılıştaki "zaten açık — öne getirilemedi" balonu (App.OnStartup; bu ayrı bir process, ayarı ui-state.json'dan okur).
   Balon bastırıldığında "bir kez gösterildi" bayrağı (UiState.TrayBalloonShown) harcanmaz. Sağ alttaki animasyonlu tepsi logosu (TrayBuildOverlayWindow) bildirim değil gösterge; bu ayardan etkilenmez.

4. Kalıcılık: iki anahtar Save'de ui-state.json'a yazılır; diyalog her açılışta kayıtlı değeri gösterir; Clear varsayılana döndürür (mevcut kural). Export/Import dosyası bu anahtarları taşır; dosyada anahtar yoksa mevcut değer korunur (mevcut kural). Değer değişince konsola tek satır not düşer (Pull before build / Stash deseni). Bu kalıcılık deseni bir sonraki işte Start with Windows için de kullanılacak; ona göre tek yerde kur.

Test: her davranış için önce kırmızı. Zor olanlar: kapanış sırası (Stop → drain → shutdown), ikinci × ile idempotency, motor yanıtsızken çıkış, üç bildirim yolunun bastırılması. Headless ortamda gerçek tepsi ve balon kurulamaz; mevcut seam'leri kullan (ITrayRunNotifier, FirstCloseBalloonGate, IUiStateStore). CLAUDE.md geçerli; 5'ten fazla madde var, önce kısa bir TDD dökümü yaz.
Doküman: ARCHITECTURE §12.3 (tepsi, balon, × davranışı), §13.3 (General), §20 ("Four General switches are not wired yet" satırı: bu iş sonrası yalnız Start with Windows ve Start minimized kalır); README'nin General paragrafı ve tepsi/kapanış bölümü. ReleaseNotes.cs'e dokunma. Git: CLAUDE.md akışı, branch önerisi feat/close-to-tray-and-notifications.
```

### P4 — Windows ile başlama (Start with Windows + Start minimized to tray)

```text
Settings → General'daki Start with Windows ve Start minimized to tray anahtarlarını çalışır hâle getireceğiz. Altyapının çoğu var ama arayüze bağlı değil: App.OnStartup her açılışta UiState.Autostart'a göre HKCU\Software\Microsoft\Windows\CurrentVersion\Run değerini yazıyor ya da siliyor (AutostartService, RegistryAutostartRegistry, App.AutostartCommand → "<exe>" --autostart). StartupArgs, --autostart'ı StartInTray yoluna yönlendiriyor (pencere gösterilmeden tepside). Ama UiState.Autostart'ı yazan tek satır yok; iki anahtar yalnız taslakta. Bağlam: .claude/outputs/2026-09-28-12-33-ui-function-inventory.md (§10 #62-#63, §13 #101, §14-4) ve .claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md. Close to tray işinde kurulan General anahtarı kalıcılık desenini kullan.

Kararlar (kullanıcı):

1. Start with Windows açık → Windows oturumu açılınca uygulama kendiliğinden başlar; kapalı → başlamaz. Windows kaydı Save anında güncellenir, yeniden başlatma beklenmez. Diyalog her açılışta gerçek durumu gösterir.

2. Start minimized to tray (yalnız Start with Windows açıkken etkin; bu bağımlılık UI'da zaten var):
   - Açık → Windows ile açılışta pencere görünmez, uygulama sağ alttaki tepside gizli başlar (bugünkü --autostart davranışı).
   - Kapalı → Windows ile açılışta pencere normal açılır.
   - Elle açılış (kısayol, exe) her zaman pencereyi gösterir; bu anahtar yalnız Windows ile başlamayı etkiler.

3. Windows tarafında gereken her şey yapılmalı (kullanıcının sözü). En az:
   - HKCU Run değeri (admin yok), tırnaklı exe yolu. Exe taşınırsa her açılışta yeniden hizalanır (mevcut).
   - Görev Yöneticisi → Başlangıç uygulamaları: uygulama orada doğru adla görünmeli. Kullanıcı orada devre dışı bırakırsa (HKCU\...\Explorer\StartupApproved\Run) Settings bunu doğru göstermeli. O durumda Save'de anahtar açılırsa ne olacağını öner ve kullanıcıya sor.
   - Tek instance: Windows ile açılan uygulama zaten açık bir uygulamaya çarparsa mevcut ikinci-instance kuralları geçerli.
   - Windows ile açılışta da, bugünkü gibi, motor hazır olunca bir Sync (git fetch + analiz) koşar (RunViewModel.OnEngineReady; ARCHITECTURE §12.1). Kullanıcı aksini söylemedikçe korunur.
   - Mekanizma önerisi: registry komutu "Windows ile açıldım" işareti olarak --autostart taşımaya devam etsin; gizli ya da görünür kararını açılışta ui-state.json'daki ayar versin (tek doğruluk kaynağı). Daha iyisini önerirsen gerekçelendir.

4. Kalıcılık: Save → ui-state.json. Export/Import dosyası iki anahtarı taşır; import edilen bir dosyayla Save'e basmak o makinede autostart'ı açar, bu bilinçli. Clear varsayılana döndürür.

Test: önce kırmızı. Testler gerçek registry'ye asla yazmaz (IAutostartRegistry fake — mevcut seam). Açılış yolu kararı (StartupArgs / route) saf kalsın. CLAUDE.md geçerli.
Doküman: ARCHITECTURE §12.1 (açılış yolları), §12.3 (autostart), §13.3 (General), §16 (diskteki durum), §20 (Known limits'teki General anahtarları satırı kalkar); README'nin General paragrafı ve "State on disk" bölümündeki "Autostart, when enabled…" cümlesi. ReleaseNotes.cs'e dokunma. Git: CLAUDE.md akışı, branch önerisi feat/start-with-windows.
```

### P5 — What's new / sürüm notları

```text
What's new penceresi (başlık çubuğundaki ✨ düğmesi, NotesDialog) hazır ama içeriği ve süreci üzerinde hiç çalışmadık. App/Services/ReleaseNotes.cs'teki notlar örnek ve birçoğu artık kodla uyuşmuyor. Bu bir tasarım ve içerik çalışması: kod yazmadan önce benimle konuş (superpowers:brainstorming). Karar vermeden dosya yazma. Bağlam: .claude/outputs/2026-09-28-12-33-ui-function-inventory.md (§16) ve .claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md.

Konuşulacaklar:
- Notlar nerede yaşasın? Bugün C# içinde bir liste (ReleaseNotes.All, tek sürüm girdisi; sürüm AppIdentity.Version ← Directory.Build.props). Alternatifler: gömülü bir markdown ya da JSON dosyası, CHANGELOG.md'den üretmek vb. Her birinin artısını ve eksisini göster.
- Sürümleme: sürüm numarası ne zaman ve nasıl artar (merge başına mı, elle mi)? Tarih nereden gelir? INSTALLED çipi ve görülmemiş-sürüm noktası (UiState.SeenVersion) bu modele nasıl oturur?
- Süreç: bir değişiklik main'e girerken notu kim ve ne zaman yazar? CLAUDE.md'ye kural olarak eklenecek mi? Notların kod davranışıyla uyumunu koruyan bir guard testi olsun mu?
- Dil ve üslup: kategoriler (Added / Changed / Performance / Fixed / Removed), cümle uzunluğu, iç terim kullanmama.

İçerik düzeltmesi (tasarım netleşince): mevcut her satırı O ANKİ koda göre tek tek doğrula. 35ddc41'de doğrulanmış bilinen yanlışlar (ReleaseNotes.cs satırları):
- ~100: Build menüsünde Clean var diyor. Build ▾ Clean işi bittiyse artık doğru olabilir.
- ~105 ve ~141: etiketlerde "retry" ve süre (2h gibi) yok.
- ~111: revizyon proje sayfasından da kaldırıldı.
- ~114: başlangıç hâli Sync'ten önce görünür, sonra değil.
- ~125: Sync'ten sonra satırlar renkli.
- ~135: gerekçe kaldırılmış commit çiftine dayanıyor.
Settings anahtarları işi bittiyse ~95'teki "take effect in a later version" ifadesi de değişir.

Bu çalışma en sona planlandı: Build ▾ Clean ve Settings anahtarları işleri bittikten sonra. Git: CLAUDE.md akışı.
```
