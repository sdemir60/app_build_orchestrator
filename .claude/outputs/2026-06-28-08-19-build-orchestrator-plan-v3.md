# Build Orchestrator — Yeni Plan (v3)

> Bu doküman, **v2 plan**'ın ([2026-06-27-22-46-build-orchestrator-yeni-plan.md](2026-06-27-22-46-build-orchestrator-yeni-plan.md)) devamıdır; orijinal 11 bölümlük prompt'tan ([2026-06-27-21-40-build-orchestrator-orijinal-prompt.md](2026-06-27-21-40-build-orchestrator-orijinal-prompt.md)) türetilmiştir.
>
> **v3'te değişen (bu session, kümülatif):**
> 1. **§7 animasyon/UX** — paralel derlemeye göre yeniden kurgulandı: tekil "aktif kart carousel / iOS-takvim / öne-gelir-döner" **kaldırıldı**; yerine **Hibrit: aktif bant (build frontier) + sticky "şu an derleniyor (N)" şeridi**.
> 2. **§4/§6/§9 build-state → GLOBAL** (projectId) — paylaşımlı OutDir'de "zaten derlendi mi" global gerçektir; per-branch keying'in branch-bounce'ta yanlış 'atla' bug'ı kapatıldı.
> 3. **§6 worktree → her zaman seçilebilir toggle** — aktif branch'ta bile committed-temiz derleme; karar tablosu + git'in Build anına ertelenmesi + otomatik worktree isimlendirme + aktif worktree listesi.
> 4. **§7 sağ-alt selector** — combo yerine ikon-buton+label chip'ler (branch / worktree+toggle / perf) + animasyonlu popup.
> 5. **§6/§11 perf** — Job Object **CPU rate cap** eklendi; perf modu ana UI'da hızlı seçici (Full/Balanced/Light), ayarlardan varsayılan, run-başı/canlı değiştirilebilir.
> 6. **§4 çıktı gerçeği netleşti** — ortak dizin, projelerin kendi **post-build `copy /y`** event'leriyle dolar (`c:\OSYS\Client\bin`, `...\Server\bin`); orchestrator kopyalamaz, sadece `dotnet build` çağırır (**VS-parity**). Ortak dizin **config-agnostic tek klasör** → build-signature'a **config** eklendi; **config değişimi tüm projeleri dirty** yapar.
>
> Diğer bölümler v2 ile aynıdır.

**Tarih:** 2026-06-28 08:19
**Karar süreci:** Brainstorming ile, kullanıcı onaylı. v2'nin tüm kararları geçerli; v3 yukarıdaki 5 maddeyi revize eder.

---

## 0. Temel Kararlar (bu planın belkemiği)

