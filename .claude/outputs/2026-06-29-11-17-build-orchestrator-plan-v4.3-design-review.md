# Build Orchestrator — Plan v4.3 (Design Review'lı)

> **v4.3 = v4.2 + Design Review.** Bu dosya = tam v4.2 (gövde + CEO + Eng review, **değiştirilmedi**) **+ Design review kararları/çıktıları** (dosyanın sonundaki `DESIGN REVIEW` bölümü). Review **App UI** sınıfında 7 pass + outside voice (bağımsız Claude design subagent; Codex bu makinede yok) ile yapıldı; 4 kullanıcı-onaylı fork + craft kararları + §7/§11/§13 gövde deltaları (OTORİTE) işlendi; 16 yeni design task (T34–T49). Verdict: **DESIGN CLEARED**. Girdi: [2026-06-29-10-48-build-orchestrator-plan-v4.2-eng-review.md](2026-06-29-10-48-build-orchestrator-plan-v4.2-eng-review.md).
>
> **⚠️ §7'yi ham okuMA:** console/stream modeli (3→2 yapısal zone), typing/live-line davranışı, motion budget + reduced-motion, interaction state'leri, discoverability ve görsel north-star **DESIGN REVIEW bölümünde revize/eklendi** (gövde tarihsel kayıt; otorite DESIGN REVIEW deltalarıdır). **Önce DESIGN REVIEW bölümünü oku.** Görsel değerler (renk/tipografi/ikon) kullanıcıda (N4 — Claude Design); bu review tasarım NİYETİ + etkileşim + state + motion + token rollerini sabitler.
>
> **v4.2 = v4.1 + Eng Review.** Bu dosya = **temiz v4 planı + CEO review** (aşağıdaki gövde + `CEO REVIEW` bölümü, **değiştirilmedi**) **+ Eng review kararları/çıktıları** (dosyanın sonundaki `ENG REVIEW` bölümü). v4.1 girdisi: [2026-06-29-00-27-build-orchestrator-plan-v4.1-ceo-review.md](2026-06-29-00-27-build-orchestrator-plan-v4.1-ceo-review.md). Temiz v4 tabanı: [2026-06-29-00-27-build-orchestrator-plan-v4.md](2026-06-29-00-27-build-orchestrator-plan-v4.md).
>
> **⚠️ KRİTİK — gövdeyi ham okuMA:** Eng review, gerçek hedef repo'yu (`D:\Projects\Delta\OSYS`) açıp doğruladı: **175/191 proje legacy .NET Framework (v4.6/v4.8), 21 packages.config, 1927 HintPath satırı (absolute `C:\OSYS\...\Bin`), 178 absolute post-build copy**. Bu yer gerçeği **§0 engine kararını (`dotnet build`), §5 graf kaynağını (ProjectReference) ve §6 worktree çıktı-izolasyon ifadesini** geçersiz kılar. Bu bölümler **ENG REVIEW bölümünde revize edildi** (gövde tarihsel kayıt olarak korundu; otorite ENG REVIEW deltalarıdır). **Önce ENG REVIEW bölümünü oku.**

> Bu doküman, **v3 plan**'ın ([2026-06-28-08-19-build-orchestrator-plan-v3.md](2026-06-28-08-19-build-orchestrator-plan-v3.md)) devamıdır; o da v2 ([2026-06-27-22-46-build-orchestrator-yeni-plan.md](2026-06-27-22-46-build-orchestrator-yeni-plan.md)) ve orijinal 11 bölümlük prompt'tan ([2026-06-27-21-40-build-orchestrator-orijinal-prompt.md](2026-06-27-21-40-build-orchestrator-orijinal-prompt.md)) türetilmiştir.
>
> **v4'te değişen (bu session, kullanıcı yorumları işlendi — kümülatif):**
> 1. **§7 console/log modeli yeniden kurgulandı** — seçime bağlı **iki görünüm**: (a) seçim yok → **özet modu** (orchestrator'ın kendi durum logları: başla/sonuç/süre + `Done` toplam süre), (b) bir karta tıkla → **proje detay modu** (sadece o projenin **tam `dotnet build` çıktısı**, başarı/hata fark etmez). Her projenin tam çıktısı **Supervisor'da buffer'lanır**, tıklanınca App'e stream edilir → "sadece sonuncusunun logu geliyor" bug'ı baştan yok. **LogLevel ayarı ve ekran üstündeki full-log checkbox KALDIRILDI** (görünüm artık seçimle belirlenir).
> 2. **§7 kart seçim efekti** — sol renkli accent şeridi **kalınlaşır + yazılar bir tık içe kayar** (kutu/border YOK), **anlık + animasyonlu**; tek seçim; başka karta tıkla → eski kart normale döner; **console'a tıkla → seçim kalkar** (özet moduna döner). v3'ün performans gerekçesiyle sildiği bu efekt, **yalnız seçili tek karta** (geçici) uygulandığından frontier perf kuralını bozmaz.
> 3. **§6/§10 sıra-koruyan paralel scheduler** — ready (derlenebilir) projeler **topolojik build-order önceliğiyle** dispatch edilir: ilk N başlatılacaksa bunlar **listenin ilk N'i** olur (baştan/sondan/ortadan rastgele başlatma yok).
> 4. **ReducedMotion KALDIRILDI** (§4/§7/§11) — standart animasyonlar **her zaman açık**; ilgili ayar ve "azaltılmış hareket" iş kalemi düşer.
> 5. **§3/§7/§10 pencere kapatma → tray'e küçülür** (exit etmez); tray menüsünden **Exit** → çalışan build dahil **cascade-kill** (nested Job zaten garanti).
> 6. **§7 özet logdaki hata satırı tıklanabilir** → ilgili kartı **seç + focus + detay log** (karta tıklamayla aynı sonuç).
> 7. **§7/§9 `Done` satırında toplam süre**; özet logda her proje satırında süre.
> 8. **§7 dark/modern custom title bar** (WPF `WindowChrome`) — pencere çubukları siyah/modern; config ekranı dropdown'ları da modern restyle.
> 9. **§10/§11 logo + app icon** — pencere/taskbar/tray ikonu + uygulama logosu.
> 10. **§10/§12 README** teslim kalemi olarak eklendi.
>
> **Atlananlar (kullanıcı kararı):** Eski (silinmiş) koda ait C11 ("manuel derleniyor, diğerinde sorun") ve C12 ("uygulamadan açılan solution yüklenemedi") bug araştırması v4'e **taşınmadı**; yeni mimaride baştan doğru kurulur. (Not: "VS'de Aç" / "Dosyada Aç" özellikleri planda kalır — §7/§9.)
>
> ---
>
> **v4 — EK REVİZYON (bu session, ikinci not seti — yukarıdaki 1–10'a EK, kümülatif):**
> 11. **§7 console temizleme + granular adım logu (N1)** — Sync ve Build'e basıldığında console **temizlenir** ve baştan yazılır; özet modda aşama aşama granular adımlar loglanır ("solution'lar taranıyor", "ProjectReference'lar okunuyor", "graf kuruluyor / cycle kontrolü", "derleme sırası belirlendi", "katman X derleniyor"...).
> 12. **§6/§7/§9 worktree kalıcı + "Sil" butonu + branch-silme guard (N3)** — worktree havuzu **kalıcı kalır** (hız/obj cache); her worktree için UI'da **"Sil"** aksiyonu; bir branch silinmeye çalışılırken worktree onu tutuyorsa **uyarı + "önce worktree'yi sil"**. ("branch silinmiyor" sorunu çözülür.)
> 13. **§7/§11 kısayollar + global hotkey (N6)** — **çift-Shift** → branch hızlı arama; **Ctrl+P** → proje/kök dizin seçici; **global hotkey** (ör. Alt+B) → tray'den uygulamayı sağ-alt köşeden **animasyonla** çıkar/restore; **Ctrl+B / Ctrl+R** → Build/Rebuild (pencere açıkken).
> 14. **§5/§7 bağımlılık sağlık göstergesi (N7)** — cycle'sız projeler **yeşil**, cycle'a dahil olanlar **kırmızı + rozet** (kart üzerinde renk + ikon).
> 15. **§5/§6/§11 KATMAN PATTERN — layered build (N8, yeni majör özellik)** — ayarlarda **sıralı, sınırsız regex** listesi; her pattern bir katman; regex **proje ADINA** eşleşir, **ilk-eşleşen kazanır**; **sert faz bariyeri** (katman bitmeden sonraki başlamaz); her katman **yalnız kendi projeleri** arasında dependency analizi + topo + paralel (katman-içi sıra-koruyan scheduler); **eşleşmeyen projeler → son implicit "Diğerleri" katmanı**; **pattern verilmezse → global graf** (v4 mevcut davranış); incremental skip katman-içinde de geçerli; ters katman bağımlılığı → **hafif tespit+uyarı** (rozet/log, bloklamaz), gelişmiş "standart dışı durum" çözümü **ertelendi**.
> 16. **§7/§9 kartta commit gösterimi (N10)** — kart üzerinde "şu an `<builtCommit>` ile derli → hedef `<targetCommit>`" + statü (derlendi / derlenmedi / atlandı / hata) renk+rozet.
> 17. **§6/§7 local-vs-committed = worktree toggle netleştirildi (N9)** — Worktree **OFF** → mevcut aktif kod, **local değişiklikler dahil**; Worktree **ON** → committed HEAD, local hariç. İkisi de chip'ten seçilir, ekranda görünür; UI etiketleri net gösterir. (v4'te zaten vardı; netleştirildi.)
> 18. **§7/§14 tasarım niyeti planda; görsel tasarım sonra (N4)** — plan tasarım niyetini (dark theme, renk/tipografi yönü, kart/console/title-bar/animasyon stili) taşımaya devam eder; kullanıcı bu notlardan ayrıca **görsel tasarım** üretip ekleyecek. Ayrı tasarım-tooling adımı şimdilik yok.
>
> **Park edilenler (sonraya):** CLAUDE.md çoklu-dosya kurgusu + agent ile senkron (N2) → implementation aşamasına ertelendi.
>
> Diğer bölümler v3 ile aynıdır.

**Tarih:** 2026-06-29 00:27 · **Ek revizyon:** 2026-06-29 01:24
**Karar süreci:** Brainstorming ile, kullanıcı onaylı. v3'ün tüm kararları geçerli; v4 yukarıdaki **1–18** maddeyi revize/ekler (1–10 ilk tur, 11–18 ikinci not seti). **Bu session kapatılıyor; planlama yeni session'dan devam edecek (gerekirse v5 yazılacak).**

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
| `BuildOrchestrator.Supervisor` | net10.0-windows (console) | Orchestration: build kuyruğu, **inner Job Object**, her projeyi `dotnet build` child process olarak çalıştırma, log parse + **proje başına tam çıktı buffer'ı**, IPC server (stdio). Core'a referans verir. |
| `BuildOrchestrator.App` | net10.0-windows (WPF) | UI/MVVM (CommunityToolkit.Mvvm), **tray (kapatınca küçülür)**, single-instance, autostart, **outer Job Object**, IPC client, **custom dark title bar**. Supervisor'ı spawn eder. |
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
7. **Pencere kapatma → tray'e küçülür (exit DEĞİL):** Pencere `X`'i uygulamayı kapatmaz; **system tray'e küçülür** (sağ alt). Uygulama arkada çalışmaya devam eder (build sürüyorsa kesilmez).
8. **Tray'den Exit → cascade-kill:** Tray menüsünden **Exit** seçilince uygulama gerçekten kapanır; build sürüyorsa outer Job kapanışı ile **her şey kaskat ölür** (Supervisor + tüm `dotnet build` ağaçları). Pencere kapalıyken tray menüsünden **Stop** da yapılabilir (build'i durdurur ama uygulamayı açık bırakır).
9. **Hata derlemeyi öldürmez:** tek proje hatası kuyruğu durdurmaz; "Failed" işaretlenir, kalanlar devam eder.

