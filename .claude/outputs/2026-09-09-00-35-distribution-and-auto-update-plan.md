# Dağıtım, sürümleme ve otomatik güncelleme akışı — plan

Tarih: 2026-09-09 · Durum: **öneri, uygulanmadı** (onay bekliyor) · Kapsam: Build Orchestrator'ın GitHub üzerinden
ücretsiz dağıtımı, sürüm notları ve VS Code benzeri uygulama içi güncelleme.

---

## 1. Kararın özeti

| Konu | Öneri |
|---|---|
| Paketleme + güncelleme motoru | **Velopack** (MIT, ücretsiz, .NET 10 destekli, GitHub Releases'ı doğrudan güncelleme kaynağı olarak kullanır) |
| Yayın yeri | **GitHub Releases** (public repo → sınırsız, ücretsiz; asset başına 2 GiB) |
| Derleme/yayın otomasyonu | **GitHub Actions** (public repo → ücretsiz `windows-latest` runner) |
| Sürüm numarasının tek kaynağı | `Directory.Build.props` → `<Version>` (bugün de öyle); tag = `v` + Version; CI tag ile props'un eşitliğini denetler |
| Sürüm notlarının tek kaynağı | Repo kökünde **`CHANGELOG.md`** (Keep a Changelog); App bunu gömülü kaynak olarak okur (What's new sekmesi), CI aynı dosyadan release gövdesini üretir. `ReleaseNotes.cs`'teki elle yazılmış liste kalkar |
| Yayın tetikleyicisi | Tek komut: `scripts/release.ps1 -Version 1.1.0` → CHANGELOG'daki `[Unreleased]` bölümünü sürüme çevirir, props'u günceller, `release: v1.1.0` commit'i + annotated tag atar, push eder. Tag push'u release workflow'unu başlatır |
| Kullanıcı deneyimi | İlk kurulum `Setup.exe` (admin gerekmez, `.NET 10 Desktop Runtime` yoksa Setup indirir). Sonrası otomatik: açılışta + 4 saatte bir sessiz kontrol, arka planda indirme, title bar'da **Restart to update** butonu, tıklayınca yeniden başlar; yeni sürümde mevcut amber nokta What's new'e yönlendirir |
| İmza | v1'de imzasız (SmartScreen uyarısı README'de anlatılır). Sonra **SignPath Foundation** ücretsiz OSS imzası (önkoşul: OSI lisansı + yayınlanmış proje) |
| Lisans | Repo'ya `LICENSE` (MIT önerisi) — bugün yok; "open source" iddiasının ve SignPath'in önkoşulu |

---

## 2. Bugünkü durum (kodla doğrulandı)

- **Sürüm:** [Directory.Build.props](../../Directory.Build.props) `<Version>1.0.0</Version>`,
  `<InformationalVersion>$(Version)+it5</InformationalVersion>`. `AppIdentity.Version` InformationalVersion'ı okur
  ([AppIdentity.cs](../../src/BuildOrchestrator.App/Services/AppIdentity.cs)); Supervisor motor sürümünü kendi
  assembly'sinden `engineReady` ile bildirir. Yani UI'daki sürüm bugün **`1.0.0+it5`**'tir.
- **Sürüm notları:** [ReleaseNotes.cs](../../src/BuildOrchestrator.App/Services/ReleaseNotes.cs) — statik C# listesi, tek
  girdi (`1.0.0`, 2026-08-27), kategoriler Added/Changed/Fixed/Performance/Removed. About → *What is new* sekmesi buradan
  beslenir; görülmemiş sürüm için `i` butonunda 5px amber nokta
  ([MainWindow.xaml](../../src/BuildOrchestrator.App/MainWindow.xaml) `UnseenNotesDot`, `ui-state.json` → `SeenVersion`).
  `WhatsNewTests` `All[0].Version == AppIdentity.Version` iddiasını pinler.
- **Publish:** framework-dependent, klasör tabanlı, `win-x64`; `supervisor\` alt klasörü zorunlu; `PublishSingleFile`
  MSBuild target'ıyla reddedilir; self-contained doğrulanmamış (README §Publish, ARCHITECTURE §18).
  `scripts/verify-publish.ps1` publish çıktısını uçtan uca doğrular (UI Automation ile canlı pencere okur → CI'da değil,
  lokalde koşar).
- **Repo:** `sdemir60/app_build_orchestrator` **public**, `LICENSE` yok, hiç tag yok, `.github/` yok, hiç release yok.
  Lokal SDK 10.0.400.
- **State dizini:** `%LOCALAPPDATA%\BuildOrchestrator\` (logs, build-state, ui-state, **20 GiB'a kadar worktree havuzu**) —
  ARCHITECTURE §16. Bu ad paketleme kimliğiyle **çakışmamalı** (bkz. §5.3).
- **Process topolojisi:** App **outer job'ın üyesi değil** (§4.2) → App'in başlattığı `Update.exe`, App kapanınca job
  cascade'ine yakalanmaz; Supervisor ise App'in kapanışında ölür. Güncelleme için tam istenen düzen.
- **Giriş noktası:** WPF'in ürettiği `Main` (App.g.cs); `StartupArgs.Decide` tanınmayan argümanı yutar
  ([StartupArgs.cs](../../src/BuildOrchestrator.App/Shell/StartupArgs.cs)).
- **Autostart:** registry'ye `Environment.ProcessPath` yazılır ([App.xaml.cs](../../src/BuildOrchestrator.App/App.xaml.cs)
  `AutostartCommand`). Velopack'te bu yol güncellemeler boyunca sabittir (§5.3).
- **Üçüncü taraf listesi:** `ThirdPartyNotices.All` ↔ csproj `PackageReference` eşitliğini `ThirdPartyNoticesTests` denetler →
  yeni paket eklenince atıf zorunlu.

---

## 3. Neden Velopack — alternatiflerle karşılaştırma

| Ölçüt | **Velopack** | ClickOnce | MSIX + App Installer | El yapımı (GitHub API + zip) |
|---|---|---|---|---|
| Ücretsiz / açık kaynak | MIT | SDK'nın parçası | SDK'nın parçası | — |
| GitHub Releases'tan güncelleme | **Yerleşik** (`GithubSource`, public repo'da token gerekmez) | GitHub Pages'ta manifest barındırmak gerekir | `.appinstaller` HTTPS'te barındırılır | kendin yazarsın |
| Uygulama içi "restart to update" butonu | **Evet** (`UpdateManager` API) | Kendi diyaloğunu açar, kontrol sınırlı | OS yönetir, uygulama içi kontrol yok | evet, ama atomik değiştirme/rollback'i sen yazarsın |
| Delta güncelleme | **Evet** | Hayır | Evet | Hayır |
| .NET Desktop Runtime yoksa kurma | **Evet** (`--framework net10.0-x64-desktop`) | Prerequisites (sınırlı) | Hayır (self-contained gerekir) | Hayır |
| İmza zorunluluğu | **Yok** (isteğe bağlı) | Test sertifikasıyla olur | **Zorunlu** güvenilir sertifika (ücretli) | yok |
| `supervisor\` alt klasörlü klasör yerleşimi | Sorunsuz (publish klasörü olduğu gibi paketlenir) | Olur | MSIX konteyneri `%LOCALAPPDATA%` yazımlarını sanallaştırır → state dizini/registry davranışı değişir | olur |
| Admin gerekmez, per-user kurulum | Evet | Evet | Evet | evet |
| Bakım durumu (2026) | Aktif, CLI 1.2.0 | Eski | Aktif ama sertifika şart | — |

MSIX zaten ARCHITECTURE §1.3'te v1 non-goal. ClickOnce güncelleme deneyimini kendi diyaloğuna hapseder. El yapımı çözüm,
Velopack'in bedavaya verdiği atomik değiştirme, geri alma, kısayol/uninstaller kaydı ve runtime bootstrap'ını yeniden yazmak
demek. **Velopack seçilir.**

---

## 4. Hedef akış — üç bakış açısı

### 4.1 Geliştirici (sen) — günlük

1. Çalışma branch'i aç, özelliği yaz.
2. Kullanıcının **göreceği** her değişiklik için `CHANGELOG.md` → `## [Unreleased]` altına **bir satır** (İngilizce,
   kullanıcı dili, ≤ 1 cümle). İç refactor, test, doküman değişikliği **yazılmaz**.
