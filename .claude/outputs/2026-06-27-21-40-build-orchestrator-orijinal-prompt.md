# Proje: Build Orchestrator

## 1. Amaç
Yüzlerce C#/WPF projesinin (farklı solution'lara dağılmış, birbirine bağımlı) tek bir masaüstü
uygulamadan; bağımlılık sırasına göre, paralel ve yalnızca değişenleri derleyerek hızlıca build
edilmesini sağlayan, Windows'a özel bir uygulama.

Temel akış: **Sync → Branch seç → Build/Rebuild → Canlı çıktı.**

## 2. Teknoloji ve Mimari (kesin)
- **UI:** WPF, .NET 8, MVVM (CommunityToolkit.Mvvm).
- **Animasyon:** Öncelik WPF (Storyboard + RenderTransform + Opacity + easing). En yoğun
  animasyon alanlarında gerekirse WinUI **Composition** interop ile güçlendir. Layout tetikleyen
  animasyon (Width/Height/Margin) yok; sadece transform/opacity.
- **Derleme motoru:** Ayrı bir **Worker process** (.NET 8 console). UI ↔ Worker arası named pipe
  veya stdio üzerinden JSON mesajlaşma. Build yükü UI thread'ini ASLA bloklamaz; Worker çökse de
  UI ayakta kalır.
- **MSBuild:** `Microsoft.Build.Locator` ile makinedeki MSBuild bulunur;
  `Microsoft.Build.Graph.ProjectGraph` ile graf; `Microsoft.Build.Execution.BuildManager` ile
  derleme; paralellik için `-maxcpucount` eşdeğeri; stop için `BuildManager.CancelAllSubmissions()`.
