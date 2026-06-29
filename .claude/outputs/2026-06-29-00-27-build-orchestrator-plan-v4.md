# Build Orchestrator — Yeni Plan (v4)

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