3. Commit (bugünkü gibi), `main`'e merge + push.
4. `ci.yml` her push'ta build + filtreli test koşar (rozet yeşil/kırmızı). Yayın **olmaz**.

### 4.2 Yayın — tek komut

```powershell
pwsh scripts/release.ps1 -Version 1.1.0      # ya da -Bump patch|minor|major
```

Script sırasıyla: `main`'de ve temiz mi · `origin/main` ile eşit mi · `[Unreleased]` boş değil mi · `CHANGELOG.md`'de
`## [Unreleased]` → `## [1.1.0] - 2026-09-09` (yeni boş `[Unreleased]` en üste) · `Directory.Build.props` `<Version>` →
`1.1.0` · `dotnet test` (filtreli) · `release: v1.1.0` commit'i · annotated tag `v1.1.0` (mesajı: o sürümün notları) ·
`git push` + `git push --tags`.

Tag push'u `release.yml`'i tetikler: guard (tag == props) → test → publish → Velopack pack (delta dahil) → GitHub Release
oluştur + asset'leri yükle → release gövdesine CHANGELOG'daki o sürümün bölümü + "Full Changelog" karşılaştırma linki.
Tahmini süre 10–15 dk; senin başka bir şey yapman gerekmez. Yayın sayfası:
`https://github.com/sdemir60/app_build_orchestrator/releases`.

