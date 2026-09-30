using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 3-4] Yayın script'leri Windows PowerShell 5.1 ile koşturulur (verify-publish.ps1 ile aynı
/// yürütücü). Derleme/paketleme YAPILMAZ (dakikalar sürer, Velopack aracı gerektirir); yalnız saf parçalar: sürüm notu
/// kesimi (CHANGELOG bölümü ↔ uygulamanın kendi parser'ı) ve guard'lar. Yürütücü yoksa test atlar.</summary>
[Collection("Console UI (serial)")]
public class ReleaseScriptsTests
{
    private static string Scripts => Path.Combine(RepoPaths.RepoRoot, "scripts");

    /// <summary>Yürütücü (Windows PowerShell 5.1) yoksa test atlanır — her testin ilk satırı.</summary>
    private static void RequirePowerShell() =>
        Skip.IfNot(File.Exists(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")));

    private static (int ExitCode, string Output) Run(string script, params string[] args) =>
        RunIn(RepoPaths.RepoRoot, Path.Combine(Scripts, script), args);

    /// <summary>Script'i verilen çalışma dizininde koşturur (sandbox testleri kendi kopyalarını çalıştırır).</summary>
    private static (int ExitCode, string Output) RunIn(string workingDirectory, string scriptPath, params string[] args)
    {
        var psi = NewPowerShell(workingDirectory);
        psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(scriptPath);
        foreach (string a in args) psi.ArgumentList.Add(a);
        return Execute(psi);
    }

    /// <summary>-Command ile koşturur: komutta tanımlanan (global kapsamlı) fonksiyonlar, çağrılan script'e görünür ve
    /// aynı adlı cmdlet'i gölgeler — ağ çağrısı (Invoke-RestMethod), çalışan process sorgusu (Get-Process) ve
    /// <c>dotnet</c> script'e dokunmadan taklit edilir. Script'in kendi tanımladığı bir fonksiyonu (dot-source ettiği
    /// release-common.ps1'inkiler) gölgelemek İŞE YARAMAZ: script kapsamındaki tanım öndedir.</summary>
    private static (int ExitCode, string Output) RunCommand(string command, string? workingDirectory = null)
    {
        var psi = NewPowerShell(workingDirectory ?? RepoPaths.RepoRoot);
        psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(command);
        return Execute(psi);
    }

    private static string PackageScript => Path.Combine(Scripts, "package.ps1");

    /// <summary>Gölge <c>dotnet</c>: çağrıyı yazar ve başarılı sayılır (script <c>$LASTEXITCODE</c>'a bakar) — package.ps1'in
    /// download / publish / pack adımları koşmadan hangi komutu, hangi sırayla ve hangi argümanlarla vereceği çıktıdan okunur.
    /// Gerçek publish dakikalar sürer ve Velopack aracı ister.</summary>
    private const string RecordingDotnet = "function dotnet { 'DOTNET ' + ($args -join ' '); $global:LASTEXITCODE = 0 }";

    /// <summary>Sahte "çalışan uygulama"nın pid'i: <see cref="RunningApp"/> tek bir process döndürür ve onu reddeden
    /// script'lerin (release.ps1, verify-publish.ps1) çıktısında bu sayı aranır.</summary>
    private const int FakeAppPid = 4242;

    /// <summary>Gölge <c>Get-Process</c> (çalışan-örnek sondası <c>Get-RunningApp</c>, release-common.ps1): hiç process görmez.
    /// Geliştirici makinesinde gerçek bir Build Orchestrator açıkken de sonuç değişmez; <see cref="RecordingDotnet"/> ile aynı yol.</summary>
    private const string NoAppShadow = "function Get-Process { @() }";

    /// <summary>Gölge <c>Get-Process</c>: sonda tek bir sahte uygulama (<see cref="FakeAppPid"/>) görür. <paramref name="exePath"/>
    /// onun konumudur (<c>Path</c>); verilmezse konum OKUNAMAZ (erişim reddi gibi) — <c>Path</c> boş gelir.</summary>
    private static string RunningApp(string? exePath = null)
    {
        string path = exePath is null ? "$null" : "'" + exePath.Replace("'", "''") + "'";
        return $"function Get-Process {{ [pscustomobject]@{{ Id = {FakeAppPid}; Path = {path} }} }}";
    }

    /// <summary>Kurulu kopyanın konumu (Velopack: <c>%LocalAppData%\BuildOrchestrator.App\current</c>) — hiçbir checkout'un içinde
    /// değil; dosyanın gerçekten var olması gerekmez, sonda yalnız <c>Path</c> metnine bakar.</summary>
    private static string InstalledAppPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator.App", "current", "BuildOrchestrator.App.exe");

    /// <summary>release.ps1'i sandbox'ta koşturur. Çalışan-örnek sondası (<c>Get-Process</c>) gölgelenir: varsayılan
    /// <see cref="NoAppShadow"/> — geliştirici makinesinde gerçek bir Build Orchestrator açıkken de git akışı testleri aynı sonucu
    /// verir; <paramref name="processShadow"/> sondaya sahte bir uygulama (<see cref="RunningApp"/>) gösterir.</summary>
    private static (int ExitCode, string Output) RunRelease(ReleaseSandbox box, string processShadow = NoAppShadow) =>
        RunCommand(
            $"{processShadow}; & '{box.ReleaseScript}' -Version {box.NextVersion} -SkipTests; exit $LASTEXITCODE",
            box.Work);

    private static ProcessStartInfo NewPowerShell(string workingDirectory)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
        return psi;
    }

    private static (int ExitCode, string Output) Execute(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi)!;
        // İki akış AYRI okunur: biri dolarken diğerini bekleyen sıralı okuma child'ı kilitleyebilir.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    [SkippableFact]
    public void The_notes_cut_from_the_changelog_parse_to_the_same_entry_the_app_shows()
    {
        RequirePowerShell();
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
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = Run("package.ps1", "-NotesOnly", "-NotesVersion", "0.0.1", "-NotesOut", Path.Combine(temp.Path, "n.md"));
        Assert.NotEqual(0, code);
        Assert.Contains("0.0.1", output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Download_previous_is_skipped_when_the_repository_has_no_release()
    {
        RequirePowerShell();
        // -ReleaseCount 0: script GitHub'a sormaz (test), release yok → vpk download hiç çağrılmaz, çıkış 0.
        // Bu yalnız -ReleaseCount giriş noktasını pinler; API'den gelen cevabın sayımı aşağıdaki iki testtedir.
        var (code, output) = Run("package.ps1", "-DownloadPrevious", "-ReleaseCount", "0", "-WhatIf");
        Assert.True(code == 0, output);
        Assert.Contains("no previous release", output, StringComparison.Ordinal);
    }

    /// <summary>Eski iddia: "-ReleaseCount 0 verilince atlama dalı çalışır" (yukarıdaki test). Kusur: -ReleaseCount VERİLMEYİNCE
    /// (release.yml böyle çağırır) sayım <c>@(Invoke-RestMethod ...).Count</c> idi; Windows PowerShell 5.1'de Invoke-RestMethod
    /// JSON dizisini numaralandırmaz, <c>@()</c> boş diziyi TEK eleman olarak sarar → release'siz repoda 0 yerine 1, atlama dalı
    /// ilk yayında hiç çalışmazdı. Bu test cevabı gerçek cmdlet gibi (numaralandırmayan <c>ConvertFrom-Json</c>) verir ve
    /// -ReleaseCount'u VERMEZ.
    /// <para><b>Değişen mekanizma:</b> eskiden test <c>-WhatIf</c> ile koşardı ve sayım yine API'den okunurdu. <c>-WhatIf</c>
    /// artık ağa çıkmaz (<see cref="WhatIf_never_asks_the_api_for_the_release_count"/>), dolayısıyla sayımın script içindeki
    /// bağlantısı <c>-WhatIf</c>'siz koşulur; <c>dotnet</c> taklit edilir (<see cref="RecordingDotnet"/>) ve gerçek indirme /
    /// publish / pack çalışmaz. İddia aynı: boş liste → indirme atlanır.</para></summary>
    [SkippableFact]
    public void An_empty_release_list_from_the_api_is_counted_as_no_release()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = RunCommand(
            $"function Invoke-RestMethod {{ '[]' | ConvertFrom-Json }}; {RecordingDotnet}; & '{PackageScript}' -DownloadPrevious -ArtifactsDir '{temp.Path}'");
        Assert.True(code == 0, output);
        Assert.Contains("no previous release", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DOTNET vpk download", output, StringComparison.Ordinal); // indirme atlandı
        Assert.Contains("DOTNET publish", output, StringComparison.Ordinal);            // yayın akışı sürdü
    }

    /// <summary>Bir kayıt dönen liste → sayım 1 → önceki paket indirilir. Mekanizma için bkz.
    /// <see cref="An_empty_release_list_from_the_api_is_counted_as_no_release"/>.</summary>
    [SkippableFact]
    public void A_release_list_with_an_entry_from_the_api_still_downloads_the_previous_package()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = RunCommand(
            $"function Invoke-RestMethod {{ '[{{\"tag_name\":\"v1.7.0\"}}]' | ConvertFrom-Json }}; {RecordingDotnet}; & '{PackageScript}' -DownloadPrevious -ArtifactsDir '{temp.Path}'");
        Assert.True(code == 0, output);
        Assert.DoesNotContain("no previous release", output, StringComparison.Ordinal);
        Assert.Contains($"DOTNET vpk download github --repoUrl https://github.com/sdemir60/app_build_orchestrator --outputDir {Path.Combine(temp.Path, "velopack")}",
            output, StringComparison.Ordinal);
    }

    /// <summary>Kusur: <c>-DownloadPrevious</c> ve <c>-ReleaseCount</c> verilmeden <c>-WhatIf</c> koşusu release sayısını
    /// GitHub API'sinden sorardı (<c>Invoke-RestMethod</c>) — başlıktaki ve ARCHITECTURE §18'deki "-WhatIf hiçbir şeyi
    /// çalıştırmaz" iddiasına aykırı. Önceki testler <c>-ReleaseCount 0</c> verir ya da cmdlet'i taklit ettiği için
    /// yüzeye çıkmıyordu. Bu test cmdlet'i ATAN bir taklitle değiştirir: ağa gidilirse script düşer. Sayı bilinmediğinden
    /// indirme adımı "ne olurdu" olarak listelenir.</summary>
    [SkippableFact]
    public void WhatIf_never_asks_the_api_for_the_release_count()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = RunCommand(
            $"function Invoke-RestMethod {{ throw 'NETWORK-TOUCHED' }}; & '{PackageScript}' -DownloadPrevious -WhatIf -ArtifactsDir '{temp.Path}'");
        Assert.True(code == 0, output);
        Assert.DoesNotContain("NETWORK-TOUCHED", output, StringComparison.Ordinal);
        Assert.Contains("GET releases", output, StringComparison.Ordinal);        // yapacağı çağrıyı yazar
        Assert.Contains("vpk download github", output, StringComparison.Ordinal); // sayı bilinmiyor: indirme "ne olurdu" satırı
    }

    /// <summary>Kusur: <c>notes.md</c> Velopack'in kendi çıktı klasörüne (<c>artifacts\velopack</c>) yazılıyordu; <c>vpk download</c>
    /// ve <c>vpk pack</c> o klasörün sahibidir ve <c>release.yml</c> dosyayı yayın herkese açıldıktan SONRA
    /// (<c>gh release edit --notes-file</c>) yeniden okur. Bir vpk sürümü klasördeki bilinmeyen dosyayı temizleseydi hata
    /// yayından sonra düşerdi. Not artık <c>artifacts\notes.md</c>'dedir (klasörün dışında, yanında); <c>vpk pack</c>
    /// <c>--releaseNotes</c> ile aynı dosyayı okur. Test <c>dotnet</c>'u taklit eder ve verilen komut satırını okur.</summary>
    [SkippableFact]
    public void The_release_notes_sit_beside_the_velopack_folder_and_vpk_pack_reads_them_from_there()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = RunCommand($"{RecordingDotnet}; & '{PackageScript}' -ArtifactsDir '{temp.Path}'");
        Assert.True(code == 0, output);

        string notes = Path.Combine(temp.Path, "notes.md");
        Assert.True(File.Exists(notes), output);
        Assert.False(File.Exists(Path.Combine(temp.Path, "velopack", "notes.md")), "notes.md Velopack'in çıktı klasöründe");
        string pack = Assert.Single(output.Split('\n'), line => line.StartsWith("DOTNET vpk pack", StringComparison.Ordinal));
        Assert.Contains($"--releaseNotes {notes}", pack, StringComparison.Ordinal);
        Assert.Contains($"--outputDir {Path.Combine(temp.Path, "velopack")}", pack, StringComparison.Ordinal);
    }

