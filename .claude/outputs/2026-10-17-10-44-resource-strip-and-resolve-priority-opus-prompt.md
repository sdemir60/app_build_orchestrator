# Opus oturumu için prompt — Resolve önceliği kuralının kaldırılması + title bar kaynak şeridi (tasarım v1.25.0)

Aşağıdaki metni yeni oturumda ilk mesaj olarak yapıştır. Ana proje (`D:\Projects\Other\Apps\app_build_orchestrator`), `develop`
üzerinde, uygulama kapalıyken başla.

---

İki işi uygulayacaksın; kararlar verildi, senin işin planlamak, kodlamak, test etmek, dokümanı güncellemek ve merge etmek.
Önce şunları tamamen oku, sonra kök `CLAUDE.md`'yi:

1. Bu brief (aşağısı) — NE ve NEDEN; her karar burada, tahmin yürütme.
2. `.claude/outputs/2026-10-17-10-06-resolve-priority-and-resource-strip-decisions.md` — ölçüm raporu ve kararların gerekçesi
   (§3 ölçüm, §4 kararlar, §8 düzeltmeler). Bu brief ile çelişirse brief geçerlidir.
3. `.claude/outputs/2026-10-15-16-54-design-v1.25.0/README.md` — tasarım paketi. Şerit için yalnız §2.1 (title bar / kaynak
   şeridi), §3.6 (örnekleme ve makineye uydurma), §4 (state), "İstenmeyenler" bölümü (kaynak şeridi maddesi) ve en alttaki
   **v1.25.0** sürüm notu esastır. `prototype/app/BuildApp.jsx` içindeki `ResourceStrip`, `ResCell`, `I.cpu`, `I.memory`,
   `I.parallel` yalnız görsel referanstır; kod kopyalanmaz. README'nin başka bölümlerinde Resolve'un "ardışık koşar" gibi eski
   iddiaları var; onlar bu işin konusu değildir, motor davranışı ARCHITECTURE.md'dedir.
