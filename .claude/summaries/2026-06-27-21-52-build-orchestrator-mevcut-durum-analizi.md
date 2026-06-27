# Build Orchestrator — Mevcut Durum İnceleme Özeti

- **Tarih:** 2026-06-27 21:52
- **Kapsam:** Kod tabanının, orijinal spec'e karşı multi-agent derinlemesine incelemesi (7 alt sistem analizi + gerçek `dotnet build`/`test` + §6.1 process kontrolü ve §4/§6 incremental için adversaryal denetim).
- **Orijinal spec:** [outputs/2026-06-27-21-40-build-orchestrator-orijinal-prompt.md](../outputs/2026-06-27-21-40-build-orchestrator-orijinal-prompt.md)
- **Build/Test (ground truth):** Build başarılı (0 uyarı, 0 hata, 5 proje). Test 11/11 geçti.

---

## Build Orchestrator — Mevcut Durum Analiz Raporu

Proje, spec'in beklediğinin **ötesinde** bir noktada: Talimatta "Faz 0 ve Faz 1'den başla, backend'i sonra bağla" denmesine rağmen kod Faz 2 (Sync/graph), Faz 3 (paralel build + process kontrolü) ve Faz 4'ün (incremental + worktree) çekirdek mantığını da içeriyor. Mimari iskelet (ayrı Worker process + stdio JSON IPC, MVVM, %LOCALAPPDATA% JSON persistence) sağlam ve spec'e büyük ölçüde uyumlu; incremental doğruluk mantığı adversaryal incelemeyi geçecek kadar temiz. Buna karşılık spec'in **en kritik zorunlu gereksinimi olan §6.1 process kontrolünde mimari bir açık** var ve Faz 1'in UI/UX animasyon vaatlerinin önemli bir kısmı (aktif kart pulse/scale, ReducedMotion, console error-toggle) henüz uygulanmamış.

### Build & Test Durumu

- **Build: Başarılı.** 0 uyarı, 0 hata. 5 proje derleniyor: Contracts, Core, Worker, Tests, App.
- **Test: 11/11 geçti** (0 başarısız, 0 atlandı, ~451 ms).
- Mevcut testler yalnızca çekirdek algoritmaları kapsıyor (graph extraction, topological order, cycle detection, file→project mapping, Safe/Fast propagation). Spec'in §10'da **ZORUNLU** dediği process-kontrol testleri, integration testleri ve performans testleri **yok**.

### Faz Bazlı Durum

| Faz | Durum | Not |
|-----|-------|-----|
| **Faz 0** — WPF iskelet, tray, autostart, config, Worker ayrımı | Büyük ölçüde | İskelet olgun; Worker process ayrımı baştan doğru kurulmuş. Config ekranında LogLevel/ReducedMotion alanları eksik. |
| **Faz 1** — UI prototip, animasyon, filtre, console, scroll | Kısmi | Virtualization, filtre mantığı, console scope, auto-follow var. İmza animasyonları (aktif kart pulse/scale/glow), ReducedMotion ve console error/full toggle eksik; mock 500+ kart yaklaşımı atlanmış. |
| **Faz 2** — Sync motoru, ProjectGraph, topolojik sıra, cache, cycle | Büyük ölçüde | Recursive tarama, Tarjan SCC, Kahn topolojik sıra, atomik JSON cache çalışıyor ve test edilmiş. `Microsoft.Build.Graph.ProjectGraph` yerine kendi builder'ı kullanılmış; cache açılışta UI'a sunulmuyor. |
| **Faz 3** — Rebuild + paralel MSBuild + log + Stop + Job Object | Kısmi | BuildManager, CancelAllSubmissions, paralel kuyruk, hata izolasyonu, tray Stop tam. Ancak §6.1 Job Object UI yerine Worker'da; hard-stop'ta TerminateJobObject çağrılmıyor. |
| **Faz 4** — Incremental, worktree, obj izolasyonu, Safe/Fast | Büyük ölçüde | Karar mantığı (commit + diff + never-built), obj izolasyonu, Safe/Fast downstream, branch-bazlı state doğru. obj izolasyon anahtarı Name (Id değil); EnsureWorktree git hataları yutuluyor; CheckoutInPlace'te obj izolasyonu yok. |
| **Faz 5** — Debug/Release, performans modları, Composition, paketleme | Kısmi | Debug/Release ve performans modu RunRequest'e akıyor; paralel derece + process önceliği moda bağlı. WinUI Composition interop ve paketleme/dağıtım yok. |

