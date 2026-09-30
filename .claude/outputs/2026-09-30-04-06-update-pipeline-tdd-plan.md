# Yayın hattı + uygulama içi güncelleme — TDD uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/release` tek komutuyla sürüm çıkan, GitHub Actions'ta paketlenip GitHub Releases'a düşen ve kullanıcı makinesinde title bar'daki `Update` hapıyla kurulan bir Velopack güncelleme hattı.

**Architecture:** Paketleme/güncelleme motoru Velopack (`Program.cs` girişinde `VelopackApp.Build().Run()`); yayın `scripts/release.ps1` (lokal: guard + tam süit + commit + tag + push) → `release.yml` (tag'de: build + test + `package.ps1` + `vpk upload github`); uygulama içinde `IAppUpdater` seam'i arkasında `UpdateService` (açılış + 5 s, 4 saat; indir → `RunViewModel.AvailableUpdate`), restart = "Closing…" ekranı → düzgün çıkış → `WaitExitThenApplyUpdates`. Core ve Supervisor'a Velopack girmez.

**Tech Stack:** .NET 10 / WPF, Velopack 1.2.161 (NuGet + `vpk` dotnet tool), GitHub Actions `windows-2025`, Windows PowerShell 5.1 uyumlu script'ler, xUnit + `Microsoft.Extensions.TimeProvider.Testing` 10.10.0.

**Spec:** `.claude/outputs/2026-09-29-22-10-update-pipeline-analysis.md` (§5 tasarım, §9 kararlar K1–K12 — hepsi kullanıcı onaylı).

## Global Constraints

- Branch `feat/release-pipeline` (worktree `D:\Projects\Other\Apps\app_build_orchestrator-ai`); task başına commit, mesaj Türkçe ASCII, mevcut üslup (`feat(x): …`), **attribution satırı yok**; commit mesajı dosyaya yazılıp `git commit -F` ile atılır.
- **Kırmızı test kuralı:** hiçbir üretim kodu, onu bekleyen test KIRMIZI görülmeden yazılmaz. Davranış bilerek değişen testler silinmez, yeni kuralı pinleyecek şekilde yeniden yazılır ve doc'una eski iddia + gerekçe yazılır.
- Kopya YASAK: aynı metin/değer iki yerde tanımlanmaz (publish komutu TEK sahibe iner: `scripts/package.ps1`; Velopack sürümü csproj + tool manifest'te durur ve guard testi eşitler).
- UI metinleri ve loglar İngilizce; kod yorumları Türkçe. Token dışı hex/ms yok (mevcut guard'lar).
- Dosya düzenlemede `Get-Content|Set-Content` ve `sed -i` YOK (UTF-8/CRLF bozar) — Edit/Write.
- Script'ler Windows PowerShell 5.1'de çalışır: `&&`/`||`/ternary yok; `Set-Content -Encoding utf8`.
- Paket kimliği **`BuildOrchestrator.App`** (K1); kısayol **Desktop + Start Menu** (K2, Velopack varsayılanı); lisans **MIT** (K3); portable **yok** (K4); kart en çok **5** madde + `+N more in What's new after restart` düz metin (K5); restart ekranı tek adım **Closing…**, Update.exe **silent** (K6); ilk yayın **v1.8.0 = installer + motor** (K7); kontrol **5 s + 4 saat** (K8); prerelease **yalnız env bayrağıyla** (K9); release commit **doğrudan main'de, script atar** (K10); CI **tam süit**, runner'da koşamayanlar `Category=LocalOnly` (K11); **`global.json`** eklenir (K12).
- Uygulama açıkken build alınmaz (Supervisor kendi binary'lerini kilitler). Tam süit `Category!=Acceptance`.

## Review Focus

1. **Update.exe dosya kilidiyle karşılaşır:** App kapanırken Supervisor/MSBuild hâlâ `current\supervisor\*.dll` tutuyorsa güncelleme yarım kalır → Task 11 `EngineHost.KillCurrent` öldürdüğü process'in çıkışını bekler; test `EngineHostTests.Dispose_waits_for_the_supervisor_process_to_exit`.
2. **Kurulu olmayan kopya (bin'den dev build) motoru başlatır:** `IsInstalled == false` iken `CheckForUpdatesAsync` fırlatır → Task 9 `UpdateService.Start` kapısı; test `Start_does_nothing_when_the_copy_is_not_installed`.
3. **Aynı sürüm ikinci kez teklif edilir / eski sürüm teklif edilir:** hap sürekli giriş oynatır ya da downgrade kurulur → Task 9 sürüm karşılaştırması; testler `An_equal_or_older_version_is_ignored`, `The_same_ready_version_is_not_published_twice`.
4. **Bozuk/boş feed notu kartı düşürür:** `ReleaseNotes.Parse` `FormatException` fırlatır → Task 8 `UpdateOffer.From` boş öne çıkanlarla döner; test `Unparsable_notes_yield_an_offer_without_highlights`.
5. **İlk yayında `vpk download github` release yokken hata verir:** Task 3 `package.ps1 -DownloadPrevious` önce release var mı sorar; test `ReleaseScriptsTests.Download_previous_is_skipped_when_the_repository_has_no_release` (mock: `gh` yerine `--ReleaseCountProbe` parametresi).

---

## Dosya yapısı

**Yeni**
- `LICENSE` — MIT.
- `global.json` — SDK 10.0.400, `latestFeature`.
- `.config/dotnet-tools.json` — `vpk` 1.2.161.
- `src/BuildOrchestrator.App/Program.cs` — giriş noktası (Velopack ilk satır).
- `src/BuildOrchestrator.App/Services/Updates/IAppUpdater.cs` — seam + `UpdateCandidate`.
- `src/BuildOrchestrator.App/Services/Updates/UpdateFeed.cs` — kaynak sabiti + env override → `IUpdateSource`.
- `src/BuildOrchestrator.App/Services/Updates/VelopackUpdater.cs` — `UpdateManager` sarmalayıcısı.
- `src/BuildOrchestrator.App/Services/Updates/UpdateService.cs` — zamanlama + durum makinesi + çıkışta kurulum.
- `scripts/package.ps1`, `scripts/release-guard.ps1`, `scripts/release.ps1`.
- `.github/workflows/ci.yml`, `.github/workflows/release.yml`.
- `.claude/skills/release/SKILL.md` — `/release`.
- Testler: `tests/.../App/RepoHygieneTests.cs`, `EntryPointTests.cs`, `ReleaseScriptsTests.cs`, `UpdateFeedTests.cs`, `UpdateServiceTests.cs`, `VelopackUpdaterTests.cs`, `UpdateRestartFlowTests.cs`, `UpdateOffers.cs` (test fixture — örnek teklifin TEK yeri).

**Değişen**
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` — `StartupObject`, `Velopack` paketi.
- `src/BuildOrchestrator.App/App.xaml.cs` — DI (`IAppUpdater`, `UpdateService`), `Start()`, Restart isteği → `RequestRestart`, `OnExit` → `ApplyOnExit`.
- `src/BuildOrchestrator.App/Services/AutostartService.cs` — `RemoveForUninstall`.
- `src/BuildOrchestrator.App/Services/EngineHost.cs` — `KillCurrent` çıkışı bekler.
- `src/BuildOrchestrator.App/Services/UpdateOffer.cs` — `From`, boyut, 5 madde + `MoreCount`; `Sample`/`NextMinor` kalkar.
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Update.cs` — varsayılan `null`, yorumlar.
- `src/BuildOrchestrator.App/ViewModels/UpdateText.cs` — `MoreHighlights`, tek adım etiketi.
- `src/BuildOrchestrator.App/ViewModels/UpdateRestartTimeline.cs` — tek adım.
- `src/BuildOrchestrator.App/Views/UpdateRestartScreen.xaml.cs` — sönüş yok, ekran çıkışa dek kalır.
- `src/BuildOrchestrator.App/Views/UpdateCard.xaml(.cs)` — `PART_More`.
- `src/BuildOrchestrator.App/MainWindow.UpdateRestart.cs` — Restart → `RequestFullExit()`.
- `scripts/verify-publish.ps1` — publish'i `package.ps1`'den alır.
- `tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj` — `Microsoft.Extensions.TimeProvider.Testing`.
- Testler: `UpdateOfferTests`, `UpdatePillTests`, `UpdateRestartLockTests`, `AccessibilityTests` (Sample → fixture), `UpdateRestartTimelineTests`, `UpdateRestartScreenTests`, `ReducedMotionCoverageTests:331`, `StartupPathTests`, `AutostartServiceTests`, `EngineHostTests`.
- Dokümanlar: `README.md`, `ARCHITECTURE.md`, `CLAUDE.md`.

---

## P1 — Yayın hattı

### Task 1: Repo hijyeni — LICENSE, global.json, tool manifest

**Files:**
- Create: `LICENSE`, `global.json`, `.config/dotnet-tools.json`
- Test: `tests/BuildOrchestrator.Tests/App/RepoHygieneTests.cs`

**Interfaces:**
- Produces: `.config/dotnet-tools.json` → `tools.vpk.version` (Task 2 guard'ı ve Task 3 script'i okur; `dotnet tool restore` + `dotnet vpk …`).

- [ ] **Step 1: Kırmızı test**

```csharp
using System.IO;
using System.Text.Json;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 1] Repo kökündeki dağıtım dosyaları: MIT lisansı (SignPath önkoşulu; public repo'da
/// lisanssız = hak verilmemiş), SDK bandını lokal ile CI'da eşitleyen global.json ve vpk'yı pinleyen tool manifest.</summary>
public class RepoHygieneTests
{
    [Fact]
    public void The_repository_carries_an_MIT_licence()
    {
        string text = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "LICENSE"));
        Assert.StartsWith("MIT License", text, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Delta Yazılım", text, StringComparison.Ordinal);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void global_json_pins_the_sdk_band_and_rolls_forward_within_it()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.StartsWith("10.0.", sdk.GetProperty("version").GetString(), StringComparison.Ordinal);
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void The_tool_manifest_pins_vpk()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".config", "dotnet-tools.json")));
        var vpk = doc.RootElement.GetProperty("tools").GetProperty("vpk");
        Assert.Matches(@"^\d+\.\d+\.\d+$", vpk.GetProperty("version").GetString());
        Assert.Equal("vpk", vpk.GetProperty("commands")[0].GetString());
    }
}
```

- [ ] **Step 2: Kırmızıyı gör** — `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~RepoHygieneTests"` → 3 FAIL (`FileNotFoundException`).

- [ ] **Step 3: Dosyalar**

`LICENSE` — standart MIT metni, ilk satır `MIT License`, telif satırı `Copyright (c) 2026 Delta Yazılım` (props `Copyright` ile aynı ad).

`global.json`:
```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestFeature"
  }
}
```

`.config/dotnet-tools.json`:
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "vpk": {
      "version": "1.2.161",
      "commands": [ "vpk" ]
    }
  }
}
```

- [ ] **Step 4: Yeşil** — aynı filtre → 3 PASS. `dotnet tool restore` koşar, `dotnet vpk --help` cevap verir.

- [ ] **Step 5: Commit** — `chore(repo): MIT lisansi, global.json (10.0.400 latestFeature), vpk tool manifest (1.2.161)`

---

### Task 2: Giriş noktası — `Program.cs`, Velopack, uninstall kancası

**Files:**
- Create: `src/BuildOrchestrator.App/Program.cs`
- Modify: `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` (PropertyGroup'a `StartupObject`, ItemGroup'a `Velopack` paketi), `src/BuildOrchestrator.App/Services/AutostartService.cs`
- Test: `tests/BuildOrchestrator.Tests/App/EntryPointTests.cs`, `tests/BuildOrchestrator.Tests/App/StartupPathTests.cs`, `tests/BuildOrchestrator.Tests/App/AutostartServiceTests.cs`

**Interfaces:**
- Produces: `AutostartService.RemoveForUninstall(IAutostartRegistry registry)` (static, refusal'ı yutar); `Program.Main(string[] args)`.

- [ ] **Step 1: Kırmızı testler**

`EntryPointTests.cs`:
```csharp
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 2] Velopack, kurulum/güncelleme kancalarını ana exe'yi <c>--veloapp-*</c> argümanıyla
/// çalıştırarak işletir ve <c>VelopackApp.Build().Run()</c>'ın WPF ayağa kalkmadan İLK ifade olmasını ister (kanca
/// modunda process orada biter; mutex, tepsi, DI hiç kurulmaz). WPF'in ürettiği Main bunu veremez → elle yazılmış
/// Program.Main + StartupObject. Kaynak guard'ı (SourceGuard deseni): headless bir testte Main koşturulamaz.</summary>
public class EntryPointTests
{
    private static string Csproj => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj"));
    private static string Program => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Program.cs"));

    [Fact]
    public void The_app_starts_from_the_hand_written_Program_class()
    {
        Assert.Contains("<StartupObject>BuildOrchestrator.App.Program</StartupObject>", Csproj, StringComparison.Ordinal);
        Assert.Contains("[STAThread]", Program, StringComparison.Ordinal);
        Assert.Contains("static void Main(string[] args)", Program, StringComparison.Ordinal);
    }

    [Fact]
    public void Velopack_runs_before_anything_else_in_Main()
    {
        // Main gövdesinde ilk ifade VelopackApp zinciri, WPF App ondan SONRA kurulur.
        int velopack = Program.IndexOf("VelopackApp.Build()", StringComparison.Ordinal);
        int run = Program.IndexOf(".Run();", velopack, StringComparison.Ordinal);
        int app = Program.IndexOf("new App()", StringComparison.Ordinal);
        Assert.True(velopack > 0 && run > velopack && app > run, "Main: VelopackApp.Build()…Run() önce, new App() sonra olmalı.");
        // Kanca kaydı: kaldırmada Windows başlangıç kaydı silinir (RemoveForUninstall) — dangling Run değeri kalmaz.
        Assert.Contains(".OnBeforeUninstallFastCallback(", Program, StringComparison.Ordinal);
        Assert.Contains("AutostartService.RemoveForUninstall(", Program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Velopack_package_and_the_vpk_tool_share_one_version()
    {
        var csproj = XDocument.Parse(Csproj);
        string package = csproj.Descendants("PackageReference").Single(p => (string?)p.Attribute("Include") == "Velopack")
            .Attribute("Version")!.Value;
        string manifest = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".config", "dotnet-tools.json"));
        string tool = Regex.Match(manifest, "\"vpk\"\\s*:\\s*\\{\\s*\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(tool, package); // vpk, kitaplıkla aynı sürümü bekler; ikisi ayrı yerde durur, bu test eşitler
    }
}
```

`StartupPathTests.An_unrecognised_argument_is_swallowed_and_leaves_the_normal_show_route` gövdesine iki satır:
```csharp
        // [yayın hattı] Velopack kanca argümanları Program.Main'de tüketilip process biter; buraya ulaşsalar da yutulur.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--veloapp-install", "1.8.0"], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--veloapp-updated", "1.8.0"], startMinimizedToTray: false));
```

`AutostartServiceTests.cs`'e (mevcut in-memory fake'i kullanarak — sınıftaki `FakeAutostartRegistry`/benzeri adı dosyadan al):
```csharp
    /// <summary>[yayın hattı · Task 2] Kaldırma kancası: Velopack uninstall'da Windows başlangıç kaydı silinir; registry
    /// reddederse kanca fırlatmaz (Velopack kancayı 30 s içinde bitmemişse öldürür, hata göstermez).</summary>
    [Fact]
    public void RemoveForUninstall_deletes_the_run_value_and_swallows_a_registry_refusal()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, "\"C:\\x.exe\" --autostart");
        AutostartService.RemoveForUninstall(registry);
        Assert.False(registry.Exists(AutostartService.DefaultValueName));

        var refusing = new RefusingAutostartRegistry(); // Remove → UnauthorizedAccessException
        AutostartService.RemoveForUninstall(refusing);   // fırlatmaz
    }
```
(`RefusingAutostartRegistry` dosyada yoksa 8 satırlık iç sınıf olarak ekle: tüm üyeler `throw new UnauthorizedAccessException()`.)

- [ ] **Step 2: Kırmızıyı gör** — `--filter "FullyQualifiedName~EntryPointTests|FullyQualifiedName~AutostartServiceTests|FullyQualifiedName~StartupPathTests"` → EntryPoint 3 FAIL, RemoveForUninstall derlenmez (önce metodu boş gövdeyle ekleyip kırmızı görmek gerekiyorsa `throw new NotImplementedException()` ile).

- [ ] **Step 3: Üretim kodu**

csproj:
```xml
    <PackageReference Include="Velopack" Version="1.2.161" />
```
```xml
    <!-- [yayın hattı] Giriş noktası elle yazılmış Program.Main'dir: Velopack'in kurulum/güncelleme kancaları
         (--veloapp-*) WPF ayağa kalkmadan orada tüketilir. WPF'in ürettiği App.Main kullanılmaz. -->
    <StartupObject>BuildOrchestrator.App.Program</StartupObject>
```

`Program.cs`:
```csharp
using BuildOrchestrator.App.Services;
using Velopack;

namespace BuildOrchestrator.App;

/// <summary>
/// [yayın hattı] Uygulamanın giriş noktası. <c>VelopackApp.Build().Run()</c> Main'in İLK ifadesidir: Velopack
/// kurulum, güncelleme ve kaldırma kancalarını ana exe'yi <c>--veloapp-install|obsolete|updated|uninstall &lt;sürüm&gt;</c>
/// ile çalıştırarak işletir; <c>Run()</c> bu argümanları görürse kancayı koşturur ve process'i orada bitirir — WPF,
/// tek-örnek mutex'i, tepsi ve DI hiç kurulmaz. Normal açılışta <c>Run()</c> hiçbir şey yapmaz ve WPF her zamanki gibi
/// başlar (<see cref="App.OnStartup"/>). İndirilmiş ama kurulmamış bir güncelleme varsa Velopack onu açılışta kurar
/// (<c>SetAutoApplyOnStartup</c> varsayılanı açık — tasarım §2.12: "bir sonraki açılışta kendiliğinden kurulur").
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // Kaldırmada Windows başlangıç kaydı silinir — aksi hâlde HKCU\...\Run'da var olmayan bir exe'ye işaret kalırdı.
            .OnBeforeUninstallFastCallback(_ => AutostartService.RemoveForUninstall(new RegistryAutostartRegistry()))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
```

`AutostartService`'e (sınıf gövdesine, `Apply`'ın altına):
```csharp
    /// <summary>[yayın hattı] Velopack'in kaldırma kancası: başlangıç kaydını siler. Servis örneği kurulmaz (komut
    /// gerekmez), registry reddederse sessizce geçer — kanca UI gösteremez ve fırlatması işe yaramaz.</summary>
    public static void RemoveForUninstall(IAutostartRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try { registry.Remove(DefaultValueName); }
        catch (Exception ex) when (IsRegistryRefusal(ex)) { /* kaldırma bir kayıt yüzünden durmaz */ }
    }
```

- [ ] **Step 4: Yeşil** — `dotnet build BuildOrchestrator.slnx` (0 uyarı), yukarıdaki filtre → PASS. Uygulamayı `dotnet run` ile bir kez aç/kapa: normal açılış değişmedi.

- [ ] **Step 5: Commit** — `feat(app): Program.Main girisinde VelopackApp.Run + StartupObject; kaldirmada autostart kaydi silinir`

---

### Task 3: `scripts/package.ps1` — publish + sürüm notu + `vpk pack` (tek sahip)

**Files:**
- Create: `scripts/package.ps1`
- Modify: `scripts/verify-publish.ps1` (publish adımı `package.ps1 -PublishOnly` çağırır)
- Test: `tests/BuildOrchestrator.Tests/App/ReleaseScriptsTests.cs`

**Interfaces:**
- Produces: `package.ps1` parametreleri — `-Configuration Release -RuntimeIdentifier win-x64 -ArtifactsDir <repo>\artifacts -PublishOnly -NotesOnly -NotesVersion x.y.z -NotesOut <dosya> -DownloadPrevious -RepoUrl <url> -Token <t> -ReleaseCount <n>`; çıktı `artifacts\publish\` ve `artifacts\velopack\` (`notes.md`, `BuildOrchestrator.App-win-Setup.exe`, `*.nupkg`, `releases.win.json`). Task 4 ve 5 aynı script'i çağırır.

- [ ] **Step 1: Kırmızı test**

```csharp
using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 3-4] Yayın script'leri Windows PowerShell 5.1 ile koşturulur (verify-publish.ps1 ile aynı
/// yürütücü). Derleme/paketleme YAPILMAZ (dakikalar sürer, Velopack aracı gerektirir); yalnız saf parçalar: sürüm notu
/// kesimi (CHANGELOG bölümü ↔ uygulamanın kendi parser'ı) ve guard'lar. Yürütücü yoksa test atlar.</summary>
[Collection("Console UI (serial)")]
public class ReleaseScriptsTests
{
    private static string Scripts => Path.Combine(RepoPaths.RepoRoot, "scripts");