    /// <summary>Not yolunun varsayılanı tek yerdedir: <c>-NotesOnly</c> ve tam koşu aynı dosyayı yazar.</summary>
    [SkippableFact]
    public void Notes_only_writes_to_the_same_default_place_as_a_full_run()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        var (code, output) = Run("package.ps1", "-NotesOnly", "-ArtifactsDir", temp.Path);
        Assert.True(code == 0, output);
        Assert.True(File.Exists(Path.Combine(temp.Path, "notes.md")), output);
    }

    private static string CommonScript => Path.Combine(Scripts, "release-common.ps1");

    private static void Touch(string folder, params string[] names)
    {
        Directory.CreateDirectory(folder);
        foreach (string name in names) File.WriteAllText(Path.Combine(folder, name), "x");
    }

    /// <summary>Kusur: aynı sürümü yeniden paketlemek (lokal prova/deneme) <c>vpk pack</c>'i düşürüyordu — "There is a release in
    /// channel win which is equal or greater to the current version" — çünkü <c>artifacts\velopack</c> önceki koşunun
    /// <c>-full.nupkg</c>'ını taşır (vpk bunu klasördeki nupkg'lardan okur; yalnız indeks dosyalarını silmek yetmez). Temizlik
    /// yalnız O sürümün full/delta paketlerini ve vpk'nın o paketlerden yeniden ürettiği indeks dosyalarını siler; başka
    /// sürümlerin nupkg'ları kalır (sonraki sürümün delta'sı onlardan üretilir — §17.6 provası). <c>11.7.0</c> ile <c>1.7.0</c>
    /// karışmaz.</summary>
    [SkippableFact]
    public void Repackaging_a_version_removes_only_its_own_packages_and_the_indexes()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        string[] mine = { "X-1.7.0-full.nupkg", "X-1.7.0-delta.nupkg", "releases.win.json", "RELEASES", "assets.win.json" };
        string[] others = { "X-1.6.0-full.nupkg", "X-1.6.0-delta.nupkg", "X-11.7.0-full.nupkg" };
        Touch(temp.Path, mine.Concat(others).ToArray());

        var (code, output) = RunCommand($". '{CommonScript}'; Remove-PackagedVersion -ReleasesDir '{temp.Path}' -Version '1.7.0'");

        Assert.True(code == 0, output);
        foreach (string name in mine) Assert.False(File.Exists(Path.Combine(temp.Path, name)), $"{name} kaldı");
        foreach (string name in others) Assert.True(File.Exists(Path.Combine(temp.Path, name)), $"{name} silindi");
    }

    /// <summary>Paketlenmemiş sürüm için temizlik hiçbir şeye dokunmaz — indeks dosyaları dahil (onlar önceki sürümlerin
    /// kaydıdır) — ve klasör hiç yoksa (ilk koşu, CI) hata vermez.</summary>
    [SkippableFact]
    public void A_version_that_was_never_packed_leaves_the_folder_as_it_is()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        string[] present = { "X-1.6.0-full.nupkg", "releases.win.json", "RELEASES", "assets.win.json" };
        Touch(temp.Path, present);

        var (code, output) = RunCommand($". '{CommonScript}'; Remove-PackagedVersion -ReleasesDir '{temp.Path}' -Version '1.7.0'");
        Assert.True(code == 0, output);
        foreach (string name in present) Assert.True(File.Exists(Path.Combine(temp.Path, name)), $"{name} silindi");

        var missing = RunCommand($". '{CommonScript}'; Remove-PackagedVersion -ReleasesDir '{Path.Combine(temp.Path, "yok")}' -Version '1.7.0'");
        Assert.True(missing.ExitCode == 0, missing.Output);
    }

    /// <summary>package.ps1 temizliği <c>vpk download</c> ve <c>vpk pack</c>'ten ÖNCE koşturur: <c>dotnet</c> gölgesi bu iki komutun
    /// çağrıldığı andaki klasör içeriğini yazar. Sürümün eski paketi o anda yoktur, önceki sürümünki durur. İndirme öncesi
    /// olması bilinçlidir: yayınlanmış bir yayından inen paket (aynı sürümse pack zaten düşmeli) silinmez.</summary>
    [SkippableFact]
    public void Packaging_clears_the_versions_earlier_packages_before_it_downloads_and_packs()
    {
        RequirePowerShell();
        string version = ReleaseNotes.All[0].Version; // == props Version (WhatsNewTests pinler)
        using var temp = new TempDir();
        string velopack = Path.Combine(temp.Path, "velopack");
        string mine = $"BuildOrchestrator.App-{version}-full.nupkg";
        const string older = "BuildOrchestrator.App-0.0.1-full.nupkg";
        Touch(velopack, mine, older);
        string dotnet = "function dotnet { 'DOTNET ' + ($args -join ' '); "
            + $"if ($args -contains 'download' -or $args -contains 'pack') {{ 'FOLDER ' + $args[1] + ' ' + ((Get-ChildItem -LiteralPath '{velopack}').Name -join ',') }}; "
            + "$global:LASTEXITCODE = 0 }";

        var (code, output) = RunCommand($"{dotnet}; & '{PackageScript}' -DownloadPrevious -ReleaseCount 1 -ArtifactsDir '{temp.Path}'");

        Assert.True(code == 0, output);
        string[] lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains($"FOLDER download {older}", lines); // indirme geldiğinde temizlenmiş
        Assert.Contains($"FOLDER pack {older}", lines);     // pack geldiğinde temizlenmiş; önceki sürüm delta için duruyor
        Assert.False(File.Exists(Path.Combine(velopack, mine)), output);
    }

    /// <summary>-WhatIf temizliği de yalnız yazar: dosya silinmez, ne yapacağı çıktıdadır (başlıktaki "hiçbir şeyi yazmaz" sözü).</summary>
    [SkippableFact]
    public void WhatIf_reports_the_removal_of_earlier_packages_and_removes_nothing()
    {
        RequirePowerShell();
        string version = ReleaseNotes.All[0].Version;
        using var temp = new TempDir();
        string velopack = Path.Combine(temp.Path, "velopack");
        string mine = $"BuildOrchestrator.App-{version}-full.nupkg";
        Touch(velopack, mine);

        var (code, output) = RunCommand($"& '{PackageScript}' -WhatIf -ArtifactsDir '{temp.Path}'");

        Assert.True(code == 0, output);
        Assert.Contains($"remove earlier packages of {version}", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(velopack, mine)), output);
    }

    [SkippableFact]
    public void The_release_guard_accepts_only_the_tag_of_the_current_version()
    {
        RequirePowerShell();
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
        RequirePowerShell();
        // -DryRun: git'e ve testlere dokunmaz; yalnız guard'lar koşar. CHANGELOG'da 99.0.0 yok → 1.
        var r = Run("release.ps1", "-Version", "99.0.0", "-DryRun");
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("CHANGELOG.md", r.Output, StringComparison.Ordinal);
    }

    /// <summary>[final review #4] Çalışan örnek sondası (<c>Get-RunningApp</c>, release-common.ps1) process'i ada göre bulur; hiçbiri
    /// yoksa boş dizi döner. Test gerçek uygulamayı AÇMAZ: olmayan bir ad "yok" verir, bu test sırasında zaten çalışan
    /// <c>powershell</c> "var" verir. Varsayılan ad Get-Process'in gölgesiyle okunur: uygulamanın process adı
    /// <c>BuildOrchestrator.App</c>.</summary>
    [SkippableFact]
    public void The_running_app_probe_finds_a_process_by_name()
    {
        RequirePowerShell();

        var absent = RunCommand($". '{CommonScript}'; @(Get-RunningApp 'bo-no-such-process-{Guid.NewGuid():N}').Count");
        Assert.True(absent.ExitCode == 0, absent.Output);
        Assert.Equal("0", absent.Output.Trim());

        var present = RunCommand($". '{CommonScript}'; @(Get-RunningApp 'powershell').Count -gt 0");
        Assert.True(present.ExitCode == 0, present.Output);
        Assert.Equal("True", present.Output.Trim());

        var byDefault = RunCommand($"function Get-Process {{ [CmdletBinding()] param([string]$Name) $Name }}; . '{CommonScript}'; Get-RunningApp");
        Assert.True(byDefault.ExitCode == 0, byDefault.Output);
        Assert.Equal("BuildOrchestrator.App", byDefault.Output.Trim());
    }

    /// <summary>Kusur: sonda process'i yalnız ADA göre buluyordu; ilk kurulumdan sonra kullanıcının tepsideki KURULU kopyası
    /// (<c>%LocalAppData%\BuildOrchestrator.App\current</c>) da <c>/release</c>'i durdururdu — oysa gerekçe (çalışan Supervisor kendi
    /// binary'lerini kilitler) yalnız bu checkout'un <c>bin\</c>'inden çalışan kopya için geçerli. <c>-UnderPath</c> verilince yalnız
    /// <c>Path</c>'i o klasörün altındaki process'ler sayılır; verilmezse eski davranış (her örnek).
    /// <para>GERÇEK bir process kullanılır: testin kendi <c>powershell</c>'i (<c>Path</c> = System32 altı); havuz yalnız o tek
    /// process'e daraltılır ki makinedeki başka (yükseltilmiş, konumu okunamayan) bir powershell sonucu bozmasın. Klasör sınırı tam
    /// ad eşleşmesidir: <c>...\v1</c>, <c>...\v1.0</c>'ın metin öneki olsa da sayılmaz — kardeş çalışma klasörleri
    /// (<c>app_build_orchestrator</c> / <c>app_build_orchestrator-ai</c>) birbirini durdurmasın.</para></summary>
    [SkippableFact]
    public void The_running_app_probe_counts_only_processes_under_the_given_folder()
    {
        RequirePowerShell();
        using var temp = new TempDir();
        const string onlyThisProcess = "function Get-Process { [System.Diagnostics.Process]::GetCurrentProcess() }";
        const string powershellDir = @"Join-Path $env:SystemRoot 'System32\WindowsPowerShell'";

        var r = RunCommand($"{onlyThisProcess}; . '{CommonScript}'; $dir = {powershellDir}; "
            + $"'outside=' + @(Get-RunningApp 'powershell' -UnderPath '{temp.Path}').Count; "
            + "'inside=' + @(Get-RunningApp 'powershell' -UnderPath $dir).Count; "
            + "'trailing=' + @(Get-RunningApp 'powershell' -UnderPath ($dir + '\\')).Count; "
            + "'prefix=' + @(Get-RunningApp 'powershell' -UnderPath (Join-Path $dir 'v1')).Count; "
            + "'unscoped=' + @(Get-RunningApp 'powershell').Count");

        Assert.True(r.ExitCode == 0, r.Output);
        var counts = r.Output.Split('\n').Select(l => l.Trim().Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        Assert.Equal("0", counts["outside"]);   // kök dışındaki process sayılmaz
        Assert.Equal("1", counts["inside"]);    // kökün altındaki sayılır
        Assert.Equal("1", counts["trailing"]);  // kökün sonundaki ayraç fark etmez
        Assert.Equal("0", counts["prefix"]);    // metin öneki klasör değildir
        Assert.Equal("1", counts["unscoped"]);  // kök verilmezse eski davranış: her örnek
    }

    /// <summary>Konumu OKUNAMAYAN process (erişim reddi: yükseltilmiş ya da başka kullanıcının süreci — <c>Path</c> boş ya da okuması
    /// istisna atar) temkinle SAYILIR: yanlış "yok" build'i kilitli dosyada düşürürdü, yanlış "var" yalnız bir "kapat" mesajı ister.
    /// Kök dışında okunabilir konumdaki process sayılmaz; kök adıyla başlayan kardeş klasör (<c>C:\root-ai</c>) de.</summary>
    [SkippableFact]
    public void A_process_whose_location_cannot_be_read_is_still_counted()
    {
        RequirePowerShell();
        const string shadow = @"function Get-Process {
            $inside = [pscustomobject]@{ Id = 1; Path = 'C:\root\app\a.exe' }
            $elsewhere = [pscustomobject]@{ Id = 2; Path = 'C:\other\a.exe' }
            $empty = [pscustomobject]@{ Id = 3; Path = $null }
            $denied = [pscustomobject]@{ Id = 4 }
            $denied | Add-Member -MemberType ScriptProperty -Name Path -Value { throw 'Access is denied' }
            $sibling = [pscustomobject]@{ Id = 5; Path = 'C:\root-ai\a.exe' }
            $inside; $elsewhere; $empty; $denied; $sibling
        }";

        var r = RunCommand($"{shadow}; $ErrorActionPreference = 'Stop'; . '{CommonScript}'; (Get-RunningApp -UnderPath 'C:\\root' | ForEach-Object Id) -join ','");

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal("1,3,4", r.Output.Trim());
    }

    /// <summary>[final review #4] Sonda TEK sahiplidir (kopya yasak): <c>release.ps1</c> ve <c>verify-publish.ps1</c> process
    /// adını kendileri yazmaz, <c>Get-RunningApp</c>'i çağırır. Eskiden verify-publish sondayı kendi içinde taşıyordu.</summary>
    [Fact]
    public void The_running_app_probe_has_one_owner()
    {
        foreach (string script in new[] { "release.ps1", "verify-publish.ps1" })
        {
            string text = File.ReadAllText(Path.Combine(Scripts, script));
            Assert.True(text.Contains("Get-RunningApp", StringComparison.Ordinal), $"{script} sondayı release-common.ps1'den çağırmıyor");
            Assert.False(text.Contains("'BuildOrchestrator.App'", StringComparison.Ordinal), $"{script} process adını kendisi yazıyor");
        }
        Assert.Contains("'BuildOrchestrator.App'", File.ReadAllText(Path.Combine(Scripts, "release-common.ps1")), StringComparison.Ordinal);
    }

    /// <summary>[final review #4] Bu checkout'tan çalışan uygulamayla build alınmaz (CLAUDE.md): Release build çalışan Supervisor'ın
    /// kilitli binary'lerine çarpar, ama kusur bunun ÇOK sonra ortaya çıkmasıydı — <c>Version</c> <c>Directory.Build.props</c>'a
    /// çoktan yazılmış, açıklanması gereken kirli bir dosya kalmıştı. Script artık props'a dokunmadan durur: çıkış 1, pid
    /// mesajda, ne props ne yerel HEAD ne origin değişir. Çalışan uygulama gerçekten açılmaz: <c>Get-Process</c> gölgelenir ve
    /// process'in konumu checkout'un (sandbox'ın çalışma klasörü) altındadır.
    /// <para><b>Değişen kural:</b> eski iddia "uygulama (herhangi bir konumdan) çalışıyorsa durur"du; process'in konumu olmayan
    /// sahte bir process yeterdi. Gerekçe yalnız bu checkout'tan çalışan kopya için geçerli olduğundan sınır repo köküne çekildi
    /// (bkz. <see cref="A_copy_installed_elsewhere_does_not_stop_the_release"/>); iddia aynı kaldı: o kopya açıkken durur.</para></summary>
    [SkippableFact]
    public void The_release_script_refuses_while_this_checkouts_app_is_running_before_it_writes_the_version()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string originBefore = box.OriginMain;
        string localBefore = box.WorkHead;

        var r = RunRelease(box, RunningApp(Path.Combine(box.Work, "src", "BuildOrchestrator.App", "bin", "Release", "BuildOrchestrator.App.exe")));

        Assert.Equal(1, r.ExitCode);
        Assert.Contains(FakeAppPid.ToString(), r.Output, StringComparison.Ordinal);
        Assert.Contains("<Version>1.7.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal); // Version yazılmadı
        Assert.Equal(localBefore, box.WorkHead);
        Assert.Equal(originBefore, box.OriginMain);
    }

    /// <summary>Kusur: sonda process'i yalnız ADA göre buluyordu — ilk kurulumdan sonra tepsideki KURULU kopya
    /// (<c>%LocalAppData%\BuildOrchestrator.App\current</c>) da <c>/release</c>'i durdururdu, oysa o kopyanın dosyaları bu
    /// checkout'un build'ini kilitlemez. release.ps1 sondaya repo kökünü verir (<c>-UnderPath</c>): kurulu kopya açıkken yayın
    /// sonuna kadar gider (release commit'i ve tag origin'de).</summary>
    [SkippableFact]
    public void A_copy_installed_elsewhere_does_not_stop_the_release()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");

        var r = RunRelease(box, RunningApp(InstalledAppPath));

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal(box.WorkHead, box.OriginMain);
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim());
    }

    /// <summary>[final review #4] Sondanın verify-publish'teki kullanımı taşındıktan sonra da aynıdır: çalışan örnek varken hiçbir
    /// ölçüm yapmadan <c>RESULT: SKIPPED</c> ve ön koşul kodu 2 ile durur. Uygulama TEK-ÖRNEKLİDİR ve script canlı pencereyi UI
    /// Automation ile okur: kurulu kopya da ölçümü bozar (ikinci örnek mevcut pencereyi öne getirip kapanır), bu yüzden
    /// verify-publish sondaya kök vermez ve konumu checkout dışında olan örnek de durdurur. Sonda gölgelenir.</summary>
    [SkippableFact]
    public void Verify_publish_stops_with_the_precondition_code_while_any_copy_of_the_app_runs()
    {
        RequirePowerShell();
        var r = RunCommand($"{RunningApp(InstalledAppPath)}; & '{Path.Combine(Scripts, "verify-publish.ps1")}'; exit $LASTEXITCODE");
        Assert.Equal(2, r.ExitCode);
        Assert.Contains(FakeAppPid.ToString(), r.Output, StringComparison.Ordinal);
        Assert.Contains("RESULT: SKIPPED", r.Output, StringComparison.Ordinal);
    }

    /// <summary>Kusur: guard'lar tag'e yalnız YEREL bakıyordu (<c>git tag --list</c>). <c>git fetch</c> ise sadece getirdiği
    /// tarihçeye işaret eden tag'leri alır; origin'de erişilemeyen bir commit'e duran aynı ad yerelde görünmez. Sonuç: main
    /// push edilir, tag reddedilir → main'de yayını olmayan bir release commit'i. Bu test o tag'i origin'e koyar ve script'in
    /// HİÇBİR şeye dokunmadan durmasını ister.</summary>
    [SkippableFact]
    public void The_release_script_refuses_a_tag_that_origin_already_has_before_touching_anything()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.TagAnUnreachableCommitOnOrigin("v1.8.0");
        string originBefore = box.OriginMain;
        string localBefore = box.WorkHead;
        Assert.Equal("", box.Git(box.Work, "tag", "--list", "v1.8.0").Trim()); // yerel klon görmüyor: kusur bu

        var r = RunRelease(box);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("already exists on origin", r.Output, StringComparison.Ordinal);
        Assert.Equal(originBefore, box.OriginMain);                                        // main push edilmedi
        Assert.Equal(localBefore, box.WorkHead);                                           // release commit'i atılmadı
        Assert.Contains("<Version>1.7.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal); // Version yazılmadı
    }

    /// <summary>Kusur: <c>git push origin main vX</c> atomik değildi. Fetch ile push arasında build + tam süit dakikalar sürer;
    /// origin/main bu arada ilerlerse git main'i non-fast-forward diye reddeder ama tag'i GÖNDERİRDİ → CI'daki release-guard
    /// geçer, origin/main'de olmayan bir commit'ten yayın çıkar. Yarış, release commit'inin hemen ardından origin'i ilerleten bir
    /// post-commit hook ile (fetch'ten sonra, push'tan önce) deterministik kurulur.</summary>
    [SkippableFact]
    public void A_main_that_moved_before_the_push_leaves_no_tag_on_origin()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.AdvanceOriginRightAfterTheNextCommit();

        var r = RunRelease(box);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal(box.OtherHead, box.OriginMain); // yarış gerçekten kuruldu: origin'de yalnız rakip commit var
        Assert.Equal("", box.Git(box.Work, "ls-remote", "--tags", "origin").Trim()); // tag origin'e ULAŞMADI (atomik push)
    }

    /// <summary>Atomik push normal akışı bozmaz: yarış yokken release commit'i ve annotated tag birlikte origin'e ulaşır.</summary>
    [SkippableFact]
    public void A_clean_release_pushes_the_release_commit_and_the_annotated_tag_together()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");

        var r = RunRelease(box);

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal(box.WorkHead, box.OriginMain);
        Assert.Equal("release: v1.8.0", box.Git(box.Origin, "log", "-1", "--format=%s", "main").Trim());
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim()); // annotated: tag nesnesi
        Assert.Contains("<Version>1.8.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal);
    }

    /// <summary>Kusur: release.yml'in guard'ı yalnız tag == Version == CHANGELOG eşitliğine bakıyordu; elle itilen ve
    /// origin/main'de OLMAYAN bir commit'e duran tag (bir iş branch'inden, push edilmemiş bir denemeden) aynı eşitliği
    /// taşırsa yayın çıkardı. <c>-RequireOnMain</c> (CI'ın kipi) HEAD'in origin/main'in atası olmasını da ister. Yerel
    /// <c>release.ps1</c> bu anahtarı vermez: orada guard release commit'inden önce koşar.</summary>
    [SkippableFact]
    public void The_release_guard_on_CI_refuses_a_commit_that_origin_main_does_not_contain()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.CommitTheNextVersionWithoutPushing();
        string tag = "v" + box.NextVersion;

        Assert.Equal(0, RunIn(box.Work, box.GuardScript, "-Tag", tag).ExitCode); // yerel kip: ata sorulmaz

        var refused = RunIn(box.Work, box.GuardScript, "-Tag", tag, "-RequireOnMain");
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("origin/main", refused.Output, StringComparison.Ordinal);

        box.Git(box.Work, "push", "-q", "origin", "main"); // commit artık origin/main'de
        var accepted = RunIn(box.Work, box.GuardScript, "-Tag", tag, "-RequireOnMain");
        Assert.True(accepted.ExitCode == 0, accepted.Output);
    }

    /// <summary>release.ps1'in gerçek git akışı için izole ortam: bare origin + çalışma klonu (script'ler ve asgari
    /// props/CHANGELOG/slnx içerir; script kökü kendi konumundan çıkarır) + origin'i "başka biri" gibi ilerleten ikinci klon.
    /// Yeni sürümün CHANGELOG bölümü commit EDİLMEMİŞ değişikliktir (gerçek akışta Claude yazar, script commit'ler). Gerçek
    /// repoya dokunulmaz; <see cref="GitTestRepo.RunGitAt"/> kullanılır.</summary>
    private sealed class ReleaseSandbox : IDisposable
    {
        private readonly TempDir _temp = new();

        public string Origin => Path.Combine(_temp.Path, "origin.git");
        public string Work => Path.Combine(_temp.Path, "work");
        public string Other => Path.Combine(_temp.Path, "other");
        public string ReleaseScript => Path.Combine(Work, "scripts", "release.ps1");
        public string GuardScript => Path.Combine(Work, "scripts", "release-guard.ps1");
        public string PropsPath => Path.Combine(Work, "Directory.Build.props");
        /// <summary>CHANGELOG'un en üstüne (commit'siz) yazılan yeni sürüm.</summary>
        public string NextVersion { get; }
        public string OriginMain => Git(Origin, "rev-parse", "main").Trim();
        public string WorkHead => Git(Work, "rev-parse", "HEAD").Trim();
        public string OtherHead => Git(Other, "rev-parse", "HEAD").Trim();

        public ReleaseSandbox(string nextVersion)
        {
            NextVersion = nextVersion;
            Git(_temp.Path, "init", "-q", "--bare", "-b", "main", Origin);
            Git(_temp.Path, "clone", "-q", Origin, Work);
            Configure(Work);

            Directory.CreateDirectory(Path.Combine(Work, "scripts"));
            foreach (string script in new[] { "release.ps1", "release-guard.ps1", "release-common.ps1" })
                File.Copy(Path.Combine(Scripts, script), Path.Combine(Work, "scripts", script));
            File.WriteAllText(PropsPath,
                "<Project><PropertyGroup><Version>1.7.0</Version><Product>Sandbox</Product><Company>Sandbox</Company></PropertyGroup></Project>\n");
            File.WriteAllText(Path.Combine(Work, "BuildOrchestrator.slnx"), "<Solution />\n");
            const string older = "## [1.7.0] - 2026-01-01\n\n### Added\n- Older entry.\n";
            string changelog = Path.Combine(Work, "CHANGELOG.md");
            File.WriteAllText(changelog, "# Changelog\n\n" + older);
            Git(Work, "add", "-A");
            Git(Work, "commit", "-q", "-m", "init");
            Git(Work, "push", "-q", "origin", "main");

            Git(_temp.Path, "clone", "-q", Origin, Other);
            Configure(Other);

            string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(changelog, $"# Changelog\n\n## [{nextVersion}] - {today}\n\n### Added\n- New entry.\n\n" + older);
        }

        public string Git(string workingDirectory, params string[] args) => GitTestRepo.RunGitAt(workingDirectory, args);

        private void Configure(string repo)
        {
            Git(repo, "config", "user.email", "test@buildorchestrator.local");
            Git(repo, "config", "user.name", "Build Orchestrator Test");
            Git(repo, "config", "core.autocrlf", "false"); // fixture LF yazar; kullanıcı ayarı satır sonu gürültüsü üretmesin
        }

        /// <summary>Yeni sürümü (props <c>Version</c> + bekleyen CHANGELOG bölümü) çalışma klonunda commit'ler ama push
        /// ETMEZ: tag == Version == CHANGELOG tutarlıdır, commit origin/main'de yoktur.</summary>
        public void CommitTheNextVersionWithoutPushing()
        {
            File.WriteAllText(PropsPath, System.Text.RegularExpressions.Regex.Replace(
                File.ReadAllText(PropsPath), "<Version>[^<]*</Version>", $"<Version>{NextVersion}</Version>"));
            Git(Work, "commit", "-q", "-a", "-m", "release: v" + NextVersion);
        }

        /// <summary>Origin'e, hiçbir branch'ten erişilemeyen (parent'sız) bir commit'i gösteren tag koyar: çalışma klonunun
        /// <c>git fetch</c>'i bunu getirmez, <c>git tag --list</c> göremez.</summary>
        public void TagAnUnreachableCommitOnOrigin(string tag)
        {
            string orphan = Git(Other, "commit-tree", "HEAD^{tree}", "-m", "orphan").Trim();
            Git(Other, "tag", tag, orphan);
            Git(Other, "push", "-q", "origin", "refs/tags/" + tag);
        }

        /// <summary>Çalışma klonunda atılacak İLK commit'in hemen ardından ikinci klondan origin/main'e bir commit iter
        /// (release.ps1'de commit, fetch'ten sonra ve push'tan önce gelir).</summary>
        public void AdvanceOriginRightAfterTheNextCommit()
        {
            string other = Other.Replace('\\', '/');
            string hook = "#!/bin/sh\n"
                + "unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_PREFIX\n" // hook'a git'in kendi ortamı geçer; öteki klonu bozmasın
                + $"cd \"{other}\" || exit 1\n"
                + "git commit -q --allow-empty -m competing || exit 1\n"
                + "git push -q origin main || exit 1\n";
            File.WriteAllText(Path.Combine(Work, ".git", "hooks", "post-commit"), hook);
        }

        public void Dispose()
        {
            // git nesne dosyaları salt-okunurdur; TempDir bunları silemez (ve assertion hatasını Dispose istisnasıyla örterdi).
            foreach (string file in Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            _temp.Dispose();
        }
    }
}