### Kabul kriteri (otomatik test edilir — §8)
- Build çalışırken pencere `X`'e basılır → **uygulama kapanmaz, tray'e küçülür**, build devam eder.
- Build çalışırken **tray'den Exit** → **2 sn içinde** arka planda MSBuild/VBCSCompiler/child KALMAZ.
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
- **Bağımlılık sağlık göstergesi (N7):** Cycle'a dahil **olmayan** (graf sağlıklı) projeler kartta **yeşil** sağlık göstergesi (nokta/ikon); cycle'a dahil olanlar **kırmızı + rozet**. Kullanıcı tek bakışta sorunlu bağımlılıkları görür.
- Bulunan **tüm** projeler listeye eklenir (derlenmeyecekler dahil).
- **Liste sırası = build order:** Sync sonrası bağımlılıklar analiz edilip topolojik sıra belirlenir; **kart listesi bu sıraya göre dizilir** (bağımlılığı olan/olmayan fark etmez — analiz edilen sıra neyse liste odur). Build sırasında da bu sıra korunur (bkz. §6 scheduler). **Katman pattern tanımlıysa** (§6/§11) liste **katmanlara göre gruplanır** (Katman 1 [topo], Katman 2 [topo], ..., Diğerleri), her katman kendi içinde topolojik sıralı.
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

### Sıra-koruyan paralel scheduler (v4 — yeni netleştirme)
- Scheduler, bir "ready set" (bağımlılıkları tamamlanmış, derlenmeye hazır projeler) üzerinden çalışır. Bir slot boşaldığında, ready set'ten **build-order'da en önde gelen** proje seçilir. **Rastgele / hash-sırası / baştan-sondan-ortadan dispatch YOK** (eski kodun gözlemlenen davranışı düzeltilir).
- Sonuç: paralel derece N ise ve N proje birbirinden bağımsızsa, ilk başlayanlar **listenin ilk N projesidir**. Kullanıcı, build frontier'ın listenin üstünden aşağıya doğru düzenli ilerlediğini görür.
- Bağımlılık nedeniyle bekleyen bir proje, önündeki bağımlılığı bitene kadar başlamaz; o slot, ready set'teki **bir sonraki en öndeki** projeye gider (sıra korunur, slot boşa yatmaz).
- Bu sıra garantisi **deterministiktir ve test edilir** (§8): aynı graf + aynı paralel derece → aynı dispatch sırası.

### Katman pattern — layered build (v4 ek · N8)
Kullanıcı, derlemeyi **insan-anlamlı katmanlara** bölmek isteyebilir (ör. önce `Types`, sonra `Business`, sonra `Orchestration`, sonra `UI`). Bu, bağımlılık grafiğinin **üstüne** binen bir sıralama katmanıdır. Uygulama OSYS'e özel değil; pattern **tamamen kullanıcı tanımlı**.

- **Tanım (ayarlardan):** **sıralı, sınırsız** regex pattern listesi. Her pattern bir **katman**; sıra önemli (Katman 1, 2, 3, ...). Pattern **proje ADINA** uygulanır; bir proje **birden fazla** pattern'e uyarsa **ilk (en düşük) katman kazanır**.
- **Sert faz bariyeri:** Katman N **tamamen** bitmeden Katman N+1 **başlamaz** (çünkü katmanlar birbirini kullanır; erken başlama hata verir).
- **Katman-içi analiz (kritik):** Her katman **yalnız kendi projeleri** arasında dependency analizi yapar → topo sıra → bağımsızlar paralel (§ sıra-koruyan scheduler katman-içinde çalışır). **Tüm-projeler-arası global graf kullanılmaz**; önceki katman zaten derlendiğinden (bariyer) o katmana olan bağımlılıklar karşılanmış sayılır.
- **Eşleşmeyen projeler:** Hiçbir katman regex'ine uymayan projeler, tüm tanımlı katmanlardan **sonra** gelen implicit bir **"Diğerleri"** katmanında derlenir (kendi içinde topo). Hiçbir proje atlanmaz; UI'da sayısı + uyarı gösterilir.
- **Pattern verilmezse:** v4 mevcut davranış — **tüm projeler tek global graf** ile analiz edilip sıralanır (katman yok).
- **Incremental skip** katman-içinde de geçerlidir (değişmeyen proje atlanır — § yukarı kriterler).
- **Standart dışı durum (ters katman bağımlılığı):** Bir erken-katman projesi geç-katman projesine bağlıysa (ör. Katman 1 → Katman 3) bariyer ihlali oluşur. **Varsayım:** kullanıcı katmanları **bağımlılık sırasına uygun (tek yönlü)** tanımlar, bu durum **beklenmez**. Yine de sessiz hata olmaması için **hafif tespit + uyarı** (cycle gibi rozet/log, **bloklamaz**) yapılır. Gelişmiş "standart dışı durum" çözümü **ertelendi** (sonra ele alınacak).
- Liste + console "derleme sırası" katmanlara göre gruplanır/yazılır.

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

- **Local vs committed seçimi = worktree toggle (netleştirme · N9):** Kullanıcının "mevcut aktif kodla mı, committed branch'tan mı derlensin" seçimi **doğrudan worktree toggle'ıdır**. **OFF** → in-place, **local değişiklikler dahil** (mevcut aktif kod; imzaya local-diff hash girer, değişiklik = dirty = derlenir). **ON** → committed HEAD, local **hariç** (temiz branch). İkisi de chip'ten seçilir, ekranda izlenir; UI etiketleri **"local dahil"** / **"committed temiz"** olarak net gösterir. İhtiyaç ikisinde de var.
- **Worktree silme + branch guard (N3):** Worktree havuzu **kalıcı** (hız/obj cache yeniden kullanılır). Her worktree için UI'da (popup/liste) görünür **"Sil"** aksiyonu (`git worktree remove`). Bir **branch silinmeye** çalışılırken bir worktree onu tutuyorsa git engeller → orchestrator **uyarı + "önce worktree'yi sil"** aksiyonu sunar (kullanıcının "branch silinmiyor" sorununu çözer).
- Derleme sonucu ortak OutDir'e düşer.
- **Git komut sonuçları kontrol edilir** (eski silent-failure fix); hata UI'a `error` event'i olarak yansır.

### Eşzamanlılık / kilit
- Orchestrator kendi içinde tek seferde tek run garanti eder; OutDir'e yazımda kendi-kendine çakışmayı önler.

---

## 7. UI / UX (tek pencere — §7, paralel-farkında + log/seçim modeli revize · v4)

**Düzen:** Sol = proje kart listesi (virtualized), Sağ = siyah console. GridSplitter.
**Pencere kabuğu:** **custom dark/modern title bar** (WPF `WindowChrome`) — pencere çubukları siyah; min/max/close butonları modern, uygulama logosu/iconu başlıkta. `X` → **tray'e küçülür** (§3.7), exit etmez. **App icon** taskbar + tray + pencerede görünür.