    private static (int ExitCode, string Output) Run(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = RepoPaths.RepoRoot,
        };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(Path.Combine(Scripts, script));
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [SkippableFact]
    public void The_notes_cut_from_the_changelog_parse_to_the_same_entry_the_app_shows()
    {
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));
        var top = ReleaseNotes.All[0];
        using var temp = new TempDir();
        string notes = Path.Combine(temp.Path, "notes.md");
        var (code, output) = Run("package.ps1", "-NotesOnly", "-NotesVersion", top.Version, "-NotesOut", notes);
        Assert.True(code == 0, output);

        var parsed = Assert.Single(ReleaseNotes.Parse(File.ReadAllText(notes)));
        Assert.Equal(top.Version, parsed.Version);
        Assert.Equal(top.Date, parsed.Date);
        Assert.Equal(top.Notes, parsed.Notes);
    }

    [SkippableFact]
    public void Cutting_notes_for_an_unknown_version_fails_loudly()
    {
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));
        using var temp = new TempDir();
        var (code, output) = Run("package.ps1", "-NotesOnly", "-NotesVersion", "0.0.1", "-NotesOut", Path.Combine(temp.Path, "n.md"));
        Assert.NotEqual(0, code);
        Assert.Contains("0.0.1", output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Download_previous_is_skipped_when_the_repository_has_no_release()
    {
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));
        // -ReleaseCount 0: script GitHub'a sormaz (test), release yok → vpk download hiç çağrılmaz, çıkış 0.
        var (code, output) = Run("package.ps1", "-DownloadPrevious", "-ReleaseCount", "0", "-WhatIf");
        Assert.True(code == 0, output);
        Assert.Contains("no previous release", output, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Kırmızıyı gör** — `--filter "FullyQualifiedName~ReleaseScriptsTests"` → 3 FAIL (script yok).

- [ ] **Step 3: Script**

`scripts/package.ps1` (5.1 uyumlu; başlık yorumu Türkçe, çıktı satırları İngilizce):
```powershell
<#
 [yayin hatti] Publish + surum notu kesimi + Velopack paketlemenin TEK sahibi. Lokal deneme ve release.yml AYNI
 script'i calistirir; publish komutu baska hicbir yerde yazilmaz (README/ARCHITECTURE buraya isaret eder,
 verify-publish.ps1 -PublishOnly ile cagirir).
   package.ps1                          -> publish + notes + vpk pack  (artifacts\velopack\)
   package.ps1 -PublishOnly -PublishDir X
   package.ps1 -NotesOnly -NotesVersion 1.8.0 -NotesOut notes.md
   package.ps1 -DownloadPrevious -RepoUrl ... -Token ...   (delta icin onceki paketi ceker; release yoksa atlar)
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$ArtifactsDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$PublishDir,
    [switch]$PublishOnly,
    [switch]$NotesOnly,
    [string]$NotesVersion,
    [string]$NotesOut,
    [switch]$DownloadPrevious,
    [string]$RepoUrl = 'https://github.com/sdemir60/app_build_orchestrator',
    [string]$Token,
    [int]$ReleaseCount = -1
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$props = [xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))
$Version = $props.Project.PropertyGroup.Version
$Product = $props.Project.PropertyGroup.Product
$Company = $props.Project.PropertyGroup.Company
if (-not $PublishDir) { $PublishDir = Join-Path $ArtifactsDir 'publish' }
$ReleasesDir = Join-Path $ArtifactsDir 'velopack'
$appProj = Join-Path $repo 'src\BuildOrchestrator.App\BuildOrchestrator.App.csproj'

function Get-ChangelogSection([string]$Path, [string]$Wanted) {
    # "## [x.y.z] - yyyy-MM-dd" basligindan bir sonraki "## [" basligina kadar (baslik dahil) — App'in parser'i
    # (ReleaseNotes.Parse) bu bicimi tek bolum olarak okur.
    $lines = Get-Content -Path $Path
    $out = New-Object System.Collections.Generic.List[string]
    $in = $false
    foreach ($line in $lines) {
        if ($line -match '^## \[(?<v>\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}$') {
            if ($in) { break }
            if ($Matches.v -eq $Wanted) { $in = $true }
        }
        if ($in) { $out.Add($line) }
    }
    if (-not $in) { throw "CHANGELOG.md has no section for version $Wanted." }
    while ($out.Count -gt 0 -and $out[$out.Count - 1] -eq '') { $out.RemoveAt($out.Count - 1) }
    return ($out -join "`r`n") + "`r`n"
}

if ($NotesOnly) {
    if (-not $NotesVersion) { $NotesVersion = $Version }
    if (-not $NotesOut) { $NotesOut = Join-Path $ReleasesDir 'notes.md' }
    New-Item -ItemType Directory -Force (Split-Path $NotesOut -Parent) | Out-Null
    [System.IO.File]::WriteAllText($NotesOut, (Get-ChangelogSection (Join-Path $repo 'CHANGELOG.md') $NotesVersion), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "notes -> $NotesOut"
    exit 0
}

if ($DownloadPrevious) {
    if ($ReleaseCount -lt 0) {
        $api = ($RepoUrl -replace '^https://github.com/', 'https://api.github.com/repos/') + '/releases?per_page=1'
        $headers = @{ 'User-Agent' = 'BuildOrchestrator-package' }
        if ($Token) { $headers['Authorization'] = "Bearer $Token" }
        $ReleaseCount = @(Invoke-RestMethod -Uri $api -Headers $headers).Count
    }
    if ($ReleaseCount -eq 0) {
        Write-Host 'no previous release - delta skipped (first release)'
    }
    elseif ($PSCmdlet.ShouldProcess($RepoUrl, 'vpk download github')) {
        $dl = @('vpk', 'download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleasesDir)
        if ($Token) { $dl += @('--token', $Token) }
        & dotnet @dl
        if ($LASTEXITCODE -ne 0) { throw "vpk download github failed (exit $LASTEXITCODE)." }
    }
    if ($WhatIfPreference) { exit 0 }
}

if ($PSCmdlet.ShouldProcess($PublishDir, 'dotnet publish')) {
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    & dotnet publish $appProj -c $Configuration -r $RuntimeIdentifier --self-contained false -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
}
if ($PublishOnly) { exit 0 }

New-Item -ItemType Directory -Force $ReleasesDir | Out-Null
$notes = Join-Path $ReleasesDir 'notes.md'
[System.IO.File]::WriteAllText($notes, (Get-ChangelogSection (Join-Path $repo 'CHANGELOG.md') $Version), (New-Object System.Text.UTF8Encoding($false)))

if ($PSCmdlet.ShouldProcess("$Product $Version", 'vpk pack')) {
    & dotnet vpk pack `
        --packId 'BuildOrchestrator.App' `
        --packVersion $Version `
        --packDir $PublishDir `
        --mainExe 'BuildOrchestrator.App.exe' `
        --packTitle $Product `
        --packAuthors $Company `
        --icon (Join-Path $repo 'src\BuildOrchestrator.App\Assets\app-icon.ico') `
        --framework 'net10.0-x64-desktop' `
        --releaseNotes $notes `
        --noPortable `
        --outputDir $ReleasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)." }
    Write-Host "package -> $ReleasesDir"
}
```
(`--shortcuts` verilmez: varsayılan `Desktop,StartMenuRoot` = K2. `-WhatIf` publish/pack/download'ı atlar — test bunu kullanır.)

`verify-publish.ps1:104` satırı:
```powershell
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'package.ps1') -PublishOnly -PublishDir $OutputDir -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
```
(`$publishLog` değişkeni artık script çıktısıdır; `Check` satırı aynen kalır.)

- [ ] **Step 4: Yeşil** — filtre → 3 PASS. Elle: `powershell -File scripts\package.ps1 -PublishOnly` → `artifacts\publish\BuildOrchestrator.App.exe` + `supervisor\` var. (Tam `vpk pack` denemesi Task 14'te.)

- [ ] **Step 5: Commit** — `feat(scripts): package.ps1 - publish, CHANGELOG bolumu kesimi ve vpk pack tek sahipte; verify-publish publish'i buradan alir`

