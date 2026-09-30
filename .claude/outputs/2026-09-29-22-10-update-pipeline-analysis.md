# Güncelleme ve yayın hattı — sıfırdan analiz ve plan

Tarih: 2026-09-29 · Durum: **analiz + plan, kod yok** (kararlar §9'da, onay bekliyor) · Branch: `docs/update-pipeline-analysis`
(worktree `app_build_orchestrator-ai`).

Kapsam: Build Orchestrator'ın (WPF, .NET 10, public + ücretsiz GitHub reposu) **tek komutla sürüm çıkarma → derleme → sürüm
notu → yayın → kullanıcıya düşme → title bar'daki `Update` hapı → yeniden başlatma** zincirinin tamamı; buna uygun **branch /
tag / CI** düzeni. Eski analiz (`2026-09-09-00-35-distribution-and-auto-update-plan.md`) fikir kaynağı olarak okundu, karar
buradan **yeniden** türetildi (§2.6 eskiyen iddialar). Tasarım tarafında v1.23.0 güncelleme hapı ana projede uygulanıyor; bu
belge o UI'ı **var sayar** ve ona motor bağlar.

Her iddia ya kodda (dosya:satır) ya da kaynakta (§10) doğrulandı; doğrulanamayan noktalar açıkça "**doğrulanacak**" etiketli.

---

## 0. Tek sayfada özet

**Karar:** Paketleme + güncelleme motoru **Velopack**, yayın yeri **GitHub Releases**, otomasyon **GitHub Actions**, branch düzeni
**GitHub Flow** (yalnız `main` + kısa ömürlü iş branch'leri — `develop` YOK), sürüm notlarının tek kaynağı **`CHANGELOG.md`**
(bugünkü gibi; notu **Claude** yazar, gerisini script + CI yapar).

```
 sen: "/release"            Claude                 scripts/release.ps1                 GitHub Actions (release.yml)
 ──────────────►  notları yazar, numarayı  ──►  guard'lar · tam süit · commit    ──►  guard · build · test · publish
                  seçer (CHANGELOG + props)       "release: vX.Y.Z" · tag vX.Y.Z        · vpk pack (delta dahil)
                                                  · push main + tag                     · GitHub Release + asset'ler
                                                                                        · release gövdesi = CHANGELOG bölümü
                                                                                                    │
                        kullanıcı makinesi                                                          ▼
   ◄── açılış + her 4 saatte sessiz kontrol ── indirme arka planda ── hazır → title bar `Update 1.8.0` hapı
       kart: kurulu → gelen, boyut, öne çıkanlar (aynı CHANGELOG bölümü), Later / Restart to update
       Restart: "Closing…" → uygulama düzgün kapanır → Update.exe `current\`'ı değiştirir → yeni sürüm açılır
       Later:   hap kalır; uygulama kapanırken sessizce kurulur → bir sonraki açılış yeni sürüm
```

| Konu | Karar | Bir cümlede neden |
|---|---|---|
| Paket + güncelleme motoru | **Velopack** (MIT, bugün 1.2.161 yayınlandı — canlı proje) | GitHub Releases'ı yerleşik kaynak olarak okur, delta günceller, runtime bootstrap yapar, atomik değiştirme + "sonraki açılışta kur" hazır gelir; tasarımın (§2.12) varsaydığı davranışların hepsi birebir karşılığı var |
| Yayın yeri | **GitHub Releases** (public repo → ücretsiz, sınırsız) | Velopack `vpk upload github` doğrudan yazar; kullanıcı `Setup.exe`'yi sabit `releases/latest/download/...` linkinden alır |
| CI/CD | **GitHub Actions**, `windows-2025` runner'a **pinli** | Public repo'da ücretsiz; imajda VS 2022 + MSBuild + vswhere + .NET 10 SDK + .NET Framework 4.6 targeting pack + `gh` var |
| Branch düzeni | **GitHub Flow**: `main` + `feat/…` `fix/…` `chore/…`; PR isteğe bağlı; `develop`/`release` branch'i YOK | GitHub Flow'da develop yoktur (o Git Flow'dur); tek geliştiricide ek branch yalnız tören ekler. Zaten böyle çalışıyorsun |
| Sürüm | **SemVer**, tek kaynak `Directory.Build.props` → `Version`; tag `v` + Version; CHANGELOG en üst = Version (guard var) | Bugünkü kural korunur; Velopack `--packVersion` da aynı değeri alır |
| Sürüm notu | **`CHANGELOG.md`**, sürüm anında **Claude yazar** (`/release`), kaynak = merge mesajları; `[Unreleased]` YOK | Commit'ler Türkçe/teknik, not İngilizce/kullanıcı dili → otomatik üreticiler (release-please, git-cliff, GitHub notes) uymaz; parser da `[Unreleased]`'ı reddeder |
| Aynı not üç yere akar | exe'ye gömülü (What's new) · GitHub Release gövdesi · Velopack `--releaseNotes` → **hap kartındaki öne çıkanlar** | Tek kaynak, kopya yok; kart mevcut `ReleaseNotes.Parse` ile okur |
| Tek komut | `/release` (proje skill'i) → `scripts/release.ps1` → tag push → CI | Sen tek şey söylersin; notu yazan zekâ Claude, geri kalanı deterministik script/CI |
| Uygulama içi motor | `Program.cs` + `VelopackApp.Build().Run()`; `IAppUpdater` seam; `UpdateService` (açılış + 4 saat); hap yalnız **indirilmiş ve hazır**ken | Tasarım §2.12 ilkesi "yalnız hazırken görünür" ile aynı; Velopack'in `UpdatePendingRestart` + `SetAutoApplyOnStartup` modeli bire bir |
| Kurulum yerleşimi | per-user `%LocalAppData%\BuildOrchestrator.App\` (packId) — state dizini `%LocalAppData%\BuildOrchestrator\` ile **çakışmaz** | Velopack kaldırırken packId klasörünü siler; ayarlar/önbellek/loglar ayrı kalır |
| İmza | v1 **imzasız** (SmartScreen bir kez "Run anyway"); sonra **SignPath Foundation** (OSS'e ücretsiz) | Azure Artifact Signing bireysel geliştiriciye yalnız ABD/Kanada → uygulanamaz (doğrulandı) |
| İlk yayın | **v1.8.0 = installer + motor birlikte** | Motorsuz kurulan sürüm kendini güncelleyemez; ilk kurulan sürüm motoru taşımalı |

---

## 1. Hedef ve başarı ölçütü (senin sözlerinle)

- "Beni yormadan bir komutla yeni versiyon oluştur dediğimde sistem derlenip versiyon notlarını belli kurallara göre yazıp
  publish olmalı, kullanıcılara düşmeli."
- "Güncelle butonu üst barda çıkmalı, versiyon notlarını görebilmeli."
- "GitHub, free ve public. Tek ben geliştiriyorum, ayrı geliştirdiğim yerde test ediyorum; canlıya gitme / tag / test /
  derleme süreçlerini oturtalım, karmaşıklaştırmadan." GitHub Flow'a uyum.

**Başarı:** (1) `/release` dedikten sonra senin yapacağın tek iş CI'ın yeşil bittiğini görmek; (2) kullanıcı makinesinde en geç
4 saat içinde hap belirir, Restart ile ~10 saniyede yeni sürüm açılır, Later derse bir sonraki açılış yeni sürümdür; (3)
`main` her an yayınlanabilir, her yayın tag'lidir, her yayının notu tek yerdedir.

Kabul ettiğim varsayımlar (yanlışsa düzelt): kullanıcılar ofis içi .NET geliştiricileri (VS/Build Tools + git zaten var);
kullanıcı sayısı onlar mertebesinde; yayın sıklığı haftada birkaç.

---

## 2. Bugün elde ne var (kodla doğrulandı)

### 2.1 Sürüm kimliği ve notlar — büyük ölçüde hazır
- `Directory.Build.props:14` `<Version>1.7.0</Version>`; `:17` `IncludeSourceRevisionInInformationalVersion=false` → ekran
  yalın `1.7.0`; `:18-19` `Product=Build Orchestrator`, `Company=Delta`. `AppIdentity.Version` InformationalVersion'ı okur
  (`Services/AppIdentity.cs:20-22`). Velopack'in istediği temiz SemVer **zaten var**.
- `CHANGELOG.md` Keep a Changelog biçiminde, 0.1.0→1.7.0 arası 11 sürüm, `[Unreleased]` yok. Exe'ye gömülü
  (`App.csproj:151`), `ReleaseNotes.Parse` okur (`Services/ReleaseNotes.cs:141-215`): başlık regex'i
  `^## \[x.y.z\] - yyyy-MM-dd$`, `[Unreleased]`/ön-sürüm başlığı **FormatException**; başlıksız **tek bölüm** parse edilir
  (`WhatsNewTests.cs:96-107`) → Velopack feed notunu aynı parser okuyabilir.
- Guard'lar: CHANGELOG en üst == `Version` (`WhatsNewTests.cs:44-51`), biçim/sıra/karakter (`ChangelogTests.cs`),
  InformationalVersion ek almaz (`PublishLayoutTests.cs:195-205`).
- What's new'in görülmemiş-sürüm noktası `UiState.SeenVersion` ≠ `AppIdentity.Version` (`MainWindow.xaml.cs:496-497`) →
  güncelleme sonrası **kendiliğinden** geri gelir; ek iş yok.
- CLAUDE.md "Sürüm çıkarma" (`:122-138`): notu Claude yazar, numara kuralı patch/minor/major, tam süit → commit → merge →
  annotated tag → push. Tag'ler annotated ve mesajı sabit ("Build Orchestrator 1.7.0 — release notes in CHANGELOG.md").

### 2.2 Publish ve yerleşim
- Framework-dependent, klasör publish: `dotnet publish … -c Release -r win-x64 --self-contained false -o <klasör>`
  (README:99-102). Aynı komut **üç yerde** (README, ARCHITECTURE:5065, `scripts/verify-publish.ps1:104`) → paketleme
  script'i tek sahip yapılmalı (§5.4).
- `supervisor\` alt klasörü özel target'larla publish listesine girer (`App.csproj:66-124`); App onu
  `AppContext.BaseDirectory\supervisor\BuildOrchestrator.Supervisor.exe` olarak bulur (`Services/SupervisorLayout.cs:30-31`).
  Velopack publish klasörünü olduğu gibi paketler → ek iş yok.
- `PublishSingleFile` MSBuild hatasıyla reddedilir (`App.csproj:129-132`) — Velopack zaten klasör ister, uyumlu.
- `.gitignore`'da `artifacts/` var (L25) → paket çıktısı `artifacts/velopack/` altına alınırsa ignore değişmez.
- `global.json` yok; lokal SDK 10.0.400. `.github/`, `LICENSE`, herhangi bir GitHub Release **yok**; tag'ler v1.1.0…v1.7.0
  remote'ta.
- Debug bin ~12 MB (pdb dahil; `supervisor\` 2 MB) → paket ~10 MB mertebesi beklenir (**ilk paketlemede ölçülür**).

### 2.3 Process yaşam döngüsü — güncelleyici için kritik gerçekler
- **Giriş noktası** WPF'in ürettiği `Main()` (`obj/…/App.g.cs:68-75`); `Program.cs`/`StartupObject` **yok**. Velopack
  `VelopackApp.Build().Run()`'ın WPF ayağa kalkmadan **ilk satırda** çalışmasını ister → `Program.cs` + `<StartupObject>`
  eklenir (§5.7).
- Argümanlar: yalnız `--font-ab` ve `--autostart` tanınır, tanınmayan **yutulur** (`Shell/StartupArgs.cs:48-54`,
  `StartupPathTests.cs:38-43`). Velopack'in `--veloapp-*` kanca argümanları `Run()` içinde tüketilip process çıkar; WPF'e
  hiç ulaşmaz.
- **Tek örnek**: kullanıcıya özel named mutex + pipe (`Shell/SingleInstance.cs:39,62`), `OnStartup` içinde alınır
  (`App.xaml.cs:76`); ikinci örnek ilkini öne getirir ve çıkar. Mutex `OnExit`'te motor kapanışından **sonra** bırakılır
  (`App.xaml.cs:172-174`). Kanca process'leri `Run()`'da bittiği için mutex'e hiç dokunmaz.
- **Kapanış**: `App.OnExit` → `AppShutdown.WaitForAsyncDisposal(EngineHost, 2 s)` → `ShutdownCommand` (500 ms) → `Kill(tree)`
  → outer job `Dispose` (KILL_ON_JOB_CLOSE kaskadı) (`App.xaml.cs:169-176`, `Services/EngineHost.cs:196-234`). App,
  Supervisor'ın **gerçekten çıkmasını beklemez** (WaitForExit yok) → güncelleme yolunda kısa bir bekleme eklenir (§5.7).
  Tepsi → Exit ve × (close-to-tray kapalı) `RequestExit` → `Application.Shutdown()` üzerinden gider, `OnExit` koşar
  (`MainWindow.xaml.cs:1347-1351,410`). `Environment.Exit` hiçbir yerde yok.
- **Job nesneleri**: App outer job'ın **sahibi, üyesi değil** (`EngineHost.cs:56`, tek `Assign` çağrısı çocuk için
  `JobProcessLauncher.cs:73`; ARCHITECTURE:156). App'in `Process.Start` ile başlattıkları (explorer, devenv) job dışında
  (`Services/OsActions.cs:50-53`) → App'in başlattığı **`Update.exe` de App kapanınca yaşar**; tam istenen düzen.
- **Autostart**: `HKCU\…\Run` → `BuildOrchestrator` = `"<Environment.ProcessPath>" --autostart`
  (`Services/AutostartService.cs:47-52`, `App.xaml.cs:162-167`); her normal açılışta yeniden yazılır (`App.xaml.cs:131`) →
  exe yer değiştirirse kendini düzeltir (ARCHITECTURE:2364-2368). Velopack'te `current\` yolu güncellemeler boyunca sabit →
  değer bozulmaz; kaldırmada bu değeri silen bir kanca gerekir (§5.7).
- **State dizini** `%LOCALAPPDATA%\BuildOrchestrator\` (ui-state, build-state, önbellekler, loglar; dört ayrı yerde yazılı —
  `UiStateStore.cs:179-181`, `Supervisor/Program.cs:24-30`, `RunLogPaths.cs:8-9`, `LegacyWorktreePool.cs:11-12`) →
  paket kimliği bu adı **kullanamaz**.
- Uygulama kendini yeniden başlatan bir yol içermiyor (yalnız motor restart'ı var, `EngineHost.cs:159-163`).

### 2.4 Test süiti — CI için önemli
- ~3.700 test; tam süit (`Category!=Acceptance`) lokalde Release'te ~7,5 dk (`…-ui-function-inventory.md:274-284`).
- Kategoriler: ProcessControl, MsBuild, Measurement, Acceptance (3 test, gerçek OSYS), Integration, Wpf, Perf. Ölçüm/sonda
  testleri `BO_*` ortam değişkeni kapılı.
- Gerçek process kullananlar: MSBuild.exe (`LegacyFixture` **v4.6 legacy csproj**, `MsBuild/LegacyFixture.cs:45`),
  Supervisor.exe, git.exe, vswhere.exe. STA testleri ekran dışı gerçek `Window.Show()` yapar (`App/AnimationHost.cs:18-31`).
- **Kayıtlı kırılganlık:** zamanlama/bütçe testleri (`UiResponsivenessBudgetTests`, `ListRealizationPerfTests`,
  `PopoverTests`) paralel yük/pil gücünde düşmüş (`…-ui-gap-cleanup-results.md:24,47`, `…-build-menu-clean-all-results.md:58-64`).
  CI runner'ında (paylaşımlı 4 vCPU) aynı davranış beklenir → §5.5.

### 2.5 Tasarım v1.23.0 ne diyor, motor ne varsayıyor (`…design-v1.24.0/README.md`)
- `:362` **İlke:** "güncelleme yalnız gerçekten hazırken görünür. Uygulama açılıştan kısa süre sonra ve periyodik olarak
  besleme kaynağını sessizce kontrol eder, yeni paket arka planda iner; indirme sürerken UI'da hiçbir iz yok."
- `:374` kart öne çıkanları "**besleme kaydındaki** özet maddelerden gelir" → Velopack feed kaydının `NotesMarkdown` alanı.
- `:376` "Restart takes a few seconds and reopens the workspace. If you wait, it installs on the next start." ·
  `:378` "`Later` hapı gizlemez; hap kurulum yapılana dek kalır, bir sonraki açılışta güncelleme kendiliğinden kurulur."
- `:380` restart ekranı "…pencere kapanıp yeni sürümle açılana kadar gösterilen kısa ekran (**gerçekte güncelleyicinin splash'i
  bu tasarımı taşır**)"; `:382` "kontrol kaynağı (iç feed / paylaşım) ve aralığı yapılandırmadan gelir; kurulumda pencere
  gerçekten kapanır, güncelleyici yeniden başlatır."
- `:377` kilit: build / Sync / bakım görevi sürerken Restart disabled, sebep satırı değişir.
- Tasarımda **olmayanlar:** GitHub adı, kontrol aralığı, çevrimdışı davranışı, hata/geri alma, elle "Check for updates",
  Settings/About'ta güncelleme satırı, kapatma anahtarı, beta kanalı, öne çıkanlar için madde **sınırı**.
- Ana projede uygulanmış/uygulanan UI: `Services/UpdateOffer.cs` (`UpdateOffer(Version, Size, Highlights)` + `Sample`
  "PLACEHOLDER — the update engine is not written yet"), `ViewModels/RunViewModel.Update.cs` (`AvailableUpdate`,
  `UpdateRestartBlockedReason`, `RestartToUpdateRequested` olayı, `RestartToUpdateCommand`), `ViewModels/UpdateText.cs`,
  `Views/ReleaseNoteBlocks.cs` (kart ile What's new'in ortak blok çizimi), `Icon.UpdateReady`. Motorun bağlanacağı dikişler
  **hazır**.

### 2.6 Eski analizde (2026-09-09) eskiyen iddialar
| Eski iddia | Bugün |
|---|---|
| Sürüm `1.0.0+it5`, notlar statik C# listesi | `1.7.0`, temiz; notlar CHANGELOG'da — **Aşama 1 yapılmış** |
| `[Unreleased]` bölümü + her işte satır | Kural tersine döndü: not yalnız sürüm anında; parser `[Unreleased]`'ı reddeder |
| `ThirdPartyNoticesTests` guard'ı (Velopack atfı) | Guard ve About Third-party sekmesi **kaldırılmış** (`AboutDialogTests.cs:290-302`) |
| Title bar'da gizli ikon + `UpdateGate` tablosu | Yerine tasarım v1.23.0: hap + kart + kilit sebepleri + restart ekranı |
| 20 GiB worktree havuzu | Legacy; yalnız ipucu yazılır |
| Velopack + GitHub Releases + Actions, packId `BuildOrchestrator.App`, `Program.cs`, `WaitExitThenApplyUpdates` | **Geçerli** — bu analiz de bağımsız olarak aynı sonuca vardı |

---

## 3. Sektör standardı: WPF masaüstü dağıtımı ve güncelleme (ücretsiz, GitHub)

| Seçenek | GitHub Releases'tan güncelleme | Uygulama içi "Restart to update" | Delta | Runtime bootstrap | Atomik değiştirme / stub | İmza şartı | Durum (2026) | Sonuç |
|---|---|---|---|---|---|---|---|---|
| **Velopack** | **yerleşik** (`GithubSource`) | evet (`UpdateManager`) | evet | evet (`--framework`) | evet (`current\` + stub exe) | yok | canlı: 1.2.161 bugün, 2.3k★, MIT | **seçildi** |
| Squirrel.Windows / Clowd.Squirrel | kısmen | evet | evet | hayır | versiyonlu klasör | yok | bakımsız; Clowd → Velopack'e evrildi | elendi |
| MSIX + App Installer | `.appinstaller` barındırma gerek | OS yönetir, uygulama içi kontrol yok | evet | hayır (self-contained) | evet | **güvenilir sertifika zorunlu** | canlı | elendi (ücretli sertifika; ARCHITECTURE §1.3 non-goal) |
| ClickOnce | manifest barındırma gerek | kendi diyaloğu | hayır | sınırlı | evet | test sertifikası | eski | elendi (UI kontrolü yok) |
| NetSparkleUpdater | appcast XML (Pages/Release) | kendi UI'ı veya headless | hayır | hayır | **kurucu senin** (Inno/MSI) | yok (Ed25519 feed imzası artı) | canlı | elendi (kurucu + kurulum mantığı ayrıca yazılır) |
| AutoUpdater.NET | XML manifest | kendi WinForms diyaloğu | hayır | hayır | hayır (zip/installer indirir) | yok | canlı | elendi |
| Onova | GitHub zip | evet | hayır | hayır | hayır | yok | durgun | elendi |
| winget | dağıtım kanalı; uygulama içi güncelleme değil | — | — | — | — | manifest PR'ı | canlı | **sonra**, tamamlayıcı (§7 P4) |
| El yapımı (GitHub API + zip + swap) | yazarsın | yazarsın | yazarsın | yazarsın | yazarsın (+ rollback, kısayol, kaldırıcı) | yok | — | elendi (Velopack'i yeniden yazmak) |

Velopack'in bu proje için **doğrulanan** özellikleri (docs.velopack.io, §10): `VelopackApp.Build().Run()` Main'in ilk
satırı; `UpdateManager(string urlOrPath)` — URL, GitHub veya **yerel klasör** (test için `SimpleFileSource`); `IsInstalled`,
`CheckForUpdatesAsync`, `DownloadUpdatesAsync(progress)`, `UpdatePendingRestart`, `WaitExitThenApplyUpdates(asset, silent,
restart, restartArgs)` (Update.exe'yi başlatır, düzgün çıkış için 60 s bekler), `SetAutoApplyOnStartup` **varsayılan açık**
("indirilmiş güncelleme bir sonraki açılışta kurulur — yalnız ileri, downgrade/kanal değişimi yok"); `VelopackAsset.Size`,
`.NotesMarkdown`, `.SHA1/.SHA256`; `ChecksumFailedException` (boyut + hash doğrulaması); kancalar `--veloapp-install|
obsolete|updated|uninstall` (30/15 s; UI gösterilemez); kurulum `%LocalAppData%\{packId}\current` + `Update.exe` + kök stub
exe, güncellemede **yalnız `current` değişir**; `--framework net{major.minor}-{arch}-{type}` "≥ 5.0 her dotnet sürümü";
`GithubSource(repoUrl, accessToken: null, prerelease: false)` public repo'da token'sız, **60 istek/saat/IP** kimliksiz limit;
`vpk pack/upload github/download github` seçenekleri §5.4.

---

## 4. Sektör standardı: branch, sürüm ve yayın akışı

| Model | Branch'ler | Ne için tasarlandı | Bu projeye uygunluk |
|---|---|---|---|
| **Git Flow** (2010) | `master`, **`develop`**, `release/*`, `hotfix/*`, `feature/*` | Planlı sürümler, aynı anda birden çok sürüm bakımı, büyük ekip | Tören fazla: `develop`'ın tek anlamı "henüz yayınlanmayan birikim"; tek geliştirici + sürekli yayında `main` zaten odur |
| **GitHub Flow** (2011; GitHub docs) | `main` + kısa ömürlü branch'ler; PR; merge; branch sil. **`develop` yok, release branch'i yok** | Sürekli teslimat, küçük ekip | **Uygun.** Bugünkü çalışman zaten bu (iş branch'i → `main`'e `--no-ff` merge → tag) |
| Trunk-based | doğrudan `main`, çok kısa branch | çok sık entegrasyon | GitHub Flow'un daha da sadesi; bugünkü kurallarla aynı sonuç |

**Karar: GitHub Flow, üstüne "release = `main`'de commit + annotated tag".**

- Günlük akış **değişmez**: `feat/x` → tam süit → `main`'e `--no-ff` merge (merge mesajı sürüm notunun kaynağı, `git log
  --first-parent` bunu kullanır) → push → branch sil.
- **Yayın**: `main`'de `release: vX.Y.Z` commit'i (CHANGELOG bölümü + `Version`) + annotated tag `vX.Y.Z` (bugünkü mesaj
  biçimi korunur) → `git push origin main vX.Y.Z` → tag push `release.yml`'i tetikler. Release commit'i için ayrı branch
  **açılmaz** (mekanik, tek commit; release-please gibi araçlar da böyle yapar) — CLAUDE.md'ye istisna olarak yazılır.
- **Hotfix**: `fix/x` → `main` → `/release` patch. `main` her zaman yayınlanabilir (mevcut kural: merge yalnız tam süit yeşilse).
- **PR**: isteğe bağlı; zorunlu kılınmaz (tek geliştirici). CI `main`'e push'ta ve PR'da koşar.
- **Sürüm numarası**: SemVer; patch/minor/major kuralı CLAUDE.md'de zaten var. Ön-sürüm eki (`-beta.1`) kullanılmaz (parser
  reddeder; ihtiyaç olursa §5.11 prerelease bayrağı sürüm numarasına dokunmadan aynı işi görür).
- **Commit mesajı**: bugünkü `feat(x): …` / `fix(x): …` / `docs:` / `chore:` yeterli; conventional-commits araçlarına
  bağımlılık kurulmaz (notu Claude yazar).
- **Koruma (GitHub tarafı, isteğe bağlı ama ucuz)**: `main` için force-push/silme kapalı; `v*` tag'lerini yalnız sahibinin
  oluşturabildiği tag ruleset'i (yanlışlıkla/başkasınca yayın tetiklenmez).

---

## 5. Hedef tasarım

### 5.1 Uçtan uca akış

**Geliştirici (sen):**
1. İş branch'i → kod → tam süit → `main`'e merge + push (bugünkü gibi). CI (`ci.yml`) `main`'de build + test koşar; yayın olmaz.
2. Yayın: **`/release`** dersin (ya da "sürüm çıkar"). Claude: `git log --first-parent v<son>..main` merge mesajları + ilgili
   `.claude/outputs/` raporlarından **İngilizce, kullanıcı dilinde** CHANGELOG bölümünü yazar; numarayı seçer (major ise
   sorar); `Version`'ı çeker; `scripts/release.ps1 -Version X.Y.Z` koşar → script guard'lar + **tam süit** + commit + tag +
   push yapar; Claude sana Actions linkini verir. **Senin işin bitti.**
3. `release.yml` (~10–15 dk): guard → build → test → publish → `vpk pack` (delta dahil) → GitHub Release + asset'ler → gövde =
   CHANGELOG bölümü + "Full changelog" karşılaştırma linki.

**Kullanıcı (client):**
- İlk kurulum: README'deki sabit link `https://github.com/sdemir60/app_build_orchestrator/releases/latest/download/BuildOrchestrator.App-win-Setup.exe`
  → çalıştır (admin yok, per-user). .NET 10 Desktop Runtime yoksa Setup indirir. SmartScreen "More info → Run anyway"
  (imzasız, §5.10). Kurulum bitince uygulama açılır.
- Sonrası otomatik: açılıştan ~5 s sonra ve her 4 saatte sessiz kontrol; yeni sürüm arka planda iner; **inince** hap belirir.
  Restart to update → birkaç saniyede yeni sürüm. Later → hap kalır; uygulama kapanınca sessizce kurulur, sonraki açılış
  yeni sürüm. Yeni sürümde `✦` üzerindeki nokta What's new'e çağırır (mevcut davranış).

### 5.2 Sürüm kimliği ve guard zinciri (tek kaynak `Version`)

| Guard | Nerede | Var mı |
|---|---|---|
| CHANGELOG en üst sürüm == `Version` | `WhatsNewTests.cs:44-51` | **var** |
| InformationalVersion eksiz | `PublishLayoutTests.cs:195-205` | **var** |
| CHANGELOG biçimi/sıra/karakter | `ChangelogTests.cs` | **var** |
| Tag == `v` + `Version` (yayın tetiklenirken) | `scripts/release-guard.ps1` → `release.yml` ilk adım; `release.ps1` de push'tan önce | **yeni** |
| CHANGELOG en üst tarih == yayın günü | `release.ps1` | **yeni** |
| `vpk` (tool manifest) sürümü == `Velopack` NuGet sürümü | yeni guard testi | **yeni** (Velopack CLI ile kitaplığın aynı sürüm olmasını bekler) |
| `--packVersion` == `Version` | `package.ps1` props'tan okur, elle yazılmaz | **yeni** |

### 5.3 Sürüm notları: kim yazar, nereye akar

- **Yazar: Claude**, sürüm anında, CLAUDE.md kuralıyla (İngilizce, düz metin, kullanıcının gördüğü özellik, küçük işler tek
  satır). Otomatik üreticiler elendi: release-please/semantic-release/git-cliff commit mesajından üretir (Türkçe, teknik);
  GitHub "Generate release notes" PR'sız akışta boş kalır. Her işte `[Unreleased]` satırı yazmak da seçilmedi — mevcut kural
  ve parser buna göre.
- **Akış (tek kaynak → üç yüzey):**
  1. **Exe** — What's new (bugünkü gömülü kaynak).
  2. **GitHub Release gövdesi** — `package.ps1` CHANGELOG'dan `## [X.Y.Z]` bölümünü `artifacts/velopack/notes.md`'ye keser;
     `release.yml` `gh release edit vX.Y.Z --notes-file` ile yazar (vpk'nın gövdeyi kendiliğinden doldurup doldurmadığı
     dokümanda net değil — belirsizliğe yaslanılmaz).
  3. **Hap kartı** — aynı `notes.md` `vpk pack --releaseNotes` ile pakete gömülür; client `UpdateInfo.TargetFullRelease.NotesMarkdown`
     alanını **mevcut** `ReleaseNotes.Parse` ile okur (tek bölüm parse edilir — `WhatsNewTests.cs:96-107`) → `UpdateOffer.Highlights`.
- **Öne çıkanlar uzunluğu — tasarım boşluğu:** 1.7.0 bölümü 5 madde ama geçmişte 10+ maddeli sürümler var; kart 344px
  genişlikte, tasarımda sınır yok, prototip tümünü çizer. Öneri (§9 K5): kart en çok **5 madde** gösterir (KindOrder
  sırasıyla), altında soluk tek satır `+N more in What's new after restart` (tasarıma eklenecek küçük öğe). Alternatif:
  sınırsız + notları kısa tutma disiplini.

### 5.4 Paketleme (Velopack) ve kurulum yerleşimi

`scripts/package.ps1` — **publish + not kesme + pack'in TEK sahibi** (lokal deneme ve CI aynı script'i çağırır; README /
ARCHITECTURE / `verify-publish.ps1` publish komutunu yeniden yazmak yerine bu script'e işaret eder → bugünkü üç kopya biter).

| Parametre | Değer | Neden |
|---|---|---|
| `--packId` | **`BuildOrchestrator.App`** | Kurulum `%LocalAppData%\<packId>\`; kaldırma o klasörü siler. State dizini `BuildOrchestrator` → çakışmamalı |
| `--packVersion` | props `Version` | tek kaynak |
| `--packDir` | `artifacts/publish` (script'in `dotnet publish … -r win-x64 --self-contained false` çıktısı) | `supervisor\` ve `Assets\GEIST-LICENSE.txt` publish listesinden gelir |
| `--mainExe` | `BuildOrchestrator.App.exe` | |
| `--packTitle` / `--packAuthors` | props `Product` / `Company` (script okur) | Start Menu + Uygulamalar listesi; kopya yok |
| `--framework` | `net10.0-x64-desktop` | framework-dependent publish → Setup eksik Desktop Runtime'ı kurar. Docs "≥5.0 her dotnet"; örnekler 9.0'a kadar; Velopack 1.0.1 notu ".NET 9 ve 10 ayrıştırma testleri" → **temiz makinede doğrulanacak** |
| `--icon` | `src/BuildOrchestrator.App/Assets/app-icon.ico` | mevcut ICO |
| `--releaseNotes` | `artifacts/velopack/notes.md` | §5.3 |
| `--shortcuts` | `StartMenuRoot` (varsayılan `Desktop,StartMenuRoot`) | geliştirici aracı; masaüstü kısayolu istenmez — §9 K2 |
| `--noPortable` | evet | tek dağıtım biçimi Setup; portable zip'in kendini güncelleme davranışı ayrıca doğrulanmak isterdi — §9 K4 |
| `--delta` | varsayılan `BestSpeed` | CI önce `vpk download github` ile önceki paketi çeker → delta üretilir |
| `--exclude` | varsayılan `.*\.pdb` | pdb pakete girmez |
| `--channel` | varsayılan `win` | ileride `beta` eklenebilir |
| `--outputDir` | `artifacts/velopack` | `.gitignore` `artifacts/` zaten var |
| `--splashImage` | isteğe bağlı: app-mark + "Installing Build Orchestrator" statik görsel | Setup sırasında; tasarımın restart ekranı buraya **taşınamaz** (statik görsel/GIF) — §5.8 |

Çıktılar: `BuildOrchestrator.App-win-Setup.exe`, `…-full.nupkg`, `…-delta.nupkg` (2. yayından itibaren), `releases.win.json`.

**Kurulum yerleşimi:** `%LocalAppData%\BuildOrchestrator.App\` → `Update.exe`, `BuildOrchestrator.App.exe` (stub),
`current\` (uygulama + `supervisor\`), `packages\`. Güncellemede yalnız `current\` değişir; `current\BuildOrchestrator.App.exe`
yolu sabit → autostart değeri geçerli kalır. State `%LocalAppData%\BuildOrchestrator\` **ayrı** → güncelleme ve kaldırma ona
dokunmaz (ayarlar, önbellek, loglar korunur; kaldırmada kullanıcı verisi silinmez — standart davranış).

**Araç sürümleri tek yerde:** `.config/dotnet-tools.json` → `vpk` 1.2.161 (`dotnet tool restore` + `dotnet vpk …`), App
csproj `Velopack` 1.2.161; guard testi ikisini eşitler. `global.json` (SDK 10.0.400, `rollForward: latestFeature`) lokal ile
CI'ı aynı SDK bandına bağlar.

### 5.5 CI/CD

**`.github/workflows/ci.yml`** — `push: [main]` + `pull_request` + `workflow_dispatch`; `runs-on: windows-2025` (pinli —
`windows-latest` Eylül 2025'te WS2025'e geçti, imaj araçları zamanla değişiyor); `actions/checkout@v4`, `actions/setup-dotnet@v4`
(`global-json-file`), `dotnet build -c Release`, `dotnet test --no-build -c Release --filter "Category!=Acceptance&Category!=LocalOnly"`,
TRX artefaktı; `concurrency` ile eski koşu iptali. Yayın yok. README'ye rozet.

**`.github/workflows/release.yml`** — `push: tags: ['v*']`; `permissions: contents: write`; `windows-2025`:

```yaml
- uses: actions/checkout@v4
- uses: actions/setup-dotnet@v4
  with: { global-json-file: global.json }
- run: pwsh scripts/release-guard.ps1 -Tag $env:GITHUB_REF_NAME      # tag == v + props Version; CHANGELOG en üst == Version
- run: dotnet build -c Release
- run: dotnet test --no-build -c Release --filter "Category!=Acceptance&Category!=LocalOnly"
- run: dotnet tool restore
- run: pwsh scripts/package.ps1 -DownloadPrevious                      # gh release list boş değilse vpk download github (delta için)
- run: dotnet vpk upload github --repoUrl https://github.com/${{ github.repository }} --token ${{ secrets.GITHUB_TOKEN }}
       --publish --merge --releaseName "Build Orchestrator ${{ github.ref_name }}" --tag ${{ github.ref_name }}
- run: gh release edit ${{ github.ref_name }} --notes-file artifacts/velopack/notes.md
  env: { GH_TOKEN: ${{ github.token }} }
```

- **Gizli anahtar gerekmez**: `GITHUB_TOKEN` yeter (public repo'da `download` token'sız da çalışır; limit için verilir).
- **Runner imajı (doğrulandı, WS2025 readme):** VS 2022 Enterprise 17.14, MSBuild, vswhere 3.1.7, .NET SDK 10.0.401, .NET
  Framework targeting pack'leri 4.5.2–4.8.1 (**4.6 dahil** → `LegacyFixture` derlenir), `gh` 2.101, PowerShell 7.6.
- **Test stratejisi:** lokal tam süit **kapı olmaya devam eder** (`release.ps1` koşar). CI aynı süiti koşar; runner'da
  koşamayan/kararsız testler (zamanlama bütçeleri, gerekirse ekran gerektiren STA) **yeni `Category=LocalOnly`** ile
  işaretlenir ve yalnız CI filtresinde dışlanır — eşik gevşetilmez, test silinmez (CLAUDE.md). Hangileri olduğu **ilk CI
  koşusunda görülür**; §2.4'teki kayıtlı kırılganlar adaydır.
- Süre tahmini: build 2–3 dk + test 8–12 dk + pack/upload 2–3 dk → yayın ~15 dk.
- İlk yayında önceki paket yok → `-DownloadPrevious` `gh release list` boşsa atlar (hata değil).

### 5.6 Tek komut: `/release`

- **Proje skill'i** `.claude/skills/release/SKILL.md`: CLAUDE.md "Sürüm çıkarma" adımlarını sıralar (kaynak → numara → not →
  `Version` → `scripts/release.ps1 -Version X` → Actions linki → yayın sayfasını doğrula). Slash komutu = "bir komut".
- **`scripts/release.ps1 -Version X.Y.Z`** (lokal, deterministik): `main`'de mi · ağaç temiz mi · `origin/main` ile eşit mi ·
  `Version` == X.Y.Z (değilse yazar) · CHANGELOG en üst `## [X.Y.Z] - <bugün>` mi · tag yok mu → `dotnet build` + **tam süit**
  → `git commit -am "release: vX.Y.Z"` → `git tag -a vX.Y.Z -m "Build Orchestrator X.Y.Z — release notes in CHANGELOG.md"` →
  `git push origin main vX.Y.Z` → Actions URL'sini yazar. Herhangi bir adımda durursa hiçbir şey push edilmemiştir.
- **`scripts/release-guard.ps1 -Tag vX.Y.Z`**: tag ↔ props ↔ CHANGELOG eşitliği (release.ps1 ve release.yml ortak kullanır).
- **`scripts/package.ps1`**: §5.4. `-SkipPublish`/`-NotesOnly` gibi anahtarlarla lokal deneme.
- Yayın tetikleyicisi **lokal** kalır (GitHub UI'dan `workflow_dispatch` ile runner'ın commit+tag atması seçilmedi: diff'i
  gözünle görürsün, notu Claude yazar).

### 5.7 Uygulama içi motor (App katmanı; Core ve Supervisor'a Velopack girmez)

**Giriş noktası** — yeni `src/BuildOrchestrator.App/Program.cs` + csproj `<StartupObject>BuildOrchestrator.App.Program</StartupObject>`:

```csharp
[STAThread]
static void Main(string[] args)
{
    VelopackApp.Build()
        .OnBeforeUninstallFastCallback(_ => AutostartService.RemoveForUninstall())   // HKCU Run değeri temizlenir
        .Run();                    // --veloapp-* kancaları burada tüketilir; kanca modunda process burada biter (WPF hiç kurulmaz)
    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```
`SetAutoApplyOnStartup` varsayılanı (açık) korunur → bekleyen güncelleme açılışta kurulur (tasarım `:378`). `e.Args` akışı ve
`StartupArgs.Decide` değişmez. Tek örnek mutex'i `OnStartup`'ta, `Run()`'dan sonra → kanca process'leri onu görmez.

**Seam** — `Services/Updates/IAppUpdater`: `bool IsInstalled` · `Task<UpdateCandidate?> CheckAsync(ct)` ·
`Task DownloadAsync(candidate, IProgress<int>?, ct)` · `UpdateCandidate? PendingRestart` · `void ApplyOnExit(candidate, restart)`.
`UpdateCandidate(Version, long DownloadBytes, string NotesMarkdown)`. Gerçek uygulama `VelopackUpdater` (`UpdateManager`
sarmalar); testler fake kullanır, Velopack'e dokunmaz.

**Kaynak** — tek sabit `UpdateFeed.Source = "https://github.com/sdemir60/app_build_orchestrator"` →
`new GithubSource(url, accessToken: null, prerelease: false)`. Dev/test kapıları (mevcut `BO_*` deseni): `BO_UPDATE_SOURCE`
(URL **veya klasör** → `UpdateManager(string)` yerel klasörü `SimpleFileSource` olarak çözer → GitHub'sız uçtan uca test),
`BO_UPDATE_PRERELEASE=1` (prerelease'leri de gör → §5.11 aşamalı yayın). UI'da ayar **yok** (tasarımda da yok).

**`UpdateService` durum makinesi** (UI thread dışı, `DispatcherTimer` + `TimeProvider` ile test edilebilir):

| Durum | Geçiş |
|---|---|
| `Off` | `IsInstalled == false` (bin'den çalışan dev build, publish klasörü) → hiç kontrol etmez, hap yok |
| `Idle` | pencere gösterildikten **5 s** sonra ilk kontrol (açılış koreografisi ve engine boot'la yarışmaz), sonra her **4 saat** |
| `Checking` | `CheckAsync`; yok/eski/eşit → `Idle`; hata (çevrimdışı, limit) → sessiz, stderr log, sonraki tur |
| `Downloading` | arka planda, göstergesiz (tasarım); `ChecksumFailedException` → sessiz, sonraki tur; tek işlem aynı anda |
| `Ready(offer)` | `AvailableUpdate = UpdateOffer(Version, Size "18.4 MB", Highlights)` → **hap belirir**; Later ile kalır |
| açılışta `PendingRestart != null` | (indirilmiş ama kurulmamış — ör. process öldürülmüş) → doğrudan `Ready` |

Boyut: indirilecek olan (`DeltasToTarget` toplamı yoksa `TargetFullRelease.Size`), `MB` tek ondalık. Highlights:
`ReleaseNotes.Parse(NotesMarkdown)[0].Notes` → KindOrder → §5.3 sınır kuralı. `UpdateOffer.Sample` placeholder'ı kalkar.

**Restart to update** (`RestartToUpdateRequested` olayına bağlanan koordinatör; kilit VM'de zaten var — `RunViewModel.Update.cs:33-36`):
1. Restart ekranı açılır, adım **`Closing Build Orchestrator…`** (tek adım; §5.8).
2. Mevcut çıkış yolu `RequestExit` → `ExitReady` → `Shutdown()` (kilit sayesinde workspace boşta).
3. `OnExit`'te: motor düzgün kapatılır **ve Supervisor process'inin çıkışı beklenir (≤2 s)** — bugün beklenmiyor (§2.3);
   `current\supervisor\*.dll` kilitliyken Update.exe dosya değiştirmesin diye. Sonra
   `WaitExitThenApplyUpdates(asset, silent: true, restart: true)` → process biter → Update.exe (job dışında, yaşıyor)
   `current\`'ı değiştirir → yeni sürüm normal açılır. `ApplyUpdatesAndRestart` **kullanılmaz** (anında çıkar, `OnExit`'i atlar).
4. Yeni sürümde `SeenVersion ≠ Version` → What's new noktası kendiliğinden (`MainWindow.xaml.cs:496-497`).

**Later** → `Ready` kalır. Normal çıkışta (tepsi Exit, ×, oturum kapanışı) `Ready` varsa `OnExit`
`WaitExitThenApplyUpdates(asset, silent: true, restart: false)` → uygulama kapanırken sessizce kurulur; bir sonraki açılış yeni
sürümdür (tasarım metniyle bire bir). Velopack'in açılışta auto-apply'ı yedek kalır (process öldürülmüşse).

**Tepside**: kontrol ve indirme pencere gizliyken de sürer; balloon yok (tasarım kuralı); hap pencere gösterilince görünür.
**Çevrimdışı**: sessiz. **Downgrade**: yok sayılır (Velopack de ileri gider). **Uninstall**: Velopack kurulum klasörü +
kısayol + kendi kayıtlarını siler; kancamız HKCU Run değerini siler; state dizini kalır (README'de yazılır).

### 5.8 Tasarımla (v1.23.0) uyum

| Tasarım | Motor | Durum |
|---|---|---|
| Hap yalnız paket hazırken (`:362,:667`) | `Ready` = indirilmiş | **birebir** |
| İndirme sırasında iz yok (`:362`) | `Downloading` göstergesiz | **birebir** |
| Öne çıkanlar "besleme kaydından" (`:374`) | `NotesMarkdown` → `ReleaseNotes.Parse` | **birebir** |
| "If you wait, it installs on the next start" (`:376,:378`) | çıkışta sessiz kurulum + açılışta auto-apply | **birebir** |
| Kilit sebepleri (`:377`) | VM'de var; motor yalnız izin verilen anda çağrılır | **birebir** (F5/Esc metni UI işi) |
| Restart sonrası hap düşer, What's new noktası döner (`:380`) | yeni process; `SeenVersion` | **birebir** |
| Kaynak "iç feed / paylaşım, yapılandırmadan" (`:382`) | GitHub Releases sabit + `BO_UPDATE_SOURCE` (klasör/paylaşım da olur) | **uyarlama** — tasarıma "kaynak GitHub Releases" notu |
| Restart ekranı 3 adım `Closing → Installing → Starting` tek pencerede; "gerçekte güncelleyicinin splash'i taşır" (`:380`) | Eski process yalnız **Closing** gösterebilir; dosyalar değişirken pencere yoktur; Velopack'in apply UI'ı DS'e uyarlanamaz (splash statik görsel, Setup için) → `silent: true`; "Starting" = yeni sürümün normal açılışı | **tasarım kararı gerek** (§9 K6): tek adım + kısa karanlık boşluk (1–3 s) + normal açılış |
| Öne çıkanlarda madde sınırı yok | 5 + "+N more" satırı önerisi | **tasarım kararı gerek** (§9 K5) |
| Ana projedeki plan U4: ekran 3 adımı oynatıp uygulamaya döner (motor yok) | Motor gelince bu davranış değişir → o testi yeni kuralı pinleyecek şekilde yeniden yazma (CLAUDE.md) | fazda yapılır |

### 5.9 Güvenlik sınırı (ARCHITECTURE §21 güncellenecek)

- **Ağ yüzeyi artar:** bugün "tek ağ dokunuşu `git fetch`" (§21:5295) → artık ikinci: `api.github.com` (release listesi) +
  `github.com/…/releases/download/…` (asset) üzerinden **HTTPS**, kimliksiz, 4 saatte bir; ofis NAT'ı arkasında onlarca client
  bile 60/saat/IP limitinin çok altında.
- **Bütünlük:** Velopack indirilen paketi boyut + SHA1/SHA256 ile doğrular (feed `releases.win.json` release'in kendi
  asset'i). Güven zinciri = TLS + GitHub hesabı. Kod imzası v1'de yok → yerel diskteki saldırgan zaten tehdit modeli dışı
  (§21.5); ağda MITM TLS'e bırakılır.
- **Hesap = dağıtım kanalı:** hesabı ele geçiren herkesin paketini tüm client'lar indirir → GitHub **2FA** şart (doğrula);
  tag ruleset (`v*` yalnız sahibi); Actions yalnız `actions/*` resmi action'ları, sürüm pinli; `permissions` en dar; hiçbir
  secret yok.
- **Kanca process'leri**: `--veloapp-*` ile çalışan kısa ömürlü process UI göstermez, 15–30 s içinde çıkar; bizim kancamız
  yalnız HKCU Run değerini siler.
- **Update.exe job dışında** — App'in kullanıcı adına başlattığı diğer process'ler gibi (§4.2 istisna listesine eklenir).

### 5.10 Kod imzalama

- **v1 imzasız.** SmartScreen yalnız tarayıcıdan inen `Setup.exe`'de (Mark-of-the-Web); uygulamanın kendi başlattığı
  `Update.exe`/güncelleme için uyarı yok. README anlatır.
- **Sonra: SignPath Foundation** (OSS'e ücretsiz OV imza). Doğrulanan koşullar: OSI lisansı (**LICENSE şart**), aktif bakım,
  **halihazırda yayınlanmış** proje, repo/SignPath'te MFA, artefaktlar doğrulanabilir biçimde CI'da üretilmiş, rol tanımları,
  ana sayfada "Code signing policy" bölümü + atıf, her yayında elle onay. Yani sıra: LICENSE → ilk yayın → başvuru.
  Velopack `--signTemplate` ile CI adımı olarak eklenir.
- **Azure Artifact Signing** (eski Trusted Signing): bireysel geliştirici **yalnız ABD/Kanada**; kurumsal listede Türkiye yok
  (doğrulandı) → uygulanamaz. Klasik OV/EV ücretli → hedefe aykırı.

### 5.11 Kullanıcı deneyimi: kurulum, geçiş, kaldırma

- **Gereksinim değişir:** kullanıcıya .NET 10 **SDK** değil Desktop **Runtime** yeter (Setup kurar); MSBuild için VS/Build Tools
  ve `git` aynen (README Requirements ikiye ayrılır: kullanmak / geliştirmek).
- **Bugünkü kopyalardan geçiş:** bugün kurulum yok, publish klasörü elden dağıtılıyor (README'de Install bölümü yok). Setup.exe
  yeni yere kurar; **ayarlar korunur** (state dizini aynı); autostart açıksa ilk açılışta yeni yola yeniden yazılır. Kullanıcı
  eski kopyayı **önce kapatır** (açıkken Setup'ın başlattığı yeni exe tek-örnek kapısına takılıp eskisini öne getirir), sonra
  eski klasörü siler. README'de üç satır.
- **Aşamalı yayın (isteğe bağlı, sıfır altyapı):** `vpk upload github --pre` ile önce **prerelease** yayınla; yalnız
  `BO_UPDATE_PRERELEASE=1` olan senin makinen görür; denedin → GitHub'da "pre-release" işaretini kaldır → herkes alır.
  Varsayılan: doğrudan yayın (senin sözün: "ayrı geliştirdiğim yerde test ediyorum").
- **Kaldırma:** Uygulamalar listesinden; kurulum klasörü + kısayollar + Run değeri gider; `%LocalAppData%\BuildOrchestrator\`
  kalır (README'de "verileri silmek için…" satırı).

### 5.12 Hata ve geri alma senaryoları

| Senaryo | Davranış |
|---|---|
| Kontrol/indirme başarısız (çevrimdışı, limit, hash) | sessiz; log stderr'e; 4 saat sonra yeniden |
| Update.exe dosya değiştiremiyor (kilit) | Supervisor çıkışı beklendiği için beklenmez; olursa Velopack `current\`'ı bozmadan bırakır, uygulama eski sürümle açılır, güncelleme bekleyen kalır → **ilk gerçek güncellemede doğrulanacak** |
| Kötü sürüm çıktı | Otomatik rollback yok (Velopack ileri gider). Standart: **fix-forward** — `/release` patch; gerekirse GitHub'da release'i sil/gizle (henüz almayanlar durur) |
| Yayın CI'da düştü | Tag durur, release oluşmaz; düzelt → tag'i sil/yeniden at (`release.ps1 -Retag`) |
| Kullanıcı Later dedi, günlerce tepside | hap kalır; kapanışta kurulur; açılış otomatik kurulumu yedek |
| Kullanıcı Restart'a bastı, build sürüyor | buton kilitli (VM); bitince kendiliğinden açılır |

---

## 6. Riskler ve ilk koşuda doğrulanacaklar

1. **`--framework net10.0-x64-desktop` bootstrap** — Velopack bilinen runtime listesi/indirme yolu .NET 10'u tanıyor mu?
   → İlk Setup **Desktop Runtime olmayan temiz makinede/VM'de** denenir (benim yapamayacağım adım).
2. **Süit CI'da** — zamanlama bütçeleri ve ekran dışı pencere testleri paylaşımlı runner'da; `LegacyFixture` 4.6 targeting
   pack (imajda var). → İlk `ci.yml` koşusu triage: `LocalOnly` kategorisi; eşik gevşetme yok.
3. **Apply anında dosya kilidi** — Supervisor/MSBuild kapanışı ile Update.exe yarışı → §5.7 bekleme + ilk gerçek güncellemede gözlem.
4. **Auto-apply-on-startup'ta argüman aktarımı** — `--autostart` ile açılan uygulamada bekleyen güncelleme kurulup yeniden
   başlarken argüman korunuyor mu (tepside kalmalı)? → doğrulanacak; asıl yol "çıkışta kur" olduğu için etkisi sınırlı.
5. **`vpk download github` ilk yayında** release yokken hata verir → script `gh release list` ile atlar.
6. **Runner imajı sürüklenmesi** (`windows-latest` → 2026'da VS 2026 imajı tartışılıyor) → `windows-2025` pinli; MSBuild
   vswhere ile çözüldüğü için VS sürümünden bağımsız.
7. **Velopack + WPF `Main`** — `Program.cs` ile `App.g.cs`'in ürettiği Main çakışmaz (`StartupObject` seçer); kaynak guard
   testiyle pinlenir.
8. **Feed notu uzunluğu / kart yüksekliği** — §5.3 kuralı kararlaştırılmalı.
9. **Kart boyutu ve pill metni** ilk gerçek pakette ölçülür (tahmin ~10 MB, delta çok daha küçük).
10. **Kaynak dizininde bayat `win-x64\`** — `App.csproj:79` glob'u Supervisor bin'indeki eski RID klasörünü de süpürür
    (lokal Release bin'de görüldü); CI temiz checkout'ta olmaz, lokal `package.ps1` temiz `-o` klasörüne publish eder. Not.

---

## 7. Uygulama planı

Her aşama kendi branch'inde, **kırmızı test önce**, doküman aynı işte, tam süit yeşil, `main`'e merge + push.

| # | Aşama / branch | İçerik | Testler (yeni / yeniden yazılan) | Boyut |
|---|---|---|---|---|
| P0 | `chore/repo-hygiene` | `LICENSE` (MIT), `global.json`, `.github/workflows/ci.yml`, README rozet + Requirements ayrımı | — (CI yeşil görülür; `LocalOnly` triage'ı burada başlar) | orta |
| P1 | `feat/release-pipeline` | `Program.cs` + `StartupObject` + `Velopack` paketi (yalnız `Run()` + uninstall kancası), `.config/dotnet-tools.json`, `scripts/package.ps1` (publish tek sahip; `verify-publish.ps1` ona bağlanır), `scripts/release.ps1`, `scripts/release-guard.ps1`, `release.yml`, `/release` skill'i, CLAUDE.md "Sürüm çıkarma" güncellemesi, ARCHITECTURE/README | `EntryPointTests` (StartupObject var; Main'in ilk ifadesi VelopackApp…Run(); ondan önce WPF yok — kaynak guard), `StartupArgs` `--veloapp-install 1.8.0` → ShowWindow, tool-manifest == PackageReference sürümü, `package.ps1` not kesme testi (`pwsh` ile 1.7.0 bölümü == parser çıktısı), `release-guard.ps1` eşitlik testleri | uzun |
| P2 | `feat/in-app-update` | `IAppUpdater` + `VelopackUpdater`, `UpdateService`, `UpdateOffer` eşlemesi (Sample kalkar), restart koordinatörü (Closing → düzgün çıkış → Supervisor çıkışı beklenir → `WaitExitThenApplyUpdates`), çıkışta sessiz kurulum, `BO_UPDATE_SOURCE`/`BO_UPDATE_PRERELEASE`, `AutostartService.RemoveForUninstall`, ARCHITECTURE yeni bölüm "Distribution and updates" + §4.2/§12.1/§12.3/§13.3/§16/§17/§18/§21/§22 | `UpdateServiceTests` (fake updater + `FakeTimeProvider`: Off/5 s/4 saat/yeni→indir→Ready/eşit-eski→yok/hata→sessiz tekrar/pending→Ready/tek işlem), `UpdateOfferMappingTests` (boyut biçimi, Highlights parse + KindOrder + sınır), `UpdateRestartCoordinatorTests` (kilitliyken no-op; Restart → Closing → ApplyOnExit(restart:true); Later + çıkış → ApplyOnExit(restart:false); Ready yokken hiçbir şey; Supervisor bekleme sınırı), uninstall kancası → Remove, hap `AvailableUpdate == null` iken gizli (eski "hep görünür" testi yeni kuralla yeniden yazılır + gerekçe), restart ekranı yeni kural (tek adım) | uzun |
| P3 | yayın | `/release` → **v1.8.0** (installer + motor; içinde design v1.23/v1.24 işi de olur). Öncesinde **yerel feed** ile uçtan uca: `package.ps1` 1.8.0 → kur → 1.8.1 paketle → `BO_UPDATE_SOURCE=<klasör>` → hap → Restart → 1.8.1 + What's new noktası + autostart değeri | elle kabul prosedürü ARCHITECTURE §17'ye | orta |
| P4 | sonra / isteğe bağlı | SignPath başvurusu + `--signTemplate` adımı; `beta` kanalı; winget manifesti; Dependabot | — | — |

**Değişmezler korunur:** OutDir'e dokunulmaz; Git'e araç kendiliğinden yazmaz (yayın komutu kullanıcının açık kararıdır ve
`release.ps1` bunu yapar — uygulama değil); planlama Core'da (Velopack yalnız App'te); stdout NDJSON; kopya yasak (publish
komutu tek sahibe iner).

---

## 8. İş bölümü

### Ben yapacağım
- P0–P2'nin tamamı: LICENSE dosyası (metnini onaylarsın), `global.json`, CI/release workflow'ları, script'ler, `/release`
  skill'i, `Program.cs`/Velopack entegrasyonu, motor + testler, README/ARCHITECTURE/CLAUDE.md güncellemeleri.
- İlk CI koşusunun triage'ı (`LocalOnly` kategorisi), yerel feed ile uçtan uca güncelleme provası, ilk yayının CI takibi ve
  sonuç raporu.
- SignPath için README "Code signing policy" bölümü taslağı (istersen, P4).

### Sen yapacaksın
1. §9'daki kararları söyle (çoğu "önerin olsun" ile geçer).
2. GitHub hesabında **2FA** açık mı doğrula. İsteğe bağlı: Settings → Rules → `main` için force-push/silme kapalı; `v*` tag
   ruleset'i (yalnız sen). Actions için secret **gerekmez**; workflow `permissions` yazma iznini kendi verir (repo varsayılanı
   engellerse Settings → Actions → General → Workflow permissions: Read and write — yalnız hata görülürse).
3. LICENSE için lisans (MIT önerisi) ve telif satırındaki ad ("Delta Yazılım" / "Sinan Demir").
4. İlk `Setup.exe`'yi **.NET 10 Desktop Runtime olmayan temiz bir makinede/VM'de** dene (risk §6.1).
5. Tasarım tarafına iki madde: restart ekranı gerçeklik uyarlaması (K6) ve öne çıkanlar sınırı (K5); kaynak GitHub notu.
6. Kullanıcılara duyuru: eski kopyayı kapat → Setup.exe → SmartScreen "More info → Run anyway" → eski klasörü sil (README'yi
   ben yazarım, sen iletirsin).
7. Sonra: SignPath başvurusu (LICENSE + ilk yayın + MFA sonrası).
8. Hazır olduğunda **`/release`** de; ilk sürümde Actions'ı birlikte izleriz.

---

## 9. Kararlar (önerim önde; itiraz yoksa böyle uygulanır)

| # | Karar | Öneri | Alternatif |
|---|---|---|---|
| K1 | Paket kimliği (kurulum klasörü adı) | **`BuildOrchestrator.App`** (kullanıcı onayladı 2026-09-30) | `BuildOrchestrator` OLMAZ: state dizini `%LocalAppData%\BuildOrchestrator\` ile aynı klasör olur, kaldırma ayarları/önbelleği siler |
| K2 | Kısayol | **Start Menu + Masaüstü** (kullanıcı kararı 2026-09-30; Velopack varsayılanı `Desktop,StartMenuRoot`) | — |
| K3 | Lisans | **MIT** (kullanıcı onayladı 2026-09-30) | — |
| K4 | Portable zip | **üretilmez** (`--noPortable`) | üretilir, "güncellenmez/kendini günceller" ayrıca doğrulanır |
| K5 | Kart öne çıkanları | **en çok 5 madde + "+N more in What's new after restart"** — satır düz metin, tıklanmaz; kalan maddeler yeni sürüm açılınca What's new'de (kullanıcı onayladı 2026-09-30; tasarım eki) | — |
| K6 | Restart ekranı | **tek adım "Closing…" eski process'te; Update.exe sessiz; yeni sürüm normal açılır** (kullanıcı onayladı 2026-09-30; tasarım güncellenir) | — |
| K7 | İlk yayın | **v1.8.0 = installer + motor birlikte**; yerel feed provası önce (kullanıcı onayladı 2026-09-30) | — |
| K8 | Kontrol sıklığı | **açılış + 5 s, sonra 4 saat** | yalnız açılış / 1 saat |
| K9 | Aşamalı yayın | **kapalı** (doğrudan yayın); istenirse `--pre` + `BO_UPDATE_PRERELEASE` bir bayrak | her yayın önce prerelease |
| K10 | Release commit'i | **doğrudan `main`'de, script atar** (CLAUDE.md istisnası) | `release/vX` branch'i + merge |
| K11 | CI test kapsamı | **tam süit, runner'da koşamayanlar `LocalOnly`** | CI yalnız build |
| K12 | `global.json` | **eklenir** (10.0.400, latestFeature) | eklenmez |

---

## 10. Kaynaklar (doğrulanan noktalar)

- Velopack — C# başlangıç (Main'de `Run()`, WPF `StartupObject`): https://docs.velopack.io/getting-started/csharp
- Velopack — `vpk pack` / `upload github` / `download github` seçenekleri: https://docs.velopack.io/reference/cli/content/vpk-windows
- Velopack — runtime adlandırma, "≥5.0 her dotnet", self-contained'de `--framework` yok: https://docs.velopack.io/packaging/bootstrapping
- Velopack — kurulum yerleşimi, yalnız `current` değişir, `WaitExitThenApplyUpdates` 60 s, `SetAutoApplyOnStartup` varsayılan açık, `ChecksumFailedException`: https://docs.velopack.io/integrating/overview
- Velopack — kancalar ve süre sınırları: https://docs.velopack.io/integrating/hooks
- Velopack — `GithubSource`, `SimpleFileSource`, 60 istek/saat/IP: https://docs.velopack.io/integrating/update-sources
- Velopack — `UpdateManager` API: https://docs.velopack.io/reference/cs/Velopack/UpdateManager · `VelopackAsset` (`NotesMarkdown`, `Size`, `SHA256`): https://docs.velopack.io/reference/cs/Velopack/VelopackAsset · `VelopackApp`: https://docs.velopack.io/reference/cs/Velopack/VelopackApp
- Velopack — Windows yerleşimi (stub exe, per-user/per-machine): https://docs.velopack.io/packaging/operating-systems/windows · GitHub Actions örneği: https://docs.velopack.io/distributing/github-actions
- Velopack sürüm/bakım: NuGet `Velopack` ve `vpk` 1.2.161; `api.github.com/repos/velopack/velopack/releases/latest` → 1.2.161, `published_at` 2026-09-29; MIT; 2.354★
- GitHub Flow tanımı (develop/release branch'i yok): https://docs.github.com/en/get-started/using-github/github-flow
- `windows-latest` → Windows Server 2025 geçişi (Eylül 2025): https://github.blog/changelog/2025-07-31-github-actions-new-apis-and-windows-latest-migration-notice/ · WS2025 imaj içeriği (VS 2022 17.14, .NET SDK 10.0.401, .NET Framework targeting pack 4.5.2–4.8.1, gh, vswhere): https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md
- SignPath Foundation OSS koşulları: https://signpath.org/terms
- Azure Artifact Signing — bireysel: yalnız ABD/Kanada; kurumsal ülke listesi: https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- Repo: `api.github.com/repos/sdemir60/app_build_orchestrator` → `private: false`, `license: null`, release yok, tag'ler v1.1.0–v1.7.0
- Kod referansları: ana proje `feat/design-v1.23-v1.24` (2026-09-29 akşamı) ve `main` @ 102d1ca; ARCHITECTURE.md §1.3, §4.1–4.4, §12.1, §12.3, §13.3, §16, §17, §18, §21, §22
