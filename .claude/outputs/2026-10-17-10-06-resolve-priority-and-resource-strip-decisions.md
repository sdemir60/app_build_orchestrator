# Resolve önceliği ölçümü ve v1.25.0 kaynak şeridi: kararlar

Tarih: 2026-10-17 · Oturum: Fable (analiz + karar; geliştirme ayrı bir Opus oturumunda yapılacak) · Kod: `develop` 76c9267e (v1.10.1)

## 1. Özet

- **"Resolve cycles at full priority" ayarı ve arkasındaki kural kaldırılıyor.** Resolve, Build gibi seçili performans
  modunu aynen uygular. Ölçüm: sakin makinede kural hiçbir şey kazandırmıyor (56,5 sn'ye karşı 52,4 sn), yüklü makinede
  yalnız %5 kazandırıyor (143,8 sn'ye karşı 151,0 sn) ve bedeli arayüzün 4-5 saniyelik kare boşluklarıyla, ~20 saniye
  boyunca 0,3-20 fps'e düşmesi. Cap'li ve BelowNormal koşuda aynı yük altında en uzun boşluk 1,6 sn, 250 ms üstü boşluk
  sayısı 23'e karşı 2.
- **Kaynak şeridi (tasarım v1.25.0) uygulanıyor;** veri App tarafında, outer job'ın accounting sayaçlarından 3 saniyede bir
  okunur; motora IPC eklenmez; pencere gizliyken örnekleme durur. Paralellik hücresinin verisi motorda zaten var.
- **Sağ alttaki perf çipi değişmiyor.** Paralellik koşu başında sabit kalmaya devam ediyor; dinamik yeniden ayar yok.
- Kullanıcının günlük modu şu an **Full** (ui-state.json). Full = cap yok + Normal, yani ölçülen "A" koşusuyla aynı
  davranış. Bu bir ayar değil modun anlamı; şerit CPU'yu gösterince bedeli görünür olacak.

## 2. Bugünkü davranış (değişmeden önce)

| Konu | Bugün |
|---|---|
| Resolve cycles koşusu | Profilin işçi sayısını korur ama cap'i kaldırır ve priority'yi Normal'e çeker (`PerfProfile.ForRun`). Ayar `Settings → General → Build → Resolve cycles at full priority`, varsayılan açık, her `startRun`'la motora gider. Build'in döngü turları bunu yapmaz. |
| CPU/RAM ölçümü | Yok. Yalnız About → Environment'ta App'in üç bellek satırı (`MemoryLine.TryRead`) ve motorun koşu sonu stderr `memory:` satırı. |
| Paralellik bilgisi | Motor koşu başında `WorkerBudget.Clamp` ile çekirdek ve boş belleğe göre kırpar; `runStarted` fiili sayıyı ve `workersReducedReason`'ı taşır; konsol ve event stream'e `workers reduced to <n> (<reason>)` yazılır. Pay (o an derlenen sayı) VM'de zaten biliniyor. |
| Perf çipi | Sağ altta, Full(6) → Balanced(4) → Light(2) döngüsü; cap ve priority koşu ortasında canlı değişir, işçi sayısı bir sonraki koşuda. |

## 3. Ölçüm

### 3.1 Kurgu

- Gerçek kurulu kopya (`%LOCALAPPDATA%\BuildOrchestrator.App\current`, v1.10.1), gerçek OSYS, `bugfix/72-haours-fix`,
  çalışma ağacı temiz. Makine: Core Ultra 7 258V (4P + 4LP-E, 8 thread), 32 GB; başlangıçta VS Code açık, VS kapalı,
  taban meşguliyet ~%17.
- Kirli küme: 16 Ekim 22:46'daki branch değişiminin yüzeyini oynattığı iki upstream projeye (`OSYS.UI.Service.LanguageResource`
  ve `OSYS.Types.UsedCars`) birer dosyada geçici **public yüzey** eklendi (`public int BoProbeMarker { get; set; }`); her
  koşuda ekle/geri al sırayla yüzey oynatıldı. Sonuç her koşuda aynı: UI.DMS döngüsünün 17 üyesinden 14'ü + upstream,
  toplam 25 proje, `levels=6`, tek tur. Sonunda iki dosya byte'ı byte'ına ve orijinal zaman damgasıyla geri alındı, OSYS git
  temiz; çıktılar orijinal kaynaktan derlenmiş halde.
- Dört koşu, hepsi **Balanced, 4 işçi**, kare sondası `BO_PROBE_FRAMES` açık (uygulamanın kendi `FrameGapProbe`'u: 5 sn'lik
  pencerede kare sayısı, en uzun boşluk, 33/50/100/250 ms üstü sayıları), WMI tabanlı süreç CPU örnekleyici 3 sn'de bir:
  - **A** ayar açık (cap yok, Normal) · **B** ayar kapalı (cap %70, BelowNormal) · sakin makine.
  - **A2 / B2** aynı iki ayar + yapay yük: 3 tek iş parçacıklı busy loop, Normal priority.
- Resolve UI Automation ile tetiklendi (`Resolve cycles` düğmesi, `InvokePattern`); koşu sonu `decision.log`'daki
  `finished:` satırından.

### 3.2 Sonuç

| Koşu | Cap / priority | Yük | Süre (tur) | 250 ms üstü boşluk | En uzun boşluk (koşu içi) | Makine meşgul ort / tepe | Yakıcıların aldığı çekirdek |
|---|---|---|---|---|---|---|---|
| A | yok / Normal | yok | 56,5 sn (41,8) | 0 | 48 ms | %63 / %89 | — |
| B | %70 / BelowNormal | yok | 52,4 sn (42,2) | 0 | 44 ms | %56 / %74 | — |
| A2 | yok / Normal | 3 yakıcı + VS açılışı | 143,8 sn (130,6) | **23** (8 pencere) | **5172 ms**; 09:57:44-09:58:06 arası ~20 sn boyunca 0,3-20 fps | %86 / %100 | 1,3 |
| B2 | %70 / BelowNormal | 3 yakıcı + VS açık | 151,0 sn (131,2) | **2** (2 pencere) | **1643 ms** | %76 / %100 | 2,3 |

Okuma:

- Sakin makinede fark yok: 4 derleyici 8 thread'i doyuramıyor (tepe %89), arayüz iki koşuda da 50 ms altı.
- Yük altında cap'siz Normal koşu arayüzü saniyelerce kilitliyor; örnekleyici o pencerelerde App'in CPU payını 0 görüyor,
  yani App hiç sıra alamıyor. Cap'li koşuda derleyiciler yakıcılara ve App'e yol veriyor (yakıcılar 1,3 yerine 2,3
  çekirdek alıyor), arayüz çoğunlukla 60-90 fps; iki pencerede 1,4-1,6 sn'lik boşluk var ve o anlarda bütün sistem
  duruyor (App %0, örnekleyici de kaçırıyor), bu kalan kısım derleyici önceliğiyle ilgili değil.
- Kuralın hız kazancı: sakin makinede yok, yüklüde %5. 2026-10-02'de ölçülen 1,9× (ARCHITECTURE §11.1'in gerekçesi) bu
  yük deseninde görülmedi.