---

### Task 4: `release-guard.ps1` + `release.ps1`

**Files:**
- Create: `scripts/release-guard.ps1`, `scripts/release.ps1`
- Test: `tests/BuildOrchestrator.Tests/App/ReleaseScriptsTests.cs` (ek)

**Interfaces:**
- Produces: `release-guard.ps1 -Tag vX.Y.Z [-ChangelogTop]` → exit 0/1; `release.ps1 -Version X.Y.Z [-SkipTests] [-DryRun]`.

- [ ] **Step 1: Kırmızı testler** (`ReleaseScriptsTests`'e)

```csharp
    [SkippableFact]
    public void The_release_guard_accepts_only_the_tag_of_the_current_version()
    {
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));
        string version = ReleaseNotes.All[0].Version; // == props Version (WhatsNewTests pinler)
        Assert.Equal(0, Run("release-guard.ps1", "-Tag", "v" + version).ExitCode);
        var wrong = Run("release-guard.ps1", "-Tag", "v0.0.1");
        Assert.Equal(1, wrong.ExitCode);
        Assert.Contains("v0.0.1", wrong.Output, StringComparison.Ordinal);
        Assert.Equal(1, Run("release-guard.ps1", "-Tag", version).ExitCode); // 'v' öneki şart
    }

    [SkippableFact]
    public void The_release_script_refuses_a_version_whose_changelog_section_is_missing()
    {
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));
        // -DryRun: git'e ve testlere dokunmaz; yalnız guard'lar koşar. CHANGELOG'da 99.0.0 yok → 1.
        var r = Run("release.ps1", "-Version", "99.0.0", "-DryRun");
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("CHANGELOG.md", r.Output, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Kırmızıyı gör** → 2 FAIL.

- [ ] **Step 3: Script'ler**

`scripts/release-guard.ps1`:
```powershell
<# [yayin hatti] Tag <-> Directory.Build.props Version <-> CHANGELOG en ust surum esitligi. release.ps1 push'tan
   once, release.yml ilk adim olarak calistirir. Cikis 0 = tutarli, 1 = uyumsuz (mesaj sebebi yazar). #>
param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -Raw (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version
$top = (Select-String -Path (Join-Path $repo 'CHANGELOG.md') -Pattern '^## \[(\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}$' | Select-Object -First 1)
$topVersion = if ($top) { $top.Matches[0].Groups[1].Value } else { '' }
$problems = @()
if ($Tag -ne "v$version") { $problems += "tag '$Tag' does not match Directory.Build.props Version '$version' (expected 'v$version')" }
if ($topVersion -ne $version) { $problems += "CHANGELOG.md top section is '$topVersion', Directory.Build.props Version is '$version'" }
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "release guard: $_" }; exit 1 }
Write-Host "release guard: $Tag == Version == CHANGELOG top ($version)"
exit 0
```

`scripts/release.ps1`:
```powershell
<# [yayin hatti] Tek komutla yayin — /release skill'inin mekanik yarisi. Notu Claude yazmis olmali (CHANGELOG en ustte
   "## [X.Y.Z] - <bugun>"); script: guard'lar -> Version'i yazar -> build + tam suit -> "release: vX.Y.Z" commit'i ->
   annotated tag -> push main + tag. Herhangi bir adimda durursa hicbir sey push edilmemistir.
     release.ps1 -Version 1.8.0            (tam akis)
     release.ps1 -Version 1.8.0 -SkipTests (suit lokalde zaten yesil gorulduyse)
     release.ps1 -Version 1.8.0 -DryRun    (yalniz guard'lar; git'e/dosyaya dokunmaz) #>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
function Fail([string]$why) { Write-Host "release: $why"; exit 1 }

# --- guard'lar (git'e dokunmadan)
$today = Get-Date -Format 'yyyy-MM-dd'
$top = Select-String -Path 'CHANGELOG.md' -Pattern '^## \[(\d+\.\d+\.\d+)\] - (\d{4}-\d{2}-\d{2})$' | Select-Object -First 1
if (-not $top) { Fail 'CHANGELOG.md has no version section.' }
if ($top.Matches[0].Groups[1].Value -ne $Version) { Fail "CHANGELOG.md top section is $($top.Matches[0].Groups[1].Value); write the $Version section first (CHANGELOG.md)." }
if ($top.Matches[0].Groups[2].Value -ne $today) { Fail "CHANGELOG.md $Version is dated $($top.Matches[0].Groups[2].Value); a release is dated today ($today)." }
if ($DryRun) { Write-Host "release: guards passed for $Version (dry run)"; exit 0 }

if ((git branch --show-current) -ne 'main') { Fail 'not on main.' }
if (git status --porcelain | Where-Object { $_ -notmatch 'Directory\.Build\.props|CHANGELOG\.md' }) { Fail 'working tree has changes besides CHANGELOG.md / Directory.Build.props.' }
git fetch origin --quiet
if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { Fail 'main and origin/main differ; sync first.' }
if (git tag --list "v$Version") { Fail "tag v$Version already exists." }

# --- Version tek yerde
$propsPath = Join-Path $repo 'Directory.Build.props'
$props = [System.IO.File]::ReadAllText($propsPath)
$updated = [regex]::Replace($props, '<Version>\d+\.\d+\.\d+</Version>', "<Version>$Version</Version>", 1)
if ($updated -ne $props) { [System.IO.File]::WriteAllText($propsPath, $updated, (New-Object System.Text.UTF8Encoding($false))) }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'release-guard.ps1') -Tag "v$Version"
if ($LASTEXITCODE -ne 0) { exit 1 }

# --- kapi: build + tam suit (lokal kapi; CI de kosar)
& dotnet build BuildOrchestrator.slnx -c Release
if ($LASTEXITCODE -ne 0) { Fail 'build failed.' }
if (-not $SkipTests) {
    & dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --no-build --filter 'Category!=Acceptance'
    if ($LASTEXITCODE -ne 0) { Fail 'tests failed - nothing was committed or pushed.' }
}

