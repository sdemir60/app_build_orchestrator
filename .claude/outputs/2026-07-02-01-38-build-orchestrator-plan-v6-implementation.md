# Build Orchestrator — Plan v6 (Final, Uygulanabilir)

> **For agentic workers:** REQUIRED SUB-SKILL — Bu planı task-by-task uygulamak için `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans` kullan. Adımlar checkbox (`- [ ]`) ile izlenir.

> **v6 nedir / ne DEĞİLDİR.** v6 = v5'in ([2026-06-29-13-06-build-orchestrator-plan-v5-implementation.md](2026-06-29-13-06-build-orchestrator-plan-v5-implementation.md)) **türevi**; v5 **tek uygulama kaynağı**ydı, v6 onu **4 onaylı yeni karar + fold-in cilalarla** günceller. **Hiçbir v5/v4.3 kararı silinmedi veya ezilmedi.** Değişen/eklenen her yer `[v6Δ]` etiketiyle işaretli; çelişki çıkarsa **v6'daki `[v6Δ]` son sözdür**, aksi halde v5/v4.3 iz etiketi (`[v4.3 §X · D.. · DD.. · N.. · Tnn]`) geçerlidir. Tasarım otoritesi zinciri: v4.3 (donmuş arşiv) → v5 (düzleştirilmiş) → **v6 (bu dosya, güncel).** Delta design-system + Claude Design promptu: [2026-07-02-01-38-delta-design-system-v1.md](2026-07-02-01-38-delta-design-system-v1.md).

**Goal:** Yüzlerce legacy .NET Framework C#/WPF projesini (tek git repo, OSYS) bağımlılık sırasına göre, paralel ve yalnızca değişenleri derleyen; derlemeyi ayrı bir Supervisor process'te nested Job Object ile yöneten, dark/modern WPF masaüstü orchestrator.

**Architecture:** İki process (App/WPF + Supervisor/console) + saf Core + Contracts + Tests. App = view + outer Job sahibi; Supervisor = inner Job + her projeyi `MSBuild.exe` ile shell-out derler; Core = tüm planlama (graf, signature, scheduler, layer) saf ve test edilebilir. İletişim stdio NDJSON. Teslim = Iteration -1 (gating spike) → It-0..5 walking-skeleton dikey dilim.

**Tech Stack:** .NET 10 (LTS) + WPF · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · xUnit · **derleme motoru `MSBuild.exe`** (VS Build Tools/VS, `vswhere` ile resolve) + `nuget restore`/`msbuild -t:restore` · Win32 Job Object / RegisterHotKey / WindowChrome P/Invoke.

---

## v6 DELTA CHANGE SET (v5 → v6, dört onaylı karar + fold-in)

> Bu bölüm v6'nın v5'e **kıyasla** getirdiği her şeyin tek-bakış listesidir. Gövde içinde her biri `[v6Δ-N]` ile işaretli.

| # | Delta | Özet | Etkilediği yerler |
|---|---|---|---|
| **Δ1** | **Branch-driven worktree** | Branch seçimi belirler; ayrı "local dahil/hariç" toggle YOK (türetilmiş etiket). Farklı branch → worktree ZORUNLU (aktif branch hiç değişmez); aynı branch → tek worktree toggle (OFF=in-place+local, ON=committed+worktree). `runBlocked` ve in-place branch-switch onayı KALKAR. | A6, A7(DD13/DD14), A9, A10, A12, T29, Part C It-3 |
| **Δ2** | **Dependency graf paneli** | Sol panel ikiye bölünür: sol-üst = yerleşik DAG görselleştirme (canlı statü renkli, frontier grafta akar, node odak), sol-alt = mevcut liste. "Graf görselleştirme" out→in scope. Core saf kalır (node/kenar/katman zaten üretir), render+animasyon App. | A7(IA+yeni alt bölüm), A8, A11, A9(opsiyonel), yeni **T50/T51**, Part C It-4/It-5 |
| **Δ3** | **Will-build noktası** | Kart accent = build statüsü; ayrı NOKTA = pre-run tahmin (amber=dirty/derlenecek, gri=güncel, hollow=Sync öncesi). N7 sağlık → yalnız cycle kırmızı-rozetine iner. | A5, A7, A8, A9, Part C It-1/It-3 |
| **Δ4** | **Çoklu hata → Failed filtresi** | Banner "✗ N hata — proj1, proj2 [Failed'a git]"; buton Failed filtre çipini uygular; her başarısız satır KENDİ logu (DD8 canonical). Birleşik/çoklu-seçim YOK. | A7(DD11), T39 |
| **Δ5** | **Fold-in cilalar** | (a) Özet stream satırı: hover-bg + seçili + tekrar-tıkla-deselect (kartla birebir). (b) Kart tekrar-tıkla → deselect + Back kaybolur + ana ekran (canonical, "bonus" değil). (c) Branch chip aranabilir. (d) Debug/Release action-bar segment toggle (config-agnostic uyarısı). (e) "Dosyada Aç/VS'de Aç" estetik ikon (hover-reveal korunur). (f) Statü glyph'leri ince-halka daire-içi serbest; anti-slop yasağı yeniden yazılır (dekoratif dolu renkli rozet yasak; amaçlı halka glyph serbest). | A5/A7(DD8/DD10/N6/Pass4), A10, T39/T40/T45 |

---

## Global Constraints

Aşağıdaki değerler **her task'ın** örtük gereksinimidir; ihlal = plan ihlali. v5'ten korunur; `[v6Δ]` işaretliler güncellendi.

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
- **OS reduced-motion'a saygı** (uygulama-içi toggle YOK). `[DD3]`
- **Console = mutlaka monospace**; status = **glyph + renk + metin** (colorblind-safe; ≥4.5:1). `[DD3/DD4]`
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
| Hedef framework (araç) | .NET 10 (LTS) + WPF | §0 |
| v1 kapsam dışı | Multi-repo | §0 |
| §3.4 flag'leri | v1'de korunur; T33 fast-follow kanıta bağlı | D9 |
| **Worktree modeli** | **Branch-driven: branch seçimi belirler, ayrı local-toggle yok** | **[v6Δ-1]** · D12/N9 revize |
| **Sol panel graf** | **Dependency graf paneli (yerleşik DAG, canlı frontier) IN-scope** | **[v6Δ-2]** · v5 A11'den taşındı |

## A1. Amaç & Temel Akış

Tek masaüstü uygulamadan, tek git repo altındaki yüzlerce birbirine bağımlı C#/WPF projesini bağımlılık sırasına göre, paralel ve yalnız değişenleri derlemek. Akış: **Sync → Branch seç → (Debug/Release, worktree) → Build/Rebuild → Canlı çıktı (graf + liste + console + stream).** `[§1 · v6Δ-2/Δ5]`

## A2. Mimari & Projeler