- 16 Ekim 22:46'daki gerçek koşu (ayar kapalı, Balanced, 15 üye) donmamıştı; kullanıcının gün içinde gördüğü donmalar
  ayar açıkken ve makine yoğunken oldu. Ölçüm bunu doğruluyor.

### 3.3 Geçerlilik notları

- **VS çakışması:** Visual Studio 09:57:18'de, A2 başladıktan 20 sn sonra açıldı (kullanıcı makineye döndü); OSYS
  çözümünü yükleyip 5 MSBuild node'u başlattı (09:57:29-32, `/nodemode:1 /nodeReuse:true`). A2'nin en büyük donması
  (09:57:44-09:58:06) VS'nin açılış ve tasarım-zamanı derlemesiyle çakışıyor; A2'nin sonraki boşlukları (390, 797, 343,
  1641 ms) bu patlamadan sonra. B2 VS açıkken ama yerleşmişken koştu. Yani A2 ile B2 birebir eş değil; fark yönü ve
  cap'li koşunun hiç çok saniyelik donma vermemesi yine de karar için yeterli. Bu beş node VS'nindir, dokunulmadı.
- Kare sondası abone olunca WPF her karede çizer: fps mutlak değerleri şişkin, A/B karşılaştırması geçerli.
- UI Automation ağaç taraması yalnız tetiklemeden önce yapıldı; koşu sırasında UIA yok.
- Yapay yük gerçek masaüstü yükünün (VS, tarayıcı, Teams) kaba bir yaklaşımı; yakıcılar Normal priority'de.
- İlk kampanya (`campaign.ps1`) A2 adımında takıldı: `& python burn.py start` çocuk süreçlere stdout'u miras
  bıraktığı için PowerShell geri dönmedi; 3 yakıcı 07:41-09:53 arası boşuna çalıştı, uygulama A2 için açılmadı. Düzeltilmiş
  `campaign2.ps1` yakıcıları `Start-Process` ile ayrı başlatıp PID ile durdurdu. Kampanya 1'in A/B'si geçerli.