# --- commit + tag + push
git add CHANGELOG.md Directory.Build.props
if (git status --porcelain) { git commit -q -m "release: v$Version" }
git tag -a "v$Version" -m "Build Orchestrator $Version - release notes in CHANGELOG.md"
git push origin main "v$Version"
Write-Host "release: v$Version pushed - https://github.com/sdemir60/app_build_orchestrator/actions"
exit 0
```

- [ ] **Step 4: Yeşil** → 2 PASS (+ Task 3'ün 3'ü).

- [ ] **Step 5: Commit** — `feat(scripts): release-guard.ps1 (tag == Version == CHANGELOG) ve release.ps1 (guard, tam suit, release commit, tag, push)`

---

### Task 5: GitHub Actions — `ci.yml` (+ `workflow_call`) ve `release.yml`

**Files:**
- Create: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Test: `tests/BuildOrchestrator.Tests/App/RepoHygieneTests.cs` (ek)

- [ ] **Step 1: Kırmızı test** (`RepoHygieneTests`'e)

```csharp
    private static string Workflow(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".github", "workflows", name));

    [Fact]
    public void CI_builds_and_tests_main_on_a_pinned_windows_image_and_is_callable_by_the_release()
    {
        string ci = Workflow("ci.yml");
        Assert.Contains("runs-on: windows-2025", ci, StringComparison.Ordinal);   // windows-latest sürüklenmez
        Assert.Contains("workflow_call:", ci, StringComparison.Ordinal);          // release.yml build+test'i buradan alır (kopya yok)
        Assert.Contains("global-json-file: global.json", ci, StringComparison.Ordinal);
        Assert.Contains("Category!=Acceptance&Category!=LocalOnly", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("vpk", ci, StringComparison.Ordinal);               // CI yayın yapmaz
    }

    [Fact]
    public void The_release_workflow_runs_on_version_tags_reuses_CI_and_publishes_through_the_package_script()
    {
        string release = Workflow("release.yml");
        Assert.Contains("tags: ['v*']", release, StringComparison.Ordinal);
        Assert.Contains("contents: write", release, StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/workflows/ci.yml", release, StringComparison.Ordinal);
        Assert.Contains("scripts/release-guard.ps1", release, StringComparison.Ordinal);
        Assert.Contains("scripts/package.ps1", release, StringComparison.Ordinal);
        Assert.Contains("vpk upload github", release, StringComparison.Ordinal);
        Assert.Contains("gh release edit", release, StringComparison.Ordinal);     // gövde = CHANGELOG bölümü
        Assert.DoesNotContain("dotnet publish", release, StringComparison.Ordinal); // publish komutunun tek sahibi package.ps1
    }
```

- [ ] **Step 2: Kırmızıyı gör** → 2 FAIL.

- [ ] **Step 3: Workflow'lar**

`.github/workflows/ci.yml`:
```yaml
# [yayin hatti] main'e her push'ta ve PR'da build + filtreli tam suit. Yayin yapmaz. release.yml ayni isi
# workflow_call ile buradan alir (build/test adimlari iki yerde yazilmaz).
name: ci
on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:
  workflow_call:
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
jobs:
  build-test:
    runs-on: windows-2025   # pinli: windows-latest imaji zamanla degisir; MSBuild vswhere ile cozuldugu icin VS surumunden bagimsiz
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet build BuildOrchestrator.slnx -c Release
      - run: dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --no-build --filter "Category!=Acceptance&Category!=LocalOnly" --logger "trx;LogFileName=tests.trx"
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: tests/BuildOrchestrator.Tests/TestResults/*.trx
```

`.github/workflows/release.yml`:
```yaml
# [yayin hatti] vX.Y.Z tag'i push edilince: guard -> (ci.yml: build + test) -> package.ps1 (publish + notlar + vpk pack,
# onceki paketten delta) -> GitHub Release + asset'ler -> govde = CHANGELOG bolumu. Gizli anahtar gerekmez.
name: release
on:
  push:
    tags: ['v*']
permissions:
  contents: write
jobs:
  guard:
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@v4
      - run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release-guard.ps1 -Tag ${{ github.ref_name }}
  ci:
    needs: guard
    uses: ./.github/workflows/ci.yml
  publish:
    needs: ci
    runs-on: windows-2025
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet tool restore
      - run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -DownloadPrevious -RepoUrl https://github.com/${{ github.repository }} -Token ${{ secrets.GITHUB_TOKEN }}
      - run: dotnet vpk upload github --repoUrl https://github.com/${{ github.repository }} --token ${{ secrets.GITHUB_TOKEN }} --outputDir artifacts/velopack --publish --merge --releaseName "Build Orchestrator ${{ github.ref_name }}" --tag ${{ github.ref_name }}
      - run: gh release edit ${{ github.ref_name }} --notes-file artifacts/velopack/notes.md
        env:
          GH_TOKEN: ${{ github.token }}
```

- [ ] **Step 4: Yeşil** → 2 PASS. (Gerçek koşu Task 14'te, `main`'e push ile.)

- [ ] **Step 5: Commit** — `ci: ci.yml (main build+test, workflow_call) ve release.yml (v* tag -> guard, ci, package.ps1, vpk upload, release notlari)`

---

### Task 6: `/release` skill'i ve CLAUDE.md "Sürüm çıkarma" güncellemesi

**Files:**
- Create: `.claude/skills/release/SKILL.md`
- Modify: `CLAUDE.md` (§"Sürüm çıkarma" adım 5 ve altı; §Git'e istisna)

- [ ] **Step 1: Skill**

```markdown
---
name: release
description: Build Orchestrator için yeni sürüm çıkar — CHANGELOG bölümünü yaz, numarayı seç, scripts/release.ps1 ile commit+tag+push; CI paketleyip GitHub Releases'a yayınlar. Kullanıcı "/release", "sürüm çıkar", "yeni versiyon" dediğinde.
---

# /release

1. `main`'de ve temiz ol; `git fetch` → `origin/main` ile eşit değilse dur ve söyle.
2. Kaynak: `git log --first-parent v<son tag>..main` merge mesajları + ilgili `.claude/outputs/*results*.md`.
3. Numara: yalnız düzeltme → patch · yeni özellik → minor · büyük dönüm noktası → major (kullanıcıya SOR).
4. `CHANGELOG.md`'nin en üstüne `## [X.Y.Z] - <bugün>` bölümü: kategoriler Added · Changed · Fixed · Performance ·
   Removed sırasıyla, boşu yazma; maddeler İngilizce, düz metin (markdown işareti yok), kısa, kullanıcının gördüğü
   özellik; iç terim, dosya/sınıf adı yok; küçük işler tek satırda. Her maddeyi o anki koda göre doğrula.
5. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version X.Y.Z`
   — guard'lar, `Version`, build + tam süit, `release: vX.Y.Z` commit'i, annotated tag, push. Düşerse sebebi
   kullanıcıya ilet; hiçbir şey push edilmemiştir.
6. Actions linkini ver: https://github.com/sdemir60/app_build_orchestrator/actions — `release` workflow'u ~15 dk.
   Bitince https://github.com/sdemir60/app_build_orchestrator/releases/latest sayfasında Setup.exe ve nupkg'ları gör.
7. Yayınlanmış bir sürümün notu yalnız yanlışsa düzeltilir.
```

- [ ] **Step 2: CLAUDE.md** — "Sürüm çıkarma" bölümünde adım 5'i şu metinle değiştir:

```
5. **Yayın:** `/release` (ya da elle `scripts/release.ps1 -Version X.Y.Z`): guard'lar → tam süit → `main`'de
   `release: vX.Y.Z` commit'i (bu commit için ayrı branch açılmaz — tek istisna) → annotated tag `vX.Y.Z` → push.
   Tag'i gören `release.yml` derler, `scripts/package.ps1` ile paketler ve GitHub Release'i açar; senin başka bir
   şey yapman gerekmez. Paket çıktıları `artifacts/` altındadır (ignore'lu).
```
Ve "Build / test" bölümüne bir madde: `CI (`ci.yml`) aynı süiti windows-2025 runner'ında koşar; runner'da koşamayan/kararsız test **`Category=LocalOnly`** alır ve yalnız CI filtresinde dışlanır — eşik gevşetilmez, test silinmez; lokal tam süit kapı olmaya devam eder.`

- [ ] **Step 3: Commit** — `docs(claude): /release skill'i; surum cikarma adimi script + CI akisina baglandi; LocalOnly kurali`

---

## P2 — Uygulama içi motor

Hazır UI'ın dikişleri (inceleme 2026-09-30): teklif `RunViewModel.AvailableUpdate` (public setter, UI thread'inde set edilmeli — `MainWindow.UpdatePill.cs:44-47` ve `UpdateCard.xaml.cs:45-49` WPF öğelerine dokunur); Restart isteği `RunViewModel.RestartToUpdateRequested` (kabuk `MainWindow.UpdateRestart.cs:26-30` kartı kapatıp ekranı oynatır); kilit `UpdateRestartBlockedReason` hazır. Üç UI boşluğu bu fazda kapanır: kartın UIA adı yok; öne çıkan yokken boş bant kalıyor; madde sınırı yok.

### Task 7: Seam — `IAppUpdater`, `UpdateCandidate`, `UpdateFeed`

**Files:**
- Create: `src/BuildOrchestrator.App/Services/Updates/IAppUpdater.cs`, `src/BuildOrchestrator.App/Services/Updates/UpdateFeed.cs`
- Test: `tests/BuildOrchestrator.Tests/App/UpdateFeedTests.cs`

**Interfaces (Produces):**
```csharp
namespace BuildOrchestrator.App.Services.Updates;
public sealed record UpdateCandidate(string Version, long DownloadBytes, string NotesMarkdown);
public interface IAppUpdater
{
    bool IsInstalled { get; }
    UpdateCandidate? PendingRestart { get; }
    Task<UpdateCandidate?> CheckAsync(CancellationToken ct);
    Task DownloadAsync(UpdateCandidate candidate, CancellationToken ct);
    void ApplyOnExit(UpdateCandidate candidate, bool restart);
}
public static class UpdateFeed
{
    public const string RepositoryUrl = "https://github.com/sdemir60/app_build_orchestrator";
    public const string SourceOverrideVariable = "BO_UPDATE_SOURCE";
    public const string PrereleaseVariable = "BO_UPDATE_PRERELEASE";
    public static bool IsEnabled(string? flag);                                  // "1" → true
    public static IUpdateSource CreateSource(string? overrideSource, bool prerelease);
}
```

- [ ] **Step 1: Kırmızı test**

```csharp
using BuildOrchestrator.App.Services.Updates;
using Velopack.Sources;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 7] Güncelleme kaynağı TEK sabittir (GitHub Releases); dev/test kapıları mevcut BO_* deseniyle
/// ortam değişkeninden gelir: BO_UPDATE_SOURCE bir klasör (yerel feed — GitHub'sız uçtan uca prova) ya da URL,
/// BO_UPDATE_PRERELEASE=1 prerelease'leri de görür (aşamalı yayın). UI'da ayar yoktur (tasarım §2.12).</summary>
public class UpdateFeedTests
{
    [Fact]
    public void Without_an_override_the_feed_is_the_GitHub_repository()
    {
        var source = UpdateFeed.CreateSource(null, prerelease: false);
        var github = Assert.IsType<GithubSource>(source);
        Assert.Equal(UpdateFeed.RepositoryUrl, github.RepoUri.ToString().TrimEnd('/'));
        Assert.False(github.Prerelease);
        Assert.True(Assert.IsType<GithubSource>(UpdateFeed.CreateSource("", prerelease: true)).Prerelease);
    }

    [Fact]
    public void A_folder_override_becomes_a_local_file_feed()
    {
        using var temp = new TempDir();
        Assert.IsType<SimpleFileSource>(UpdateFeed.CreateSource(temp.Path, prerelease: false));
    }

    [Fact]
    public void A_web_override_becomes_a_web_feed_and_a_GitHub_url_stays_a_GitHub_feed()
    {
        Assert.IsType<SimpleWebSource>(UpdateFeed.CreateSource("https://updates.example.com/bo/", prerelease: false));
        Assert.IsType<GithubSource>(UpdateFeed.CreateSource("https://github.com/someone/fork", prerelease: false));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("true", false)]
    public void The_prerelease_flag_is_exactly_one(string? flag, bool expected) => Assert.Equal(expected, UpdateFeed.IsEnabled(flag));
}
```
(`GithubSource.RepoUri` / `Prerelease` üyeleri yoksa — ilspy ile bak — test yalnız tipi ve `ToString()`'i pinler.)

- [ ] **Step 2: Kırmızıyı gör** — derlenmez (tipler yok).

- [ ] **Step 3: Kod**

`IAppUpdater.cs`: yukarıdaki sözleşme + XML doc: "Velopack bir seam arkasındadır: testler fake kullanır. `PendingRestart` = indirilmiş, kurulum bekleyen paket (process öldürülmüş olabilir). `ApplyOnExit` Update.exe'yi başlatır ve process'in çıkmasını bekletir (`WaitExitThenApplyUpdates`, silent); `restart` false ise sessiz kurulum, uygulama yeniden açılmaz."

`UpdateFeed.cs`:
```csharp
using System.IO;
using Velopack.Sources;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] Güncelleme beslemesinin TEK yeri: GitHub Releases (public repo, token gerekmez). Dev/test kapıları
/// ortam değişkeninden (mevcut <c>BO_*</c> deseni): <see cref="SourceOverrideVariable"/> bir klasör (yerel feed —
/// GitHub'sız uçtan uca prova) ya da URL; <see cref="PrereleaseVariable"/>=1 prerelease'leri de görür (aşamalı yayın:
/// <c>vpk upload github --pre</c>). UI'da ayar yoktur.</summary>
public static class UpdateFeed
{
    public const string RepositoryUrl = "https://github.com/sdemir60/app_build_orchestrator";
    public const string SourceOverrideVariable = "BO_UPDATE_SOURCE";
    public const string PrereleaseVariable = "BO_UPDATE_PRERELEASE";

    /// <summary>Ölçüm testlerinin kapısıyla aynı kural: yalnız tam olarak "1".</summary>
    public static bool IsEnabled(string? flag) => flag == "1";

    public static IUpdateSource CreateSource(string? overrideSource, bool prerelease)
    {
        if (string.IsNullOrWhiteSpace(overrideSource)) return new GithubSource(RepositoryUrl, accessToken: null, prerelease);
        if (overrideSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || overrideSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return overrideSource.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(overrideSource, accessToken: null, prerelease)
                : new SimpleWebSource(overrideSource);
        }
        return new SimpleFileSource(new DirectoryInfo(overrideSource)); // UpdateManager(string) de böyle çözer
    }
}
```

- [ ] **Step 4: Yeşil** → PASS. **Step 5: Commit** — `feat(update): IAppUpdater seam'i, UpdateCandidate ve UpdateFeed (GitHub sabiti, BO_UPDATE_SOURCE / BO_UPDATE_PRERELEASE kapilari)`

---

### Task 8: `UpdateOffer.From` — boyut, 5 madde + `MoreCount`; placeholder kalkar; kart boşlukları

**Files:**
- Modify: `src/BuildOrchestrator.App/Services/UpdateOffer.cs`, `ViewModels/RunViewModel.Update.cs` (varsayılan `null`, sınıf yorumu), `ViewModels/UpdateText.cs` (`MoreHighlights`), `Views/UpdateCard.xaml(.cs)` (`PART_More`, boş bant, UIA adı), `AccessibilityNames.cs` (kart adı = `UpdateTo`)
- Create: `tests/BuildOrchestrator.Tests/App/UpdateOffers.cs` (fixture)
- Test: `UpdateOfferTests.cs` (yeniden yazılır), `UpdateCardTests.cs` (ek), `UpdatePillTests.cs:203` (yeniden), `UpdateRestartLockTests.cs:243` (yeniden), `AccessibilityTests.cs:475`, `UpdateRestartLockTests.NewVm`

**Interfaces (Produces):**
```csharp
public sealed record UpdateOffer(string Version, string Size, IReadOnlyList<ReleaseNote> Highlights, int MoreCount)
{
    public const int MaxHighlights = 5;
    public static UpdateOffer From(UpdateCandidate candidate);          // Task 9 çağırır
    internal static string FormatSize(long bytes);                       // "18.4 MB"
    internal static (IReadOnlyList<ReleaseNote> Shown, int More) SelectHighlights(IReadOnlyList<ReleaseNote> notes);
}
public static string UpdateText.MoreHighlights(int count);              // "+3 more in What's new after restart"
```
Test fixture (örnek teklifin TEK yeri — eski `Sample` tüketicileri buraya döner):
```csharp
internal static class UpdateOffers
{
    public static UpdateOffer Sample(string version = "9.9.0") => new(version, "18.4 MB",
        [ new(NoteKind.Performance, "Sync reads project files in parallel — about twice as fast on large solutions."),
          new(NoteKind.Fixed, "Copy log keeps its line breaks when pasted into Teams or Outlook."),
          new(NoteKind.Fixed, "A project renamed on disk is picked up by the next Sync.") ], MoreCount: 0);
}
```

- [ ] **Step 1: Kırmızı testler**

`UpdateOfferTests.cs` (tümü yeniden):
```csharp
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Services.Updates;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 8 · K5] Feed kaydından karta: boyut MB tek ondalık; öne çıkanlar CHANGELOG bölümünün
/// kendisidir (mevcut parser), KindOrder sırasıyla en çok 5 madde, kalanı sayı olarak (kart "+N more" yazar).
/// <para><b>Değişen kural:</b> eski <c>Sample</c>/<c>NextMinor</c> (motor yokken hapı hep gösteren placeholder) kalktı;
/// teklif artık yalnız gerçek bir feed kaydından üretilir. Örnek teklif testlerin fixture'ında (<c>UpdateOffers</c>).</para></summary>
public class UpdateOfferTests
{
    private const string Notes = "## [1.8.0] - 2026-10-01\n\n### Added\n\n- A\n- B\n\n### Changed\n\n- C\n\n### Fixed\n\n- D\n- E\n- F\n\n### Performance\n\n- G\n";

    [Theory]
    [InlineData(19_293_798, "18.4 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(512_000, "0.5 MB")]
    public void The_size_is_megabytes_with_one_decimal(long bytes, string expected) => Assert.Equal(expected, UpdateOffer.FormatSize(bytes));

    [Fact]
    public void Highlights_are_the_feed_notes_in_category_order_capped_at_five_with_the_rest_counted()
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 19_293_798, Notes));
        Assert.Equal("1.8.0", offer.Version);
        Assert.Equal("18.4 MB", offer.Size);
        Assert.Equal(UpdateOffer.MaxHighlights, offer.Highlights.Count);
        Assert.Equal(["A", "B", "C", "D", "E"], offer.Highlights.Select(n => n.Text));
        Assert.Equal(2, offer.MoreCount);
    }

    [Fact]
    public void A_short_feed_shows_everything_and_counts_nothing()
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "## [1.8.0] - 2026-10-01\n### Fixed\n- D\n"));
        Assert.Single(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
    }

    [Fact]
    public void Unparsable_notes_yield_an_offer_without_highlights()
    {
        // Feed notu bozuksa (yayın script'i dışından üretilmiş bir paket) kart düşmez: hap yine çıkar, öne çıkanlar boş.
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "not a changelog"));
        Assert.Empty(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
        Assert.Empty(UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "")).Highlights);
    }

    [Fact]
    public void The_more_line_counts_what_the_card_leaves_out()
    {
        Assert.Equal("+3 more in What's new after restart", UpdateText.MoreHighlights(3));
        Assert.Equal("+1 more in What's new after restart", UpdateText.MoreHighlights(1));
    }
}
```

`UpdateCardTests.cs`'e:
```csharp
    [StaFact]
    public void Beyond_five_highlights_the_card_says_how_many_more_wait_in_whats_new()
    {
        // K5: kart en çok 5 madde; kalanı düz metin bir satırla sayar (tıklanmaz — gelen sürümün notları restart'tan sonra What's new'de).
        var (card, vm) = NewCard(); // sınıftaki mevcut kurulum yardımcısı
        vm.AvailableUpdate = UpdateOffers.Sample() with { MoreCount = 3 };
        var more = (TextBlock)card.FindName("PART_More");
        Assert.Equal(Visibility.Visible, more.Visibility);
        Assert.Equal("+3 more in What's new after restart", more.Text);
        vm.AvailableUpdate = UpdateOffers.Sample();
        Assert.Equal(Visibility.Collapsed, more.Visibility);
    }

    [StaFact]
    public void An_offer_without_highlights_collapses_the_highlights_band()
    {
        var (card, vm) = NewCard();
        vm.AvailableUpdate = UpdateOffers.Sample() with { Highlights = [] };
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)card.FindName("PART_HighlightsBlock")).Visibility);
    }

    [StaFact]
    public void The_card_is_named_for_screen_readers_like_its_pill()
    {
        var (card, vm) = NewCard();
        vm.AvailableUpdate = UpdateOffers.Sample("9.9.0");
        Assert.Equal(AccessibilityNames.UpdateTo("9.9.0"), AutomationProperties.GetName(card));
    }
```

Yeniden yazılan pinler (eski iddia + gerekçe doc'a):
- `UpdatePillTests:203` → `Without_an_offer_the_pill_is_hidden_from_the_first_frame`: `MainWindowHost.NewRealized` → `UpdatePillSlot.Visibility == Collapsed`; eski iddia "örnek teklif hapı ilk karede gösterir" (motor yokken tasarım aktarımı); gerekçe: motor geldi, hap yalnız indirilmiş teklif varken (tasarım §2.12 ilkesi).
- `UpdatePillTests:73,135,187` ve kart/ekran kabuk testleri: kurulumdan sonra `vm.AvailableUpdate = UpdateOffers.Sample();` satırı eklenir (yardımcıya toplanır: `MainWindowHost.NewRealizedWithOffer`).
- `UpdateRestartLockTests:243` → `The_app_starts_without_an_offer`: `Assert.Null(vm.AvailableUpdate)`; `NewVm()` teklif verir.
- `AccessibilityTests:475` → `UpdateTo(UpdateOffers.Sample().Version)` ve tarama öncesi teklif atanır.

- [ ] **Step 2: Kırmızıyı gör** — derlenmez / FAIL.

- [ ] **Step 3: Kod**

`UpdateOffer.cs` (tümü):
```csharp
using System.Globalization;
using BuildOrchestrator.App.Services.Updates;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [design v1.23.0 §2.12 · K5] İnip kuruluma hazır bekleyen bir güncelleme: gelen sürüm, paket boyutu, kartın öne
/// çıkanları ve kartın göstermediği madde sayısı. Maddeler What's new'in veri tipidir (<see cref="ReleaseNote"/>); kart
/// onları <see cref="ReleaseNotes.KindOrder"/> sırasıyla çizer. Teklif yalnız gerçek bir feed kaydından üretilir
/// (<see cref="From"/>); feed notu yayın script'inin CHANGELOG'dan kestiği bölümdür ve aynı parser okur.
/// </summary>
public sealed record UpdateOffer(string Version, string Size, IReadOnlyList<ReleaseNote> Highlights, int MoreCount)
{
    /// <summary>Kartın gösterdiği en çok madde (K5) — 8 maddelik bir sürüm kartı ~580px'e çıkarıyordu.</summary>
    public const int MaxHighlights = 5;

    private const double BytesPerMegabyte = 1024 * 1024;

    public static UpdateOffer From(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var (shown, more) = SelectHighlights(ParseNotes(candidate.NotesMarkdown));
        return new(candidate.Version, FormatSize(candidate.DownloadBytes), shown, more);
    }

    /// <summary>"18.4 MB" — Explorer'ın MB'ı (2^20), tek ondalık, İngilizce nokta.</summary>
    internal static string FormatSize(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPerMegabyte:0.0} MB");

    /// <summary>KindOrder sırasıyla ilk <see cref="MaxHighlights"/> madde; kalan sayısı.</summary>
    internal static (IReadOnlyList<ReleaseNote> Shown, int More) SelectHighlights(IReadOnlyList<ReleaseNote> notes)
    {
        var ordered = ReleaseNotes.KindOrder.SelectMany(kind => notes.Where(n => n.Kind == kind)).ToList();
        return (ordered.Take(MaxHighlights).ToList(), Math.Max(0, ordered.Count - MaxHighlights));
    }

    /// <summary>Feed notu bu uygulamanın CHANGELOG bölümü değilse (elle yüklenmiş paket) kart düşmez: boş öne çıkanlar.</summary>
    private static IReadOnlyList<ReleaseNote> ParseNotes(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        try { return ReleaseNotes.Parse(markdown).FirstOrDefault()?.Notes ?? []; }
        catch (FormatException) { return []; }
    }
}
```

`RunViewModel.Update.cs:25` → `private UpdateOffer? _availableUpdate;` ve sınıf/alan yorumları: "Teklifi <c>UpdateService</c> yazar (UI thread'inde); <c>null</c> = hap yok."

`UpdateText.cs`'e:
```csharp
    /// <summary>[K5] Kartın 5 maddeden sonrasını sayan satır — düz metin, tıklanmaz: gelen sürümün tüm notları restart'tan
    /// sonra What's new'dedir.</summary>
    public static string MoreHighlights(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"+{count} more in What's new after restart");
```

`UpdateCard.xaml`: `PART_Highlights` StackPanel'in altına
```xml
        <TextBlock x:Name="PART_More" Margin="0,8,0,0" Visibility="Collapsed" FontSize="{DynamicResource FontSize.Xs}"
                   Foreground="{DynamicResource Brush.TextFaint}" />
```
(`PART_HighlightsBlock` içinde StackPanel'e sarılır.) Kök öğeye `AutomationProperties.Name` kodla verilir.

`UpdateCard.xaml.cs` `RefreshContent`:
```csharp
        var offer = Vm?.AvailableUpdate;
        PART_Incoming.Text = offer?.Version ?? "";
        PART_Size.Text = offer?.Size ?? "";
        AutomationProperties.SetName(this, offer is null ? "" : AccessibilityNames.UpdateTo(offer.Version));
        var highlights = offer?.Highlights ?? [];
        PART_HighlightsBlock.Visibility = highlights.Count == 0 ? Visibility.Collapsed : Visibility.Visible; // boş bant kalmaz
        ReleaseNoteBlocks.Fill(PART_Highlights, highlights, HighlightBlocks);
        int more = offer?.MoreCount ?? 0;
        PART_More.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        PART_More.Text = more > 0 ? UpdateText.MoreHighlights(more) : "";
        PART_Note.Text = Vm?.UpdateRestartBlockedReason ?? UpdateText.RestartNote;
```

- [ ] **Step 4: Yeşil** — ilgili filtreler + `AccessibilityTests`, `AntiSlop`, `AppIdentityTests` (ürün adı literali yok) PASS.

- [ ] **Step 5: Commit** — `feat(update): UpdateOffer.From - boyut, KindOrder'da en cok 5 madde + '+N more' satiri; Sample placeholder kalkti, hap teklif yokken gizli; kart UIA adi ve bos bant`

---

### Task 9: `UpdateService` — zamanlama ve durum makinesi

**Files:**
- Create: `src/BuildOrchestrator.App/Services/Updates/UpdateService.cs`
- Modify: `tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj` (`<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />`)
- Test: `tests/BuildOrchestrator.Tests/App/UpdateServiceTests.cs`

**Interfaces (Produces):**
```csharp
public sealed class UpdateService : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);   // K8
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);        // K8
    public UpdateService(IAppUpdater updater, TimeProvider time, Action<UpdateOffer> publish);
    public UpdateOffer? Ready { get; }
    public bool RestartRequested { get; }
    public void Start();                 // IsInstalled false → hiçbir şey; PendingRestart → hemen publish; zamanlayıcı kurulur
    public void RequestRestart();        // Restart to update: OnExit'te restart:true
    public void ApplyOnExit();           // Ready varsa updater.ApplyOnExit(candidate, RestartRequested)
    internal Task RunCycleAsync();       // test yüzeyi: bir kontrol turu (zamanlayıcı da bunu çağırır)
}
```
`publish` çağıranın thread'inde koşar → App tarafı `Dispatcher.Invoke` ile sarar (Task 12).

- [ ] **Step 1: Kırmızı testler**

```csharp
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Services.Updates;
using Microsoft.Extensions.Time.Testing;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 9] Kontrol sessiz (açılış + 5 s, sonra 4 saat), indirme arka planda, teklif yalnız indirme
/// bitince (tasarım §2.12 "yalnız hazırken"); hata sessiz ve bir sonraki turda yeniden; kurulu olmayan kopya hiç
/// kontrol etmez; aynı sürüm ikinci kez yayımlanmaz; eşit/eski sürüm yok sayılır. Zaman sahtedir (D8).</summary>
public class UpdateServiceTests
{
    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsInstalled { get; set; } = true;
        public UpdateCandidate? PendingRestart { get; set; }
        public Func<UpdateCandidate?> OnCheck = () => null;
        public Func<UpdateCandidate, Task> OnDownload = _ => Task.CompletedTask;
        public int Checks, Downloads;
        public (UpdateCandidate Candidate, bool Restart)? Applied;
        public Task<UpdateCandidate?> CheckAsync(CancellationToken ct) { Checks++; return Task.FromResult(OnCheck()); }
        public Task DownloadAsync(UpdateCandidate c, CancellationToken ct) { Downloads++; return OnDownload(c); }
        public void ApplyOnExit(UpdateCandidate c, bool restart) => Applied = (c, restart);
    }

    private static readonly UpdateCandidate Newer = new("99.0.0", 19_293_798, "## [99.0.0] - 2026-10-01\n### Fixed\n- D\n");

    private static (UpdateService Service, FakeUpdater Updater, FakeTimeProvider Time, List<UpdateOffer> Published) Rig()
    {
        var updater = new FakeUpdater();
        var time = new FakeTimeProvider();
        var published = new List<UpdateOffer>();
        return (new UpdateService(updater, time, published.Add), updater, time, published);
    }

    [Fact]
    public void Start_does_nothing_when_the_copy_is_not_installed()
    {
        var (service, updater, time, published) = Rig();
        updater.IsInstalled = false;
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.CheckInterval * 3);
        Assert.Equal(0, updater.Checks);
        Assert.Empty(published);
    }

    [Fact]
    public async Task The_first_check_waits_five_seconds_and_a_ready_offer_is_published_after_the_download()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.FirstCheckDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, updater.Checks);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await service.RunningCycle; // test yüzeyi: uçuştaki turun tamamlanması
        Assert.Equal(1, updater.Checks);
        Assert.Equal(1, updater.Downloads);
        var offer = Assert.Single(published);
        Assert.Equal("99.0.0", offer.Version);
        Assert.Equal("18.4 MB", offer.Size);
        Assert.Same(offer, service.Ready);
    }

    [Fact]
    public async Task Checks_repeat_every_four_hours_and_the_same_ready_version_is_not_published_twice()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.FirstCheckDelay); await service.RunningCycle;
        time.Advance(UpdateService.CheckInterval); await service.RunningCycle;
        time.Advance(UpdateService.CheckInterval); await service.RunningCycle;
        Assert.Equal(3, updater.Checks);
        Assert.Equal(1, updater.Downloads);
        Assert.Single(published);
    }

    [Theory]
    [InlineData("1.7.0")]   // eşit (AppIdentity.Version testte 1.7.0 değilse InlineData yerine AppIdentity.Version kullan)
    [InlineData("0.9.0")]
    public async Task An_equal_or_older_version_is_ignored(string version)
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer with { Version = version == "1.7.0" ? AppIdentity.Version : version };
        await service.RunCycleAsync();
        Assert.Equal(0, updater.Downloads);
        Assert.Empty(published);
    }

    [Fact]
    public async Task A_failing_check_or_download_is_silent_and_retried_next_time()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => throw new HttpRequestException("offline");
        await service.RunCycleAsync();
        Assert.Empty(published);
        updater.OnCheck = () => Newer;
        updater.OnDownload = _ => throw new IOException("checksum");
        await service.RunCycleAsync();
        Assert.Empty(published);
        updater.OnDownload = _ => Task.CompletedTask;
        await service.RunCycleAsync();
        Assert.Single(published);
    }

    [Fact]
    public void A_download_left_from_an_earlier_session_is_offered_at_once()
    {
        var (service, updater, time, published) = Rig();
        updater.PendingRestart = Newer;
        service.Start();
        Assert.Single(published);
        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task Apply_on_exit_installs_the_ready_update_and_restarts_only_when_asked()
    {
        var (service, updater, time, published) = Rig();
        service.ApplyOnExit();                       // hazır bir şey yok → hiçbir şey
        Assert.Null(updater.Applied);
        updater.OnCheck = () => Newer;
        await service.RunCycleAsync();
        service.ApplyOnExit();                       // Later + normal çıkış → sessiz kurulum, yeniden açılmaz
        Assert.Equal((Newer, false), updater.Applied);
        service.RequestRestart();
        service.ApplyOnExit();                       // Restart to update → yeniden açılır
        Assert.Equal((Newer, true), updater.Applied);
    }

    [Fact]
    public async Task A_cycle_that_is_still_running_is_not_started_twice()
    {
        var (service, updater, time, published) = Rig();
        var gate = new TaskCompletionSource();
        updater.OnCheck = () => Newer;
        updater.OnDownload = _ => gate.Task;
        var first = service.RunCycleAsync();
        var second = service.RunCycleAsync();       // uçuşta tur var → hemen döner
        Assert.True(second.IsCompleted);
        Assert.Equal(1, updater.Checks);
        gate.SetResult();
        await first;
        Assert.Single(published);
    }
}
```
(`RunningCycle`: `internal Task RunningCycle` — uçuştaki turun Task'ı, yoksa `Task.CompletedTask`.)

- [ ] **Step 2: Kırmızıyı gör** — derlenmez.

- [ ] **Step 3: Kod**

```csharp
namespace BuildOrchestrator.App.Services.Updates;

/// <summary>
/// [design v1.23.0 §2.12 · K8] Güncelleme motorunun durum makinesi: kurulu kopyada açılıştan <see cref="FirstCheckDelay"/>
/// sonra ve her <see cref="CheckInterval"/>'de sessiz kontrol; yeni sürüm arka planda iner; indirme bitince teklif
/// <paramref name="publish"/> ile yayımlanır (hap o anda belirir). Hata sessizdir — bir sonraki turda yeniden. Kurulu
/// olmayan kopya (bin'den dev build, publish klasörü) hiç kontrol etmez. Önceki oturumdan indirilmiş paket
/// (<see cref="IAppUpdater.PendingRestart"/>) açılışta hemen teklif edilir.
/// <para><b>Çıkış:</b> hazır teklif varsa <see cref="ApplyOnExit"/> Update.exe'yi başlatır — <c>Restart to update</c>
/// istendiyse yeniden açılır, aksi hâlde (Later + normal çıkış) sessizce kurulur ve bir sonraki açılış yeni sürümdür
/// (tasarım: "If you wait, it installs on the next start").</para>
/// <para>Zaman <see cref="TimeProvider"/> üzerinden (D8: testte duvar saati beklenmez); <paramref name="publish"/>
/// çağıranın thread'inde koşar, UI'a marshal çağıranın işidir.</para>
/// </summary>
public sealed class UpdateService(IAppUpdater updater, TimeProvider time, Action<UpdateOffer> publish) : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    private ITimer? _timer;
    private UpdateCandidate? _ready;
    private Task _cycle = Task.CompletedTask;
    private int _cycleRunning;

    public UpdateOffer? Ready { get; private set; }
    public bool RestartRequested { get; private set; }
    internal Task RunningCycle => _cycle;

    public void Start()
    {
        if (!updater.IsInstalled) return;
        if (updater.PendingRestart is { } pending) Publish(pending);
        _timer = time.CreateTimer(_ => _ = RunCycleAsync(), null, FirstCheckDelay, CheckInterval);
    }

    internal Task RunCycleAsync()
    {
        if (Interlocked.CompareExchange(ref _cycleRunning, 1, 0) != 0) return Task.CompletedTask;
        _cycle = CycleAsync();
        return _cycle;
    }

    private async Task CycleAsync()
    {
        try
        {
            var candidate = await updater.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            if (candidate is null || !IsNewer(candidate.Version) || candidate.Version == _ready?.Version) return;
            await updater.DownloadAsync(candidate, CancellationToken.None).ConfigureAwait(false);
            Publish(candidate);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* sessiz: çevrimdışı, limit, hash — sonraki tur */ }
        finally { Volatile.Write(ref _cycleRunning, 0); }
    }

    private void Publish(UpdateCandidate candidate)
    {
        _ready = candidate;
        Ready = UpdateOffer.From(candidate);
        publish(Ready);
    }

    /// <summary>Velopack yalnız daha yenisini döndürür; yine de eşit/eski sürüm burada da elenir (feed hatası hapı açmasın).</summary>
    private static bool IsNewer(string version) =>
        Version.TryParse(version, out var incoming) && Version.TryParse(AppIdentity.Version, out var installed) && incoming > installed;

    public void RequestRestart() => RestartRequested = true;

    public void ApplyOnExit()
    {
        if (_ready is { } ready) updater.ApplyOnExit(ready, RestartRequested);
    }

    public void Dispose() => _timer?.Dispose();
}
```

- [ ] **Step 4: Yeşil** → PASS. **Step 5: Commit** — `feat(update): UpdateService - 5 s + 4 saat sessiz kontrol, arka planda indirme, hazir teklif yayimi, cikista kurulum (restart/sessiz)`

---

### Task 10: `VelopackUpdater` — `UpdateManager` sarmalayıcısı

**Files:**
- Create: `src/BuildOrchestrator.App/Services/Updates/VelopackUpdater.cs`
- Test: `tests/BuildOrchestrator.Tests/App/VelopackUpdaterTests.cs`

- [ ] **Step 1: Kırmızı test**

```csharp
using BuildOrchestrator.App.Services.Updates;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 10] Gerçek Velopack, ağ yok: test host'u Velopack ile KURULMAMIŞ bir kopyadır — sarmalayıcı bunu
/// doğru bildirmeli (UpdateService kapısı buna dayanır) ve bekleyen paket olmamalı. Kurulu kopyadaki davranış yerel feed
/// provasıyla elle doğrulanır (ARCHITECTURE §17.6).</summary>
public class VelopackUpdaterTests
{
    [Fact]
    public void A_copy_that_was_not_installed_by_Velopack_reports_itself_as_not_installed()
    {
        var updater = new VelopackUpdater(UpdateFeed.CreateSource(null, prerelease: false));
        Assert.False(updater.IsInstalled);
        Assert.Null(updater.PendingRestart);
    }