| Karar | Seçim | Gerekçe |
|---|---|---|
| **Derleme motoru** | **Shell-out** (`dotnet build` ayrı child process) | Job Object child'ları doğrudan yönetir → §6.1 "öksüz process imkânsız" garantisi doğal gelir. MSBuild assembly versiyon-matching derdi yok. Kullanıcının kurulu SDK'sını birebir kullanır. **VS'deki ayarlar + post-build event'ler birebir çalışır (§4 parity).** Kod basit. (Eski kodun #1 borcu in-process BuildManager'dan geliyordu.) |
| **Process topolojisi** | **UI + Supervisor (engine) process** | UI saf view kalır; engine ayrı process. İleride headless/CLI için mimari hazır. §6.1 nested Job Object ile deterministik. |
| **§6.1 garantisi** | **Nested Job Object** (managed watcher değil) | UI dış Job sahibi; Supervisor içinde doğar; Supervisor iç Job'da `dotnet build` child'larını tutar. UI ölünce kaskat ölüm. |
| **Teslim stratejisi** | **Walking skeleton / dikey dilim** | Her iterasyon uçtan uca çalışır+gösterilebilir. En kritik risk (process kontrol + IPC) en başta kanıtlanır. |
| **Hedef framework** | **.NET 10 (LTS) + WPF** | Güncel LTS, uzun ömür. Shell-out sayesinde derlenen projelerin TFM'inden bağımsız. |
| **v1 kapsam dışı** | **Multi-repo** | v1 tek repo. Mimari ileride genişletilebilir kurulur. |

---

## 1. Amaç

Yüzlerce C#/WPF projesinin (farklı solution'lara dağılmış, birbirine bağımlı, **tek git repo** altında) tek bir masaüstü uygulamadan; bağımlılık sırasına göre, paralel ve yalnızca değişenleri derleyerek hızlıca build edilmesini sağlayan, Windows'a özel bir uygulama.

Temel akış: **Sync → Branch seç → Build/Rebuild → Canlı çıktı.**

---

## 2. Mimari & Projeler

İki process; dört + test projesi:

| Proje | TFM | Sorumluluk |
|---|---|---|
| `BuildOrchestrator.Core` | net10.0 | Saf, test edilebilir çekirdek: project discovery, dependency graph, git servisi, incremental planner (DiffAnalyzer/IncrementalPlanner), state & config persistence. UI ve process kontrolünden **bağımsız**. |
| `BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor IPC sözleşmesi: command/event DTO'ları, enum'lar, polimorfik JSON serialization. |
| `BuildOrchestrator.Supervisor` | net10.0-windows (console) | Orchestration: build kuyruğu, **inner Job Object**, her projeyi `dotnet build` child process olarak çalıştırma, log parse, IPC server (stdio). Core'a referans verir. |
| `BuildOrchestrator.App` | net10.0-windows (WPF) | UI/MVVM (CommunityToolkit.Mvvm), tray, single-instance, autostart, **outer Job Object**, IPC client. Supervisor'ı spawn eder. |
| `BuildOrchestrator.Tests` | net10.0 (xUnit) | Core unit + process-control + integration testleri. |

**Mimari ilkeler:**
- App, Supervisor'ın assembly'sine referans vermez; sadece çıktısını yanına kopyalar ve runtime'da process olarak başlatır. İletişim tamamen IPC (Contracts) üzerinden.
- Core, UI/Supervisor'dan bağımsız test edilebilir; iş mantığı App/Supervisor'a sızdırılmaz.
- **DI** (`Microsoft.Extensions.DependencyInjection`) hem App hem Supervisor'da baştan kurulu → `GitService`/`BuildStateStore` gibi diske/git'e bağlı servisler mock'lanabilir. (Eski kodun test borcu kapanır.)
- IPC: **stdio newline-delimited JSON** (named pipe yerine — basit, orphan pipe yok).

---

## 3. Process Kontrolü & Güvenli Durdurma (§6.1 — KRİTİK, ZORUNLU)

**Problem:** Derleme alt process'ler doğurur (MSBuild node'ları, `VBCSCompiler.exe`, pre/post-build event'leri). Yanlış yönetimde uygulama kapansa bile bunlar öksüz kalıp CPU doldurabilir. Bu KESİNLİKLE önlenir.

### Nested Job Object topolojisi

```
App (UI)  ── outer Job [JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE]
   └─ Supervisor (CREATE_SUSPENDED → AssignProcessToJobObject → ResumeThread)
         └─ inner Job [JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE  (+ CPU rate control)]
               ├─ dotnet build #1  (CREATE_SUSPENDED → assign → resume)
               ├─ dotnet build #2
               └─ ...
```

### Zorunlu kurallar
1. **Outer Job (UI sahibi):** App açılışta outer Job'u `KILL_ON_JOB_CLOSE` ile kurar, Supervisor'ı `CREATE_SUSPENDED` ile doğurur, Job'a atar, sonra resume eder.
2. **Inner Job (Supervisor sahibi):** Supervisor kendi Job'unu kurar; her `dotnet build` child'ını `CREATE_SUSPENDED` → assign → resume sırasıyla başlatır (alt process'lerini doğurmadan önce Job içinde olması garanti).
3. **Cascade kill:** App herhangi bir sebeple (normal kapatma, crash, Task Manager kill) ölünce → outer Job son handle kapanır → Supervisor ölür → inner Job son handle kapanır → tüm `dotnet build` ağaçları ölür. **Managed parent-watcher YOK; deterministik.** (Eski kodun kırılgan `parent.Exited → Environment.Exit` deseni kullanılmaz.)
4. **Roslyn paylaşımlı derleyiciyi kapat:** her build `-p:UseSharedCompilation=false -nodeReuse:false` ile → arkada asılı `VBCSCompiler.exe` kalmaz.
5. **Graceful Stop:** yeni proje kuyruğa alınmaz; in-flight proje bitene kadar (örn. 5 sn timeout) beklenir → `runCancelled`.
6. **Hard Stop (timeout sonrası):** `TerminateJobObject(inner)` → tüm build ağacı anında, deterministik ölür. **PID-heuristik süpürme YOK** (eski kodun #2 borcu kapanır). Sonraki run için inner Job yeniden kurulur.
7. **Tray'den Stop:** pencere kapalıyken bile tray menüsünden tüm işlem durdurulabilir.
8. **Hata derlemeyi öldürmez:** tek proje hatası kuyruğu durdurmaz; "Failed" işaretlenir, kalanlar devam eder.

### Kabul kriteri (otomatik test edilir — §8)
- Build çalışırken pencere kapatılır → **2 sn içinde** arka planda MSBuild/VBCSCompiler/child KALMAZ.
- Build çalışırken uygulama kill edilir (crash) → aynı şekilde process kalmaz.
- Stop → graceful biter ya da timeout sonrası hard-kill; tüm child'lar temizlenir.

---

## 4. Çıktı Dizini Gerçeği (§4 — KRİTİK, korunuyor)

- **Çıktı mekanizması = projelerin KENDİ post-build copy event'leri.** Her proje normal `$(TargetDir)`'ine (bin/Debug|Release) derlenir; ardından projenin kendi post-build adımı çıktıyı ortak konum(lar)a kopyalar — gerçek örnek:
  ```
  copy /y "$(TargetDir)$(TargetName).*" "c:\OSYS\Client\bin\"
  copy /y "$(TargetDir)$(TargetName).*" "c:\OSYS\Server\bin\"
  ```
  Tüm projeler geliştirme aşamasında DLL'leri buradan kullanır. **Orchestrator bu kopyalamayı KENDİ yapmaz**; sadece `dotnet build` çağırır, event'ler VS'deki ile **birebir aynı** çalışır. **Ortak dizine DOKUNULMAZ, okunmaz.**
- **VS-parity (ZORUNLU):** Orchestrator her projeyi, kullanıcının VS'de derlediği **aynı ayarlarla** derler (config, OutputPath/TargetDir, post-build event'ler) — shell-out `dotnet build` bunu doğal sağlar. Hiçbir MSBuild ayarı override edilmez. Tek eklenen: §3 process-control flag'leri (`-p:UseSharedCompilation=false -nodeReuse:false`) ve worktree build'lerde ayrı çalışma dizini (obj doğal olarak orada izole). _(Bu, in-process/özel build yerine shell-out kararını da pekiştirir — post-build event'ler atlanmaz.)_
- **Final çıktı branch'e göre izole EDİLMEZ.** Bilinçli tercih: "şu an hangi branch + config derlendiyse onun DLL'i ortak dizinde geçerlidir."
- **Config tek klasör (config-agnostic):** ortak dizin Debug/Release ayırmaz → **Release derlemesi Debug DLL'lerini ezer** (ve tersi). Sonuç: **config değişimi tüm projeleri dirty yapar** (bkz. §6); aksi halde ortak dizin karışır ve runtime bozulur.
- Yalnızca **ara çıktı (obj / `BaseIntermediateOutputPath`)** worktree build'lerinde izole kalır (her worktree ayrı checkout → obj doğal ayrı; in-place build normal obj'i kullanır, VS ile parity). Aynı isimli projelerin obj çakışması proje **Id (tam yol)** ile önlenir (eski bug fix).
- **Sonuç:** Ortak dizin paylaşımlı + config-agnostic → DLL timestamp "değişti mi" kararında GÜVENİLMEZ. Karar yalnız **kaynak sinyaline** dayanır: imza = **config + commit + local-diff**. Ortak dizin global olduğundan **build-state de GLOBAL tutulur** (projectId anahtarlı = "ortak dizinde şu an hangi imza materyalize") — per-branch değil (bkz. §6/§9).

---

## 5. Sync — Proje Keşfi & Bağımlılık Grafiği

- Kökte `*.sln` ve `*.csproj` recursive taranır. Ignore: `.git bin obj node_modules .vs`.
- `ProjectReference`'lardan tek **global bağımlılık grafiği**; **Tarjan SCC** (cycle) + **Kahn** (topolojik sıra). Atomik `dependency-graph.json` cache **(version alanı + invalidation)**.
- **Açılışta cache'ten okunur** (full re-scan YOK — eski bug fix). Tam yeniden analiz yalnız **Sync** butonuyla.
- **Circular dependency:** SCC ile tespit; kartlarda "Cycle" rozeti + döngü projelerini gösteren tooltip.
- Bulunan **tüm** projeler listeye eklenir (derlenmeyecekler dahil).
- **Graf doğruluğu (enhancement):** XML parse'ın conditional/SDK reference kör noktasını kapatmak için opsiyonel olarak `dotnet msbuild -getItem:ProjectReference` ile evaluated reference okunabilir (hâlâ shell-out, assembly yüklemeden). v1'de XML parse temel; bu opsiyon değerlendirilir.

---

## 6. Derleme Stratejisi

### Rebuild
Tüm projeler topolojik sıraya göre; **bağımsızlar paralel** `dotnet build` ile.

### Build (incremental) — projenin kalbi
Bir proje **yalnız** şu hallerde derlenir:
1. Branch'teki güncel commit, son başarıyla derlenen commit'ten farklı **ve** projeyi etkiliyor, **veya**
2. Working-tree'de projeyi etkileyen local değişiklik (`git status`), **veya**
3. Daha önce hiç başarıyla derlenmemiş.

Aksi halde **Skipped** (listede soluk + "atlandı" animasyonu).

- **DLL/bin timestamp ASLA okunmaz.** Karar yalnız kaynak sinyali.
- Dosya→proje eşlemesi: değişen dosya, proje klasörü altında ve build'i etkileyen uzantıda (`.cs .xaml .resx .csproj .props .targets`) → proje dirty. Üstten import edilen `Directory.Build.props/targets` değişirse kapsamındaki tüm projeler dirty.
- Downstream: **Safe (varsayılan)** = dirty + transitif bağımlılar; **Fast** = sadece dirty.
- **`build-state.json` → `projectId` anahtarlı (GLOBAL — branch'e özel değil).** Değer: ortak dizinde **şu an materyalize olan kaynak imzası** = **config + HEAD commit + (in-place derlemede) local-diff hash**. "Zaten derlendi mi" kararı bu global imza ile verilir (bkz. §4). Branch-bounce (developer→X→developer) senaryosunda doğru rebuild tetiklenir; per-branch keying'in yanlış 'atla' bug'ı oluşmaz. Başarısızlıkta eski imza korunur (tekrar dener). _(branch alanı yalnız teşhis/log; karar anahtarı değildir.)_
- **Config değişimi (Debug↔Release) → TÜM projeler dirty.** Ortak dizin config-agnostic tek klasör olduğundan (§4), istenen config ortak dizindeki mevcut config'ten farklıysa global "OutDir config" işareti uyuşmaz → hepsi yeniden derlenir (yarı-Debug/yarı-Release karışık dizin engellenir). UI'da "config değişti, tümü yeniden derlenecek" bilgisi loglanır.

### Paralellik & kaynak sınırlama
Bağımsız projeler eşzamanlı. Paralel derece **bizde** (Supervisor) yönetilir. **Performans modu** (Full/Balanced/Light) üç şeyi birden belirler: **paralel derece + process priority + Job Object CPU rate cap** (sert CPU tavanı, örn. Light≈%40 / Balanced≈%70 / Full=sınırsız). Priority ve CPU cap **inner Job Object** üzerinden uygulanır → "bilgisayar kitleniyor" sorununa doğrudan çözüm. CPU cap **çalışırken bile** değiştirilebilir (run ortasında yavaşlat/hızlandır).

### Branch & worktree yönetimi
- Açılışta **kullanıcının aktif branch'i** otomatik seçili.
- **Branch seçimi = sadece niyet.** Build'e basılana kadar git'te **hiçbir işlem yapılmaz** (`git worktree add` dahil). Worktree, Build anında hazırlanır/güncellenir.
- **Worktree, her zaman seçilebilir bir toggle'dır** (sadece farklı branch'a özel değil). Aktif branch'ta bile, local değişiklikleri **dahil etmeden** committed-temiz derleme için worktree açılabilir.
- **Varsayılan toggle:** farklı branch → ON; aktif branch → OFF. İkisi de değiştirilebilir.
- Worktree ON → o branch'ın **committed HEAD**'i `%LOCALAPPDATA%\BuildOrchestrator\worktrees\<isim>\` altında derlenir; **VS'de açık working tree DEĞİŞMEZ**, local değişiklikler **hariç**. Worktree'ye seçildiği an **otomatik standart isim** atanır (örn. `<branch>-<n>`), düzenlenebilir. **Aktif worktree'ler UI'da listelenir**, yeniden kullanılabilir.
- **Davranış matrisi (Build anında çözülür):**

  | Worktree | Seçili branch | Local değişiklik | Davranış |
  |---|---|---|---|
  | OFF | = Aktif | Yok | In-place. Uyarı yok. |
  | OFF | = Aktif | Var | In-place, **local dahil**, commit istenmez, log'a düşer. |
  | OFF | ≠ Aktif | Var | **Uyarı (`runBlocked`):** checkout local'i ezer → "worktree'ye geç / commit-stash". |
  | OFF | ≠ Aktif | Yok | In-place checkout → VS branch'ı değişir (dikkat gerektirir). |
  | **ON** | herhangi (aktif dahil) | (fark etmez) | Worktree'de committed HEAD; local değişiklikler hariç; ana tree dokunulmaz. |

- Derleme sonucu ortak OutDir'e düşer.
- **Git komut sonuçları kontrol edilir** (eski silent-failure fix); hata UI'a `error` event'i olarak yansır.

### Eşzamanlılık / kilit
- Orchestrator kendi içinde tek seferde tek run garanti eder; OutDir'e yazımda kendi-kendine çakışmayı önler.

---

## 7. UI / UX (tek pencere — §7, paralel-farkında revize edildi · v3)

**Düzen:** Sol = proje kart listesi (virtualized), Sağ = siyah console. GridSplitter.

- **Sol alt:** Sync ikonu + 5 sayaç (Toplam / Derlenen / Başarılı / Başarısız / Atlanan); sayaca tıkla → kartlar **animasyonlu** filtrelenir (opacity → collapse).
- **Sağ alt (selector kümesi — combo DEĞİL):** **ikon-buton + label chip**'ler:
  - **Branch chip:** küçük ikon + branch adı; tıkla → **animasyonlu popup** (aranabilir branch listesi, aktif branch işaretli).
  - **Worktree chip:** worktree ikonu **aynı zamanda ON/OFF toggle**; yanında worktree adı; tıkla → popup (mevcut worktree'ler + "yeni (oto-isim)").
  - **Perf chip:** Full/Balanced/Light hızlı seçici (CPU cap dahil).
  - Yanında **Build**, **Rebuild**; çalışırken buton **Stop**'a morph + loading.
  - Popup'lar yalnız `RenderTransform` + `Opacity` ile açılır/kapanır (layout animasyonu yok).
- **Kartlar:** proje + solution adı; sağ altta "Dosyada Aç" / "Visual Studio'da Aç" ikonları.
- **Durumlar (renk):** Discovered, Queued, Building, Succeeded, Failed, Skipped, CycleDetected (+ cycle rozeti).

**Console:**
- Varsayılan **errors-only**; **toggle ile full log** (eksik toggle eklenir). Ring buffer (max satır).
- Hatalı karta tıkla → console'da **sadece o projenin** çıktısı; tekrar tıkla → tümü.
- **Auto-follow:** yeni log geldikçe en alta kayar; kullanıcı scroll yapınca durur; ~2 sn idle'da devam (her hareket sayacı sıfırlar).

### Paralel derleme görünürlüğü (build frontier — TEKİL carousel YOK · v3)
- Paralel çoklu derlemede aynı anda **N proje** Building olur → "tek aktif kart" varsayımı **geçersizdir**. Tekil carousel / iOS-takvim / "öne gelir döner" konsepti **kullanılmaz**.
- **Aktif bant (build frontier):** liste build order'a göre sıralı; o an Building olan **tüm** kartlar birden canlı (pulse + shimmer). Auto-scroll tek karta değil **aktif grubun ağırlık merkezine** yumuşak takip eder; grup ekrandayken kaydırmaz.
- **Sticky "şu an derleniyor (N)" şeridi:** üstte sabit; o anki paralel Building setini küçük çiplerle gösterir → kullanıcı nereye scroll ederse etsin canlı set görünür. Çipe tıkla → ilgili karta git.
- **Auto-scroll duraklatma:** manuel scroll'da grup takibi durur, ~2 sn idle'da devam (her hareket sayacı sıfırlar). Eski kodun "her ProjectStarted'da zorla en üste atlama" deseni yerine.

### Per-card durum animasyonları (paralel-uyumlu; UI'ı KİLİTLEMEZ)
- Yalnız `RenderTransform` + `Opacity` — **Width/Height/Margin YOK** (eski accent-strip Width 6→13 ihlali düzeltilir). Liste **UI virtualization** (VirtualizingStackPanel; `CanContentScroll`/`IsVirtualizing` yanlışlıkla kapatılmaz). 500–1000 kartta akıcı.
- Her kartın kendi state animasyonu: **Building** = pulse border + shimmer; **Failed** = kısa shake; **Succeeded** = glow; **Skipped** = fade + **desaturate**; **Cycle** = rozet. (Bunlar kartın kendi state'i olduğundan paralel-uyumlu.)
- **ReducedMotion gerçekten tüm animasyonlara bağlanır** (eski bug: hiçbirine bağlı değildi). Açıkken animasyonlar minimuma iner (bant takibi + per-card efektler + popup'lar dahil).

---

## 8. Test Stratejisi (§10 — process testleri artık first-class)

- **Unit (Core):** graph extraction, topolojik sıra, cycle tespiti, dosya→proje eşleme, **incremental kararı (global imza + commit-delta tetikleyici izole)**, **branch-bounce senaryosu (developer→X→developer doğru rebuild)**, Safe/Fast propagation, scanner ignore-dir.
- **Process-control (ZORUNLU — eski kodda HİÇ yoktu):** otomatik test — gerçek/dummy build başlat → App kill / crash / Stop → **2 sn içinde artık process kalmadığını assert et**. Iteration 0'dan itibaren çalışır.
- **Integration:** çoklu-solution örnek workspace → Sync, Build, Rebuild, branch switch (worktree toggle), Stop.
- **Performans:** 500+ kart akıcı scroll; cache-hit'te hızlı sync; paralel build ölçülebilir kazanç; **CPU cap'in gerçekten tavanı tuttuğu**; log akışında UI bloklanmaz.

---

## 9. Supervisor ↔ UI Sözleşmesi (§8 — temiz tutulur)

- **Komutlar:** `syncWorkspace(rootPath)`, `reanalyze()`, `listBranches()`, `listWorktrees()`, `selectBranch(branch)`, `startRun(mode, branch, useWorktree, worktreeName?, config, dependentMode, perfMode)`, `setPerfMode(perfMode)` _(çalışan run'a canlı uygulanır)_, `stopRun(runId)`, `openPath(projectId)`, `openInVS(projectId)`.
- **Eventler:** `syncProgress`, `syncCompleted`, `worktreesListed`, `runStarted`, `projectStarted`, `projectLog`, `projectSucceeded`, `projectFailed`, `projectSkipped`, `runCompleted`, `runCancelled`, `runBlocked` _(ör. farklı branch + local değişiklik + worktree OFF)_, `error`.
- **Tipler:**
  - `ProjectNode { id, name, projectPath, solutionName, dependencies[], buildOrder }`
  - `BuildState { projectId, builtSignature, lastResult, lastRunAt, lastBranch? }` — **GLOBAL** (projectId anahtarlı); `builtSignature` = materyalize kaynak imzası (**config + HEAD commit + varsa local-diff hash**). `lastBranch` yalnız teşhis. Ayrıca global tekil `OutDirConfig` işareti (ortak dizinde şu an hangi config materyalize).
  - `Worktree { name, branch, path, isActive }`
  - `RunRequest { mode:'build'|'rebuild', branch, useWorktree:bool, worktreeName?, config:'Debug'|'Release', dependentMode:'safe'|'fast', perfMode:'full'|'balanced'|'light' }`
- **Disiplin:** ölü komut / spec dışı event / doğrulanmayan alan eklenmez (eski sözleşme sapmaları tekrarlanmaz). Eklenen her komut/event hem gönderilir hem işlenir. `skipped` event'i planner'ın **gerçek reason'ını** taşır (eski sabit "no source change" yerine).

---

## 10. Walking-Skeleton Faz Planı (her iterasyon uçtan uca çalışır + gösterilebilir)

| It. | Teslim | Neden bu sırada |
|---|---|---|
| **0** | İki process + stdio IPC + **nested Job cascade**. Minimal pencere: root seç → dummy/uzun child derleme → canlı log → **çalışan Stop**. §3 kabul testi (kapat/kill → 2 sn'de process kalmaz) **geçer**. DI iskeleti kurulu. | **En kritik risk (process+IPC) en başta kanıtlanır.** |
| **1** | Sync/graph: scan, graph, Tarjan cycle, Kahn topo, cache (açılışta load). Kartlar gerçek projelerle dolar. | Veri temeli. |
| **2** | **Rebuild** (gerçek, paralel): topo sıra, bağımsızlar paralel `dotnet build`, canlı log, hata izolasyonu, sayaçlar, per-card console scope. | İlk gerçek değer: çalışan derleme. |
| **3** | **Incremental Build**: commit/diff/status, **GLOBAL build-state (projectId/imza)**, Safe/Fast, **worktree toggle modeli** (deferred git, otomatik isim, karar matrisi) + obj izolasyonu, Skipped. | Projenin asıl zekâsı. |
| **4** | UX polish + config: **build frontier (aktif bant + sticky "şu an derleniyor" şeridi)**, **sağ-alt chip selector (branch / worktree+toggle / perf) + animasyonlu popup'lar**, **aktif worktree listesi**, per-card imza animasyonları, ReducedMotion, console toggle, filtre/Stop morph, config ekranı (LogLevel/ReducedMotion **dahil**), tray, autostart, single-instance. | Cila + eski config bug fix. |
| **5** | Perf modları (Full/Balanced/Light = derece + priority + **Job Object CPU rate cap**, canlı değiştirilebilir), 500–1000 kart perf doğrulama, basit `dotnet publish` paketleme. | Ölçek + dağıtım. |

---

## 11. Yapılandırma (config)

- Kök dizin (taranacak).
- Build configuration: **Debug (varsayılan)** / Release. Ortak dizin config-agnostic olduğundan config değiştirince tüm projeler yeniden derlenir (§4/§6).
- Performans modu: **Full Power / Balanced / Light** (paralel derece + process priority + **Job Object CPU rate cap**). **Ana UI'da hızlı seçici** olarak da yer alır (Build yanındaki perf chip); buradaki değer ayarlardaki varsayılandan başlar, **run öncesi/sırasında** değiştirilebilir.
- Worktree varsayılanı: **farklı branch → ON, aktif branch → OFF** (her derlemede toggle ile değiştirilebilir). Worktree havuzu konumu yapılandırılır.
- Log seviyesi: **Errors-only (varsayılan)** / Full.
- Bağımlı (downstream) modu: **Safe (varsayılan)** / Fast.
- Cache konumu, **Reduced Motion (varsayılan kapalı)**.
- **Not (eski bug fix):** LogLevel ve ReducedMotion config ekranında **görünür** ve `ToConfig()` ile taşınır (eski kodda Save'de default'a sıfırlanıyordu).

---

## 12. Kapsam Sınırları (v1)

- **İçinde:** tek repo, shell-out derleme, nested-Job process kontrolü (+CPU cap), sync/graph/cache, rebuild + incremental (global build-state), worktree toggle modeli, tam UI/UX + animasyonlar + chip selector, config, tray/autostart/single-instance, perf modları, unit+process+integration test, basit `dotnet publish`.
- **Dışında (sonraya):**
  - **Multi-repo** (onaylandı) — mimari genişletilebilir kurulur.
  - **MSIX/installer/auto-update** — v1'de sadece publish.
  - **WinUI Composition** animasyon güçlendirme — WPF-native yeterli; gerekirse sonra.
  - **Graf dalgası (dependency-flow) görselleştirme** — etkileyici ama virtualized yüzlerce kartta pahalı; v1 sonrası değerlendirilir.
  - **Özel CPU % slider** (3 modun ötesinde elle yüzde) — v1'de 3 mod yeterli; gerekirse sonra.
  - **Headless/CLI** — mimari Supervisor ile hazır, v1'de CLI arayüz geliştirilmez.

---

## 13. Varsayımlar / Varsayılanlar

- Projeler **tek git repo** altında (multi-repo sonraya).
- Ortak çıktı dizini projelerin kendi post-build event'leriyle dolar (orchestrator dokunmaz); config-agnostic tek klasör; "değişti mi" kararı imza = config + commit + local-diff; build-state **global** (projectId).
- Kullanıcı VS'de aynı projeleri Orchestrator ile eşzamanlı derlemez.
- Varsayılanlar: Debug, Safe, worktree (farklı branch'ta ON), Errors-only, Full Power, Reduced Motion kapalı.
- Bağımlılık sırası cache'ten okunur; tam yeniden analiz yalnız Sync ile.
- Orchestrator .NET 10 hedefler; derlenen projeler kullanıcının kurulu SDK'sıyla derlenir (TFM bağımsız).

---

## 14. Sıradaki Adım

Planlama diyaloğu **devam ediyor** (yeni karalama/eski notlar plana karşı değerlendiriliyor). v3 üzerinde mutabık kalınınca **writing-plans** ile Iteration 0 için detaylı, adım adım uygulanabilir implementation plan çıkarılacak.