### Bölüm Bazlı Durum (spec §)

**§2 Teknoloji & Mimari**
- WPF + .NET 8 + MVVM (CommunityToolkit) doğru: `MainViewModel.cs:7-13`, `[ObservableProperty]`/`[RelayCommand]` tutarlı.
- Ayrı Worker process + stdio newline-delimited JSON IPC; build UI thread'ini bloklamıyor, event'ler `Dispatcher.BeginInvoke` ile marshal ediliyor (`WorkerClient.cs:32-84`, `MainViewModel.cs:222-228`). Worker çökse de UI ayakta (`MainViewModel.cs:369-377`).
- MSBuildLocator herhangi bir `Microsoft.Build` tipine dokunmadan ÖNCE register ediliyor (`Program.cs:8-19`), `BuildManager` ile derleme (`MsBuildBuildEngine.cs:46-53`).
- **Kısmi/Eksik:** Spec'in adlandırdığı `Microsoft.Build.Graph.ProjectGraph` hiç kullanılmamış; bağımlılık grafı kendi `DependencyGraphBuilder` ile kurulup engine'de elle gating yapılıyor (`MsBuildBuildEngine.cs:55-72`). Animasyon kuralı "Width/Height animasyonu yok" seçili kartta Accent şerit Width 6→13 animasyonuyla ihlal ediliyor (`MainWindow.xaml:101-107,124-126`). WinUI Composition interop yok.

**§3 Config**
- Root dizin, Debug/Release, Full/Balanced/Light, worktree/checkout, Safe/Fast, Cache konumu UI'da var; default değerler spec'le birebir (`AppConfig.cs:12-23`).
- **Eksik:** LogLevel ve ReducedMotion model'de var ama Config ekranında YOK ve `ConfigViewModel.ToConfig()` bunları taşımıyor (`ConfigViewModel.cs:39-49`) → her Save'de default'a sıfırlanıyor.

**§4 Çıktı Dizini Gerçeği (KRİTİK)**
- OutDir/OutputPath hiçbir yerde override edilmiyor; sadece `BaseIntermediateOutputPath`/`IntermediateOutputPath` worktree altındaki `.bo-obj`'e yönlendiriliyor (`MsBuildBuildEngine.cs:109-121`, `WorkerHost.cs:203`).
- "Değişti mi" kararı yalnızca kaynak sinyaline dayanıyor; tüm src'de DLL/bin timestamp karşılaştırması YOK (adversaryal olarak doğrulandı: `DiffAnalyzer.cs`, `IncrementalPlanner.cs:42-52`).
- **Risk:** obj izolasyon anahtarı `project.Name` (`MsBuildBuildEngine.cs:118`); aynı isimli iki proje obj dizinini paylaşıp incremental'i bozabilir.

**§5 Sync (Keşif & Bağımlılık Grafiği)**
- Recursive `*.sln`/`*.csproj` taraması doğru ignore listesiyle (`ProjectScanner.cs:24-27`); ProjectReference edge'leri (`DependencyGraphBuilder.cs:94-124`); iteratif Tarjan SCC ile cycle (InCycle + CycleMembers); Kahn topolojik sıra; atomik `dependency-graph.json` cache (`GraphStore.cs`, `JsonStore.cs:30-44`).
- **Kısmi:** UI her açılışta `SyncWorkspace`/`Reanalyze` gönderip full re-scan tetikliyor (`MainViewModel.cs:57-64`, `WorkerHost.cs:69-75`). Spec "sıra her açılışta yeniden hesaplanmaz, cache'ten okunur" diyor; cache açılışta UI'a sunulmuyor. Cache versiyonlama/invalidation yok. Sln/csproj parse regex+XDocument tabanlı (MSBuild ProjectGraph değil).

