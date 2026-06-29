# Build Orchestrator — Plan v5 (Final, Uygulanabilir)

> **For agentic workers:** REQUIRED SUB-SKILL — Bu planı task-by-task uygulamak için `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans` kullan. Adımlar checkbox (`- [ ]`) ile izlenir.

> **v5 nedir / ne DEĞİLDİR.** v5 = v4.3'ün ([2026-06-29-11-17-build-orchestrator-plan-v4.3-design-review.md](2026-06-29-11-17-build-orchestrator-plan-v4.3-design-review.md)) **türevi**, yeni bir tasarım değil. v4.3 **donmuş arşiv + otorite**dir ("neden bu kararlar" — gövde + CEO/Eng/Design review birlikte). v5 **tek uygulama kaynağı**dır ("ne inşa edilecek, hangi sırada"). **Hiçbir karar / eklenen özellik / delta silinmedi veya ezilmedi** — v4.3'te dağınık (gövde + 3 review delta katmanı) olan otorite burada **tek gerçeğe düzleştirildi**, T1–T49 tek sıralı backlog'a indirgendi, iterasyonlara eşlendi. Çelişki çıkarsa **v4.3'teki ilgili D/DD kararı** son sözdür; her bölüm izlenebilirlik etiketi taşır (`[v4.3 §X · D.. · DD.. · N.. · fork]`).

**Goal:** Yüzlerce legacy .NET Framework C#/WPF projesini (tek git repo, OSYS) bağımlılık sırasına göre, paralel ve yalnızca değişenleri derleyen; derlemeyi ayrı bir Supervisor process'te nested Job Object ile yöneten, dark/modern WPF masaüstü orchestrator.

**Architecture:** İki process (App/WPF + Supervisor/console) + saf Core + Contracts + Tests. App = view + outer Job sahibi; Supervisor = inner Job + her projeyi `MSBuild.exe` ile shell-out derler; Core = tüm planlama (graf, signature, scheduler, layer) saf ve test edilebilir. İletişim stdio NDJSON. Teslim = Iteration -1 (gating spike) → It-0..5 walking-skeleton dikey dilim.