"Derleyip elimle paket alayım" ihtiyacı için aynı adımlar `scripts/package.ps1` ile lokalde koşar (upload hariç);
`release.yml` de bu script'i çağırır → **publish + pack komut satırı tek yerde** (kopya yasağı).

### 4.3 Kullanıcı (client) — kurulum ve güncelleme

- **İlk kurulum:** README'deki sabit link
  `https://github.com/sdemir60/app_build_orchestrator/releases/latest/download/BuildOrchestrator.App-win-Setup.exe` →
  çalıştır. Admin istemez; `%LOCALAPPDATA%\BuildOrchestrator.App\current\` altına kurar, Start Menu kısayolu ve *Uygulamalar*
  listesine kaldırıcı kaydı yapar. `.NET 10 Desktop Runtime` yoksa Setup indirir. Portable `.zip` de yayında olur.
- **SmartScreen:** imzasız `Setup.exe` ilk açılışta "Windows protected your PC" der → *More info → Run anyway*. README'de
  ekran metniyle anlatılır (§5.7'de imza yolu).
- **Güncelleme (VS Code deseni):** uygulama açılıştan ~5 sn sonra ve her 4 saatte bir GitHub Releases'a sessizce bakar.
  Yeni sürüm varsa arka planda indirir (hash doğrulamalı). İndirme bitince title bar'ın sağ grubunda **Restart to update**
  butonu belirir (amber nokta). Tıklayınca uygulama düzgün kapanır (Supervisor cascade ile ölür), `Update.exe` `current\`
  klasörünü değiştirir ve uygulamayı yeniden açar. Yeni sürümde `i` üzerindeki mevcut amber nokta What's new'e götürür —
  açılış pop-up'ı yok (mevcut tasarım kararı korunur).
- **Build sürerken:** buton görünür ama **devre dışı** (tooltip: "Restart to update — after the current run"). Yarım build'i
  kesmek yok.
- Çevrimdışı / GitHub erişilemez: sessiz; About → Environment sekmesinde tek satır "Updates: last check 12:30 — up to date /
  could not reach GitHub". Kurulu olmayan kopya (bin'den çalıştırılan dev build, portable) hiç kontrol etmez, satır
  "Updates: not an installed copy" der.

---

## 5. Tasarım

### 5.1 Sürüm kimliği — tek kaynak ve guard'lar

- **Kaynak:** `Directory.Build.props` `<Version>` (bugünkü [D1] kararı korunur). Tag ve CHANGELOG başlığı bu değerden
  **türetilir** (release script yazar), bağımsız tanımlanmaz.
- **Guard'lar:** (a) `release.yml`: `GITHUB_REF_NAME` (`v1.1.0`) == `v` + props Version, değilse yayın durur;
  (b) yeni test `ChangelogTests`: CHANGELOG'daki en üst yayınlanmış bölüm == props Version, kategoriler yalnız beş bilinen
  başlık, `[Unreleased]` en üstte — repo dosyalarını `RepoPaths` ile okuyan mevcut guard desenine (`PublishLayoutTests`)
  uyar ve CI'da `dotnet test` ile koşar.
- **`+it5` kalkar:** `InformationalVersion` = `$(Version)`; UI `1.1.0` gösterir. Bu bilinçli bir davranış değişikliği: `+it5`'i
  pinleyen test(ler) yeni kuralı pinleyecek şekilde yeniden yazılır ve doc'una eski iddia ("kablonun kurulu olduğunun
  kanıtı") + değişme gerekçesi ("SDK varsayılanı 1.0.0'dan farklı her Version zaten kanıttır; kullanıcıya görünen sürüm
  temiz SemVer olmalı") yazılır. Velopack `--packVersion` da aynı değeri alır; SemVer olmayan bir ek Velopack tarafında
  kanal/karşılaştırma sorunlarına açıktır.
- **`SeenVersion`** karşılaştırması aynen kalır (artık `1.1.0` gibi temiz değerlerle).

### 5.2 `CHANGELOG.md` — sürüm notlarının tek kaynağı

Repo kökünde, Keep a Changelog biçimi; kategoriler uygulamanın **mevcut** beş türü (`NoteKind`) ile birebir:

```markdown
# Changelog

