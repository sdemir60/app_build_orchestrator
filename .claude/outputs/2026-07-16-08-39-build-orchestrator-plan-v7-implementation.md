# Build Orchestrator — Plan v7 (Uygulanabilir)

> **For agentic workers:** REQUIRED SUB-SKILL — Bu planı task-by-task uygulamak için `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans` kullan. Adımlar checkbox (`- [ ]`) ile izlenir.

> **v7 nedir / ne DEĞİLDİR.** v7 = v6'nın ([2026-07-02-01-38-build-orchestrator-plan-v6-implementation.md](2026-07-02-01-38-build-orchestrator-plan-v6-implementation.md)) **türevi**; v6'yı üç girdiyle günceller:
> 1. **Onaylı tasarım paketi** [design-v1](2026-07-15-19-00-design-v1/README.md) (high-fidelity README + çalışan HTML prototip) — artık **tek görsel otorite** (renk/ölçü/tipografi/motion/kopya metinleri).
> 2. **WPF uygulanabilirlik analizi** [design-wpf-feasibility-analysis](2026-07-15-23-34-design-wpf-feasibility-analysis.md) — 21-agent analiz + adversarial doğrulama; WPF kararı DOĞRULANDI, teknik mimari kararları ve 6 doğrulama düzeltmesi bu rapordan.
> 3. **11 onaylı kullanıcı kararı** (2026-07-16 — aşağıda "v7 Karar Kaydı").
>
> **Hiçbir v5/v6 kararı silinmedi veya ezilmedi.** Değişen/eklenen her yer `[v7Δ]` etiketiyle işaretli; çelişki çıkarsa **v7'deki `[v7Δ]` son sözdür**, aksi halde v6/v5/v4.3 iz etiketleri geçerlidir. Plan otoritesi zinciri: v4.3 (donmuş arşiv) → v5 → v6 → **v7 (bu dosya, güncel)**. Görsel otorite: **design-v1 README + prototip kaynakları** (`BuildApp.jsx`, `build-data.js`, `_ds` token'ları) — görsel/etkileşim çelişkisinde design-v1 kazanır; **davranış semantiği** çelişkisinde v7 kararları kazanır. Delta design-system promptu: [2026-07-02-01-38-delta-design-system-v1.md](2026-07-02-01-38-delta-design-system-v1.md) (tarihsel; token kaynağı artık design-v1 `_ds/…/tokens/*.css`).

**Goal:** Yüzlerce legacy .NET Framework C#/WPF projesini (tek git repo, OSYS) bağımlılık sırasına göre, paralel ve yalnızca değişenleri derleyen; derlemeyi ayrı bir Supervisor process'te nested Job Object ile yöneten, dark/modern WPF masaüstü orchestrator.

**Architecture:** İki process (App/WPF + Supervisor/console) + saf Core + Contracts + Tests. App = view + outer Job sahibi; Supervisor = inner Job + her projeyi `MSBuild.exe` ile shell-out derler; Core = tüm planlama (graf, signature, scheduler, layer) saf ve test edilebilir. İletişim stdio NDJSON. Teslim = Iteration -1 (gating spike) → It-0..5 walking-skeleton dikey dilim.

**Tech Stack:** .NET 10 (LTS) + WPF · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · xUnit · **derleme motoru `MSBuild.exe`** (VS Build Tools/VS, `vswhere` ile resolve) + `nuget restore`/`msbuild -t:restore` · Win32 Job Object / RegisterHotKey / WindowChrome P/Invoke · **[v7Δ-6] AvalonEdit (konsol host)** · **[v7Δ-6] WPF kararı fizibilite analiziyle DOĞRULANDI** (alternatifler — WebView2 hibrit, Avalonia, WinUI 3 — gerekçeli elendi; WebView2 yalnız T65 font karar kapısında geri gelebilir).

---

## v7 KARAR KAYDI (2026-07-16, kullanıcı onaylı)

| # | Karar | Sonuç |
|---|---|---|
| K1 | Sync semantiği | **`git fetch origin` DAHİL, ref-only** — checkout/pull yok; ağ yoksa warn + yerel HEAD ile degrade |
| K2 | Scheduler dispatch | **Ready-set, ileri atlamalı (v6 A6 korunur)** — design README §3.2'deki `break` kuralı sim sadeleştirmesidir, Core'a taşınmaz |
| K3 | Branch seçim konsol satırı | **Niyet bildirimi** (`branch target: <name> (<sha>) — worktree will be used at Build`); `git switch --detach` satırı KALKAR; gerçek `git worktree add` Build anında loglanır |
| K4 | Graf düğüm şekli | **26px, 4px-radius KARE** (DS bileşeninin çizdiği/prototipte görünen); README'nin "daire" metni düzeltilmiş sayılır |
| K5 | X→tray ilk bilgilendirme | **OS tray balloon (ilk sefer, tek seferlik)** — uygulama İÇİ toast yasağı (design §8) korunur; DD12 böyle karşılanır |
| K6 | Kısayol şeması | **F5=Build/Continue · Ctrl+F5(Shift+F5)=Rebuild · Ctrl+F=proje filtresi · Esc zinciri · Alt+B global hotkey.** Çift-Shift ve Ctrl+P KALDIRILDI; v6'daki Ctrl+B/Ctrl+R, F5/Ctrl+F5 ile İKAME edildi. Koşarken F5 = Stop |
| K7 | Open in Visual Studio | **Projenin bağlı olduğu solution bulunur (>1 ise seçtir — T32) ve o .sln VS'de açılır**; mekanizma: `vswhere -latest` → devenv.exe (deterministik); çalışan-VS'ye ROT/DTE bağlanma v2 backlog |
| K8 | Maximize buton glyph'i | **Restore glyph (iç içe iki kare) eklenir** — WindowState=Maximized'da swap (tasarımda tanımsızdı) |
| K9 | Font/WebView2 karar kapısı | **It-4 başında A/B font testi** (WPF Display/Ideal × ClearType/Grayscale ↔ tarayıcı, hedef monitörde); kabul → saf WPF kesin; ret → WebView2 hibrit gündeme (yalnız App katmanı değişir) |
| K10 | Repo değiştirme girişi | **Settings dialoguna REPOSITORY satırı** (mevcut kök + `Change…` → OpenFolderDialog); dişli tooltip'i "Settings" olarak genelleşir |
| K11 | Perf modları | **Sabit Full(6)/Balanced(4)/Light(2) + process priority + inner Job CPU rate cap (∞/%70/%40)**; konsol notu cap'i de yazar: `parallelism: 4 · cpu cap 70%` |

---

## v6 DELTA CHANGE SET (v5 → v6, dört onaylı karar + fold-in — TARİHSEL, korunur)

> Bu bölüm v6'nın v5'e **kıyasla** getirdiklerinin tek-bakış listesidir. Gövde içinde her biri `[v6Δ-N]` ile işaretli.