**§6 Derleme Stratejisi**
- Rebuild tüm projeleri BuildOrder ile derliyor; bağımsızlar paralel (`MsBuildBuildEngine.cs:148-174`). Incremental kararı 3 sinyal (commit≠ / local change / never-built) ile doğru (`IncrementalPlanner.cs:46-52`). `build-state.json` branch::projectId anahtarlı, başarısızlıkta eski commit korunuyor (`BuildStateStore.cs:20`, `WorkerHost.cs:282-292`). Safe=dirty+transitif downstream BFS, Fast=sadece dirty (`IncrementalPlanner.cs:55-58,92-116`). Dosya→proje "en derin sahip" eşlemesi + Directory.Build.props kapsam yayılımı doğru (`DiffAnalyzer.cs:47-69`). Worktree havuzu ana repo'ya dokunmadan hazırlanıyor; tek-run kilidi var (`WorkerHost.cs:147-156`).
- **Kısmi/Risk:** `EnsureWorktreeAsync` git komut sonuçlarını kontrol etmiyor (sessiz başarısızlık riski, `GitService.cs:105-113`). CheckoutInPlace modunda obj izolasyonu yok. Skip event'inde planner'ın reason'ı yerine sabit "no source change" gönderiliyor (`WorkerHost.cs:229`). currentCommit/diff ana repo'dan okunuyor (worktree değil) — dokümante edilmemiş subtil varsayım.

**§6.1 Process Kontrolü (KRİTİK — ZORUNLU)**
- Doğru olanlar: Job Object KILL_ON_JOB_CLOSE kuruluyor (`JobObject.cs:24-45`), UseSharedCompilation=false + EnableNodeReuse=false (`MsBuildBuildEngine.cs:51,109-114`), CancelAllSubmissions token register (`MsBuildBuildEngine.cs:90-94`), tray Stop (`MainWindow.xaml:148`), hata izolasyonu — tek proje hatası kuyruğu durdurmuyor (`MsBuildBuildEngine.cs:126-161`).
- **KRİTİK AÇIK 1:** Job Object UI tarafında DEĞİL, Worker'ın kendi içinde oluşturulup kendisine atanıyor (`Program.cs:26-28`). Bu yüzden "Worker ölünce child'lar ölür" yönü çalışıyor ama spec'in çekirdek garantisi "**UI herhangi bir sebeple (crash/kill) ölünce tüm ağaç otomatik ölür**" Job Object ile DEĞİL, kırılgan bir managed parent-watcher (`parent.Exited -> Environment.Exit`, `Program.cs:55-57`) ile sağlanıyor. Worker yarı-çökmüş/deadlock olursa veya `--parent` geçilmezse hem Worker hem MSBuild/VBCSCompiler ÖKSÜZ kalabilir.
- **KRİTİK AÇIK 2:** Hard-stop fallback'inde (timeout sonrası) `JobObject.TerminateAll`/TerminateJobObject HİÇ çağrılmıyor (ölü kod); yerine PID-tabanlı `ProcessSweeper.SweepDescendants` kullanılıyor (`WorkerHost.cs:340`). Süpürme gerçek PID izleme değil, anlık `GetProcessesByName` + parent-chain heuristiği; VBCSCompiler reparent olursa öksüzler kaçırılabilir.
- Kabul kriterleri (kill/crash/Stop → 2sn içinde process kalmaz) kod yoluyla kısmen destekleniyor ama **otomatik/runtime test yok**.