## [Unreleased]
### Added
- Automatic updates: the app checks GitHub Releases and offers *Restart to update* in the title bar.

## [1.0.0] - 2026-08-27
### Added
- What's new — release notes now live in this window.
...
```

- App csproj'da `<EmbeddedResource Include="..\..\CHANGELOG.md" LogicalName="CHANGELOG.md" />`. `ReleaseNotes.All` artık
  gömülü kaynağı **parse eder** (küçük, saf parser; `[Unreleased]` atlanır — yayınlanmamış madde UI'da görünmez).
  `KindOrder`, `SwatchBrushKey`, `Label`, `OpenByDefault`, `Current` aynen kalır; sadece veri kaynağı değişir.
- Bugünkü `1.0.0` maddeleri C#'tan CHANGELOG'a **taşınır** (kopya değil, taşıma).
- CI, release gövdesi için aynı dosyadan `## [x.y.z]` bölümünü keser (5 satırlık PowerShell; biçim doğrulaması zaten
  `ChangelogTests`'te). Velopack `--releaseNotes` ile aynı metni pakete de gömer; GitHub Release gövdesi ayrıca
  `gh release edit --notes-file` ile **açıkça** yazılır (vpk'nın gövdeyi kendiliğinden doldurup doldurmadığı dokümanda
  net değil — belirsizliğe yaslanılmaz). Gövdenin sonuna `**Full Changelog**: .../compare/v1.0.0...v1.1.0` eklenir.
