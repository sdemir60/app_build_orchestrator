# GÖZLE KONTROL — yürünebilir kontrol listesi (It-4b devri + E6 Adım 3 + It-5)

**Kim yürüyecek:** kullanıcı (harness ekran görüntüsü alamaz — bu pas otomatikleştirilemez).
**Nasıl:** uygulama açık, belge yanda. Sırayla git, her satırda **ne yap** → **ne görmelisin** → kutuyu işaretle.
**Sapma bulursan:** satırın sonuna kısa not düş; belgenin sonundaki **"Sapma kaydı"** bölümüne yaz (nereye
yazılacağı orada anlatılıyor).

Üç bölüm var:
- **BÖLÜM 1** — It-4b'den devreden ~81 kalem, **panel panel** yeniden dizilmiş (kaynak: `.claude/outputs/2026-07-25-04-12-it4-records.md` §2; her satırın sonundaki `[B1]`,`[D4]`… o kalemin geldiği task).
- **BÖLÜM 2** — prototiple yan yana (E6 Adım 3), design-v1 README §2.1→§2.9.
- **BÖLÜM 3** — It-5'in kendi görsel kalemleri.

**Hazırlık:**
- Uygulama: `dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` (ya da BÖLÜM 3'teki publish çıktısı).
- Bir repo kökü hazır olsun (ör. `D:\Projects\Delta\OSYS`).
- Prototip (BÖLÜM 2 için): `prototype\Build Orchestrator (standalone).html` — çift tıkla, tarayıcıda açılır.

---

# BÖLÜM 1 — It-4b'nin ertelenen görsel borcu (panel panel)

## 0) Açılış

- [ ] Uygulamayı başlat → pencere açılıyor, boş durumda çökme/hata yok. `[C1]`
- [ ] **Taskbar ikonu** net (bulanık ölçekleme yok). `[B2]`
- [ ] **Tray ikonu** net. ⚠️ **İNSAN ONAYI GEREKİR:** tray ikonu artık eski "D" letterform DEĞİL, `delta-app-icon.svg` quarter-disc mark. Bu görsel kimlik değişimini onaylıyor musun? `[B2]`
- [ ] `--it4a-lab` ile başlat → **lab kabuğu AÇILMAZ** (T35'te kaldırıldı, primitifleri gerçek pencereye taşındı); argüman tanınmadığı için **yok sayılır ve normal ana pencere açılır** — bu doğru davranış, sapma değil. `--font-ab` ile ise **font A/B kabuğu** açılmalı (Supervisor spawn edilmez). `[C1]`

## 1) Pencere kabuğu / title bar / layout

- [ ] Pencere zemini `#0e0e10`, title bar `#141417`, metin `#ededee` — önceki sürüme göre hafifçe daha koyu / daha nötr. `[B1]`
- [ ] Pencerenin **dış çerçevesi (1px)** hâlâ görünür. `[B1]`
- [ ] Title bar'da min/max/close **çizilmiş 10×10 stroke ikon** (karakter/glyph değil); maximize → restore glyph **iki iç içe kareye** döner. `[B2]`
- [ ] Pencere **4 panelli**: sol-üst graf (boşken `Graph appears after Sync`) · sol-alt `PROJECTS` · sağ-üst konsol · sağ-alt `EVENT STREAM`. `[C1]`
- [ ] Title bar sağındaki **3 layout ikonu**: `list` + `focus` grafı gizler, `focus` konsolu büyütür, `quad`'a dönüş split'leri 50/50/50'ye sıfırlar. `[C1]`
- [ ] **Splitter'ı sürükle** → 7px kavrama bandı RAHAT tutuluyor (dar hissettirmiyor); çizgi sürüklerken **amber**; kolon **%28/%72**'de duruyor. `[C1]`
- [ ] Uygulamayı kapat-aç → **split konumları + layout modu korunuyor**. `[C1]`

## 2) Sticky şerit (faz / chip / hata kümesi / progress / ETA)

> Anlamlı olması için önce bir repo bağlayıp Sync/Build koşturman gerekir (bkz. 7).

- [ ] Açılışta şerit: `▸ Waiting for Sync — project states appear after Sync`. `[D2]`
- [ ] Sync başlat → `▸ Sync — git fetch origin…` + progress **belirsiz (kayan)** modda. `[D2]`
- [ ] Build sırasında → `▸ Building 7/14 · 24s · ~35s left` biçimi + **derlenen chip'ler**; bir chip'e tıkla → o proje seçiliyor (chip etiketi **kısa ad**, `OSYS.` öneki yok). `[D2]`
- [ ] Bir proje hata alsın → progress **ANINDA kırmızı**; sağda `✗ 5 failed · 4 dependency-affected` + ilk 3 hatalı chip + `+2 more`; `+2 more`'a tıkla → liste **Failed** filtresine geçiyor. `[D2]`
- [ ] Koşu bitince `Completed — …` satırı. *(Bilinen wire gap: canlı `· N warnings` görünmez.)* `[D2]`

## 3) Dependency graph (sol üst)

- [ ] Sync sonrası graf **KATMAN KATMAN** belirir (fade + 5px yukarıdan, katman başına 55ms). `[D5]`
- [ ] Build sırasında derlenen düğüme giden kenarlar **AMBER KESİKLİ akar**; kamera frontier'i **yumuşak** takip eder. `[D5]`
- [ ] Bir düğüme tıkla → komşuları normal kalır, gerisi söner (%25 düğüm / %16 kenar); **liste ve konsol AYNI ANDA** o projeye geçer. `[D5]`
- [ ] Boşluğa tıkla → seçim kalkar. `[D5]`
- [ ] depIssue taşıyan düğümde **sağ üstte kırmızı üçgen rozeti**. `[D5]`

## 4) Projects listesi (sol alt)

- [ ] Sync+Build sonrası satırlar tasarım kartı: **ince statü şeridi** / **8px will-build dot** / **ad + soluk sln adı** / sağda **mono süre**. `[D1]`
- [ ] Satırın üzerine gel → **sha çifti yerini klasör + VS ikonlarına** bırakır. `[D1]`
- [ ] Building satırda **çok hafif amber "nefes"** — süpürme/parlama YOK. `[D1]`
- [ ] Hata alan satır **BİR KEZ yatay sarsılır**. `[D1]`
- [ ] Dep'i hatalı ama başarılı derlenen satırda **▲** + birebir tooltip: `Failed dependency: … — last successful output referenced`. `[D1]`
- [ ] Cycle üyeleri **mor/cycle şeridi**; koşarken planlı-bekleyenler **queued şeridi**. `[D1]`
- [ ] Süre kolonu **ham ms değil**: `4.2s` / `1m 12s`. `[C2]`
- [ ] Katman başlıkları (Settings'te katman tanımlıysa) kaydırırken **birikerek yapışıyor** (i'inci başlık `i×24px`'e asılı kalır). `[D7/E4]`

## 5) Konsol (sağ üst) — ⚠️ **D4 ZORUNLU, ATLANAMAZ**

> **⚠️ ZORUNLU PAS:** D4 kalemleri headless harness'ta İMKÂNSIZ. Bu blok atlanamaz.

- [ ] **[D4]** Sync başlat → konsolda `▸ git fetch origin main` + granular satırlar; **en yeni satır HARF HARF yazılır**, imleç yanıp söner.
- [ ] **[D4]** MSBuild çıktısı akarken konsol **KİLİTLENMEZ** ve ham çıktı **harf-harf yazılmaz**.
- [ ] **[D4]** Bir karta tıkla → başlık `← Back · {proje} · statü` olurken gövde **AYNI ANDA** proje loguna geçer (eski içerik **BİR AN BİLE** kalmaz); satırlar **kaskatla** açılır.
- [ ] **[D4]** `Back` → anlatı moduna döner.
- [ ] **[D4]** Bir kartı hızlıca seç, hemen bırak (IPC dönmeden) → konsol **DONMAZ**, anlatı akmaya devam eder.
- [ ] Konsol paneli **değişmemiş** görünüyor (B1 token geçişinden etkilenmedi). `[B1]`
- [ ] Bir projeye tıkla → konsol başlığındaki **Copy log** çizilmiş kopya ikonu; tıkla → **1400ms** tik'e döner, sonra geri gelir. `[B2]`
- [ ] Konsolda yukarı kaydır → `⌄ latest` pill'in **chevron'u düzgün render** (tofu/kutu yok) ve **label ile aynı hizada**. `[B2]`

## 6) Event stream (sağ alt)

- [ ] Build sırasında sağ-alt panelde olaylar akar; aktif `building…` satırı **GERÇEKTEN daktilo eder**; olaylar hızlanınca (<340ms) ve hatalarda **anında** basılır. `[D3]`
- [ ] Olay satırına tıkla → o proje **HER YERDE** seçili. `[D3]`
- [ ] Hatasız biten koşuda son `Completed …` satırı **BİR KEZ yeşil parlar**. `[D3]`
- [ ] Yukarı kaydır → `⌄ latest` pill çıkar. `[D3]`
- [ ] `Build started — N projects` satırında **N = will-build sayısı** (skip'ler HARİÇ; incremental'de "8 projects", "36" DEĞİL). `[D3]`
- [ ] Panel boş / pre-Sync → `No events yet.` `[D3]`

## 7) Action bar + popover'lar + tooltip

> Repo başlangıçta bağlı DEĞİL — bar **tüm-disabled** açılır; önce Settings'ten bir repo seçip Sync'le.

- [ ] Sayaç chip'lerine tıkla → liste filtrelenir; aynı chip'e tekrar / `Σ` → temizlenir; `PROJECTS` başlığında **kaldırılabilir filtre chip'i** çıkar. `[D6]`
- [ ] `branch` chip → **272px** popover, arama çalışır; **aktif-OLMAYAN** bir branch seç → worktree **oto ON + kilitli**, konsolda `git switch` DEĞİL **niyet satırı**. `[D6]`
- [ ] `worktree` popover'ının **üç açıklaması** duruma göre değişir (zorunlu / açık / kapalı) + `source` satırı doğru. `[D6]`
- [ ] Build `▾` → menü **YUKARI** açılır; Stop sonrası sol yarı **Continue** olur ve **F5 rozeti** oraya geçer. `[D6]`
- [ ] Koşarken **branch/worktree/Debug-Release sönük**, **perf chip canlı**. `[D6/C2]`
- [ ] Sync sürerken **Rebuild/Retry sönük** ama **Build aktif**. `[C2]`
- [ ] **Başarısız** bir Sync'ten sonra Rebuild/Retry **GERİ açılır**. `[C2]`
- [ ] Geçici butonlar (Rebuild/Stop/Continue/Restart engine) DS görünümünde: **amber primary**, koyu secondary, **24/28px** yükseklik, **4px radius**. `[B3]`
- [ ] Üzerlerine gel → **yumuşak ~120ms renk geçişi** (ani sıçrama değil). `[B3]`
- [ ] Herhangi bir buton/ikon üzerine gel → tooltip **anında** (gecikmesiz), hedefin **tam ortasında**, **6px** boşlukla. `[B4]`
- [ ] Fare tooltip'in üzerine gidince tooltip **kaybolmuyor**. `[B4]`

## 8) Settings dialog

- [ ] Dişliye tıkla → boş katman listesinde **kesikli kutu metni**. `[D7]`
- [ ] `Load sample layers` → 6 katman gelir; **grip'ten sürükle** → kart **YERİNDE kayarak** komşusuyla yer değiştirir (OS sürükle-bırak hayaleti YOK). `[D7]`
- [ ] Bir regex'i boz → input **kırmızı**, `Save` **disabled**. `[D7]`
- [ ] `Save` → liste **katman başlıklarıyla gruplanır**, konsola `Layer definitions updated — 6 layers`. `[D7]`
- [ ] `Change…` → klasör seçici açılır; yeni kök seç → **otomatik Sync** başlar. `[D7]`
- [ ] Settings açıkken **Tab arkadaki kontrollere KAÇMAZ** (focus dialog içinde döner). `[D7]`
- [ ] `RootPathBox` **DS Input** görünümünde. `[B3]`

## 9) OS eylemleri

- [ ] Satır hover → **klasör ikonu** tıkla → Explorer **o dosya SEÇİLİ** açılır + konsolda dim not: `{name}.csproj revealed in Explorer`. `[E1]`
- [ ] **VS ikonu** tıkla → bağlı solution Visual Studio'da açılır; >1 sln varsa **seçim popover'ı**, seçince `{name} opened in Visual Studio`. `[E1]`

## 10) Klavye · focus · ekran okuyucu · kontrast

- [ ] Fareye hiç dokunmadan **Tab** ile dolaş → **amber focus halkası HER YERDE** görünüyor (2px halka, 1px boşluk). `[E5/B3]`
- [ ] Listede **ok tuşları + Enter** ile seçim yapılıyor. `[E5]`
- [ ] `Ctrl+F` → filtre kutusu; içindeyken **Esc yalnız temizler + blur eder**, global Esc zincirine sızmaz. `[E5]`
- [ ] `Esc` **SIRASIYLA**: açık dialog → popover → seçim kapatır. `[E5]`
- [ ] `F5` / `Ctrl+F5` build/rebuild tetikler; koşarken `F5` = Stop, stopped'ta Continue. `[E5]`
- [ ] Pencere gizliyken **`Alt+B`** onu geri getirir. `[E5]`
- [ ] Ekran okuyucu **İngilizce ad** okur + faz değişimini **duyurur** (live region). `[E5]`
- [ ] `DsSplitter` **ok tuşlarıyla** resize eder ve **ORAN persist olur** (kapat-aç → aynı yerde). `[E5]`
- [ ] Kontrast — gövde/statü metinleri okunaklı. ℹ️ **Bilgi (karar verildi, sapma DEĞİL):** şeridin Boot (`Waiting for Sync…`) ve Stopped (`▸ Stopped — …`) canlı metni `TextDim` ile **4.28:1** (<4.5) — design-v1 birebirliği lehine **RATIFY edildi**, token değiştirilmedi. `[E5]`

## 11) Motion · reduced-motion · auto-scroll arbitration

- [ ] Windows Ayarlar → Erişilebilirlik → Görsel efektler → **Animasyon efektleri**'ni **KAPAT** (uygulama açıkken) → nefes / spinner / dash akışı / daktilo / kamera / pop-in **DURUR**, her şey anlık. Geri aç → hepsi **döner**. `[E3]`
- [ ] Sync sonrası graf katmanları kademeli belirir (55ms/katman, tavan 330ms) ve **aynı anda TEK hero** motion olur. `[E3]`
- [ ] Sync sonrası **LİSTE satırları da** kademeli belirir (10ms/satır). `[E4]`
- [ ] Koşarken listede **yukarı kaydır** → takip **DURMALI**, konsol akmaya **DEVAM** etmeli. `[E4]`
- [ ] **Dibe dön** → takip **SÜRMELİ**. `[E4]`
- [ ] Karta tıkla → takip durur; seçimi kaldır → sürer. `[E4]`
- [ ] Hiçbir panelde **yo-yo** (ileri-geri zıplama) YOK. `[E4]`

## 12) Durumlar (empty · engine-died · autostart · all-skipped)

- [ ] Repo kökünü temizle → boş davet metinleri: `Pick a repository to get started` + açıklama satırı + `Choose Folder` butonu. `[E2]`
- [ ] Supervisor'ı **Task Manager'dan öldür** → şerit **KALICI hata modu** + `Restart engine` (banner/toast YOK); bas → motor geri döner. `[E2]`
- [ ] Uygulama açıkken **ikinci kez başlat** → mevcut pencere öne gelir; getirilemezse **tray balloon** (SESSİZ değil) ve ikinci instance **ayrışan çıkış kodu 3** ile kapanır. `[E2]`
- [ ] Autostart'ı aç, oturumu kapat-aç → tray'de **temiz Idle** (otomatik Sync YOK). `[E2]`
- [ ] Boş bir klasör seç → `No projects found under this folder.` + `Ready — nothing to build`. `[E2]`
- [ ] Hiç değişmemiş repoda Build → **all-skipped DELIGHT**: `Everything up to date — {n} projects checked in {dur}, nothing to build`. `[E2]`

---

# BÖLÜM 2 — Prototiple yan yana (E6 Adım 3)

**Kurulum:** `prototype\Build Orchestrator (standalone).html` tarayıcıda (sol) ↔ uygulama (sağ). Prototipin
üstündeki **sahne şeridi (Hero/Detail/Failure/…) prototip iskelesidir** — gerçek uygulama penceresi şeridin
altındaki çerçeveli alandır; karşılaştırma **yalnız o alanla** yapılır.

**Hedef fidelity:** HIGH — renk/ölçü/tipografi/kopya **birebir**. Tek meşru istisna sınıfı, aşağıda BÖLÜM 2
sonunda listelenen **A13.1 "algısal eşdeğer"** kalemleridir.

## §2.1 Title bar (40px)

- [ ] **Solda:** prototipte Delta logosu (dark, 15px) + "Build Orchestrator" başlığı → uygulamada aynı logo, aynı yükseklik, aynı başlık.
- [ ] Başlıktan sonra **mono 11px `text-dim` bağlam**: prototipte `OSYS · main` → uygulamada aynı biçim; worktree aktifse `· main-2` (daha soluk, `text-faint`); repo yokken `no repository`.
- [ ] **Sağda:** 3 layout ikonu (quad/list/focus, aktif olan vurgulu, tooltip'li) · **1px dikey ayraç** · dişli (tooltip `Settings — layer definitions`) · pencere kontrolleri. Sıra ve boşluklar aynı mı?
- [ ] Title bar yüksekliği **40px** — prototiple aynı optik yükseklik.

## §2.2 Sticky şerit (32px + 2px progress)

- [ ] Şerit yüksekliği 32px, zemin `surface-base`, altta `border-subtle` çizgi — prototiple aynı.
- [ ] Faz metni **mono 12px** ve `▸ ` önekli. Metinleri **kelime kelime** karşılaştır (boot / syncing / idle / running / stopped / done-başarılı / done-hatalı / all-clean). Prototip: `▸ Ready — 14 to build · 22 up to date`, `▸ Building 7/14 · 24s · ~35s left`, `Completed — 14 succeeded · 22 skipped · 1m 12s` (yeşil + ✓).
- [ ] **ETA nüansı** (README §2.2): ETA **<4s** kala metin `· almost done` olur; ETA **5s'e yuvarlanır** (yani `~35s left` gibi 5'in katları görülür, `~33s left` değil).
- [ ] Faz metninin yanındaki **building chip'leri**: spinner ikonu + kısa ad, **en çok 4**, fazlası `+N`.
- [ ] **Sağdaki hata kümesi**: ✗ glyph + `5 failed` (kırmızı, 500) + `· 4 dependency-affected` (dim 11px) + ilk 3 chip + `+2 more`. **"View failures" butonu OLMAMALI** (tasarım kararı: kaldırıldı).
- [ ] Altındaki **2px global progress**: radius 0; building=amber, failed=kırmızı, done=yeşil, sync sırasında indeterminate.

## §2.3 Dependency graph (sol üst)

- [ ] Panel başlığı 28px, zemin `surface`, altta `border-subtle`; solda caps `DEPENDENCY GRAPH`, sağda mono 11px `36 projects · 58 dependencies`.
- [ ] **Düğüm:** prototipte 26px, altında kısa ad etiketi (`OSYS.` öneki atılmış). Uygulamada aynı boyut/etiket. ⚠️ **Bilinen yapısal fark:** uygulamada düğüm **4px radius kare** (It-4a T63 kararı), prototipte daire — sapma olarak mı kaydedilecek yoksa kabul mü, karar ver.
- [ ] **Katman aralığı 96px**, düğüm aralığı ≤96px, kenarlar yukarıdan aşağı **kübik bezier**.
- [ ] **Kenar renkleri:** varsayılan `border` 1px · hedef building → **amber kesikli akış** (dash 4 7, 0.9s) · succeeded → yeşil · failed → kırmızı · hata dalı → kırmızı **statik** kesikli `3 4` · seçili düğüme değen kenar → amber (hata dalıysa kırmızı), **1.6px**, tam opak.
- [ ] **Seçim:** seçili + komşular normal; gerisi düğümde **%25**, kenarda **%16** opaklığa söner. Boşluğa tıkla → kalkar.
- [ ] **Kamera:** seçili düğüme / building frontier ağırlık merkezine / done'da merkeze; geçiş **460ms**, ölçek **0.68–1.08** arasında kıstırılmış.
- [ ] **Dep rozeti:** sağ üstte 13px daire (zemin `surface-base`, 1px kırmızı border) içinde **dolu kırmızı üçgen**.
- [ ] Sync öncesi boş durum: ortada **kesikli çerçeveli kutu** `Graph appears after Sync`.

## §2.4 Projects listesi (sol alt)

- [ ] Panel başlığı: caps `PROJECTS` + mono `build-order` etiketi; aktif filtre varsa kaldırılabilir chip (`Failed ✕`).
- [ ] **Satır 36px**, altında `border-subtle` çizgi. Yedi slotu soldan sağa tek tek karşılaştır:
  1. [ ] 2px dikey **statü şeridi** (tam sol kenar; discovered=transparent; seçiliyken **3px + amber**).
  2. [ ] **8px WillBuildDot**: dolu amber=dirty · dolu gri `#3a3a42`=clean · transparent + 1px `#1c1c20` halka=unknown.
  3. [ ] **Ad** 13px/500 (`skipped`/`discovered` ise `text-dim`) + yanında **sln adı** 12px `text-faint`; taşmada ellipsis.
  4. [ ] **Sağ blok min 118px**: hover'da 2 ikon buton (klasör + VS); hover yokken dirty projelerde **mono 10.5px** `a3f81c2 → b7e91d4`.
  5. [ ] **Statü glyph'i 14px** + tooltip (durum adı; building ise geçen süre; depIssue ise `— dependency issue`).
  6. [ ] **Sabit 14px slot**: depIssue varsa 12px kırmızı üçgen-ünlem. Slot **her satırda** var → **hiza asla bozulmuyor** (dep'siz satırlarla yan yana bak).
  7. [ ] **Süre**: mono 12px, sağa yaslı 46px; building=canlı sayaç, bitti=`4.2s`, yoksa `—`; failed'da kırmızı.
- [ ] **Building satırı:** hareketsiz amber "nefes" — `amber-soft` opacity 0→0.32→0, **3.8s** ease-in-out sonsuz. Süpürme/parlama/kayma **OLMAMALI**.
- [ ] **Failed anı:** satır **360ms** yatay shake (±3px), **bir kez**.
- [ ] **Katman başlıkları:** 24px, caps 11px + mono sayı; **birikerek yapışıyor** (i'inci başlık `i×24px`).
- [ ] **Follow-mode sayıları** (README §2.4): koşarken ve seçim yokken liste frontier'i takip eder — scroll animasyonu **550 ms'de bir**, hedef sapması **<54 px** ise **dokunulmaz** (ufak kaymalarda liste zıplamamalı). Karta tıklayınca takip durur, seçim kalkınca sürer.
- [ ] Boş durum metinleri birebir: `Pick a repository to get started` (14px/600) + `Point to the OSYS solution root — projects and the dependency graph are discovered automatically.` + `Choose Folder` (klasör ikonlu primary).
- [ ] Filtre eşleşmezse: `No projects match this filter.`

## §2.5 Console (sağ üst)

- [ ] Zemin **`#060608`**, **radius 0**, padding 8×12; mono **12px**, satır yüksekliği 1.55.
- [ ] Alta yapışık scroll: kullanıcı **48px**'ten fazla yukarı kaydırınca serbest, dibe inince yeniden yapışır.
- [ ] `⌄ latest` pill: dipten ≥48px uzaktayken **panel alt-ortasında** küçük mono pill (`surface-overlay`, `border-strong`, radius-md, popover gölgesi); tıkla → **yumuşak** en alta iner; dibe dönünce kaybolur.
- [ ] **Anlatı modu:** her satır `HH:MM:SS` (`text-faint`) + **10px ikon kolonu** + metin; cmd satırında amber `▸`; **en yeni satırda 7×13px yanıp sönen blok imleç (1.1s)**; en yeni satır **daktiloyla** yazılır (≤~250ms), yazım bitince imleç ~420ms sonra söner.
- [ ] Boşta tek satır: `12:04:07 ▮ ready` (dim).
- [ ] Satır renkleri: cmd=`text-primary` · info=`text-secondary` · dim=`text-faint` · success/warn/error=ilgili `-text` tonu.
- [ ] **Seçili proje logu modu:** başlık → `← Back` ghost buton + proje adı (mono) + statü glyph + statü adı + (varsa) `▲ dependency issue`.
- [ ] Log satırları **sıfırdan kaskatla** açılıyor: **26ms'de 3 satır**. ⚠️ **A13.1 madde 3 (kabul edilmiş fark):** satır başına 140ms **translateY+scale pop-in** yerine **opacity-fade** var — AvalonEdit satır transform'u desteklemiyor. **Tempo birebir olmalı**; tempo kaymışsa bu bir sapmadır.
- [ ] Seçili proje **hâlâ building** ise proje logunun **sonunda amber `build in progress ▮`** satırı var.
- [ ] Log yoksa metinler birebir: skipped → `Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (a3f81c2)` · queued → `Queued — waiting for dependencies: Sales.Core, Security` · diğer → `No log yet — output streams here once the build starts.`
- [ ] Akış yönü **klasik: en yeni ALTTA**.
- [ ] Panel başlığı sağında mono `N lines`.

## §2.6 Event stream (sağ alt)

- [ ] Panel başlığı: caps `EVENT STREAM`; sağda mono `N events`.
- [ ] Satır min 24px / mono 12px: saat + glyph (ok=✓, fail=✗, skip=—, sync/info=amber `▸`, done=✓/✗) + metin. Renkler: fail=kırmızı · skip=`text-faint` · done=yeşil/kırmızı · sync/info=`text-dim` · ok=`text-secondary`.
- [ ] Örnek metinleri **birebir** karşılaştır: `OSYS.Domain.Service built (2.9s)` · `OSYS.Sales.Core failed — 2 errors (3.1s)` · `OSYS.Base skipped — up to date` · **`Sync — 14 to build, 22 up to date`** · `Build started — 14 projects, parallelism 4` · `Completed — 5 failed · 12 succeeded · 17 skipped · 1m 30s · 4 dependency-affected`.
- [ ] En yeni satır **daktiloyla**; ardışık olaylarda (<340ms) ve **hata olaylarında ANINDA**.
- [ ] Aktif satır: `OSYS.Server.Api building…` — saat + **imleç** + amber daktilo metni.
- [ ] Projeli satırlar tıklanabilir; seçili satırda **sol 2px amber şerit + `surface-raised` zemin**.
- [ ] Tümü başarılı biten koşuda done satırı **bir kez yeşil parlar** (`success-soft` → transparent, **1.1s**).

## §2.7 Action bar (42px)

- [ ] Zemin `surface`, üstte 1px `border`, yükseklik 42px.
- [ ] Soldan sağa sıra **birebir**: `Sync` (secondary sm, döngü ikonu) · 1px ayraç · sayaç chip'leri · esnek boşluk · `branch: main ▾` · `worktree: off ▾` · `Debug | Release` segment · `perf: Balanced` chip · 1px ayraç · **Build split-button**.
- [ ] Sayaç chip'leri: `Σ 36` · spinner+`4` (boşken gri nokta) · `✓ 14` · `✗ 5` · `— 17` · `▲ 4` (sayı >0 ise üçgen kırmızı). Aktif filtre chip'i **vurgulu**.
- [ ] `perf` chip'inde **tooltip OLMAMALI** (tasarım kararı).
- [ ] Build split-button: sol `Build` + sağ `▴`; menü **YUKARI** açılır ve iki satırı da ikon + başlık + açıklama + Kbd taşır: `Build — Only changed projects — F5` / `Rebuild — All 36 projects — cache ignored — Ctrl+F5`.
- [ ] Koşarken Build'in yerinde **`Stop` danger butonu** (kare ikon).
- [ ] Uygulamanın **hiçbir yerinde toast/popup YOK**.

## §2.8 Popover'lar

- [ ] Ortak: chip'in **üstünde 8px boşlukla** açılır; `surface-overlay` zemin, **1px `border-strong`**, **radius 8**, overlay gölgesi, **140ms pop-in (4px yukarı + scale .985→1)**. Dışarı tıkla / Esc → kapanır.
  - ⚠️ **A13.1 madde 2 (kabul edilmiş fark):** WPF `DropShadowEffect`'te **spread** parametresi yok — gölge tek katmanla yakınsanıyor. Gölgenin "yumuşaklığı" tam eşleşmeyebilir; **konum/opaklık/renk** eşleşmeli.
- [ ] **Branch popover 272px:** caps başlık `SWITCH BRANCH` · arama inputu (büyüteç, `Search branches…`) · 28px satırlar (seçilide ✓ amber, değilse branch ikonu; ad mono; aktif branch'te amber `active` rozeti, diğerlerinde mono SHA) · eşleşme yoksa `No branches match "q".` · alt not: `Picking a non-active branch requires a worktree; the active branch stays untouched.`
- [ ] ⚠️ **Bilinçli sapma (plan kazandı, Y2):** aktif olmayan branch seçilince prototip konsola `git switch --detach f3a02c8 …` yazar; uygulama **komut değil NİYET satırı** yazar. Bu **sapma değil, karar** — doğrulanacak olan uygulamanın niyet satırını yazdığıdır.
- [ ] **Worktree popover 300px:** caps `WORKTREE` · `Build in worktree` switch'i · üç açıklama metni **birebir** (zorunlu / açık / kapalı) · açıkken `TARGET WORKTREE` listesi (`main-2 (new)` + mevcutlar `main-1 — 2 days ago · clean`, hover'da çöp kutusu) · en altta mono `source` satırı.

## §2.9 Settings dialog (620px)

- [ ] Genişlik 620px; `surface-overlay`, radius 8, overlay gölge, **düz scrim** (blur YOK).
- [ ] Caps başlık `LAYERS` + açıklama metni birebir: `Projects are grouped by the first matching pattern (regex on the project name), top to bottom; card order is the layer order in the list. Non-matching projects fall under Other.`
- [ ] Katman kartları **36px + 6px boşluk**: grip · ad inputu (170px) · regex inputu (mono, esnek; geçersizde kırmızı) · çöp ikonu.
- [ ] Sürükleme sırasında: kart **raised zemin + strong border**, **yarım satır eşiğiyle** yer değiştirir.
- [ ] `+ Add layer` ghost buton.
- [ ] **Varsayılan BOŞ** + kesikli kutu: `No layers yet — projects show as a single list in build order.`
- [ ] Footer: solda ghost `Load sample layers`, sağda `Cancel` + `Save` (ad boş / regex geçersizse disabled).
- [ ] Kaydet → konsola dim not: `Layer definitions updated — 6 layers` / `Layers removed — single project list`.
- [ ] **Eşleşme sayacı GÖSTERİLMEMELİ** (tasarım kararı).

## Genel (§1'in çapraz kuralları — yan yana bakarken kontrol et)

- [ ] **Amber dışında dekoratif renk YOK**; gradient / mor / indigo / emoji **YOK**; **panel gölgesi YOK** (yapıyı 1px border taşır); **backdrop-blur YOK**.
- [ ] Mono font yalnız makine çıktısında (console, süre, SHA, sayaç, yol) — **dekoratif kullanılmıyor**; rakamlar **tabular**.
- [ ] Caps etiketler: 11px, 500, letter-spacing **0.07em**, uppercase, `text-faint`.
- [ ] Motion süreleri 80/120/180/280ms bandında; **bounce/overshoot YOK**; aynı anda **en fazla 1 hero motion**.

## Sapma bulursan — iki yol var

**(a) DÜZELTİLİR.** Sapma gerçek bir fidelity kaybıysa (yanlış renk, yanlış ölçü, eksik/yanlış kopya metni,
eksik animasyon) → belgenin sonundaki **Sapma kaydı** tablosuna yaz; bunlar için bir **fix wave** açılır.

**(b) A13.1 "ALGISAL EŞDEĞER" SINIFINA YAZILIR.** Sapma, WPF'in yapısal sınırından geliyorsa ve iş gücüyle
kapanmıyorsa, **gerekçesiyle** kabul edilmiş farklar listesine eklenir. Bugünkü liste
(`.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md` §5, 7 madde):

| # | Kabul edilmiş fark |
|---|---|
| 1 | **Font rasterization** — DirectWrite ≠ Chromium/Skia; 11-13px metin bit-düzeyinde aynı olmaz (~%95-98). *(T65'te kullanıcı A/B ile onayladı: ayırt edilemedi.)* |
| 2 | **Gölge spread'i** — `DropShadowEffect`'te spread yok; popover/pill gölgesi tek katmanla yakınsanır |
| 3 | **AvalonEdit satır pop-in'i** — kaskat **temposu birebir**, satır başına translateY+scale yerine opacity-fade |
| 4 | **Animasyon threading'i** — UI donsa animasyon akmaz (compositor yok); Supervisor ayrımı sayesinde pratik etki beklenmez |
| 5 | **CSS `dashed` köşe hizalama** — WPF dash desenini köşeye hizalamaz |
| 6 | **OS yüzeyleri** — klasör seçici / Explorer / VS pencereleri uygulama temasına boyanamaz |
| 7 | **Tooltip HWND davranışları** — pencere dışına taşabilir, ekran kenarında flip eder |

**Nereye yazılacak:** yeni bir madde eklenecekse `.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md`
**§5** tablosuna **8. madde** olarak (biçim: *ne farklı · neden kapanamıyor · algısal etkisi ne*), ve bu belgenin
sonundaki Sapma kaydına "→ A13.1'e eklendi" diye işaretlenerek.

---

# BÖLÜM 3 — It-5'in kendi görsel kalemleri

## 3.1 Perf mode değiştirme (K11)

- [ ] Action bar'daki **`perf: Balanced` chip'ine** tıkla → döngü: **Full(6) → Balanced(4) → Light(2)**; chip etiketi anında değişiyor.
- [ ] **Chip tıklaması koşu DIŞINDAYKEN**: konsola **hiçbir not yazılmamalı**. (Not yalnız mid-run yazılır.)
- [ ] **Koşu SIRASINDA (mid-run)** chip'e tıkla → konsola **tek dim satır** düşer. Biçimi **tam olarak**:
  - Önünde `HH:mm:ss` **zaman damgası** var.
  - Gövde: `parallelism: 4 · cpu cap 70%` — ayırıcı **`·` (U+00B7)**, çevresinde birer boşluk.
  - `Full` seçilirse: `parallelism: 6 · cpu cap off`.
  - `Light` seçilirse: `parallelism: 2 · cpu cap 40%`.
- [ ] ℹ️ **Beklenen davranış (bug değil):** koşu sürerken **paralellik canlı değişmez** — worker'lar run başında bir kez yaratılır. Değişen şey **cpu cap + process priority**'dir. Konsol notu yine yeni profilin paralelliğini yazar.
- [ ] **Gözlemle (opsiyonel ama değerli):** koşu sırasında `Light`'a geç → Task Manager'da `MSBuild.exe` toplam CPU'su gözle görülür şekilde düşmeli, **ama uygulamanın kendisi (Sync/IPC) akıcı kalmalı**.

## 3.2 Büyük graf (500+ düğüm) — akıcılık · cull · etiket LOD · tooltip

> Gerçek OSYS grafı **177 düğüm**. 500+ senaryosu sentetik ölçüm zeminiyle (`SyntheticGraph`, test-only) ölçüldü;
> aşağıdaki gözle kontrol **eldeki en büyük gerçek grafla** (177) yapılır, LOD davranışı orada zaten aktiftir
> (eşik: **150 düğüm** — `GraphView.FullDetailMaxNodes`).

- [ ] Grafta **kaydır / pan yap** → hareket **akıcı**, takılma/kasma yok.
- [ ] **Cull görünür kusur üretmiyor:** hızlıca kaydır, scrollbar'la uzağa atla → boş kalan / geç gelen / yarım çizilmiş düğüm-kenar **yok**.
- [ ] **Etiket LOD:** 150 düğümün **altındaki** graflarda **hiçbir etiket düşmez** (küçük grafta tüm adlar görünür olmalı).
- [ ] 150 üstü grafta, düğüm aralığı **en geniş etiketin genişliğinin altına düştüğü katmanlarda** etiketler **düşer** (bu doğru davranış — o etiketler zaten üst üste binip okunamaz hâldeydi).
- [ ] **Etiketi düşen bir düğümün üzerine gel → TOOLTIP çıkmalı ve TAM proje adını vermeli.** (Bu, LOD'un kimlik kaybını kapatan tek afordansı — çalışmıyorsa graf anonim karelere döner.)
- [ ] Etiketi olan ve olmayan katmanlar **aynı grafta bir arada** olabilir — bu beklenen (LOD **katman başına** karar verir).

## 3.3 Publish edilen exe'nin ilk açılışı

> `scripts/verify-publish.ps1` bunu otomatik doğruluyor (16 check), ama kullanıcı gözüyle de bakılacak.

- [ ] `dotnet publish src/BuildOrchestrator.App/BuildOrchestrator.App.csproj -c Release -r win-x64 --self-contained false -o <klasör>` → çıktı klasöründe **`BuildOrchestrator.App.exe`** ve **`supervisor\BuildOrchestrator.Supervisor.exe`** var.
- [ ] Publish edilmiş **exe'yi çift tıkla** (dev makinesindeki bin klasöründen değil, publish klasöründen).
- [ ] Pencere açılıyor, konsolda boot satırı: **`Engine ready — v<sürüm>`**.
- [ ] Şeritte **engine hata modu YOK** (`Engine missing` / `Engine could not start` görünmüyor).
- [ ] Repo seç → Sync + Build **gerçekten koşuyor** (publish çıktısı motorla birlikte çalışıyor).
- [ ] Uygulamayı kapat → Task Manager'da **`BuildOrchestrator.Supervisor.exe` kalmıyor** (cascade).

## 3.4 W1 — sha göstergesi

- [ ] Dirty bir satırda, hover **yokken** sağ blokta sha çifti: **`a3f81c2 → b7e91d4`** — her iki taraf da **7 hane**, ham 40 hane **DEĞİL**.
- [ ] Çift **118px slot'a sığıyor**, taşma/ellipsis yok, sağa yaslı hizalama bozulmuyor.
- [ ] **Hiç derlenmemiş bir projede** (ilk Sync sonrası, henüz build olmamış): sol yarı **BOŞ**, yalnız **hedef sha** (7 hane) basılıyor — uydurma yer tutucu (`0000000`, `—`) **YOK**, havada kalan `→` oku **YOK**.
- [ ] Bir Build koştur → derlenen projelerin **sol yarısı dolmalı** (BuiltCommit artık taşınıyor); ikinci bir Sync'ten sonra da **slot boş kalmamalı**.

## 3.5 W2/G2 — reveal stagger, geç materyalize olan düğüm

- [ ] **Büyük bir graf rebuild'i** tetikle (Sync ya da katman tanımı değişimi ile grafın yeniden kurulmasını sağla).
- [ ] Düğümler **kademeli** belirmeli (katman başına 55ms, tavan 330ms).
- [ ] ⚠️ **Kritik kalem:** stagger **sürerken** grafı kaydır → o an cull yüzünden **yeni materyalize olan düğümler de animasyona KATILMALI**. **Tam opaklıkta birden belirmemeliler.** (Motion sözleşmesi ihlaliydi, G2 fix round 1'de kapatıldı — gözle doğrulanacak.)
- [ ] Reduced-motion açıkken aynı rebuild → **her şey anında**, stagger yok.

## 3.6 L1 — kart hover ikonlarının ilk-hover davranışı

> L1'de hover ikonları + VS-chooser popup'ı **ilk hover'da lazy** kurulur hâle geldi (191 satırın realize
> maliyeti 787,3 → 487,5 ms). Bunun bedeli ilk hover'da bir kurulum işidir.

- [ ] Uzun bir listede (191 satır) **daha önce hover etmediğin** bir satırın üzerine gel → klasör + VS ikonları **anında** çıkmalı; **gözle görülür bir gecikme / takılma HİSSEDİLMEMELİ**.
- [ ] Aynı satırdan çık, tekrar gel → ikinci hover'da hiç fark olmamalı.
- [ ] Listeyi hızlıca hover ederek tara (fareyi satırlar boyunca sürükle) → biriken bir yavaşlama yok.

## 3.7 Liste ilk realize (It-4 kabul #5 — kapandı, ölçüldü)

- [ ] Repo seç → liste ilk kez dolarken **gözle rahatsız edici bir bekleme** var mı? ℹ️ **Ölçüm:** 191 satır medyan **487,5 ms** (öncesi 787,3 ms). **400 ms bütçesi TUTMADI**, ama **kullanıcı kararıyla virtualization (L2) AÇILMADI**. Bu satır bir kabul kapısı değil, kararın gözle teyididir — "kabul edilemez" hissediyorsan bunu bildir.

---

# Sapma kaydı

Yürürken bulduklarını buraya yaz. Her satır: **nerede · ne bekleniyordu · ne gördün · nereye gidecek**.

| # | Nerede (bölüm/madde) | Beklenen | Görülen | Karar |
|---|---|---|---|---|
| 1 | | | | ☐ düzelt (fix wave) / ☐ A13.1'e ekle |
| 2 | | | | ☐ düzelt (fix wave) / ☐ A13.1'e ekle |
| 3 | | | | ☐ düzelt (fix wave) / ☐ A13.1'e ekle |

**Yönlendirme:**
- **☐ düzelt** işaretlenenler → It-5 kapanışında bir **fix wave** olarak açılır.
- **☐ A13.1'e ekle** işaretlenenler → `.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md` **§5**
  tablosuna yeni madde (ne farklı · neden kapanamıyor · algısal etkisi) olarak yazılır.
