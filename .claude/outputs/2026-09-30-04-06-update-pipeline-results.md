# Yayın hattı + uygulama içi güncelleme — uygulama sonucu

Plan: `.claude/outputs/2026-09-30-04-06-update-pipeline-tdd-plan.md` · Spec: `.claude/outputs/2026-09-29-22-10-update-pipeline-analysis.md`
(K1–K12 kullanıcı onaylı) · Branch `feat/release-pipeline` (worktree), `main` @ `9acc01f` üstüne **40 commit, 63 dosya, +5792/−576**.
Yürütme: subagent-driven development (P1 3, P2 6 toplu dispatch; her biri implementer → reviewer → düzeltme turu), branch geneli son
inceleme + iki düzeltme dalgası. Ledger ve tüm raporlar: `.superpowers/sdd/2026-09-30-04-06-update-pipeline-tdd-plan/` (git-ignored).

## Yapılanlar

**Yayın hattı (P1)**
- `LICENSE` (MIT), `global.json` (SDK 10.0.400, `latestFeature`), `.config/dotnet-tools.json` (`vpk` 1.2.161; csproj'daki `Velopack`
  paketiyle eşitliği guard testi pinler).
- `Program.cs`: `VelopackApp.Build()…Run()` Main'in ilk ifadesi, `<StartupObject>`; kaldırma kancası `HKCU\…\Run` değerini siler
  (`AutostartService.RemoveForUninstall`). WPF'in ürettiği Main devre dışı.
- `scripts/package.ps1`: publish komutunun TEK sahibi (`verify-publish.ps1` buradan alır) + CHANGELOG bölümünü `artifacts\notes.md`'ye
  keser + `vpk pack` (packId `BuildOrchestrator.App`, `--framework net10.0-x64-desktop`, `--noPortable`, varsayılan kısayollar
  Desktop + Start Menu, delta için `-DownloadPrevious`); aynı sürüm yeniden paketlenebilir (o sürümün paketleri + vpk indeksleri
  silinir, önceki sürümler delta için kalır); `-WhatIf` hiçbir yan etki ve ağ dokunuşu yapmaz.