- **Kural (CLAUDE.md'ye):** kullanıcıya görünen davranış değiştiren her iş `[Unreleased]`'a bir satır ekler; release
  script `[Unreleased]` boşsa durur.

Otomatik üretim (git-cliff / GitHub "generate notes") **seçilmedi:** commit mesajları Türkçe ve teknik; UI/notlar İngilizce
ve kullanıcı dilinde olmak zorunda (CLAUDE.md dil kuralı). PR'sız merge akışında GitHub'ın PR tabanlı üreteci de boş kalır.

### 5.3 Paketleme (Velopack)

| Parametre | Değer | Neden |
|---|---|---|
| `--packId` | **`BuildOrchestrator.App`** | Velopack `%LOCALAPPDATA%\<packId>\`'a kurar ve kaldırırken **o klasörü siler**. `BuildOrchestrator` deseydik state dizini (20 GiB worktree havuzu, `build-state.json`) ile aynı klasör olurdu → uninstall kullanıcı verisini silerdi, güncelleme ise dev verinin yanında dosya değiştirirdi. Ayrı kimlik = ayrı klasör |
| `--packTitle` / `--packAuthors` | `Build Orchestrator` / `Delta` | Start Menu, Uygulamalar listesi. Değerler props'taki `Product`/`Company` ile aynı olmalı → `package.ps1` bunları props'tan **okur**, elle yazmaz |
| `--packVersion` | props `<Version>` | tek kaynak |
| `--packDir` | `dotnet publish` çıktısı (README'deki komutun aynısı: `-c Release -r win-x64 --self-contained false`) | `supervisor\` ve `Assets\GEIST-LICENSE.txt` publish listesinden gelir; ekstra iş yok |
| `--mainExe` | `BuildOrchestrator.App.exe` | |
| `--framework` | `net10.0-x64-desktop` | framework-dependent publish olduğu için şart; Setup eksikse Desktop Runtime kurar. Self-contained'e geçilirse **kaldırılır** (Velopack dokümanı uyarıyor) |
| `--icon` | `src/BuildOrchestrator.App/Assets/app-icon.ico` | mevcut çok boyutlu ICO |
| `--releaseNotes` | CI'ın kestiği bölüm dosyası | pakete gömülür |
| `--shortcuts` | `StartMenuRoot` (öneri; varsayılan Desktop+StartMenu) | geliştirici aracı, masaüstü kısayolu istenmiyor — **karar §7** |
| `--exclude` | varsayılan (`.*\.pdb`) | pdb'ler pakete girmez |
| `--channel` | varsayılan `win` | ileride `beta` kanalı eklenebilir |

Çıktılar (`Releases/` → `.gitignore`'a eklenir): `BuildOrchestrator.App-win-Setup.exe`, `…-win-Portable.zip`,
`…-full.nupkg`, `…-delta.nupkg` (ikinci yayından itibaren), `releases.win.json`. Delta için CI önce
`vpk download github` ile önceki yayını çeker (ilk yayında bulunamaması hata değildir).

Kurulum yerleşimi: `%LOCALAPPDATA%\BuildOrchestrator.App\` → `Update.exe`, `current\` (uygulama + `supervisor\`),
`packages\`. Güncellemede **yalnız `current\` değişir**; `current\BuildOrchestrator.App.exe` yolu sabit kalır → autostart
registry değeri bozulmaz. Portable zip'in kendini güncelleyip güncellemediği uygulama aşamasında doğrulanır (dokümanda
net değil); README ona göre yazılır.

### 5.4 Uygulama içi güncelleme

**Giriş noktası.** Velopack `VelopackApp.Build().Run()`'ın WPF'te **`Main`'in ilk satırı** olmasını ister (kurulum/
kaldırma kancalarında WPF hiç ayağa kalkmadan çıkılır). Yeni `Program.cs`:

```csharp
[STAThread]
static int Main(string[] args)
{
    VelopackApp.Build()
        .OnBeforeUninstallFastCallback(_ => /* HKCU Run autostart değerini sil — AutostartService.Apply(false) */)
        .Run();
    var app = new App();
    app.InitializeComponent();
    return app.Run();
}
```

csproj: `<StartupObject>BuildOrchestrator.App.Program</StartupObject>`. `--veloapp-install|obsolete|updated|uninstall
{version}` argümanları `Run()` içinde tüketilip çıkılır; normal açılışta `e.Args` aynen `StartupArgs.Decide`'a ulaşır
(tanınmayan argüman zaten yutulur). `--font-ab` ve ikinci-instance yolları değişmez; güncelleme servisi **yalnız** normal
composition root'ta kurulur.

**Servis (App katmanı, `Services/UpdateService.cs`).** Velopack bir seam arkasında (`IAppUpdater`: `IsInstalled`,
`CheckAsync`, `DownloadAsync`, `RestartToApply`) — testler fake kullanır, Velopack'e dokunmaz. Zamanlama: pencere
gösterildikten 5 sn sonra ilk kontrol (açılış koreografisi ve engine boot'la yarışmasın), sonra `DispatcherTimer`
4 saat; tüm ağ işi UI thread dışında. Bulunca hemen indirir (Velopack hash doğrular; `ChecksumFailedException` → sessiz,
bir sonraki turda yeniden). Sonuç `UpdateState`: `NotInstalled | Idle | Checking | Downloading | Ready(version) | Failed(reason)`.

**Karar dikişi (saf, testli):** `UpdateGate.Decide(state, runActive)` → butonun görünürlüğü/etkinliği/tooltip'i.
`SecondInstanceGate`/`StartupArgs` deseniyle aynı gerekçe: karar WPF'siz test edilir.

| state | run aktif? | buton | tooltip |
|---|---|---|---|
| `Ready(v)` | hayır | görünür, etkin | `Version 1.1.0 is ready — restart to update` |
| `Ready(v)` | evet | görünür, **devre dışı** | `Restart to update — after the current run` |
| diğer | — | gizli | — |

**Title bar butonu.** Sağ komut grubunun **en solunda** (görünüm toggle'larından önce, kendi hairline ayracıyla): grup sağa
yaslı olduğundan beliren buton diğerlerini kaydırmaz. `Ds.IconButton` stili, yeni `Icon.Update` path'i (Tokens/ikon
kaynaklarına eklenir), `i`'dekiyle **aynı** 5px amber nokta deseni. Yeni XAML şablonu → **realize testi** zorunlu
(CLAUDE.md). Buton VM/`UpdateGate` çıktısına bağlanır; tıklama → `RestartToApply`.

**Yeniden başlatma.** `WaitExitThenApplyUpdates(asset, silent: true, restart: true)` sonra `Application.Shutdown()`:
`OnExit` normal koşar → `AppShutdown.WaitForAsyncDisposal(EngineHost)` Supervisor'ı düzgün kapatır → process biter →
`Update.exe` (App job üyesi olmadığı için hayatta) `current\`'ı değiştirir ve uygulamayı yeniden açar. `ApplyUpdatesAndRestart`
**kullanılmaz** (Environment.Exit ile `OnExit`'i atlar; cascade yine öldürür ama düzgün kapanış varken tercih edilmez).
Gate mid-run'da butonu kapattığı için `MSBuild.exe` asla ortada kalmaz.

**Tepsi.** Pencere tepsideyken güncelleme hazır olursa balloon **yok** (tasarım kuralı: toast yok); buton pencere
açılınca görünür. Tepsi menüsüne madde eklenmez (YAGNI).

**About → Environment.** Tek satır: `Updates` = son kontrol saati + sonuç (`up to date` / `1.1.0 ready` / `could not
reach GitHub` / `not an installed copy`). Diagnostics raporuna da girer (destek talebinde işe yarar).

### 5.5 CI/CD

**`.github/workflows/ci.yml`** — `push` + `pull_request` (main): `windows-latest`, `actions/setup-dotnet` (10.0.x),
`dotnet build`, `dotnet test --filter "Category!=Acceptance"`. Yayın yok.

**`.github/workflows/release.yml`** — `push: tags: ['v*']`, `permissions: contents: write`:

```yaml
name: release
on: { push: { tags: ['v*'] } }
permissions: { contents: write }
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.0.x }
      - name: Guard — tag equals Directory.Build.props Version
        run: pwsh scripts/release-guard.ps1 -Tag $env:GITHUB_REF_NAME
      - run: dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
      - run: dotnet tool install -g vpk
      - run: vpk download github --repoUrl https://github.com/${{ github.repository }}
        continue-on-error: true            # ilk yayında önceki paket yok
      - run: pwsh scripts/package.ps1      # publish + notes extract + vpk pack (lokalle aynı script)
      - run: >
          vpk upload github --repoUrl https://github.com/${{ github.repository }}
          --token ${{ secrets.GITHUB_TOKEN }} --publish
          --releaseName "Build Orchestrator ${{ github.ref_name }}" --tag ${{ github.ref_name }}
      - run: gh release edit ${{ github.ref_name }} --notes-file Releases/notes.md
        env: { GH_TOKEN: ${{ github.token }} }