- **Sol alt:** Sync ikonu + 5 sayaç (Toplam / Derlenen / Başarılı / Başarısız / Atlanan); sayaca tıkla → kartlar **animasyonlu** filtrelenir (opacity → collapse).
- **Sağ alt (selector kümesi — combo DEĞİL, modern chip'ler):** **ikon-buton + label chip**'ler:
  - **Branch chip:** küçük ikon + branch adı; tıkla → **animasyonlu popup** (aranabilir branch listesi, aktif branch işaretli).
  - **Worktree chip:** worktree ikonu **aynı zamanda ON/OFF toggle**; yanında worktree adı; tıkla → popup (mevcut worktree'ler + "yeni (oto-isim)"). Popup'taki her worktree satırında **"Sil"** aksiyonu (N3); ON iken etiket "committed temiz", OFF iken "local dahil" net gösterilir (N9).
  - **Perf chip:** Full/Balanced/Light hızlı seçici (CPU cap dahil).
  - Yanında **Build**, **Rebuild**; çalışırken buton **Stop**'a morph + loading.
  - Popup'lar yalnız `RenderTransform` + `Opacity` ile açılır/kapanır (layout animasyonu yok).
- **Kartlar:** proje + solution adı; sol kenarda renkli **accent şerit**; sağ altta **"Dosyada Aç"** (projenin dizinini açar) / **"Visual Studio'da Aç"** (projenin bağlı olduğu **solution**'ı açar) ikonları.
  - **Bağımlılık sağlığı (N7):** cycle'sız=**yeşil** gösterge, cycle=**kırmızı + rozet** (§5).
  - **Commit gösterimi (N10):** kartta "şu an `<builtCommit>` ile derli → hedef `<targetCommit>`" (built = `BuildState.builtSignature` commit'i, target = güncel HEAD). İkisi farklıysa görsel vurgu (derlenecek). Tooltip'te kısa SHA + tarih.
- **Durumlar (renk + statü N10):** Discovered, Queued, Building, Succeeded, Failed, Skipped (pass), CycleDetected (+ cycle rozeti). Statü hem **renk** hem **metin/rozet** olarak kartta görünür (derlendi / derlenmedi / atlandı / hata).

### Kart seçim modeli (v4 — yeni)
- **Seçim efekti:** bir karta tıklanınca **sol accent şeridi kalınlaşır** + **kart içeriği (yazılar) bir tık içe kayar** → seçili olduğu net belli olur. **Kutu/border YOK.** Geçiş **animasyonlu ama anlık/hızlı** (tıklanan an hisseder).
- **Tek seçim:** başka bir karta tıklanınca eski kart **normale döner**, yeni kart seçilir. **Console'a tıkla → seçim kalkar** (özet moduna döner).
- **Seçim ↔ console kapsamı:** kart seçimi, console'un hangi modu göstereceğini belirler (aşağıdaki Console bölümü).
- **Teknik / perf:** seçim efekti yalnız **seçili tek kartta** (geçici) uygulanır → `RenderTransform` (yazı translate) + accent kalınlık animasyonu **tek kart**. Frontier'da (paralel Building) **çoklu/sürekli** layout animasyonu yasağı korunur; seçim tekil ve geçici olduğundan virtualization perf'i bozulmaz. (v3'ün performans için sildiği "accent genişler + yazı kayar" efekti, **yalnız seçime özel** geri getirildi.)

### Console / Log modeli (v4 — yeniden kurgulandı)
İki görünüm; **aktif kart seçimi** belirler:

- **Özet modu (varsayılan — seçim yok):** Orchestrator'ın **kendi durum/özet logları**. Build aracının ham çıktısı DEĞİL; bizim ürettiğimiz okunur özet:
  - **Console temizleme (N1):** **Sync**'e basınca console **temizlenir** ve sync adımları baştan yazılır; **Build/Rebuild**'e basınca console **yine temizlenir** ve derleme adımları baştan yazılır. (Her aksiyon temiz bir özetle başlar.)
  - **Granular aşama logları (N1):** işlem aşama aşama yazılır — ör. `Solution'lar taranıyor (N bulundu)`, `ProjectReference'lar okunuyor`, `Bağımlılık grafiği kuruluyor / cycle kontrolü`, `Derleme sırası belirlendi (N proje)`, katman varsa `Katman 1 (Types) derleniyor — M proje`.
  - Akış olayları: `Sync started`, `Bağımlılıklar analiz edildi — derleme sırası belirlendi (N proje)`, `Build started (mode, branch, config)`.
  - Her proje için **tek satır**: başlama + sonuç + **süre** (örn. `✓ Client.Core — 2.3s`, `✗ Server.Api — failed (1.1s)`, `↷ Common.Utils — skipped (no source change)`).
  - En altta **`Done` satırı + TOPLAM süre** (örn. `Done — 118 ok, 2 failed, 7 skipped · toplam 4m12s`).
  - **Okunurluk:** durum/kategoriye göre **renk + ikon** (info / building / success=yeşil / fail=kırmızı / skipped=soluk / done). Sadelik bozulmadan.
- **Proje detay modu (bir karta tıklayınca):** Console **sadece o projenin tam `dotnet build` çıktısını** gösterir — **başarı/başarısız fark etmez**; tıkladığın her kartta o projenin VS'deki gibi tam derleme output'u gelir. Geri dönmek için console'a (veya seçili karta) tekrar tıkla.
- **Saklama mimarisi:** **Supervisor her projenin tam çıktısını buffer'lar** (proje başına ring buffer, max satır cap). Bir karta tıklanınca App, o projenin logunu Supervisor'dan **lazy** ister → stream edilir → console'da gösterilir. Böylece **"sadece sonuncusunun logu geliyor" bug'ı baştan yoktur** — hangi karta tıklarsan onun tam logu gelir.
- **Kaldırılanlar:** Ayarlardan **LogLevel (Errors-only/Full)** ve ekran üstündeki **full-log checkbox** **kaldırıldı**. Görünüm artık **seçimle** belirlenir (özet vs proje-detay), ayrı bir log-seviyesi anahtarına gerek yok.
- **Özet logdaki hata satırı tıklanabilir (C6):** özet modda bir proje **hata** satırına tıklanınca → ilgili **kart seçilir + listede focus/scroll + detay log** açılır (karta tıklamayla birebir aynı sonuç). Hatadan tek tıkla projenin tam çıktısına gidilir.
- **Auto-follow:** yeni log geldikçe en alta kayar; kullanıcı scroll yapınca durur; ~2 sn idle'da devam (her hareket sayacı sıfırlar).

### Paralel derleme görünürlüğü (build frontier — TEKİL carousel YOK)
- Paralel çoklu derlemede aynı anda **N proje** Building olur → "tek aktif kart" varsayımı **geçersizdir**. Tekil carousel / iOS-takvim / "öne gelir döner" konsepti **kullanılmaz**.
- **Aktif bant (build frontier):** liste build order'a göre sıralı; o an Building olan **tüm** kartlar birden canlı (pulse + shimmer). Auto-scroll tek karta değil **aktif grubun ağırlık merkezine** yumuşak takip eder; grup ekrandayken kaydırmaz. (Scheduler sıra-koruyan olduğundan frontier listenin üstünden aşağı düzenli ilerler — §6.)
- **Sticky "şu an derleniyor (N)" şeridi:** üstte sabit; o anki paralel Building setini küçük çiplerle gösterir → kullanıcı nereye scroll ederse etsin canlı set görünür. Çipe tıkla → ilgili karta git.
- **Auto-scroll duraklatma:** manuel scroll'da grup takibi durur, ~2 sn idle'da devam (her hareket sayacı sıfırlar). Eski kodun "her ProjectStarted'da zorla en üste atlama" deseni yerine.

### Per-card durum animasyonları (paralel-uyumlu; UI'ı KİLİTLEMEZ)
- Frontier/state animasyonları yalnız `RenderTransform` + `Opacity` — **Width/Height/Margin YOK** (eski accent-strip Width 6→13 ihlali frontier'da düzeltilir; seçim efektindeki accent kalınlaşması ayrı ve tekildir, yukarıda). Liste **UI virtualization** (VirtualizingStackPanel; `CanContentScroll`/`IsVirtualizing` yanlışlıkla kapatılmaz). 500–1000 kartta akıcı.
- Her kartın kendi state animasyonu: **Building** = pulse border + shimmer; **Failed** = kısa shake; **Succeeded** = glow; **Skipped** = fade + **desaturate**; **Cycle** = rozet. (Bunlar kartın kendi state'i olduğundan paralel-uyumlu.)
- **Animasyonlar her zaman açık** (ReducedMotion KALDIRILDI — v4). Standart animasyon süreçleri (bant takibi + per-card efektler + seçim efekti + popup'lar) varsayılan ve tek davranıştır; ayar yok.

### Kısayollar & global hotkey (v4 ek · N6)
- **Çift-Shift** → branch hızlı arama ekranı (aranabilir popup'a odak; §7 branch chip popup'ı ile aynı liste).
- **Ctrl+P** → proje/kök dizin seçici (root seçme).
- **Ctrl+B** → Build, **Ctrl+R** → Rebuild (pencere açıkken; Build çalışıyorsa Stop'a düşer).
- **Global hotkey** (varsayılan ör. **Alt+B**, ayarlardan değiştirilebilir) → uygulama tray'deyken bile sistem genelinde tetiklenir; pencere **sağ-alt köşeden animasyonla** çıkar/restore olur (maximize/normal). Win32 `RegisterHotKey` ile; tray ile entegre (§3.7).
- Kısayollar v1'de sabit varsayılanlarla gelir; özelleştirme ayarlardan (ileride genişletilebilir).

### Tasarım niyeti (v4 ek · N4)
- Bu plan, **tasarım niyetini** taşır: tek pencere düzeni, **dark/modern tema**, custom title bar, kart accent + seçim efekti, console özet/detay, build frontier, chip selector, animasyon dili. **Kesin görsel tasarım** (renk paleti, tipografi, ikonografi, mockup) bu notlardan **ayrıca üretilecek** (kullanıcı prompt'layıp çizdirecek); planın kapsamı "nasıl davranacağı + niyet", görsel comp değil.

---

## 8. Test Stratejisi (§10 — process testleri artık first-class)

- **Unit (Core):** graph extraction, topolojik sıra, cycle tespiti, dosya→proje eşleme, **incremental kararı (global imza + commit-delta tetikleyici izole)**, **branch-bounce senaryosu (developer→X→developer doğru rebuild)**, Safe/Fast propagation, scanner ignore-dir, **sıra-koruyan scheduler dispatch sırası (deterministik)**.
- **Process-control (ZORUNLU — eski kodda HİÇ yoktu):** otomatik test — gerçek/dummy build başlat → **tray'den Exit** / App kill / crash / Stop → **2 sn içinde artık process kalmadığını assert et**; ayrıca **pencere `X` → tray'e küçülür, process ölmez** assert'i. Iteration 0'dan itibaren çalışır.
- **Integration:** çoklu-solution örnek workspace → Sync, Build, Rebuild, branch switch (worktree toggle), Stop, **kart seçimi → proje-detay log akışı (herhangi bir karta tıkla → o projenin logu)**.
- **Performans:** 500+ kart akıcı scroll; cache-hit'te hızlı sync; paralel build ölçülebilir kazanç; **CPU cap'in gerçekten tavanı tuttuğu**; log akışında UI bloklanmaz; **per-proje log buffer'ın bellek tavanını aşmadığı**.

---

## 9. Supervisor ↔ UI Sözleşmesi (§8 — temiz tutulur)

- **Komutlar:** `syncWorkspace(rootPath)`, `reanalyze()`, `listBranches()`, `listWorktrees()`, `selectBranch(branch)`, `startRun(mode, branch, useWorktree, worktreeName?, config, dependentMode, perfMode)`, `setPerfMode(perfMode)` _(çalışan run'a canlı uygulanır)_, `stopRun(runId)`, `getProjectLog(projectId)` _(o projenin tam build çıktısını ister — kart seçiminde lazy)_, `deleteWorktree(name)` _(N3 — worktree'yi siler; branch guard)_, `openPath(projectId)` _(proje dizinini açar)_, `openInVS(projectId)` _(projenin solution'ını açar)_.
- **Eventler:** `syncProgress`, `syncCompleted`, `worktreesListed`, `runStarted`, `projectStarted`, `projectLog` _(ham build çıktısı parçaları — Supervisor buffer'ına da yazılır; UI özet modda göstermez, detay modda/`getProjectLog` ile akar)_, `projectSucceeded` _(+`durationMs`)_, `projectFailed` _(+`durationMs`)_, `projectSkipped` _(+`reason`)_, `runCompleted` _(+`totalDurationMs`, ok/failed/skipped sayıları)_, `runCancelled`, `runBlocked` _(ör. farklı branch + local değişiklik + worktree OFF)_, `error`.
- **Tipler:**
  - `ProjectNode { id, name, projectPath, solutionName, dependencies[], buildOrder, layerIndex?, layerName?, healthy:bool }` — `layerIndex/layerName` katman pattern varsa dolu (yoksa null = global); `healthy` = cycle'a dahil değil (N7).
  - `BuildState { projectId, builtSignature, builtCommit?, lastResult, lastRunAt, lastBranch? }` — **GLOBAL** (projectId anahtarlı); `builtSignature` = materyalize kaynak imzası (**config + HEAD commit + varsa local-diff hash**); `builtCommit` UI'da "şu an hangi commit ile derli" gösterimi için (N10). `lastBranch` yalnız teşhis. Ayrıca global tekil `OutDirConfig` işareti (ortak dizinde şu an hangi config materyalize).
  - `Worktree { name, branch, path, isActive }`
  - `LayerPattern { order:int, regex, name }` — ayarlardan; sıralı liste (N8).
  - `RunRequest { mode:'build'|'rebuild', branch, useWorktree:bool, worktreeName?, config:'Debug'|'Release', dependentMode:'safe'|'fast', perfMode:'full'|'balanced'|'light' }` — katman pattern'leri Supervisor planner'ı config'ten okur.
  - `ProjectResult { projectId, result:'succeeded'|'failed'|'skipped', durationMs, reason?, builtCommit?, targetCommit? }` — `builtCommit/targetCommit` kart gösterimi (N10).
- **Disiplin:** ölü komut / spec dışı event / doğrulanmayan alan eklenmez (eski sözleşme sapmaları tekrarlanmaz). Eklenen her komut/event hem gönderilir hem işlenir. `skipped` event'i planner'ın **gerçek reason'ını** taşır (eski sabit "no source change" yerine). `projectLog` Supervisor buffer'ına yazılır → `getProjectLog` ile **herhangi bir** projenin tam çıktısı geri alınır (sadece sonuncusu değil).

---

## 10. Walking-Skeleton Faz Planı (her iterasyon uçtan uca çalışır + gösterilebilir)

| It. | Teslim | Neden bu sırada |
|---|---|---|
| **0** | İki process + stdio IPC + **nested Job cascade**. Minimal pencere: root seç → dummy/uzun child derleme → canlı log → **çalışan Stop**. §3 kabul testi (pencere `X`→tray; tray Exit/kill → 2 sn'de process kalmaz) **geçer**. DI iskeleti kurulu. | **En kritik risk (process+IPC) en başta kanıtlanır.** |
| **1** | Sync/graph: scan, graph, Tarjan cycle, Kahn topo, cache (açılışta load). Kartlar gerçek projelerle **build-order sırasında** dolar; **bağımlılık sağlık göstergesi (yeşil/cycle-kırmızı)**. | Veri temeli. |
| **2** | **Rebuild** (gerçek, paralel): topo sıra, **sıra-koruyan scheduler**, bağımsızlar paralel `dotnet build`, **per-proje tam çıktı buffer'ı**, özet log (**Sync/Build'de console temizle + granular adımlar**) + **kart seçince proje-detay log**, hata izolasyonu, sayaçlar. | İlk gerçek değer: çalışan derleme + yeni log modeli. |
| **3** | **Incremental Build**: commit/diff/status, **GLOBAL build-state (projectId/imza)**, Safe/Fast, **worktree toggle modeli** (deferred git, otomatik isim, karar matrisi, **local-vs-committed**) + obj izolasyonu, Skipped, **kartta commit gösterimi (built vs target)**, **katman pattern (regex/sert bariyer/katman-içi analiz/Diğerleri katmanı)**. | Projenin asıl zekâsı. |
| **4** | UX polish: **build frontier (aktif bant + sticky "şu an derleniyor" şeridi)**, **kart seçim efekti (accent kalınlaşır + yazı kayar)**, **özet logdaki hata satırı → karta git**, **sağ-alt chip selector (branch / worktree+toggle / perf) + animasyonlu popup'lar**, **aktif worktree listesi + "Sil" + branch guard**, **kısayollar + global hotkey (çift-Shift / Ctrl+P / Ctrl+B/R / Alt+B restore)**, per-card state animasyonları, filtre/Stop morph, **custom dark title bar + logo/app icon**, **tray (X→küçül, Exit→cascade-kill)**, autostart, single-instance, config ekranı (modern dropdown'lar + **katman pattern editörü**; **LogLevel & ReducedMotion YOK**). | Cila + yeni UX modeli. |
| **5** | Perf modları (Full/Balanced/Light = derece + priority + **Job Object CPU rate cap**, canlı değiştirilebilir), 500–1000 kart perf doğrulama, **README**, basit `dotnet publish` paketleme. | Ölçek + dağıtım + dokümantasyon. |

---

## 11. Yapılandırma (config)

- Kök dizin (taranacak).
- Build configuration: **Debug (varsayılan)** / Release. Ortak dizin config-agnostic olduğundan config değiştirince tüm projeler yeniden derlenir (§4/§6).
- Performans modu: **Full Power / Balanced / Light** (paralel derece + process priority + **Job Object CPU rate cap**). **Ana UI'da hızlı seçici** olarak da yer alır (Build yanındaki perf chip); buradaki değer ayarlardaki varsayılandan başlar, **run öncesi/sırasında** değiştirilebilir.
- Worktree varsayılanı: **farklı branch → ON, aktif branch → OFF** (her derlemede toggle ile değiştirilebilir). Worktree havuzu konumu yapılandırılır; havuz **kalıcı**, her worktree **silinebilir** (N3).
- Bağımlı (downstream) modu: **Safe (varsayılan)** / Fast.
- **Katman pattern (layered build · N8):** **sıralı, sınırsız** regex listesi; her satır bir katman (sıra = derleme önceliği), regex **proje adına** eşleşir. Boşsa → **global graf sırası** (katman yok). Eşleşmeyenler implicit son "Diğerleri" katmanında. Katman editörü config ekranında (ekle/sil/sırala).
- **Kısayollar (N6):** varsayılanlar — **çift-Shift** (branch arama), **Ctrl+P** (dizin seçici), **Ctrl+B/Ctrl+R** (Build/Rebuild), **global hotkey** (varsayılan ör. **Alt+B**, restore). Özelleştirilebilir (ileride genişler).
- Cache konumu.
- **Görsel kimlik:** uygulama **logo + icon** (pencere/taskbar/tray), **dark/modern title bar**.
- **KALDIRILANLAR (v4):** **LogLevel** ayarı (console artık seçimle özet/detay) ve **Reduced Motion** ayarı (animasyonlar her zaman açık) config ekranından **çıkarıldı**.

---

## 12. Kapsam Sınırları (v1)

- **İçinde:** tek repo, shell-out derleme, nested-Job process kontrolü (+CPU cap, tray X→küçül / Exit→cascade-kill), sync/graph/cache (build-order listeleme, **bağımlılık sağlık göstergesi**), rebuild + incremental (global build-state, **kartta commit gösterimi**), **sıra-koruyan paralel scheduler**, **katman pattern (layered build)**, worktree toggle modeli (**+ silme & branch guard, local-vs-committed**), tam UI/UX (kart seçim efekti, **seçime bağlı özet/detay console log modeli + Sync/Build temizleme**, build frontier, chip selector, **kısayollar + global hotkey**, dark title bar, logo/icon, animasyonlar her-zaman-açık), config, tray/autostart/single-instance, perf modları, unit+process+integration test, **README**, basit `dotnet publish`.
- **Dışında (sonraya):**
  - **Multi-repo** (onaylandı) — mimari genişletilebilir kurulur.
  - **MSIX/installer/auto-update** — v1'de sadece publish.
  - **WinUI Composition** animasyon güçlendirme — WPF-native yeterli; gerekirse sonra.
  - **Graf dalgası (dependency-flow) görselleştirme** — etkileyici ama virtualized yüzlerce kartta pahalı; v1 sonrası değerlendirilir.
  - **Özel CPU % slider** (3 modun ötesinde elle yüzde) — v1'de 3 mod yeterli; gerekirse sonra.
  - **Headless/CLI** — mimari Supervisor ile hazır, v1'de CLI arayüz geliştirilmez.
  - **Eski-kod bug araştırması (C11/C12)** — silinmiş koda ait; yeni mimaride baştan doğru kurulur, ayrı bir taşıma yapılmaz.
  - **CLAUDE.md çoklu-dosya kurgusu + agent senkron (N2)** — repo dev-tooling; implementation aşamasında ele alınacak, plan kapsamı değil.
  - **Katman pattern "standart dışı durum" gelişmiş çözümü (N8)** — ters/karmaşık katman bağımlılıkları için otomatik çözüm; v1'de yalnız tespit+uyarı, gelişmiş handling sonraya.

---

## 13. Varsayımlar / Varsayılanlar

- Projeler **tek git repo** altında (multi-repo sonraya).
- Ortak çıktı dizini projelerin kendi post-build event'leriyle dolar (orchestrator dokunmaz); config-agnostic tek klasör; "değişti mi" kararı imza = config + commit + local-diff; build-state **global** (projectId).
- Kullanıcı VS'de aynı projeleri Orchestrator ile eşzamanlı derlemez.
- Varsayılanlar: Debug, Safe, worktree (farklı branch'ta ON), Full Power. **Animasyonlar her zaman açık** (ReducedMotion yok). **Console varsayılan = özet modu** (seçim yokken).
- Bağımlılık sırası cache'ten okunur; tam yeniden analiz yalnız Sync ile. Liste ve build, bu **topolojik sırayı** korur.
- **Katman pattern verilirse**, kullanıcı katmanları **bağımlılık sırasına uygun (tek yönlü)** tanımlar; ters katman bağımlılığı beklenmez (tespit edilirse uyarılır, bloklanmaz). Pattern verilmezse global graf sırası geçerli.
- Pencere `X` → tray'e küçülür; uygulama yalnız tray'den **Exit** ile kapanır (build varsa cascade-kill).
- Orchestrator .NET 10 hedefler; derlenen projeler kullanıcının kurulu SDK'sıyla derlenir (TFM bağımsız).

---

## 14. Sıradaki Adım

Bu session'da **iki tur kullanıcı notu** v4'e işlendi (1–10 ilk tur, 11–18 ikinci not seti). **Bu konuşma kapatılıyor; planlama yeni session'dan devam edecek.** Yeni session'da ek notlar gelirse v4 tekrar revize edilir (gerekirse v5) — değilse bu v4 üzerinde mutabık kalınınca **writing-plans** ile Iteration 0 için detaylı, adım adım uygulanabilir implementation plan çıkarılacak.

> **Not:** Yukarısı temiz v4 gövdesidir (dokunulmadı). CEO review'ın tüm kararları, task listesi ve review report'u aşağıdadır.

---

# CEO REVIEW — Kararlar & Çıktılar

**Tarih:** 2026-06-29 · **Skill:** `/plan-ceo-review` · **Mod:** HOLD SCOPE · **Yaklaşım:** risk-first spike · **Verdict:** **CEO CLEARED**

## Özet

CEO review v4 kapsamını **olduğu gibi tuttu** (HOLD SCOPE — kapsam 4 versiyonda bilinçli budanmıştı; risk özellik eksikliğinde değil **doğruluk/robustluk**ta). Tek gerçek tehlike **sessiz yanlış build** ihtimaliydi; üç doğruluk mayını (graf HintPath kör noktası, file→project path-prefix eşlemesi, layered forward-propagation) + ortak bin'in hard-kill'de yırtılması + post-mortem'in imkânsızlığı kapatıldı. Implementation'dan önce **risk-first spike** gelir.

## Karara bağlanan fork'lar

- **Yaklaşım (B):** Risk-first spike, Iteration 0'dan önce — üç doğruluk mayınını GERÇEK repo'da kanıtlar. (Reddedilen alternatif: MSBuild-native `-graph -m`/Traversal SDK; gerekçe = MSBuild incremental timestamp-tabanlı, config-agnostic paylaşımlı OutDir'de güvenilmez. İki model de önerdi, bilinçli reddedildi.)
- **Mod:** HOLD SCOPE (kapsam korundu, sessiz ekleme/çıkarma yok).
- **1A (graf tamlığı · CRITICAL):** Graf sadece `<ProjectReference>` okuyor; OSYS muhtemelen `<Reference HintPath="...\bin\X.dll">` raw-ref kullanıyor. Spike doğrular; varsa **HintPath→üretici-proje resolver** eklenir (DLL-adı→proje haritası). Aksi halde sessiz yanlış-skip + yanlış topo sıra.
- **2A (hard-kill güvenliği · CRITICAL):** Graceful Stop **copy-aware**; `TerminateJobObject` yalnız **proje sınırlarında**, post-build `copy` ortasında asla → ortak bin yırtılmaz, §3 deterministik kill korunur.
- **3C (ters katman dep):** Planda kaldığı gibi **warn + log, bloklamaz** (kullanıcı kararı; katmanların bağımlılık sırasına uygun tanımlandığı varsayımı). **Bilinen davranış:** tetiklenirse compile-error + uyarı rozeti (bkz. T15).
- **4A (log mimarisi · CRITICAL):** Bellek ring buffer yerine **per-run disk log** (`%LOCALAPPDATA%\BuildOrchestrator\logs\run-<ts>\`) + diskten stream + **decision log** (imza girdileri, build/skip reason, exit, süre). Bellek tavanı sorununu da çözer; mimari sadeleşir.
- **Worktree havuzu (cross-model tension):** Outside voice "kalıcı çok-worktree havuzu dev repo'da ölçeklenmeyebilir" dedi; **kullanıcı kalıcı havuzu korudu, ölçek riskini kabul etti.** Mitigation = disk size/GC/cap (T14).

## Outside Voice (bağımsız Claude subagent challenge — 8 bulgu)

> Codex bu makinede yok → bağımsız Claude subagent. Verbatim 8 bulgu, disposition ile:

1. **Job Object breakaway** — `dotnet build` muxer + MSBuild/VS tooling `JOB_OBJECT_LIMIT_BREAKAWAY_OK`/`SILENT_BREAKAWAY` ile inner Job'dan kaçabilir; §3 garantisi asserted-not-proven. → **T1** (spike açıkça breakaway probe eder).
2. **CPU rate cap tüm Job'u throttle eder** — post-build copy/git/IPC de Light'ta starve olur; 2A copy'siyle çelişir. → **T20** (etkileşimi ölç, copy fazına rate floor).
3. **Kalıcı çok-worktree havuzu ölçek riski** — disk GB×N + git metadata bozulması. → kullanıcı kararı: havuz korundu, **T14** mitigation.
4. **Worktree output izole etmez** — worktree build'i de aynı global bin'e yazar; concurrent-VS race §13 varsayımına yaslı. → **T12/T21**.
5. **file→project path-prefix eşlemesi yanlış** — linked/shared/`<Compile Include>` proje-dışı dosyalar → silent stale build. → **T19** (MSBuild-evaluated Compile items).
6. **Layered incremental forward-propagation kırık** — katman-içi analiz cross-layer kenarı atıyor; değişen L1, L3 bağımlılarını dirty yapmaz → stale üst katman. → **T18** (propagation GLOBAL graf üzerinden).
7. **Multiplexed log It-2'den önce kanıtlanmıyor** — It-0 tek dummy child kanıtlıyor, It-2 per-project keyed buffer istiyor. → **T1** (spike concurrent multi-child log handling kanıtlar; 4A ile sadeleşti).
8. **Stratejik: MSBuild zaten yapıyor** — Approach C; daha önce tartılıp reddedildi (timestamp incremental shared OutDir'de güvenilmez).

## Failure Modes Registry

```
CODEPATH                           | FAILURE MODE                    | RESCUED?      | TEST? | USER SEES              | LOGGED?
-----------------------------------|--------------------------------|---------------|-------|------------------------|--------
TerminateJobObject mid-copy        | torn/locked DLL in shared bin   | Y (2A)        | ADD   | nothing (was silent)   | Y (4A)
parallel post-build copy /y        | sharing violation / lock        | ADD: retry    | ADD   | random "Failed"        | Y (4A)
graph ProjectReference-only        | missed HintPath edge→wrong skip | Y (1A)        | ADD   | stale DLL (was silent) | Y (4A)
file->project path-prefix map      | linked/globbed file→wrong skip  | Y (T19)       | ADD   | stale DLL (was silent) | Y (4A)
layered intra-layer propagation    | L1 change→L3 not dirty          | Y (T18)       | ADD   | stale upper layer      | Y (4A)
Supervisor crash mid-run           | App hangs on dead pipe          | ADD: detect   | ADD   | "engine died, restart" | Y
IPC oversized/malformed JSON line  | stream desync, frozen UI        | ADD: framing  | ADD   | (was silent freeze)    | Y
dotnet build breakaway from Job    | orphan process survives         | spike asserts | YES   | (must be impossible)   | Y
git worktree add fails             | run can't start                 | Y (error evt) | ADD   | error event            | Y
getProjectLog after buffer evict   | N/A (4A: from disk)             | Y (4A)        | ADD   | full log from disk     | Y
detached HEAD / no commits         | undefined signature             | ADD: warn     | ADD   | "treating as dirty"    | Y
zero projects after Sync           | empty card list, no feedback    | ADD: empty st | ADD   | "no projects found"    | Y
single project compile error       | (by design) isolated            | Y             | Y     | "Failed" + log         | Y
config Debug<->Release switch       | all-dirty rebuild               | Y             | ADD   | "config changed" log   | Y
layer reverse-dependency           | hard compile error (3C: warn)   | N (user call) | ADD   | compile err + warn badge| Y
```

Silent-unrescued kalan **yok** (1A/2A/4A/T18/T19 kapattı). Tek bilinçli unrescued = layer reverse-dep (3C, kullanıcı kararı, görünür compile-error).

## Implementation Tasks (T1–T21 · `writing-plans` girdisi)

```
P1 (spike / Iteration 0-2 correctness):
- [ ] T1  Spike: nested-Job cascade kill GERÇEK dotnet build ağacına karşı (MSBuild node + VBCSCompiler);
          JOB_OBJECT breakaway/escapee açıkça probe; concurrent multi-child log multiplexing kanıtla. (≤2s, 0 orphan)
- [ ] T2  Spike: GERÇEK OSYS repo'da HintPath/raw <Reference> kenarları + linked/globbed Compile item ölç. (T3/T19 gate)
- [ ] T3  Graph: HintPath→üretici-proje resolver (output-DLL-name→proje); inferred edge'leri topo+incremental'a ekle. (1A)
- [ ] T4  Stop: copy-aware graceful; hard-kill yalnız proje sınırında, copy ortasında asla. (2A)
- [ ] T5  Logs: per-run disk project log + decision log; kart seçince diskten stream. (4A)
- [ ] T6  Supervisor crash recovery: App child handle izler → error + restart.
- [ ] T7  IPC framing: length-prefixed (veya escaped + max-line guard) NDJSON.
- [ ] T8  Parallel copy: retry-on-sharing-violation + backoff; contention ölç.
- [ ] T9  Test: kill mid-parallel-build → shared bin'de torn DLL yok + leftover process yok (2am-Friday testi).
- [ ] T18 Layered incremental: downstream dirty-propagation GLOBAL graf üzerinden (katman yalnız dispatch sırası). [outside-voice #6]
- [ ] T19 file→project mapping: MSBuild-evaluated Compile item'larından (path-prefix değil). [outside-voice #5]

P2 (aynı branch, robustluk):
- [ ] T10 Empty/error UI state: 0 proje, 0 branch, all-skipped run, popup'ta git-list hatası.
- [ ] T11 Edge input: detached HEAD / no-commits / shallow repo → treat-as-dirty + warn.
- [ ] T12 Mid-run lock: Building sırasında branch/config/worktree selector'larını kilitle.
- [ ] T13 Path sanitization: worktree + branch isimleri (.., reserved, drive).
- [ ] T14 Worktree pool: UI'da per-worktree disk boyutu + configurable cap / LRU prune. [outside-voice #3 mitigation]
- [ ] T20 CPU-cap × post-build copy/git/IPC etkileşimini ölç; copy fazına rate floor gerekirse. [outside-voice #2]
- [ ] T21 Doc+guard: worktree build'i aynı shared bin'e yazar (output izole değil); concurrent-VS varsayımı explicit. [outside-voice #4]
- [ ] T15 Doc: layer reverse-dep detect+warn-only (3C) bilinen davranış + compile-error semptomu.

P3 (follow-up):
- [ ] T16 Autostart temiz Idle açar; exe değiştirmeden önce tam exit notu.
- [ ] T17 Trust-boundary doc: root dizin VS'de açılmış gibi güvenilir (arbitrary MSBuild exec).
```

---

# ENG REVIEW — Kararlar & Çıktılar

**Tarih:** 2026-06-29 10:48 · **Skill:** `/plan-eng-review` · **Mod:** FULL_REVIEW · **Verdict:** **CEO + ENG CLEARED** (Iteration -1 Feasibility Spike şartıyla)

## Özet

Eng review **HOW**'u kilitledi. 4 review section + outside voice çalıştırıldı. İçeride 9 mimari/test/perf bulgu (A1–A5, CQ1–CQ2, D8–D9) çözülüp plana işlendi. Asıl olay **outside voice'tan** geldi: bağımsız Claude subagent gerçek hedef repo'yu (`D:\Projects\Delta\OSYS`) **açıp doğruladı** ve planın §0 belkemiğinin **yanlış varsayıma** dayandığını kanıtladı (legacy .NET Framework + absolute-path HintPath-into-shared-bin). Bu, CEO/eng review'ın test etmediği 4 **cross-model reversal** üretti (engine, graf, worktree-ifadesi, sıralama); hepsi kullanıcı onayıyla karara bağlandı. **Çözülmemiş karar yok.** Implementation, **Iteration -1 Feasibility Spike** (T23) üç yes/no'yu kanıtladıktan sonra başlar.

## Doğrulanan Yer Gerçeği (verified — `D:\Projects\Delta\OSYS`, komutla teyit edildi)

```
csproj: 191 · sln: 45 · packages.config: 21
SDK-style (Project Sdk=): 16    |    legacy (TargetFrameworkVersion): 175  (152×v4.6 + 23×v4.8)
ProjectReference dosyası: 122   |    HintPath satırı: 1927   |   PostBuildEvent: 178

<HintPath>C:\OSYS\Server\Bin\OSYS.Base.dll</HintPath>      ← absolute, shared bin
<PostBuildEvent>copy /y "$(TargetDir)$(TargetName).*" "c:\OSYS\Client\bin\"</PostBuildEvent>
```

**Sonuç:** Gerçek dependency sinyali ProjectReference değil, **HintPath-into-shared-bin**; projeler **legacy Framework**; çıktı **absolute** ortak bin'e gider. Bu, build engine + graf + incremental + worktree'yi aynı anda etkiler.

## Kararlar (D1–D13 · tümü kullanıcı onaylı / kullanıcı bana bıraktı)

| # | Bulgu | Karar |
|---|---|---|
| **D1** (A1) | dotnet/MSBuild, Job içinde kendi job'unu kuramayıp patlayabilir (sdk#10150) | T1/spike kabul kriteri sertleşti: **build job İÇİNDE başarılı tamamlanır + breakaway flag GEREKMEZ** (→ T23). |
| **D2** (A2) | build-state.json concurrent yazım atomik değil | Supervisor'da **tek-yazar (serialized) + atomik temp+rename**, her proje bitiminde persist (hard-kill'de ilerleme korunur). |
| **D3** (A3) | Planlama Core mu Supervisor mu (testability) | **Tüm planlama saf Core'da** (`BuildPlan` DTO); Supervisor yalnız planı yürütür. §9 "Supervisor planner" düzeltilir. |
| **D4** (A4) | getProjectLog tek stdio kanalında HOL blocking | **Chunk'lı stream + canlı event'lerle interleave**, tek kanal; **stdout yalnız NDJSON**, Supervisor logging stderr/dosyaya. |
| **D5** (A5) | T3/T19 eval yüzlerce projede Sync'i dakikalarca yavaşlatır | **Tek-geçiş batch MSBuild evaluation + cache** (csproj/props/targets mtime+hash invalidation). |
| **D6** (CQ1) | İmza 3 yerde; kodda drift riski | **Tek Core `BuildSignature.Compute`** (byte-stable, determinism testli); planner + state store aynısını çağırır. |
| **D7** (CQ2) | Shell-out'lar için ortak checked-exec yok | **Tek `ProcessRunner`** (zorunlu exit-code+stderr+timeout). dotnet/msbuild non-zero = `projectFailed`, git/eval fail = `error` event. |
| **D8** | Process-control testi "2sn sonra say" → flaky | **Deterministik bekleme** (handle/IOCP sinyali + timeout tavanı), sleep değil. |
| **D9** | `-nodeReuse:false -UseSharedCompilation=false` build'i 2-3× yavaşlatır; öksüz-garantisi zaten Job'tan | **v1: flag'ler korunur (güvenli).** Spike ölçer: flag'ler kaldırılırsa (reuse açık) Job ≤2sn kill + hız kazancı; kanıtlanırsa **T33 fast-follow** açar. **[EUREKA]** |
| **D10** (OV1) | `dotnet build` legacy Framework + packages.config'i derleyemez | **Engine = `MSBuild.exe` (vswhere) + `nuget restore`/`msbuild -t:restore`**, per project. Nested Job + shell-out + cascade-kill **AYNEN korunur** (sadece exe + restore değişir). |
| **D11** (OV2) | Gerçek graf HintPath→producer; ProjectReference neredeyse boş | Graf **primer = HintPath-basename→producer** (evaluated AssemblyName/TargetName haritası); PR ikincil. Skip yalnız self-source değil **GLOBAL graf (T18) propagation**'a bağlı; imza upstream producer imzalarını katar. T2/T23 match-rate ölçer. |
| **D12** (OV3) | Worktree çıktı-izolasyonu absolute post-build bin yüzünden tutmuyor | **Yeniden çerçevelendi (kullanıcı niyeti):** worktree = **ana checkout'u bozmadan farklı branch derle + çalıştır/test**. Çıktının ortak havuza yazılması **kasıtlı/istenen**. UI etiketi "committed **kaynak**" der ("izole çıktı" demez); T21 concurrent-VS guard + tek-run kilidi korunur. |
| **D13** (OV4) | En pahalı belirsizlik (araç repo'yu derliyor mu) en sona ertelenmiş | **Iteration -1 Feasibility Spike eklendi (gating);** T1 oraya taşındı (dummy değil **gerçek MSBuild ağacı**). |

## EUREKA

> Asıl "öksüz process yok" garantisi **inner Job Object**'ten (`TerminateJobObject`) gelir; `-nodeReuse:false -p:UseSharedCompilation=false` flag'leri redundant "kemer + askı" ve her build'i ~2-3× yavaşlatır. Job zaten askıysa, flag'ler kaldırılıp **hem hız hem güvenlik** elde edilebilir — spike kanıtlarsa (T23/T33). Logged: `~/.gstack/analytics/eureka.jsonl`.

## Gövde Deltaları (OTORİTE — gövdedeki ilgili ifadeleri geçersiz kılar)

- **§0 "Derleme motoru":** `dotnet build` → **`MSBuild.exe`** (VS Build Tools / VS, `vswhere` ile resolve) + packages.config için **`nuget restore`** veya `msbuild -t:restore`. "TFM-bağımsız / kullanıcının SDK'sını birebir kullanır" iddiası **düşer** (legacy Framework full-MSBuild gerektirir). Shell-out / nested Job / §6.1 cascade-kill / VS-parity **değişmeden** kalır.
- **§3.4 flag'leri:** MSBuild.exe sözdiziminde de geçerli. **v1: korunur** (shared compilation + node reuse KAPALI = güvenli, yavaş). **Fast-follow (T33):** spike flag-on kill+hız kanıtlarsa flag'ler kaldırılır.
- **§5 graf:** **Primer = HintPath-basename→producer** (her projenin evaluated AssemblyName/TargetName'i); ProjectReference ikincil ek sinyal. "HintPath opsiyonel enhancement" ifadesi düşer — HintPath artık **temel**. Batch MSBuild eval (D5) AssemblyName + ProjectReference + Compile item'ları tek geçişte okur; **45 sln kökü** üzerinde çalışır.
- **§6 incremental:** İmza tek Core fn (D6) + **transitive upstream producer imzalarını katar**; skip kararı yalnız self-source diff değil, **GLOBAL graf (T18)** ile upstream-değişti propagation'a da bağlı (stale-DLL-link kapanır). build-state single-writer atomic (D2). Worktree D12'ye göre yeniden çerçevelenir.
- **§9 sözleşme:** "Supervisor planner config okur" → **"Core planner `BuildPlan` üretir; Supervisor yürütür"**. `getProjectLog` chunk'lı + interleaved (D4). stdout yalnız NDJSON.
- **§10 sıralama:** **Iteration -1 Feasibility Spike** eklenir (writing-plans + It-0'ı **gate eder**). It-0..5 sıralaması aynı kalır; T1 spike It-1'e taşınır.
- **Solution belirsizliği (outside voice #6):** Bir csproj 0 veya >1 `.sln`'de olabilir; "Visual Studio'da Aç" hangi solution'ı açacağını tanımla (>1 ise seçtir / en yakın); `ProjectNode.solutionName` çok-değerli olabilir.

## Test Eklemeleri (§8 açıkça genişletilir)

§8'e şu testler **açıkça** eklenir (çoğu T-task'larda "ADD" olarak vardı, §8'de yazılı değildi): `BuildSignature` determinism (D6) · build-state atomik/tek-yazar + crash-mid-write resilience (D2) · layer assignment (regex first-match + "Diğerleri") + **T18 cross-layer forward-propagation** · **HintPath→producer resolver + match-rate** (D11/T24) · MSBuild-evaluated Compile items (T19) · **build job İÇİNDE başarılı + no-breakaway** (D1/T23) · getProjectLog chunk interleave → canlı event donmaz (D4) · stdout-IPC desync (stray Console.Out) · **cold Sync süresi 100+ proje + cache-hit** (D5) · T6/T7/T8/T9/T10/T11/T12 · C6 hata-satırı→kart. **Process-control assert'leri deterministik** (D8).

## NOT in scope (eng review eklemeleri; §12 mevcut kalemleri geçerli)

- **Node reuse / shared compilation'ı v1'de açmak** — D9 fast-follow'a (T33) ertelendi; spike kill+hız kanıtlayınca.
- **packages.config → PackageReference migration** — reddedildi (175 legacy projeyi değiştirmek devasa + müşteri repo'sunu değiştirir; kapsam dışı).
- **Worktree gerçek output izolasyonu** — repo absolute post-build bin kullandığından mümkün değil + istenmiyor (D12); kapsam dışı.

## What already exists (reuse analizi)

Greenfield — eski kod silinmiş, **reuse edilecek kod yok**. Tek "mevcut" varlık: önceki plan dokümanları + CEO review (tasarım tabanı). Plan doğru biçimde sıfırdan kuruyor. **Hedef repo (OSYS) bir varlık değil girdi**; gerçeği artık doğrulandı (yukarı).

## Failure Modes — yeni codepath'ler (registry'ye eklenen)

```
CODEPATH                          | FAILURE MODE                | RESCUED? | TEST | SILENT?
BuildSignature iki-nokta drift    | sonsuz rebuild/yanlış skip  | Y (D6)   | ADD  | önlendi
build-state torn write (paralel)  | bozuk imza store            | Y (D2)   | ADD  | önlendi
getProjectLog HOL block           | canlı event/UI donar        | Y (D4)   | ADD  | önlendi
stray Console.Out → IPC desync    | stream desync/frozen UI     | Y (D4+T7)| ADD  | önlendi
MSBuild.exe legacy proje derlemez | engine hiç çalışmaz         | Y (T23)  | YES  | spike kanıtlar
HintPath→producer match miss      | yanlış-skip / stale DLL link| Y (D11)  | ADD  | match-rate ölçülür
worktree çıktı ortak bin'i ezer   | (TASARIM — istenen)         | n/a      | ADD  | kullanıcıya net
```
Silent-unrescued **yeni kritik gap yok** — eng kararları + spike hepsini kapattı.

## Parallelization (worktree lanes)

- **Seam önce:** `Contracts` (IPC DTO/event + `BuildPlan`) küçük, önce sabitlenir.
- **Lane A — Core (pure):** scanner → graph (HintPath→producer) → planner (signature, GLOBAL propagation, layer, scheduler) + unit testler.
- **Lane B — Supervisor:** Job Object + ProcessRunner + MSBuild.exe/nuget invoke + queue + disk log + IPC server.
- **Lane C — App/UI:** MVVM, kart/console/chip/tray/title-bar.
- **Sıra:** **Iteration -1 spike** (gate) → Contracts → A/B/C paralel worktree → her iteration entegre. **Conflict flag:** Contracts değişimi üçüne yayılır (koordine). **Not:** walking-skeleton dikey-dilim baskın; It-0 (process+IPC+Job) önce iner.

## Implementation Tasks (T22–T33 · `writing-plans` girdisi; T1–T21 CEO task'larına ek)

```
P1 (spike / correctness — writing-plans'i gate eder):
- [ ] T23 Iteration -1 Feasibility Spike (GATE): gerçek OSYS'te (a) 5 proje MSBuild.exe+nuget ile uçtan uca derle,
        (b) HintPath→producer match-rate ölç, (c) cascade-kill GERÇEK MSBuild ağacına karşı ≤2sn (T1 buraya taşındı,
        breakaway/escape + build-success probe — D1), (d) D9 flag-on kill+hız delta. 3 yes/no kanıtlanmadan It-0 başlamaz.
- [ ] T22 Engine: dotnet build → MSBuild.exe (vswhere resolve) + nuget restore/msbuild -t:restore, per project;
        nested Job + shell-out + cascade-kill korunur. (D10)
- [ ] T24 Graph: HintPath-basename→producer (evaluated AssemblyName/TargetName); PR ikincil; batch tek-geçiş eval +
        mtime/hash cache; 45 sln kökü. (D11/D5)
- [ ] T25 Signature+propagation: tek Core BuildSignature; transitive upstream producer imzası; skip GLOBAL graf
        (T18) ile gate (self-source değil). (D11/D6)
- [ ] T26 Planning Core'da: BuildPlan DTO; Supervisor sadece yürütür; §9 wording fix. (D3)
- [ ] T27 build-state: single-writer serialized + atomik temp+rename, per-project persist + crash test. (D2)
- [ ] T28 IPC: getProjectLog chunk+interleave; stdout NDJSON-only, logging stderr/dosya. (D4)

P2 (aynı branch, robustluk):
- [ ] T29 Worktree wording/label dürüstlüğü: kaynak-izole / çıktı-ortak (tasarım); T21 guard + tek-run kilidi. (D12)
- [ ] T30 ProcessRunner tek helper: zorunlu exit-code+stderr; non-zero build = projectFailed semantiği. (D7)
- [ ] T31 Process-control testleri deterministik (handle/IOCP + timeout tavanı), sleep yok. (D8)
- [ ] T32 Solution belirsizliği: csproj 0/>1 sln davranışı + Open-in-VS seçimi; eval 45 sln kökü. (outside voice #6)

P3 (follow-up):
- [ ] T33 D9 fast-follow: spike Job-kill'i kanıtlarsa node reuse + shared compilation'ı aç (flag'leri kaldır).
```

**CEO task'larıyla ilişki:** T1 → T23'e absorbe (gerçek MSBuild ağacı). T3 → T24 ile yeniden çerçevelendi (HintPath-basename→producer). T18 → T25 ile pekiştirildi (skip GLOBAL gate). T7 → T28 ile genişletildi (stdout disiplini). T19/T2 → T24'le birleşik. T21 → D12 ile netleşti.

## Unresolved decisions

Yok — D1–D13'ün hepsi yanıtlandı.

---

# DESIGN REVIEW — Kararlar & Çıktılar

**Tarih:** 2026-06-29 11:17 · **Skill:** `/plan-design-review` · **Sınıf:** **App UI** (data-dense developer tool) · **Mod:** FULL_REVIEW (7 pass) · **Outside voice:** bağımsız Claude design subagent (Codex bu makinede yok) · **Verdict:** **DESIGN CLEARED** · **Skor:** 5/10 → 9/10
**Görsel durum:** Renk/tipografi/ikon **değerleri** kullanıcıda (N4 — Claude Design). Bu review tasarım **NİYETİ + etkileşim modeli + state'ler + motion davranışı + token rolleri**ni sabitler; pixel comp üretmez.

## Özet

§7 bir **özellik listesi** olarak yazılmıştı, **tasarlanmış bir deneyim** olarak değil. Kullanıcının yeni 3-katman console fikri (ana console + özet feed + typing imleç satırı) planın en heyecanlı **ve** en riskli fikriydi: yanlış kurulursa tam da bu aracın var olma sebebi olan yük altında (N proje aynı anda biter) **olmuş biteni okumak için kullanıcıyı bekletir**. 7 pass + outside voice ortak sonucu net: **bu kategoride zarafet/heyecan "daha fazla animasyon"dan değil, karmaşa altında okunabilirlikten gelir.** 4 fork kullanıcı onayıyla, gerisi craft kararıyla çözüldü. Çözülmemiş tasarım kararı yok.

## Cross-cutting reframe (NORTH-STAR — tüm UI bu cümleye uyar)

> Bu aracın zarif/profesyonel/heyecanlı versiyonu, **gerçekten karmaşık bir işi yaparken sakin ve okunur kalan** versiyondur. Heyecan, **dependency-order build-frontier'ın listede aşağı yürümesinden** gelir (signature görsel — hiçbir mainstream araç bunu göstermez); typing **yalnız sakin anlarda**; hatalar **dramasız ama anında**. Bu kategoride **restraint = farklılaşma.** Outside voice'un cümlesi: "more motion ≠ more polish — invert it."

## Pass puanları (önce → sonra)

| Pass | Önce | Sonra | Ana düzeltme |
|---|---|---|---|
| 1 · Information Architecture | 6 | 9 | Attention order sabitlendi; sağ pane 3→2 yapısal zone; idle state; global progress; şerit(mekânsal) vs feed(zamansal) ayrımı |
| 2 · Interaction States | 3 | 9 | Tam state tablosu; empty/idle/all-skipped/partial/engine-died **tasarlandı** |
| 3 · User Journey | 5 | 9 | Onboarding; sync reveal; pre-build confirm; failure orchestration; success flourish; **global progress/ETA** |
| 4 · AI Slop | 6 | 9 | Anti-slop direction: glyph≠emoji, gerçek font+mono, restrained radius, accent bilgi taşır, kart=dense row |
| 5 · Design System | 3 | 8 | Semantic token-intent (DESIGN.md tohumu): renk/tipografi/spacing/motion rolleri |
| 6 · Responsive & A11y | 3 | 9 | Pencere min/DPI/truncation; keyboard nav; SR live-region; kontrast tabanı; **OS reduced-motion** |
| 7 · Unresolved | — | 0 açık | 4 fork kullanıcı-onaylı + 10 "implementer'ı kovalayan" belirsizlik çözüldü |

## Kararlar (DD1–DD14)

| # | Karar | Kaynak |
|---|---|---|
| **DD1** | **3 yapısal değil 2 yapısal zone.** Sağ pane = (üstte) **ANA CONSOLE** + (altta) **KALICI ÖZET STREAM**. Kullanıcının istediği "3-katman his" (console / feed-geçmişi / animasyonlu en-yeni) korunur ama live-line **ayrı bölmeli üçüncü pane değil**, stream'in **aktif alt satırı**dır (en yeni satır yerinde yazılır → settle olup yukarı kayar). 3 bağımsız animasyonlu bölge dikkati böler. | Fork-1 (kullanıcı: 3-katman, feed console altında) + outside voice §1.3 refinement |
| **DD2** | **Typing = rate-gated lüks, FIFO kuyruk değil.** Live-line yalnız özet stream'in en-yeni satırını yazar; **ham MSBuild detayı ASLA harf-harf yazılmaz.** Net degradation kuralı aşağıda (KRİTİK spec). | Fork-2 (kullanıcı: hız-cap + en-yeniye atla, sadece özet) + outside voice §2 |
| **DD3** | **OS reduced-motion ayarına saygı** (uygulama-içi toggle YOK — sadelik korunur). Windows "animasyonları göster" KAPALIYSA: typing→anlık metin, pulse/shimmer/shake/stagger→anlık renk/fade. Bilgi aynı, motion kalkar. Bu **custom toggle'dan az UI** + erişilebilir + doğru OS vatandaşlığı. **Gövde §7/§11/§13 "ReducedMotion KALDIRILDI / her zaman açık" ifadesini geçersiz kılar.** | Fork-3 (kullanıcı) + outside voice §6.1 (CRITICAL) |
| **DD4** | **Görsel north-star = sakin-hassas dark (Linear/Geist ruhu) + heyecanlı frontier.** near-black yüzeyler, tek soğuk accent, gergin tipografi, minimal chrome, ince micro-interaction; heyecan MOTION'dan (frontier) gelir, gürültüden değil. | Fork-4 (kullanıcı) |
| **DD5** | **Attention order sabit:** ① "ne oluyor" = build-frontier seti + global progress · ② frontier listesi (mekânsal) + özet stream (zamansal) · ③ per-project detay (on-demand). Title bar + sayaçlar = **chrome, content değil**. Hiyerarşi renkle değil **ağırlıkla** (boyut/kontrast/konum) kurulur. | outside voice §1.1 |
| **DD6** | **Global progress / ETA eklenir** (planda HİÇ yoktu): "Building 8/120 · 1m04s · ~40s kaldı" — frontier/header'da ince determinate affordance. Çok-dakikalı build'de izleyenin #1 ihtiyacı. | outside voice §4.4 (HIGH) |
| **DD7** | **Şerit vs feed = ikisi de kalır, net ayrışır.** Sticky "şu an derleniyor (N)" şeridi = **mekânsal canlı set** (çip→karta git, **statik metin günceller, animasyon DEĞİL**); özet stream = **zamansal akış**. | Pass 1 + outside voice §1.4 |
| **DD8** | **Tek canonical click→detay jesti:** özet stream'de **herhangi bir satıra düz tıklama** = o projeyi seç + detay + Back (kart tıklamasıyla ve C6 hata-satırıyla tutarlı). **Ctrl+click kaldırıldı** (affordance sıfır). Ham console'da **metin seçimi kutsal** → "console'a tıkla=seçim kalkar" **kaldırıldı**; detay'dan çıkış **görünür Back butonu** (canonical) + "seçili karta tekrar tıkla" (bonus). | Fork-2 prose + outside voice §5.1/§5.2/§8.4 |
| **DD9** | **Motion budget: aynı anda en fazla 1 hero motion.** Aktif build'de hero = **frontier kartları**; typing burst'te susar (DD2); sticky şerit statik (DD7). Yalnız **viewport'taki** kartlar anime olur; settled state'ler (Succeeded/Skipped) **statik** (sonsuz glow yok); kart başına aynı anda tek motion tipi; sadece `RenderTransform`+`Opacity`. | outside voice §1.4/§6.2 |
| **DD10** | **All-skipped = signature delight** (gri/başarısızlık gibi DEĞİL): "Her şey güncel — 120 proje 0.4sn'de kontrol edildi, derlenecek yok" + güvenli success affect. Incremental aracın en sık başarılı çıktısı; sahiplen. | Pass 2 + outside voice §3.4 (HIGH) |
| **DD11** | **Failure findable-in-one-action:** hata anında stream **anında** anons (typing'i atlar); run boyunca **scroll'a dayanıklı, kapatılabilir** "N hata: `<proje>` — [logu aç]" affordance'ı; "✗ Failed" filtre çipi öne çıkar. Shake yalnız ikincil ipucu. | Pass 3 + outside voice §4.5 (HIGH) |
| **DD12** | **X-to-tray ilk seferinde toast** ("Build Orchestrator tepside çalışıyor; çıkmak için tray ikonuna sağ-tık → Exit"); sonra sessiz. En güçlü OS konvansiyonunu (X=kapat) sessiz bozmak bug gibi okunur. | outside voice §5.5 (HIGH) |
| **DD13** | **Worktree chip = iki sinyal ayrışır:** toggle durumu (ON/OFF) görsel net + "açılır" caret'i; aktif mod ("local dahil"/"committed temiz") **Build yanında glanceable** (yalnız chip içinde gizli değil) — çünkü §6 matrisi run'ı **bloklayabilir**, yüksek-bahisli. | outside voice §5.4/§5.6 |
| **DD14** | **Sync reveal + success flourish + pre-build confirm** (hepsi reduce-motion aware): kartlar build-order'da yukarıdan aşağı **staggered fade-in (≤400ms toplam)** → topolojik sıralama görünür; temiz full-success'te Done satırında **tek** settle/glow + frontier sakin-yeşil (bir kez); in-place branch değiştiren build öncesi (OFF/≠Active) **tek satır sakin confirm**. | outside voice §4.2/§4.3/§4.6 |

## Gövde Deltaları (OTORİTE — §7/§11/§13'teki ilgili ifadeleri geçersiz kılar)

- **§7 Console/Log modeli (satır 237-249):** "özet modu XOR detay modu, tek console, kart seçimiyle toggle" → **yeniden kurgu (DD1):** sağ pane **2 yapısal zone** — (üst) **ANA CONSOLE**: seçim yok = run narrative / granular adımlar (idle: blink cursor + "ready"); kart seçili = o projenin tam MSBuild çıktısı + **[← Back]**. (alt) **KALICI ÖZET STREAM**: kronolojik tek-satır olaylar, **her zaman görünür** (kart seçince özeti kaybetme sorunu çözülür); en-yeni satır = **aktif typing satırı** (DD2 kuralıyla). Yatay GridSplitter; min-height'lar; kısa pencerede stream → yalnız aktif satıra çöker.
- **§7 "console'a tıkla → seçim kalkar":** **kaldırıldı** (DD8 — metin seçimi kutsal). Detay'dan çıkış = görünür **Back** butonu.
- **§7 özet logdaki hata satırı tıklanabilir (C6):** genelleşti → **stream'deki HER satır** düz-tıkla = detay (DD8).
- **§7 Build frontier:** sticky "şu an derleniyor (N)" şeridi **statik metin günceller** (animasyon değil, DD7/DD9); auto-scroll frontier'ı izler ama **center-of-gravity** net tanımlı (yo-yo yasak, DD/aşağı belirsizlik #6).
- **§7 Per-card animasyonlar:** **motion budget** uygulanır (DD9 — viewport-only, settled=statik, kart başına tek motion).
- **§7/§11/§13 "ReducedMotion KALDIRILDI / animasyonlar her zaman açık":** → **DD3:** uygulama-içi toggle yok ama **OS reduced-motion'a saygı** (otomatik sade-mod). "Her zaman açık" ifadesi düşer.
- **§7 Kısayollar:** liste **ok tuşları / Enter=log / Esc=back-deselect / focus-visible ring** eklenir (DD/Pass 6); çift-Shift chip tooltip'inde duyurulur.
- **§7 Tasarım niyeti (N4):** korunur + **north-star (DD4) + token-intent (aşağı) + anti-slop direction** ile zenginleşir; Claude Design işine net brief olur.
- **§10 It-4 (UX polish):** design task'ları (T34–T49) bu iterasyona düşer; **Iteration -1 Feasibility Spike (T23) etkilenmez** (görsel değil feasibility).

## Reconciled Information Architecture (§7 OTORİTE)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [◆]  OSYS · main                                          — □ ×   (dark chrome) │  chrome (content değil)
├──────────────────────────────────────────────────────────────────────────────┤
│ ▸ Building 8/120 · 1m04s · ~40s kaldı   [Client.Core][Server.Api][Auth.Core]…  │ ① canlı set + GLOBAL progress (DD6/DD7)
├───────────────────────────────────┬──────────────────────────────────────────┤
│  PROJECT LIST (build-ordered,     │  ANA CONSOLE (rol = seçime göre)          │
│  virtualized)        ② frontier   │  • seçim yok: run narrative/granular;     │
│  ▌ Client.Core          ✓ 2.3s    │    idle: blink cursor + "ready"           │
│  ▌ Server.Api        ⟳ building 1.1s│  • kart seçili: tam MSBuild çıktısı       │ ③ detay (on-demand)
│  ▌ Auth.Core         ⟳ building 0.4s│                            [← Back]      │
│  ▌ Common.Utils         ↷ skipped │                                          │
│  …(500–1000, viewport-only motion)├──────────────────────────────────────────┤
│                                ═══│  ÖZET STREAM (kalıcı · zamansal)          │
│  (GridSplitter)                   │  ✓ Client.Core built · 2.3s               │ satırlar düz-tıkla→detay (DD8)
│                                   │  ↷ Common.Utils skipped · no change       │
│                                   │  ▌ Server.Api building…█  ← AKTİF satır   │ en-yeni = typing (yalnız sakin, DD2)
├───────────────────────────────────┴──────────────────────────────────────────┤
│ ⟳Sync  Σ120 ●98 ✓96 ✗2 ↷22       main ▾   ⌥committed temiz ▾   ⚡Balanced  Build ▸│ aksiyon + bağlam (DD13)
└──────────────────────────────────────────────────────────────────────────────┘
```

**Attention order (DD5):** ① ne oluyor (frontier seti + global progress) → ② frontier listesi (mekânsal) + özet stream (zamansal) → ③ per-project detay. Title bar repo·branch = wayfinding (Krug "hangi sayfadayım"). Default split ~ %46/%54 (liste/console), GridSplitter konumu persist.

## Typing / Live-line degradation kuralı (KRİTİK — net spec, DD2)

Naif FIFO kuyruk **ship-broken**'dır: `Server.Api succeeded` 28. harfini yazarken gelen `Client.Core FAILED` kuyruğa girer → hata, bir başarının animasyonu arkasında gecikir. **Bir hata asla bir başarının animasyonuna gate edilemez.** Kural:

1. **Drop-to-latest, kuyruk değil:** typing sürerken yeni olay gelirse → mevcut satırı **anında final metnine settle et**, aradaki satırları **anında** (animasyonsuz) ekle, yalnız **tek en-yeni** satırı yazmaya başla.
2. **Throughput eşiği:** olay hızı ~3-4 satır/sn'yi aşarsa (layer barrier biter, 30 proje 200ms'de rapor eder) typing **tamamen askıya alınır** → satırlar anında eklenir; stream ~400ms **sessizleşince** typing geri döner. Yani imleç **fırtınada susar, sakinde yazar**.
3. **Hatalar typing'i her zaman atlar:** failed satır **anında** render + statik vurgu. Hata anlatılmaz, **anons edilir**.
4. **Hız cap:** uzunluktan bağımsız bir satır asla ~250ms'den yavaş yazılmaz (uzun satır chunk'lar / snap eder).
5. **İmleç her zaman blink eder** (engine canlı / sıradaki olayı bekliyor göstergesi) — **typing** ise nadir, hak edilmiş, sakin an. AI-chat hissi korunur, latency patolojisi gider.

## Interaction State Tablosu (§8'e test, §7'ye UI — Pass 2)

```
ÖĞE             | LOADING                  | EMPTY / IDLE                         | ERROR                        | SUCCESS                          | PARTIAL
----------------|--------------------------|-------------------------------------|------------------------------|----------------------------------|------------------------
Project List    | sync: skeleton shimmer   | ilk açılış: "Başlamak için repo seç"| "kök altında proje yok —     | build-order kartlar (reveal DD14)| cycle-kırmızı rozetler
                |   satırlar               |   + [Klasör Seç] (Ctrl+P home)      |   yolu/ignore kontrol et"    |                                  |   karışık
Ana Console     | —                        | "Ready" + blink cursor              | engine-died banner: "Build   | run narrative / detay            | —
                |                          |                                     |   engine durdu [Restart]"    |                                  |
Özet Stream     | —                        | "Henüz yok — build hikâyen burada"  | kırmızı satır (tıkla→log)    | tam hikâye + Done satırı         | Done hata-önce (DD11)
Live-line       | —                        | "▌" idle blink                      | "▌ engine error"             | son olay                         | —
Sync            | spinner + granular adım  | —                                   | console'da error + toast     | sayaçlar dolar                   | —
Build butonu    | → Stop morph + indeterm. | 0 proje → disabled (tooltip "önce   | runBlocked banner (worktree  | Done özeti + flourish (DD14)     | "118 ok, 2 failed" +
                |                          |   Sync")                            |   conflict) → aksiyonlar     |                                  |   [ilk hataya git]
Branch popup    | "branch'ler yükleniyor"  | "branch yok"                        | "git failed [Retry]" inline  | aranabilir liste                 | —
All-skipped run | —                        | —                                   | —                            | **DELIGHT (DD10):** "Her şey     | —
                |                          |                                     |                              |   güncel — N proje Xsn'de"       |
```

Empty state'ler **feature**'dır: her biri **sıcaklık + tek primary action + bağlam** taşır; asla boş void değil.

## User Journey / Emotional beats (Pass 3)

```
ADIM                | KULLANICI HİSSİ        | PLAN NE SAĞLAR (DD)
--------------------|------------------------|----------------------------------------------
İlk açılış          | "ne yapmalıyım?"       | onboarding empty state — tek davet (DD/T37)
Sync                | "hepsini buldu!"       | granular adımlar + staggered reveal topo-sort (DD14)
Grafı görme         | gurur ("tüm sistemim") | build-order liste + health dot
Branch seç → Build  | kontrol / (kaygı?)     | pre-build in-place confirm strip (DD14)
Paralel build izleme| HEYECAN (tepe nokta)   | frontier hero + global progress/ETA + sakin stream (DD5/DD6/DD9)
Bir hata            | endişe ama kurtarılır  | anında anons + scroll-proof "logu aç" (DD11)
Başarı              | tatmin / rahatlama     | Done + tek settle/glow + frontier sakin-yeşil (DD14)
Incremental (ertesi)| "hızlı! akıllı"        | all-skipped delight (DD10)
```

Tepe nokta = paralel build izleme; **heyecan burada, restraint ile.** İlk-açılış, başarı çözülüşü, hata-kurtarma artık tasarlı.

## Anti-slop Design Direction (Pass 4 — App UI kuralları)

- **Glyph ≠ emoji:** ✓ ✗ ↷ ⟳ durum işaretleri **gerçek ikon seti / font glyph** (OS renkli emoji DEĞİL — platformda tutarsız + amatör).
- **Font (yön, değer sende):** UI = gerçek grotesk (Inter bile artık "default" listesinde → Geist / IBM Plex Sans gibi karakterli bir seç); **console = gerçek monospace** (JetBrains Mono / Geist Mono / Cascadia — `Consolas`-default değil). Console **mutlaka mono**; mono seçimi aracın ruhu.
- **Accent şerit (slop #8 riski):** burada **statü kodlar** (bilgi) + seçim affordance → hak ediyor; ikinci **dekoratif** renkli border ekleme.
- **Radius/shadow disiplini:** her şeye aynı şişik radius = oyuncak; console keskin (0), kontroller hafif/tutarlı küçük radius; **dekoratif gölge yok** (litmus #7: gölgeler kalkınca premium kalmalı).
- **Kart = dense LİSTE SATIRI**, mosaic/marketing-card değil: proje adı primary, solution adı dim, glyph+süre tertiary; kalın border/shadow yok; ince separator.
- **Distinctiveness (outside voice §7.2):** (a) **dependency-order frontier = kimlik** — listede sırayla yürüyen frontier'ı kutla (screenshot'lanacak şey); (b) **stream = sakin anlatıcı**, ham dump değil — tek-satır metinleri bir insan gibi yaz (tutarlı fiiller, nokta ile bit: "Common skipped — no changes."); (c) **all-skipped anı**nı sahiplen; (d) **restraint = farklılaşma** (meşgul kategoride sakin araç).

## Design North-Star / Token-Intent (DESIGN.md tohumu — Pass 5; değerler N4'te kullanıcıda)

- **Renk rolleri (semantic):** `surface` / `surface-raised` (kart, popup) / `console-bg` (en derin) / `text-primary` / `text-dim` / `border-subtle` / **tek `accent`** + **status paleti**: `success` / `fail` / `building(active)` / `skipped(muted)` / `cycle(warn)`. App UI: **az renk.** Status **renk + glyph + metin** (colorblind-safe).
- **Tipografi rolleri:** `display` (title/başlık) · `ui` (kart/chip) · `mono` (console + süre + commit SHA). Etkin kural: 2 aile + mono.
- **Ölçekler:** spacing scale · küçük/tutarlı radius scale · **minimal** elevation (App UI: minimal chrome).
- **Motion token'ları (paylaşılan dil = zarafet):** süre + easing → `selection` (anlık/hızlı) · `frontier-pulse` · `stream-settle` · `typing` (cap'li, DD2) · `popup` (RenderTransform+Opacity). Hepsi **reduce-motion** altında anlık'a düşer (DD3).

## Responsive & Accessibility (Pass 6)

- **Pencere/DPI:** min window size + zone min-height (kısa pencerede stream→aktif satıra çöker); GridSplitter konumları persist; per-monitor DPI 125/150/175%.
- **Uzun isim/path (47 char):** kart + stream satırı **ellipsis + tooltip**.
- **Keyboard-first (a11y + pro-tool ruhu):** liste **ok tuşları**, **Enter**=log aç, **Esc**=back/deselect, **focus-visible** ring; mevcut global kısayollar korunur.
- **Screen reader:** kart `AutomationProperties` (ad + statü); özet stream = **live-region** ama **harf-harf DEĞİL** → tamamlanan satırı **bir kez** anons (typing SR'ı spam'lemez).
- **Kontrast:** "skipped = soluk" tuzağı → soluk/dim metin yine **≥4.5:1**; status renkleri console-bg üstünde ≥4.5:1.
- **Motion:** OS reduced-motion'a saygı (DD3); fotosensitivite için hızlı shimmer cap'li.

## Çözülen "implementer'ı kovalayan" belirsizlikler (outside voice §8)

1. Typing burst davranışı → **DD2 net spec.** · 2. Zone layout matematiği → **DD1 (2 zone) + min-height/reflow.** · 3. Click jesti çakışması → **DD8 tek canonical tablo.** · 4. Log'da metin seçimi vs deselect → **DD8 (seçim kutsal; çıkış=Back).** · 5. 3 bölgede auto-scroll arbitration → **T48:** user-scroll yalnız o bölgeyi duraklatır; öncelik frontier>console>stream. · 6. Frontier center-of-gravity → **T48** net tanım (yo-yo yasak). · 7. Run sınırları/Done stream'de → granular: her Build console'u temizler (N1), önceki run stream'de **terminator satırıyla** kapanır. · 8. Hâlâ-derlenen projeye tıklama → detay **live-stream** + "still going" göstergesi. · 9. Counter-filter × seçim → filtre seçimi korur; seçili kart filtrelenirse detay açık kalır, liste vurgusu düşer. · 10. Global progress varlığı → **DD6 (var).**

## NOT in scope (design — ertelenen, gerekçeli)

- **Tema/renk değerleri, ikonografi, mockup** → kullanıcı (N4, Claude Design); bu review brief'i sağlar.
- **Uygulama-içi motion/tema toggle'ı** → reddedildi (DD3: OS ayarı yeter, daha az UI).
- **Komut paleti (Raycast-vari) / fuzzy global search** → ilgi çekici ama v1 kapsamı değil; çift-Shift + Ctrl+P yeter.
- **Dependency-flow grafik görselleştirme** → §12'de zaten ertelendi (virtualized yüzlerce kartta pahalı).
- **Onboarding tour / coachmark dizisi** → tek empty-state daveti yeter; tour = sizzle (Krug goodwill).
- **Tema light mode** → v1 dark-only (north-star DD4); sonra.

## What already exists (reuse)

- **DESIGN.md yok** → bu review token-intent tohumunu verir (Pass 5); implementation'da DESIGN.md'ye dönüştürülebilir.
- **Reuse edilecek UI kodu yok** (greenfield; eski kod silinmiş — Eng review teyitli). Tek temel: §7 davranış niyeti (bu review ile revize edildi).
- **Patternler:** İki-pane (index+detay) = kanonik IDE/mail; konvansiyona uyulur (Krug). Custom dark chrome WPF `WindowChrome` ile (gövde §7).

## Design Implementation Tasks (T34–T49 · `writing-plans` girdisi; design-specific)

```
P1 (It-4 UX polish bloklayıcı — yanlış kurulursa deneyim kırılır):
- [ ] T34 Typing/live-line degradation engine: drop-to-latest (FIFO YOK), throughput-suspend (>~3-4 satır/sn→anlık;
        ~400ms idle→resume), failure typing'i atlar (anlık), ~250ms/satır cap, imleç her zaman blink. (DD2)
- [ ] T35 Console/stream 2-zone layout: ana console (idle narrative / detay + Back) + kalıcı özet stream
        (en-yeni=aktif typing satırı, settle-up); yatay splitter + min-height + reflow. (DD1)
- [ ] T36 OS reduced-motion: Windows ayarını oku → typing/pulse/shimmer/shake/stagger anlık'a düşer; in-app toggle yok. (DD3)
- [ ] T37 Interaction state'leri tasarla+kur: pre-first-run, 0-proje, 0-branch/git-fail, all-skipped DELIGHT,
        partial(hata-önce), sync skeleton, engine-died banner. (Pass 2 / DD10)
- [ ] T38 Global progress/ETA: X/N + geçen + kaba kalan; frontier/header'da determinate affordance. (DD6)

P2 (aynı iterasyon, polish):
- [ ] T39 Failure orchestration: anlık anons + scroll-proof kapatılabilir "N hata:[logu aç]" + tek-aksiyon erişim + Failed filtre öne. (DD11)
- [ ] T40 Discoverability: tek canonical click→detay (stream satırı + kart), görünür Back, console-deselect kaldır
        (metin seçimi kutsal), worktree chip iki-sinyal (toggle+caret) + Build yanı glanceable, X-to-tray ilk toast, çift-Shift tooltip. (DD8/DD12/DD13)
- [ ] T41 Motion budget: 1 hero motion; viewport-only kart animasyonu; settled=statik; sticky şerit statik metin. (DD9/DD7)
- [ ] T42 Sync reveal: build-order yukarı→aşağı staggered fade-in ≤400ms (reduce-motion aware). (DD14)
- [ ] T43 Pre-build context confirm: in-place branch switch (OFF/≠Active) öncesi tek-satır sakin confirm. (DD14)
- [ ] T45 Anti-slop enforcement: gerçek glyph seti (emoji değil), gerçek grotesk+mono (system-ui/Consolas-default değil),
        restrained radius/no-deco-shadow, accent statü kodlar, kart=dense row. (Pass 4)
- [ ] T46 Keyboard nav: liste ok/Enter/Esc + focus-visible ring. (Pass 6)
- [ ] T47 Screen reader + kontrast: kart automation name+statü; stream live-region (satır-bir-kez, harf-harf değil); dim/skipped ≥4.5:1. (Pass 6)
- [ ] T48 Auto-scroll arbitration + frontier center-of-gravity net spec (yo-yo yasak; user-scroll bölge-lokal duraklatma). (belirsizlik #5/#6)

P3 (follow-up):
- [ ] T44 Success flourish: Done satırı tek settle/glow + frontier sakin-yeşil (bir kez, reduce-motion aware). (DD14)
- [ ] T49 Token-intent → DESIGN.md: semantic renk/tipografi(display/ui/mono)/spacing/radius/elevation/motion rolleri. (Pass 5)
```

## Completion Summary

```
+====================================================================+
|         DESIGN PLAN REVIEW — COMPLETION SUMMARY                     |
+====================================================================+
| System Audit         | DESIGN.md yok · App UI · greenfield          |
| Step 0               | 7/10 başlangıç (intent güçlü, deneyim eksik) |
| Pass 1 (Info Arch)   | 6 → 9                                         |
| Pass 2 (States)      | 3 → 9                                         |
| Pass 3 (Journey)     | 5 → 9                                         |
| Pass 4 (AI Slop)     | 6 → 9                                         |
| Pass 5 (Design Sys)  | 3 → 8                                         |
| Pass 6 (Responsive)  | 3 → 9                                         |
| Pass 7 (Decisions)   | 4 fork çözüldü + 10 belirsizlik kapandı       |
+--------------------------------------------------------------------+
| NOT in scope         | 6 madde yazıldı                               |
| What already exists  | yazıldı (DESIGN.md yok, greenfield)           |
| Design tasks         | 16 (T34–T49)                                  |
| Decisions made       | 14 (DD1–DD14) plana işlendi                   |
| Decisions deferred   | 0                                             |
| Overall design score | 5/10 → 9/10                                   |
+====================================================================+
```

## Unresolved decisions (design)

Yok — 4 fork kullanıcı onaylı (DD1–DD4), 10 craft kararı (DD5–DD14) gerekçesiyle işlendi.

---

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|--------|---------|-----|------|--------|----------|
| CEO Review | `/plan-ceo-review` | Scope & strategy | 1 | clean | HOLD_SCOPE, 0 critical gap, 5 fork + 7 P1 correctness task (T1–T21) |
| Codex Review | `/codex review` | Independent 2nd opinion | 0 | — | Codex bu makinede yok |
| Eng Review | `/plan-eng-review` | Architecture & tests (required) | 1 | clean | 9 iç bulgu (A1–A5,CQ1–2,D8–9) + **4 cross-model REVERSAL** (engine→MSBuild.exe, graf→HintPath-producer, worktree-wording, Iteration -1 spike); 0 unresolved; 12 yeni task T22–T33 |
| Design Review | `/plan-design-review` | UI/UX gaps | 1 | clean | skor 5/10 → 9/10; 4 fork (console-model, typing-degradation, OS-reduced-motion, north-star) + 10 craft karar (DD1–DD14); 16 design task T34–T49; outside-voice cross-model agreement |
| DX Review | `/plan-devex-review` | Developer experience gaps | 0 | — | — |

- **CROSS-MODEL (eng):** Outside voice (Claude subagent; Codex yok) gerçek OSYS repo'sunu **açıp DOĞRULADI**: 175/191 legacy .NET Framework + 1927 absolute HintPath + 178 absolute post-build copy → §0 engine / §5 graf / §6 worktree temel varsayımları çürüdü. 4 reversal kullanıcı onayıyla plana işlendi (D10–D13). Approach C (MSBuild-native graph/incremental) yine reddedildi, ama **engine olarak MSBuild.exe** kabul edildi.
- **CROSS-MODEL (design):** Bağımsız Claude design subagent (Codex yok) §7'yi + yeni console fikrini ayrı değerlendirdi; **birincil ve outside voice güçlü mutabakat:** (1) typing FIFO-kuyruk olarak ship-broken → drop-to-latest/throughput-suspend (DD2); (2) "animasyonlar her zaman açık, kaçış yok" savunulamaz → OS reduced-motion (DD3); (3) 3-zone → 2-zone (live-line=stream aktif satırı, DD1); (4) eksik state'ler (özellikle all-skipped delight, DD10) + global progress/ETA (DD6); (5) reframe: **restraint = farklılaşma, motion ≠ polish.** Hepsi DD1–DD14'e işlendi; çelişki yok.
- **VERDICT:** **CEO + ENG + DESIGN CLEARED** — kapsam tutuldu, doğruluk yer-gerçeğine göre sertleştirildi, deneyim tasarlandı. **Iteration -1 Feasibility Spike (T23)** writing-plans'i gate eder (design'dan etkilenmez); 3 yes/no kanıtlanınca implementation başlar (design task'ları It-4 UX polish'e düşer).

NO UNRESOLVED DECISIONS