**§7 UI/UX**
- 3 kolonlu düzen (sol kart listesi / GridSplitter / sağ console), virtualization doğru kurulu (CanContentScroll/IsVirtualizing kapatılmamış, `MainWindow.xaml:183-191`), 5 sayaç etiketi, kart-bazlı console scope, console auto-follow (2sn idle resume, `AutoScrollBehavior.cs:59-93`), ring buffer, 7 durum rengi (`Converters.cs:11-28`) tam.
- **Eksik/Kısmi:** Aktif kart pulse border + scale + öne gelme animasyonu ve başarı glow'u HİÇ yok (IsActive yalnız VM'de, XAML'de trigger yok). Console'da error/full toggle kontrolü yok (mantık "özet vs per-project" üzerine kurulu). Filtre geçişi ve Build→Stop "morph + loading" animasyonsuz/anlık. Skipped'da desaturate yok, sadece opacity. Proje listesi auto-focus'ta "kullanıcı scroll edince takip durur" mantığı yok (her ProjectStarted'da zorla en üste atlar). ReducedMotion hiçbir animasyona bağlı değil.

**§8 Worker ↔ UI Sözleşmesi**
- 8 komutun ve 11 event'in TAMAMI polimorfik `$kind` JSON tipleri olarak tanımlı, Worker'da dispatch ediliyor, UI'da uçtan uca handle ediliyor. ProjectNode/BuildState/RunRequest spec alanlarını eksiksiz taşıyor (geriye uyumlu superset alanlarla).
- **Sapmalar:** Spec'te olmayan `branchList` event'i ve `shutdown` komutu eklenmiş; `ListBranchesCommand` UI'dan hiç gönderilmiyor (ölü komut); `StopRunCommand.runId` Worker'da doğrulanmadan yok sayılıyor. İşlevsel olarak zararsız ama sözleşme spec ile birebir örtüşmüyor.

**§9 Aşamalı Geliştirme**
- Talimattan sapma (lehte): "mock ile başla, backend'i sonra bağla" yerine gerçek backend (Faz 2-4) doğrudan kurulmuş. Mock 500+ kart üreticisi yok; UI gerçek Worker'a bağlı.

**§10 Test Planı**
- Unit kapsam iyi (graph/topo/cycle/diff/planner) ve 11/11 geçiyor. **Eksik (yüksek önem):** §6.1 process-kontrol testleri (ZORUNLU), integration testleri, performans testleri, commit-delta tetikleyicisi izole testi, ProjectScanner ignore-dir testi tamamen yok. `tests/BuildOrchestrator.Core.Tests/` ölü/stale artefakt dizini.

**§11 Varsayımlar/Varsayılanlar**
- Tüm default değerler (Debug, Safe, worktree, ErrorsOnly, FullPower, ReducedMotion=false) model'de birebir doğru (`AppConfig.cs:12-23`). Tek pürüz LogLevel/ReducedMotion'ın Save'de kaybolması.

### Güçlü Yönler

- **Worker process ayrımı mimarisi sağlam:** ayrı process, stdio JSON IPC, tüm event'ler UI thread'e marshal, Worker çıkışı yakalanıp UI ayakta tutuluyor.
- **Incremental doğruluk adversaryal incelemeyi geçti:** karar tamamen kaynak sinyaline (commit + git status + never-built) dayalı, hiçbir yerde DLL timestamp okunmuyor; OutDir hiç override edilmiyor, sadece obj izole.
- **Sync çekirdeği olgun:** iteratif Tarjan SCC (deep-graph güvenli), deterministik Kahn topolojik sıra, atomik JSON cache; birim testleriyle doğrulanmış.
- **Console auto-follow** spec'e çok yakın, temiz bir attached behavior ile; UI virtualization doğru kurulmuş.
- **§8 sözleşmesi uçtan uca kapalı:** 8 komut + 11 event tam, tip alanları eksiksiz.
- **Tek-instance + tray + opt-in autostart** doğru varsayılanlarla eksiksiz.
- **Hata izolasyonu (§6.1 kural 7) ve tray Stop (kural 6)** tam ve doğru.

### Eksikler, Riskler ve Spec Uyumsuzlukları

1. **[YÜKSEK] §6.1 Job Object UI yerine Worker'da kuruluyor** (`Program.cs:26-28`). "UI crash/kill → tüm ağaç otomatik ölür" garantisi Job Object ile değil, kırılgan managed parent-watcher ile sağlanıyor. Worker yarı-çökerse veya `--parent` yoksa öksüz process kalabilir. Doğru tasarım: Job Object'i UI process'inde oluşturup Worker'ı (CREATE_SUSPENDED ile) ona atamak.
2. **[YÜKSEK] §6.1 hard-stop'ta TerminateJobObject çağrılmıyor** (`JobObject.TerminateAll` ölü kod); yerine PID-heuristik süpürme (`WorkerHost.cs:340`). Takılan/cancel'a yanıt vermeyen node'lar reparent olunca kaçırılabilir; deterministik garanti yok.
3. **[YÜKSEK] §10 ZORUNLU process-kontrol, integration ve performans testleri yok.** Spec'in en kritik kabul kriterleri (kill/crash → 2sn'de process kalmaz) hiç otomatik doğrulanmıyor.
4. **[ORTA] §3/§11 LogLevel ve ReducedMotion config ekranında yok ve `ToConfig()` taşımıyor** (`ConfigViewModel.cs:39-49`) → her Save'de default'a sıfırlanıyor; ReducedMotion hiçbir animasyona bağlı değil; LogLevel RunRequest'e iletilmiyor.
5. **[ORTA] §7 imza animasyonları eksik:** aktif kart pulse/scale/öne-gelme ve başarı glow'u hiç yok (IsActive yalnız VM'de); console error/full toggle yok; filtre/Stop geçişleri animasyonsuz; Skipped'da desaturate yok.
6. **[ORTA] §4/§6 obj izolasyon anahtarı `project.Name`** (`MsBuildBuildEngine.cs:118`); aynı isimli projelerde incremental çakışması. CheckoutInPlace modunda obj izolasyonu hiç yok.
7. **[ORTA] §5 cache yaşam döngüsü uyumsuzluğu:** UI her açılışta full Reanalyze tetikliyor (`MainViewModel.cs:57-64`); spec "açılışta cache'ten oku, full analiz yalnızca Sync" diyor. Cache versiyonlama/invalidation yok.
8. **[DÜŞÜK] §2 `Microsoft.Build.Graph.ProjectGraph` kullanılmamış** (kendi builder + elle gating). Çalışıyor ama spec'in adlandırdığı API değil; kondisyonel/SDK üzerinden gelen ProjectReference'ları kaçırabilir.
9. **[DÜŞÜK] §8 sözleşme sapmaları:** spec dışı `branchList`/`shutdown`, ölü `ListBranchesCommand`, doğrulanmayan `StopRunCommand.runId`. `EnsureWorktreeAsync` git hatalarını yutuyor (`GitService.cs:105-113`) — sessiz yanlış-worktree riski.

### Mimari Notlar

- Katmanlama temiz: Contracts (DTO + polimorfik IPC), Core (discovery/incremental/git/persistence), Worker (MSBuild + process control), App (WPF/MVVM). Çekirdek algoritmalar saf ve test edilebilir.
- DI container yok; servisler doğrudan `new` ile inşa ediliyor (`MainViewModel.cs:16,30`, `WorkerHost.cs:21-27`) — MVVM'i ihlal etmiyor ama mock'lanabilirliği/test edilebilirliği düşürüyor (özellikle `BuildStateStore`/`GitService` diske/git'e doğrudan bağlı).
- En büyük mimari borç §6.1: Job Object sahipliğinin yanlış process'te (Worker, UI değil) olması ve hard-kill'in deterministik job-terminate yerine heuristik PID süpürmeye düşmesi. Bu, spec'in "öksüz process imkânsız" çekirdek vaadini zayıflatıyor ve düzeltilmesi gereken birincil kalemdir.