- `scripts/release-common.ps1` (props okuma, CHANGELOG regex, repo URL, `Get-ReleaseCount` (5.1'de boş liste 0 sayılır),
  `Get-RunningApp -UnderPath`, `Remove-PackagedVersion`), `scripts/release-guard.ps1` (tag == Version == CHANGELOG en üst;
  `-RequireOnMain` tag'in `origin/main` atası), `scripts/release.ps1` (`main`'de/temiz/origin ile eşit, uzak tag yok, CHANGELOG bugünün
  tarihli bölümü, bu checkout'tan çalışan uygulama yok → `Version` → build + tam süit → `release: vX.Y.Z` commit'i → annotated tag →
  `git push --atomic origin main vX.Y.Z`).
- `.github/workflows/ci.yml` (`main` push + PR + `workflow_call`; `windows-2025` pinli; `Category!=Acceptance&Category!=LocalOnly`;
  `permissions: contents: read`) ve `release.yml` (`v*` tag → guard job (`-RequireOnMain`, `fetch-depth: 0`) → ci → publish job
  (`dotnet tool restore`, `package.ps1 -DownloadPrevious`, `vpk upload github --publish --merge`, `gh release edit --notes-file
  artifacts/notes.md`); `concurrency: release`; yazma izni yalnız publish job'ında; gizli anahtar yok).
- `/release` skill'i (`.claude/skills/release/SKILL.md`): ana proje checkout'unda `main` üzerinde; ilk `/release` öncesi CI `main`'de
  yeşil + `LocalOnly` triage'ı; push reddedilirse ve tag push'undan sonra yayın düşerse kurtarma adımları. CLAUDE.md "Sürüm çıkarma"
  adım 5 script + CI akışına bağlandı; release commit'i doğrudan `main`'de (tek istisna); `LocalOnly` kuralı.

**Uygulama içi motor (P2)**
- `Services/Updates/IAppUpdater` (+ `UpdateCandidate`), `UpdateFeed` (GitHub Releases sabiti; `BO_UPDATE_SOURCE` klasör/URL ve
  `BO_UPDATE_PRERELEASE=1` dev kapıları; bozuk değer GitHub varsayılanına döner), `UpdateService` (kurulu kopyada pencere sonrası
  5 s, sonra 4 saat; sessiz kontrol → arka planda indirme → teklif yalnız indirme bitince; hata sessiz, sonraki tur; önceki oturumdan
  bekleyen paket açılışta teklif; `RequestRestart`; `ApplyOnExit` — Restart istendiyse yeniden açar, Later ise sessiz kurar),
  `VelopackUpdater` (`UpdateManager` sarmalayıcısı; `Lazy` + `VelopackLocator.IsCurrentSet` kapısı; yalnız teklif edilen sürüm kurulur;
  `WaitExitThenApplyUpdates(silent: true)`).
- `UpdateOffer.From`: boyut MB tek ondalık, KindOrder'da en çok 5 madde + `MoreCount`; `Sample`/`NextMinor` placeholder kalktı; VM
  varsayılanı `null` → hap yalnız indirilmiş teklifle görünür. Kart: `+N more in What's new after restart` (tıklanmaz), öne çıkan yokken
  bant gizli, UIA adı `Update to <v>`.
- Restart akışı (K6): timeline tek adım `Closing <ürün>…` (800 ms'de çubuk dolar), ekran sönmez; çubuk dolunca güvenli tam çıkış
  istenir (`BarFilled` → `RequestFullExit`); kurulum pencere kapandıktan sonra Update.exe'de. `EngineHost.KillAndAwaitExit`: öldürülen
  Supervisor'ın çıkışı ≤1 s beklenir (Update.exe `current\supervisor\*.dll` kilidine takılmasın); startup-timeout yolu
  `ConfigureAwait(false)` ile UI thread'ini bloklamaz.
- `App.xaml.cs`: DI (`IAppUpdater`, `UpdateService`), `Start()` pencere gösterildikten sonra, `RestartToUpdateRequested → RequestRestart`,
  `OnExit`: motor kapanışı → tepsi/mutex temizliği → `ApplyOnExit` (sessiz) → `base.OnExit`.
- Dokümanlar: README (Install, Update, Package and release, Licence, Requirements ayrımı, CI rozeti), ARCHITECTURE (§1.3, §4.2/4.4,
  §12.1, §12.3, §12.4, yeni **§12.5 Distribution and updates**, §13.3, §14.5, §16, §17.1/17.2/17.5, yeni **§17.6 Update rehearsal**,
  §18 "Build, run, package, release", §21 ağ yüzeyi, §22 kod haritası), CLAUDE.md değişmezleri.

## Doğrulama

- Tam süit (Release, worktree, `de22ddd`): **4061 geçti / 0 düştü / 7 atlandı** (ortam kapılı ölçüm testleri), 4 dk 51 s; build 0 uyarı.
  Önceki iki tam koşuda süit yükünde düşen `StickyLayerHeaderClickTests` (6), `EngineSilenceWatchdogTests.Restarting…` ve
  `KillMidBuildTests.Kill_mid_parallel…` tek başına ve tekrarlı koşularda yeşildi; üçüncü koşuda hepsi geçti.
- Gerçek paketleme (`package.ps1`, sürüm 1.7.0): `BuildOrchestrator.App-win-Setup.exe` **12,0 MB**, `…-full.nupkg` **4,5 MB**, publish
  klasörü 6,0 MB; vpk `Program.Main`'deki `VelopackApp.Run()`'ı doğruladı; `releases.win.json` kaydı CHANGELOG bölümünü `NotesMarkdown`
  olarak taşıyor (kart öne çıkanlarının gerçek kaynağı). Aynı sürümü ikinci kez paketleme ve çok sürümlü klasörde delta üretimi doğrulandı.
- Branch geneli son inceleme: Critical 0, Important 2 (UI thread bloğu; tag sonrası runbook), Minor 13 → düzeltme dalgası (7 commit) +
  kapsamlı yeniden inceleme: 13/13 kapandı. Kapanış doğrulamasının kalıntıları (aynı-sürüm paketleme, sonda kapsamı, 3 küçük) → ikinci
  dalga (6 commit) + yeniden inceleme: 5/5 kapandı, yeni kırılma yok (yalnız kozmetik notlar: bir assertion tekrarı, uzun doküman
  satırı).
- **CI (`ci.yml`, `windows-2025` = Windows Server 2025 + Visual Studio 2026 18.10 imajı):** build her koşuda 1 dk'da geçti (SDK
  `global.json` bandı, restore, WPF). Test triage üç koşu sürdü — #3: 11 düşüş (runner'da global git kimliği yok → klonlardaki test
  commit'leri "Author identity unknown", 4 test; legacy fixture `v4.6` targeting pack imajda yok → MSB3644, 6 test; workflow guard
  testi CRLF checkout'ta `(?m)$` tutmadı, 1 test) · #4: 7 düşüş (runner'da çıplak bir `v4.6` klasörü var, pack değil → seçim yine
  ona düştü; UI bütçe testi paylaşımlı CPU'da 71 ms > 50 ms) · **#5: yeşil** (4053+ test). Düzeltmeler test altyapısında:
  `GitTestRepo.ConfigureIdentity` klonlara da kimlik yazar (tek yer); `LegacyFixture.TargetFrameworkVersion` makinedeki GERÇEK
  targeting pack'lerden seçilir (v4.6 varsa o, yoksa `RedistList\FrameworkList.xml` taşıyan en yeni 4.x — MSBuild'in ölçütü),
  düşen derleme mesajına MSBuild hata satırları + pack envanteri girer; `RepoHygieneTests.Workflow()` satır sonlarını LF'e indirir;
  `UiResponsivenessBudgetTests` **`LocalOnly`** (eşik gevşetilmedi; lokal tam süit koşturur). CI'daki `LocalOnly` kümesi bugün bu
  tek sınıftır.
- Kurulu kopyada güncelleme provası (§17.6) ve temiz makinede Setup **yapılmadı** — kullanıcının makinesinde.

## Kararlar (plan dışı, ledger'da gerekçeli)

- `scripts/release-common.ps1` eklendi (kopya yasağı) · `release-guard.ps1 [-ChangelogTop]` yazılmadı (tüketici yok) · CLAUDE.md §Git'e
  "release commit'i main'de" istisnası tek satır · `KillStrategy` dikişi ctor parametresi (`SupervisorIsolationGuardTests`) ·
  `new UpdateManager` locator yokken fırlatır → `Lazy` + `IsCurrentSet` kapısı · `UpdateService.IsNewer` Velopack `SemanticVersion`
  (ön sürüm karşılaştırması; K9) · Restart: çubuk dolunca (800 ms) çıkış istenir — "Closing…" ekranı gerçekten çizilsin ·
  `Get-RunningApp -UnderPath`: `/release` yalnız bu checkout'tan çalışan kopyayı sayar (kurulu tepsi kopyası engel değil) ·
  `--releaseName "Build Orchestrator …"` YAML'de sabit (props okunamaz) · bozuk `BO_UPDATE_SOURCE` → GitHub varsayılanına sessiz dönüş.
- **Parked (CAN WAIT):** aday eşleşmezse `ApplyOnExit` Update.exe'yi başlatmaz → Restart demiş kullanıcı uygulamayı elle açar (§12.5'te
  yazılı, çok nadir) · `Get-ReleaseCount` draft/prerelease'i de sayar (K9 açılınca) · `ReleaseScriptsTests` sandbox'ı gerçek `dotnet build`
  koşar (~7 s) · test hijyeni artıkları (telif adı testte literal; `Dispose_waits…` adı; `KillCurrent→KillAndAwaitExit` bağı pinsiz;
  `WaitForExit` dönüşü; `UpdateService` DI'da iki kez çözülür; `RestartRequested` yapışkan; `RestartAsync` yolunda yazma senkron
  tamamlanırsa `KillCurrent` UI thread'inde koşabilir — yaygın değil).

## Kullanıcının adımları (K7: v1.8.0 = installer + motor)

1. ~~İlk CI koşusu ve `LocalOnly` triage'ı~~ — yapıldı: `ci.yml` `main`'de yeşil (koşu #5). İlk `/release` için ön koşul tamam.
2. GitHub hesabında 2FA açık mı doğrula (yayın kanalı = hesap). İsteğe bağlı: `main` ruleset (force-push/silme kapalı), `v*` tag ruleset.
   Secret gerekmez.
3. Yerel prova (ARCHITECTURE §17.6): `package.ps1` → Setup → sürümü artırıp tekrar `package.ps1` → `BO_UPDATE_SOURCE=<klasör>` ile
   kurulu kopya → hap → Restart → yeni sürüm + What's new noktası + autostart değeri. Setup'ı **.NET 10 Desktop Runtime olmayan** temiz
   makinede de dene (`--framework net10.0-x64-desktop` bootstrap'ı).
4. Tasarım tarafına: restart ekranı tek adım (K6), kart 5 madde + "+N more" (K5), kaynak GitHub Releases.
5. Kullanıcılara duyuru: eski kopyayı kapat → Setup.exe (SmartScreen "More info → Run anyway") → eski klasörü sil; ayarlar
   `%LocalAppData%\BuildOrchestrator\` altında kalır.
6. `/release` (ana proje checkout'unda, `main`'de). Sonra: SignPath başvurusu (LICENSE + ilk yayın + MFA).