- Defender ortalama payı (tek çekirdeğin yüzdesi): A 83, B 38, A2 60, B2 23; ilk koşu sonrası taze çıktılar taranıyor.
- Kullanıcının sonraki Build'inde ek derleme beklenmez: upstream dosyalar orijinal içerik ve zaman damgasında, döngü
  üyeleri orijinal kaynaktan derlenmiş, döngü dışı bağımlılar içerik kararıyla güncel. Beklenmedik derleme görülürse
  sebebi bu ölçümdür.

### 3.4 Yeniden üretim

`.claude/temp/resolve-priority-2026-10-17/` (git'e girmez): `campaign2.ps1` (yük koşuları; `campaign.ps1` ilk kampanya,
yakıcı hatasıyla), `cpu_sampler.ps1`, `burner.py`, `surface_touch.py` (mark / revert / final), `analyze.py` →
`analysis.txt`; ham veri `frames-*.log`, `cpu-*.csv`, `burn-*/`, `campaign*.log`. Koşu klasörleri
`%LOCALAPPDATA%\BuildOrchestrator\logs\run-20261017-073835-235` (A), `074039-721` (B), `095658-119` (A2), `100109-488` (B2).

## 4. Kararlar

### K1. Ayar ve kural kaldırılır; Resolve profili aynen uygular

- `PerfProfile.ForRun`, `PerfNoteText.ResolveNote`, `GeneralSetting.ResolveAtFullPriority` ve katalog satırı,
  `SettingsDraftViewModel.ResolveAtFullPriority`, `RunViewModel.ResolveAtFullPriority` + `ApplyResolveAtFullPriority`,
  `UiStateStore.ResolveAtFullPriority`, `SettingsFile.resolveAtFullPriority`, `StartRunCommand.ResolveAtFullPriority`,
  Supervisor'daki uygulama noktası (`RunCoordinator.ApplyPerfLocked` içindeki dönüşüm) silinir. Kaynakta ~58, testlerde 39
  dosyada ~60 referans.
- Resolve koşusunun konsol notu sıradan koşu notuna döner (`parallelism: <n> · cpu cap <p>%`). Koşu ortası çip değişimi
  Resolve'da da sıradan notu yazar.
- Eski `ui-state.json` ve dışa aktarılmış settings dosyalarındaki alan yok sayılır; ARCHITECTURE §16'nın "eski sürümün
  yazdığı, bu sürümün okumadığı alan yok sayılır" kuralı zaten bunu karşılar; ayrıca bir göç kodu yazılmaz.
- Copy floor ve drain kuralı (§11.3) değişmez; "cap'siz koşu" artık yalnız Full'dür.
- Testler silinmez, yeni kuralı pinleyecek şekilde yeniden yazılır: "Cycles koşusu profilin cap'ini ve priority'sini
  aynen uygular" + "startRun artık bu alanı taşımaz" + "katalogda bu satır yoktur". Kırmızı önce gösterilir.
- Doküman aynı işte: ARCHITECTURE §5.2 (`startRun` alanı), §8.1 (Cycles paragrafı), §11.1 (Resolve paragrafı ve tablo
  notu; gerekçe olarak bu ölçüm: "sakin makinede kazanç yok, yüklü makinede %5, bedeli saniyelik arayüz donmaları"),
  §11.3, §13.3 (General sayfası satır listesi), §16 (ui-state alanları), §22 kod haritası satırı; README "Performance
  modes" bölümü. CHANGELOG'a dokunulmaz, sürüm çıkarırken `Removed` maddesi yazılır.

### K2. Full modu için uyarı yok, cümle var

