# Boşta bellek: perf çalışması sonrası regresyon var mı? — inceleme raporu

**Tarih:** 2026-10-19, 11:10–12:07 · **Soru:** v1.10.0/1.10.1 ile boşta ~300 MB'a inen bellek şimdi "500'lerde" görünüyor; perf
çalışmasından sonra develop'a giren işler etkilemiş olabilir mi? Yalnız inceleme, kod değişikliği yok. Worktree `-ai` (develop-ai →
develop e1ebbdfd) kullanıldı.

**Tek cümle:** Hayır — aynı 4K ekranda, aynı OSYS köküyle, sırayla (ABAB) ölçüldü: develop ile v1.10.1 boşta ve 12 proje sayfası
tıklamasından sonra aynı bantta; "300 → 500" farkı ölçünün ve koşulun değişmesinden geliyor (toplam ↔ yalnız App, kullanım sonrası,
motor uyanık, 4K, açılıştan açılışa sapma). Kullanıcının 500'leri gördüğü **Clean → Resolve cycles** akışında da eski ↔ yeni eşit:
bitişten 10 sn sonra 432 ↔ 414 MB (§6); 500'ler koşunun son saniyelerinde derleyiciler çıkarken ya da koşu sırasında görülen toplamdır.

## 1. Sürüm notları

- Son yayınlanan sürüm **v1.10.1** (2026-10-16); kurulu kopya da 1.10.1 (`%LocalAppData%\BuildOrchestrator.App\current`, 16 Ekim 17:27).
  Bellek işi v1.10.0'ın *Performance* notu (işlem sonu trim, anlatı diskte, motor uykusu) ve v1.10.1'in *Fixed* notu (3B geçiş → 2B).
  İki iddia bugün yeniden doğrulandı: motor 5 dk işsizlikten sonra uyudu (S0'dan 244 s sonra); 4K'da proje sayfası açmak belleği
  1 GB'a çıkarmadı (en çok 401 MB).
- v1.10.1'den sonra develop'a üç merge girdi, hiçbiri yayınlanmadı ve kural gereği CHANGELOG'a yazılmadı: `7542cdd9` (Resolve cycles
  profili aynen uygular — ayar kalktı), `a3ecfa0a` (title bar kaynak şeridi), `e1ebbdfd` (şerit sonrası temizlik). Sürüm çıkarılınca
  Added / Changed / Removed olarak girecek.

## 2. Kod incelemesi (v1.10.1..develop: 29 commit, 34 `src` dosyası)

- App'in bellek yolu değişmedi: işlem sonu trim kablajı (`RequestTrimWhenDue` → `MemoryTrim.CollectAndRelease`), anlatı diski, motor
  uykusu ve uyanması aynen; `MainWindow.HiddenSurface.cs`'e yalnız örnekleyicinin durdur/başlat çağrısı eklendi.
- Yeni canlı parçalar: `ResourceSampler` (3 sn'de bir okuma, ısınma sonrası örnek başına 0 bayt, 256 int'lik PID tamponu ve bir
  `Process` nesnesi; pencere gizli/küçültülmüşken durur), title bar şeridi (üç hücre + tooltip), ikon ve token'lar.
  `MemoryLine` artık `GetProcessMemoryInfo` okur; eski sayaç her çağrıda makinedeki tüm süreçleri tarıyordu, yeni değer ~1 MB düşük.
- Diff taraması: `Viewport3D`, `BitmapCache`, `Effect`, yeni `Timer`/`Thread`, `ArrayPool`, `PerformanceCounter` eklenmedi.
- **Şeridin RAM hücresi toplamdır** (§11.5): App + motor + outer job'daki diğer her süreç (motorun `conhost.exe`'si dahil). Dalga
  raporlarındaki "236 MB" (dizüstü paneli) ve v1.10.1'in "294–318 MB" (4K) rakamları yalnız App'ti; motor ayrı yazılmış ya da uykudaydı.

## 3. Ölçüm