4. ARCHITECTURE.md: §4.1-4.2 (process topolojisi, job'lar), §8.1 (run modları), §11 (kaynak yönetimi, tamamı), §12.2 ve
   §13.2'nin title bar kısmı, §13.3 (General sayfası), §14.1/14.2/14.4 (token, tipografi, ikonografi), §17 (test kuralları),
   §22 (kod haritası).

Planı `superpowers:writing-plans` ile yaz, `superpowers:subagent-driven-development` ile (yoksa `superpowers:executing-plans`)
görev görev uygula. İki iş iki ayrı branch'tir ve sırayla gider: önce İş 1, merge; sonra İş 2, merge.

## Değişmeyen şeyler (dokunma)

- Sağ alttaki perf çipi (Full/Balanced/Light döngüsü, tooltip'siz) aynen kalır.
- Paralellik koşu başında sabitlenir (`WorkerBudget.Clamp`, `runStarted.Parallelism`); koşu ortasında işçi sayısı
  değiştirilmez. Dinamik yeniden ayar YOK.
- Copy floor ve drain kuralı (§11.3) değişmez.
- Motorun stderr `memory:` satırı ve `MemoryLine`/`MemoryTrim` değişmez.
- `CHANGELOG.md` ve `Directory.Build.props` → `Version` DOKUNULMAZ; sürüm ayrı iştir (ikisi bitince minor, 1.11.0, kullanıcı
  ister).
- Tasarımın açıkça istemediği şeyler yapılmaz: şeritte metin etiketi yok, CPU'ya minik bar yok, cap şeridin metninde yok,
  değer değişiminde animasyon yok, `2/4` için uyarı rengi yok, mod sözcüğü tıklanmaz, şeride hover zemini yok, açık tema yok.

---

## İş 1 — `fix/resolve-follows-profile`: "Resolve cycles at full priority" ayarı ve kuralı kaldırılır

### Bugün

Bir `Cycles` (Resolve cycles) koşusu, Settings → General → Build → *Resolve cycles at full priority* açıkken (varsayılan
açık) seçili profilin işçi sayısını korur ama cap'ini kaldırıp priority'yi Normal'e çeker. Kural tek saf fonksiyondur,
`PerfProfile.ForRun(mode, profile, resolveAtFullPriority)`; Supervisor koşu başında ve her canlı `setPerfMode`'da uygular;
App aynı dönüşümle konsol notunu yazar (`PerfNoteText.ResolveNote`). Ayar `startRun` ile motora gider
(`StartRunCommand.ResolveAtFullPriority`), `ui-state.json`'da ve dışa aktarılan settings dosyasında saklanır.

### Neden kalkıyor (ölçüm, 2026-10-17, gerçek OSYS, kurulu v1.10.1, kare sondası `BO_PROBE_FRAMES`)

| Koşu (Balanced, 4 işçi, UI.DMS 17 üyeden 14'ü + upstream = 25 proje) | Süre | 250 ms üstü kare boşluğu | En uzun boşluk |
|---|---|---|---|
| Ayar açık (cap yok, Normal), sakin makine | 56,5 sn | 0 | 48 ms |
| Ayar kapalı (cap %70, BelowNormal), sakin makine | 52,4 sn | 0 | 44 ms |
| Ayar açık, yüklü makine (3 Normal busy loop + VS) | 143,8 sn | 23 (8 pencere) | 5172 ms; ~20 sn boyunca 0,3-20 fps |
| Ayar kapalı, yüklü makine | 151,0 sn | 2 | 1643 ms |

Sakin makinede kazanç yok, yüklü makinede %5 hız karşılığında arayüz saniyelerce kilitleniyor (App'in CPU payı o pencerelerde
0: UI thread hiç sıra alamıyor). ARCHITECTURE §11.1'deki 1,9× gerekçesi bu yük deseninde görülmedi. Kullanıcının gördüğü
"tur ortasında, makine yoğunken donma" tam bu. Ayrıca tek düğme ilkesi: cap ve priority'yi yalnız perf çipi belirler.

### Yeni kural

**Bir `Cycles` koşusu, profilin cap'ini ve priority'sini aynen uygular; Build, Rebuild ve Clean'den hiçbir farkı yoktur.**
Hızlı Resolve isteyen kullanıcı çipten Full seçer. Konsol notu sıradan koşu notudur (`parallelism: <n> · cpu cap <p>%`,
Full'de `cpu cap off`); koşu ortası çip değişimi Resolve'da da sıradan notu yazar. "Cap'siz koşu" artık yalnız Full'dür.

### Kaldırılacaklar (bugünkü `develop` 76c9267e'ye göre; satırlar kaymış olabilir, grep ile doğrula:
`ResolveAtFullPriority|resolveAtFullPriority|ForRun\(|ResolveNote\(`)

- Core: `ProcessControl/PerfProfile.cs` → `ForRun`; `ProcessControl/PerfNoteText.cs` → `ResolveNote`, üç parametreli
  `Note(mode, profile, bool)` aşırı yüklemesi ve yalnız onların kullandığı `PriorityValue` (başka kullanıcı varsa kalır).
- Contracts: `Ipc/IpcMessages.cs` → `StartRunCommand.ResolveAtFullPriority` parametresi ve doc'u. Eski App'lerin yazdığı
  alan JSON'da gelirse yok sayılır (System.Text.Json bilinmeyen alanı atar; test et).
- Supervisor: `RunCoordinator.cs` → `ApplyPerfLocked` ve koşu başı yolundaki `ForRun` çağrıları; profil doğrudan uygulanır.
- App: `ViewModels/GeneralSettings.cs` → `GeneralSetting.ResolveAtFullPriority` enum değeri ve katalog satırı (BUILD grubunda
  yalnız *Pull before build* kalır); `ViewModels/SettingsDraftViewModel.cs` → özellik, ctor parametresi, Apply/Import/Export/
  State yolları, `_generalRows` aboneliği; `ViewModels/SettingsFile.cs` → `resolveAtFullPriority`; `Shell/UiStateStore.cs` →
  `ResolveAtFullPriority`; `ViewModels/RunViewModel.cs` → `_resolveAtFullPriority`, `_runPerf` demetinin bu üyesi,
  `CyclePerfAsync`/`OnRunStarted`'daki Resolve notu dalı; `ViewModels/RunViewModel.ActionBar.cs` → `ApplyResolveAtFullPriority`
  ve Save parametresi (konsola yazdığı "<label> on/off — …" satırları dahil); `MainWindow.xaml.cs` → yükleme/kaydetme satırları
  (`saved.ResolveAtFullPriority`, `s.ResolveAtFullPriority`); `Views/SettingsDialog.xaml.cs` → ctor argümanı.
- Testler: 39 dosyada ~60 referans. **Silinmez, yeni kuralı pinleyecek biçimde yeniden yazılır** ve doc'una eski iddia + bu
  ölçüm gerekçesi yazılır. Kırmızı önce: "Balanced'da bir `Cycles` koşusu inner job'a cap 70 ve BelowNormal yazar" testi bugünkü
  kodda KIRMIZI olmalı (bugün cap yok/Normal yazıyor); "startRun bu alanı taşımaz" ve "General kataloğunda bu satır yoktur"
  pinleri eklenir. Perf tablosu guard'ları ve `PerfNoteText` testleri sıradan nota göre düzeltilir.
- Göç kodu YAZILMAZ: `ui-state.json` ve settings dosyasındaki eski alan, ARCHITECTURE §16'nın "eski sürümün yazdığı, bu sürümün
  okumadığı alan yok sayılır" kuralıyla düşer; bunu bir testle göster (alanı içeren eski bir dosya hatasız yüklenir).

### Doküman (aynı işte, anlatı üslubu, changelog gibi yazma)

- ARCHITECTURE.md: §5.2 (`startRun` paragrafındaki `resolveAtFullPriority` cümlesi), §8.1 ("A `Cycles` run is also the one run
  that does not take…" paragrafı kalkar), §11.1 (tablonun altındaki Resolve notu ve "A Resolve cycles run takes full priority"
  paragrafı → yeni kural + ölçüm gerekçesi: sakin makinede kazanç yok, yüklüde %5, bedeli saniyelik kare boşlukları; mid-run
  switch cümlesindeki Resolve istisnası kalkar), §11.3 ("a run with no cap — Full" yalnız Full), §13.3 (General sayfası
  satır listesi ve "travels with every startRun" cümlesi), §16 (ui-state alan listesi), §17.5 ("under a real, full-priority
  run" ifadesi → "under a run with no cap (Full)"), §22 (kod haritası satırı: perf tablosu + copy floor, Resolve kuralı
  çıkar).
- **Full için bir cümle (karar K2):** §11.1'in "Memory, not cores" paragrafına CPU tarafı eklenir: Full'de derleyiciler arayüzle
  eşit önceliktedir ve yüklü makinede saniyelik kare boşlukları ölçüldü; Balanced varsayılan budur. Arayüzde uyarı YOK.
- README.md: "Performance modes" bölümü (Resolve paragrafı kalkar, Full cümlesi eklenir) ve ayarlar listesindeki
  *Resolve cycles at full priority* ifadesi.

### Bitiş

Tam süit yeşil (token/motion/D8 guard'ları dahil) → `develop`'a merge + push → merge doğrulanınca branch local ve remote'tan
silinir. CI'ı kontrol et (`ci.yml`); ortam kaynaklı düşen test varsa yeniden üretip `LocalOnly` kuralına göre davran.

---

## İş 2 — `feature/resource-strip`: title bar'ın ortasında kaynak şeridi

### Ne

Title bar'ın TAM ORTASINDA, pencere genişliğine göre mutlak ortalanmış (sol logo kümesi, sağ küme ve Update hapı onu
oynatmaz), tıklanmayan, hover zemini olmayan, üç hücreli bir şerit: **CPU · RAM · paralellik + mod**. Aracın makineye o anki
maliyetini gösterir: uygulama + motor + derleyici süreçleri birlikte. Görünüm birebir tasarım README §2.1: hücre arası 14px;
hücre = 12px Lucide ikon (`text-faint`, stroke 1.8; *cpu*, *memory-stick*, üç dik çizgi) + 5px + değer Geist Mono 11px tabular
`text-dim`; mod sözcüğü Geist 11px `text-faint`, sayıdan 5px sonra, `/4` bir ton soluk. Etiket yok; anlam tooltip'te, aşağı
açılır. Sabit çerçeve: her hücre en geniş değer kadar yer ayırır (`100%` · `12.3 GB` · `12/12 Balanced`), değer ikonun yanında
sola hizalı; değişimde kayma ve animasyon yok. Okunamayan değer `—` (aynı genişlik). İkon geometrileri `Resources/Icons.xaml`'a
eklenir (§14.4 kuralları; prototipteki path'ler referans).

### Veri nereden gelir (karar K3) — motora IPC EKLENMEZ

- Topoloji: App, outer job'ın sahibidir ve üyesi değildir (`EngineHost._outerJob`, `EngineHost.OuterJob`); Supervisor, onun
  başlattığı git/vswhere ve inner job'daki `MSBuild.exe` + csc hepsi outer job'ın içindedir (§4.1-4.2). Dolayısıyla "motor +
  derleyiciler" tek job sorgusuyla okunur:
  - CPU: `QueryInformationJobObject(JobObjectBasicAccountingInformation)` → `TotalUserTime + TotalKernelTime` (ölen süreçlerin
    zamanı da dahil; iki örnek arasındaki fark doğru sonuç verir).
  - Bellek: `QueryInformationJobObject(JobObjectBasicProcessIdList)` → PID listesi → her PID için
    `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `GetProcessMemoryInfo` (`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`) +
    `CloseHandle`. Motor = `EngineHost.EnginePid`'in süreci; `compilers` = job toplamı − motor (Sync sırasında git de buraya
    düşer; kabul edilen basitleştirme, dokümana bir cümle).
  - App'in kendisi: `GetProcessTimes` (ya da tek bir `Process` nesnesinde `Refresh` + `TotalProcessorTime`) ve bellek için
    `MemoryLine.Read().PrivateBytes` ile AYNI okuma (kopya yazma).
- CPU yüzdesi = (ΔApp + ΔJob işlemci zamanı) / (Δduvar saati × `Environment.ProcessorCount`), sıfırdan uzağa yuvarlanmış tam
  sayı; `%100` tavan. RAM = üç private bytes toplamı.
- Zamanlama: `DispatcherTimer`, UI thread'de (iş mikrosaniyeliktir, ayrı thread gerekmez). **İlk okuma açılıştan 0,5 sn
  sonra**, sonra 3 sn'de bir. Pencere gizliyken (tepsi; `HiddenSurface` sinyali, `MainWindow.HiddenSurface.cs`) ya da
  küçültülmüşken (`WindowState.Minimized`) timer DURUR; görünür olunca hemen bir örnek alınır ve 3 sn'lik ritim yeniden başlar.
- Motor uyurken (§4.7, `EngineState` Asleep) job boştur: motor ve derleyici değerleri **0**'dır (`engine 0 MB · compilers 0 MB`),
  şeritteki RAM yalnız App'tir; "asleep" gibi bir yazı YOK. Okuma Win32 seviyesinde başarısız olursa o hücre `—`.
- Maliyet: 3 sn'de 2 job sorgusu + 5-8 `OpenProcess`; mikrosaniyeler. `Process.GetProcesses()` ya da makine çapında süreç
  taraması YASAK (kaynak guard'ı yaz). Örnek başına tahsis: ısınma sonrası sıfır bayt (PID listesi için yeniden kullanılan
  buffer); yalnız üç kısa metin üretilir.
- Nerede durur: saf parçalar Core'da (`Core/ProcessControl/` ya da `Core/Diagnostics/`): yüzde hesabı, bellek metni, paralellik
  metni, tooltip metinleri — süreç okumayan, birim test edilen fonksiyonlar. P/Invoke eklemeleri yalnız
  `Core/ProcessControl/NativeMethods.cs`'e (Core'daki TEK P/Invoke yeri); job sorguları `JobObject`'e yeni metotlar olarak
  (`QueryAccounting`, `QueryProcessIds`; `QueryCpuRate` deseni). App'te örnekleyici servis (`App/Services/`), VM'de şeridin
  bağlanacağı özellikler, `MainWindow.xaml`'de şerit.

### Biçim kuralları (karar K5)

- RAM private bytes'tır: About satırları ve motorun `memory:` satırıyla aynı kural, tek kaynak `MemoryLine`. Task Manager'ın
  "private working set" sütunundan biraz farklı çıkabilir; dokümana bir cümle.
- 1 GB altı tam MB (`310 MB`, sayı `MemoryLine.Megabytes` ile), 1 GB ve üstü bir ondalık, invariant nokta (`4.8 GB`). Bu GB
  kuralı tek yerde yazılır (`MemoryLine` yanına ya da yeni saf sınıfa) ve testlenir.
- CPU `1%` · `69%` · `100%`.

### Üçüncü hücre: paralellik + mod (karar K4)

- Pay = o an gerçekten derlenen proje sayısı; VM'nin mevcut building sayısından (`RunCounters`'ın `building` sayacıyla AYNI
  kaynak) gelir, yeniden sayılmaz. Canlıdır, örneklenmez.
- Boşta: `0/N Mod` — N = çipin modunun tablo değeri (`PerfProfile.For(mode).Parallelism`: Full 6 · Balanced 4 · Light 2), mod
  sözcüğü çipin modu (`RunViewModel.PerfMode`).
- Koşarken hücre KOŞUYU anlatır: payda = koşu başındaki modun tablo değeri, mod sözcüğü koşu başındaki mod; koşu ortasında çip
  değişirse hücre bir sonraki koşuda değişir (işçi sayısı zaten koşu başında sabittir; konsol notu ve CPU hücresi canlı değişimi
  gösterir). Koşu bitince hücre tekrar çipi okur.
- Kısılmış koşu: `runStarted.Parallelism` tablo değerinin altındaysa pay zaten onu aşamaz (ör. `2/4 Balanced`); tooltip
  `2 of 4 — reduced: <reason>`; `<reason>` motorun gönderdiği `workersReducedReason` metnidir (tek kaynak `PerfNoteText`;
  tasarımın `low memory` örneği bu metnin bir halidir, ayrı bir çeviri tablosu yazma). Renk yok, uyarı yok.
- Satırdan tek proje derlemesi: `1/N Mod` (pay 1, payda ve mod çipten). Resolve'da özel bir şey yok.
- Tooltip'ler: boşta `Balanced — up to 4 projects build in parallel` · derlerken `3 of 4 projects building in parallel` ·
  kısılmışken yukarıdaki. Mod sözcüğünün ayrı tooltip'i yok.

### Tooltip'ler (karar K6)

- CPU: `CPU used by Build Orchestrator — app, engine and compilers · cap 70%` (Balanced), `cap 40%` (Light), Full'de ek yok. Cap
  modun tablo değeridir (`PerfProfile.For(mode).CpuCapPercent`); copy floor penceresindeki geçici yükselme ve koşu ortası
  değişim şeritte gösterilmez (konsol notu zaten yazar).
- RAM: `Memory used by Build Orchestrator · app 310 MB · engine 85 MB · compilers 4.4 GB` (motor uyurken `engine 0 MB ·
  compilers 0 MB`).
- Aşağı açılır (`ToolTipService.Placement=Bottom`); mevcut tooltip stilini kullan. Bu makinede `MenuDropAlignment` sağa hizalar
  (bkz. `Controls/PopoverPlacement.cs`); tooltip hücrenin altında kalıyorsa yeterlidir, özel hizalama gerekmiyorsa ekleme.
- Şerit `WindowChrome.IsHitTestVisibleInChrome=True` alır (yalnız tooltip için); title bar sürüklemesi bozulmamalı (realize
  testi + elle doğrulama).

### Yerleşim

`MainWindow.xaml` → `TitleBarRow` (Border) içindeki `DockPanel` sol kümeyi (`TitleBarLogoLock`) ve sağ kümeyi (`UpdatePillSlot`,
layout düğmeleri, Settings/What's new/About, caption düğmeleri) taşır. Şerit bu akışa girmez: içeriği bir `Grid`'e sarıp şeridi
aynı hücrede `HorizontalAlignment=Center` ile üstte (Z-order) konumlandır; sol/sağ kümeler ve Update hapı onu oynatmaz.
Minimum genişlik 1240'ta Update hapı görünürken sağ küme ile çakışmadığını realize testiyle pinle (sol lock ≈250px, şerit
≈175px merkezde, sağ küme ≈450px → sığar; ölç, varsayma). Yükseklik `Size.TitleBarHeight` token'ından.

### About → Environment (karar K7)

`Services/DiagnosticsReport.cs` RUNTIME grubundaki üç görünür satır (`App private` · `GC committed` · `Managed heap`,
2026-10-13'te eklendi) About penceresinden kalkar; **kopyalanan tanı metninde** üç değer kalır (hata bildirimi için). Model dialog
ve kopya için ortaksa görünür/yalnız-kopya ayrımını tek yerde, etiketler tek kaynakta kalacak biçimde yap. `AboutDialog`
`MemoryLine.TryRead()` okumasını kopya için sürdürür. İlgili testler yeni kurala göre yeniden yazılır.

### Testler (hepsi, kırmızı önce)

- Birim (Core): yüzde hesabı (sınırlar, `%100` tavan, sıfır aralık), MB/GB biçimi (1023 MB → `1023 MB`, 1 GB → `1.0 GB`,
  yuvarlama), paralellik metni ve tooltip metinleri (boşta / derlerken / kısılmış / tek proje), reason'ın motor metninden
  geçmesi.
- VM: koşu başında payda ve mod sözcüğü koşunun modunu alır, koşu ortası çip değişimi hücreyi değiştirmez, koşu bitince çipe
  döner; pay `RunCounters.building` ile aynı; `runStarted.Parallelism` kısıldığında tooltip; motor Asleep iken motor/derleyici 0.
- Örnekleyici: gizli yüzeyde timer durur, görünür olunca hemen örnek alır; ilk örnek 0,5 sn; başarısız okuma `—`; ısınma sonrası
  sıfır tahsis pini (`PerfMeasure.AllocatedBy` deseni, gate'siz).
- Realize (headless süit XAML runtime'ı görmez): şerit `window.Content` üzerinde realize edilir; üç hücre, ghost genişlikleri,
  merkezleme, 1240 + Update hapı ile çakışmama, `IsHitTestVisibleInChrome`.
- Kaynak guard'ları: `Process.GetProcesses(` App ve Core'da yok; şerit XAML'inde renk/ölçü literal'i yok (token guard); değer
  değişiminde animasyon yok (motion guard mevcut desenine uy); D8 (`Thread.Sleep` ile yoklama yok).
- Tüm yeni test adları ve kategorileri §17 kurallarına uyar; ölçüm/sonda testi ekliyorsan ortam değişkeni kapısı
  (`[SkippableFact]` + `Skip.IfNot`).

### Doküman (aynı işte)

- ARCHITECTURE.md: §12.2/§13.2 title bar anlatımı ve ASCII yerleşim satırı (şerit eklenir; "the brand alone" ifadesi düzelir),
  §11.1 (şerit koşu başındaki kırpmayı gösterir; `workersReducedReason` artık tooltip'e de gider), §11.2 (App job'da değildir
  cümlesinin yanına: App outer job'ı yalnız OKUR, şerit için), yeni alt bölüm "Resource strip" (veri kaynağı, formül, 3 sn /
  0,5 sn, gizliyken durma, private bytes ve Task Manager farkı, git'in compilers'a düşmesi, uyuyan motor 0), §14.4 (üç yeni
  ikon), §16 (yeni state yok — belirt), §17 (yeni pin ve guard'lar), §22 (kod haritası satırları), About satırlarının
  kalkması (§13.3 About anlatımı ve §22'deki "three memory rows" satırı).
- README.md: arayüz tanıtımındaki title bar cümlesi + "Performance modes" bölümüne şeridin ne gösterdiği (kısa; ayrıntı
  ARCHITECTURE'da).
- `CHANGELOG.md` dokunulmaz.

### Bitiş

Tam süit yeşil → `develop`'a merge + push → branch silinir → CI kontrolü. Gerçek uygulamada gözle doğrula (Release build,
uygulama kapalıyken derle): boşta `0/4 Balanced`, Build sırasında pay canlı değişiyor, tepsiye alınca örnekleme duruyor
(bir sayaçla ya da logla göster), Update hapı varken şerit yerinden oynamıyor.

---

## İş 1'den sonra doğrulama ölçümü (isteğe bağlı ama planda)

Kaldırma merge olduktan sonra, kullanıcı makineyi KULLANMIYORKEN ve `devenv` kapalıyken (başlamadan önce chat'te sor ve
`Get-Process devenv` + son girdi zamanını kontrol et), 2026-10-17 kampanyasının yüklü kurgusunu bir kez tekrarla:
`.claude/temp/resolve-priority-2026-10-17/campaign2.ps1`'i `campaign3.ps1` olarak kopyala; `ResolveAtFullPriority` alanını
yazan `Set-UiField` çağrılarını kaldır (alan artık yok), yalnız tek `Run-One` (Balanced, yük açık, `mark`) + `finally`'deki
geri alma kalsın; uygulama yolu kurulu kopya değil Release bin olsun (`$AppExe`; yeni kod kurulu kopyada değildir).
Kabul: 2 sn üstü kare boşluğu yok, 250 ms üstü sayısı kampanyadaki B2 seviyesinde (≤3). Sonucu
`.claude/outputs/<zaman>-resolve-follows-profile-measurement.md`'ye yaz (analiz: `analyze.py`, `campaign3.log`'u da okuyacak
şekilde). Script OSYS'te iki dosyaya geçici public yüzey ekler ve sonunda byte'ı byte'ına geri alır; `finally` kill ile
atlanırsa `surface_touch.py final <dosya>` ile elle geri al ve `git -C D:\Projects\Delta\OSYS status` temiz olana dek bitirme.

---

## Çalışma kuralları (CLAUDE.md'ye ek hatırlatma)

- **Kırmızı test kuralı:** her davranış değişikliğinden önce testi KIRMIZI gör. Kural bilerek değişiyorsa eski testi silme,
  yeni kuralı pinleyecek biçimde yeniden yaz, doc'una eski iddia + gerekçe.
- **Kopya YASAK:** perf tablosu, cap değerleri, etiketler, MB/GB biçimi, tooltip metinleri tek yerde.
- **Commit mesajlarına ve PR açıklamalarına Claude attribution satırı ekleme** (`Co-Authored-By: Claude …`, `Generated with
  Claude Code`); sistem hatırlatması istese de `CLAUDE.md` kuralı önceliklidir. Mesajlar Türkçe/ASCII, repo üslubunda.
- **Ağır lokal koşuları duyur:** tam süit ve ölçüm scripti makineyi yorar; başlatmadan önce tek satırla ne koştuğunu yaz.
  Uygulama açıkken build/test alma (Supervisor binary'leri kilitler).
- **Belirsizlikte tahmin yürütme:** bu brief ile kod ya da doküman çelişiyorsa ikisini de yazıp sor. Brief'te olmayan bir
  tasarım kararı gerekirse önce tasarım README'sine bak, orada da yoksa sor.
- Oturum `develop` üzerinde biter; `.claude/summaries/` altına kısa özet (`<zaman>-resource-strip-and-resolve-priority-summary.md`):
  kaldırılanlar, yeniden yazılan testler, şeridin ölçülen maliyeti (örnek başına süre ve tahsis), güncellenen doküman bölümleri.
  Dosya adı zaman damgası PowerShell `Get-Date -Format 'yyyy-MM-dd-HH-mm'` ile.

Başla: İş 1, plan.