Full'ün anlamı "cap yok, Normal": yoğun makinede arayüz donabilir. Tasarım şeritte uyarı rengi istemiyor; şerit CPU'yu
gösterdiği için bedel görünür olur. ARCHITECTURE §11.1'deki "Memory, not cores" paragrafına CPU tarafı için bir cümle
eklenir (Full'de derleyiciler arayüzle eşit önceliktedir; yüklü makinede kare boşlukları ölçüldü), README'ye kısa karşılığı.
Varsayılan Balanced kalır.

### K3. Kaynak şeridinin verisi App'te, outer job'dan okunur

- Motor + derleyiciler: App'in sahibi olduğu outer job (`EngineHost.OuterJob`) tüm Supervisor ağacını içerir (git,
  vswhere ve inner job'daki MSBuild/csc dahil). CPU: `QueryInformationJobObject(JobObjectBasicAccountingInformation)`
  → `TotalUserTime + TotalKernelTime` (ölen süreçlerin zamanı dahil; iki örnek arasındaki fark doğrudur). Bellek:
  `JobObjectBasicProcessIdList` → PID listesi → her PID için `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` +
  `GetProcessMemoryInfo` (`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`). Motor PID'si `EngineHost.EnginePid`; "engine" =
  o süreç, "compilers" = job toplamı − engine (Sync sırasında git de buraya düşer; kabul edildi).
- App'in kendisi: `GetProcessTimes` (ya da mevcut `Process` nesnesinde `TotalProcessorTime`) ve `MemoryLine.Read`
  ile aynı private bytes.
- CPU yüzdesi = (ΔApp + ΔJob işlemci zamanı) / (Δduvar saati × `Environment.ProcessorCount`), tam sayıya yuvarlanır.
- Maliyet: 3 sn'de birkaç syscall + 5-8 `OpenProcess`; mikrosaniyeler. `Process.GetProcesses()` ile makine taraması
  YASAK (kaynak guard'ı). IPC yok; motor uyurken job boş, şerit yalnız App'i gösterir, tooltip `engine asleep`.
- Örnekleme `DispatcherTimer` ile UI thread'de (iş o kadar küçük ki ayrı thread gerekmez); pencere gizli (tepsi) ya da
  küçültülmüşken timer durur (`HiddenSurface` sinyali + `WindowState`), görünür olunca hemen bir örnek alır. İlk CPU
  değeri ikinci örnekte gelir, o 3 sn `—`. Değer değişiminde animasyon yok, hücre genişliği hayalet metinle sabit
  (`100%` · `12.3 GB` · `12/12 Balanced`).
- Saf parçalar Core'da: yüzde hesabı, MB/GB metni (1 GB altı tam MB `MemoryLine.Megabytes` ile, üstü bir ondalık
  invariant `4.8 GB`), paralellik metni, tooltip metinleri. P/Invoke eklemeleri `NativeMethods` (tek yer), job sorguları
  `JobObject`'e (`QueryAccounting`, `QueryProcessIds`). App'te örnekleyici servis + VM özellikleri + title bar XAML.
- Testler: birim (hesap, biçim, tooltip), sıfır tahsis pini (`PerfMeasure.AllocatedBy`, ısınma sonrası), title bar
  realize testi (şerit var, ortalanmış, 1240 genişlikte update pill açıkken çakışmıyor), gizli yüzeyde timer durur,
  kaynak guard'ı (`Process.GetProcesses` yok; renk/ölçü literal yok).

### K4. Paralellik hücresi

- Boşta `0/N Mod`: N = çipin modunun tablo değeri, mod sözcüğü çipin modu.
- Koşarken hücre **koşuyu** anlatır: pay = o an derlenen proje sayısı (VM'nin building satırları, canlı), payda =
  koşu başındaki modun tablo değeri, mod sözcüğü koşu başındaki mod. Koşu ortasında çip değişirse hücre bir sonraki koşuda
  değişir (işçi sayısı zaten koşu başında sabit); konsol notu ve CPU hücresi canlı değişimi zaten gösterir.
- Kısılmış koşu: `runStarted.Parallelism < tablo değeri` → pay zaten o sayıyı aşamaz; tooltip `2 of 4 — reduced: <reason>`,
  reason `workersReducedReason` metnidir (motorun yazdığı, ör. `1 logical processor` ya da boş bellek GB'si); tasarımın
  `low memory` örneği bu metnin bir hali.
- Satırdan tek proje derlemesi: `1/N Mod` (pay 1, payda çip). Resolve'da özel bir şey yok.
- Tooltip'ler tasarımdaki metinler; mod sözcüğünün tooltip'i yok, hücre tıklanmaz.

### K5. RAM ölçüsü private bytes

About satırları ve motorun `memory:` satırıyla aynı kural (tek doğruluk kaynağı `MemoryLine`). Task Manager'ın "private
working set"inden farklı olduğu dokümana bir cümleyle yazılır.

### K6. Tooltip'teki cap modun tablo değeri

Balanced `cap 70%`, Light `cap 40%`, Full'de ek yok. Copy floor penceresindeki geçici yükselme ve koşu ortası değişim
şeritte gösterilmez; konsol notu zaten yazıyor.

### K7. About → Environment'taki üç bellek satırı kalkar

Şerit gelince `DiagnosticsReport`'un RUNTIME grubundaki `App private · GC committed · Managed heap` görünür satırları
kalkar; kopyalanan tanı raporunda sayılar kalır (hata bildirimi için). `MemoryLine` ve motorun stderr satırı değişmez.

### K8. Paralellik koşu başında sabit kalır

Dinamik yeniden ayar (koşu ortasında makineye göre işçi sayısını değiştirmek) yapılmaz: scheduler değişikliği, mod
kaymaları riski ve belirsiz kazanç. Şerit koşu başındaki kırpmayı gösterir; tasarımın istediği de bu.

## 5. Opus'a iş bölümü

1. **`fix/resolve-follows-profile`** (önce; çoğu silme): K1 + K2. Kırmızı testler → kaldırma → dokümanlar → tam süit →
   `develop`'a merge + push → branch silme.
2. **`feature/resource-strip`**: K3-K7. Tasarım paketi README §2.1, §3.6, §4 ve v1.25.0 sürüm notu esas; `prototype/`
   yalnız görsel referans. Realize testi + guard'lar + tam süit → merge + push.
3. Kaldırma sonrası doğrulama (branch 1'in sonunda ya da 2'den sonra, makine boşken ve kullanıcı kullanmıyorken):
   `campaign2.ps1`'in B2 kurgusu bir kez; kabul: 4-5 sn'lik boşluk yok, 250 ms üstü boşluk B2 seviyesinde. Sonuç
   `.claude/outputs/`'a yazılır.
4. Sürüm: iki iş de merge olunca minor (`1.11.0`); CHANGELOG o zaman yazılır (Added: resource strip · Removed: Resolve
   cycles at full priority setting; Resolve follows the performance mode).

## 6. Açık noktalar ve riskler

- Yüzde yüz yüklü makinede kalan 1,4-1,6 sn'lik tek tük boşluklar (B2) bu işin kapsamı dışında; o anlarda bütün sistem
  duruyor (VS node'ları, Defender, disk). 6 Ekim ölçümü App'i AboveNormal'a almanın da fark yaratmadığını göstermişti.
- A2'nin en büyük donması VS açılışıyla çakıştı (§3.3). Karar için yeterli; "önce/sonra" sayısı istenirse madde 5.3'teki
  doğrulama kullanılır.
- Kullanıcı Full modunda çalışıyorsa yoğun makinede donma yaşamaya devam eder; bu modun bedelidir, şerit gösterir.
- Ölçüm sırasında kullanıcı makineye döndü (VS 09:57'de açıldı): uygulama ekranda açılıp kapandı, iki OSYS dosyası
  kısa süre değişik görünmüş olabilir; hepsi geri alındı, git temiz.

## 7. Dosyalar

- Bu rapor: `.claude/outputs/2026-10-17-10-06-resolve-priority-and-resource-strip-decisions.md`
- Tasarım paketi: `.claude/outputs/2026-10-15-16-54-design-v1.25.0/README.md` (§2.1, §3.6, §4, v1.25.0 notu)
- Ölçüm araçları ve ham veri: `.claude/temp/resolve-priority-2026-10-17/`
- Önceki ölçümler: `.claude/outputs/2026-10-02-22-15-cycle-resolve-performance-analysis.md` (1,9× iddiasının kaynağı),
  `2026-10-06-22-38-memory-fans-resolve-stutter.md` (AboveNormal A/B)

## 8. Aynı oturumda yapılan düzeltmeler (10:40, kullanıcı kararı)

- K3'teki "ilk CPU değeri ikinci örnekte gelir, o 3 sn `—`" kalktı: ilk okuma açılıştan 0,5 sn sonra alınır, sonra 3 sn'de
  bir; hücre hemen gerçek sayı gösterir. `—` yalnız okuma gerçekten başarısızsa.
- K3'teki "motor uyurken tooltip `engine asleep`" kalktı: motor uyurken süreci yok, değeri 0'dır; tooltip `engine 0 MB ·
  compilers 0 MB` yazar, şeritteki RAM yalnız App'tir.
- K4 "satırdan tek proje `1/N Mod`" aynen kaldı. About satırlarının varlığı doğrulandı: `DiagnosticsReport` RUNTIME grubu,
  commit 8eadeaea (2026-10-13), v1.10.1'de mevcut.
- Opus brief'i: `.claude/outputs/2026-10-17-10-44-resource-strip-and-resolve-priority-opus-prompt.md` (kararların Opus'a
  verilen tam hali; bu raporla çelişirse brief geçerlidir).