| # | Delta | Özet | Etkilediği yerler |
|---|---|---|---|
| **Δ1** | **Branch-driven worktree** | Branch seçimi belirler; ayrı "local dahil/hariç" toggle YOK (türetilmiş etiket). Farklı branch → worktree ZORUNLU (aktif branch hiç değişmez); aynı branch → tek worktree toggle (OFF=in-place+local, ON=committed+worktree). `runBlocked` ve in-place branch-switch onayı KALKAR. | A6, A7(DD13/DD14), A9, A10, A12, T29, Part C It-3 |
| **Δ2** | **Dependency graf paneli** | Sol panel ikiye bölünür: sol-üst = yerleşik DAG görselleştirme (canlı statü renkli, frontier grafta akar, node odak), sol-alt = mevcut liste. "Graf görselleştirme" out→in scope. Core saf kalır (node/kenar/katman zaten üretir), render+animasyon App. | A7(IA+yeni alt bölüm), A8, A11, A9(opsiyonel), yeni **T50/T51**, Part C It-4/It-5 |
| **Δ3** | **Will-build noktası** | Kart accent = build statüsü; ayrı NOKTA = pre-run tahmin (amber=dirty/derlenecek, gri=güncel, hollow=Sync öncesi). N7 sağlık → yalnız cycle kırmızı-rozetine iner. | A5, A7, A8, A9, Part C It-1/It-3 |
| **Δ4** | **Çoklu hata → Failed filtresi** | Banner "✗ N hata — proj1, proj2 [Failed'a git]"; buton Failed filtre çipini uygular; her başarısız satır KENDİ logu (DD8 canonical). Birleşik/çoklu-seçim YOK. *(Sunum v7'de sticky şerit hata kümesine revize edildi — bkz. [v7Δ-7].)* | A7(DD11), T39 |
| **Δ5** | **Fold-in cilalar** | (a) Özet stream satırı: hover-bg + seçili + tekrar-tıkla-deselect (kartla birebir). (b) Kart tekrar-tıkla → deselect + Back kaybolur + ana ekran (canonical). (c) Branch chip aranabilir. (d) Debug/Release action-bar segment toggle (config-agnostic uyarısı). (e) "Dosyada Aç/VS'de Aç" estetik ikon (hover-reveal korunur). (f) Statü glyph'leri ince-halka daire-içi serbest; anti-slop yasağı yeniden yazılır. | A5/A7(DD8/DD10/N6/Pass4), A10, T39/T40/T45 |

---

## v7 DELTA CHANGE SET (v6 → v7, tasarım paketi + fizibilite + 11 karar)

> Gövde içinde her biri `[v7Δ-N]` ile işaretli. Kaynaklar: design-v1 paketi (D), fizibilite raporu (F), kullanıcı kararları (K1–K11).

| # | Delta | Özet | Etkilediği yerler |
|---|---|---|---|
| **Δ1** | **Görsel otorite = design-v1** | README + çalışan prototip tek görsel kaynak; tüm token/ölçü/kopya/animasyon değerleri oradan. **Sim-metin istisnası:** prototipteki `net8.0` yolları, "Osys.sln loaded (36 projects)", eksik flag'ler PLACEHOLDER'dır — format/dil/ton birebir, sayılar/yollar/TFM gerçek veriden. README'de olmayan **25 prototip davranışı** (rapor Ek A: Continue/Retry menüleri, Copy log, Ctrl+F, ETA +400ms, engine tick kadansı, render dilimleri…) spec kapsamındadır. | A0, A7, T49, tüm UI taskları |
| **Δ2** | **Sync = git fetch (ref-only) dahil** `[K1]` | Sync akışının başı: `git fetch origin <branch>` — yalnız ref güncelleme; checkout/pull ASLA (v6Δ-1 korunur). Hedef SHA = remote-tracking ref; kartlardaki `curSha → targetSha` buna dayanır. Ağ yoksa: warn satırı + yerel HEAD ile degrade. N1 granular tarama satırları tasarımın dim/info konsol diliyle harmanlanır. | Global Constraints, A5, A9, A12, yeni **T69**, It-3 |
| **Δ3** | **depIssue / dependency-affected sistemi** | Scheduler'da bağımlılık "çözüldü" = succeeded VEYA failed VEYA skipped — **hatalı bağımlılık alt projeleri BLOKLAMAZ**; bağımlılar son başarılı çıktıyla derlenir; kök hata adları `depIssue` olarak zincir boyunca taşınır. Log başı warn satırları, ▲ rozet (kart+graf+konsol başlığı), action bar `▲ N` chip + `dep` filtresi, stream `built — dependency issue (2.4s)`, done metninde `N dependency-affected`. | A6, A7, A9(Contracts), T39/T53, yeni **T54**, It-3/4 |
| **Δ4** | **Continue + Retry failed** | Stop sonrası: faz `stopped`, primary buton **Continue** (F5) — kalan queued'lar mevcut plandan sürer, elapsed korunur. Build menüsünde failed>0 iken **Retry failed — N failed + dependents**: failed + transitif bağımlılar yeni willBuild kümesi; konsol/stream SIFIRLANMAZ. | A7, A9 (`RunRequest.mode` genişler), T4, yeni **T55**, It-2/3 |
| **Δ5** | **Kısayol şeması + branch niyet satırı + kilit teyidi** `[K3/K6]` | Kısayollar: **F5 = Build (stopped'ta Continue; koşarken Stop) · Ctrl+F5/Shift+F5 = Rebuild · Ctrl+F = proje filtre inputuna odak · Esc = dialog→popover/menü→seçim zinciri · Alt+B = global hotkey (ayarlanabilir).** Çift-Shift ve Ctrl+P KALDIRILDI (branch popover'da arama zaten var). Branch seçiminde konsola **niyet satırı** (git komutu değil); `git worktree add` Build anında loglanır. Mid-run kilit (T12) TEYİT: koşarken branch/worktree/Debug-Release kilitli, perf chip canlı — prototipteki kilitsizlik gözden kaçmaydı. | A7(N6), A9, A10, T29, T40, T12 |
| **Δ6** | **WPF teknik mimari paketi (fizibilite)** | WPF DOĞRULANDI; zorunlu teknik kararlar: **konsol = AvalonEdit** (metin seçimi + renkli satır + hacim üçlüsünün tek yolu; hibrit aktif-satır typewriter; kaskat pop-in'de transform yerine tempo+fade), `TrackedTextBlock` (letter-spacing), sticky-header **overlay mimarisi** + virtualization stratejisi, smooth-scroll attached-DP altyapısı, DS kontrol kütüphanesi (ControlTemplate seti), tooltip altyapısı (delay=0 + CustomPopupPlacementCallback), `MotionCoordinator` (1 hero), pencere kabuğu düzeltmeleri (maximize padding ZORUNLU, Snap Layouts hook, restore glyph `[K8]`, AllowSetForegroundWindow), graf hibrit render (Shapes ≤150 → DrawingVisual katmanları + FlowOverlay; etiketlerde Ideal), asset hattı (statik OTF gömme — variable font YASAK). **It-0'a CompositeFont line-height spike; It-4 başına font A/B karar kapısı `[K9]`.** Yapısal fark kabulleri → yeni **A13**. | A7, yeni **A13**, T34–T51, yeni **T56–T68**, It-0/4 |
| **Δ7** | **Sunum revizyonları** `[K4/K5/K7/K8/K10/K11]` | (a) Failure sunumu: kapatılabilir banner YOK → **sticky şerit hata kümesi** (✗ `5 failed` + `· 4 dependency-affected` + ilk 3 chip + `+N more`→Failed filtresi); T39 böyle okunur. (b) DD12 → **OS tray balloon** (ilk sefer). (c) Perf: **sabit 6/4/2 + priority + CPU cap**. (d) **Settings = LAYERS + REPOSITORY satırı** (kök + Change…); kalan A10 alanları config JSON'da. (e) Success flourish YALNIZ stream done satırı glow'u — liste/graf yeşil dalga YOK (T44 daraltıldı). (f) Graf düğümü = **4px-radius kare**. (g) Görünüm modları **quad/list/focus** (title bar, preset 50/50/50) + splitter sınırları %28-72/%18-82 + layout persist. (h) **Open in VS** = bağlı sln (T32) → vswhere→devenv. (i) DD13 "Build yanında etiket" düşürüldü — worktree chip değeri + title bar `· main-2` eki yeterli glance sinyali. | A7, A10, A11, T32, T37–T45 |
| **Δ8** | **Motor küçük ekleri** | (a) `BuildState.lastDurationMs` — ETA girdisi; **ETA formülü:** (queued süre tahminleri toplamı + building kalanları)/paralellik + (building varsa) 400ms; üstel yumuşatma 0.75·eski+0.25·yeni; gösterim 5s'e yuvarlanır, <4s → `· almost done`. (b) willBuild `hollow` = **Sync öncesi VEYA imza hesaplanamadı (null)** (birleşik tanım). (c) **Succeeded olan projenin will-dot'u ANINDA griye (clean) döner** — canlı geçiş. | A6, A9, T38, T53, yeni **T70** |

---

## Global Constraints

Aşağıdaki değerler **her task'ın** örtük gereksinimidir; ihlal = plan ihlali. v6'dan korunur; `[v7Δ]` işaretliler eklendi/güncellendi.

- **Derleme motoru = `MSBuild.exe`** (`vswhere -latest -requires Microsoft.Component.MSBuild`), **`dotnet build` DEĞİL** — hedef repo 175/191 legacy .NET Framework. Packages.config için `nuget restore` / `msbuild -t:restore`, per project. `[D10]`
- **§3.4 flag'leri (v1): `-p:UseSharedCompilation=false -nodeReuse:false`** — korunur; kaldırma yalnız T33 fast-follow'da, spike kanıtlarsa. `[D9]`
- **§6.1 garantisi = nested Job Object** (managed watcher YOK): App outer Job (`KILL_ON_JOB_CLOSE`) → Supervisor → inner Job (`KILL_ON_JOB_CLOSE` + CPU rate) → `MSBuild.exe` child'ları; hepsi `CREATE_SUSPENDED → AssignProcessToJobObject → ResumeThread`. App ölünce kaskat ölür. `[§3]`
- **Shell-out per project**, in-process MSBuild (BuildManager) **asla**. `[§0]`
- **Ortak OutDir'e DOKUNULMAZ/OKUNMAZ.** "Değişti mi" yalnız kaynak sinyalinden. DLL/bin timestamp asla okunmaz. `[§4]`
- **build-state GLOBAL** (projectId anahtarlı), single-writer serialized + atomik temp+rename, her proje bitiminde persist. `[§4/§6 · D2]`
- **Signature = tek Core `BuildSignature.Compute`** (byte-stable, determinism testli): `config + HEAD commit + (in-place'de) local-diff hash + transitive upstream producer signatures`. `[D6]`
- **Graf primer = HintPath-basename→producer**; ProjectReference ikincil. Skip GLOBAL graf propagation'a bağlı. `[D11]`
- **Tüm planlama saf Core'da** (`BuildPlan` DTO); Supervisor yalnız yürütür. `[D3]`
- **IPC = stdio NDJSON, framed**; **stdout YALNIZ NDJSON**, tüm logging stderr/dosyaya. `[D4]`
- **Per-run disk log:** `%LOCALAPPDATA%\BuildOrchestrator\logs\run-<ts>\`; bellek ring buffer YOK. `[D4]`
- **Worktree havuzu:** `%LOCALAPPDATA%\BuildOrchestrator\worktrees\<name>\`, **kalıcı** (hız/obj cache), per-worktree silinebilir. Çıktı da **ortak bin'e** yazar (izole değil — kasıtlı); etiket "committed **kaynak**". `[N3 · D12]`
- **Tek `ProcessRunner`**: zorunlu exit-code + stderr + timeout. `[D7]`
- **OS reduced-motion'a saygı** (uygulama-içi toggle YOK); algılama `SystemParameters.ClientAreaAnimation` + `StaticPropertyChanged` canlı takip — Chromium'un `prefers-reduced-motion` için okuduğu AYNI OS sinyali. `[DD3 · v7Δ-6]`
- **Console = mutlaka monospace**; status = **glyph + renk + metin** (colorblind-safe; ≥4.5:1). `[DD3/DD4]`
- **[v7Δ-2] Sync başında `git fetch origin <branch>` (ref-only)** — checkout/pull ASLA; ağ yoksa warn + yerel HEAD ile devam. `[K1]`
- **[v7Δ-1] Görsel otorite = design-v1** README + prototip; sim metinleri placeholder istisnası (format birebir, sayılar/yollar/TFM gerçek veriden).
- **[v7Δ-6] Fidelity çerçevesi:** hedef piksel-hassas; A13'teki yapısal farklar "algısal eşdeğer" kabul sınıfındadır — iş gücüyle kapatılmaya çalışılmaz.
- **v1 = tek repo.** `[§12]`
- **Scan ignore:** `.git bin obj node_modules .vs`. **Build-etkileyen uzantılar:** `.cs .xaml .resx .csproj .props .targets`. `[§5/§6]`
- **Hedef repo (girdi, varlık değil):** `D:\Projects\Delta\OSYS` — 191 csproj · 45 sln · 21 packages.config · 175 legacy (152×v4.6 + 23×v4.8) · 1927 HintPath · 178 post-build copy. `[Eng yer-gerçeği]`

---

# PART A — Konsolide Tasarım (tek doğru kaynak)

## A0. Temel Kararlar

| Karar | Seçim (GÜNCEL) | İz |
|---|---|---|
| Derleme motoru | **`MSBuild.exe` (vswhere) + nuget restore**, shell-out child process | D10 |
| Process topolojisi | App (UI) + Supervisor (engine) ayrı process | §0 |
| §6.1 garantisi | Nested Job Object (managed watcher değil) | §3 · D1 |
| Teslim | Walking-skeleton / dikey dilim, **Iteration -1 spike GATE** | §0 · D13 |
| Hedef framework (araç) | .NET 10 (LTS) + WPF — **fizibilite analiziyle doğrulandı** | §0 · **v7Δ-6** |
| v1 kapsam dışı | Multi-repo | §0 |
| §3.4 flag'leri | v1'de korunur; T33 fast-follow kanıta bağlı | D9 |
| Worktree modeli | Branch-driven: branch seçimi belirler, ayrı local-toggle yok | v6Δ-1 · D12/N9 |
| Sol panel graf | Dependency graf paneli (yerleşik DAG, canlı frontier) IN-scope | v6Δ-2 |
| **Görsel otorite** | **design-v1 README + HTML prototip (piksel-hassas, final)** | **v7Δ-1** |
| **Sync semantiği** | **`git fetch origin` (ref-only) dahil** | **v7Δ-2 · K1** |
| **Konsol host** | **AvalonEdit (read-only + colorizer)** — metin seçimi + renk + hacim | **v7Δ-6** |
| **Kısayol şeması** | **F5/Ctrl+F5 · Ctrl+F · Esc · Alt+B** (çift-Shift/Ctrl+P kalktı) | **v7Δ-5 · K6** |
| **Perf modeli** | **Sabit 6/4/2 + priority + Job CPU cap** | **v7Δ-7 · K11** |

## A1. Amaç & Temel Akış

Tek masaüstü uygulamadan, tek git repo altındaki yüzlerce birbirine bağımlı C#/WPF projesini bağımlılık sırasına göre, paralel ve yalnız değişenleri derlemek. Akış: **Sync (fetch dahil `[v7Δ-2]`) → Branch seç → (Debug/Release, worktree) → Build/Rebuild → Canlı çıktı (graf + liste + console + stream) → (gerekirse Stop → Continue / Retry failed `[v7Δ-4]`).** Build her zaman implicit Sync koşar (`[v7Δ-1]` — D5 cache sayesinde ucuz; elapsed/ETA sayacı Sync süresini içermez). `[§1 · v6Δ-2/Δ5]`

## A2. Mimari & Projeler

| Proje | TFM | Sorumluluk |
|---|---|---|
| `BuildOrchestrator.Core` | net10.0 | Saf çekirdek: scanner, graph (HintPath→producer), **tüm planlama** (BuildSignature, GLOBAL propagation, layer, sıra-koruyan scheduler → `BuildPlan` DTO), **pre-run dirty/will-build kümesi** `[v6Δ-3]`, **depIssue propagation `[v7Δ-3]`**, state & config persistence. UI/process bağımsız. `[D3]` |
| `BuildOrchestrator.Contracts` | net10.0 | IPC sözleşmesi: command/event DTO, enum, **`BuildPlan`**, polimorfik JSON. |
| `BuildOrchestrator.Supervisor` | net10.0-windows (console) | Orchestration: inner Job, `MSBuild.exe`/nuget shell-out, build kuyruğu, per-run disk log, IPC server (stdout NDJSON-only). Planı yalnız yürütür. |
| `BuildOrchestrator.App` | net10.0-windows (WPF) | UI/MVVM, tray, single-instance, autostart, outer Job, IPC client, custom dark title bar. **Dependency graf render+animasyon burada.** `[v6Δ-2]` **UI teknik altyapısı `[v7Δ-6]`:** AvalonEdit konsol, TrackedTextBlock, sticky-overlay, ScrollAnimator, MotionCoordinator, DS kontrol/tooltip kütüphanesi. Supervisor spawn eder. |
| `BuildOrchestrator.Tests` | net10.0 (xUnit) | Core unit + process-control + integration. |

**İlkeler:** App, Supervisor assembly'sine referans vermez. DI baştan kurulu. IPC stdio NDJSON; stdout yalnız NDJSON. `[§2 · D3/D4]`

## A3. Process Kontrolü & Güvenli Durdurma (KRİTİK — değişmedi)

Nested Job topolojisi. Kurallar `[§3]`: Outer Job (App) `KILL_ON_JOB_CLOSE` → Supervisor suspended→assign→resume; Inner Job (Supervisor) her `MSBuild.exe` child'ı suspended→assign→resume; **cascade kill** App ölünce deterministik; Roslyn paylaşımlı derleyici v1'de kapalı; **Graceful Stop copy-aware (2A)** proje sınırında; **Hard Stop** `TerminateJobObject(inner)` proje sınırında; **Pencere X → tray'e küçülür** (ilk seferde **OS tray balloon** bilgilendirmesi `[v7Δ-7 · K5]` — uygulama içi toast DEĞİL); **Tray Exit → cascade-kill**, tray'den Stop da; **hata derlemeyi öldürmez.** **[v7Δ-4] Stop sonrası kalanlar queued kalır — Continue ile devam edilebilir.**

**Spike şartı (D1):** build job İÇİNDE tamamlanır + breakaway flag GEREKMEZ (T23 probe). **Kabul (deterministik, sleep yok):** X→tray, build devam; Exit/kill/crash → **≤2sn** artık process yok (handle/IOCP); Stop → graceful ya da hard-kill, ortak bin'de torn DLL yok. `[D8]`

## A4. Çıktı Dizini Gerçeği (KRİTİK — değişmedi)

- Çıktı = projelerin KENDİ post-build copy event'leri; orchestrator kopyalamaz. Ortak dizine dokunulmaz/okunmaz. **VS-parity zorunlu.** `[§4]`
- Final çıktı branch'e göre izole edilmez (bilinçli). **Config tek klasör (config-agnostic):** config değişimi tüm projeleri dirty yapar. `[A6]`
- Yalnız ara çıktı (obj) worktree build'lerinde izole; proje **Id (tam yol)** ile çakışma önlenir.
- build-state GLOBAL. **Worktree çıktı-izolasyonu YOKTUR (D12).** Concurrent-VS guard (T29) + tek-run kilidi korunur.

## A5. Sync — Proje Keşfi & Bağımlılık Grafiği

- **[v7Δ-2 · K1] Sync akışının İLK adımı: `git fetch origin <branch>` (ref-only).** Checkout/pull ASLA (v6Δ-1 "aktif branch değişmez" korunur). Hedef SHA = fetch sonrası remote-tracking ref; kartlardaki `curSha → targetSha` (N10) ve sticky şerit `▸ Sync — git fetch origin…` metni buna dayanır. Ağ/remote yoksa: konsola warn + yerel branch HEAD hedef alınarak devam (degrade). N1 granular adım satırları (`Solution'lar taranıyor (N)`, `HintPath/Compile okunuyor`, `Graf/cycle`, `Sıra belirlendi (N)`) tasarımın dim/info konsol diliyle fetch satırının ardından basılır.
- Kökte `*.sln` + `*.csproj` recursive tara (ignore listesi). 45 sln kökü.
- **Graf primer = HintPath-basename→producer (D11):** evaluated AssemblyName/TargetName'den DLL-adı→üretici haritası; HintPath raw-reference'lar bu harita ile kenara çevrilir. **ProjectReference ikincil.**
- **Batch MSBuild evaluation (D5):** AssemblyName + ProjectReference + Compile item'ları tek geçişte; **mtime+hash cache** ile invalidation.
- **file→project = MSBuild-evaluated Compile item'larından (D11), path-prefix DEĞİL.**
- Tarjan SCC (cycle) + Kahn (topo). Atomik `dependency-graph.json` cache. Açılışta cache'ten; tam analiz yalnız Sync.
- **Bağımlılık sağlık göstergesi → cycle rozetine indirgendi `[v6Δ-3]`:** cycle = **kırmızı rozet + tooltip**. O görsel yuva **will-build noktasına** ayrıldı.
- **Liste sırası = build order:** topo sıraya göre. **Katman pattern varsa** katmanlara göre gruplanır (her katman kendi içinde topo). Sticky ara başlıklar = **katman adları**; **[v7Δ-6] birikimli yapışma** (i'inci görünür başlık `top=i×24px`) — WPF'te overlay mimarisiyle (T58). `[N8]`
- **Solution belirsizliği (OV#6):** csproj 0/>1 sln; `solutionNames` çok-değerli; "VS'de Aç" >1 ise seçtir. `[T32]`
- **Graf verisi App'e (`[v6Δ-2]`):** Sync sonrası Core, node + kenar + katman + build-order + cycle bilgisini `ProjectNode`/graf DTO'da üretir; App graf panelini bu veriyle çizer.

## A6. Derleme Stratejisi

**Rebuild:** tüm projeler topo sıraya göre; bağımsızlar paralel `MSBuild.exe`.

**Build (incremental) — kalp:** bir proje **yalnız** şu hallerde derlenir: (1) güncel commit ≠ son başarılı commit ve projeyi etkiliyor, (2) working-tree'de projeyi etkileyen local değişiklik, (3) upstream producer imzası değişti (GLOBAL propagation), (4) hiç başarıyla derlenmemiş. Aksi halde **Skipped**.

- DLL/bin timestamp **asla** okunmaz. Dosya→proje: MSBuild-evaluated Compile item + build-etkileyen uzantı → dirty. Üst `Directory.Build.props/targets` → kapsam dirty.
- **Downstream propagation GLOBAL graf üzerinden (T25):** **Safe (varsayılan)** = dirty + transitif; **Fast** = sadece dirty.
- **Signature (D6)** ve **build-state.json (D2)** kuralları değişmedi.
- **Config değişimi (Debug↔Release) → TÜM projeler dirty**; action-bar segment toggle + mini-uyarı `[v6Δ-5d]`.
- **Pre-run will-build kümesi (`[v6Δ-3]` + `[v7Δ-8]`):** Core, run'dan ÖNCE her proje için `willBuild:bool?` türetir. **hollow/unknown = Sync öncesi VEYA imza hesaplanamadı (null)** — birleşik tanım. **Koşu içi canlı geçiş: succeeded olan projenin dot'u ANINDA griye (clean) döner** — "artık güncel". Cycle projeleri `willBuild=false` + cycle rozeti.
- **[v7Δ-3] Hatalı bağımlılık ALT PROJELERİ BLOKLAMAZ:** scheduler'da bağımlılık "çözüldü" = `succeeded | failed | skipped`. Bağımlılar **son başarılı çıktıyla** derlenir (D12 ortak-bin modeliyle uyumlu); kök hata adları `depIssue` olarak zincir boyunca aşağı taşınır (doğrudan failed dep'ler + miras alınan kökler birleşir). Bu projeler: log başında warn satır(lar)ı alır (`warning: OSYS.Sales.Core failed in this run — last successful output referenced (…)`; dolaylıysa `warning: failure in dependency chain (…) — referenced outputs may be stale`), kartta/grafta ▲ rozet taşır, `dependency-affected` sayacına girer, stream'de `built — dependency issue (2.4s)`.
- **Temiz projeler dalga dalga skip** edilir (bağımlılıkları çözülünce; tek seferde hepsi değil).
- **[v7Δ-8] ETA:** (queued süre tahminleri toplamı + building kalanları) / paralellik + (building varsa) 400ms; üstel yumuşatma 0.75·eski + 0.25·yeni; gösterim 5s'e yuvarlanır, <4s → `· almost done`. Süre tahmini girdisi = `BuildState.lastDurationMs` (T70).

**Paralellik & kaynak `[v7Δ-7 · K11]`:** bağımsızlar eşzamanlı; **Performans modu = sabit paralellik Full(6)/Balanced(4)/Light(2) + process priority + inner Job CPU rate cap (Full=sınırsız, Balanced≈%70, Light≈%40)**. Çalışırken değiştirilebilir; konsola dim not: `parallelism: 4 · cpu cap 70%`. `[§6]`

**Sıra-koruyan paralel scheduler (deterministik) — [v7Δ · K2] TEYİT:** ready set'ten slot boşalınca build-order'da en önde gelen seçilir (rastgele/hash YOK); bağımlılığı bekleyenin ÜZERİNDEN ATLANIR. Aynı graf+derece → aynı dispatch. *(design README §3.2'deki "İLERİ ATLANMAZ / break" kuralı sim sadeleştirmesidir — Core'a taşınmaz; UI görünümü etkilenmez.)* `[§6]`

**Katman pattern — layered build (N8):** semantik değişmedi. Sıralı sınırsız regex + isim; ilk eşleşen kazanır; sert faz bariyeri; katman-içi topo+paralel dispatch; incremental propagation GLOBAL. Eşleşmeyenler → "Other" (ad design-v1 İngilizce copy'sinden `[v7Δ-1]`; v6'daki "Diğerleri"nin karşılığı). Pattern yoksa başlıksız tek liste. Ters katman bağımlılığı: tespit + warn-only (3C). `[T15]`

**Branch & worktree — BRANCH-DRIVEN MODEL `[v6Δ-1]` (değişmedi) + `[v7Δ-5 · K3]`:**
- Açılışta aktif branch seçili. **Branch seçimi = niyet**; Build'e basılana kadar git'te işlem yok. Branch chip aranabilir.
- **Ayrı "local dahil/hariç" toggle YOK.** 3-durum matrisi (v6) aynen geçerli: aktif+OFF=in-place+local · aktif+ON=worktree committed · farklı=zorunlu ON.
- **[v7Δ-5 · K3] Konsol satırı:** farklı branch seçilince cmd görünümlü satır YERİNE **niyet bildirimi**: `branch target: release/2026.06 (f3a02c8) — worktree will be used at Build` + `Branch changed: release/2026.06 — Sync required`. `git switch --detach` satırı KALKTI (yanlış komut + yanlış zamanlama). Gerçek `git worktree add --detach …` Build anında cmd satırı olarak loglanır. Proje durumları seçimde sıfırlanır (discovered/unknown), faz boot'a döner (design davranışı korunur).
- Worktree isimlendirme/seçim/silme + branch guard + pool cap (T14) değişmedi. Auto-ad algoritması `[v7Δ-1]`: slug = branch adında `/`→`-`; ek sayı = aynı prefix'li mevcut sayısı+1.
- Git komut sonuçları kontrol edilir; hata → `error` event.

**Eşzamanlılık:** orchestrator tek seferde tek run; OutDir kendi-kendine çakışmayı önler.

## A7. UI / UX (tek pencere — OTORİTE: design-v1 `[v7Δ-1]` + DD1–DD14)

> **[v7Δ-1] Otorite notu:** Bu bölüm davranış/kapsam listesidir; **tüm görsel kesin değerler** (renk token'ları, ölçüler, tipografi, animasyon süreleri/eğrileri, kopya metinleri) **design-v1 README §1–§3 + prototip kaynaklarındadır** ve oradan uygulanır. Görsel çelişkide design-v1 kazanır. README'de olmayan 25 prototip davranışı (fizibilite raporu **Ek A**) spec kapsamındadır — implementasyonda birebir taşınır.

**North-star (DD4):** sakin-hassas dark + heyecanlı frontier. Heyecanın kaynağı = build-frontier'ın grafta ve listede aşağı akması. `[v6Δ-2]`

**Pencere kabuğu `[v7Δ-6/Δ7]`:** custom dark title bar (`WindowChrome` CaptionHeight=40, UseAeroCaptionButtons=false); repo·branch başlıkta (`OSYS · main`, worktree aktifse `· main-2`); sağda 3 görünüm modu ikonu (quad/list/focus) + dişli (tooltip artık genel: `Settings` `[K10]`) + min/max/close. `X` → tray'e küçülür (**ilk seferde OS tray balloon** `[K5]`). App icon taskbar+tray+pencerede (SVG→çoklu-boyut ICO; 16/24px elle netleştirilir). **Teknik zorunluluklar:** Win11 köşe = `DWMWA_WINDOW_CORNER_PREFERENCE` (AllowsTransparency ASLA); 1px çerçeve = `DWMWA_BORDER_COLOR`; **maximize'da içerik taşması düzeltmesi ZORUNLU** (Padding = resize-border; dotnet/wpf#3887); **Snap Layouts** = WM_NCHITTEST'te HTMAXBUTTON; **maximize'da restore glyph swap `[K8]`**; single-instance'ta `AllowSetForegroundWindow`; min 1240×620.

**Reconciled Information Architecture (v6 IA aynen — 2 kolon × 2 satır + 3 splitter):**
```
┌───────────────────────────────────────────────────────────────────────┐
│ [◆Delta] Build Orchestrator  OSYS · main      [⊞][≡][▣] ⚙  — □ ×      │ title bar 40px
├───────────────────────────────────────────────────────────────────────┤
│ ▸ Building 8/120 · 1m04s · ~40s left  [chip][chip]  ✗ 5 failed +3 more │ sticky şerit 32px + 2px progress
├──────────────────────────────┬────────────────────────────────────────┤
│ ② DEPENDENCY GRAPH (sol-üst) │ ③ CONSOLE (sağ-üst, AvalonEdit)        │
│ ══════(yatay splitter)═══════ │═════════(yatay splitter)══════════════ │
│ ④ PROJECTS (sol-alt)         │ ⑤ EVENT STREAM (sağ-alt)               │
├──────────────────────────────┴────────────────────────────────────────┤
│ ⟳Sync Σ36 ⟳4 ✓14 ✗5 —17 ▲4   branch▾ worktree▾ [Debug|Release] perf  Build▴│ action bar 42px
└───────────────────────────────────────────────────────────────────────┘
```
**[v7Δ-7] Görünüm modları:** title bar'daki 3 ikon — **quad** (varsayılan; preset'e dönüş split'leri 50/50/50'ye sıfırlar) · **list** (graf gizli; sol kolon tamamen liste) · **focus** (graf gizli; sağda konsol %76). Splitter'lar: 7px tutma alanı + görünür 1px çizgi (sürüklerken amber); sınırlar kolon %28–72, satırlar %18–82; konumlar + mod persist (user settings JSON). *(v6'daki ~%46/%54 varsayılanı yerine design-v1 50/50/50 preset'i geçerlidir.)*

**Attention order (DD5 — korunur):** ① ne oluyor (frontier + global progress) → ② graf+liste (mekânsal) + özet stream (zamansal) → ③ per-project detay. Hiyerarşi renkle değil **ağırlıkla**.

**② Dependency graf paneli (`[v6Δ-2]` + `[v7Δ-6/Δ7]`):**
- Yerleşik katmanlı DAG (satır aralığı 96px); kenarlar yukarıdan aşağı kübik bezier. **Düğüm = 26px, 4px-radius KARE `[K4]`** + 13px package ikonu + altında mono 10px kısa ad (`OSYS.` öneki atılır); discovered kesikli çerçeve (`Rectangle.StrokeDashArray` — WPF Border dashed desteklemez).
- Kenar stilleri/dash-flow/seçim sönmesi/kamera/rozet: design-v1 §2.3 birebir. **Teknik `[v7Δ-6]`:** dash birimleri StrokeThickness ÇARPANI (1px'te birebir; 1.6px kenarda böl); akan kenarlar TEK paylaşımlı clock; kamera = From'suz To-animasyonu + SnapshotAndReplace (CSS retarget paritesi) + frontier küçük-sapma eşiği; **graf etiketlerinde `TextFormattingMode=Ideal`** (Display, scale altında bozar); ilk açılış katman stagger'ında başlangıç Opacity=0 set edilir (flash önlenir).
- **Ölçek (T51/T63):** ≤~150 düğüm Shapes yolu (tasarımın 36'sı bu bantta); üstünde 3 katman: EdgeLayer (tek OnRender) + NodeLayer (DrawingVisual; katman host'ları UIElement — ContainerVisual.Opacity animate edilemez) + FlowOverlay (akan dash kenarları HER ZAMAN UIElement Path). Cull + GlyphRun cache; zoom<0.8 etiket LOD'u ayrı tasarım kararı.
- Reduced-motion: statik dash + renk güncellemesi (DD3).

**③ Console (sağ-üst) `[v7Δ-6]`:** **Host = read-only AvalonEdit** — "metin seçimi (DD8 kutsal) + renkli satır + MSBuild-verbose hacim" üçlüsünün WPF'teki tek yolu (TextBlock seçim vermez; FlowDocument hacimde çöker). Satırlar düz metin (`HH:MM:SS ▸ metin` — kopyalanan da anlamlı), renkler offset-bazlı `DocumentColorizingTransformer`. **Hibrit aktif satır:** en yeni satır dokümana girmeden altındaki TextBlock'ta daktilolanır (Stopwatch-bazlı, satır ≤250ms), bitince commit — aktif satır ~250ms seçilemez (bilinçli, en temiz çözüm). İmleç = 7×13 Rectangle (font glyph'i değil). **Satır yüksekliği 1.55:** CompositeFont `LineSpacing` sarmalaması — **It-0 spike'ı** (tutmazsa ~%10-15 sıkışık kabulü). **Proje logu kaskatı:** tempo birebir (26ms'de 3 satır); satır başına translateY+scale pop-in AvalonEdit'te YAPILAMAZ → opacity-fade eşdeğeri (yapısal fark, A13). Tampon/`⌄ latest` pill/alta-yapışma (48px eşik + jumping penceresi)/iki mod/Back başlığı/boş-durum metinleri: design-v1 §2.5 birebir. **Copy log butonu `[v7Δ-1]`** (Clipboard retry sarmalayıcıyla — CLIPBRD_E_CANT_OPEN). **Hacim:** IPC background → Channel → ~50ms batch flush → `BeginUpdate/Insert/EndUpdate`; typing degradation (DD2) korunur.

**⑤ Event stream (sağ-alt):** ListBox + virtualization (satır=click-to-select; metin seçimi gerekmez). Satır görselleri/typewriter/burst-instant (<340ms, fail=anında)/aktif satır/glow-once/pill: design-v1 §2.6 birebir. Glow-once'ta recycle tekrar-oynatma bayrağı (VM `GlowPlayed`).

**Konsol temizleme + granular adım logu (N1):** Sync/Build/Rebuild'e basınca konsol temizlenir + adımlar baştan (fetch satırı dahil `[v7Δ-2]`). **[v7Δ-7] Run başı cmd satırı** solution-level msbuild İZLENİMİ VERMEZ — orchestrator-özet biçimi: `build — 14 projects, parallelism 4, Debug` (motor gerçeği = proje-başına shell-out; gerçek MSBuild komutları proje logunda).

**Özet stream satırları (`[v6Δ-5a]`):** değişmedi — hover/seçili/tekrar-tıkla-deselect kartla birebir.

**Typing / live-line degradation (DD2):** değişmedi — drop-to-latest; throughput-suspend; hatalar anında; ham MSBuild asla harf-harf.

**Saklama mimarisi (4A/D4):** değişmedi — per-run disk; `getProjectLog` chunk + canlı interleave (AvalonEdit'te scroll telafili prepend `[v7Δ-6]`).

**Kart seçim modeli (`[v6Δ-5b]`):** değişmedi. **[v7Δ] Netlik:** seçili satır zemini `surface-raised` — "kutu/border yok" kuralını bozmaz (çerçeve çizilmiyor; yüzey adımı hover diliyle tutarlı).

**Tek canonical click→detay (DD8):** değişmedi; console'da metin seçimi kutsal (AvalonEdit bunu native verir `[v7Δ-6]`).

**Durumlar:** Discovered, Queued, Building, Succeeded, Failed, Skipped, CycleDetected(+rozet) + **ortogonal: will-build dot ve depIssue ▲ `[v7Δ-3]`**. Statü = renk + metin/rozet + ince-halka daire-içi glyph.

**Kartlar (`[v6Δ-3/Δ5e/f]` + `[v7Δ-1/Δ3]`):** design-v1 §2.4 birebir — 2px statü şeridi (seçilide 3px), 8px will-dot, ad+sln, hover'da 2 ikon / hover yokken dirty'de `curSha → targetSha`, statü glyph, **sabit 14px depIssue slotu (hiza asla bozulmaz)**, 46px süre kolonu; building nefesi (yalnız sabit "nefes"); failed shake (hata anından 700ms pencerede, bir kez). **[v7Δ-1] Proje arama inputu** panel başlığında (`Filter…`, Ctrl+F odak; Esc=temizle+blur, global zincire sızmaz; statü filtresiyle AND; yalnız proje adında arar).

**Build frontier:** değişmedi — liste build-order; follow-mode (550ms/54px dead-band); sticky şerit chip'leri (≤4 + `+N`); graf frontier senkron. **[v7Δ-6] Follow/seçim scroll hedefleri LayoutMetrics offset tablosundan** (sticky ile TEK ortak servis); **virtualization stratejisi:** liste virtualization KAPALI başlar (birkaç yüz satır sorunsuz; OSYS 191 bu bantta) — 500+ hedefinde drift-kalibrasyonlu açılır (T58/T51).

**Motion budget (DD9):** değişmedi — 1 hero (graf+liste frontier AYNI hero); **[v7Δ-6] `MotionCoordinator` servisi tek kapı**; dekoratif sonsuz animasyonlara `DesiredFrameRate=30`; sayaçlar tek DispatcherTimer'dan. WPF gerçeği: animasyonlar UI thread'te tick'lenir (compositor yok) — UI thread'te iş yasağı mimari kural (Supervisor ayrımı ana avantaj).

**OS reduced-motion (DD3):** değişmedi; algılama `SystemParameters.ClientAreaAnimation` (canlı takip), tüm süre/eğri token'ları tek ResourceDictionary'den (topluca 0'lanır).

**Global progress / ETA (DD6 + `[v7Δ-8]`):** `▸ Building 7/14 · 24s · ~35s left`; ETA formülü A6'da; 2px progress radius 0, faz renkli, sync'te indeterminate (custom template).

**Interaction state'leri (DD10/Pass2 + `[v7Δ-7]`):** pre-first-run (`Choose Folder` → OpenFolderDialog; seçimde otomatik Sync `[v7Δ-1]`); 0-proje; 0-branch/git-fail → konsol error + sticky şeritte kırmızı faz + Sync ile retry; all-skipped DELIGHT (`Everything up to date — 36 projects checked in 8.4s, nothing to build`); partial; **engine-died → sticky şeridin kalıcı hata modu + `Restart engine` aksiyonu (banner/toast YOK)**; sync skeleton yerine indeterminate progress yeterli (bilinçli karar).

**Failure orchestration (DD11 + `[v6Δ-4]` + `[v7Δ-7]`):** hata anında stream ANINDA anons (typing atlanır). **Sunum = sticky şerit hata kümesi** (kapatılabilir banner YOK — design kararı): ✗ glyph + `5 failed` + `· 4 dependency-affected` (dim) + ilk 3 hatalı chip (tıkla→seç) + `+N more` chip (tıkla→**Failed filtresi** — Δ4'ün [Failed'a git] eşdeğeri). v6'daki "✗ Failed çipi öne" mikro-davranışı design-v1'in sabit sayaç-chip sırasına devredildi (sıra değişmez; hata varken ✗ sayacı zaten kırmızı vurgulu). Her hatalı satır KENDİ logu (DD8). Birleşik hata görünümü / çoklu-seçim YOK. Shake yalnız ikincil ipucu.

**Sync reveal + success flourish (DD14 + `[v7Δ-7]`):** kartlar build-order stagger fade-in (satır 10ms, tavan 380ms; graf katman 55ms, tavan 330ms `[v7Δ-1]`); **flourish YALNIZ stream done satırı glow-once** (success-soft→transparent 1.1s, sıfır hatalı koşuda) — liste/graf yeşil dalga YOK (T44 daraltıldı; "süpürme/parlama istenmedi" kararıyla tutarlı).

**Worktree chip iki sinyal (DD13 + `[v7Δ-7]`):** toggle + seçici + `source` satırı popover'da; **glance sinyali = chip değeri (`worktree: main-2`) + title bar `· main-2` eki** — Build yanında ayrı etiket beklentisi düşürüldü.

**Kısayollar & global hotkey (N6 — `[v7Δ-5 · K6]` REVİZE):** **F5 = Build** (stopped'ta **Continue**; koşarken **Stop**) · **Ctrl+F5 / Shift+F5 = Rebuild** · **Ctrl+F = proje filtre inputuna odak** · **Esc** = dialog → popover/menü → seçim zinciri · **Alt+B global hotkey** (RegisterHotKey, ayarlanabilir; çakışmada sessiz devre dışı + ayarlardan değiştirilebilir) → tray'den pencereyi getirir (animasyon reduced-motion'a tabi). **Çift-Shift ve Ctrl+P KALDIRILDI; v6'daki Ctrl+B/Ctrl+R kısayolları F5/Ctrl+F5 ile İKAME edildi.** Keyboard nav: liste satırları focusable (tabIndex) + Enter=seçim toggle, ok tuşları, focus-visible ring (2px amber %50, offset 1) `[v7Δ-1]`.

**Anti-slop (Pass4 + `[v6Δ-5f]`):** değişmedi — glyph ≠ emoji; Geist/Geist Mono; restrained radius (console 0); dekoratif gölge yok (yalnız floating overlay); dolu renkli rozet yasak / amaçlı halka glyph serbest; dönen globe yasak.

**Auto-scroll arbitration (T48):** değişmedi — user-scroll bölgesel duraklatır; öncelik frontier > console > stream; yo-yo yasak (animasyon handoff + suppress bayrağı `[v7Δ-6]`).

**Tasarım niyeti (N4 — `[v7Δ-1]` REVİZE):** kesin görsel **design-v1 paketindedir**; token'lar `prototype/_ds/…/tokens/*.css` → WPF ResourceDictionary'ye çevrilir (T49 = bu çeviri + `TextOptions` kararları). Geist/Geist Mono **statik OTF** olarak gömülür (vercel/geist-font GitHub sürümü; variable font YASAK — WPF eksen desteklemez; Google CDN build'i KULLANMA — OpenType tabloları kırpık olabilir).

## A8. Test Stratejisi

- **Unit (Core):** v6 listesi aynen + **[v7Δ]**: depIssue propagation (doğrudan/dolaylı kök birleşimi, resolved={succ,fail,skip}) `[Δ3]` · continueRun/retryFailed kümeleri (failed+transitif) `[Δ4]` · ETA formülü (EMA + yuvarlama + almost-done eşiği) `[Δ8]` · willBuild hollow birleşik tanımı + succeeded→clean geçişi `[Δ8]` · fetch-degrade (ağ yok → yerel HEAD + warn) `[Δ2]` · `fmtDur`/`fmtElapsed` InvariantCulture (9950ms eşiği dahil) `[Δ1]`.
- **Process-control (ZORUNLU, deterministik — D8):** değişmedi (≤2sn, torn DLL yok, no-breakaway).
- **State/IPC:** değişmedi + `lastDurationMs` persist `[Δ8]`.
- **Integration:** v6 listesi + **Stop→Continue ve Retry-failed akışları** `[Δ4]` + fetch'li Sync `[Δ2]`.
- **Perf:** 500+ kart akıcı (virtualization stratejisi T58 ile); cold Sync + cache-hit; log akışında UI bloklanmaz (AvalonEdit batch flush `[Δ6]`); graf 500–1000 node (T51/T63); **UI thread animasyon bütçesi profiling** `[Δ6]`.
- **[v7Δ-6] Görsel doğrulama:** It-4 başında font A/B karşılaştırması (T65 karar kapısı — K9); CompositeFont line-height spike çıktısı (It-0).

## A9. Supervisor ↔ UI Sözleşmesi

- **Komutlar:** `syncWorkspace(rootPath)` (**fetch dahil `[v7Δ-2]`**), `reanalyze()`, `listBranches()`, `listWorktrees()`, `selectBranch(branch)`, `startRun(mode, branch, useWorktree, worktreeName?, config, dependentMode, perfMode)`, `setPerfMode(perfMode)`, `stopRun(runId)`, `getProjectLog(projectId)`, `deleteWorktree(name)`, `openPath(projectId)`, `openInVS(projectId)` (**bağlı sln çözümü + >1 ise UI seçtirir — T32; vswhere→devenv `[v7Δ-7 · K7]`**).
- **[v7Δ-4] `RunRequest.mode` genişledi:** `'build' | 'rebuild' | 'continue' | 'retryFailed'` — `continue`: kalan queued'lar mevcut plandan sürer (elapsed korunur); `retryFailed`: failed + transitif bağımlılar yeni willBuild kümesi (konsol/stream sıfırlanmaz).
- **Eventler:** v6 seti aynen (`runBlocked` yok) + `buildPreview` (willBuild). **[v7Δ-3] `projectSucceeded`/`projectFailed` + `runCompleted` genişler** (aşağıda).
- **Tipler (`[v7Δ]` ile):**
  - `ProjectNode { id, name, projectPath, solutionNames[], dependencies[], buildOrder, layerIndex?, layerName?, inCycle:bool, willBuild?:bool }` — değişmedi (hollow=null birleşik tanımı `[Δ8]`).
  - `BuildState { projectId, builtSignature, builtCommit?, lastResult, lastRunAt, lastBranch?, lastDurationMs? }` — **`lastDurationMs` EKLENDİ `[v7Δ-8]`** (ETA girdisi).
  - `ProjectResult { projectId, result, durationMs, reason?, builtCommit?, targetCommit?, depIssues?:string[] }` — **`depIssues` EKLENDİ `[v7Δ-3]`** (kök hatalı bağımlılık adları; UI ▲ rozet/tooltip/log-warn üretir).
  - `runCompleted` totals: `{ succeeded, failed, skipped, durationMs, depIssueCount }` — **`depIssueCount` EKLENDİ `[v7Δ-3]`**.
  - `Worktree { name, branch, path, isActive, diskSizeBytes? }` · `LayerPattern { order:int, regex, name }` · `BuildPlan { … }` — değişmedi.
- **Disiplin (D3/D4):** planlama Core'da; stdout yalnız NDJSON; `skipped` gerçek reason (UI sözlüğü: `skipped — up to date` `[v7Δ-1]`).

## A10. Yapılandırma

Kök dizin (**Settings'te REPOSITORY satırı: mevcut yol + `Change…` → OpenFolderDialog `[v7Δ-7 · K10]`**; ilk seçim empty-state `Choose Folder`) · **Build config** (Debug varsayılan / Release; action-bar segment; config-agnostic mini-uyarı) · **Perf modu (sabit 6/4/2 + priority + CPU cap `[v7Δ-7 · K11]`; ana UI chip, tooltip yok)** · **Worktree** (branch-driven; havuz kalıcı, per-worktree Sil + cap/LRU — T14) · Downstream modu (Safe/Fast — config JSON'da `[v7Δ-7]`) · **Katman pattern editörü** (Settings LAYERS: sıralı sınırsız regex + isim; sürükle-sırala **Mouse.Capture ile — `DragDrop.DoDragDrop` YASAK** `[v7Δ-6]`; boş regex geçerli; varsayılan BOŞ; `Load sample layers`) · **Kısayollar** (`[v7Δ-5]` şeması; Alt+B ayarlanabilir) · Cache konumu (config JSON) · **Görsel kimlik** (Delta logo+icon, dark title bar; token'lar design-v1 `[v7Δ-1]`) · **Görünüm** (mod quad/list/focus + splitter konumları persist `[v7Δ-7]`) · **KALDIRILANLAR:** LogLevel, in-app Reduced Motion, ayrı local-dahil toggle, **çift-Shift/Ctrl+P kısayolları `[v7Δ-5]`**.

## A11. Kapsam Sınırları (v1)

**İçinde:** v6 listesi aynen (tek repo · MSBuild.exe+nuget shell-out · nested-Job · sync/graph/cache · rebuild + incremental · sıra-koruyan scheduler · katman pattern · branch-driven worktree · dependency graf paneli · tam UI/UX · config · tray/autostart/single-instance · perf modları · testler · README · `dotnet publish`) **+ [v7Δ] eklenenler:** Sync-fetch (ref-only) · depIssue/dependency-affected sistemi (▲ chip + `dep` filtresi) · Continue + Retry failed · görünüm modları (quad/list/focus) · proje arama inputu (Ctrl+F) · Copy log · tray balloon (ilk X) · Settings REPOSITORY satırı · restore glyph · Snap Layouts · font A/B karar kapısı (T65) · AvalonEdit konsol + chunk log · DS kontrol/tooltip kütüphanesi.

**Dışında (sonraya, gerekçeli):** v6 listesi aynen (multi-repo · MSIX · WinUI Composition · dönen globe · CPU % slider · headless/CLI · light mode · komut paleti · onboarding · packages.config migration · worktree çıktı izolasyonu · T33 v1'de · graf düzenleme · eski-kod bug araştırması · CLAUDE.md çoklu-dosya + agent senkron · katman gelişmiş çözüm · uygulama-içi motion/tema toggle) **+ [v7Δ]:** **çalışan-VS'ye ROT/EnvDTE bağlanma** (v2 backlog — K7) · **çift-Shift/Ctrl+P kısayolları** (kaldırıldı — K6) · **WebView2 hibrit** (yalnız T65 kapısından geri gelebilir — K9) · zoom-LOD etiket gizleme (ayrı tasarım kararı gerektirir).

## A12. Varsayımlar / Varsayılanlar

v6 listesi aynen (tek git repo · ortak çıktı post-build event'lerle · config-agnostic · kullanıcı VS'de eşzamanlı derlemez · Debug/Safe/branch-driven/Full varsayılanları · graf cache'ten · X→tray · araç .NET 10 · aktif branch ASLA checkout edilmez · trust boundary T17) **+ [v7Δ]:** Sync-fetch ağ gerektirir — **offline'da degrade** (warn + yerel HEAD) `[K1]` · VS çözümü **vswhere** ile (VS2017+ sabit konum) `[K7]` · fontlar **gömülü statik OTF** (air-gapped çalışır) `[Δ6]` · UI thread'te uzun senkron iş YASAK (animasyon bütçesi) `[Δ6]` · liste virtualization kapalı başlar (OSYS 191 bandı), 500+ hedefte kalibrasyonlu açılır `[Δ6]`.

## A13. Fidelity Çerçevesi & WPF Teknik Kararları `[v7Δ-6]` (YENİ)

> Kaynak: [design-wpf-feasibility-analysis](2026-07-15-23-34-design-wpf-feasibility-analysis.md) — ~100 öğe, ~60 adversarial doğrulama, 6 düzeltme. Bu bölüm implementasyon sırasında BAĞLAYICIDIR.

**A13.1 Kabul edilen yapısal farklar ("algısal eşdeğer" sınıfı — iş gücüyle kapatılmaya ÇALIŞILMAZ):**
1. **Font rasterization:** DirectWrite ≠ Chromium/Skia — 11-13px metin bit-düzeyinde aynı olmaz (~%95-98); `TextFormattingMode=Display` + ClearType/Grayscale A/B'si hedef monitörde yapılır (T65 kapısı).
2. **Gölge spread'i:** DropShadowEffect'te spread yok — popover/pill çift gölgesi tek effect'le yakınsanır.
3. **AvalonEdit satır pop-in'i:** kaskat temposu birebir; satır başına translateY+scale yerine opacity-fade.
4. **Animasyon threading'i:** compositor yok — "UI donsa da animasyon akar" garantisi verilemez; telafi = UI thread disiplini.
5. **CSS `dashed` köşe hizalaması** birebir değil (pratikte fark edilmez).
6. **OS yüzeyleri** (OpenFolderDialog, Explorer, VS) uygulama temasına boyanamaz.
7. **Tooltip ayrı HWND** — pencere dışına taşabilir/kenarda flip eder (çoğunlukla iyileştirme).

**A13.2 Zorunlu teknik kararlar (doğrulanmış):**
- **Konsol = AvalonEdit** (IsReadOnly + DocumentColorizingTransformer + hibrit aktif satır + CompositeFont line-height spike'ı It-0'da). FlowDocument/RichTextBox YASAK (hacimde çöker); ItemsControl konsolu YASAK (metin seçimi kaybı).
- **Letter-spacing 0.07em = `TrackedTextBlock`** (GlyphRun + AdvanceWidths; uppercase gömülü). Hair-space ekleme YASAK.
- **Sticky katman başlıkları = overlay mimarisi** (ScrollViewer üstü ItemsControl + salt aritmetik offset tablosu; `LayoutMetrics` TEK servis — follow-mode ile paylaşılır). **Virtualization:** kapalı başla; açılırsa realized-container drift kalibrasyonu ŞART (`ScrollUnit=Pixel`'de karışık 36/24 yükseklik tahmini kayar — doğrulanmış).
- **Smooth scroll = attached DP `ScrollAnimator.VerticalOffset`** (native yok); wheel'de animasyon iptali; suppress bayrağı.
- **Pencere kabuğu:** WindowChrome + `SingleBorderWindow`; **maximize Padding düzeltmesi ZORUNLU** (dotnet/wpf#3887); DWM köşe/border API'leri (`AllowsTransparency` ASLA); Snap Layouts = HTMAXBUTTON hook; single-instance `AllowSetForegroundWindow`.
- **Graf:** ≤~150 düğüm Shapes → üstünde EdgeLayer/NodeLayer(DrawingVisual)/FlowOverlay üçlüsü; **akan dash HER ZAMAN UIElement Path** (DrawingContext'te Pen.DashStyle.Offset animasyonu güvenilmez); dash birimi = StrokeThickness çarpanı; **etiketler Ideal mode**; katman host'ları UIElement (ContainerVisual.Opacity animate edilemez).
- **Motion:** üç CSS eğrisi KeySpline ile birebir; süre/eğri token ResourceDictionary; `MotionCoordinator` tek kapı; dekoratif sonsuz animasyonlar `DesiredFrameRate=30`; typewriter/kaskat Stopwatch-bazlı (DispatcherTimer ~15.6ms); koleksiyon reset'i YASAK.
- **DS kontrolleri:** hepsi custom ControlTemplate (Switch=CheckBox template — WPF'te ToggleSwitch yok; Input=watermark+prefix overlay; split-button=per-corner radius; 120ms geçişler template-lokal brush'larla — frozen/paylaşılan brush anime edilemez); Kbd hariç hazır gelen yok.
- **Tooltip:** global implicit Style + `InitialShowDelay=0` override (görev-kritik) + `CustomPopupPlacementCallback` (Top/Bottom ORTALAMAZ) + canlı içerik binding + `ClearTypeHint=Enabled`.
- **Settings sürükle-sırala:** `Mouse.Capture` + 21px eşik-swap portu; **`DragDrop.DoDragDrop` YASAK** (OS ghost semantiği tasarımı bozar); komşular animasyonsuz snap (birebirlik).
- **OS eylemleri:** `explorer.exe /select,"path"` (tırnak şart) · vswhere→devenv (K7) · `OpenFolderDialog` (.NET 8+ native) · Clipboard retry sarmalayıcı.
- **Asset:** vercel/geist-font **statik OTF** (Regular/Medium/SemiBold ×2); glif kapsam testi (▸ → · — …); SVG→XAML Geometry ikonlar; çoklu-boyut ICO (16/24 elle); imleç/chevron çizim (font değil).
- **px→DIP 1:1**; PerMonitorV2 + kökte `UseLayoutRounding=True` (hairline'lar); tüm sayı formatlaması `InvariantCulture` (VM'de).

**A13.3 Teknoloji kararı:** WPF DOĞRULANDI. WebView2 hibrit = fidelity sigortası, **yalnız T65 kapısından** (K9); Avalonia (Windows text-rendering regresyon geçmişi) ve WinUI 3 (kazanım/maliyet dengesi — CharacterSpacing/Composition buradaki ihtiyaca değmez) ELENDİ. Backend (Contracts/Core/Supervisor) UI'dan bağımsız — kapı maliyetsiz açık.

---

# PART B — Birleşik Task Backlog (T1–T70)

> v6'nın T1–T53'ü korundu (izlenebilirlik; absorbe olanlar hâlâ başka task içinde). **[v7Δ] T54–T70 eklendi**; revize edilen mevcut tasklar aşağıda ayrıca işaretli.

| ID | Başlık (kısa) | Durum | İt. | Kaynak/iz |
|---|---|---|---|---|
| **T23** | Iteration -1 Feasibility Spike (GATE) — MSBuild.exe+nuget derle, HintPath match-rate, cascade-kill ≤2s + breakaway, D9 flag delta | AKTİF (gate) | **-1** | Eng/D1,D13 · T1+T2 absorbe |
| **T22** | Engine: MSBuild.exe (vswhere) + nuget restore; nested Job+shell-out+cascade | AKTİF | 0/2 | D10 |
| **T30** | Tek `ProcessRunner` (exit-code+stderr+timeout) | AKTİF | 0 | D7 |
| **T7** | IPC framing: length-prefixed/escaped + max-line NDJSON | AKTİF | 0 | CEO · T28 genişletir |
| **T28** | getProjectLog chunk+interleave; stdout NDJSON-only | AKTİF | 0/2 | D4 |
| **T6** | Supervisor crash recovery: App handle izler → error+restart | AKTİF | 0 | CEO |
| **T31** | Process-control testleri deterministik | AKTİF | 0 | D8 |
| **T24** | Graph: HintPath→producer; batch eval + mtime/hash cache; file→proje=Compile items | AKTİF | 1 | D11,D5 · T3+T19+T2 absorbe |
| **T32** | Solution belirsizliği: csproj 0/>1 sln + **Open-in-VS sln seçimi (K7 akışı)** `[v7Δ-7]` | AKTİF (revize) | 1 | OV#6 |
| **T26** | Planning Core'da: `BuildPlan` DTO | AKTİF | 1 | D3 |
| **T25** | Signature+propagation: tek Core BuildSignature; skip GLOBAL gate | AKTİF | 3 | D6,D11 · T18 pekiştirir |
| **T27** | build-state single-writer + atomik + per-project persist | AKTİF | 3 | D2 |
| **T4** | Stop: copy-aware graceful; hard-kill yalnız proje sınırı; **Stop→queued→Continue edilebilir `[v7Δ-4]`** | AKTİF (revize) | 0/2 | CEO/2A |
| **T5** | Logs: per-run disk project log + decision log | AKTİF | 2 | CEO/4A |
| **T8** | Parallel copy retry-on-sharing-violation + backoff | AKTİF | 2 | CEO |
| **T9** | Test: kill mid-parallel-build → torn DLL yok | AKTİF | 2 | CEO |
| **T29** | Branch-driven worktree (3-durum matris) + **niyet satırı — `git switch --detach` KALKTI `[v7Δ-5 · K3]`** | AKTİF (revize) | 3 | v6Δ-1 · T21 absorbe |
| **T11** | Edge input: detached HEAD / no-commits / shallow → treat-as-dirty + warn | AKTİF | 3 | CEO |
| **T13** | Path sanitization: worktree + branch | AKTİF | 3 | CEO |
| **T14** | Worktree pool: per-worktree disk + cap / LRU prune | AKTİF | 3 | OV#3 |
| **T15** | Layer reverse-dep detect+warn-only | AKTİF | 3 | 3C |
| **T34** | Typing/live-line degradation engine — **Stopwatch-bazlı tempo `[v7Δ-6]`** | AKTİF (revize) | 4 | DD2 |
| **T35** | Console/graf/stream layout: 2×2 + 3 splitter + **görünüm modları quad/list/focus + 50/50/50 preset + persist `[v7Δ-7]`** | AKTİF (revize) | 4 | DD1 · v6Δ-2 |
| **T36** | OS reduced-motion → anlık; canlı takip | AKTİF | 4 | DD3 |
| **T37** | Interaction state'leri + **engine-died = sticky şerit kalıcı hata modu + Restart engine; tray balloon (K5) `[v7Δ-7]`** | AKTİF (revize) | 4 | Pass2,DD10 |
| **T38** | Global progress/ETA — **formül Δ8 (EMA + 5s yuvarlama + almost done)** `[v7Δ-8]` | AKTİF (revize) | 4 | DD6 |
| **T39** | Failure orchestration: **sticky şerit hata kümesi (banner YOK) + `+N more`→Failed filtresi + depIssue sayacı `[v7Δ-3/Δ7]`** | AKTİF (revize) | 4 | DD11 · v6Δ-4 |
| **T40** | Discoverability & seçim: canonical click/deselect, stream satırı, görünür Back, aranabilir branch chip, worktree 2-sinyal — **çift-Shift tooltip KALKTI `[v7Δ-5]`** | AKTİF (revize) | 4 | DD8,DD12,DD13 |
| **T41** | Motion budget: 1 hero + **MotionCoordinator `[v7Δ-6]`** | AKTİF (revize) | 4 | DD9,DD7 |
| **T42** | Sync reveal: build-order staggered fade-in (satır 10ms/380 · katman 55ms/330 `[v7Δ-1]`) | AKTİF (revize) | 4 | DD14 |
| **T43** | Pre-build context confirm: config-değişti-tümü-dirty mini-uyarısı (branch-switch confirm yok) | AKTİF | 4 | DD14 · v6Δ-1 |
| **T45** | Anti-slop: glyph≠emoji, grotesk+mono, restrained radius, halka glyph serbest, dolu rozet yasak | AKTİF | 4 | Pass4 |
| **T46** | Keyboard nav: ok/Enter/Esc + focus-visible ring — **T68 detaylandırır `[v7Δ-6]`** | AKTİF | 4 | Pass6 |
| **T47** | Screen reader + kontrast — **T68 detaylandırır `[v7Δ-6]`** | AKTİF | 4 | Pass6 |
| **T48** | Auto-scroll arbitration + frontier center-of-gravity (graf-pan dahil; yo-yo yasak) | AKTİF | 4 | belirsizlik#5,#6 |
| **T10** | Empty/error UI state | AKTİF | 4 | CEO |
| **T12** | Mid-run lock: Building'de branch/worktree/Debug-Release kilidi (perf serbest) — **TEYİT `[v7Δ-5]`; prototipteki kilitsizlik taşınmaz** | AKTİF (revize) | 4 | CEO |
| **T16** | Autostart temiz Idle açar | AKTİF | 4 | CEO |
| **T50** | Dependency graf paneli: yerleşik DAG, canlı frontier (AYNI hero), node odak, senkron seçim — **düğüm = 4px-radius kare (K4); teknik kurallar A13 `[v7Δ-6/Δ7]`** | AKTİF (revize) | 4 | v6Δ-2 |
| **T51** | Graf perf: 500–1000 node + cull — **hibrit mimari T63'te somutlandı `[v7Δ-6]`** | AKTİF (revize) | 5 | v6Δ-2 |
| **T53** | Will-build noktası: Core `willBuild?` + kartta dot — **hollow birleşik tanım + succeeded→clean canlı geçiş `[v7Δ-8]`** | AKTİF (revize) | 1/3 | v6Δ-3 |
| **T20** | CPU-cap × copy/git/IPC etkileşimi; copy fazına rate floor | AKTİF | 5 | OV#2 |
| **T33** | D9 fast-follow: node reuse + shared compilation (spike kanıtlarsa) | AKTİF (koşullu) | 5 | D9 |
| **T44** | Success flourish — **YALNIZ stream done satırı glow-once (liste/graf dalga YOK) `[v7Δ-7]`** | AKTİF (daraltıldı) | 5 | DD14 |
| **T49** | Token çevirisi: design-v1 `_ds` token'ları → WPF ResourceDictionary (renk/tip/spacing/radius/motion + TextOptions kararları); `statusbar 28` token'ı `PanelHeaderHeight` olarak adlandırılır — ayrı statusbar bölgesi YOK `[v7Δ-1]` | AKTİF (revize) | 4/5 | Pass5 |
| **T17** | Trust-boundary doc | AKTİF | 5 | CEO |
| **T54** | **[v7Δ-3] depIssue sistemi:** resolved={succ,fail,skip}; kök-hata propagation (Core); `ProjectResult.depIssues[]` + `depIssueCount` (Contracts); log-başı warn satırları (Supervisor); ▲ rozet kart+graf+konsol başlığı + `▲ N` chip + `dep` filtresi (App); unit testler | YENİ | 3/4 | v7Δ-3 · D |
| **T55** | **[v7Δ-4] Continue + Retry failed:** `RunRequest.mode` genişlemesi; Continue=queued'lardan sürme (elapsed korunur); Retry=failed+transitif willBuild (konsol/stream sıfırlanmaz); split-button menü maddeleri + F5 stopped=Continue; testler | YENİ | 2/3 | v7Δ-4 · D |
| **T56** | **[v7Δ-6] AvalonEdit konsol:** read-only host + colorizer + hibrit aktif-satır typewriter + kaskat (tempo+fade) + tampon/trim + chunk loader (scroll telafili prepend + sequence-id dikişi) + batch flush + **It-0 CompositeFont line-height spike** | YENİ | 0(spike)/2/4 | v7Δ-6 · F |
| **T57** | **[v7Δ-6] TrackedTextBlock:** GlyphRun + 0.07em advance + uppercase; tüm caps etiketler; DPI/Display testi | YENİ | 4 | v7Δ-6 · F |
| **T58** | **[v7Δ-6] Sticky overlay + LayoutMetrics:** birikimli yapışan başlıklar (overlay ItemsControl + aritmetik tablo); virtualization stratejisi (kapalı başla → 500+ drift kalibrasyonu); follow-mode ile ortak servis | YENİ | 4 | v7Δ-6 · F |
| **T59** | **[v7Δ-6] Scroll altyapısı:** ScrollAnimator attached DP + BottomAnchorBehavior (48px eşik + jumping) + FollowScrollController (550ms/54px) + `⌄ latest` pill | YENİ | 4 | v7Δ-6 · F |
| **T60** | **[v7Δ-6] DS kontrol kütüphanesi:** Button×4×3, split-button, Chip, Switch(CheckBox), Segment(RadioButton), Input(watermark+prefix+invalid), IconButton, Kbd + 120ms geçiş altyapısı (template-lokal brush) + focus ring | YENİ | 4 | v7Δ-6 · F |
| **T61** | **[v7Δ-6] Tooltip altyapısı:** implicit Style + delay=0 override + CustomPopupPlacementCallback (ortalama+6px) + canlı içerik binding + ClearTypeHint | YENİ | 4 | v7Δ-6 · F |
| **T62** | **[v7Δ-6] Pencere kabuğu paketi:** WindowChrome + maximize Padding düzeltmesi + DWM köşe/border + Snap Layouts (HTMAXBUTTON) + **restore glyph (K8)** + tray (H.NotifyIcon + 16px elle ikon + **ilk-X balloon K5**) + single-instance (AllowSetForegroundWindow) + Alt+B hotkey | YENİ | 0(temel)/4 | v7Δ-6/Δ7 · F |
| **T63** | **[v7Δ-6] Graf hibrit render:** Shapes yolu (≤150) + EdgeLayer/NodeLayer(DrawingVisual, UIElement katman host)/FlowOverlay mimarisi + cull + GlyphRun cache + Ideal etiketler — T50/T51'in teknik somutlaması | YENİ | 4/5 | v7Δ-6 · F |
| **T64** | **[v7Δ-6] Asset hattı:** statik OTF gömme (vercel GitHub) + glif kapsam testi + SVG→XAML ikonlar + çoklu-boyut ICO (16/24 elle) + LICENSE taşıma | YENİ | 0/4 | v7Δ-6 · F |
| **T65** | **[v7Δ-6 · K9] Font A/B karar kapısı (It-4 BAŞI):** Geist 12-13px WPF (Display/Ideal × ClearType/Grayscale) ↔ tarayıcı, hedef monitörde; kullanıcı kararı → saf WPF kesinleşir VEYA WebView2 hibrit gündemi | YENİ (gate-lite) | 4 başı | K9 · F |
| **T66** | **[v7Δ-6/Δ7] Settings dialog:** LAYERS editörü (Mouse.Capture sürükle-sırala; DoDragDrop YASAK; boş regex geçerli) + **REPOSITORY satırı (K10)** + Load sample layers; dişli tooltip'i `Settings` olarak genelleşir | YENİ | 4 | v7Δ-6/Δ7 · D/F |
| **T67** | **[v7Δ-6] OS eylemleri:** explorer /select (tırnaklı) + **vswhere→devenv sln açma (K7 + T32 seçtirme)** + OpenFolderDialog + Clipboard retry + konsola dim not düşme | YENİ | 4 | v7Δ-6/Δ7 · F |
| **T68** | **[v7Δ-6] Klavye/focus mimarisi:** satır tabIndex+Enter; in-window dialog focus-trap; popover focus yönetimi; AutomationProperties.Name + live-region; T46/T47'nin teknik somutlaması | YENİ | 4 | v7Δ-6 · F |
| **T69** | **[v7Δ-2 · K1] Sync-fetch adımı:** `git fetch origin <branch>` ref-only; offline degrade (warn + yerel HEAD); hedef SHA = remote-tracking ref; `curSha → targetSha` beslemesi; testler | YENİ | 3 | v7Δ-2 · K1 |
| **T70** | **[v7Δ-8] ETA + lastDurationMs:** BuildState alanı + persist; ETA formülü (EMA 0.75/0.25 + building 400ms + 5s yuvarlama + almost-done); ilk-koşu fallback'i (tahmin yokken X/N + geçen); unit testler | YENİ | 3/4 | v7Δ-8 · D |

**Özet:** v6'nın 46 task'ı korundu — **17'si `[v7Δ]` revize** (T4/T12/T29/T32/T34/T35/T37–T42/T44/T49/T50/T51/T53); T46/T47 yalnız nota bağlı (T68 detaylandırır); **T54–T70 (17 yeni)** eklendi. Hiçbir v5/v6 kararı silinmedi.

---

# PART C — İterasyon Yol Haritası

| It. | Teslim | Tasklar | Acceptance (bitti tanımı) |
|---|---|---|---|
| **-1** | **Feasibility Spike (GATE, throwaway)** | T23 | (a) 5 legacy proje MSBuild.exe+nuget green; (b) HintPath→producer match-rate + eşik; (c) cascade-kill gerçek MSBuild ağacı ≤2s, 0 orphan, no-breakaway; (d) D9 flag delta. **SPIKE-RESULTS.md** yazıldı. Herhangi biri fail → STOP. |
| **0** | İki process + stdio IPC + nested Job cascade + minimal pencere + DI iskeleti **+ [v7Δ] UI spike'ları** | T22(resolve), T30, T7, T28(base), T6, T31, T4(base), **T56(CompositeFont spike), T62(WindowChrome temel + maximize düzeltmesi), T64(font gömme + glif testi)** | §3 deterministik kabul geçer. stdout yalnız NDJSON. **[v7Δ] CompositeFont line-height spike sonucu kayıtlı (1.55 tutuyor/tutmuyor); Geist gömülü ve 400/500/600 ayrışıyor.** |
| **1** | Sync/graph: scan, HintPath→producer, Tarjan/Kahn, batch eval+cache, BuildPlan, build-order kartlar + cycle rozeti; pre-run will-build (Core) | T24, T32, T26, T53(Core) | Gerçek OSYS Sync cache-hit'te hızlı; kartlar build-order'da; cycle rozeti; will-build kümesi testli. |
| **2** | Rebuild (gerçek, paralel) + per-run disk log + kart seçince detay + copy-aware Stop **+ [v7Δ] Continue + AvalonEdit gerçek akış** | T22(invoke), T28(stream), T5, T4(copy-aware), T8, T9, **T55(Continue), T56(konsol canlı akış + batch flush)** | OSYS rebuild paralel green; dispatch deterministik (ready-set — K2); kill mid-build → torn DLL yok; karta tıkla → tam log diskten; **Stop→Continue kalanlardan sürer; konsol MSBuild-verbose altında akıcı.** |
| **3** | Incremental: commit/diff/status, GLOBAL build-state, propagation, Safe/Fast, branch-driven worktree, Skipped, will-build UI, katman pattern **+ [v7Δ] fetch + depIssue + retry + ETA** | T25, T27, T11, T13, T14, T29, T15, T53(UI), **T69(fetch), T54(depIssue motor), T55(Retry failed), T70(ETA+lastDurationMs)** | Branch-bounce doğru; L1→L3 dirty; config-switch all-dirty; worktree matris + **niyet satırı (K3)**; will-build dot doğru + **succeeded→clean canlı**; **fetch'li Sync (offline degrade dahil — K1); depIssue zinciri testli; Retry failed kümesi doğru; ETA formülü testli.** |
| **4** | **UX polish (design-v1 birebir):** 2×2 layout + görünüm modları, graf paneli, typing degradation, senkron seçim/deselect, frontier + sticky şerit (hata kümesi), chip'ler + aranabilir branch + Debug/Release, worktree UI, kısayollar (K6) + hotkey, motion budget, dark chrome + tray balloon, autostart, single-instance, Settings (LAYERS+REPOSITORY), interaction states, progress/ETA, keyboard nav, SR/kontrast, reduced-motion **+ [v7Δ] tüm UI altyapı taskları. İterasyon BAŞI: T65 font A/B karar kapısı (K9).** | T34–T43, T45–T48, T10, T12, T16, T50, T49, **T54(UI), T56(kaskat/chunk UI), T57–T63, T64(ikonlar), T65(GATE-lite), T66, T67, T68, T70(ETA gösterimi)** | **T65 kararı kayıtlı**; design-v1 birebirlik gözle doğrulandı (token'lar, animasyon süreleri, kopya metinleri); 500–1000 kart+node akıcı; frontier senkron (tek hero); seçim/deselect çalışır; all-skipped DELIGHT; reduce-motion anlık; keyboard-first; **failure sticky kümesi + per-row log; ▲ dep filtresi; Continue/Retry menüde; Ctrl+F filtresi; Copy log; restore glyph; Snap Layouts; balloon.** |
| **5** | **Perf + dağıtım + docs:** perf modları (**6/4/2 + CPU cap — K11**, canlı), 500–1000 doğrulama, graf perf (T51/T63), README, `dotnet publish`, son cila | T20, T33(koşullu), T44(daraltılmış), T51, T63(perf), T49, T17 | CPU cap tavanı tutar; copy fazı starve olmaz; graf 500–1000 akıcı + cull; publish çalışır exe; **flourish yalnız stream glow**; README + trust-boundary. |

**Paralelizasyon (worktree lanes):** Contracts (DTO/event + BuildPlan + `willBuild`/`inCycle` + **`depIssues`/`lastDurationMs`/`mode` `[v7Δ]`**) önce sabitlenir → Lane A Core (pure) · Lane B Supervisor · Lane C App/UI (graf render + UI altyapı taskları Lane C). Walking-skeleton dikey-dilim baskın.

---

# PART D — Iteration -1 Feasibility Spike (T23, GATE)

> **Throwaway/investigation** — production değil, kanıt üretir. Çıktı = `.claude/outputs/<ts>-spike-results.md`. **v7 deltaları spike gate'lerini DEĞİŞTİRMEZ** — spike engine/graf/cascade feasibility'sini ölçer; UI/worktree/tasarım kararları spike'tan bağımsız. İçerik v5/v6 Part D ile **birebir aynıdır**; kısaca:

- **S1 — MSBuild.exe + nuget resolve:** `vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"` → sürüm; nuget erişimi. **Pass:** yol + sürüm (full-MSBuild). Fail→STOP.
- **S2 — 5 temsilî legacy proje uçtan uca derle:** (i) yaprak v4.6, (ii) packages.config, (iii) çok HintPath, (iv) WPF, (v) post-build copy. `msbuild $proj -t:restore` + `-p:Configuration=Debug -p:UseSharedCompilation=false -nodeReuse:false -clp:Summary`. **Pass:** 5/5 exit 0 + çıktı DLL + post-build copy (VS-parity). Fail→STOP.
- **S3 — HintPath→producer match-rate:** 191 csproj batch eval; producer map; intra-repo çözülme oranı. **Pass eşiği ≥%95.** Partial→STOP değil; It-1 öncesi fallback strateji plana eklenir.
- **S4 — Nested Job cascade-kill gerçek MSBuild ağacına:** outer Job → child suspended→assign→resume → OSYS derlemesi; outer öldür → **≤2sn** 0 orphan; **breakaway probe (D1).** **Pass:** ≤2sn 0 orphan VE no-breakaway. Fail→STOP.
- **S5 — D9 flag delta (gate değil, kayıt):** reuse kapalı vs açık wall-time delta; reuse açıkken S4 kill hâlâ ≤2sn mi.
- **S6 — Verdict + GATE:** `SPIKE-RESULTS.md` (S1–S5 PASS/FAIL/PARTIAL + sayılar). Tüm gate PASS → It-0 + detaylı TDD. S2/S4 FAIL → It-0 başlamaz. S3 PARTIAL → It-0 başlar + It-1'e fallback task.

**Spike acceptance:** SPIKE-RESULTS.md mevcut + S2/S3/S4 net karara bağlı + D9 kaydı + spike kodu ana solution'a sızmadı.

---

## İzlenebilirlik & Self-Review

**v6 → v7 coverage:** Part A/B/C/D v6'dan korundu; **Δ1–Δ8** inline `[v7Δ]` işaretli. Revize edilen tasklar (T4/T12/T29/T32/T34/T35/T37–T42/T44/T49/T50/T51/T53 — 17 adet; T46/T47 yalnız nota bağlı) hiçbiri silinmedi; **T54–T70 eklendi**. Spike (Part D) değişmedi. 11 kullanıcı kararı (K1–K11) "v7 Karar Kaydı"nda; design-v1 ↔ plan çelişkilerinin tam listesi ve gerekçeleri fizibilite raporu §7'dedir.

**Korunan kritik kararlar (ezilmedi):** MSBuild.exe (D10) · HintPath→producer (D11) · GLOBAL build-state/propagation (D2/T25) · nested Job cascade + ≤2s (§3/D1) · copy-aware stop (2A) · sıra-koruyan ready-set scheduler (§6 — K2 ile teyit) · 2-kolon/2-zone + typing degradation (DD1/DD2) · OS reduced-motion (DD3) · katman pattern (N8) · motion budget (DD9) · all-skipped delight (DD10) · global progress/ETA (DD6) · tray X→küçül/Exit→cascade · commit gösterimi (N10) · planlama Core'da (D3) · stdout NDJSON-only (D4) · branch-driven worktree (v6Δ-1) · graf paneli (v6Δ-2) · will-build (v6Δ-3).

**v7 yeni/revize kararlar (onaylı):** görsel otorite design-v1 (Δ1) · Sync-fetch ref-only (Δ2/K1) · depIssue sistemi (Δ3) · Continue+Retry (Δ4) · kısayol şeması + niyet satırı (Δ5/K3/K6) · WPF teknik paket + A13 (Δ6) · sunum revizyonları (Δ7/K4/K5/K7/K8/K10/K11) · motor ekleri (Δ8).

---

## Execution Handoff

Plan kaydedildi: `.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md`. Görsel otorite: `.claude/outputs/2026-07-15-19-00-design-v1/` (README + prototip). Fizibilite raporu: `.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md`. Delta design-system promptu (tarihsel): `.claude/outputs/2026-07-02-01-38-delta-design-system-v1.md`.

**Sıradaki adım = Iteration -1 Feasibility Spike (Part D).** Bu gate geçmeden It-0 kodlanmaz. Spike sonrası It-0 için detaylı TDD planı (writing-plans 2. tur).

> Not: v5/v6 dokunulmadı (donmuş kaynaklar). v7 onlardan türetilmiş güncel uygulama kaynağıdır; görsel değerler için design-v1 paketi esastır.