```

- Gizli anahtar **gerekmez**: `GITHUB_TOKEN` yeterli (public repo'da `vpk download` token'sız çalışır).
- `scripts/package.ps1`: props'tan Version/Product/Company okur → `dotnet publish` (README'deki komut) → CHANGELOG'dan
  bölümü `Releases/notes.md`'ye keser → `vpk pack …`. Lokalde `-SkipTests` ile elle paket almak için de aynı script.
- **Bilinen risk:** süit `windows-latest`'ta ilk kez koşacak. `LegacyFixture` gerçek `MSBuild.exe` ile **v4.6** class
  library derliyor; runner'da VS 2022 + vswhere var ama 4.6 targeting pack'i garanti değil; WPF STA testleri servis
  oturumunda koşmayabilir. Kural: **lokal tam süit kapı olmaya devam eder** (release script koşar); CI'da koşamayan
  testler ayrı bir `Category` ile işaretlenip filtrelenir, sessizce silinmez/gevşetilmez.
- `scripts/verify-publish.ps1` CI'a girmez (canlı pencere + UI Automation); release script'te `-Verify` anahtarıyla
  isteğe bağlı koşar.

### 5.6 GitHub tarafı ayarlar

- **Tag protection** (`v*`): yalnız repo sahibi tag oluşturabilir → yanlışlıkla/başkasınca yayın tetiklenemez.
- **Branch rule** `main`: force-push kapalı (bugünkü merge akışına dokunmaz).
- Hesapta **2FA** zorunlu düşünülmeli: güncelleme kaynağı = bu repo'nun Releases'ı; hesabı ele geçiren herkesin
  yazdığı paketi tüm client'lar indirir (§5.9).
- Actions: sürüm pinli `uses:`; `permissions` yalnız release job'ında `contents: write`.
- İsteğe bağlı: Dependabot (NuGet + Actions sürümleri), Issues'da bug şablonu.

### 5.7 Kod imzalama

- **v1: imzasız.** SmartScreen ilk çalıştırmada uyarır; README *Install* bölümü adımı anlatır. Kurulum per-user olduğu
  için UAC yok.
- **Sonra: SignPath Foundation** (OSS projelerine ücretsiz OV imza). Koşullar: OSI lisansı, public repo, ücretsiz indirme,
  aktif bakım, **halihazırda yayınlanmış** proje → yani önce §5.8 + ilk yayın, sonra başvuru. Entegrasyon: GitHub Actions
  adımı, `Setup.exe`/`Update.exe`/uygulama exe'leri imzalanır (Velopack `--signTemplate` ile ya da pack öncesi).
- **Azure Artifact Signing** (eski Trusted Signing, ~10 $/ay) bireysel geliştiriciye **yalnız ABD/Kanada**'da açık →
  uygulanamaz. Klasik OV/EV sertifika ücretli → "ücretsiz" hedefine aykırı.

### 5.8 Lisans ve üçüncü taraf

- Repo köküne `LICENSE` (**MIT** önerisi; Geist fontlarının OFL'i ile uyumlu). README *Licence* bölümü ("no licence file")
  yeniden yazılır; `Copyright` props'ta zaten var.
- `ThirdPartyNotices.All`'a `Velopack` (MIT, `https://github.com/velopack/velopack`) — eklenmezse mevcut guard testi
  kırmızı verir.

