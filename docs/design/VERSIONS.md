# Prototip sürümleri

Build Orchestrator'ın UI tasarımı yaşayan bir prototiptir: Claude'daki *Build Orchestrator - Design* artifact'i ve buradaki paketler. Her sürüm burada, en yenisi en üstte: ne değişti, neden, uygulamaya ne düşüyor ve uygulamaya geçip geçmediği. Tasarıma devam eden önce bu dosyayı ve son paketin spesifikasyonunu okur, yeni sürümü bir öncekinin üzerine kurar; kod tarafı `Uygulandı: —` olan en eski sürümden başlar ([tasarım paketi](README.md#koda-geçirmek)).

Numaralama: yeni özellik, davranış veya ekran **minor**; küçük düzeltme **patch**; **major** yalnız Sinan'ın kararıyla ve bir milatı işaretler. Metindeki `§` numaraları o sürümün paketindeki README'nin bölümleridir.

| Sürüm | Tarih | Konu | Uygulandı |
|---|---|---|---|
| **2.0.0** | 2026-11-23 | Milat: tasarım ve design system uygulamanın kodundan (app 1.15.0) yeniden kuruldu | app 1.15.0 (koddan alındı) |

## 2.0.0 — 2026-11-23 · Milat: uygulamanın kodundan yeniden kuruldu

Paket: [`packages/2026-11-23-16-07-design-v2.0.0/`](packages/2026-11-23-16-07-design-v2.0.0/README.md) · Prototip: [`prototype/BuildOrchestrator-prototype-2.0.0.html`](prototype/BuildOrchestrator-prototype-2.0.0.html) · Design system: [2.0.0](../design-system/VERSIONS.md).

Uygulandı: app 1.15.0 — tasarım uygulamadan alındı; uygulamaya düşen bir değişiklik yok (yalnız spesifikasyonda listelenen doküman–kod uyuşmazlıkları karar bekliyor).

**Neden.** Tasarımın son 1.x sürümü (1.25.0, 2026-10-15) uygulamanın 1.10.0'ına denk geliyordu. Uygulama sonra 1.11'den 1.15.0'a kadar ilerledi — Exit, worktree'nin yeni biçimi, branch kartı, kompakt konsol, yeni General satırları, CHANGELOG'dan beslenen What's new, son derlenen configuration, Build'in döngü grubunu turlarla derlemesi — ve tasarım iki katmanda geride kaldı: 1.20–1.22'de yalnız metin olarak yazılan modeller hiç çizilmemişti, uygulamanın o tarihten sonraki kararları da hiç alınmamıştı. Tasarımı uygulamanın kararlarının arkasından yamamak yerine tasarım ve design system **uygulamanın kodundan baştan kuruldu**: her metin, ölçü ve davranış koddan okundu (`ARCHITECTURE.md` yalnız yer bulmak için), prototip birkaç geçişte eşitlendi ve son bir denetimle satır satır doğrulandı, §1–§8 bu kodu anlatacak şekilde yeniden yazıldı. Bu sürüm uygulamadan bir değişiklik istemez — uygulama 1.15.0 v2.0.0'ı zaten uygular (`VERSIONS.md`'deki ilk kayıt). Bundan sonra tasarım elle değişir; her değişiklik bir 2.x sürümüdür.

### Ne içeriyor (alan alan)

**Title bar, Exit, kaynak şeridi (§2.1, §2.13)**
- Sağ küme `[Update <sürüm>] | [quad][list][focus] | [⚙][✦][ⓘ] | [⏻]  [— □ ×]`: öğe arası 1px, hairline yan payı 8px, bütün küme ikonları 16px kutuda; What's new'in kısayolu yok; pencere kontrolleri 46px, Close'un kırmızı hover'ı yok.
- **Exit:** ⏻ butonu (tooltip `Exit — quit and end every process (Ctrl+Q)`), iş yokken kartsız çıkış; iş varken 320px kart (durum · `ENDS ON EXIT` süreç listesi · karar: `Stop and exit` / `Wait and exit`), kart açıkken iş biterse boşta hâli; kapanış ekranı `Stopping build…` → `Ending N processes…` / `Quitting…`.
- **Kaynak şeridi:** CPU · RAM · `koşan/koşunun paralelliği Mod · sınır` (boşta `0/0 Balanced · 4`, kısılmış koşu `2/2 Balanced · 4`); ilk örnek 0.5 sn'de; mod sözcüğü koşunun başladığı mod.