| Proje | TFM | Sorumluluk |
|---|---|---|
| `BuildOrchestrator.Core` | net10.0 | Saf çekirdek: scanner, graph (HintPath→producer), **tüm planlama** (BuildSignature, GLOBAL propagation, layer, sıra-koruyan scheduler → `BuildPlan` DTO), **pre-run dirty/will-build kümesi** `[v6Δ-3]`, state & config persistence. UI/process bağımsız. `[D3]` |
| `BuildOrchestrator.Contracts` | net10.0 | IPC sözleşmesi: command/event DTO, enum, **`BuildPlan`**, polimorfik JSON. |
| `BuildOrchestrator.Supervisor` | net10.0-windows (console) | Orchestration: inner Job, `MSBuild.exe`/nuget shell-out, build kuyruğu, per-run disk log, IPC server (stdout NDJSON-only). Planı yalnız yürütür. |
| `BuildOrchestrator.App` | net10.0-windows (WPF) | UI/MVVM, tray, single-instance, autostart, outer Job, IPC client, custom dark title bar. **Dependency graf render+animasyon burada** (Core'un node/kenar/katman verisinden; Core saf kalır). `[v6Δ-2]` Supervisor spawn eder. |
| `BuildOrchestrator.Tests` | net10.0 (xUnit) | Core unit + process-control + integration. |

**İlkeler:** App, Supervisor assembly'sine referans vermez. DI baştan kurulu. IPC stdio NDJSON; stdout yalnız NDJSON. `[§2 · D3/D4]`

## A3. Process Kontrolü & Güvenli Durdurma (KRİTİK — v5'ten değişmedi)

Nested Job topolojisi. Kurallar `[§3]`: Outer Job (App) `KILL_ON_JOB_CLOSE` → Supervisor suspended→assign→resume; Inner Job (Supervisor) her `MSBuild.exe` child'ı suspended→assign→resume; **cascade kill** App ölünce deterministik; Roslyn paylaşımlı derleyici v1'de kapalı; **Graceful Stop copy-aware (2A)** proje sınırında; **Hard Stop** `TerminateJobObject(inner)` proje sınırında; **Pencere X → tray'e küçülür** (ilk toast DD12); **Tray Exit → cascade-kill**, tray'den Stop da; **hata derlemeyi öldürmez.**

**Spike şartı (D1):** build job İÇİNDE tamamlanır + breakaway flag GEREKMEZ (T23 probe). **Kabul (deterministik, sleep yok):** X→tray, build devam; Exit/kill/crash → **≤2sn** artık process yok (handle/IOCP); Stop → graceful ya da hard-kill, ortak bin'de torn DLL yok. `[D8]`

## A4. Çıktı Dizini Gerçeği (KRİTİK — v5'ten değişmedi)

- Çıktı = projelerin KENDİ post-build copy event'leri; orchestrator kopyalamaz. Ortak dizine dokunulmaz/okunmaz. **VS-parity zorunlu.** `[§4]`
- Final çıktı branch'e göre izole edilmez (bilinçli). **Config tek klasör (config-agnostic):** config değişimi tüm projeleri dirty yapar. `[A6]`
- Yalnız ara çıktı (obj) worktree build'lerinde izole; proje **Id (tam yol)** ile çakışma önlenir.
- build-state GLOBAL. **Worktree çıktı-izolasyonu YOKTUR (D12):** worktree = ana checkout'u bozmadan farklı branch **kaynağını** derle; çıktı ortak havuza yazar (kasıtlı). Concurrent-VS guard (T29) + tek-run kilidi korunur.

## A5. Sync — Proje Keşfi & Bağımlılık Grafiği

- Kökte `*.sln` + `*.csproj` recursive tara (ignore listesi). 45 sln kökü.
- **Graf primer = HintPath-basename→producer (D11):** evaluated AssemblyName/TargetName'den DLL-adı→üretici haritası; HintPath raw-reference'lar bu harita ile kenara çevrilir. **ProjectReference ikincil.**
- **Batch MSBuild evaluation (D5):** AssemblyName + ProjectReference + Compile item'ları tek geçişte; **mtime+hash cache** ile invalidation.
- **file→project = MSBuild-evaluated Compile item'larından (D11), path-prefix DEĞİL.**
- Tarjan SCC (cycle) + Kahn (topo). Atomik `dependency-graph.json` cache. Açılışta cache'ten; tam analiz yalnız Sync.
- **Bağımlılık sağlık göstergesi → cycle rozetine indirgendi `[v6Δ-3]`:** cycle = **kırmızı rozet + tooltip** (istisna işareti). Cycle-dışı projelerde **ayrı yeşil "sağlık noktası" YOK**; o görsel yuva artık **will-build noktasına** (A7) ayrıldı. (v5 N7'nin "cycle'sız=yeşil nokta" kısmı düştü; "cycle=kırmızı rozet" kısmı korundu.)
- **Liste sırası = build order:** topo sıraya göre. **Katman pattern varsa** katmanlara göre gruplanır (her katman kendi içinde topo). Sticky ara başlıklar = **katman adları** (UI). `[N8]`
- **Solution belirsizliği (OV#6):** csproj 0/>1 sln; `solutionNames` çok-değerli; "VS'de Aç" >1 ise seçtir. `[T32]`
- **Graf verisi App'e (`[v6Δ-2]`):** Sync sonrası Core, node (proje) + kenar (bağımlılık) + katman + build-order + cycle bilgisini **zaten** `ProjectNode`/graf DTO'da üretir; App dependency graf panelini bu veriyle çizer (ek Core sorumluluğu yok, yalnız var olan veri tüketilir).

## A6. Derleme Stratejisi

**Rebuild:** tüm projeler topo sıraya göre; bağımsızlar paralel `MSBuild.exe`.

**Build (incremental) — kalp:** bir proje **yalnız** şu hallerde derlenir: (1) güncel commit ≠ son başarılı commit ve projeyi etkiliyor, (2) working-tree'de projeyi etkileyen local değişiklik, (3) upstream producer imzası değişti (GLOBAL propagation), (4) hiç başarıyla derlenmemiş. Aksi halde **Skipped**.

- DLL/bin timestamp **asla** okunmaz. Dosya→proje: MSBuild-evaluated Compile item + build-etkileyen uzantı → dirty. Üst `Directory.Build.props/targets` → kapsam dirty.
- **Downstream propagation GLOBAL graf üzerinden (T25):** değişen L1, GLOBAL graf'taki L3 bağımlılarını dirty yapar. **Safe (varsayılan)** = dirty + transitif; **Fast** = sadece dirty.
- **Signature (D6):** tek Core `BuildSignature.Compute`. **build-state.json (D2):** projectId anahtarlı GLOBAL; single-writer + atomik; her proje bitiminde persist.
- **Config değişimi (Debug↔Release) → TÜM projeler dirty** (A4); loglanır. **Debug/Release seçimi ana action-bar'da (`[v6Δ-5d]`):** segment toggle; değiştirilince "config değişti, tümü derlenecek" mini-uyarı gösterilir (sürpriz full-rebuild engellenir).
- **Pre-run will-build kümesi (`[v6Δ-3]`):** Sync + build-state'ten Core, **run'dan ÖNCE** her proje için `willBuild:bool?` türetir (dirty ⇒ true, güncel ⇒ false, imza hesaplanamadıysa null). Bu, `BuildPlan`'ın skip-kararlarıyla aynı mantık (ayrı kod yolu değil); UI'daki will-build noktasını besler. Cycle projeleri `willBuild=false` + cycle rozeti.

**Paralellik & kaynak:** bağımsızlar eşzamanlı; **Performans modu (Full/Balanced/Light)** = paralel derece + priority + inner Job CPU rate cap (Light≈%40/Balanced≈%70/Full=sınırsız). Çalışırken değiştirilebilir. `[§6]`

**Sıra-koruyan paralel scheduler (deterministik):** ready set'ten slot boşalınca build-order'da en önde gelen seçilir (rastgele/hash YOK). Aynı graf+derece → aynı dispatch. `[§6]`

**Katman pattern — layered build (N8):** ayarlarda sıralı sınırsız **regex + isim**; regex proje ADINA; ilk eşleşen kazanır; **isim = UI sticky başlık.** Sert faz bariyeri (Katman N bitmeden N+1 başlamaz). Katman-içi topo+paralel dispatch, ama **incremental dirty-propagation GLOBAL graf** üzerinden (katman yalnız dispatch sırası). Eşleşmeyenler → implicit "Diğerleri" katmanı. Pattern yoksa tek global graf. **Ters katman bağımlılığı (3C):** hafif tespit + uyarı (bloklamaz). `[T15]`

**Branch & worktree — BRANCH-DRIVEN MODEL `[v6Δ-1]` (v5 A6 worktree bölümünü değiştirir):**
- Açılışta aktif branch seçili. **Branch seçimi = niyet**; Build'e basılana kadar git'te işlem yok. **Branch chip aranabilir (`[v6Δ-5c]`)** (çok branch: arama kutusu + çift-Shift kısayolu).
- **Ayrı "local dahil/hariç" toggle YOK.** Local-inclusion, worktree/branch seçiminin **türevi**dir ve etiket olarak gösterilir.
- **Davranış matrisi (3 durum — v5'in 5-satırı yerine):**

  | Seçili branch | Worktree | Derleme kaynağı | Local | Aktif branch | Etiket |
  |---|---|---|---|---|---|
  | = aktif | **OFF** (default) | in-place | **dahil** | değişmez | "yerel dahil" |
  | = aktif | **ON** | worktree, committed HEAD | hariç | değişmez | "committed temiz · \<ad\>" |
  | ≠ aktif | **ZORUNLU ON** | worktree, committed HEAD | hariç | **hiç değişmez** | "committed \<branch\> · \<ad\>" |

- **Kaldırılanlar (bu model sayesinde):**
  - v5'teki `runBlocked` (OFF + ≠aktif + local ezme) durumu → **YOK** (farklı branch hep worktree; ana working-tree hiç dokunulmaz).
  - v5/DD14'teki **in-place branch-switch pre-build confirm** → **YOK** (uygulama aktif branch'i asla checkout etmez). *(DD14'ün sync-reveal + success-flourish kısmı KORUNUR; yalnız branch-switch confirm düştü.)*
- **Worktree isimlendirme/seçim:** ON (aynı branch) veya zorunlu (farklı branch) durumunda: otomatik standart isim (`<branch>-<n>`), düzenlenebilir; **mevcut worktree'lerden seçilebilir**; per-worktree **Sil** (`git worktree remove`). `[N3]`
- **Worktree silme + branch guard (N3):** havuz kalıcı; branch silinmeye çalışılırken worktree tutuyorsa uyarı + "önce worktree'yi sil".
- **Worktree pool ölçek (T14):** per-worktree disk UI'da + configurable cap / LRU prune.
- **Git komut sonuçları kontrol edilir** (silent-failure fix); hata → `error` event.

**Eşzamanlılık:** orchestrator tek seferde tek run; OutDir kendi-kendine çakışmayı önler.

## A7. UI / UX (tek pencere — OTORİTE: DD1–DD14 + `[v6Δ]`)

**North-star (DD4):** sakin-hassas dark + heyecanlı frontier. Heyecanın kaynağı = dependency-order build-frontier'ın **grafta ve listede** aşağı akması. `[v6Δ-2]`

**Pencere kabuğu:** custom dark title bar (`WindowChrome`); repo·branch başlıkta. `X` → tray'e küçülür. App icon taskbar+tray+pencerede.

**Reconciled Information Architecture (OTORİTE — `[v6Δ-2/Δ3/Δ5]` ile güncellendi):**
```
┌───────────────────────────────────────────────────────────────────────┐
│ [◆Delta] OSYS · main                              — □ ×   (dark chrome) │ chrome
├───────────────────────────────────────────────────────────────────────┤
│ ▸ Building 8/120 · 1m04s · ~40s kaldı   [Client.Core][Server.Api]…     │ ① sticky set + GLOBAL progress
├──────────────────────────────┬────────────────────────────────────────┤
│ ② DEPENDENCY GRAF (sol-üst)  │ ③ ANA CONSOLE (sağ-üst)                 │
│   yerleşik DAG; node=proje,   │   seçim yok: run narrative/granular      │
│   canlı statü rengi; frontier │   kart seçili: tam MSBuild log + [←Back] │
│   grafta amber dalga; node    │                                          │
│   odak/komşu-vurgu            │                                          │
│ ══════(yatay splitter)═══════ │═════════(yatay splitter)════════════════ │
│ ④ PROJE LİSTESİ (sol-alt)    │ ⑤ ÖZET STREAM (sağ-alt · kalıcı)        │
│  ●Core ⟳building 1.1s         │  ✓ Client.Core built · 2.3s             │
│  ●Api  ✓2.3s   ○Utils ↷skip   │  ▌ Server.Api building…█ (hover/seç-toggle)│
│  (●amber=derlenecek ○gri=güncel)│                                        │
├──────────────────────────────┴────────────────────────────────────────┤
│ ⟳Sync Σ120 ●98 ✓96 ✗2 ↷22  main▾(ara) ⌥worktree▾ [Debug|Release] ⚡Balanced Build▸│ aksiyon
└───────────────────────────────────────────────────────────────────────┘
  dikey splitter = sol/sağ kolon; her kolonda 1 yatay splitter; hepsi persist
```
**Attention order (DD5):** ① ne oluyor (frontier + global progress) → ② graf+liste (mekânsal) + özet stream (zamansal) → ③ per-project detay. Hiyerarşi renkle değil **ağırlıkla**. Default kolon split ~%46/%54; sol-kolon graf/liste ~%40/%60 (persist).

**② Dependency graf paneli (`[v6Δ-2]` — YENİ):**
- **Layout:** gerçek DAG; node=proje, ince kenar=bağımlılık; **build-order/katman düzeninde YERLEŞİK** (kalıcı rotasyon/globe YOK). Katmanlar görsel gruplar (sütun/kuşak).
- **Canlı statü:** node rengi = statü (building=amber pulse, ✓yeşil, ✗kırmızı, ↷dim/gri, queued nötr). Frontier = grafın içinde akan amber dalga — **listedeki frontier ile AYNI tek hero motion** (senkron; motion-budget bozulmaz).
- **Odak:** listeden/graftan node seç → o node ortalanır (auto-pan center-of-gravity) + amber halka + komşu kenarlar belirir, gerisi hafif dimlenir. Seçim liste+console+graf **senkron**.
- **Reduced-motion:** statik layout + yalnız renk güncellemesi (DD3).
- **Perf:** yalnız RenderTransform+Opacity; görünmeyen node cull; çok büyük graf (500+) için katman-agregeli sadeleşme (T51). Core saf; render App.

**③/⑤ Sağ kolon:** üst = ANA CONSOLE (seçim yok → narrative/granular, idle: blink+"ready"; kart seçili → tam log + [←Back]). alt = KALICI ÖZET STREAM (kronolojik, her zaman görünür; en-yeni = aktif typing satırı). Yatay splitter + min-height.

**Console temizleme + granular adım logu (N1):** Sync/Build/Rebuild'e basınca console temizlenir + adımlar baştan. Granular: `Solution'lar taranıyor (N)`, `HintPath/Compile okunuyor`, `Graf/cycle`, `Sıra belirlendi (N)`, `Katman 1 (Types) — M proje`. Önceki run terminator satırıyla kapanır.

**Özet stream satırları (`[v6Δ-5a]`):** her proje tek satır + süre (`✓ Client.Core — 2.3s`, `✗ Server.Api — failed (1.1s)`, `↷ Common.Utils — skipped (no source change)`); en altta `Done` + TOPLAM. **Satır etkileşimi kartla birebir:** hover arka planı hem seçili hem seçili-olmayan satırda görünür (affordance); seçili durumda satır accent'i kalınlaşır/koyulaşır (kart seçim stiliyle aynı); **tıkla → proje seç + detay + [←Back] + satır SEÇİLİ görünür**; **tekrar tıkla → seçim kalkar + Back kaybolur + ana ekrana döner** (anlık). İnsan-gibi yazım.

**Typing / live-line degradation (DD2 — değişmedi):** drop-to-latest (kuyruk yok); throughput-suspend (>3-4 satır/sn → anlık, ~400ms sessizlikte döner); hatalar typing'i atlar (anlık); hız cap ~250ms; imleç her zaman blink; ham MSBuild asla harf-harf.

**Saklama mimarisi (4A/D4):** her projenin tam çıktısı per-run diske; kart seçilince App diskten chunk'lı + canlı event'lerle interleave ister (`getProjectLog`). Hâlâ-derlenen → live-stream + "still going".

**Kart seçim modeli (`[v6Δ-5b]`):** tıkla → sol accent şeridi kalınlaşır + yazılar bir tık içe kayar (kutu/border YOK; anlık+hızlı) + grafta node odak. Tek seçim. **Tekrar tıkla → seçim kalkar + [←Back] kaybolur + ana ekrana döner** (v5'te "bonus"tu, artık **canonical**). Deselect yolları: birincil = tekrar-tıkla (canonical); ikincil = Esc (keyboard nav) + her zaman görünür [←Back]. **Console-tıkla-deselect YOK** (metin seçimi kutsal). Efekt yalnız seçili tek kartta (virtualization perf korunur).

**Tek canonical click→detay (DD8 + `[v6Δ-5a]`):** özet stream'de **HER satır** düz-tıkla = proje seç/deselect toggle. Kart ve stream-satırı ve graf-node **aynı seçim modelini** paylaşır. Ham console'da metin seçimi kutsal (tıkla=seçim kalkar YOK); çıkış = [←Back] (canonical) + seçili karta/satıra tekrar tıkla.

**Durumlar (renk + statü):** Discovered, Queued, Building, Succeeded, Failed, Skipped, CycleDetected(+rozet). Statü = renk + metin/rozet + **ince-halka daire-içi glyph** (`[v6Δ-5f]`).

**Kartlar (`[v6Δ-3/Δ5e/Δ5f]`):** proje + solution adı; **sol accent şerit = build statüsü**; **sol accent şeridin hemen sağında (ad'dan önce) WILL-BUILD NOKTASI (ORTOGONAL, ~8px, salt-okunur):** amber dolu = dirty/derlenecek, gri = güncel/atlanacak, hollow (yalnız ince halka) = Sync öncesi bilinmiyor (A6 `willBuild`). **Cycle → kırmızı rozet** (ayrı istisna işareti; eski yeşil sağlık noktası kaldırıldı). Sağ altta **estetik** "Dosyada Aç" / "Visual Studio'da Aç" ikonları (ince çizgili, folder-open + external-window tarzı; **hover'da belirir**, sadelik korunur; solution >1 ise seçtir — T32). **Commit gösterimi (N10):** "şu an `<builtCommit>` → hedef `<targetCommit>`". Kart = dense liste satırı.

**Build frontier:** liste build-order sıralı; Building kartlar canlı (pulse+shimmer). Sticky "şu an derleniyor (N)" şeridi = statik metin günceller (animasyon değil — DD7); çip→karta/node'a git. Auto-scroll center-of-gravity yumuşak (yo-yo yasak — T48). **Graf frontier senkron (`[v6Δ-2]`).**

**Motion budget (DD9):** aynı anda en fazla **1 hero motion** (graf+liste frontier'ı AYNI hero); typing burst'te susar; sticky şerit statik. Yalnız viewport'taki node/kartlar anime; settled statik; kart/node başına tek motion tipi; yalnız RenderTransform+Opacity. Liste UI virtualization; graf cull. 500–1000'de akıcı.

**OS reduced-motion (DD3):** Windows animasyon kapalıysa typing→anlık, pulse/shimmer/shake/stagger + graf frontier→anlık renk/fade. Uygulama-içi toggle YOK.

**Global progress / ETA (DD6):** "Building 8/120 · 1m04s · ~40s kaldı".

**Interaction state'leri (DD10/Pass2):** pre-first-run ("repo seç" + [Klasör Seç], graf boş placeholder); 0-proje; 0-branch/git-fail (inline retry); **all-skipped = DELIGHT** ("Her şey güncel — 120 proje 0.4sn'de kontrol edildi", tüm will-build noktaları gri); partial (Done hata-önce); sync skeleton; engine-died banner. Empty state'ler feature.

**Failure orchestration (DD11 + `[v6Δ-4]`):** hata anında stream **anında** anons (typing atlanır); run boyunca scroll-proof kapatılabilir banner: **"✗ N hata — Server.Api, Web.Portal [Failed'a git]"**. **[Failed'a git] butonu** → action-bar'daki Failed filtre çipini uygular (otomatik değil; kullanıcı tetikler — istemeden filtrelenmez). Banner'daki proje **ismine** veya özet stream'deki başarısız **satıra** tıklama → o projenin **kendi logu** (DD8 canonical). Her hatanın logu bağımsız erişilir. **Birleşik hata görünümü / çoklu-seçim YOK** — sınırsız hataya ölçeklenir. "✗ Failed" filtre çipi öne. Shake yalnız ikincil ipucu.

**Sync reveal + success flourish (DD14, reduce-motion aware):** kartlar build-order'da staggered fade-in (≤400ms) + graf katman düzeninde belirir; temiz full-success'te Done'da tek settle/glow + frontier sakin-yeşil (bir kez). *(DD14'ün in-place branch-switch confirm kısmı `[v6Δ-1]` ile kaldırıldı.)*

**Worktree chip iki sinyal (DD13 + `[v6Δ-1]`):** toggle (aktif branch'te ON/OFF; farklı branch'te zorunlu-ON kilitli/gizli) + worktree seçici (oto-isim/mevcut/Sil); aktif mod etiketi ("yerel dahil"/"committed temiz"/"committed \<branch\>") **Build yanında glanceable**. **X-to-tray ilk toast (DD12).** **Debug/Release segment toggle** ayrıca action-bar'da (`[v6Δ-5d]`).

**Kısayollar & global hotkey (N6):** **çift-Shift** → branch **aranabilir** hızlı seçim (`[v6Δ-5c]`); **Ctrl+P** → proje/kök seçici; **Ctrl+B/Ctrl+R** → Build/Rebuild (çalışıyorsa Stop); **global hotkey** (Alt+B, ayarlanabilir) → tray'deyken pencereyi sağ-alt köşeden animasyonla çıkar. **Keyboard nav:** liste ok tuşları, Enter=log, Esc=back/deselect, focus-visible ring.

**Anti-slop (Pass4 + `[v6Δ-5f]`):** glyph ≠ emoji; UI = gerçek grotesk (Geist), console = gerçek monospace (Geist Mono); accent statü kodlar; restrained radius (console 0); dekoratif gölge yok; kart dense row. **Statü glyph'leri: ince-halka daire-içi (tik/tire/çarpı) SERBEST/teşvik.** **Yasak yeniden yazıldı:** dekoratif **dolu renkli daire-rozet** kalabalığı yasak; **amaçlı ince-halka statü glyph'i serbest.** **Dönen dekoratif globe yasak** (graf yerleşik/amaçlı).

**Auto-scroll arbitration (T48):** user-scroll yalnız o bölgeyi duraklatır (~2sn idle→devam); öncelik frontier > console > stream; frontier/graf-pan center-of-gravity net.

**Tasarım niyeti (N4):** plan brief sağlar; kesin görsel Delta design-system + Claude Design'da. Token-intent → `delta-design-system-v1.md`.

## A8. Test Stratejisi

- **Unit (Core):** graph extraction (HintPath→producer + match-rate), topo, cycle, file→proje (Compile items), **BuildSignature determinism (D6)**, incremental kararı, branch-bounce, **GLOBAL propagation (T25)**, Safe/Fast, scanner ignore, **sıra-koruyan scheduler**, **layer assignment (first-match + "Diğerleri")**, config-switch all-dirty, **pre-run will-build kümesi (`[v6Δ-3]`: dirty⇒true, güncel⇒false, imza-yok⇒null; BuildPlan skip-kararıyla tutarlı)**.
- **Process-control (ZORUNLU, deterministik — D8):** tray Exit/App kill/crash/Stop → **≤2sn** artık process yok; X→tray küçülür; kill mid-parallel-build → torn DLL yok + leftover yok; build job İÇİNDE + no-breakaway (D1).
- **State/IPC:** build-state atomik/tek-yazar + crash-mid-write; getProjectLog chunk interleave; stdout-IPC desync; IPC framing/max-line.
- **Integration:** çoklu-solution → Sync, Build, Rebuild, **branch switch (branch-driven worktree matris `[v6Δ-1]`)**, Stop, kart/stream/graf seçimi → detay log.
- **Perf:** 500+ kart akıcı; cold Sync + cache-hit (D5); paralel kazanç; CPU cap; log akışında UI bloklanmaz; **dependency graf 500–1000 node akıcı render + cull (`[v6Δ-2]` · T51)**.

## A9. Supervisor ↔ UI Sözleşmesi

- **Komutlar:** `syncWorkspace(rootPath)`, `reanalyze()`, `listBranches()`, `listWorktrees()`, `selectBranch(branch)`, `startRun(mode, branch, useWorktree, worktreeName?, config, dependentMode, perfMode)`, `setPerfMode(perfMode)`, `stopRun(runId)`, `getProjectLog(projectId)`, `deleteWorktree(name)`, `openPath(projectId)`, `openInVS(projectId)`.
- **Eventler:** `syncProgress`, `syncCompleted`, `worktreesListed`, `runStarted`, `projectStarted`, `projectLog`, `projectSucceeded`(+durationMs), `projectFailed`(+durationMs), `projectSkipped`(+reason), `runCompleted`(+totals), `runCancelled`, `error`, `runProgress`(X/N+ETA). **`runBlocked` KALDIRILDI `[v6Δ-1]`** (branch-driven modelde in-place-over-local durumu yok; genel guard'lar `error` ile). Yeni: **`buildPreview`** (opsiyonel — `syncCompleted` ile ya da ayrı; her projeye `willBuild:bool?` — will-build noktasını besler `[v6Δ-3]`).
- **Tipler (`[v6Δ]` ile):**
  - `ProjectNode { id, name, projectPath, solutionNames[], dependencies[], buildOrder, layerIndex?, layerName?, inCycle:bool, willBuild?:bool }` — `inCycle` (v5 `healthy` yerine; cycle rozeti) + `willBuild?` (will-build noktası `[v6Δ-3]`; Core **Sync sonrası** dirty/güncel/imza-yok durumundan türetir — BuildPlan skip-mantığının pre-run önizlemesi, ayrı kod yolu değil; T53 unit test: dirty⇒true, güncel⇒false, imza-hesaplanamadı⇒null).
  - `BuildState { projectId, builtSignature, builtCommit?, lastResult, lastRunAt, lastBranch? }` — GLOBAL. Global tekil `OutDirConfig`.
  - `Worktree { name, branch, path, isActive, diskSizeBytes? }`.
  - `LayerPattern { order:int, regex, name }`.
  - `BuildPlan { ... }` — **Core üretir**; skip/dirty kararları (will-build ile aynı mantık).
  - `RunRequest { mode, branch, useWorktree:bool, worktreeName?, config:'Debug'|'Release', dependentMode, perfMode }` — **`useWorktree` App'te branch-driven türetilir `[v6Δ-1]`** (branch≠aktif ⇒ zorunlu true); kontrat alanı kalır.
  - `ProjectResult { projectId, result, durationMs, reason?, builtCommit?, targetCommit? }`.
- **Disiplin (D3/D4):** planlama Core'da; stdout yalnız NDJSON; `skipped` gerçek reason.

## A10. Yapılandırma

Kök dizin · **Build config (Debug varsayılan / Release; ana action-bar segment toggle `[v6Δ-5d]`; config-agnostic → değiştirilince Sync/Build öncesi "Config değişti, tümü derlenecek" mini-uyarısı gösterilir; sürpriz full-rebuild engellenir)** · Perf modu (Full/Balanced/Light; ana UI chip) · **Worktree (branch-driven `[v6Δ-1]`: aktif branch toggle default OFF, farklı branch zorunlu; havuz kalıcı, per-worktree Sil + cap/LRU — T14)** · Downstream modu (Safe/Fast) · **Katman pattern editörü** (sıralı sınırsız regex + **isim**; ekle/sil/sırala; boş→global) · **Kısayollar** (çift-Shift **aranabilir** branch/Ctrl+P/Ctrl+B-R/global hotkey) · Cache konumu · **Görsel kimlik** (Delta logo+icon, dark title bar; token'lar `delta-design-system-v1.md`) · **Graf paneli** (sol-üst; splitter ile gizlenebilir `[v6Δ-2]`) · **KALDIRILANLAR:** LogLevel, in-app Reduced Motion (OS ayarı — DD3), **ayrı local-dahil toggle (`[v6Δ-1]`)**.

## A11. Kapsam Sınırları (v1)

**İçinde:** tek repo · MSBuild.exe+nuget shell-out · nested-Job (+CPU cap, X→küçül/Exit→cascade) · sync/graph/cache (HintPath→producer, build-order liste, cycle rozeti) · rebuild + incremental (GLOBAL build-state, N10 commit, GLOBAL propagation, **pre-run will-build `[v6Δ-3]`**) · sıra-koruyan scheduler · katman pattern (regex+isim) · **branch-driven worktree `[v6Δ-1]`** (toggle/zorunlu + sil & branch guard + pool cap) · **dependency graf paneli `[v6Δ-2]`** (yerleşik DAG, canlı frontier, node odak, reduced-motion, cull) · tam UI/UX (2-kolon×2-satır console+graf+liste+stream, typing degradation, kart/stream/graf senkron seçim + deselect, build frontier senkron, chip selector, **aranabilir branch `[v6Δ-5c]`**, **Debug/Release toggle `[v6Δ-5d]`**, will-build noktası, kısayollar+hotkey, dark title bar, logo/icon, motion budget, interaction states, all-skipped delight, global progress/ETA, **failure→Failed-filter `[v6Δ-4]`**, keyboard nav, SR/kontrast, estetik open-icon + halka glyph `[v6Δ-5e/f]`) · config · tray/autostart/single-instance · perf modları · unit+process+integration test · README · `dotnet publish`.

> **Netlik (`[v6Δ-2]`):** Dependency graf paneli (yerleşik DAG + canlı frontier senkron + node odak) **v1 IN-SCOPE**'tur (Part B T50/T51, Part C It-4/It-5). Kapsam-dışı olan **yalnızca** dönen/dekoratif globe animasyonu ve graf manuel düzenleme/re-layout'tur.

**Dışında (sonraya, gerekçeli):** Multi-repo · MSIX/installer/auto-update · WinUI Composition · **dönen/dekoratif globe graf animasyonu (`[v6Δ-2]`: yalnız yerleşik/amaçlı DAG in-scope)** · özel CPU % slider · Headless/CLI · eski-kod bug araştırması · CLAUDE.md çoklu-dosya + agent senkron · katman gelişmiş çözüm · packages.config→PackageReference migration · worktree gerçek output izolasyonu (D12) · node reuse/shared compilation v1'de açmak (T33) · komut paleti / fuzzy search · onboarding tour · light mode · uygulama-içi motion/tema toggle · **graf'a manuel düzenleme / re-layout / edge-editing (yalnız görüntüleme).**

## A12. Varsayımlar / Varsayılanlar

Tek git repo · ortak çıktı projelerin post-build event'leriyle dolar, config-agnostic, imza=config+commit+local-diff+upstream, build-state GLOBAL · kullanıcı VS'de eşzamanlı derlemez · varsayılanlar: **Debug (action-bar'dan değiştirilebilir `[v6Δ-5d]`)**, Safe, **worktree branch-driven (aktif branch OFF / farklı branch zorunlu ON `[v6Δ-1]`)**, Full Power, console=özet stream + idle console, **graf paneli açık (splitter ile gizlenebilir `[v6Δ-2]`)**, OS reduced-motion'a saygı · graf cache'ten, tam analiz yalnız Sync · katman pattern tek-yönlü varsayılır · `X`→tray, kapanış yalnız Exit · araç .NET 10; derlenen projeler MSBuild.exe + kullanıcı VS toolchain'i · **uygulama aktif branch'i ASLA checkout etmez (`[v6Δ-1]`)** · trust boundary: root VS-açılmış kadar güvenilir (T17).

---

# PART B — Birleşik Task Backlog (T1–T51)

> v5'in T1–T49'u korundu (izlenebilirlik); **T50–T51 eklendi `[v6Δ-2]`**. Absorbe olanlar (T1,T2,T3,T18,T19,T21) hâlâ başka task içinde. Değişen task açıklamaları `[v6Δ]` etiketli.

| ID | Başlık (kısa) | Durum | İt. | Kaynak/iz |
|---|---|---|---|---|
| **T23** | Iteration -1 Feasibility Spike (GATE) — MSBuild.exe+nuget derle, HintPath match-rate, cascade-kill ≤2s + breakaway, D9 flag delta | AKTİF (gate) | **-1** | Eng/D1,D13 · T1+T2 absorbe |
| **T22** | Engine: MSBuild.exe (vswhere) + nuget restore; nested Job+shell-out+cascade | AKTİF | 0/2 | D10 |
| **T30** | Tek `ProcessRunner` (exit-code+stderr+timeout); non-zero=projectFailed | AKTİF | 0 | D7 |
| **T7** | IPC framing: length-prefixed/escaped + max-line NDJSON | AKTİF | 0 | CEO · T28 genişletir |
| **T28** | getProjectLog chunk+interleave; stdout NDJSON-only | AKTİF | 0/2 | D4 |
| **T6** | Supervisor crash recovery: App handle izler → error+restart | AKTİF | 0 | CEO |
| **T31** | Process-control testleri deterministik (handle/IOCP), sleep yok | AKTİF | 0 | D8 |
| **T24** | Graph: HintPath→producer; batch tek-geçiş eval + mtime/hash cache; 45 sln; file→proje=Compile items | AKTİF | 1 | D11,D5 · T3+T19+T2 absorbe |
| **T32** | Solution belirsizliği: csproj 0/>1 sln + Open-in-VS seçimi | AKTİF | 1 | OV#6 |
| **T26** | Planning Core'da: `BuildPlan` DTO; Supervisor yürütür | AKTİF | 1 | D3 |
| **T25** | Signature+propagation: tek Core BuildSignature; transitive upstream; skip GLOBAL gate | AKTİF | 3 | D6,D11 · T18 pekiştirir |
| **T27** | build-state single-writer + atomik + per-project persist + crash test | AKTİF | 3 | D2 |
| **T4** | Stop: copy-aware graceful; hard-kill yalnız proje sınırı | AKTİF | 0/2 | CEO/2A |
| **T5** | Logs: per-run disk project log + decision log; seçince diskten stream | AKTİF | 2 | CEO/4A |
| **T8** | Parallel copy retry-on-sharing-violation + backoff | AKTİF | 2 | CEO |
| **T9** | Test: kill mid-parallel-build → torn DLL yok + leftover yok | AKTİF | 2 | CEO |
| **T29** | **`[v6Δ-1]` Branch-driven worktree**: 3-durum matris (aktif OFF/ON, farklı zorunlu); ayrı local-toggle YOK; `runBlocked` + in-place branch-switch confirm KALDIRILDI; wording (kaynak-izole/çıktı-ortak) + branch guard + tek-run kilidi + oto-isim/seç/Sil; farklı branch seçimi → worktree ANINDA zorunlu-ON (onay adımı yok, local kaybı riski yok); aktif branch seçimi → toggle OFF/ON serbest | AKTİF (revize) | 3 | Eng/D12 · **v6Δ-1** · T21 absorbe |
| **T11** | Edge input: detached HEAD / no-commits / shallow → treat-as-dirty + warn | AKTİF | 3 | CEO |
| **T13** | Path sanitization: worktree + branch | AKTİF | 3 | CEO |
| **T14** | Worktree pool: per-worktree disk + cap / LRU prune | AKTİF | 3 | OV#3 |
| **T15** | Layer reverse-dep detect+warn-only (3C) + compile-error semptomu | AKTİF | 3 | 3C |
| **T34** | Typing/live-line degradation engine | AKTİF | 4 | DD2 |
| **T35** | **`[v6Δ-2]` Console/graf/stream layout**: 2-kolon×2-satır (graf+liste sol; console+stream sağ) + 3 splitter/min-height/reflow | AKTİF (revize) | 4 | DD1 · **v6Δ-2** |
| **T36** | OS reduced-motion → anlık; in-app toggle yok | AKTİF | 4 | DD3 |
| **T37** | Interaction state'leri: pre-first-run, 0-proje, 0-branch, all-skipped DELIGHT, partial, sync skeleton, engine-died | AKTİF | 4 | Pass2,DD10 |
| **T38** | Global progress/ETA (X/N + geçen + kalan) | AKTİF | 4 | DD6 |
| **T39** | **`[v6Δ-4]` Failure orchestration**: anlık anons + scroll-proof "✗ N hata — proj1, proj2 [Failed'a git]" → **Failed FİLTRE** + her satır kendi logu (birleşik/çoklu-seçim YOK) + Failed çip öne | AKTİF (revize) | 4 | DD11 · **v6Δ-4** |
| **T40** | **`[v6Δ-5a/b/c]` Discoverability & seçim**: tek canonical click→detay (kart/stream/graf senkron), **stream satırı hover+seç+tekrar-tıkla-deselect**, **kart tekrar-tıkla→deselect+Back kaybolur+ana ekran**, görünür Back, **aranabilir branch chip**, worktree chip 2-sinyal (branch-driven), X-to-tray toast, çift-Shift tooltip | AKTİF (revize) | 4 | DD8,DD12,DD13 · **v6Δ-5a/b/c** |
| **T41** | Motion budget: 1 hero (**graf+liste frontier AYNI hero `[v6Δ-2]`**), viewport-only, settled statik | AKTİF | 4 | DD9,DD7 |
| **T42** | Sync reveal: build-order staggered fade-in ≤400ms (+graf katman düzeninde belirir `[v6Δ-2]`) | AKTİF | 4 | DD14 |
| **T43** | Pre-build context confirm — **`[v6Δ-1]` in-place branch-switch confirm KALKTI** (branch-driven'da aktif branch değişmez). Kalan pre-build bağlam mesajları (ör. config-değişti-tümü-dirty `[v6Δ-5d]`) korunur | AKTİF (revize) | 4 | DD14 · **v6Δ-1** |
| **T45** | **`[v6Δ-5e/f]` Anti-slop**: glyph≠emoji, grotesk+mono, restrained radius/no-shadow, accent kodlar, dense row + **ince-halka daire-içi statü glyph SERBEST**, **estetik open-icon (folder-open/external-window, hover-reveal)**, yasak reword (dolu renkli rozet yasak / halka glyph serbest / dönen globe yasak) | AKTİF (revize) | 4 | Pass4 · **v6Δ-5e/f** |
| **T46** | Keyboard nav: ok/Enter/Esc + focus-visible ring (Esc=back/deselect) | AKTİF | 4 | Pass6 |
| **T47** | Screen reader + kontrast (automation name+statü; stream live-region; dim ≥4.5:1) | AKTİF | 4 | Pass6 |
| **T48** | Auto-scroll arbitration + frontier center-of-gravity (**graf-pan dahil `[v6Δ-2]`**; yo-yo yasak) | AKTİF | 4 | belirsizlik#5,#6 |
| **T10** | Empty/error UI state | AKTİF | 4 | CEO (T37 örtüşür) |
| **T12** | Mid-run lock: Building'de branch/config/worktree/**Debug-Release `[v6Δ-5d]`** selector kilidi | AKTİF | 4 | CEO |
| **T16** | Autostart temiz Idle açar; exe değişiminden önce tam exit | AKTİF | 4 | CEO |
| **T50** | **`[v6Δ-2]` Dependency graf paneli (YENİ)**: sol-üst yerleşik DAG (Core node/kenar/katman verisinden), canlı statü rengi, **frontier grafta akan amber dalga (liste ile senkron, AYNI hero)**, node odak/komşu-vurgu, listeden/graftan senkron seçim, reduced-motion statik, yalnız RenderTransform+Opacity | AKTİF (yeni) | 4 | **v6Δ-2** |
| **T51** | **`[v6Δ-2]` Graf perf (YENİ)**: 500–1000 node akıcı render + görünmez node cull + çok-büyük graf katman-agregeli sadeleşme; DrawingVisual/low-level gerekiyorsa; perf test (A8) | AKTİF (yeni) | 5 | **v6Δ-2** |
| **T53** | **`[v6Δ-3]` Will-build noktası (YENİ)**: Core pre-run `willBuild?` kümesi (BuildPlan skip-mantığıyla tutarlı) + kartta ortogonal nokta (amber dirty/gri clean/hollow unknown); cycle→kırmızı rozet (eski yeşil sağlık noktası kaldırıldı); unit test (dirty⇒true/güncel⇒false/imza-yok⇒null) | AKTİF (yeni) | 1/3 | **v6Δ-3** · N7 revize |
| **T20** | CPU-cap × copy/git/IPC etkileşimi; copy fazına rate floor | AKTİF | 5 | OV#2 |
| **T33** | D9 fast-follow: node reuse + shared compilation aç (spike kanıtlarsa) | AKTİF (koşullu) | 5 | D9 |
| **T44** | Success flourish: Done tek settle/glow + frontier sakin-yeşil | AKTİF | 5 | DD14 |
| **T49** | Token-intent → DESIGN.md/`delta-design-system-v1.md` (semantic renk/tipografi/spacing/radius/elevation/motion) | AKTİF | 4/5 | Pass5 |
| **T17** | Trust-boundary doc | AKTİF | 5 | CEO |

**Özet:** v5'in 43 aktif task'ı korundu (6 absorbe hâlâ yaşıyor); **T50/T51/T53 eklendi**; T29/T35/T39/T40/T43/T45/T12/T41/T42/T48 `[v6Δ]` revize. Hiçbir v5/v4.3 kararı silinmedi.

---

# PART C — İterasyon Yol Haritası

| It. | Teslim | Tasklar | Acceptance (bitti tanımı) |
|---|---|---|---|
| **-1** | **Feasibility Spike (GATE, throwaway)** | T23 | (a) 5 legacy proje MSBuild.exe+nuget green; (b) HintPath→producer match-rate + eşik; (c) cascade-kill gerçek MSBuild ağacı ≤2s, 0 orphan, no-breakaway; (d) D9 flag delta. **SPIKE-RESULTS.md** yazıldı. Herhangi biri fail → STOP. |
| **0** | İki process + stdio IPC + nested Job cascade + minimal pencere + DI iskeleti | T22(resolve), T30, T7, T28(base), T6, T31, T4(base) | §3 deterministik kabul geçer (X→tray; Exit/kill/crash → ≤2s). stdout yalnız NDJSON. ProcessRunner exit-code zorunlu. |
| **1** | Sync/graph: scan, HintPath→producer, Tarjan/Kahn, batch eval+cache, BuildPlan (Core), build-order kartlar + **cycle rozeti**; **pre-run will-build kümesi (Core) `[v6Δ-3]`** | T24, T32, T26, **T53(Core kısmı)** | Gerçek OSYS Sync cache-hit'te hızlı; kartlar build-order'da dolar; cycle kırmızı rozet; csproj 0/>1 sln doğru; planlama Core'da testli; **will-build kümesi testli (dirty/güncel/null)**. |
| **2** | Rebuild (gerçek, paralel): MSBuild.exe+nuget, sıra-koruyan scheduler, per-run disk log, özet log + kart seçince detay, copy-aware Stop, parallel-copy retry, hata izolasyonu, sayaçlar | T22(invoke), T28(stream), T5, T4(copy-aware), T8, T9 | OSYS rebuild paralel green; dispatch deterministik; kill mid-build → torn DLL yok + leftover yok; herhangi karta tıkla → tam log (diskten). |
| **3** | Incremental: commit/diff/status, GLOBAL build-state (atomik), GLOBAL propagation, Safe/Fast, **branch-driven worktree modeli (`[v6Δ-1]`: 3-durum matris, oto-isim, aktif-branch-değişmez)** + obj izolasyon, Skipped, kartta commit (N10), **will-build noktası UI `[v6Δ-3]`**, katman pattern (regex+isim/bariyer/dispatch/Diğerleri) | T25, T27, T11, T13, T14, **T29(v6Δ-1)**, T15, **T53(UI kısmı)** | Branch-bounce doğru rebuild; L1→L3 dirty (GLOBAL); config-switch all-dirty; **branch-driven worktree matris (aktif OFF/ON, farklı zorunlu; aktif branch hiç değişmez; runBlocked YOK)** + sil + branch guard testli; will-build noktası doğru (amber/gri/hollow); layer assignment testli; build-state crash-resilient. |
| **4** | **UX polish (design):** **2-kolon×2-satır layout (graf+liste sol, console+stream sağ, 3 splitter) `[v6Δ-2]`**, **dependency graf paneli (T50) `[v6Δ-2]`**, typing degradation, kart/stream/graf senkron seçim + **deselect canonical `[v6Δ-5a/b]`**, build frontier senkron + sticky şerit, chip selector + **aranabilir branch `[v6Δ-5c]`** + **Debug/Release toggle `[v6Δ-5d]`**, worktree liste+Sil+guard UI (branch-driven), kısayollar+hotkey, per-card/node animasyon (motion budget), **failure→Failed-filter `[v6Δ-4]`**, dark title bar + Delta logo/icon, tray, autostart, single-instance, config ekranı (+layer editör isimli), interaction states, global progress/ETA, keyboard nav, SR/kontrast, OS reduced-motion, **estetik open-icon + halka glyph `[v6Δ-5e/f]`** | T34–T48, T10, T12, T16, **T50**, T49(tohum) | Typing degradation spec'e uyar; **500–1000 kart + graf node akıcı (viewport-only motion, cull)**; **graf+liste frontier senkron (tek hero)**; **stream/kart/graf seçim + deselect çalışır**; all-skipped DELIGHT; reduce-motion anlık; keyboard-first; **failure→Failed-filter + per-row log**; X→tray toast; mid-run selector kilidi (config dahil). |
| **5** | **Perf + dağıtım + docs:** perf modları (Job CPU cap, canlı), 500–1000 perf doğrulama, **graf perf (T51) `[v6Δ-2]`**, README, `dotnet publish`, fast-follow + son cila | T20, T33(koşullu), T44, **T51**, T49, T17 | CPU cap tavanı tutar; copy fazı starve olmaz; **graf 500–1000 node akıcı + cull + agrega**; publish çalışır exe; README + trust-boundary; T33 spike kanıtladıysa flag kalkar; success flourish. |

**Paralelizasyon (worktree lanes):** Contracts (DTO/event + BuildPlan + `willBuild`/`inCycle`) önce sabitlenir → Lane A Core (pure) · Lane B Supervisor · Lane C App/UI (**graf render+animasyon Lane C**). Walking-skeleton dikey-dilim baskın.

---

# PART D — Iteration -1 Feasibility Spike (T23, GATE)

> **Throwaway/investigation** — production değil, kanıt üretir. Çıktı = `.claude/outputs/<ts>-spike-results.md`. **v6 deltaları (worktree/graf/UI) spike gate'lerini DEĞİŞTİRMEZ** — spike engine/graf/cascade feasibility'sini ölçer; UI/worktree kararları spike'tan bağımsız. Spike içeriği v5 Part D ile **birebir aynıdır**; kısaca:

- **S1 — MSBuild.exe + nuget resolve:** `vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"` → sürüm; nuget erişimi. **Pass:** yol + sürüm (full-MSBuild). Fail→STOP.
- **S2 — 5 temsilî legacy proje uçtan uca derle:** (i) yaprak v4.6, (ii) packages.config, (iii) çok HintPath, (iv) WPF, (v) post-build copy. `msbuild $proj -t:restore` + `-p:Configuration=Debug -p:UseSharedCompilation=false -nodeReuse:false -clp:Summary`. **Pass:** 5/5 exit 0 + çıktı DLL + post-build copy (VS-parity). Fail→STOP.
- **S3 — HintPath→producer match-rate:** 191 csproj batch eval (AssemblyName/TargetName + Reference/HintPath); producer map; intra-repo çözülme oranı. **Pass eşiği ≥%95** üreticiye çözülür. Partial→STOP değil ama It-1 öncesi fallback strateji plana eklenir.
- **S4 — Nested Job cascade-kill gerçek MSBuild ağacına:** outer Job (`KILL_ON_JOB_CLOSE`) → child suspended→assign→resume → OSYS derlemesi (MSBuild node + VBCSCompiler doğar); outer öldür → **≤2sn** 0 orphan (handle/wait sinyali, sleep-say değil); **breakaway probe (D1):** build Job İÇİNDE breakaway flag GEREKMEDEN tamamlanır mı (sdk#10150). **Pass:** ≤2sn 0 orphan VE no-breakaway. Fail→STOP.
- **S5 — D9 flag delta (gate değil, kayıt):** reuse kapalı vs açık wall-time delta; reuse açıkken S4 kill hâlâ ≤2sn mi. Reuse+kill korunuyor + anlamlı hız → T33 haklı.
- **S6 — Verdict + GATE:** `SPIKE-RESULTS.md` (S1–S5 PASS/FAIL/PARTIAL + sayılar). Tüm gate PASS → It-0 + detaylı TDD (writing-plans 2. tur). S2/S4 FAIL → It-0 başlamaz. S3 PARTIAL → It-0 başlar + It-1'e HintPath fallback task.

**Spike acceptance:** SPIKE-RESULTS.md mevcut + S2/S3/S4 net karara bağlı + D9 kaydı + spike kodu ana solution'a sızmadı.

---

## İzlenebilirlik & Self-Review

**v5 → v6 coverage:** Part A/B/C/D v5'ten korundu; **Δ1–Δ5** inline `[v6Δ]` işaretli. Değişen tasklar (T29/T35/T39/T40/T43/T45/T12/T41/T42/T48) revize edildi, **hiçbiri silinmedi**; **T50/T51/T53 eklendi**. Spike (Part D) değişmedi.

**Korunan kritik kararlar (ezilmedi):** MSBuild.exe (D10) · HintPath→producer (D11) · GLOBAL build-state/propagation (D2/T25) · nested Job cascade + ≤2s (§3/D1) · copy-aware stop (2A) · 2-kolon/2-zone console + typing degradation (DD1/DD2) · OS reduced-motion (DD3) · katman pattern (N8) · sıra-koruyan scheduler · motion budget (DD9) · all-skipped delight (DD10) · global progress/ETA (DD6) · tray X→küçül/Exit→cascade · kısayollar+hotkey (N6) · commit gösterimi (N10) · planlama Core'da (D3) · stdout NDJSON-only (D4).

**v6 yeni kararlar (onaylı):** branch-driven worktree (Δ1) · dependency graf paneli (Δ2) · will-build noktası (Δ3) · çoklu-hata→Failed-filter (Δ4) · fold-in cilalar (Δ5: stream/kart deselect, aranabilir branch, Debug/Release toggle, estetik open-icon, halka glyph).

---

## Execution Handoff

Plan kaydedildi: `.claude/outputs/2026-07-02-01-38-build-orchestrator-plan-v6-implementation.md`. Delta design-system + Claude Design promptu: `.claude/outputs/2026-07-02-01-38-delta-design-system-v1.md`.

**Sıradaki adım = Iteration -1 Feasibility Spike (Part D).** Bu gate geçmeden It-0 kodlanmaz. Spike sonrası It-0 için detaylı TDD planı (writing-plans 2. tur).

> Not: v5 dokunulmadı (donmuş kaynak). v6 ondan türetilmiş güncel uygulama kaynağıdır; v4.3 hâlâ donmuş arşiv/otorite.