    [Fact]
    public void The_download_size_is_the_deltas_when_they_exist_and_the_full_package_otherwise()
    {
        Assert.Equal(300, VelopackUpdater.DownloadBytes(full: 1000, deltas: [100, 200]));
        Assert.Equal(1000, VelopackUpdater.DownloadBytes(full: 1000, deltas: []));
    }
}
```

- [ ] **Step 2: Kırmızıyı gör** — derlenmez.

- [ ] **Step 3: Kod**

```csharp
using Velopack;
using Velopack.Sources;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] <see cref="IAppUpdater"/>'ın Velopack uygulaması. <see cref="UpdateManager"/> feed'i okur, paketi
/// boyut + SHA ile doğrulayıp indirir (<c>ChecksumFailedException</c> → çağıran sessizce geçer) ve çıkışta
/// <c>Update.exe</c>'yi başlatır: <see cref="UpdateManager.WaitExitThenApplyUpdates"/> process'in düzgün çıkışını bekler
/// (60 s), <c>silent</c> — Velopack'in kendi penceresi gösterilmez (K6). <c>ApplyUpdatesAndRestart</c> KULLANILMAZ:
/// anında çıkar, <c>App.OnExit</c>'i (motorun düzgün kapanışını) atlar.</summary>
public sealed class VelopackUpdater(IUpdateSource source) : IAppUpdater
{
    private readonly UpdateManager _manager = new(source);
    private UpdateInfo? _lastInfo; // DownloadAsync'in indireceği paket (CheckAsync'in bulduğu)