### 5.9 Güvenlik sınırı güncellemesi (ARCHITECTURE §21)

Bugün "tek ağ dokunuşu `git fetch`" deniyor; artık ikinci dokunuş var: GitHub Releases'a HTTPS (releases listesi API'si +
asset indirme). Yazılacaklar: indirilen paket **boyut + hash** ile doğrulanır (Velopack); bütünlük zinciri = TLS +
GitHub hesabı; kod imzası yok (v1) → dosya sistemi üzerinde yerel saldırgan zaten tehdit modeli dışı (§21.5), ağdaki
MITM TLS'e bırakılır. Rate limit: kimliksiz GitHub API IP başına 60 istek/saat; 4 saatlik kontrol ile ofis NAT'ı arkasında
onlarca kullanıcı bile sınırın çok altında kalır.

### 5.10 Doküman güncellemeleri (aynı işte)

- **ARCHITECTURE.md:** yeni bölüm *Distribution and updates* (paket kimliği, kurulum yerleşimi, güncelleme zamanlaması,
  gate, restart yolu, CI akışı); §12.1 giriş noktası; §12.4 title bar komut grubu; §16 kurulum dizini ≠ state dizini;
  §18 publish → paketleme; §21 ağ yüzeyi; §22 kod haritası (`Program.cs`, `UpdateService`, `UpdateGate`, CHANGELOG
  parser); §1.3 non-goal listesi (MSIX kalır).
- **README.md:** *Install* (link, SmartScreen, runtime), *Updates* (davranış), *Release process* (tek komut, CHANGELOG
  kuralı), *Licence*.
- **CLAUDE.md:** çalışma kuralı — "kullanıcıya görünen değişiklik → `[Unreleased]`'a satır"; yayın komutu; `Releases/`
  çıktısı; CI'da koşmayan test kategorisi.

---

## 6. Uygulama aşamaları

Her aşama kendi branch'inde, test önce (kırmızı gösterilmeden fix yok), doküman aynı işte, sonunda tam süit yeşil,
`main`'e merge + push. Aşamalar birbirinden bağımsız merge edilebilir; sıra bağımlılığa göredir.

| # | Aşama | İçerik | Testler (yeni/yeniden yazılan) | Boyut |
|---|---|---|---|---|
| 0 | Repo hijyeni | `LICENSE`, `.gitignore` (`Releases/`), `ci.yml`, README Licence | — (CI'ın yeşil olduğu görülür) | kısa |
| 1 | Sürüm notu tek kaynağı | `CHANGELOG.md` (1.0.0 taşınır), gömülü kaynak + parser, `ReleaseNotes.All` parser'dan, `+it5` kaldırılır | `ChangelogTests` (biçim/props eşitliği guard'ı), `WhatsNewTests` uyarlaması, `+it5` testinin yeni kurala göre yeniden yazımı | orta |
| 2 | Yayın altyapısı | `Program.cs` + `StartupObject`, Velopack paketi, `ThirdPartyNotices`, `scripts/package.ps1`, `scripts/release.ps1`, `scripts/release-guard.ps1`, `release.yml`; **ilk yayın** | `ThirdPartyNoticesTests` (mevcut), `PublishLayoutTests`'e Velopack/StartupObject guard'ı, `StartupArgs` testine `--veloapp-*` yutulma vakası | uzun |
| 3 | Uygulama içi güncelleme | `IAppUpdater` + `UpdateService`, `UpdateGate`, title bar butonu + `Icon.Update`, About Environment satırı, uninstall kancasında autostart temizliği | `UpdateGateTests` (tablo §5.4), butonun realize testi, VM bağlama testi, `AutostartService` uninstall yolu; **ikinci yayınla** uçtan uca gerçek güncelleme denemesi | uzun |
| 4 | Sonra / isteğe bağlı | SignPath başvurusu + imza adımı; `beta` kanalı (`--channel beta`, `--pre`, `GithubSource(prerelease: true)`); winget manifesti | — | — |