**Sticky şerit (§2.2)**
- Pill sözlüğü `SYNC · BUILD · REBUILD · CLEAN · DEEP CLEAN · OPTIMIZE · RESOLVE · SWITCHING BRANCH · CREATING WORKTREE`; kendiliğinden Sync pill yazmaz.
- Öncelik: motor ölümü → sessizlik → Exit bekleyişi → motor uyanıyor → workspace yok → workspace işi → faz satırı. Sync adımları (`fetching` → `analyzing` → ilk Sync'te `indexing source files · d/t`), `▸ Starting — resolving what to build`, `▸ Stopping — …`, `▸ Stopped — n/m · K not built`, `▸ Cleaning n/m`, her koşuda döngü turu sürerken `▸ Resolving cycles · round r/3 · n/m · t`. Bitiş satırına uyarı varsa ` · N warnings`. Bakım işi ve Resolve için ayrı yeşil sonuç satırı yok.

**Action bar (§2.7)**
- Sıra: Sync · bakım kutusu · sayaçlar · workspace adı (`ACME`, tooltip aktif kök) · **worktree anahtarı** (24px kare *folders*) · branch · `N behind` · Debug|Release · perf · [**Restart engine**] · Build/Stop.
- Sayaçlar `Σ · building · ✓ · ✗ · ⚠(>0)` satırın görsel durumundan; `—` ve `○` chip'i yok. Resolve tooltip'i kapsamda bayat upstream varsa ` · U upstream to build first` ekler (sayı Resolve'un kendi kapsamından).
- Build menüsü `Build` / `Rebuild` / `Clean` (F5 / F6 / F7); Clean bir `-t:Clean` koşusudur, tooltip `Clean — msbuild /t:Clean on every project; caches are untouched`. Stop üç aşama `Stop` → `Stop now` → `Terminating…`.
- Branch chip'i gerçek checkout; git işlemi yarıdayken 6px amber nokta. `N behind` → ff-only pull. Debug|Release bir Sync başlatır.

**Klavye (§3.3)** — `F5` yalnız Build · `F6` Rebuild · `F7` Clean · `Ctrl+Q` Exit · `F1` About · `Ctrl+F` filtre; global `Shift+Space` / `Ctrl+Shift+Space`. Esc zinciri exit kartından pencereyi kapatmaya kadar tek sıra.

**Popover'lar, branch kartı (§2.8)** — branch popover 480px, 28px satır, ↓/↑/Enter; branch kartı (kirli ağaç, *Stash and switch* kapalıyken): `Cancel` · `Use worktree` / `Set up worktree…` / `Use repository` · `Stash and switch`. Diğer kopyada açık branch'e geçiş reddedilir — yalnız konsola `warning: develop is already checked out in D:\src\ACME-wt — a branch can be open in one working copy at a time`, stream satırı yok.

**Settings, About, What's new, güncelleme (§2.9–§2.12)**
- Settings General altı grup (STARTUP · BUILD · BRANCHES · CONSOLE · ENGINE · NOTIFICATIONS), kaydet notları `<Label> on/off — <sonuç>`; Workspace'te `WORKTREE FOLDER`; Export/Import düz anahtarlar; Import okuma hatası `Invalid settings file`.
- About: sürüm çipi uygulama sürümü; Shortcuts GLOBAL · BUILD · APPLICATION; Copy diagnostics. What's new verisi uygulamanın `CHANGELOG.md`'si (33 sürüm), başlık ve etiketler düz kesme işaretiyle `What's new`.
- Güncelleme kartı dialog açılınca kapanır; kilit satırları Stop'un aşamasını izler; restart ekranı tek adım `Closing Build Orchestrator…`.

**Graf (§2.3)** — bantlar listenin gruplarıdır; `No graph` / keşif blokları; içerik payı 36px; hover 1.5×; seçili node hover görünümünü tutar; parlak tutma 1400ms → 700ms; beads adım 4.4px, tur 4000ms; küp stroke 1.6; filtre dimlemesi arama VE çiplerle 420ms; koşu grafta filtreyi yok sayar; döngü küpü her durumda amber.

**Proje listesi (§2.4)**
- Karar etiketi sözlüğü `modified` · `modified · local` · `affected` · `never built` · `failed` · `up to date` + native `title` gerekçeleri; zaman kuyruğu yok.
- **Son derlenen configuration:** defter son başarılı derlemenin configuration'ını tutar; Debug ↔ Release her şeyi kirli saymaz (`affected` + `… last built in Debug`). **Dışarıda derlenmiş** proje (örnek veride `ACME.Sales.Api`, `ACME.Notifications.Api`): `up to date` + `Up to date — built outside this tool` ya da bağımlılık arkasında `affected`.
- **Open in Visual Studio seçicisi** (örnek veride `ACME.Security` dört, `ACME.Sales.Core` iki solution'da).
- Filtre chip'i etiketleri barın sabit sırasıyla birleştirir, tooltip `Clear the active filter`. Satır menüsü koşu sürerken disabled, tooltip `Build in progress — wait or stop it first`. `No projects` / keşif blokları.

**Konsol (§2.5)**
- Başlıkta statü glyph'i/sözcüğü yok; kompakt konsol; renk metinden; satırlar sarmaz; düz kayma geçişi; 200 satırlık anlatı penceresi + bellekten geri yükleme; `ready` yalnız anlatı boşken.
- **Uygulamanın satır sözlüğü:** açılışta ve Restart engine sonrası `Engine ready — v1.15.0` ve altına eklenen Sync; Sync transkripti `git fetch origin <b>` → `HEAD … · N commits behind origin/<b>` → `Scanning solutions (N)` → `Reading HintPath/Compile items (N projects)` → `Dependency graph — N cycles` → `Build order resolved (N)` → (ilk indekste) `Indexing …` / `Indexed …` → `Sync complete — N changed projects, M to build`; çevrimdışı fetch `warning: git fetch failed — continuing against the local HEAD; …`; koşu başında `build requested` / `rebuild requested` / `clean requested` / `cycles requested` (tek projede `… — X (single project)`) + planlayıcı satırları; Stop `stop requested — …` / `stop now requested — …` / `stopped — N in-flight compiles terminated`; pull tek `git merge --ff-only origin/<b>` satırı, ret `warning: pull refused — …`; harici güncelleme `Updating external '<name>'` → `Updated external '<name>' → <rev>`.
- **Kalktı:** uydurma açılış satırı (`Build Orchestrator 2.4.1 — … loaded`), koşu başında `msbuild <sln> …` / `acme-build …` / `…-resolve-cycles …` satırları, koşu sonu özet satırları (`Build complete — …`, `Build failed — …`, `Clean complete — outputs removed …`, `Everything up to date — …`, `Cycles resolved — …`), `note: the circular reference remains …`, `cycle round r/3 — building with …`, `Build stopped — …`, `Fetch degraded — …`, `Pull refused — …` (büyük harfli), harici pull'da `git -C … pull` / `tf get …` satırları. Koşunun özeti şeritte ve stream'de yaşar.
- Proje logunda bağımlılık uyarıları uygulamanın biçimiyle: `warning: X failed in this run — last successful output referenced (X)`; dolaylı kökler `warning: failure in dependency chain (Tam.Adlar) — referenced outputs may be stale` doğrudan satırlara ek olarak yazılır. Döngü üyesinin logu yalnız MSBuild çıktısıdır.

**Event stream (§2.6)**
- Üç kolon, metin kendi kolonunda sarar; `warn` türü (branch/pull reddi, in-use notu); döngü satırları (`cycle round r/3 — N members` · `cycle converged — 3 members, 3 compiled, 2 rounds · t`); bakım işleri tek kapanış satırı.
- **Tek biçimli kapanış:** her koşu `Completed — [f failed · ]s succeeded · k skipped · t[ · N dependency-affected]` ile kapanır (Clean ve hızlı kontrol dahil); durdurulan koşu `Stopped — N remaining projects queued`. `Build started — X (single project)` · `Clean started — N projects, parallelism P` · `Sync — 0 to build, 36 up to date` · `X skipped — in dependency cycle`. Uydurma `Pulled N external working copies …` satırı kalktı.

**Akışlar (§3)**
- Deep clean / Optimize: liste ve graf boşalır, şeritte tek ikincil satır, konsolda servis satırları, stream'de tek kapanış satırı, ardından Sync.
- **Build ve Rebuild bayat döngü grubunu turlarla derler:** grup tek birim, upstream'ini bekler, üyeler bir slotta teker teker, tur 1 her kirli üye, tur 2 yüzeyi kayan üye, tavan 3; tur arasında üyeler kuyrukta, grup bitince tek sonuç + `cycle converged`. **Resolve cycles** aynı işin dar ve artımlı biçimidir (yalnız grup + bayat upstream).
- Satırdan tek proje Build / Rebuild / Clean kapsamlı birer koşudur — satırdan Clean `clean requested — X (single project)`, `Clean started — X (single project)` → `Completed — 1 succeeded · 0 skipped · t`.
- Kendiliğinden Sync (commit, pencereye dönüş) bölüm açmaz; branch değişimi açar.

### Design system 2.0.0

Design system de bu milatla uygulamanın kodundan yeniden kuruldu ve **2.0.0** oldu (`docs/design-system/`). Uygulamada olup eski DS'te karşılığı olmayan değerler token olarak girdi:

| Token | Değer | Nerede |
|---|---|---|
| `status-queued-border` | `rgba(124,124,132,.24)` (`#3D7C7C84`) | kuyruk glyph halkası |
| `brand-strip-dim` | `#44444b` | app-mark şeridi, tray göstergesi |
| `brand-chevron-deep` | `#c9860c` | uygulama içi chevron gradyanının dip durağı |
| ribbon / progress / action bar / panel header / layer header | 32 / 2 / 42 / 28 / 24 | pencere iskeleti |
| pencere minimumu | 1240 × 620 | pencere |
| reading line height | 1.62 (13px'te 21.06) | About paragrafı, What's new maddeleri |
| console weight | 300 (Light) | konsol gövdesi |

§1.4 uygulamaya özgü ikonları (*power*, *folders*, *arrow-down-to-line*, *rotate-ccw*), 16px küme kutusunu ve uyarı üçgeninin amber, konturlu olduğunu (kırmızı dolu üçgen değil) yazar. WPF'in overlay gölgesi tek katman ve spread'sizdir (§1.3). Bundan sonra DS'teki bir değişiklik, onu getiren tasarım sürümünün notunda yazılır.

### Simüle olan / yer tutucu
- **Prototipte yok:** OS yüzeyleri (tray ikonu ve menüsü, tray build göstergesi, balonlar, taskbar, başlık butonlarının sistem tooltip'leri, yerel klasör/dosya seçicileri, kurulum programı), global kısayollar · yakınsamayan döngü grubu (`cycle failed`, `cycle round cap reached`, ` (N stuck in a cycle)`; örnekte düşen grup yok) · taşınan üye (`skipped — up to date (carried: …)`) ve yüzey kanıtı · zaman kipinde `OutputReplaced` / `WaitingForDependency` gerekçeleri · `Start with Windows` Görev Yöneticisi notu, Engine PID `asleep`, MSBuild `resolving…`, motor uykusunda 0 MB · `Sync failed` / `Run failed` satırları · git işlemi yarıdayken Build'in uyarı satırı · kendiliğinden Sync'in stream satırları · kamera snap ve etiket itme · reveal adım sıkıştırması. Uygulamanın yalnız panoya giden bellek satırları (`App private`, `GC committed`, `Managed heap`) Environment sekmesinde doğru olarak yoktur.
- **Basit:** döngü grubunun tur planı sabit (2 tur, tur 2 bir üye; Resolve'da tur 2 tüm üyeler) ve üyeler kesin teker teker (uygulama tur seviyelerini paralel dalgalarla koşar) · Resolve'un ön koşul listesinde `ACME.Parts.Catalog` (örnek veride bir üyenin aşağısında; geri kenar olmadığı için gerçekte döngü bileşenine düşerdi) · koşu sayıları ve süreleri (`Stopped — N …`'daki N = derlenecek − biten; satırdan Clean anında, sabit `0.4s`; hızlı kontrol `checked in 0.0s` — sim koşuyu tek tikte çözer) · tek proje ve Resolve koşusu Stop'un ilk basışında durur · anlatı geri yüklemesi yalnız bellekten, ölçülen scrollHeight farkıyla · Deep clean sonrası Sync her satırı `modified` okur ve Sync `lastBuiltAt`'i yeniden üretir · katman başlığı klavyeyle de odaklanır · Build menüsündeki Clean tooltip'i native `title`.
- **Demo ile ulaşılır** (`window.boDemo`, §7): motor ölümü/sessizliği, yarıdaki git işlemi, kirli ağaç, stash, worktree kurulumu, Exit; "Demo:" ile başlayan "pencere gitti" yüzeyi.
- **Yer tutucu metin:** projelerin MSBuild çıktısı (uyarı kodları, `Build succeeded — …` / `Build FAILED — …`) · `git checkout <b>` konsol satırı · art arda ikinci Build'den önceki `HEAD <sha> — N projects changed … since last build` notu · stash mesajının tırnak içi · in-use tutucu adı (`ACME.Client.exe`) · 7 güncelleme maddesi (`1.16.0` örnek teklifi; CHANGELOG'da yok) · Exit süreç adları · bellek MB değerleri · demo disk klasörleri · ağ hatası metni (`could not resolve host git.acme.local`).

### Doküman ↔ kod uyuşmazlıkları (kullanıcının kararı)
Tasarım koddan kurulurken uygulamanın kendi dokümanı ile kodu arasında şu uyuşmazlıklar görüldü. Bu README kodu izler; hangisinin doğru olduğuna kullanıcı karar verir:
1. `ARCHITECTURE.md` §13.3 — Export anahtar listesi ve "anahtarı olmayan dosya" listesi `replaceFilesInUse`'u saymıyor; `SettingsFile.cs` onu `pullExternalBeforeBuild`'den sonra yazıyor ve okuyor.
2. `ARCHITECTURE.md` §13.3 — bir yerde "The four workflow preferences ride along as optional values" deniyor; başka yerde (ve katalogda) iş akışı tercihi **beş** (pull · replace · stash · compact · idle).
3. `ARCHITECTURE.md` §14.4 — "carets and chevrons are drawn, not typed"; `Controls/LatestPill.xaml` ⌄'yi metin glifi (U+2304) olarak yazıyor.
4. `Controls/LatestPill.xaml` yorumu "radius-md (=overlay 8)" diyor; DS'te ve `Tokens.xaml`'de `radius-md` 6'dır, pill 8 çiziliyor (bu README 8 alır).
5. `Graph/GraphView.xaml.cs` — `IconStroke = 1.8` sabiti (doc'u "1.8px stroke") hiçbir yerde okunmuyor; küp `Icon.Package` stroke 1.6 ile çiziliyor (bu README 1.6 alır).
6. `ARCHITECTURE.md` §14.2 — konsolun 1.55 satır yüksekliği için "20.15 DIP at 13 px" ölçümü veriliyor; konsol 12px'tir (`LineHeight.Mono12` 18.6) — ölçüm başka bir yüzeyden mi, doküman mı eski, doğrulanmadı.
7. DS ↔ uygulama: DS overlay gölgesi iki katmanlı ve spread'li, WPF tek katman spread'siz (§1.3) · DS `StatusGlyph` README'si "yanında daima Türkçe statü metni" diyor, UI metinleri İngilizce — DS metni bayat görünüyor.

### Önceki sürümler (1.0.0–1.25.0)
Tasarımın 1.0.0–1.25.0 sürümleri (ve uygulama 1.15.0'a eşitleyen ara sürüm 1.26.0) başka bir Claude hesabında yapıldı. Repoda `.claude/outputs/<tarih>-design-v1.x/` altında arşivlenmişlerdir ve **yalnız tarihtir**: bu spesifikasyonun yerine geçmez, ondan kod işi çıkmaz. §1–§8'deki `(v1.x.y)` etiketleri ve "arşiv v1.x.y" atıfları bir kararın hangi 1.x sürümünde alındığını gösterir; ayrıntısı arşivdedir. 2.x hattı onların numaralamasını sürdürmez — v2.0.0 yeni bir başlangıçtır.

## Milat öncesi

1.0.0–1.25.0 sürümleri (15 Temmuz – 15 Ekim 2026) başka bir Claude hesabında üretildi; paketleri `.claude/outputs/<tarih>-design-v1.x/` altında arşivdedir ve yalnız tarihtir. Koddaki eski yorumlar (`design v1.11.0 §2.3` gibi) bu arşive bakar.