Koşul: 4K harici monitör birincil (DISPLAY2 3840×2160), pencere büyütülmüş, kök `D:\Projects\Delta\OSYS`, kullanıcının `ui-state.json`'ı
(StopEngineWhenIdle açık, Balanced, Debug); başka Build Orchestrator kopyası kapalı. Script `.claude/temp/mem-2026-10-19/mem-abab.ps1`,
ham log `r1/mem-abab.log`, şeridin kendi kaydı (`BO_PROBE_STRIP`) `r1/strip-B-r*.txt`. Kopyalar: **A** kurulu v1.10.1 (güncelleme
denetimi açılıştan 5 sn sonra koşar) · **A1** v1.10.1 tag'den `bin\Release` (denetimsiz) · **B** develop `e1ebbdfd` `bin\Release`
(worktree). S0 = pencereden 60 s sonra (açılış Sync'i bitmiş, motor uyanık); S4 = motor uyuduktan 20 s sonra. Ölçü: private bytes
(commit charge — şeridin ve motorun `memory:` satırının ölçüsü).

| Tur | Kopya | App | motor | conhost | toplam = şeridin gösterdiği |
|---|---|---|---|---|---|
| 1 · S0 | A kurulu 1.10.1 | 351 | 38 | 1 | 390 |
| 1 · S4 | A, motor uyudu | 351 | 0 | 0 | 351 |
| 1 · S0 | A1 1.10.1 bin | 299 | 39 | 1 | 339 |
| 1 · S0 | B develop | 292 | 39 | 1 | 332 (şerit kaydı 332) |
| 1 · S4 | B, motor uyudu | 294 | 0 | 0 | 294 (şerit kaydı 294) |
| 2 · S0 | A kurulu | 299 | 39 | 1 | 339 |
| 2 · S0 | A1 | 300 | 40 | 1 | 341 |
| 2 · S0 | B | 295 | 38 | 1 | 334 (şerit kaydı 334) |

12 proje sayfası tıklama turu (`mem-2026-10-13/mem-project-click.ps1 -Rounds 12 -OnPrimary`; `click-B/`, `click-A1/`; her tur
bir satır seç → sayfa → Back, 10'ar sn), App private:

| Kopya | taze (30 s) | 12 tur sonra, 20 s boşta | GPU committed |
|---|---|---|---|
| B develop | 347 | 394 | 232 → 273 |
| A1 v1.10.1 | 288 | 363 | 196 → 248 |
| v1.10.1, 16 Ekim (click9, aynı ekran) | 290 | 387 | 197 → 252 |

Bulgular:

1. **develop = v1.10.1.** Taze açılış 292–295 ↔ 299–300; tıklama sonrası 394 ↔ 363–387. Perf sonrası işler boşta belleği oynatmadı.
2. **Açılıştan açılışa ±50 MB sapma, iki sürümde de.** Kurulu kopya ilk açılışta 351 (4,5 dk sonra da 351), ikinci açılışta 299;
   develop tıklama koşusunda taze 347, ABAB turlarında 292–295. Yüksek açılışlarda GPU committed de yüksek (232 ↔ 196 MB): çizim
   yolu; nedeni ölçülmedi. Kurulu kopyanın güncelleme motoru ölçülebilir fark yaratmadı (2. tur 299 = bin kopyası). Paket
   framework-dependent ve klasör tabanlı (`package.ps1`: `--self-contained false`), runtime yerleşimi bin'le aynı.
3. **Motor uyanıkken toplam +39–41 MB** (koşudan sonra 56–74, Dalga 3 raporu); conhost +1. Uykuda 0.
4. **4K ekran** dizüstü paneline göre App'e ~+120 MB çizim belleği ekler (16 Ekim notu); bugünkü bütün ölçümler 4K'da.

## 4. "500" nereden geliyor (kod değişmeden)

| Parça | MB |
|---|---|
| App, kullanım sonrası (proje sayfaları / koşu), 4K | 365–400 |
| motor uyanık, koşu sonrası | 56–74 |
| conhost | 1 |
| **toplam (şerit · Task Manager uygulama satırı)** | **420–475** |
| yüksek açılış sapması (+50) | → 470–525 |

Eski "300'ler": App tek başına, taze açılış, motor uykuda (ya da ayrı yazılmış). Yeni "500'ler": büyük olasılıkla toplam — şeritli bir
develop derlemesinde (ana checkout'ta 17 Ekim 20:20 tarihli Debug bin var) ya da Task Manager'ın Processes sekmesinde (App + Supervisor +
conhost tek satırda toplanır) — koşulardan sonra, motor uyumadan okunmuş. Bu sabah 09:22–09:34 arası dört koşu var; log hangi exe'nin
koştuğunu yazmıyor.

## 5. Yapılmayanlar ve sınırlar

- OSYS'te Build/Rebuild koşturulmadı; kullanıcının isteğiyle Clean + Resolve cycles koşturuldu (§6) — OSYS'in bin/obj'si silindi ve
  döngü kapsamı (88 proje) yeniden derlendi; `UpdateExternals` açık olduğundan koşu başında dört harici kök ff-only güncellendi.
  "Rebuild sonrası" rakamları Dalga 3 raporundan (`2026-10-16-13-25-idle-memory-wave3-result.md`).
- Açılış sapmasının (GPU/çizim) nedeni araştırılmadı — istenirse ayrı iş.
- Kod değişikliği yok; worktree `develop-ai`'de temiz; `ui-state.json` bayt bayt aynı (hash kontrolü); artık süreç yok. Ölçüm araçları
  ve ham veri `.claude/temp/mem-2026-10-19/` altında (git dışı).

## 6. Clean → Resolve cycles akışı (kullanıcının 500'leri gördüğü an)

Kullanıcı 500'leri Resolve'dan sonra gördüğünü söyledi; Resolve'a dokunan tek iş `7542cdd9` (Resolve cycles artık profilin CPU tavanı
ve önceliğini aynen uygular; bellek yoluna dokunmaz). Aynı 4K ekranda, OSYS'te bakım kutusunun Clean'i (bin/obj + defter sıfırlama,
ardından Sync) → Resolve cycles iki ikilide de UI Automation ile tetiklendi (`mem-clean-resolve.ps1`; loglar
`cr/clean-resolve-A1.log`, `cr/clean-resolve-B.log`; şeridin kendi kaydı `cr/strip-B.txt`). İki koşu da 88 derledi / 1 düştü /
106 atladı; süre eski 355 s (cpuCap=off), yeni 136 s (Balanced, cpuCap=70% — ikinci koşuda NuGet/MSBuild önbellekleri sıcaktı).
Derleme modeli aynı: 88 projede komut satırı `csc.exe`, 14 restore.

| An | eski v1.10.1 (A1) | yeni develop (B) |
|---|---|---|
| açılış Sync sonrası (App + motor) | 295 + 42 = 338 | 295 + 40 = 336 |
| Clean + Sync sonrası | 286 + 47 = 334 | 307 + 47 = 355 |
| Resolve SIRASINDA, App tek başına | 397–429 | 404–412 |
| Resolve sırasında toplam (App + motor + derleyiciler), 30 sn örnekler | 498–1132 | 506–608 |
| bitiş + 10 s | 385 + 46 = **432** | 392 + 21 = **414** |
| bitiş + 60 s / + 180 s | 428 / 427 | 409 / 413 |
| motor uyudu (+ 5 dk), App tek başına | 385 | 396 |

Şeridin kendi kaydı (develop, 3 sn'de bir): koşu boyunca 500–2138 MB (csc/MSBuild dahil), koşunun son saniyelerinde 1363 → 555 → 438,
bitişten 10 sn sonra 414 ve orada kalıyor; motor uyuyunca 396.

**Sonuç:** Resolve sonrası boşta bellek eski ve yeni sürümde aynı (432 ↔ 414; App 385 ↔ 392–396). "500'ler", koşu biterken
derleyicilerin çıktığı birkaç saniye (438–555) ya da koşu sırasındaki toplamdır; kod değişmeden, v1.10.1'de de görülür.

Harness notu: ilk develop koşusunda Clean, düğme bir anlığına devre dışıyken tetiklendi (`ElementNotEnabledException`); script sürdü ve
Resolve temizlenmemiş ağaçta 6 sn'de bitti (74 "up to date"). O koşu geçersiz sayıldı (`-invalid` dosyaları), script sertleştirildi
(etkinleşmeyi bekle, yeniden dene, defter mtime + bin/obj sayısıyla doğrula) ve tekrarlandı. Tek başına probe (`clean-probe.ps1`,
`cr/clean-probe-B.png`) develop'un Clean'inin tam çalıştığını gösterdi (defter sıfırlandı, 79 projenin bin/obj'si silindi, 195 satır
"never built").