    public bool IsInstalled => _manager.IsInstalled;

    public UpdateCandidate? PendingRestart =>
        _manager.IsInstalled && _manager.UpdatePendingRestart is { } asset ? ToCandidate(asset, asset.Size) : null;

    public async Task<UpdateCandidate?> CheckAsync(CancellationToken ct)
    {
        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _lastInfo = info;
        if (info is null) return null;
        long bytes = DownloadBytes(info.TargetFullRelease.Size, info.DeltasToTarget.Select(d => d.Size).ToArray());
        return ToCandidate(info.TargetFullRelease, bytes);
    }

    public Task DownloadAsync(UpdateCandidate candidate, CancellationToken ct)
    {
        var info = _lastInfo ?? throw new InvalidOperationException("Download requested before a check found an update.");
        return _manager.DownloadUpdatesAsync(info, progress: null, ct);
    }

    public void ApplyOnExit(UpdateCandidate candidate, bool restart)
    {
        var asset = _manager.UpdatePendingRestart ?? _lastInfo?.TargetFullRelease;
        _manager.WaitExitThenApplyUpdates(asset, silent: true, restart: restart);
    }

    /// <summary>İndirilecek olan: delta zinciri varsa toplamı, yoksa tam paket.</summary>
    internal static long DownloadBytes(long full, IReadOnlyList<long> deltas) => deltas.Count > 0 ? deltas.Sum() : full;