**Tech Stack:** .NET 10 (LTS) + WPF (orchestrator'ın kendisi) · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · xUnit · **derleme motoru `MSBuild.exe`** (VS Build Tools/VS, `vswhere` ile resolve) + `nuget restore`/`msbuild -t:restore` · Win32 Job Object / RegisterHotKey / WindowChrome P/Invoke.

---

## Global Constraints

Aşağıdaki değerler **her task'ın** örtük gereksinimidir; ihlal = plan ihlali. Değerler v4.3'ten **verbatim**.

- **Derleme motoru = `MSBuild.exe`** (`vswhere -latest -requires Microsoft.Component.MSBuild` ile resolve), **`dotnet build` DEĞİL** — hedef repo 175/191 legacy .NET Framework (v4.6/v4.8). Packages.config için `nuget restore` veya `msbuild -t:restore`, per project. `[D10 · §0 delta]`
- **§3.4 flag'leri (v1): `-p:UseSharedCompilation=false -nodeReuse:false`** — korunur (güvenli/yavaş). Kaldırma yalnız T33 fast-follow'da, spike kanıtlarsa. `[D9 · §3.4 delta]`
- **§6.1 garantisi = nested Job Object** (managed watcher YOK): App outer Job (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) → Supervisor → inner Job (`KILL_ON_JOB_CLOSE` + CPU rate) → `MSBuild.exe` child'ları; hepsi `CREATE_SUSPENDED → AssignProcessToJobObject → ResumeThread`. App ölünce kaskat ölür. `[§3]`
- **Shell-out per project**, in-process MSBuild (BuildManager) **asla**. `[§0]`
- **Ortak OutDir'e DOKUNULMAZ/OKUNMAZ.** "Değişti mi" yalnız kaynak sinyalinden. DLL/bin timestamp asla okunmaz. `[§4]`
- **build-state GLOBAL** (projectId anahtarlı, branch'e özel değil), **single-writer (serialized) + atomik temp+rename**, her proje bitiminde persist. `[§4/§6 · D2]`
- **Signature = tek Core `BuildSignature.Compute`** (byte-stable, determinism testli): `config + HEAD commit + (in-place'de) local-diff hash + transitive upstream producer signatures`. Drift yok. `[D6 · §6 delta]`
- **Graf primer = HintPath-basename→producer** (evaluated AssemblyName/TargetName haritası); ProjectReference **ikincil**. Skip yalnız self-source değil **GLOBAL graf propagation**'a bağlı. `[D11 · §5 delta]`
- **Tüm planlama saf Core'da** (`BuildPlan` DTO üretir); Supervisor yalnız **yürütür**. `[D3 · §9 delta]`
- **IPC = stdio NDJSON, framed** (length-prefixed veya escaped + max-line guard); **stdout YALNIZ NDJSON**, tüm logging stderr/dosyaya. `[T7/T28 · D4]`
- **Per-run disk log:** `%LOCALAPPDATA%\BuildOrchestrator\logs\run-<ts>\` (proje logları + decision log); bellek ring buffer YOK. `[4A · D4]`
- **Worktree havuzu:** `%LOCALAPPDATA%\BuildOrchestrator\worktrees\<name>\`, **kalıcı** (hız/obj cache), per-worktree silinebilir. Worktree çıktısı da **ortak bin'e** yazar (izole değil — kasıtlı); etiket "committed **kaynak**" der, "izole çıktı" demez. `[N3 · D12]`
- **Tek `ProcessRunner`** helper: zorunlu exit-code + stderr + timeout. dotnet/msbuild non-zero = `projectFailed`; git/eval fail = `error`. `[D7]`
- **OS reduced-motion'a saygı** (uygulama-içi toggle YOK). Windows animasyon ayarı kapalıysa typing/pulse/shimmer/shake/stagger → anlık. `[DD3 · §7/§11/§13 delta]`
- **Console = mutlaka monospace**; status = **glyph + renk + metin** (emoji değil; colorblind-safe; dim/skipped dahil ≥4.5:1 kontrast). `[DD3/DD4/Pass4/Pass6]`
- **v1 = tek repo.** Multi-repo mimari genişletilebilir ama kapsam dışı. `[§12]`
- **Scan ignore:** `.git bin obj node_modules .vs`. **Build-etkileyen uzantılar:** `.cs .xaml .resx .csproj .props .targets`. `[§5/§6]`
- **Hedef repo (girdi, varlık değil):** `D:\Projects\Delta\OSYS` — doğrulandı: 191 csproj · 45 sln · 21 packages.config · 175 legacy (152×v4.6 + 23×v4.8) · 1927 HintPath · 178 post-build copy. `[Eng yer-gerçeği]`

---

# PART A — Konsolide Tasarım (tek doğru kaynak)

> v4.3 gövdesi + tüm Eng (D) + Design (DD) deltaları **inline düzleştirilmiş**. Hiçbir şey çıkarılmadı; review'ların ezdiği gövde ifadeleri burada **güncel gerçeğiyle** yazıldı. Tarihsel "önce şöyleydi" kaydı v4.3'te durur.

## A0. Temel Kararlar

| Karar | Seçim (GÜNCEL) | İz |
|---|---|---|
| Derleme motoru | **`MSBuild.exe` (vswhere) + nuget restore**, shell-out child process | D10 (gövde `dotnet build` iddiası düştü — legacy Framework full-MSBuild gerektirir) |
| Process topolojisi | App (UI) + Supervisor (engine) ayrı process | §0 |
| §6.1 garantisi | Nested Job Object (managed watcher değil) | §3 · D1 |
| Teslim | Walking-skeleton / dikey dilim, **Iteration -1 spike GATE** | §0 · D13 |
| Hedef framework (araç) | .NET 10 (LTS) + WPF | §0 |
| v1 kapsam dışı | Multi-repo | §0 |
| §3.4 flag'leri | v1'de korunur; T33 fast-follow ile kaldırma kanıta bağlı | D9 [EUREKA] |

## A1. Amaç & Temel Akış

Tek masaüstü uygulamadan, tek git repo altındaki yüzlerce birbirine bağımlı C#/WPF projesini bağımlılık sırasına göre, paralel ve yalnız değişenleri derlemek. Akış: **Sync → Branch seç → Build/Rebuild → Canlı çıktı.** `[§1]`

## A2. Mimari & Projeler

| Proje | TFM | Sorumluluk |
|---|---|---|
| `BuildOrchestrator.Core` | net10.0 | Saf çekirdek: scanner, graph (HintPath→producer), **tüm planlama** (BuildSignature, GLOBAL propagation, layer, sıra-koruyan scheduler → `BuildPlan` DTO), state & config persistence. UI/process bağımsız. `[D3]` |
| `BuildOrchestrator.Contracts` | net10.0 | IPC sözleşmesi: command/event DTO, enum, **`BuildPlan`**, polimorfik JSON. |
| `BuildOrchestrator.Supervisor` | net10.0-windows (console) | Orchestration: inner Job, `MSBuild.exe`/nuget shell-out (ProcessRunner), build kuyruğu, per-run **disk** log, IPC server (stdout NDJSON-only). Core'a referans. **Planı yalnız yürütür.** |
| `BuildOrchestrator.App` | net10.0-windows (WPF) | UI/MVVM, tray, single-instance, autostart, **outer Job**, IPC client, custom dark title bar. Supervisor spawn eder. |
| `BuildOrchestrator.Tests` | net10.0 (xUnit) | Core unit + process-control (deterministik) + integration. |

**İlkeler:** App, Supervisor assembly'sine referans vermez (çıktı kopyalanır, runtime spawn). DI baştan kurulu (App + Supervisor). IPC stdio NDJSON; stdout yalnız NDJSON, logging stderr/dosya. `[§2 · D3/D4]`

## A3. Process Kontrolü & Güvenli Durdurma (KRİTİK, ZORUNLU)

Nested Job topolojisi (Global Constraints'teki gibi). Zorunlu kurallar `[§3]`:

1. **Outer Job (App):** açılışta `KILL_ON_JOB_CLOSE` ile kur → Supervisor `CREATE_SUSPENDED` → assign → resume.
2. **Inner Job (Supervisor):** kendi Job'unu kur; her `MSBuild.exe` child'ı `CREATE_SUSPENDED → assign → resume`.
3. **Cascade kill:** App ölünce (normal/crash/Task Manager) → outer Job son handle → Supervisor → inner Job → tüm MSBuild ağaçları ölür. **Deterministik, managed watcher YOK.**
4. **Roslyn paylaşımlı derleyici:** v1'de `-p:UseSharedCompilation=false -nodeReuse:false` → asılı `VBCSCompiler.exe` kalmaz. (T33 ile değişebilir — D9.)
5. **Graceful Stop = copy-aware (2A):** yeni proje kuyruğa alınmaz; in-flight proje **post-build copy dahil** bitene kadar beklenir (timeout, örn. 5sn) → `runCancelled`. **`TerminateJobObject` asla copy ortasında, yalnız proje sınırında.** `[T4]`
6. **Hard Stop (timeout sonrası):** `TerminateJobObject(inner)` proje sınırında → tüm ağaç deterministik ölür. PID-heuristik süpürme YOK. Sonraki run inner Job'u yeniden kurar.
7. **Pencere `X` → tray'e küçülür** (exit DEĞİL); build sürerse kesilmez. İlk seferinde toast (DD12).
8. **Tray Exit → cascade-kill** (build varsa her şey kaskat ölür). Tray'den **Stop** da yapılabilir (build durur, app açık kalır).
9. **Hata derlemeyi öldürmez:** tek proje hatası kuyruğu durdurmaz; "Failed", kalanlar devam.

**Spike şartı (D1):** build job **İÇİNDE başarıyla tamamlanır + breakaway flag GEREKMEZ** (sdk#10150 riski → T23 probe eder).

**Kabul kriteri (deterministik test — D8, sleep yok):** Build çalışırken `X` → app kapanmaz, tray'e küçülür, build devam. · Tray Exit/kill/crash → **≤2sn** içinde MSBuild/VBCSCompiler/child KALMAZ (handle/IOCP sinyali ile assert). · Stop → graceful (copy bütün) ya da timeout sonrası hard-kill, tüm child temizlenir, **ortak bin'de torn DLL yok**.

## A4. Çıktı Dizini Gerçeği (KRİTİK)

- Çıktı mekanizması = **projelerin KENDİ post-build copy event'leri** (`copy /y "$(TargetDir)$(TargetName).*" "c:\OSYS\...\bin\"`). Orchestrator kopyalamayı **kendi yapmaz**; `MSBuild.exe` çağırır, event'ler VS'deki ile birebir. **Ortak dizine dokunulmaz/okunmaz.** `[§4]`
- **VS-parity (ZORUNLU):** her proje VS'deki **aynı ayarlarla** (config, OutputPath, post-build); hiçbir MSBuild ayarı override edilmez. Tek ek: §3.4 flag'leri + worktree'de ayrı çalışma dizini (obj doğal izole).
- Final çıktı branch'e göre **izole edilmez** (bilinçli): "şu an hangi branch+config derlendiyse onun DLL'i ortak dizinde geçerli".
- **Config tek klasör (config-agnostic):** Release, Debug DLL'lerini ezer → **config değişimi tüm projeleri dirty yapar** (A6).
- Yalnız **ara çıktı (obj)** worktree build'lerinde izole; aynı isimli projelerin obj çakışması proje **Id (tam yol)** ile önlenir.
- **build-state GLOBAL** (projectId anahtarlı) çünkü ortak dizin global: "ortak dizinde şu an hangi imza materyalize". Per-branch değil.
- **Worktree çıktı-izolasyonu YOKTUR (D12 reframe):** worktree = ana checkout'u bozmadan farklı branch **kaynağını** derle/çalıştır/test; çıktının ortak havuza yazılması **kasıtlı/istenen**. UI etiketi "committed **kaynak**" (≠ "izole çıktı"). Concurrent-VS guard (T29) + tek-run kilidi korunur.

## A5. Sync — Proje Keşfi & Bağımlılık Grafiği

- Kökte `*.sln` + `*.csproj` recursive tara (ignore listesi). 45 sln kökü.
- **Graf primer = HintPath-basename→producer (D11):** her projenin **evaluated AssemblyName/TargetName**'inden DLL-adı→üretici-proje haritası kurulur; `<HintPath>...\bin\X.dll>` raw-reference'lar bu harita ile kenara çevrilir. **ProjectReference ikincil ek sinyal.** (Gövdedeki "ProjectReference'tan tek graf" + "HintPath opsiyonel enhancement" ifadeleri **düştü**.)
- **Batch MSBuild evaluation (D5):** AssemblyName + ProjectReference + Compile item'ları **tek geçişte** okunur; **csproj/props/targets mtime+hash cache** ile invalidation → yüzlerce projede Sync dakikalarca sürmez.
- **file→project eşlemesi = MSBuild-evaluated Compile item'larından (T19/D11), path-prefix DEĞİL** → linked/shared/`<Compile Include>` proje-dışı dosyalar doğru eşlenir (silent stale build kapanır).
- Tarjan SCC (cycle) + Kahn (topo). Atomik `dependency-graph.json` cache (version + invalidation). **Açılışta cache'ten okunur** (full re-scan yok); tam analiz yalnız **Sync**.
- **Bağımlılık sağlık göstergesi (N7):** cycle dışı = **yeşil**; cycle = **kırmızı + rozet** + tooltip.
- **Liste sırası = build order:** topo sıraya göre dizilir. **Katman pattern varsa** katmanlara göre gruplanır (Katman 1 [topo], 2 [topo], …, "Diğerleri"), her katman kendi içinde topo.
- **Solution belirsizliği (Eng OV#6):** bir csproj **0 veya >1** `.sln`'de olabilir; `ProjectNode.solutionName` **çok-değerli**; "Visual Studio'da Aç" >1 ise seçtir / en yakını. `[T32]`

## A6. Derleme Stratejisi

**Rebuild:** tüm projeler topo sıraya göre; bağımsızlar paralel `MSBuild.exe`.

**Build (incremental) — kalp:** bir proje **yalnız** şu hallerde derlenir:
1. Güncel commit son başarılı commit'ten farklı **ve** projeyi etkiliyor, **veya**
2. Working-tree'de projeyi etkileyen local değişiklik, **veya**
3. **Upstream producer imzası değişti** (GLOBAL graf propagation — T18/T25), **veya**
4. Hiç başarıyla derlenmemiş.

Aksi halde **Skipped**.

- DLL/bin timestamp **asla** okunmaz.
- Dosya→proje: MSBuild-evaluated Compile item + build-etkileyen uzantı → dirty. Üst `Directory.Build.props/targets` → kapsam dirty.
- **Downstream propagation GLOBAL graf üzerinden (T18/T25):** katman-içi analiz cross-layer kenarı **atmaz**; değişen L1, GLOBAL graf'taki L3 bağımlılarını dirty yapar (stale üst katman kapanır). **Safe (varsayılan)** = dirty + transitif bağımlılar; **Fast** = sadece dirty.
- **Signature (D6):** tek Core `BuildSignature.Compute` = `config + HEAD commit + (in-place) local-diff hash + transitive upstream producer signatures`. byte-stable, determinism testli.
- **build-state.json (D2):** projectId anahtarlı **GLOBAL**; single-writer serialized + atomik temp+rename; her proje bitiminde persist (hard-kill'de ilerleme korunur). Başarısızlıkta eski imza korunur. `BuildState { projectId, builtSignature, builtCommit?, lastResult, lastRunAt, lastBranch? }` (`lastBranch` yalnız teşhis). Ayrıca global tekil `OutDirConfig`.
- **Config değişimi (Debug↔Release) → TÜM projeler dirty** (A4); "config değişti, tümü derlenecek" loglanır.

**Paralellik & kaynak:** bağımsızlar eşzamanlı; derece Supervisor'da. **Performans modu (Full/Balanced/Light)** üçünü birden belirler: paralel derece + process priority + **inner Job CPU rate cap** (Light≈%40 / Balanced≈%70 / Full=sınırsız). **Çalışırken değiştirilebilir.** `[§6]`

**Sıra-koruyan paralel scheduler (deterministik, test edilir):** ready set'ten slot boşalınca **build-order'da en önde** gelen seçilir (rastgele/hash/baştan-sondan dispatch YOK). N bağımsız proje → ilk başlayanlar listenin ilk N'i. Bağımlılık bekleyen başlamaz; slot bir sonraki en öndekine gider. Aynı graf + derece → aynı dispatch sırası. `[§6 · scheduler]`

**Katman pattern — layered build (N8):**
- **Tanım:** ayarlarda **sıralı, sınırsız regex** listesi; her pattern bir katman; regex **proje ADINA**; **ilk (en düşük) eşleşen kazanır**.
- **Sert faz bariyeri:** Katman N tamamen bitmeden N+1 başlamaz.
- **Katman-içi dispatch + GLOBAL incremental:** her katman kendi projeleri arasında topo+paralel **dispatch** eder; ama **incremental dirty-propagation GLOBAL graf üzerinden** yürür (T18/T25 — katman yalnız dispatch sırası, dependency analiz kaynağı değil). Önceki katman bariyerle bitti sayılır.
- **Eşleşmeyenler → implicit son "Diğerleri" katmanı** (kendi içinde topo); hiçbir proje atlanmaz, UI'da sayı + uyarı.
- **Pattern verilmezse → tek global graf** (katman yok).
- **Ters katman bağımlılığı (3C):** beklenmez (kullanıcı katmanları bağımlılık sırasına uygun tanımlar varsayımı); yine de **hafif tespit + uyarı (rozet/log, bloklamaz)**; tetiklenirse compile-error + uyarı rozeti görünür. Gelişmiş çözüm ertelendi. `[T15]`

**Branch & worktree (deferred git):**
- Açılışta aktif branch seçili. **Branch seçimi = niyet**; Build'e basılana kadar git'te işlem yok (`git worktree add` dahil).
- **Worktree = her zaman toggle.** Varsayılan: farklı branch → ON, aktif branch → OFF (ikisi değiştirilebilir).
- **ON →** committed HEAD `%LOCALAPPDATA%\...\worktrees\<name>\` altında derlenir; ana working tree değişmez, local hariç. Otomatik standart isim (`<branch>-<n>`), düzenlenebilir; aktif worktree'ler UI'da listelenir.
- **Local-vs-committed = worktree toggle (N9):** OFF → in-place, **local dahil** (imzaya local-diff girer); ON → committed HEAD, local hariç. UI etiketi "local dahil" / "committed temiz" net.
- **Davranış matrisi (Build anında):**

  | Worktree | Branch | Local | Davranış |
  |---|---|---|---|
  | OFF | =Aktif | Yok | In-place, uyarı yok |
  | OFF | =Aktif | Var | In-place, local dahil, commit istenmez, log |
  | OFF | ≠Aktif | Var | **`runBlocked`:** checkout local'i ezer → "worktree'ye geç / commit-stash" |
  | OFF | ≠Aktif | Yok | In-place checkout → VS branch değişir (pre-build confirm — DD14) |
  | ON | herhangi | fark etmez | Worktree committed HEAD; local hariç; ana tree dokunulmaz |

- **Worktree silme + branch guard (N3):** havuz kalıcı (hız/obj cache); her worktree için UI'da **"Sil"** (`git worktree remove`); branch silinmeye çalışılırken worktree tutuyorsa **uyarı + "önce worktree'yi sil"**.
- **Worktree pool ölçek mitigation (CEO/OV#3):** per-worktree disk boyutu UI'da + configurable cap / LRU prune. `[T14]`
- **Git komut sonuçları kontrol edilir** (silent-failure fix); hata → `error` event.

**Eşzamanlılık:** orchestrator tek seferde tek run; OutDir'e kendi-kendine çakışmayı önler.

## A7. UI / UX (tek pencere — OTORİTE: DD1–DD14)

**North-star (DD4):** sakin-hassas dark (Linear/Geist ruhu) + heyecanlı frontier. Heyecan **motion'dan değil, karmaşa altında okunabilirlikten** gelir; **restraint = farklılaşma**. Heyecanın kaynağı = dependency-order build-frontier'ın listede aşağı yürümesi.

**Pencere kabuğu:** custom dark/modern title bar (WPF `WindowChrome`); repo·branch başlıkta (wayfinding). `X` → tray'e küçülür (A3.7), exit etmez. App icon taskbar+tray+pencerede.

**Reconciled Information Architecture (DD1/DD5 — OTORİTE):**
```
┌─────────────────────────────────────────────────────────────────────┐
│ [◆] OSYS · main                                  — □ ×  (dark chrome) │ chrome
├─────────────────────────────────────────────────────────────────────┤
│ ▸ Building 8/120 · 1m04s · ~40s kaldı  [Client.Core][Server.Api]…    │ ① canlı set + GLOBAL progress (DD6/DD7)
├──────────────────────────────┬──────────────────────────────────────┤
│ PROJECT LIST (build-ordered, │ ANA CONSOLE (rol = seçime göre)       │
│ virtualized)      ② frontier │ • seçim yok: run narrative/granular;  │
│ ▌Client.Core         ✓ 2.3s  │   idle: blink cursor + "ready"        │
│ ▌Server.Api    ⟳ building 1.1s│ • kart seçili: tam MSBuild çıktısı   │ ③ detay (on-demand)
│ ▌Auth.Core     ⟳ building 0.4s│                          [← Back]    │
│ ▌Common.Utils       ↷ skipped │                                      │
│ …(viewport-only motion)  ════├──────────────────────────────────────┤
│ (GridSplitter)               │ ÖZET STREAM (kalıcı · zamansal)       │
│                              │ ✓ Client.Core built · 2.3s            │ satır düz-tıkla→detay (DD8)
│                              │ ↷ Common.Utils skipped · no change    │
│                              │ ▌ Server.Api building…█ ← AKTİF satır │ en-yeni = typing (yalnız sakin, DD2)
├──────────────────────────────┴──────────────────────────────────────┤
│ ⟳Sync Σ120 ●98 ✓96 ✗2 ↷22  main▾  ⌥committed temiz▾  ⚡Balanced  Build▸│ aksiyon + bağlam (DD13)
└─────────────────────────────────────────────────────────────────────┘
```
**Attention order (DD5):** ① ne oluyor (frontier seti + global progress) → ② frontier listesi (mekânsal) + özet stream (zamansal) → ③ per-project detay. Title bar/sayaçlar = **chrome**. Hiyerarşi renkle değil **ağırlıkla** (boyut/kontrast/konum). Default split ~%46/%54, GridSplitter konumu persist.

**Sağ pane = 2 yapısal zone (DD1 — gövdedeki "özet XOR detay, tek console" düştü):**
- **Üst = ANA CONSOLE:** seçim yok → run narrative / granular adımlar (idle: blink cursor + "ready"); kart seçili → o projenin tam `MSBuild.exe` çıktısı + **[← Back]**.
- **Alt = KALICI ÖZET STREAM:** kronolojik tek-satır olaylar, **her zaman görünür** (kart seçince özet kaybolmaz); en-yeni satır = **aktif typing satırı** (DD2). Yatay GridSplitter + min-height; kısa pencerede stream → yalnız aktif satıra çöker.

**Console temizleme + granular adım logu (N1):** **Sync**'e basınca console temizlenir + sync adımları baştan; **Build/Rebuild**'e basınca yine temizlenir + derleme adımları baştan. Granular: `Solution'lar taranıyor (N)`, `ProjectReference/HintPath okunuyor`, `Graf kuruluyor / cycle kontrolü`, `Derleme sırası belirlendi (N proje)`, `Katman 1 (Types) derleniyor — M proje`. Önceki run stream'de **terminator satırıyla** kapanır (belirsizlik #7).

**Özet stream satırları:** her proje tek satır + süre (`✓ Client.Core — 2.3s`, `✗ Server.Api — failed (1.1s)`, `↷ Common.Utils — skipped (no source change)`); en altta **`Done` + TOPLAM süre** (`Done — 118 ok, 2 failed, 7 skipped · toplam 4m12s`). İnsan-gibi yazım (tutarlı fiil, nokta ile bit).

**Typing / live-line degradation (DD2 — KRİTİK net spec):**
1. **Drop-to-latest, kuyruk YOK:** yeni olay gelirse mevcut satırı anında settle, aradakileri anında ekle, yalnız tek en-yeni satırı yazmaya başla.
2. **Throughput-suspend:** olay hızı ~3-4 satır/sn'yi aşarsa typing tamamen askıya → satırlar anında; ~400ms sessizleşince typing döner.
3. **Hatalar typing'i her zaman atlar:** failed satır anında render + statik vurgu (anlatılmaz, **anons edilir**).
4. **Hız cap:** bir satır asla ~250ms'den yavaş yazılmaz (uzun satır chunk/snap).
5. **İmleç her zaman blink** (engine canlı); **typing** nadir/sakin an. (Ham MSBuild detayı **asla** harf-harf yazılmaz.)

**Saklama mimarisi (4A/D4):** her projenin tam çıktısı **per-run diske** yazılır (`run-<ts>/`); kart seçilince App diskten **chunk'lı + canlı event'lerle interleave** stream ister (`getProjectLog`, HOL-block yok). "Sadece sonuncusunun logu" bug'ı baştan yok. Hâlâ-derlenen projeye tıklama → **live-stream** + "still going" (belirsizlik #8).

**Kart seçim modeli:** tıkla → **sol accent şeridi kalınlaşır + yazılar bir tık içe kayar** (kutu/border YOK); animasyonlu ama anlık/hızlı. Tek seçim; başka karta tıkla → eski normale. Efekt yalnız **seçili tek kartta** (geçici) → virtualization perf bozulmaz. `[v4 #2]`

**Tek canonical click→detay (DD8):** özet stream'de **HER satır** düz-tıkla = proje seç + detay + Back. **Ctrl+click kaldırıldı.** Ham console'da **metin seçimi kutsal** → "console'a tıkla=seçim kalkar" **kaldırıldı**; çıkış = görünür **[← Back]** (canonical) + "seçili karta tekrar tıkla" (bonus). C6 (özet logdaki hata satırı) → bu genel kurala dahil.

**Durumlar (renk + statü):** Discovered, Queued, Building, Succeeded, Failed, Skipped, CycleDetected (+rozet). Statü = renk + metin/rozet.

**Kartlar:** proje + solution adı; sol accent şerit (statü kodlar); sağ altta "Dosyada Aç" / "Visual Studio'da Aç" (solution >1 ise seçtir — T32). N7 sağlık (yeşil/cycle-kırmızı). **Commit gösterimi (N10):** "şu an `<builtCommit>` → hedef `<targetCommit>`", farklıysa vurgu, tooltip kısa SHA+tarih. Kart = **dense liste satırı** (mosaic değil): ad primary, solution dim, glyph+süre tertiary.

**Build frontier:** liste build-order sıralı; Building kartlar canlı (pulse+shimmer). **Sticky "şu an derleniyor (N)" şeridi = mekânsal canlı set, statik metin günceller (animasyon DEĞİL — DD7); çip→karta git.** Auto-scroll **center-of-gravity**'yi yumuşak izler (yo-yo yasak — T48).

**Motion budget (DD9):** aynı anda **en fazla 1 hero motion** (aktif build'de = frontier kartları; typing burst'te susar; sticky şerit statik). Yalnız **viewport'taki** kartlar anime; settled state (Succeeded/Skipped) **statik** (sonsuz glow yok); kart başına tek motion tipi; **yalnız `RenderTransform`+`Opacity`** (Width/Height/Margin YOK). Liste UI virtualization (`VirtualizingStackPanel`; IsVirtualizing yanlışlıkla kapanmaz). 500–1000 kartta akıcı.

**OS reduced-motion (DD3):** Windows "animasyonları göster" KAPALIYSA typing→anlık metin, pulse/shimmer/shake/stagger→anlık renk/fade. **Uygulama-içi toggle YOK** (gövdedeki "ReducedMotion KALDIRILDI / her zaman açık" düştü).

**Global progress / ETA (DD6):** "Building 8/120 · 1m04s · ~40s kaldı" — frontier/header'da determinate affordance (planda hiç yoktu).

**Interaction state'leri (DD10/Pass2 — tam tablo A8'de test, burada UI):** pre-first-run onboarding ("Başlamak için repo seç" + [Klasör Seç]); 0-proje ("kök altında proje yok"); 0-branch/git-fail (inline retry); **all-skipped = DELIGHT (DD10)** ("Her şey güncel — 120 proje 0.4sn'de kontrol edildi, derlenecek yok" + success affect, gri/fail DEĞİL); partial (Done hata-önce); sync skeleton; engine-died banner ("Build engine durdu [Restart]"). Empty state'ler **feature** (sıcaklık + tek primary action + bağlam).

**Failure orchestration (DD11):** hata anında stream **anında** anons (typing atlanır); run boyunca **scroll-proof, kapatılabilir** "N hata: `<proje>` — [logu aç]"; "✗ Failed" filtre çipi öne. Shake yalnız ikincil ipucu.

**Sync reveal + success flourish + pre-build confirm (DD14, reduce-motion aware):** kartlar build-order'da yukarı→aşağı **staggered fade-in (≤400ms)** → topo sıra görünür; temiz full-success'te Done'da **tek** settle/glow + frontier sakin-yeşil (bir kez); in-place branch değiştiren build öncesi (OFF/≠Aktif) **tek satır sakin confirm**.

**Worktree chip iki sinyal (DD13):** toggle (ON/OFF) görsel net + caret; aktif mod ("local dahil"/"committed temiz") **Build yanında glanceable** (chip içinde gizli değil) — §6 matrisi run'ı bloklayabilir, yüksek-bahis. **X-to-tray ilk toast (DD12).**

**Kısayollar & global hotkey (N6):** **çift-Shift** → branch hızlı arama (chip tooltip'inde duyurulur); **Ctrl+P** → proje/kök seçici; **Ctrl+B/Ctrl+R** → Build/Rebuild (çalışıyorsa Stop); **global hotkey** (varsayılan Alt+B, ayarlanabilir, `RegisterHotKey`) → tray'deyken pencereyi **sağ-alt köşeden animasyonla** çıkar/restore. **Keyboard nav (Pass6):** liste **ok tuşları**, **Enter**=log, **Esc**=back/deselect, **focus-visible ring**.

**Anti-slop (Pass4):** glyph ≠ emoji (✓✗↷⟳ gerçek font glyph); UI = gerçek grotesk (Geist/IBM Plex Sans — "Inter default" değil), **console = gerçek monospace** (JetBrains Mono/Cascadia — Consolas-default değil); accent **statü kodlar** (dekoratif ikinci border yok); restrained radius (console keskin=0, kontroller hafif), **dekoratif gölge yok**; kart=dense row.

**Auto-scroll arbitration (T48):** user-scroll yalnız o bölgeyi duraklatır (~2sn idle→devam, hareket sayacı sıfırlar); öncelik **frontier > console > stream**; frontier center-of-gravity net (yo-yo yasak).

**Tasarım niyeti (N4):** plan niyeti + north-star (DD4) + token-intent (A-altı) + anti-slop taşır; kesin görsel (renk/tipografi/ikon/mockup) **kullanıcıda** (Claude Design); bu plan brief sağlar, pixel comp üretmez.

**Design token-intent (DESIGN.md tohumu — Pass5; değerler N4'te kullanıcıda):** Renk rolleri `surface`/`surface-raised`/`console-bg`/`text-primary`/`text-dim`/`border-subtle` + tek `accent` + status (`success`/`fail`/`building`/`skipped`/`cycle`). Tipografi `display`/`ui`/`mono`. Spacing + küçük/tutarlı radius + minimal elevation. Motion token'ları: `selection`/`frontier-pulse`/`stream-settle`/`typing`(cap'li)/`popup`(RenderTransform+Opacity) — hepsi reduce-motion altında anlık. `[T49]`

## A8. Test Stratejisi (process testleri first-class)

- **Unit (Core):** graph extraction (**HintPath→producer + match-rate**), topo, cycle, **file→proje (MSBuild Compile items)**, **BuildSignature determinism (D6)**, incremental kararı (global imza + commit-delta izole), **branch-bounce (developer→X→developer doğru rebuild)**, **GLOBAL graf forward-propagation (T18: L1 değişti→L3 dirty)**, Safe/Fast, scanner ignore, **sıra-koruyan scheduler dispatch (deterministik)**, **layer assignment (regex first-match + "Diğerleri")**, **config-switch all-dirty**.
- **Process-control (ZORUNLU, deterministik — D8, sleep yok):** gerçek/dummy build → tray Exit / App kill / crash / Stop → **≤2sn artık process yok** (handle/IOCP); **pencere X → tray'e küçülür, process ölmez**; **kill mid-parallel-build → ortak bin'de torn DLL yok + leftover process yok (T9 "2am-Friday")**; **build job İÇİNDE başarılı + no-breakaway (D1/T23)**.
- **State/IPC:** build-state **atomik/tek-yazar + crash-mid-write resilience (D2)**; **getProjectLog chunk interleave → canlı event donmaz (D4)**; **stdout-IPC desync (stray Console.Out)**; IPC framing/max-line.
- **Integration:** çoklu-solution workspace → Sync, Build, Rebuild, branch switch (worktree toggle), Stop, **kart seçimi → herhangi projenin detay log akışı**.
- **Perf:** 500+ kart akıcı scroll; **cold Sync 100+ proje + cache-hit (D5)**; paralel build kazancı; **CPU cap gerçekten tavanı tutar**; log akışında UI bloklanmaz; per-proje disk log bellek tavanını aşmaz.

## A9. Supervisor ↔ UI Sözleşmesi

- **Komutlar:** `syncWorkspace(rootPath)`, `reanalyze()`, `listBranches()`, `listWorktrees()`, `selectBranch(branch)`, `startRun(mode, branch, useWorktree, worktreeName?, config, dependentMode, perfMode)`, `setPerfMode(perfMode)` (canlı), `stopRun(runId)`, `getProjectLog(projectId)` (chunk'lı + interleaved — D4), `deleteWorktree(name)` (N3, branch guard), `openPath(projectId)`, `openInVS(projectId)` (solution >1 ise seçim — T32).
- **Eventler:** `syncProgress`, `syncCompleted`, `worktreesListed`, `runStarted`, `projectStarted`, `projectLog` (ham parça — diske yazılır; UI özet modda göstermez, detay/`getProjectLog` ile akar), `projectSucceeded`(+durationMs), `projectFailed`(+durationMs), `projectSkipped`(+reason), `runCompleted`(+totalDurationMs, ok/failed/skipped), `runCancelled`, `runBlocked`, `error`. Ayrıca: `runProgress` (X/N + ETA — DD6).
- **Tipler:**
  - `ProjectNode { id, name, projectPath, solutionNames[], dependencies[], buildOrder, layerIndex?, layerName?, healthy:bool }` — `solutionNames` çok-değerli (T32); `healthy`=cycle dışı (N7).
  - `BuildState { projectId, builtSignature, builtCommit?, lastResult, lastRunAt, lastBranch? }` — GLOBAL; `builtSignature`=materyalize imza (config+commit+local-diff+upstream); `lastBranch` teşhis. Global tekil `OutDirConfig`.
  - `Worktree { name, branch, path, isActive, diskSizeBytes? }` (T14).
  - `LayerPattern { order:int, regex, name }` (N8).
  - `BuildPlan { ... }` — **Core üretir** (D3): sıralı/katmanlı dispatch + skip kararları; Supervisor yürütür.
  - `RunRequest { mode:'build'|'rebuild', branch, useWorktree:bool, worktreeName?, config:'Debug'|'Release', dependentMode:'safe'|'fast', perfMode:'full'|'balanced'|'light' }`.
  - `ProjectResult { projectId, result:'succeeded'|'failed'|'skipped', durationMs, reason?, builtCommit?, targetCommit? }` (N10).
- **Disiplin (D3/D4):** ölü komut / spec dışı event eklenmez; **planlama Supervisor'da değil Core'da** (gövde "Supervisor planner" düzeltildi). **stdout yalnız NDJSON**; logging stderr/dosya. `skipped` gerçek reason taşır.

## A10. Yapılandırma

Kök dizin · Build config (Debug varsayılan / Release; config-agnostic → değişince tümü dirty) · Perf modu (Full/Balanced/Light; ana UI'da perf chip) · Worktree varsayılanı (farklı branch ON / aktif OFF; havuz konumu, **kalıcı**, per-worktree silinebilir + cap/LRU — T14) · Downstream modu (Safe/Fast) · **Katman pattern editörü** (sıralı sınırsız regex, ekle/sil/sırala; boş→global) · **Kısayollar** (çift-Shift/Ctrl+P/Ctrl+B-R/global hotkey, özelleştirilebilir) · Cache konumu · **Görsel kimlik** (logo+icon, dark title bar) · **KALDIRILANLAR:** LogLevel (artık 2-zone), in-app Reduced Motion (OS ayarı — DD3). `[§11]`

## A11. Kapsam Sınırları (v1)

**İçinde:** tek repo · `MSBuild.exe`+nuget shell-out · nested-Job (+CPU cap, X→küçül/Exit→cascade) · sync/graph/cache (HintPath→producer, build-order liste, N7 sağlık) · rebuild + incremental (GLOBAL build-state, N10 commit, GLOBAL propagation) · sıra-koruyan scheduler · katman pattern · worktree toggle (+sil & branch guard, local-vs-committed, pool cap) · tam UI/UX (2-zone console + typing degradation, kart seçim efekti, build frontier, chip selector, kısayollar+hotkey, dark title bar, logo/icon, motion budget, interaction states, all-skipped delight, global progress/ETA, failure orchestration, keyboard nav, SR/kontrast) · config · tray/autostart/single-instance · perf modları · unit+process+integration test · README · `dotnet publish`.

**Dışında (sonraya, gerekçeli):** Multi-repo · MSIX/installer/auto-update · WinUI Composition · graf dalgası görselleştirme · özel CPU % slider · Headless/CLI · eski-kod bug araştırması (C11/C12) · CLAUDE.md çoklu-dosya + agent senkron (N2 — implementation tooling) · katman "standart dışı durum" gelişmiş çözümü · packages.config→PackageReference migration (175 legacy değiştirilmez) · worktree gerçek output izolasyonu (mümkün değil + istenmiyor — D12) · node reuse/shared compilation v1'de açmak (T33 fast-follow) · komut paleti / fuzzy search · onboarding tour · light mode · uygulama-içi motion/tema toggle.

## A12. Varsayımlar / Varsayılanlar

Tek git repo · ortak çıktı projelerin post-build event'leriyle dolar (orchestrator dokunmaz), config-agnostic, imza=config+commit+local-diff+upstream, build-state GLOBAL · kullanıcı VS'de aynı projeleri eşzamanlı derlemez · varsayılanlar: Debug, Safe, worktree (farklı branch ON), Full Power, **console=özet stream + idle console**, **OS reduced-motion'a saygı** · graf cache'ten, tam analiz yalnız Sync · katman pattern verilirse tek-yönlü varsayılır (ters tespit edilirse uyarılır, bloklanmaz) · `X`→tray, kapanış yalnız Exit (build varsa cascade) · araç .NET 10; derlenen projeler `MSBuild.exe` + kullanıcının VS toolchain'i ile · **trust boundary:** root dizin VS'de açılmış kadar güvenilir (arbitrary MSBuild exec — T17).

---

# PART B — Birleşik Task Backlog (T1–T49 dedupe + sıralı)

> v4.3'teki **49 task ID korundu** (izlenebilirlik). Absorbe/merge olanlar açıkça işaretli — **hiçbiri kaybolmadı**, başka bir task içinde yaşıyor. "İt." = atandığı iterasyon. Kaynak: CEO (T1–T21) · Eng (T22–T33) · Design (T34–T49).

| ID | Başlık (kısa) | Durum | İt. | Kaynak/iz |
|---|---|---|---|---|
| **T23** | Iteration -1 Feasibility Spike (GATE) — 5 proje MSBuild.exe+nuget derle, HintPath match-rate, cascade-kill gerçek MSBuild ağacı ≤2s + breakaway probe, D9 flag delta | AKTİF (gate) | **-1** | Eng/D1,D13 · **T1+T2 absorbe** |
| ~~T1~~ | spike nested-Job cascade (dummy) | → **T23** (gerçek MSBuild ağacı) | -1 | CEO |
| ~~T2~~ | spike OSYS HintPath/Compile ölç | → **T23 + T24** | -1 | CEO |
| **T22** | Engine: MSBuild.exe (vswhere) + nuget restore/-t:restore, per project; nested Job+shell-out+cascade korunur | AKTİF | 0/2 | Eng/D10 |
| **T30** | Tek `ProcessRunner` (exit-code+stderr+timeout zorunlu); non-zero build=projectFailed | AKTİF | 0 | Eng/D7 |
| **T7** | IPC framing: length-prefixed/escaped + max-line NDJSON | AKTİF | 0 | CEO · **T28 genişletir** |
| **T28** | getProjectLog chunk+interleave; **stdout NDJSON-only**, logging stderr/dosya | AKTİF | 0/2 | Eng/D4 |
| **T6** | Supervisor crash recovery: App child handle izler → error + restart | AKTİF | 0 | CEO |
| **T31** | Process-control testleri deterministik (handle/IOCP + timeout tavanı), sleep yok | AKTİF | 0 | Eng/D8 |
| **T24** | Graph: HintPath-basename→producer (evaluated AssemblyName/TargetName); PR ikincil; **batch tek-geçiş eval + mtime/hash cache**; 45 sln; file→proje=Compile items | AKTİF | 1 | Eng/D11,D5 · **T3+T19+T2 absorbe** |
| ~~T3~~ | HintPath→producer resolver | → **T24** | 1 | CEO |
| ~~T19~~ | file→project MSBuild Compile items | → **T24** | 1 | CEO |
| **T32** | Solution belirsizliği: csproj 0/>1 sln + Open-in-VS seçimi; eval 45 sln | AKTİF | 1 | Eng/OV#6 |
| **T26** | Planning Core'da: `BuildPlan` DTO; Supervisor yürütür; §9 wording | AKTİF | 1 | Eng/D3 |
| **T25** | Signature+propagation: tek Core BuildSignature; transitive upstream; **skip GLOBAL graf gate (self-source değil)** | AKTİF | 3 | Eng/D6,D11 · **T18 pekiştirir** |
| ~~T18~~ | layered incremental GLOBAL propagation | → **T25** | 3 | CEO/OV#6 |
| **T27** | build-state single-writer serialized + atomik temp+rename, per-project persist + crash test | AKTİF | 3 | Eng/D2 |
| **T4** | Stop: copy-aware graceful; hard-kill yalnız proje sınırı, copy ortasında asla | AKTİF | 0(base)/2(copy-aware) | CEO/2A |
| **T5** | Logs: per-run disk project log + decision log; seçince diskten stream | AKTİF | 2 | CEO/4A |
| **T8** | Parallel copy retry-on-sharing-violation + backoff; contention ölç | AKTİF | 2 | CEO |
| **T9** | Test: kill mid-parallel-build → torn DLL yok + leftover yok | AKTİF | 2 | CEO |
| **T29** | Worktree wording/label dürüstlüğü (kaynak-izole/çıktı-ortak) + T21 guard + tek-run kilidi | AKTİF | 3 | Eng/D12 · **T21 absorbe** |
| ~~T21~~ | worktree shared-bin guard + concurrent-VS | → **T29** | 3 | CEO/OV#4 |
| **T11** | Edge input: detached HEAD / no-commits / shallow → treat-as-dirty + warn | AKTİF | 3 | CEO |
| **T13** | Path sanitization: worktree + branch (.., reserved, drive) | AKTİF | 3 | CEO |
| **T14** | Worktree pool: per-worktree disk + configurable cap / LRU prune | AKTİF | 3 | CEO/OV#3 |
| **T15** | Doc+guard: layer reverse-dep detect+warn-only (3C) + compile-error semptomu | AKTİF | 3 | CEO/3C |
| **T34** | Typing/live-line degradation engine (drop-to-latest, throughput-suspend, failure-skip, ~250ms cap, blink) | AKTİF | 4 | Design/DD2 |
| **T35** | Console/stream 2-zone layout (ana console + kalıcı stream + splitter/min-height/reflow) | AKTİF | 4 | Design/DD1 |
| **T36** | OS reduced-motion oku → anlık'a düş; in-app toggle yok | AKTİF | 4 | Design/DD3 |
| **T37** | Interaction state'leri: pre-first-run, 0-proje, 0-branch/git-fail, all-skipped DELIGHT, partial, sync skeleton, engine-died | AKTİF | 4 | Design/Pass2,DD10 |
| **T38** | Global progress/ETA (X/N + geçen + kaba kalan) | AKTİF | 4 | Design/DD6 |
| **T39** | Failure orchestration: anlık anons + scroll-proof "logu aç" + Failed filtre öne | AKTİF | 4 | Design/DD11 |
| **T40** | Discoverability: tek canonical click→detay, görünür Back, console-deselect kaldır, worktree chip 2-sinyal, X-to-tray toast, çift-Shift tooltip | AKTİF | 4 | Design/DD8,DD12,DD13 |
| **T41** | Motion budget: 1 hero, viewport-only, settled=statik, sticky şerit statik | AKTİF | 4 | Design/DD9,DD7 |
| **T42** | Sync reveal: build-order staggered fade-in ≤400ms (reduce-motion aware) | AKTİF | 4 | Design/DD14 |
| **T43** | Pre-build context confirm (in-place branch switch öncesi tek-satır) | AKTİF | 4 | Design/DD14 |
| **T45** | Anti-slop enforcement (glyph≠emoji, grotesk+mono, restrained radius/no-shadow, accent kodlar, dense row) | AKTİF | 4 | Design/Pass4 |
| **T46** | Keyboard nav: ok/Enter/Esc + focus-visible ring | AKTİF | 4 | Design/Pass6 |
| **T47** | Screen reader + kontrast (automation name+statü; stream live-region satır-bir-kez; dim ≥4.5:1) | AKTİF | 4 | Design/Pass6 |
| **T48** | Auto-scroll arbitration + frontier center-of-gravity (yo-yo yasak; bölge-lokal duraklatma) | AKTİF | 4 | Design/belirsizlik#5,#6 |
| **T10** | Empty/error UI state (0 proje, 0 branch, all-skipped, git-list hatası) | AKTİF | 4 | CEO (T37 ile örtüşür) |
| **T12** | Mid-run lock: Building'de branch/config/worktree selector kilidi | AKTİF | 4 | CEO |
| **T16** | Autostart temiz Idle açar; exe değişiminden önce tam exit | AKTİF | 4 | CEO |
| **T20** | CPU-cap × post-build copy/git/IPC etkileşimi ölç; copy fazına rate floor | AKTİF | 5 | CEO/OV#2 |
| **T33** | D9 fast-follow: spike Job-kill kanıtlarsa node reuse + shared compilation aç (flag kaldır) | AKTİF (koşullu) | 5 | Eng/D9 |
| **T44** | Success flourish: Done tek settle/glow + frontier sakin-yeşil (bir kez) | AKTİF | 5 | Design/DD14 |
| **T49** | Token-intent → DESIGN.md (semantic renk/tipografi/spacing/radius/elevation/motion) | AKTİF | 4/5 | Design/Pass5 |
| **T17** | Trust-boundary doc: root dizin VS-açılmış gibi güvenilir | AKTİF | 5 | CEO |

**Özet:** 49 ID → 6'sı absorbe (T1,T2,T3,T18,T19,T21), 43 aktif task. Hiçbiri silinmedi.

---

# PART C — Iterasyon Yol Haritası (tümü konsolide)

> Walking-skeleton: her iterasyon uçtan uca çalışır + gösterilebilir. **Iteration -1 GATE'tir** (geçmeden It-0 başlamaz). Her iterasyonun **acceptance criteria**'sı = "bitti" tanımı.

| It. | Teslim | Tasklar | Acceptance (bitti tanımı) |
|---|---|---|---|
| **-1** | **Feasibility Spike (GATE, throwaway)** — gerçek OSYS'te 3(+1) yes/no | T23 | (a) 5 temsilî legacy proje MSBuild.exe+nuget ile **green** derlenir; (b) HintPath→producer **match-rate ölçüldü** + eşik kararı; (c) cascade-kill gerçek MSBuild ağacı **≤2s, 0 orphan, breakaway flag gerekmez**; (d) D9 flag-on kill+hız delta kaydedildi. **SPIKE-RESULTS.md** 3(+1) verdict ile yazıldı. Herhangi biri başarısız → STOP + plan revize. |
| **0** | İki process + stdio IPC + **nested Job cascade** + minimal pencere (root seç → uzun child → canlı log → çalışan Stop) + DI iskeleti | T22(resolve), T30, T7, T28(base), T6, T31, T4(base) | §3 deterministik kabul testi **geçer** (X→tray; Exit/kill/crash → ≤2s process yok). stdout yalnız NDJSON. ProcessRunner exit-code zorunlu. Supervisor crash → App error+restart. |
| **1** | Sync/graph: scan, **HintPath→producer graf**, Tarjan/Kahn, batch eval+cache, **BuildPlan (Core)**, build-order kartlar + N7 sağlık | T24, T32, T26 | Gerçek OSYS Sync **cache-hit'te hızlı**; kartlar build-order'da dolar; cycle kırmızı/sağlık yeşil; csproj 0/>1 sln doğru; planlama tamamen Core'da (`BuildPlan` testli). |
| **2** | **Rebuild (gerçek, paralel):** MSBuild.exe+nuget per project, **sıra-koruyan scheduler**, per-run **disk log**, özet log (Sync/Build temizle + granular) + kart seçince detay (chunk stream), copy-aware Stop, parallel-copy retry, hata izolasyonu, sayaçlar | T22(invoke), T28(stream), T5, T4(copy-aware), T8, T9 | OSYS rebuild paralel **green**; dispatch sırası deterministik (test); kill mid-build → torn DLL yok + leftover yok; herhangi karta tıkla → o projenin tam logu (diskten). |
| **3** | **Incremental:** commit/diff/status, **GLOBAL build-state (atomik)**, **GLOBAL propagation (T25)**, Safe/Fast, **worktree toggle modeli** (deferred git, oto-isim, matris, local-vs-committed) + obj izolasyon, Skipped, **kartta commit (N10)**, **katman pattern** (regex/bariyer/katman-içi dispatch/Diğerleri) | T25, T27, T11, T13, T14, T29, T15 | Branch-bounce doğru rebuild; L1 değişti→L3 dirty (GLOBAL test); config-switch all-dirty; worktree ON/OFF matris + sil + branch guard; layer assignment (first-match+Diğerleri) testli; build-state crash-mid-write resilient. |
| **4** | **UX polish (design):** 2-zone console + typing degradation, kart seçim efekti, build frontier + sticky şerit, chip selector + popup, worktree liste+Sil+guard UI, kısayollar+hotkey, per-card animasyon (motion budget), filtre/Stop morph, dark title bar + logo/icon, tray (X→küçül/Exit→cascade), autostart, single-instance, config ekranı (+layer editör; LogLevel/ReducedMotion YOK), interaction states, global progress/ETA, failure orchestration, keyboard nav, SR/kontrast, OS reduced-motion | T34–T48, T10, T12, T16, T49(tohum) | Typing degradation spec'e uyar (FIFO yok); 500–1000 kart akıcı (viewport-only motion); all-skipped DELIGHT; reduce-motion altında anlık; keyboard-first çalışır; X→tray ilk toast; mid-run selector kilidi. |
| **5** | **Perf + dağıtım + docs:** perf modları (derece+priority+**Job CPU cap**, canlı), 500–1000 perf doğrulama, **README**, `dotnet publish`, fast-follow + son cila | T20, T33(koşullu), T44, T49, T17 | CPU cap gerçekten tavanı tutar (test); copy fazı starve olmaz (T20 rate floor gerekiyorsa); publish çalışır exe üretir; README + trust-boundary doc; T33 spike kanıtladıysa flag'ler kaldırılır. |

**Paralelizasyon (Eng — worktree lanes):** Seam önce **Contracts** (DTO/event + BuildPlan) sabitlenir → **Lane A Core** (pure) · **Lane B Supervisor** (Job/ProcessRunner/MSBuild/queue/disk-log/IPC) · **Lane C App/UI** paralel. Contracts değişimi üçe yayılır (koordine). Walking-skeleton dikey-dilim baskın: It-0 (process+IPC+Job) önce iner.

---

# PART D — Detaylı Sıradaki Adım: Iteration -1 Feasibility Spike (T23, GATE)

> **Bu iterasyon throwaway/investigation'dır** — production kodu değil, kanıt üretir. Çıktı = `D:\Projects\Other\Apps\app_build_orchestrator\.claude\outputs\<ts>-spike-results.md`. **Üç(+bir) yes/no kanıtlanmadan It-0 başlamaz.** Spike kodu ayrı bir klasörde (`spike/`) durur, ana solution'a girmez.

> **Neden bite-sized TDD değil:** spike feasibility ölçer, davranış test etmez. "Test" = her probe'un net pass/fail eşiği. It-0+ için detaylı TDD adımları **spike geçtikten sonra** yazılır (spike sonucu engine/graf'ı değiştirebilir).

**Files:**
- Create: `spike/` (geçici klasör — ana solution dışında)
- Create: `spike/run-spike.ps1` (probe runner)
- Create: `.claude/outputs/<ts>-spike-results.md` (verdict belgesi)
- Read-only girdi: `D:\Projects\Delta\OSYS`

**Önkoşul:** VS Build Tools veya VS kurulu (MSBuild.exe + `vswhere`); `nuget.exe` PATH'te veya indirilebilir. OSYS repo'su temiz/erişilebilir.

### S1 — MSBuild.exe + nuget resolve (engine var mı?)

- [ ] **Adım 1:** `vswhere` ile MSBuild.exe yolunu bul.

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
  -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
"MSBuild: $msbuild"
& $msbuild -version
```

- [ ] **Adım 2:** `nuget.exe` erişimini doğrula (`nuget help` veya `msbuild -t:restore` denenecek).

**Pass:** MSBuild.exe yolu bulunur + sürüm yazılır (full-MSBuild, Framework destekli). **Fail → STOP:** engine yoksa plan engine kararı revize.

### S2 — 5 temsilî legacy proje uçtan uca derle (araç repo'yu derliyor mu? — en pahalı belirsizlik, D13)

- [ ] **Adım 1:** OSYS'te 5 temsilî csproj seç (kapsayıcı karışım): (i) yaprak legacy v4.6, (ii) `packages.config`'li, (iii) çok HintPath'li, (iv) WPF, (v) post-build copy'li.

- [ ] **Adım 2:** Her biri için restore + build, exit-code + çıktı DLL + post-build copy'yi yakala.

```powershell
foreach ($proj in $projects) {
  & $msbuild $proj -t:restore                                  # veya: nuget restore <çözüm/proje>
  & $msbuild $proj -p:Configuration=Debug `
    -p:UseSharedCompilation=false -nodeReuse:false `
    -clp:Summary
  "$proj → exit=$LASTEXITCODE"
}
```

**Pass:** 5/5 **exit 0** + beklenen çıktı DLL üretilir + post-build copy ortak bin'e çalışır (VS-parity). **Fail → STOP:** engine yaklaşımı (D10) revize; muhtemelen ek toolset/restore stratejisi gerekir.

### S3 — HintPath→producer match-rate (graf güvenilir mi? — D11)

- [ ] **Adım 1:** 191 csproj üzerinde batch MSBuild evaluation ile her projenin **AssemblyName/TargetName** ve **HintPath** item'larını oku (assembly yüklemeden, shell-out).

- [ ] **Adım 2:** DLL-basename→üretici-proje haritası kur; tüm HintPath referanslarının **repo-içi** üreticilere çözülme oranını hesapla. Çözülmeyenleri listele.

```powershell
# kavramsal: her csproj için
#   & $msbuild $proj -getProperty:AssemblyName
#   & $msbuild $proj -getItem:Reference  (HintPath dahil)
# producer map = { basename(AssemblyName) -> projeId }
# match-rate = (çözülen intra-repo HintPath) / (toplam intra-repo HintPath)
```

**Pass eşiği:** intra-repo HintPath referanslarının **≥%95'i** bir üreticiye çözülür (kalan, harici/3rd-party olarak triage edilir). **Kısmi (eşik altı):** STOP değil ama **It-1'den önce** fallback strateji (manuel mapping / ek heuristik) plana eklenir — match-rate raporu ile.

### S4 — Nested Job cascade-kill gerçek MSBuild ağacına karşı (§3 garantisi + breakaway — D1)

- [ ] **Adım 1:** Minimal C# spike: outer Job (`KILL_ON_JOB_CLOSE`) kur → child process `CREATE_SUSPENDED` → assign → resume; child, yavaş/çok bir OSYS derlemesi başlatsın (MSBuild node'ları + VBCSCompiler doğsun).

- [ ] **Adım 2:** Outer process'i öldür (Job handle kapat / Terminate). `≤2sn` içinde `msbuild`/`VBCSCompiler`/`conhost` torunlarının kalmadığını **handle/wait sinyaliyle** doğrula (sleep-say değil — D8).

- [ ] **Adım 3:** **Breakaway probe (D1):** build, Job **İÇİNDE** başarıyla tamamlanıyor mu — `JOB_OBJECT_LIMIT_BREAKAWAY_OK`/`SILENT_BREAKAWAY` **gerekmeden**? (sdk#10150: MSBuild Job içinde kendi job'unu kuramayıp patlayabilir.)

**Pass:** kill sonrası ≤2sn 0 orphan **VE** build Job içinde breakaway flag'siz tamamlanır. **Fail → STOP:** §3 process-control yaklaşımı revize (breakaway konfigürasyonu / topoloji değişikliği).

### S5 — D9 flag delta (node reuse + shared compilation hız/güvenlik — fast-follow gerekçesi)

- [ ] **Adım 1:** Aynı (orta boy) derlemeyi iki kez ölç: (a) `-nodeReuse:false -p:UseSharedCompilation=false`, (b) reuse açık. Wall-time delta kaydet.

- [ ] **Adım 2:** Reuse açıkken S4 kill'i tekrarla → hâlâ ≤2sn 0 orphan mı?

**Sonuç (gate değil, kayıt):** reuse açıkken kill ≤2sn korunuyor + anlamlı hız kazancı varsa → **T33 fast-follow haklı** (It-5). Aksi halde v1 flag'leri kalır.

### S6 — Verdict belgesi + GATE kararı

- [ ] **Adım 1:** `.claude/outputs/<ts>-spike-results.md` yaz: S1–S5 her biri **PASS/FAIL/PARTIAL** + ölçülen sayılar (match-rate %, kill süresi, hız delta) + ham komut çıktıları özeti.

- [ ] **Adım 2:** GATE kararı:
  - **Tüm gate'ler PASS** → It-0 başlar; It-0 için detaylı TDD planı yazılır (writing-plans 2. tur).
  - **S2 veya S4 FAIL** → It-0 BAŞLAMAZ; ilgili plan bölümü (engine/process) revize → spike tekrar.
  - **S3 PARTIAL** → It-0 başlayabilir ama It-1 backlog'una HintPath fallback task'ı eklenir.

**Spike acceptance:** SPIKE-RESULTS.md mevcut + 3 ana gate (S2/S3/S4) net karara bağlı + D9 kaydı var. Spike kodu ana solution'a sızmadı.

---

## İzlenebilirlik & Self-Review

**Spec coverage (v4.3 → v5):** §0–14 gövde → Part A (deltalar inline). CEO T1–T21 / Eng D1–D13,T22–T33 / Design DD1–DD14,T34–T49 → Part B (43 aktif + 6 absorbe, **hiçbiri kaybolmadı**). §10 walking-skeleton + Iteration -1 → Part C (acceptance ile). §14 "writing-plans Iteration 0" + spike gate → Part D (Iteration -1 detaylı). N1–N10 özellikleri → A5/A6/A7 + Part B'de etiketli. Tüm "NOT in scope" → A11. **Açık gap yok.**

**Korunan kritik kararlar (ezilmedi):** MSBuild.exe (D10) · HintPath→producer (D11) · GLOBAL build-state/propagation (D2/T25) · nested Job cascade + ≤2s (§3/D1) · copy-aware stop (2A) · 2-zone console + typing degradation (DD1/DD2) · OS reduced-motion (DD3) · katman pattern (N8) · worktree toggle + local-vs-committed + sil/guard (N9/N3/D12) · sıra-koruyan scheduler · motion budget (DD9) · all-skipped delight (DD10) · global progress/ETA (DD6) · tray X→küçül/Exit→cascade · kısayollar+hotkey (N6) · commit gösterimi (N10) · sağlık göstergesi (N7) · planlama Core'da (D3) · stdout NDJSON-only (D4).

---

## Execution Handoff

Plan kaydedildi: `.claude/outputs/2026-06-29-13-06-build-orchestrator-plan-v5-final-implementation.md`.

**Sıradaki adım = Iteration -1 Feasibility Spike (Part D).** Bu gate geçmeden It-0 kodlanmaz. Spike sonrası It-0 için detaylı TDD planı (writing-plans 2. tur) yazılır.

İki yürütme seçeneği (spike + sonraki iterasyonlar için):
1. **Subagent-Driven (önerilen)** — task başına taze subagent, aralarda review (`superpowers:subagent-driven-development`).
2. **Inline Execution** — bu session'da batch + checkpoint (`superpowers:executing-plans`).

> Not: v4.3 dokunulmadı (donmuş arşiv/otorite). v5 ondan türetilmiş tek uygulama kaynağıdır.