Aşama 2'nin sonunda **v1.1.0** ilk GitHub yayını olur (notlarında "automatic updates" maddesi Aşama 3 ile gelecekse
1.1.0 yalnız "installer" der; ya da 2+3 birlikte bitirilip tek yayın yapılır — §7).

---

## 7. Senden beklenen kararlar (önerim önde)

1. **Paket kimliği:** `BuildOrchestrator.App` (state dizini çakışması nedeniyle `BuildOrchestrator` OLMAZ). Alternatif
   `Delta.BuildOrchestrator`.
2. **Kısayol:** yalnız Start Menu (öneri) / Start Menu + Masaüstü.
3. **İlk yayın numarası:** `1.1.0` (öneri — `1.0.0` girdisi geçmiş olarak kalır, installer+updater yeni özellik) / `1.0.0`.
4. **Yayın tetikleyicisi:** lokal `release.ps1` (öneri; diff'i gözünle görürsün) / GitHub UI'dan `workflow_dispatch`
   (runner commit + tag atar, tek tık ama "sihirli").
5. **Kontrol sıklığı:** açılış + 4 saat (öneri) / yalnız açılış / Settings'te kapatma anahtarı (YAGNI, önerilmez).
6. **Lisans:** MIT (öneri).
7. **İmza:** v1 imzasız + SignPath başvurusu ilk yayından sonra (öneri) / imzasız kal.
8. **Aşama 2 ve 3 tek yayında mı:** ayrı (öneri: 1.1.0 installer, 1.2.0 auto-update — ikinci yayın güncelleme yolunu
   gerçekten test eder) / birlikte.

---

## 8. Günlük kullanım kopya kağıdı (uygulama sonrası)

```powershell
# geliştirme
git switch -c feat/x
#  … kod …  + CHANGELOG.md → [Unreleased] → "### Added / - One short line."
git commit -am "feat(x): …" ; git switch main ; git merge feat/x ; git push

# yayın (tek komut; CI gerisini yapar, ~10-15 dk)
pwsh scripts/release.ps1 -Version 1.2.0

# elle paket (yayınsız, deneme)
pwsh scripts/package.ps1 -SkipTests      # → Releases\BuildOrchestrator.App-win-Setup.exe
```

---

## 9. Kaynaklar (doğrulanan noktalar)

- Velopack CLI 1.2.0 seçenekleri (`pack`/`upload github`/`download github`): https://docs.velopack.io/reference/cli/content/vpk-windows
- Velopack .NET başlangıç (`Main`'de `VelopackApp.Build().Run()`, `UpdateManager` API): https://docs.velopack.io/getting-started/csharp
- Velopack entegrasyon (`GithubSource(url, null, false)`, `%LocalAppData%\{packId}\current\`, yalnız `current` değişir, hash doğrulama): https://docs.velopack.io/integrating/overview
- Velopack kancalar (`--veloapp-*` argümanları, 30/15 sn zaman aşımı): https://docs.velopack.io/integrating/hooks
- Velopack runtime bootstrap (`net{major.minor}-{arch}-{type}`, ≥ .NET 5, self-contained'de `--framework` verilmez): https://docs.velopack.io/packaging/bootstrapping
- Velopack GitHub Actions örneği (`permissions: contents: write`, `vpk download` → delta): https://docs.velopack.io/distributing/github-actions
- Velopack `UpdateManager` üyeleri (`WaitExitThenApplyUpdates`, `UpdatePendingRestart`): https://docs.velopack.io/reference/cs/Velopack/UpdateManager
- SignPath Foundation OSS koşulları: https://signpath.org/terms.html
- Azure Artifact Signing bireysel uygunluk (ABD/Kanada): https://azure.microsoft.com/en-us/products/artifact-signing
- Repo görünürlüğü: `api.github.com/repos/sdemir60/app_build_orchestrator` → `"private": false`, `"license": null`