    private static UpdateCandidate ToCandidate(VelopackAsset asset, long bytes) =>
        new(asset.Version.ToString(), bytes, asset.NotesMarkdown ?? "");
}
```
(`asset.Version.ToString()` "1.8.0" biçiminde değilse `ToNormalizedString()`/`ToFullString()` — ilk derlemede kontrol.)

- [ ] **Step 4: Yeşil** → PASS. **Step 5: Commit** — `feat(update): VelopackUpdater - UpdateManager sarmalayicisi (kontrol, indirme, cikista silent apply)`

---

### Task 11: Restart akışı — tek adım "Closing…", gerçek çıkış, Supervisor'ın çıkışı beklenir

**Files:**
- Modify: `ViewModels/UpdateRestartTimeline.cs`, `ViewModels/UpdateText.cs` (`RestartStepLabel`), `Views/UpdateRestartScreen.xaml.cs` (`Leave`/`Close` kalkar; çubuk dolunca zamanlayıcı durur), `MainWindow.UpdateRestart.cs`, `Services/EngineHost.cs` (`KillCurrent`)
- Test: `UpdateRestartTimelineTests.cs`, `UpdateRestartScreenTests.cs`, `ReducedMotionCoverageTests.cs:318-331`, yeni `UpdateRestartFlowTests.cs`, `EngineHostTests.cs` (ek)

**Değişen kural (doc'lara yazılır):** eski iddia — ekran üç adımı (Closing 800 → Installing 1100 → Starting 800) oynatır, 120 ms sonra söner, uygulama aynen kalır (motor yokken tasarım önizlemesi). Yeni kural (K6, kullanıcı kararı 2026-09-30) — Windows çalışan programın dosyalarını değiştirmeye izin vermediği için kurulum ancak uygulama kapandıktan sonra Update.exe tarafından yapılır: ekran yalnız `Closing <ürün>…` adımını gösterir (çubuk 800 ms'de dolar), uygulama güvenli tam çıkış yoluna girer ve pencere kapanır; kurulum penceresiz sürer; yeni sürüm normal açılır. Ekran kendi kendine sönmez.

- [ ] **Step 1: Kırmızı testler**

`UpdateRestartTimelineTests` (yeniden):
```csharp
    [Fact]
    public void The_only_step_is_closing_and_the_bar_fills_over_800ms()
    {
        var stage = Assert.Single(UpdateRestartTimeline.Stages);
        Assert.Equal(UpdateRestartStep.Closing, stage.Step);
        Assert.Equal(800, stage.DurationMs);
        Assert.Equal(100, stage.EndPercent);
        Assert.Equal(800, UpdateRestartTimeline.TotalMs);
        Assert.Equal(new UpdateRestartFrame(UpdateRestartStep.Closing, 50), UpdateRestartTimeline.At(400));
        Assert.Equal(100, UpdateRestartTimeline.At(5000).Percent); // çıkış gecikirse çubuk dolu kalır
    }

    [Fact]
    public void The_step_label_names_the_product()
        => Assert.Equal("Closing " + AppIdentity.Product + "…", UpdateText.RestartStepLabel(UpdateRestartStep.Closing, "9.9.0"));
```

`UpdateRestartScreenTests` — yeniden yazılanlar: `The_step_label_and_the_bar_follow_the_timeline_frame_by_frame` (tek adım, 0→100), `The_screen_stays_until_the_process_exits_and_stops_its_timer` (`FrameAt(TotalMs + 5000)` → `IsShowing` true, `Timer.IsRunning` false), `Each_step_is_announced_once_to_a_screen_reader` (1 duyuru), `With_motion_on_it_fades_in_over_the_base_duration` (sönüş iddiası kalkar), `When_the_screen_leaves_the_app_is_exactly_as_it_was` **silinir yerine** aşağıdaki akış testi gelir (eski iddia doc'a). `ReducedMotionCoverageTests:318` → yalnız giriş anında; sönüş satırı (`:331`) kalkar.

`UpdateRestartFlowTests.cs` (yeni):
```csharp
/// <summary>[motor · Task 11 · K6] Restart to update = kart kapanır, ekran "Closing…" der, uygulama güvenli tam çıkış yoluna
/// girer (mevcut RequestFullExit: iş yoksa hemen ExitReady → Shutdown). Kurulumu çıkışta Update.exe yapar (Task 12).
/// <para>Eski iddia (UpdateRestartScreenTests.When_the_screen_leaves_the_app_is_exactly_as_it_was): ekran oynayıp
/// sönerdi, uygulama aynen kalırdı — motor yokken tasarım önizlemesi. Değişti: gerçek kurulum için pencere kapanmalı.</para></summary>
public class UpdateRestartFlowTests
{
    [StaFact]
    public void Restart_to_update_shows_the_closing_screen_and_takes_the_safe_exit_path()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;
        vm.AvailableUpdate = UpdateOffers.Sample("9.9.0");

        vm.RestartToUpdateCommand.Execute(null);

        Assert.True(window.UpdateRestartOverlay.IsShowing);
        Assert.Equal("Closing " + AppIdentity.Product + "…", ((TextBlock)window.UpdateRestartOverlay.FindName("PART_Step")).Text);
        Assert.Equal(1, shutdowns);            // iş yok → çıkış hemen
        Assert.False(vm.ExitPending);
    }

    [StaFact]
    public void While_a_build_runs_the_restart_is_locked_and_nothing_closes()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;
        vm.AvailableUpdate = UpdateOffers.Sample();
        MainWindowHost.StartBuild(vm);
        vm.RestartToUpdateCommand.Execute(null);
        Assert.False(window.UpdateRestartOverlay.IsShowing);
        Assert.Equal(0, shutdowns);
    }
}
```

`EngineHostTests`'e (gerçek Supervisor, `--logs <sandbox>` deseni dosyadaki mevcut testlerden):
```csharp
    /// <summary>[motor · Task 11] Güncelleyici (Update.exe) App çıkar çıkmaz current\ klasörünü değiştirir; Supervisor
    /// hâlâ current\supervisor\*.dll tutuyorsa kurulum yarım kalır. Dispose, öldürdüğü process'in GERÇEKTEN çıkmasını
    /// bekler (≤1 s) — daha önce Kill'den sonra beklenmiyordu.</summary>
    [SkippableFact]
    public async Task Dispose_waits_for_the_supervisor_process_to_exit()
    {
        using var sandbox = new SupervisorSandbox();
        var engine = new EngineHost(TestPaths.SupervisorExe, supervisorArgs: sandbox.Args);
        await engine.StartAsync();
        int pid = engine.EnginePid!.Value;
        await engine.DisposeAsync();
        Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid)); // çıkmış: bekleme yok
    }
```

- [ ] **Step 2: Kırmızıyı gör** — timeline/screen/flow FAIL; EngineHost testi zaman zaman geçer (yarış) → `Kill` sonrası `Assert` hemen: `GetProcessById` bazen hâlâ bulur; kırmızıyı görmek için testi 20 kez koştur (`--filter` + `-- xunit.repeat`? yoksa döngü) ya da geçici olarak beklemeyi 0 yap.

- [ ] **Step 3: Kod**

`UpdateRestartTimeline.cs`: `UpdateRestartStep { Closing }` (tek üye); sabitler `ClosingMs = 800`, `ClosingEndPercent = 100`; `Stages = [new(Closing, ClosingMs, ClosingEndPercent)]`; `FadeOutDelayMs`/`FadeOutAtMs` silinir; sınıf yorumu K6 gerekçesiyle yeniden.

`UpdateText.RestartStepLabel`: `=> "Closing " + AppIdentity.Product + "…"` (switch kalkar; `incoming` parametresi kalkar → çağıranlar düzelir).

`UpdateRestartScreen.xaml.cs`: `OnFrame` → `Render(elapsed); if (elapsedMs >= UpdateRestartTimeline.TotalMs) Timer.Stop();`; `Leave`, `Close`, `FadeOutDuration`, `_generation` kalkar; sınıf yorumu: "Ekran pencere kapanana dek kalır; sönüş yoktur — pencereyi Update.exe kapatır."

`MainWindow.UpdateRestart.cs`:
```csharp
    private void OnRestartToUpdateRequested()
    {
        CloseUpdateCard(returnFocusToPill: true);
        if (_vm.AvailableUpdate is not { } offer) return;
        UpdateRestartOverlay.Play(AppIdentity.Version, offer.Version); // yalnız "Closing…" — kurulum pencere kapanınca (App.OnExit)
        RequestFullExit();                                             // × ile aynı güvenli tam çıkış yolu
    }
```

`EngineHost.KillCurrent`:
```csharp
    /// <summary>Öldürülen process'in çıkışına tanınan süre — güncelleyici current\ klasörünü değiştirmeden önce
    /// supervisor\*.dll kilitleri bırakılmış olsun (Update.exe App'in çıkışını bekler, Supervisor'ın değil).</summary>
    internal static readonly TimeSpan KillExitWait = TimeSpan.FromSeconds(1);

    private void KillCurrent()
    {
        var child = Interlocked.Exchange(ref _child, null);
        if (child is null) return;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(child.Pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(KillExitWait);
        }
        catch (ArgumentException) { /* zaten öldü */ }
        child.Dispose();
        _writer = null;
    }
```

- [ ] **Step 4: Yeşil** — ilgili filtreler PASS; `SafeExitProcessTests`, `AppShutdownTests` (2 s bütçe) hâlâ PASS.

- [ ] **Step 5: Commit** — `feat(update): Restart to update gercek cikisa gider - ekran tek adim 'Closing…', sonus yok; EngineHost oldurdugu Supervisor'in cikisini bekler`

---

### Task 12: App kablajı — DI, `Start()`, Restart isteği, `OnExit`'te kurulum

**Files:**
- Modify: `src/BuildOrchestrator.App/App.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/StartupPathTests.cs` (kaynak guard eki)