- **Git:** Komut satırı git. Branch listesi, diff, status, **worktree**.
- **Veri saklama:** `%LOCALAPPDATA%\BuildOrchestrator\` altında JSON (graf, build state, config).
- **Tray + Autostart:** WPF tray icon + Windows `Run` registry key (opt-in). Tek instance kilidi;
  ikinci açılış mevcut pencereyi öne alır.

## 3. Yapılandırma (config)
- Kök dizin (taranacak).
- Build configuration: **Debug (varsayılan)** / Release.
- Performans modu: **Full Power / Balanced / Light** (paralel derece + işlem önceliği).
- Branch çalışma modu: **worktree (varsayılan)** / mevcut dizinde checkout.
- Log seviyesi: **Errors-only (varsayılan)** / Full.
- Bağımlı (downstream) modu: **Safe (varsayılan)** / Fast.
- Cache konumu, Reduced Motion (varsayılan kapalı).

## 4. Çıktı Dizini Gerçeği (KRİTİK)
- Projeler, Visual Studio ayarlarında **sabit ortak bir çıktı dizinine (OutDir)** DLL üretir;
  container bu ortak dizinden okur. **Bu davranış korunur, OutDir'e DOKUNULMAZ.**
- Bu nedenle **final çıktı (bin) branch'e göre izole EDİLMEZ.** Derlenen DLL'ler her zaman ortak
  OutDir'e düşer; container otomatik alır. (Bilinçli tercih: "şu an hangi branch derlendiyse onun
  DLL'i container'da geçerlidir.")
- Yalnızca **ara çıktı (obj / BaseIntermediateOutputPath)** worktree içinde izole tutulur
  (incremental doğruluğu için).
- **Sonuç:** OutDir paylaşıldığı için DLL timestamp'i "değişti mi" kararında GÜVENİLMEZ. Karar
  yalnızca **kaynak sinyaline** (commit + local diff) dayanır (bkz. Bölüm 6).

## 5. Sync (Proje Keşfi ve Bağımlılık Grafiği)
- Kök dizinde `*.sln` ve `*.csproj` recursive taranır. Yok sayılan: `.git`, `bin`, `obj`,
  `node_modules`, `.vs`.
- `ProjectReference`'lardan tek bir **global bağımlılık grafiği** kurulur, topolojik sıra üretilir
  ve **diske cache'lenir** (`dependency-graph.json`).
- **Sıra her derlemede yeniden hesaplanmaz**; cache'ten okunur. Yalnızca kullanıcı "Yeniden Analiz"
  (Sync) butonuna basınca tüm graf yeniden kurulur.
- **Circular dependency:** SCC ile tespit; ilgili kartlarda "Cycle" rozeti + döngüdeki projeleri
  gösteren tooltip.
- Bulunan **tüm** projeler listeye eklenir (derlenmeyecekler dahil).

## 6. Derleme Stratejisi
### Rebuild
Tüm projeler, bağımlılık sırasına göre derlenir; **bağımsız olanlar paralel** çalışır.

### Build (incremental)
- Her proje için **branch + en son başarıyla derlenen commit no** kalıcı tutulur
  (`build-state.json`).
- Bir proje şu hallerde derlenir:
  1. O branch'teki güncel commit, son derlenen commit'ten farklı, **veya**
  2. Working tree'de o projeyi etkileyen local değişiklik (`git status`) var, **veya**
  3. Daha önce hiç başarıyla derlenmemiş.
- Aksi halde **Skipped** → listede **pasif/soluk** ve animasyonla "atlandı" gösterimi.
- Dosya → proje eşlemesi: değişen dosya proje klasörü altında ve build'i etkileyen uzantıda
  (`.cs .xaml .resx .csproj .props .targets`) ise proje dirty. Yukarıdan import edilen
  `Directory.Build.props/targets` değişirse kapsamındaki tüm projeler dirty.
- Bağımlı (downstream) projeler:
  - **Safe (varsayılan):** dirty + transitif bağımlılar derlenir.
  - **Fast:** sadece dirty projeler derlenir.

### Paralellik
Bağımlılığı olmayan projeler eşzamanlı derlenir. Paralel derece performans moduna bağlı
(Full/Balanced/Light).

### Branch yönetimi (worktree)
- Açılışta **kullanıcının aktif branch'i** otomatik seçili gelir.
- Kullanıcı başka branch seçse bile **VS'de açık working tree DEĞİŞMEZ.** Orchestrator o branch'i
  kendi worktree havuzunda (`%LOCALAPPDATA%\BuildOrchestrator\worktrees\<branch>\`) hazırlar/günceller
  ve orada derler. Derleme sonucu ortak OutDir'e düşer.
- Kullanıcı bu sırada VS'de **kesintisiz çalışmaya** devam eder.

### Eşzamanlılık / kilit
- Kullanıcı VS'de **aynı projeleri aynı anda derlemez** (teyit edildi); ağır kilit kuyruğu gereksiz.
- Yalnızca Orchestrator'ın **kendi içinde tek seferde tek run** çalıştırmasını garanti et ve OutDir'e
  yazımda atomik davran (kendi kendine çakışmayı önle).

### Hata ve Stop (özet — detay Bölüm 6.1)
- Bir projede hata olsa da kuyruk devam eder; o proje "Failed" işaretlenir; sonda toplu özet.
- Stop: önce graceful (`CancelAllSubmissions`), takılırsa timeout sonrası tüm process ağacı zorla
  sonlandırılır.

## 6.1 Process Kontrolü ve Güvenli Durdurma (KRİTİK — zorunlu gereksinim)
**Problem:** MSBuild derleme sırasında alt process'ler başlatır (paralel MSBuild worker node'ları,
`VBCSCompiler.exe` Roslyn sunucusu, pre/post-build event process'leri). Yanlış yönetimde uygulama
kapatılsa bile bu process'ler "öksüz" kalıp CPU'yu doldurarak çalışmaya devam eder. Bu KESİNLİKLE
önlenmelidir.

### Zorunlu kurallar
1. **Windows Job Object kullan.** Worker process ve onun başlattığı tüm alt process'ler tek bir Job
   Object'e bağlanır. Job, `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` bayrağı ile oluşturulur. Böylece ana
   process herhangi bir sebeple (normal kapatma, çökme, Task Manager'dan kill) sonlandığında, Windows
   tüm process ağacını OTOMATİK öldürür. Öksüz process oluşması imkânsız olmalı.
2. **Graceful Stop:** Stop butonu önce `BuildManager.CancelAllSubmissions()` çağırır; aktif proje
   güvenli noktada biter, kuyruk iptal edilir. Belirlenen timeout (örn. 5 sn) içinde durmazsa Job
   Object kapatılarak tüm process ağacı zorla sonlandırılır.
3. **Roslyn paylaşımlı derleyiciyi kapat:** Derleme `-p:UseSharedCompilation=false` ile çalıştırılır;
   `VBCSCompiler.exe` arka planda asılı kalmaz.
4. **PID süpürme (ikinci güvenlik ağı):** Worker, başlattığı tüm process PID'lerini izler; her run
   sonunda ve çıkışta süpürür.
5. **Çökme dayanıklılığı:** UI veya Worker beklenmedik şekilde çökerse, Job Object sayesinde tüm
   derleme process'leri otomatik temizlenir; manuel müdahale gerekmez.
6. **Tray'den Stop:** Pencere kapalıyken bile tray menüsünden "Stop Build" tüm işlemleri durdurabilir.
7. **Hata derlemeyi öldürmez:** Tek bir projenin hatası kuyruğu durdurmaz; o proje "Failed" işaretlenir,
   kalanlar devam eder. Yalnızca kullanıcı Stop derse veya kritik bir Worker hatası olursa run durur.

### Kabul kriteri (test edilmeli)
- Build çalışırken pencere kapatılır → 2 sn içinde arka planda MSBuild/VBCSCompiler/child process
  KALMAZ (Task Manager ile doğrulanır).
- Build çalışırken uygulama çökertilir (kill) → aynı şekilde arka planda hiçbir derleme process'i kalmaz.
- Stop butonu → aktif proje güvenli durur, kuyruk iptal, tüm child process'ler temizlenir.

## 7. UI / UX (tek pencere)
**Düzen:** Sol = proje kart listesi, Sağ = siyah console (output).
- **Sol alt:** Sync ikon butonu + etiketler (Toplam / Derlenen / Başarılı / Başarısız / Atlanan).
  Etikete tıklayınca kartlar **animasyonlu** filtrelenir (opacity → collapse).
- **Sağ alt:** Branch dropdown, Build, Rebuild. Çalışırken buton **Stop**'a morph + loading.
- **Kartlar:** Proje adı + solution adı. Sağ altta "Dosyada Aç" ve "Visual Studio'da Aç" ikonları.
- **Durumlar (renk):** Discovered, Queued, Building, Succeeded, Failed, Skipped, CycleDetected.

**Console:**
- Varsayılan **sadece error**; toggle ile full log. Ring buffer (max satır limiti).
- Hatalı karta tıklayınca console'da **sadece o projenin** çıktısı; tekrar tıklayınca tümü.
- **Auto-follow:** yeni log geldikçe en alta kayar; kullanıcı scroll yapınca durur; ~2 sn hareketsiz
  kalınca tekrar devam eder (her hareket sayacı sıfırlar).

**Animasyonlar (UI'ı KİTLEMEZ):**
- Sadece `RenderTransform` + `Opacity`; liste **UI virtualization** ile (VirtualizingStackPanel;
  `CanContentScroll`/`IsVirtualizing` yanlışlıkla kapanmamalı). 500–1000 kartta akıcı.
- Aktif kart: pulse border + hafif scale + öne gelme (carousel hissi). Başarısızda kısa shake,
  başarılıda glow, atlananda fade+desaturate.
- **Auto-focus:** aktif kart otomatik görünüre kaydırılır; kullanıcı manuel scroll yapınca otomatik
  takip durur, ~2 sn idle sonrası devam eder.
- Derleme sırasında kullanıcı listede/console'da serbestçe gezebilir.
- Reduced Motion açıkken animasyonlar minimuma iner.

## 8. Worker ↔ UI Sözleşmesi
- Komutlar: `syncWorkspace(rootPath)`, `reanalyze()`, `listBranches()`, `selectBranch(branch)`,
  `startRun(mode, branch, config, dependentMode)`, `stopRun(runId)`, `openPath(projectId)`,
  `openInVS(projectId)`.
- Eventler: `syncProgress`, `syncCompleted`, `runStarted`, `projectStarted`, `projectLog`,
  `projectSucceeded`, `projectFailed`, `projectSkipped`, `runCompleted`, `runCancelled`, `error`.
- Tipler:
  - `ProjectNode { id, name, projectPath, solutionName, dependencies[], buildOrder }`
  - `BuildState { projectId, branch, lastBuiltCommit, lastResult, lastRunAt }`
  - `RunRequest { mode:'build'|'rebuild', branch, config:'Debug'|'Release', dependentMode:'safe'|'fast' }`

## 9. Aşamalı Geliştirme Planı
1. **Faz 0:** WPF iskelet, tek pencere, tray, autostart, config ekranı.
2. **Faz 1:** UI prototipi (mock 500+ kart) — virtualization, animasyonlar, filtreler, console, scroll
   kuralları. Backend yok.
3. **Faz 2:** Sync motoru — tarama, ProjectGraph, topolojik sıra, cache, cycle tespiti.
4. **Faz 3:** Rebuild + paralel MSBuild + canlı log + Stop + **Job Object process kontrolü (Bölüm 6.1)**.
5. **Faz 4:** Incremental Build — commit/diff/status analizi, worktree, obj izolasyonu, skip mantığı,
   Safe/Fast mod.
6. **Faz 5:** Debug/Release, performans modları, Composition ile animasyon güçlendirme, paketleme/dağıtım.

## 10. Test Planı
- **Unit:** graf çıkarımı, topolojik sıra, cycle tespiti, dosya→proje eşleme, commit/diff delta.
- **Integration:** çoklu-solution örnek workspace'te Sync, Build, Rebuild, branch switch (worktree), Stop.
- **Process kontrolü (zorunlu):** Bölüm 6.1'deki kabul kriterleri — kapatma/çökme/Stop sonrası arka
  planda kalan process olmadığı doğrulanır.
- **Performans:** 500+ kartta akıcı scroll; cache hit'te Sync hızlanır; paralel build ölçülebilir
  kazanç; log akışında UI bloklanmaz.

## 11. Varsayımlar / Varsayılanlar
- Projeler tek git repo altında (multi-repo ileride).
- Ortak OutDir korunur; final DLL'ler branch'e göre izole edilmez; "değişti mi" kararı yalnızca
  commit + local diff ile verilir.
- Kullanıcı VS'de aynı projeleri Orchestrator ile eşzamanlı derlemez.
- Varsayılan: Debug, Safe mod, worktree, Errors-only, Full Power, Reduced Motion kapalı.
- Bağımlılık sırası cache'ten okunur; tam yeniden analiz yalnızca Sync ile.

## Talimat
**Faz 0 ve Faz 1'den başla.** Önce çalışan, animasyonlu, mock veriyle dolu tek pencere UI iskeletini
üret; backend'i sonraki fazlarda bağla. Process kontrolü (Bölüm 6.1) Faz 3'te devreye girecek ancak
mimari baştan buna uygun kurulmalı (Worker process ayrımı Faz 0'dan itibaren). Her fazı küçük, çalışır parçalar halinde teslim et.