- [ ] **Step 1: Kırmızı test** (`StartupPathTests`'e)

```csharp
    /// <summary>[motor · Task 12] Kablo kaynak üzerinden pinlenir (App headless kurulamaz): motor DI'da, kontrol pencere
    /// gösterildikten SONRA başlar (açılış koreografisiyle yarışmaz), Restart isteği servise iner, OnExit motor
    /// kapandıktan sonra kurulumu başlatır.</summary>
    [Fact]
    public void The_update_engine_is_wired_after_the_window_shows_and_installs_on_exit()
    {
        string startup = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Contains("new VelopackUpdater(UpdateFeed.CreateSource(", startup, StringComparison.Ordinal);
        Assert.Contains("UpdateFeed.SourceOverrideVariable", startup, StringComparison.Ordinal);
        Assert.Contains("UpdateFeed.PrereleaseVariable", startup, StringComparison.Ordinal);
        int show = startup.IndexOf("else window.Show();", StringComparison.Ordinal);
        int start = startup.IndexOf("GetRequiredService<UpdateService>().Start()", StringComparison.Ordinal);
        Assert.True(show > 0 && start > show, "UpdateService.Start() pencere gösterildikten sonra çağrılmalı.");
        Assert.Contains("RestartToUpdateRequested += (_, _) =>", startup, StringComparison.Ordinal);
        Assert.Contains(".RequestRestart()", startup, StringComparison.Ordinal);
        int dispose = startup.IndexOf("AppShutdown.WaitForAsyncDisposal(", StringComparison.Ordinal);
        int apply = startup.IndexOf("GetService<UpdateService>()?.ApplyOnExit()", StringComparison.Ordinal);
        Assert.True(dispose > 0 && apply > dispose, "ApplyOnExit motor kapandıktan sonra çağrılmalı.");
    }
```

- [ ] **Step 2: Kırmızıyı gör** → FAIL.

- [ ] **Step 3: Kod** (`App.xaml.cs`)

DI bloğuna (`AutostartService` satırından sonra):
```csharp
        // [motor] Güncelleme: feed GitHub Releases (BO_UPDATE_SOURCE/BO_UPDATE_PRERELEASE dev/test kapıları), teklif VM'e
        // UI thread'inde yazılır (kabuk PropertyChanged'da WPF öğelerine dokunur). Kurulu olmayan kopyada servis boşta kalır.
        sc.AddSingleton<IAppUpdater>(_ => new VelopackUpdater(UpdateFeed.CreateSource(
            Environment.GetEnvironmentVariable(UpdateFeed.SourceOverrideVariable),
            UpdateFeed.IsEnabled(Environment.GetEnvironmentVariable(UpdateFeed.PrereleaseVariable)))));
        sc.AddSingleton(sp => new UpdateService(sp.GetRequiredService<IAppUpdater>(), TimeProvider.System,
            offer => Dispatcher.Invoke(() => sp.GetRequiredService<RunViewModel>().AvailableUpdate = offer)));
```
`window.Show()` satırından sonra:
```csharp
        // [motor] Kontrol pencere gösterildikten 5 s sonra başlar (UpdateService.FirstCheckDelay); Restart isteği kuruluma
        // "yeniden aç" der — kurulumun kendisi OnExit'te.
        var updates = Services.GetRequiredService<UpdateService>();
        Services.GetRequiredService<RunViewModel>().RestartToUpdateRequested += (_, _) => updates.RequestRestart();
        updates.Start();
```
`OnExit`:
```csharp
        AppShutdown.WaitForAsyncDisposal(Services?.GetService<EngineHost>(), AppShutdown.DisposalTimeout);
        // [motor] Motor kapandı, dosya kilitleri bırakıldı → hazır güncelleme varsa Update.exe (job dışında) kurulumu
        // process çıkınca yapar: Restart istendiyse yeniden açar, Later denmişse sessiz kurar.
        Services?.GetService<UpdateService>()?.ApplyOnExit();
```
`using BuildOrchestrator.App.Services.Updates;` eklenir.

- [ ] **Step 4: Yeşil** — guard PASS; `dotnet run` ile aç/kapa: hap görünmez (kurulu değil), çıkış normal.

- [ ] **Step 5: Commit** — `feat(app): guncelleme motoru kablaji - DI, pencere sonrasi Start, Restart istegi, OnExit'te kurulum`

---

### Task 13: Dokümanlar — README, ARCHITECTURE, CLAUDE.md (aynı işte)

**Files:** `README.md`, `ARCHITECTURE.md`, `CLAUDE.md`

- [ ] **README.md**
  - `## Requirements` ikiye: **To use it** — Windows, .NET 10 Desktop Runtime (Setup kurar), VS 2022/Build Tools (MSBuild), `git`; **To build it** — .NET 10 SDK (`global.json` bandı).
  - Yeni `## Install` (Requirements'tan sonra): sabit link `…/releases/latest/download/BuildOrchestrator.App-win-Setup.exe`; per-user, admin yok; SmartScreen "More info → Run anyway" (imzasız); kurulum yeri `%LocalAppData%\BuildOrchestrator.App\`; eski kopyadan geçiş (önce kapat, sonra kur, eski klasörü sil; ayarlar `%LocalAppData%\BuildOrchestrator\` altında kalır); kaldırma Uygulamalar listesinden, veriler kalır.
  - `### Update` (560-575) yeniden: motor var — kontrol açılış + 5 s ve 4 saatte bir, sessiz; iner; hap; kart 5 madde + "+N more"; Restart → `Closing…` → pencere kapanır → kurulum → yeni sürüm açılır, What's new noktası; Later → çıkışta sessiz kurulum / sonraki açılış; kurulu olmayan kopyada (bin'den çalıştırılan) hap yoktur; `BO_UPDATE_SOURCE` (klasör/URL) ve `BO_UPDATE_PRERELEASE=1` dev kapıları.
  - `## Publish` → `## Package and release`: publish komutu artık `scripts/package.ps1` (satırları oradan; komut README'de yeniden yazılmaz), `-PublishOnly`, tam paket; yayın: `/release` → `scripts/release.ps1` → `release.yml`; çıktı `artifacts/velopack/`; `verify-publish.ps1` aynen.
  - `## Licence`: MIT (`LICENSE`); Geist OFL paragrafı kalır.
  - Rozet: `![ci](https://github.com/sdemir60/app_build_orchestrator/actions/workflows/ci.yml/badge.svg)` başlığın altına.
- [ ] **ARCHITECTURE.md**
  - §1.3 non-goals: "MSIX packaging" kalır; "code signing (v1)" eklenir.
  - §4.2: App'in başlattığı job-dışı process listesine `Update.exe` eklenir (kullanıcının açık kararıyla başlar, App kapanınca yaşar).
  - §12.1: giriş noktası paragrafı — `Program.Main`, `VelopackApp.Build().Run()` ilk ifade, `--veloapp-*` kancaları, auto-apply on startup; composition root'a `IAppUpdater`/`UpdateService`; "kontrol pencere gösterildikten sonra başlar".
  - §12.3 autostart: Velopack'te `current\` yolu sabit; kaldırma kancası Run değerini siler.
  - §12.4 (2396-2402): "There is no update engine yet…" cümlesi → hap yalnız indirilmiş teklif varken; giriş teklif gelince oynar.
  - §13.3 (3202-3242): kart — "content is the sample offer" → feed kaydının CHANGELOG bölümü, en çok 5 madde + "+N more"; boş öne çıkan bandı gizli; kartın UIA adı. Restart ekranı paragrafı K6'ya göre yeniden (tek adım, sönüş yok, güvenli çıkış yolu, kurulum Update.exe'de, yeni sürümde What's new noktası).
  - §14.5 (4766-4778): "never plays yet" → teklif gelince oynar.
  - §16: kurulum dizini `%LocalAppData%\BuildOrchestrator.App\` ≠ state dizini; kaldırma state'e dokunmaz; Velopack'in kendi `packages\`/`Update.exe`.
  - §17.1/17.2: `RepoHygieneTests`, `EntryPointTests`, `ReleaseScriptsTests` guard tablosuna; §17.5: `LocalOnly` kategorisi (CI'da dışlanır, lokal kapı) ve yeni **§17.6 "Update rehearsal"** — yerel feed provası adımları (`package.ps1` → Setup → `package.ps1` yeni sürümle → `BO_UPDATE_SOURCE=<klasör>` → hap → Restart → sürüm + What's new noktası + autostart değeri).
  - §18 → "Build, run, package, release": publish komutu kaldırılır, `scripts/package.ps1` anlatılır; Velopack parametreleri (packId, framework, shortcuts, noPortable, delta), çıktı yerleşimi; `release.ps1` + `release-guard.ps1` + `release.yml`/`ci.yml` akışı (workflow_call), runner pini; ilk yayında delta yok.
  - Yeni **§18.x "Distribution and updates"** (ya da §12.5): motorun durum makinesi (Off/Idle/Checking/Downloading/Ready), 5 s + 4 saat, sessizlik, `PendingRestart`, çıkışta kurulum (restart/sessiz), auto-apply on startup, `BO_UPDATE_*` kapıları, sürüm karşılaştırması.
  - §21: "The only network touch is `git fetch`" → iki dokunuş: `git fetch` ve GitHub Releases HTTPS (api.github.com + asset indirme; kimliksiz, 60/saat/IP; 4 saatlik kontrol); bütünlük = boyut + SHA (Velopack) + TLS + GitHub hesabı (2FA); kod imzası yok (v1); Update.exe yerel.
  - §22 kod haritası: `Program.cs`, `Services/Updates/*`, `UpdateOffer` satırı güncellenir, `scripts/*.ps1`, `.github/workflows/*`, `.claude/skills/release`.
- [ ] **CLAUDE.md**: Task 6'da yapıldı; ek: değişmezler listesine "Velopack yalnız App'te; Core/Supervisor'a girmez" ve "Motor yalnız kurulu kopyada çalışır; hap yalnız indirilmiş teklif varken".
- [ ] **Commit** — `docs: kurulum, guncelleme ve yayin hatti - README (Install/Update/Package and release/Licence), ARCHITECTURE (12.1, 12.4, 13.3, 16, 17, 18, 21, 22), CLAUDE.md degismezleri`

---

### Task 14: Kapanış — tam süit, merge, push, ilk CI, yerel prova

- [ ] `dotnet build BuildOrchestrator.slnx -c Release` 0 uyarı.
- [ ] Tam süit çıktı dosyaya: `dotnet test … -c Release --no-build --filter "Category!=Acceptance" > .claude/temp/full-suite.txt 2>&1`; yeşil (bilinen ortam düşüşü `StickyLayerHeaderClickTests` oturum kilitliyse — tek başına yeniden).
- [ ] `powershell -File scripts\package.ps1 -WhatIf:$false` ile gerçek `vpk pack` denemesi → `artifacts\velopack\BuildOrchestrator.App-win-Setup.exe` boyutunu not et (analiz §2.2 tahmini ~10 MB).
- [ ] Sonuç raporu `.claude/outputs/2026-09-30-04-06-update-pipeline-results.md` (yapılanlar, kararlar, süit sayıları, paket boyutu, açık kalanlar).
- [ ] `main`'e `--no-ff` merge (ana proje `main`'de ve temizse orada; değilse `main-ai`'de — memory), push; branch'i local+remote sil; worktree `main-ai`'ye döner ve ff güncellenir.
- [ ] İlk `ci.yml` koşusunu API'den izle (`curl -s https://api.github.com/repos/sdemir60/app_build_orchestrator/actions/runs?per_page=1`); düşen test varsa triage: ortam kaynaklıysa `[Trait("Category","LocalOnly")]` + doc'una gerekçe; kod kaynaklıysa düzelt. Eşik gevşetme YOK.
- [ ] Kullanıcıya: yerel prova ve v1.8.0 için `/release` hazır; temiz makinede Setup denemesi onun adımı.

---

## Self-review

- **Spec kapsamı:** §5.2 guard zinciri → Task 1/2/4/5; §5.3 notlar → Task 3/8; §5.4 paketleme → Task 3; §5.5 CI → Task 5; §5.6 tek komut → Task 4/6; §5.7 motor → Task 7–12; §5.8 tasarım uyarlamaları K5/K6 → Task 8/11; §5.9 güvenlik → Task 13 (§21); §5.10 imza → doküman notu (P4, kod yok); §5.11 kullanıcı deneyimi → Task 13 README; §5.12 hata/geri alma → Task 9 sessiz tekrar + Task 13. Boşluk: `Velopack.log` / tanı logu yok (bilinçli, analiz §5.7).
- **Tip tutarlılığı:** `UpdateCandidate(Version, DownloadBytes, NotesMarkdown)` Task 7/8/9/10 aynı; `UpdateOffer(Version, Size, Highlights, MoreCount)` Task 8/9/kart; `UpdateService.Start/RequestRestart/ApplyOnExit/Ready/RunCycleAsync/RunningCycle` Task 9/12; `UpdateFeed.CreateSource(string?, bool)`/`IsEnabled` Task 7/12; `AutostartService.RemoveForUninstall(IAutostartRegistry)` Task 2; `UpdateRestartStep.Closing` tek üye Task 11; `UpdateText.RestartStepLabel(step)` — Task 11'de `incoming` parametresi kalkar, `UpdateRestartScreen.Render` çağrısı düzelir.
- **Yer tutucu taraması:** kod blokları eksiksiz; "ilspy ile bak / ilk derlemede kontrol" iki notu (GithubSource üyeleri, SemanticVersion.ToString) uygulayıcının doğrulayacağı gerçek belirsizliklerdir, yer tutucu değil.
- **Review Focus** 1–5 → Task 11, 9, 9, 8, 3 testleri mevcut.
