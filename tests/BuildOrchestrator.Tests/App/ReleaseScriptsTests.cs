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

    /// <summary>Gölge <c>Invoke-RestMethod</c>'un "koşu yok" cevabı: boş <c>workflow_runs</c> — gerçek cmdlet gibi
    /// <c>ConvertFrom-Json</c>'dan gelir.</summary>
    private const string NoCiRunAnswer = "'{\"total_count\":0,\"workflow_runs\":[]}' | ConvertFrom-Json";

    /// <summary>Gölge <c>Invoke-RestMethod</c> (develop'un CI sondası <c>Get-CiConclusion</c>, release-common.ps1): hiçbir commit'in
    /// koşusu yok — push edilmemiş ya da CI'ı hiç tetiklenmemiş bir develop.</summary>
    private const string NoCiRuns = "function Invoke-RestMethod { " + NoCiRunAnswer + " }";

    /// <summary>Gölge <c>Invoke-RestMethod</c>: ağa çıkan her çağrı düşer (<c>NETWORK-TOUCHED</c>) — bir akışın API'ye HİÇ
    /// sormadığını ya da sorulamayan API'de durduğunu gösterir.</summary>
    private const string NetworkForbidden = "function Invoke-RestMethod { throw 'NETWORK-TOUCHED' }";

    /// <summary>Gölge <c>Invoke-RestMethod</c>: YALNIZ <paramref name="sha"/>'nın <c>ci.yml</c> koşularını soran çağrıya tek bir koşu
    /// (<paramref name="status"/> / <paramref name="conclusion"/>; bitmemiş koşunun sonucu <c>null</c>) döner, başka her sorguya boş
    /// liste — script yanlış commit'i ya da yanlış workflow'u sorarsa "koşu yok" görür ve durur. Ağa çıkılmaz.</summary>
    private static string CiRun(string sha, string status, string? conclusion)
    {
        string run = "{\"status\":\"" + status + "\",\"conclusion\":" + (conclusion is null ? "null" : "\"" + conclusion + "\"") + "}";
        return "function Invoke-RestMethod { param([string]$Uri, $Headers) "
            + "if ($Uri -like '*/actions/workflows/ci.yml/runs?head_sha=" + sha + "&*') "
            + "{ '{\"total_count\":1,\"workflow_runs\":[" + run + "]}' | ConvertFrom-Json } "
            + "else { " + NoCiRunAnswer + " } }";
    }

    /// <summary>Gölge <c>git</c>: script'in her git çağrısı (<c>Invoke-Git</c> dahil) önce <paramref name="body"/>'den geçer;
    /// <c>$args[0]</c> alt komuttur. Gerçek git'e <see cref="RealGit"/> ile inilir; <c>return</c> eden dal hiç inmez. Çıkış kodu
    /// script'in okuduğu <c>$global:LASTEXITCODE</c>'a yazılır. Ayrı process'te koşan release-guard.ps1'e gölge geçmez.</summary>
    private static string GitShadow(string body) => "function git { " + body + " }";

    /// <summary>Gölgenin gerçek git'e geçen çağrısı (<c>git</c> adı gölgenin kendisidir; <c>git.exe</c> uygulamadır).</summary>
    private const string RealGit = "& git.exe @args";

    /// <summary><c>git worktree list</c> düşer (çıkış 128, çıktı yok); öteki her çağrı gerçek git'tir.</summary>
    private static string WorktreeListFails =>
        GitShadow("if ($args[0] -eq 'worktree') { $global:LASTEXITCODE = 128; return }; " + RealGit);

    /// <summary>Push GERÇEKTEN yapılır (origin yayını alır) ama çıkış kodu 1 döner: sunucu atomik güncellemeyi uyguladıktan sonra
    /// cevap gelmeden kopan bağlantı.</summary>
    private static string PushAppliesButReportsAnError =>
        GitShadow(RealGit + "; if ($args[0] -eq 'push') { $global:LASTEXITCODE = 1 }");

    /// <summary>Push hiç gönderilmez (çıkış 1 — ağ gitti) ve ondan SONRA origin okunamaz: push'tan sonraki <c>ls-remote</c> 128
    /// döner. Guard evresindeki <c>ls-remote</c> gerçektir.</summary>
    private static string PushFailsAndOriginCannotBeRead =>
        GitShadow("if ($args[0] -eq 'push') { $global:pushTried = $true; $global:LASTEXITCODE = 1; return }; "
            + "if ($args[0] -eq 'ls-remote' -and $global:pushTried) { $global:LASTEXITCODE = 128; return }; " + RealGit);

    /// <summary>release.ps1'i sandbox'ta koşturur; iki sonda gölgelenir. Çalışan-örnek sondası (<c>Get-Process</c>): varsayılan
    /// <see cref="NoAppShadow"/> — geliştirici makinesinde gerçek bir Build Orchestrator açıkken de git akışı testleri aynı sonucu
    /// verir; <paramref name="processShadow"/> sondaya sahte bir uygulama (<see cref="RunningApp"/>) gösterir. develop'un CI koşusu
    /// (<c>Invoke-RestMethod</c>): varsayılan, develop'un ŞU ANKİ commit'i için yeşil bir koşu (<see cref="CiRun"/>);
    /// <paramref name="ciShadow"/> başka bir cevap verir. <paramref name="switches"/> script'e verilen anahtarlardır.
    /// <paramref name="gitShadow"/> verilirse script'in git çağrıları ondan geçer (<see cref="GitShadow"/>); varsayılan gerçek git.</summary>
    private static (int ExitCode, string Output) RunRelease(ReleaseSandbox box, string processShadow = NoAppShadow,
        string? ciShadow = null, string switches = "-SkipTests", string gitShadow = "") =>
        RunCommand(
            $"{processShadow}; {ciShadow ?? CiRun(box.LocalDevelop, "completed", "success")}; {gitShadow}; "
            + $"& '{box.ReleaseScript}' -Version {box.NextVersion} {switches}; exit $LASTEXITCODE",
            box.Work);

    /// <summary>Script'in kurtarma satırındaki komutlar. Satır script'in SON <c>release:</c> satırıdır (git'in kendi hata satırları
    /// stderr'dedir ve birleşik çıktıda sonra gelir); biçimi <c>release: undo ... with: &lt;komut&gt;; &lt;komut&gt; ...</c>.</summary>
    private static string UndoCommands(string output)
    {
        string last = output.Split('\n').Select(l => l.TrimEnd('\r')).Last(l => l.StartsWith("release: ", StringComparison.Ordinal));
        const string marker = " with: ";
        Assert.StartsWith("release: undo ", last, StringComparison.Ordinal);
        return last[(last.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
    }

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

    /// <summary>develop'un CI sondası (<c>Get-CiConclusion</c>, release-common.ps1) GitHub'ın açık API'sine kimliksiz sorar:
    /// <c>actions/workflows/&lt;workflow&gt;/runs?head_sha=&lt;sha&gt;&amp;per_page=1</c> — o commit'in en yeni koşusu (API en yeniyi
    /// başta verir; varsayılan workflow <c>ci.yml</c>) — ve <c>User-Agent</c> gönderir (GitHub başlıksız isteği reddeder). Koşu varsa
    /// durumunu ve sonucunu döner. Gölge <c>Invoke-RestMethod</c> isteği yazar, cevabı gerçek cmdlet gibi <c>ConvertFrom-Json</c>'dan
    /// verir.</summary>
    [SkippableFact]
    public void The_CI_probe_asks_for_the_newest_run_of_the_commit_and_reads_its_state()
    {
        RequirePowerShell();
        const string recording = "function Invoke-RestMethod { param([string]$Uri, $Headers) "
            + "Write-Host ('URI ' + $Uri); Write-Host ('UA ' + $Headers['User-Agent']); "
            + "'{\"total_count\":2,\"workflow_runs\":[{\"status\":\"completed\",\"conclusion\":\"success\"}]}' | ConvertFrom-Json }";

        var r = RunCommand($"{recording}; . '{CommonScript}'; "
            + "$run = Get-CiConclusion -RepoUrl 'https://github.com/o/r' -Sha 'abc123'; 'RESULT ' + $run.Status + '/' + $run.Conclusion; "
            + "$null = Get-CiConclusion -RepoUrl 'https://github.com/o/r' -Sha 'abc123' -Workflow 'release.yml'");

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Contains("URI https://api.github.com/repos/o/r/actions/workflows/ci.yml/runs?head_sha=abc123&per_page=1", r.Output, StringComparison.Ordinal);
        Assert.Contains("URI https://api.github.com/repos/o/r/actions/workflows/release.yml/runs?head_sha=abc123&per_page=1", r.Output, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^UA \S+", r.Output);
        Assert.Contains("RESULT completed/success", r.Output, StringComparison.Ordinal);
    }

    /// <summary>Koşusu olmayan commit (push edilmemiş ya da CI'ı tetiklenmemiş) <c>$null</c> verir: boş <c>workflow_runs</c>
    /// Windows PowerShell 5.1'de de "koşu yok" sayılır (bkz. <see cref="An_empty_release_list_from_the_api_is_counted_as_no_release"/>
    /// — orada dizi cevabın kendisiydi, burada cevabın bir özelliği).</summary>
    [SkippableFact]
    public void A_commit_without_a_CI_run_reads_as_no_run()
    {
        RequirePowerShell();
        var r = RunCommand($"{NoCiRuns}; . '{CommonScript}'; $null -eq (Get-CiConclusion -RepoUrl 'https://github.com/o/r' -Sha 'abc123')");
        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal("True", r.Output.Trim());
    }

    /// <summary>Üç durumlu ata sorusu tek yerdedir (<c>Test-GitAncestor</c>, release-common.ps1): <c>git merge-base --is-ancestor</c>
    /// çıkışı 0 → <c>$true</c>, 1 → <c>$false</c>, diğeri (ref yok, sığ checkout) → istisna (<c>git exit N</c>). Sorulamayan ata ne
    /// "ata" ne "değil" sayılır — çağıranlar (release.ps1'in ata guard'ı, release-guard.ps1'in <c>-RequireOnMain</c>'i) kendi
    /// mesajıyla durur. Sorular geçici bir repoya sorulur: script kökü (<c>$RepoRoot</c>) dot-source'tan sonra oraya çevrilir.</summary>
    [SkippableFact]
    public void The_ancestry_probe_answers_yes_or_no_and_throws_when_git_cannot_tell()
    {
        RequirePowerShell();
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "1");
        string older = repo.CommitAll("older");
        repo.WriteFile("a.txt", "2");
        string newer = repo.CommitAll("newer");

        var r = RunCommand($". '{CommonScript}'; $RepoRoot = '{repo.RootPath}'; "
            + $"'yes=' + (Test-GitAncestor {older} {newer}); 'no=' + (Test-GitAncestor {newer} {older}); "
            + $"try {{ $null = Test-GitAncestor no-such-ref {newer}; 'unknown=answered' }} catch {{ 'unknown=threw ' + $_.Exception.Message }}");

        string[] lines = r.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains("yes=True", lines);
        Assert.Contains("no=False", lines);
        Assert.Contains(lines, l => l.StartsWith("unknown=threw git exit ", StringComparison.Ordinal));
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
            $"{NetworkForbidden}; & '{PackageScript}' -DownloadPrevious -WhatIf -ArtifactsDir '{temp.Path}'");
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
        // -DryRun: yalnız guard'lar koşar, hiçbir şey yazılmaz. CHANGELOG guard'ı İLK sıradadır (git'ten, fetch'ten ve CI
        // sorgusundan önce) — bu test GERÇEK repoda koştuğu için ağa ve git'e hiç inmez. CHANGELOG'da 99.0.0 yok → 1.
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
    /// mesajda, ne props ne yerel branch/tag'ler ne origin değişir. Çalışan uygulama gerçekten açılmaz: <c>Get-Process</c>
    /// gölgelenir ve process'in konumu checkout'un (sandbox'ın çalışma klasörü) altındadır.
    /// <para><b>Değişen kural:</b> eski iddia "uygulama (herhangi bir konumdan) çalışıyorsa durur"du; process'in konumu olmayan
    /// sahte bir process yeterdi. Gerekçe yalnız bu checkout'tan çalışan kopya için geçerli olduğundan sınır repo köküne çekildi
    /// (bkz. <see cref="A_copy_installed_elsewhere_does_not_stop_the_release"/>); iddia aynı kaldı: o kopya açıkken durur.</para></summary>
    [SkippableFact]
    public void The_release_script_refuses_while_this_checkouts_app_is_running_before_it_writes_the_version()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string before = box.Snapshot();

        var r = RunRelease(box, RunningApp(Path.Combine(box.Work, "src", "BuildOrchestrator.App", "bin", "Release", "BuildOrchestrator.App.exe")));

        Assert.Equal(1, r.ExitCode);
        Assert.Contains(FakeAppPid.ToString(), r.Output, StringComparison.Ordinal);
        Assert.Contains("<Version>1.7.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal); // Version yazılmadı
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary>Kusur: sonda process'i yalnız ADA göre buluyordu — ilk kurulumdan sonra tepsideki KURULU kopya
    /// (<c>%LocalAppData%\BuildOrchestrator.App\current</c>) da <c>/release</c>'i durdururdu, oysa o kopyanın dosyaları bu
    /// checkout'un build'ini kilitlemez. release.ps1 sondaya repo kökünü verir (<c>-UnderPath</c>): kurulu kopya açıkken yayın
    /// sonuna kadar gider (merge, develop ve tag origin'de).</summary>
    [SkippableFact]
    public void A_copy_installed_elsewhere_does_not_stop_the_release()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");

        var r = RunRelease(box, RunningApp(InstalledAppPath));

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal(box.WorkHead, box.OriginMain);
        Assert.Equal(box.OriginMain, box.OriginDevelop);
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
    /// tarihçeye işaret eden tag'leri alır; origin'de erişilemeyen bir commit'e duran aynı ad yerelde görünmez. Sonuç: branch'ler
    /// push edilir, tag reddedilir → main'de yayını olmayan bir release merge'ü. Bu test o tag'i origin'e koyar ve script'in
    /// HİÇBİR şeye dokunmadan durmasını ister.</summary>
    [SkippableFact]
    public void The_release_script_refuses_a_tag_that_origin_already_has_before_touching_anything()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.TagAnUnreachableCommitOnOrigin("v1.8.0");
        string before = box.Snapshot();
        Assert.Equal("", box.Git(box.Work, "tag", "--list", "v1.8.0").Trim()); // yerel klon görmüyor: kusur bu

        var r = RunRelease(box);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("already exists on origin", r.Output, StringComparison.Ordinal);
        Assert.Contains("<Version>1.7.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal); // Version yazılmadı
        Assert.Equal(before, box.Snapshot()); // ne push ne release commit'i ne merge
    }

    /// <summary>Kusur: push atomik değildi. Fetch ile push arasında build + tam süit dakikalar sürer; bu arada origin'de bir ref
    /// ilerlerse git onu non-fast-forward diye reddeder ama diğerlerini GÖNDERİRDİ — ör. main + tag gider, develop gitmez (yayın
    /// çıkar, develop ile main ayrışır) ya da tag, origin/main'in hiç görmediği bir merge'e gider. Push atomiktir: main, develop ve
    /// tag ya birlikte gider ya hiçbiri. Yarış, release commit'inin hemen ardından origin'i ilerleten bir post-commit hook ile
    /// (fetch'ten sonra, push'tan önce) deterministik kurulur; hem develop'un (olası yarış: biri develop'a iş itti) hem main'in
    /// ilerlemesi denenir.
    /// <para>Push reddinden sonra yerelde release commit'i, merge ve tag kalır; script'in son <c>release:</c> satırı onları geri alan
    /// komutları verir. Test o komutları ÇALIŞTIRIR: develop ve main yayının başladığı commit'e döner, tag silinir, CHANGELOG bölümü
    /// ve <c>Version</c> çalışma ağacında değişiklik olarak kalır (yazılmış not kaybolmaz). Komutlardan ÖNCE bir <c>fetch</c> koşar:
    /// kullanıcı araya fetch/pull sokabilir ve o zaman isimler (<c>origin/develop</c>, <c>origin/main</c>) yarışan commit'e kayar —
    /// satırdaki sha'lar kaymaz. Sandbox'ta develop main'in önünde olduğundan yer değiştirmiş sha'lar da, isimli bir kurtarma da
    /// burada kırmızı verir.</para>
    /// <para><b>Değişen kural (kullanıcı kararı 2026-09-30: günlük iş develop'ta, main yalnız sürümler):</b> eski iddia "origin/main
    /// ilerlerse main de tag de gitmez" idi — iki ref vardı, kurtarma elle yazılırdı (<c>git reset --soft origin/main</c>). Artık üç
    /// ref gider, develop'un ilerlemesi asıl olası yarıştır ve kurtarma komutlarını script verir.</para></summary>
    [SkippableTheory]
    [InlineData("develop")]
    [InlineData("main")]
    public void A_branch_that_moved_before_the_push_leaves_nothing_on_origin_and_the_printed_undo_restores_the_clone(string moved)
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string developBefore = box.OriginDevelop, mainBefore = box.OriginMain;
        Assert.NotEqual(mainBefore, developBefore); // sha'lar ayrışır: kurtarma satırında yer değiştirmeleri görünür
        box.AdvanceOriginRightAfterTheNextCommit(moved);

        var r = RunRelease(box);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal(box.OtherHead, box.Git(box.Origin, "rev-parse", moved).Trim()); // yarış gerçekten kuruldu
        if (moved == "develop") Assert.Equal(mainBefore, box.OriginMain);             // öteki branch de gitmedi
        else Assert.Equal(developBefore, box.OriginDevelop);
        Assert.Equal("", box.OriginTags);                                             // tag origin'e ULAŞMADI (atomik push)

        box.Git(box.Work, "fetch", "-q", "origin"); // kurtarmadan önce araya giren fetch: isim yarışan commit'e kayar
        Assert.Equal(box.OtherHead, box.Git(box.Work, "rev-parse", "refs/remotes/origin/" + moved).Trim());
        var undo = RunCommand(UndoCommands(r.Output), box.Work);

        Assert.True(box.CurrentBranch == "develop", undo.Output);
        Assert.Equal(developBefore, box.LocalDevelop);
        Assert.Equal(mainBefore, box.LocalMain);
        Assert.Equal("", box.Git(box.Work, "tag", "--list").Trim());
        Assert.Equal(new[] { " M CHANGELOG.md", " M Directory.Build.props" }, box.StatusLines());
        Assert.Contains($"## [{box.NextVersion}]", File.ReadAllText(box.ChangelogPath), StringComparison.Ordinal);
        Assert.Contains($"<Version>{box.NextVersion}</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal);
    }

    /// <summary>Kusur: push sıfırdan farklı döndüğünde script "--atomic: nothing reached origin" diyor ve kurtarma satırını
    /// basıyordu. Oysa sunucu atomik güncellemeyi uyguladıktan sonra bağlantı koparsa git yine hata döner ve yayın origin'dedir:
    /// kurtarma yerel develop/main/tag'i geri alır, ardından <c>git pull --ff-only</c> çalışma ağacındaki CHANGELOG/props
    /// değişikliğine çarpar. Script "hiçbir şey gitmedi" demeden origin'e tag'i sorar (<c>git ls-remote</c>); tag oradaysa bunu
    /// söyler ve kurtarma satırı BASMAZ. Gölge <c>git</c> push'u gerçekten yapar, yalnız çıkış kodunu bozar; tag sondası gerçek
    /// origin'e sorar.</summary>
    [SkippableFact]
    public void A_push_that_errs_after_origin_took_the_release_says_so_and_prints_no_undo()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");

        var r = RunRelease(box, gitShadow: PushAppliesButReportsAnError);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim()); // yayın gerçekten origin'de
        Assert.Equal(box.LocalMain, box.OriginMain);
        Assert.Equal(box.LocalDevelop, box.OriginDevelop);
        Assert.Contains("but origin has v1.8.0", r.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing reached origin", r.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("release: undo", r.Output, StringComparison.Ordinal);
    }

    /// <summary>Push düştükten sonra origin OKUNAMAZSA (ağ gitti) script "hiçbir şey gitmedi" diyemez: <c>ls-remote</c> hatası
    /// "tag yok" sayılmaz (guard'lardaki kural). Kurtarma satırı yine son satırdır (büyük olasılıkla push gitmemiştir), ama
    /// üstündeki satır önce origin'de tag'in olmadığını görmeyi söyler. Gölge <c>git</c> push'u hiç göndermez ve push'tan sonraki
    /// <c>ls-remote</c>'u düşürür.</summary>
    [SkippableFact]
    public void A_failed_push_whose_outcome_cannot_be_read_asks_to_check_origin_before_the_undo()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string originBefore = box.OriginRefs;

        var r = RunRelease(box, gitShadow: PushFailsAndOriginCannotBeRead);

        Assert.Equal(1, r.ExitCode);
        Assert.Equal(originBefore, box.OriginRefs); // push gitmedi
        Assert.Contains("origin cannot be read", r.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing reached origin", r.Output, StringComparison.Ordinal);
        Assert.Contains("git reset --soft", UndoCommands(r.Output), StringComparison.Ordinal);
    }

    /// <summary>Temiz yayın: develop'taki <c>release: vX</c> commit'i main'e <c>--no-ff</c> merge edilir (<c>merge: release vX</c>),
    /// annotated tag o merge commit'ine konur, develop main'e ilerler (develop == main) ve üç ref birlikte origin'e gider. main'in
    /// ilk ebeveyni önceki sürüm, ikincisi develop'un release commit'idir — o da develop'un yayından önceki ucunun üstündedir
    /// (son yayından beri develop'a giren iş main'e geçer); oturum develop'ta, ağaç temiz biter.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-09-30: günlük iş develop'ta, main yalnız sürümler):</b> eski iddia "release
    /// commit'i main'de atılır, tag o commit'tedir, main ve tag birlikte push edilir" idi. main'e artık yalnız bu script dokunur ve
    /// main'deki her commit bir sürümün merge'üdür.</para></summary>
    [SkippableFact]
    public void A_clean_release_merges_develop_into_main_tags_the_merge_and_leaves_develop_equal_to_main()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string mainBefore = box.OriginMain, developBefore = box.OriginDevelop;
        Assert.NotEqual(mainBefore, developBefore); // develop'ta main'de olmayan iş var: ebeveynler ayırt edilir

        var r = RunRelease(box);

        Assert.True(r.ExitCode == 0, r.Output);
        string main = box.OriginMain;
        Assert.Equal("merge: release v1.8.0", box.Git(box.Origin, "log", "-1", "--format=%s", "main").Trim());
        Assert.Equal(mainBefore, box.Git(box.Origin, "rev-parse", "main^1").Trim());                    // ilk ebeveyn: önceki sürüm
        Assert.Equal("release: v1.8.0", box.Git(box.Origin, "log", "-1", "--format=%s", "main^2").Trim()); // ikinci: develop'un commit'i
        Assert.Equal(developBefore, box.Git(box.Origin, "rev-parse", "main^2^1").Trim());               // ...develop'un ucunda atıldı
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim());          // annotated: tag nesnesi
        Assert.Equal(main, box.Git(box.Origin, "rev-parse", "refs/tags/v1.8.0^{commit}").Trim());        // tag merge commit'inde
        Assert.Equal(main, box.OriginDevelop);                                                            // develop == main
        Assert.Equal(main, box.LocalDevelop);
        Assert.Equal(main, box.LocalMain);
        Assert.Equal("develop", box.CurrentBranch);
        Assert.Empty(box.StatusLines());
        Assert.Contains("<Version>1.8.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal);
        Assert.Contains("https://github.com/sdemir60/app_build_orchestrator/actions", r.Output, StringComparison.Ordinal);
    }

    /// <summary>Yayın develop'tan çıkar: başka bir branch'te (burada main) koşan script hiçbir şeye dokunmadan durur.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-09-30: günlük iş develop'ta, main yalnız sürümler):</b> eski kural "yalnız
    /// main'de koşar" idi (<c>not on main.</c>) ve release commit'i main'de atılırdı. main'e artık yalnız bu script'in merge'ü girer;
    /// main'de başlayan bir koşu bu modeli delerdi.</para></summary>
    [SkippableFact]
    public void The_release_script_refuses_to_run_anywhere_but_develop()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.Git(box.Work, "switch", "-q", "main"); // bekleyen CHANGELOG bölümü de gelir (iş commit'i CHANGELOG'a dokunmaz)
        string before = box.Snapshot();

        var r = RunRelease(box);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("release runs from develop", r.Output, StringComparison.Ordinal);
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary>Yayın, CI'ın yeşil gördüğü commit'ten çıkar: develop HEAD'inin <c>ci.yml</c> koşusu yoksa (push edilmemiş, CI'ı
    /// tetiklenmemiş), bitmemişse ya da başarısızsa script hiçbir şeye dokunmadan durur; mesaj durumu ve çıkışı
    /// (<c>-SkipCiCheck</c>) söyler. Gölge API yalnız develop'un commit'ini sorana cevap verir (<see cref="CiRun"/>).</summary>
    [SkippableTheory]
    [InlineData(null, null, "no run")]
    [InlineData("completed", "failure", "failure")]
    [InlineData("in_progress", null, "in_progress")]
    public void The_release_script_refuses_a_develop_whose_CI_run_is_not_green(string? status, string? conclusion, string shown)
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string before = box.Snapshot();

        var r = RunRelease(box, ciShadow: status is null ? NoCiRuns : CiRun(box.LocalDevelop, status, conclusion));

        Assert.Equal(1, r.ExitCode);
        Assert.Contains($"develop's CI run is not green ({shown})", r.Output, StringComparison.Ordinal);
        Assert.Contains("-SkipCiCheck", r.Output, StringComparison.Ordinal);
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary>develop'un CI koşusu SORULAMAZSA (ağ yok, API hatası, oran sınırı) yayın çıkmaz — doğrulanamayan yayın çıkmaz
    /// (<c>git ls-remote</c> hatasının "tag yok" sayılmaması gibi); mesaj sebebi ve çıkışı (<c>-SkipCiCheck</c>) söyler.</summary>
    [SkippableFact]
    public void The_release_script_refuses_when_develops_CI_run_cannot_be_read()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string before = box.Snapshot();

        var r = RunRelease(box, ciShadow: NetworkForbidden);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains("cannot read develop's CI run", r.Output, StringComparison.Ordinal);
        Assert.Contains("-SkipCiCheck", r.Output, StringComparison.Ordinal);
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary><c>-SkipCiCheck</c> (çevrimdışı / acil durum) develop'un CI'ını HİÇ sormaz — ağa çıkan her çağrı düşecek şekilde
    /// gölgelenir — ve bunu çıktıda söyler; yayın sonuna kadar gider (release.yml tag'i yine derleyip test eder).</summary>
    [SkippableFact]
    public void SkipCiCheck_releases_without_asking_the_api_and_says_so()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");

        var r = RunRelease(box, ciShadow: NetworkForbidden, switches: "-SkipTests -SkipCiCheck");

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.DoesNotContain("NETWORK-TOUCHED", r.Output, StringComparison.Ordinal);
        Assert.Contains("CI run was not checked", r.Output, StringComparison.Ordinal);
        Assert.Equal(box.OriginMain, box.OriginDevelop);
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim());
    }

    /// <summary>Akışın git ön koşulları yazmadan ÖNCE denetlenir; tutmazsa script hiçbir şeye dokunmadan durur:
    /// <list type="bullet">
    /// <item><c>develop</c> ve <c>main</c> origin'de var: fetch budar (<c>--prune</c>) — origin'de silinmiş bir branch'in uzak izleme
    /// ref'i klonda kalsaydı guard onu var sayar ve yayın silinmiş branch'i yeniden yaratırdı.</item>
    /// <item><c>develop</c> = <c>origin/develop</c> (fetch sonrası): yayın origin'deki ve CI'ın gördüğü commit'ten çıkar.</item>
    /// <item>yerel <c>main</c> (varsa) = <c>origin/main</c>: main'i yalnız yayın ilerletir; farklıysa elle dokunulmuştur.</item>
    /// <item><c>origin/main</c> develop'un atası: main develop'un tamamını alır, fazlasını değil — değilse merge, build'in hiç
    /// görmediği bir ağaç üretirdi (çakışırsa main'de yarım bir merge kalırdı).</item>
    /// <item><c>main</c> başka bir worktree'de açık değil: akış <c>git switch main</c> yapar; açıksa bu, release commit'inden SONRA
    /// düşerdi. <c>git worktree list</c> düşerse guard sessizce geçmez (doğrulanamayan yayın çıkmaz).</item>
    /// </list></summary>
    [SkippableTheory]
    [InlineData("develop-missing-on-origin", "origin has no develop branch")]
    [InlineData("main-missing-on-origin", "origin has no main branch")]
    [InlineData("develop-behind-origin", "develop and origin/develop differ")]
    [InlineData("local-main-moved", "main and origin/main differ")]
    [InlineData("main-not-in-develop", "origin/main has commits develop does not have")]
    [InlineData("main-in-another-worktree", "main is checked out in")]
    [InlineData("worktree-list-fails", "cannot check where main is checked out")]
    public void The_release_script_refuses_a_develop_or_main_out_of_step_before_touching_anything(string scenario, string message)
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        string gitShadow = "";
        switch (scenario)
        {
            case "develop-missing-on-origin":
                box.DeleteOnOrigin("develop");
                break;
            case "main-missing-on-origin":
                box.DeleteOnOrigin("main");
                break;
            case "develop-behind-origin":
                box.AdvanceOrigin("develop");
                break;
            case "local-main-moved":
                box.Git(box.Work, "branch", "-f", "main", box.Git(box.Work, "commit-tree", "HEAD^{tree}", "-p", "HEAD", "-m", "local").Trim());
                break;
            case "main-not-in-develop":
                box.AdvanceOrigin("main");
                box.Git(box.Work, "fetch", "-q", "origin");
                box.Git(box.Work, "branch", "-f", "main", "origin/main"); // yerel main origin'le eşit: yalnız ata guard'ı tutmaz
                break;
            case "main-in-another-worktree":
                box.CheckOutMainInAnotherWorktree();
                break;
            case "worktree-list-fails":
                gitShadow = WorktreeListFails;
                break;
        }
        string before = box.Snapshot();

        var r = RunRelease(box, gitShadow: gitShadow);

        Assert.Equal(1, r.ExitCode);
        Assert.Contains(message, r.Output, StringComparison.Ordinal);
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary>Yerel <c>main</c>'i olmayan bir klon (yalnız develop'la çalışan) da yayın çıkarır: script <c>main</c>'i
    /// <c>origin/main</c>'i izleyen bir branch olarak yazma evresinde açar (prova açmaz — bkz.
    /// <see cref="A_dry_run_runs_every_guard_and_writes_nothing"/>).</summary>
    [SkippableFact]
    public void A_clone_without_a_local_main_releases_through_a_tracking_branch()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.Git(box.Work, "branch", "-q", "-D", "main");

        var r = RunRelease(box);

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal(box.OriginMain, box.LocalMain);
        Assert.Equal("origin/main", box.Git(box.Work, "rev-parse", "--abbrev-ref", "main@{upstream}").Trim());
    }

    /// <summary><c>-DryRun</c> gerçek koşunun durduğu HER yerde durur ve hiçbir şey yazmaz: CI kırmızıyken reddeder; her şey
    /// tutarken "guards passed (dry run)" der ve props, yerel branch/tag'ler (eksik yerel main dahil — açılmaz) ve origin aynı kalır.
    /// Yazdığı tek şey fetch'in uzak izleme ref'leridir.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-09-30):</b> eski prova yalnız CHANGELOG guard'larını koşardı (branch, fetch,
    /// tag ve sonda öncesinde çıkardı). Yeni akışın ön koşullarının çoğu git ve CI durumudur (develop güncel mi, CI yeşil mi);
    /// yalnız CHANGELOG'a bakan bir prova "geçer" deyip gerçek koşuyu düşürürdü.</para></summary>
    [SkippableFact]
    public void A_dry_run_runs_every_guard_and_writes_nothing()
    {
        RequirePowerShell();
        using var box = new ReleaseSandbox("1.8.0");
        box.Git(box.Work, "branch", "-q", "-D", "main");
        string before = box.Snapshot();

        var red = RunRelease(box, ciShadow: CiRun(box.LocalDevelop, "completed", "failure"), switches: "-DryRun");
        Assert.Equal(1, red.ExitCode);
        Assert.Contains("develop's CI run is not green (failure)", red.Output, StringComparison.Ordinal);

        var green = RunRelease(box, switches: "-DryRun");
        Assert.True(green.ExitCode == 0, green.Output);
        Assert.Contains("guards passed for 1.8.0 (dry run)", green.Output, StringComparison.Ordinal);
        Assert.Equal(before, box.Snapshot());
    }

    /// <summary>Kusur: release.yml'in guard'ı yalnız tag == Version == CHANGELOG eşitliğine bakıyordu; elle itilen ve
    /// origin/main'de OLMAYAN bir commit'e duran tag (bir iş branch'inden, push edilmemiş bir denemeden) aynı eşitliği
    /// taşırsa yayın çıkardı. <c>-RequireOnMain</c> (CI'ın kipi) HEAD'in origin/main'in atası olmasını da ister. Yerel
    /// <c>release.ps1</c> bu anahtarı vermez: orada guard release commit'inden önce koşar. develop'a push edilmiş ama main'e
    /// girmemiş bir commit de yayın çıkarmaz: main yalnız sürümleri taşır, tag release.ps1'in main'deki merge'ündedir. Ata
    /// sorulamazsa (<c>origin/main</c> yok) da reddeder — üç durum <c>Test-GitAncestor</c>'dadır.</summary>
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

        box.Git(box.Work, "push", "-q", "origin", "develop"); // develop'ta ama main'de değil: yine yayın yok
        Assert.Equal(1, RunIn(box.Work, box.GuardScript, "-Tag", tag, "-RequireOnMain").ExitCode);

        box.Git(box.Work, "push", "-q", "origin", "HEAD:main"); // commit artık origin/main'de
        var accepted = RunIn(box.Work, box.GuardScript, "-Tag", tag, "-RequireOnMain");
        Assert.True(accepted.ExitCode == 0, accepted.Output);

        box.Git(box.Work, "update-ref", "-d", "refs/remotes/origin/main"); // ata sorulamaz (tarihçesi getirilmemiş checkout gibi)
        var unknown = RunIn(box.Work, box.GuardScript, "-Tag", tag, "-RequireOnMain");
        Assert.Equal(1, unknown.ExitCode);                                  // sorulamayan ata "ata" sayılmaz
        Assert.Contains("cannot check the tagged commit against origin/main", unknown.Output, StringComparison.Ordinal);
    }

    /// <summary>release.ps1'in gerçek git akışı için izole ortam: bare origin (<c>main</c> ve onun bir commit önündeki
    /// <c>develop</c> — son yayından beri develop'a iş girmiş olağan hal; iş commit'i CHANGELOG'a ve props'a dokunmaz) +
    /// <c>develop</c>'ta duran çalışma klonu (yerel <c>main</c>'i de vardır; script'ler ve asgari props/CHANGELOG/slnx içerir;
    /// script kökü kendi konumundan çıkarır) + origin'i "başka biri" gibi ilerleten ikinci klon. Yeni sürümün CHANGELOG bölümü
    /// commit EDİLMEMİŞ değişikliktir (gerçek akışta Claude yazar, script commit'ler). Gerçek repoya dokunulmaz;
    /// <see cref="GitTestRepo.RunGitAt"/> kullanılır.
    /// <para><b>Değişen model (kullanıcı kararı 2026-09-30):</b> eskiden origin'de yalnız <c>main</c> vardı ve çalışma klonu
    /// <c>main</c>'deydi — yayın main'den çıkardı. Artık günlük iş develop'ta, main yalnız sürümleri taşır.</para>
    /// <para><b>Neden develop önde:</b> ilk kurulumda <c>main</c> ile <c>develop</c> aynı commit'teydi; iki branch'in sha'sı
    /// ayrışmadığı için yarış testinin kurtarma satırında sha'lar yer değiştirse ya da isimli ref'lere (<c>origin/develop</c>,
    /// <c>origin/main</c>) dönse de test yeşil kalıyordu (ölçüldü) ve develop'ta main'de olmayan iş uçtan uca hiç koşmuyordu.</para></summary>
    private sealed class ReleaseSandbox : IDisposable
    {
        private readonly TempDir _temp = new();

        public string Origin => Path.Combine(_temp.Path, "origin.git");
        public string Work => Path.Combine(_temp.Path, "work");
        public string Other => Path.Combine(_temp.Path, "other");
        public string ReleaseScript => Path.Combine(Work, "scripts", "release.ps1");
        public string GuardScript => Path.Combine(Work, "scripts", "release-guard.ps1");
        public string PropsPath => Path.Combine(Work, "Directory.Build.props");
        public string ChangelogPath => Path.Combine(Work, "CHANGELOG.md");
        /// <summary>CHANGELOG'un en üstüne (commit'siz) yazılan yeni sürüm.</summary>
        public string NextVersion { get; }
        public string OriginMain => Git(Origin, "rev-parse", "main").Trim();
        public string OriginDevelop => Git(Origin, "rev-parse", "develop").Trim();
        public string OriginTags => Git(Work, "ls-remote", "--tags", "origin").Trim();
        /// <summary>origin'in bütün ref'leri (branch + tag) ve gösterdikleri nesneler.</summary>
        public string OriginRefs => Git(Origin, "for-each-ref", "--format=%(refname) %(objectname)").Trim();
        public string WorkHead => Git(Work, "rev-parse", "HEAD").Trim();
        public string LocalDevelop => Git(Work, "rev-parse", "refs/heads/develop").Trim();
        public string LocalMain => Git(Work, "rev-parse", "refs/heads/main").Trim();
        public string CurrentBranch => Git(Work, "branch", "--show-current").Trim();
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
            File.WriteAllText(ChangelogPath, "# Changelog\n\n" + older);
            Git(Work, "add", "-A");
            Git(Work, "commit", "-q", "-m", "init");
            Git(Work, "push", "-q", "origin", "main");
            Git(Work, "switch", "-q", "-c", "develop"); // günlük iş develop'ta; yerel main de durur
            File.WriteAllText(Path.Combine(Work, "work.txt"), "work since the last release\n"); // CHANGELOG'a ve props'a dokunmaz
            Git(Work, "add", "work.txt");
            Git(Work, "commit", "-q", "-m", "feat: work since the last release");
            Git(Work, "push", "-q", "-u", "origin", "develop"); // origin: develop main'in bir commit önünde

            Git(_temp.Path, "clone", "-q", Origin, Other);
            Configure(Other);

            string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(ChangelogPath, $"# Changelog\n\n## [{nextVersion}] - {today}\n\n### Added\n- New entry.\n\n" + older);
        }

        /// <summary>"Hiçbir şeye dokunmadan durdu" iddiası için anlık görüntü: origin'in bütün ref'leri (branch + tag), çalışma
        /// klonunun yerel branch ve tag'leri, açık branch ve props'un metni. Guard'ların kendi <c>fetch</c>'i yalnız uzak izleme
        /// ref'lerini (<c>refs/remotes</c>) günceller; onlar görüntüye girmez.</summary>
        public string Snapshot() => string.Join("\n",
            "origin: " + OriginRefs,
            "work: " + Git(Work, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads", "refs/tags").Trim(),
            "branch: " + CurrentBranch,
            "props: " + File.ReadAllText(PropsPath));

        /// <summary><c>git status --porcelain</c> satırları, sıralı.</summary>
        public string[] StatusLines() => Git(Work, "status", "--porcelain").Split('\n')
            .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Order(StringComparer.Ordinal).ToArray();

        public string Git(string workingDirectory, params string[] args) => GitTestRepo.RunGitAt(workingDirectory, args);

        private void Configure(string repo)
        {
            GitTestRepo.ConfigureIdentity(repo); // kimlik tek yerde (GitTestRepo)
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

        /// <summary>İkinci klonda origin/<paramref name="branch"/>'in üstüne boş bir commit atıp onu iten git komutları — tek yer:
        /// hem doğrudan (<see cref="AdvanceOrigin"/>) hem hook'tan (<see cref="AdvanceOriginRightAfterTheNextCommit"/>) koşar.</summary>
        private static string[][] CompetingCommit(string branch) =>
        [
            ["fetch", "-q", "origin"],
            ["checkout", "-q", "--detach", "origin/" + branch],
            ["commit", "-q", "--allow-empty", "-m", "competing"],
            ["push", "-q", "origin", "HEAD:refs/heads/" + branch],
        ];

        /// <summary>İkinci klondan origin/<paramref name="branch"/>'e bir commit iter: "başka biri" o branch'i ilerletti.</summary>
        public void AdvanceOrigin(string branch)
        {
            foreach (string[] args in CompetingCommit(branch)) Git(Other, args);
        }

        /// <summary>Çalışma klonunda atılacak İLK commit'in hemen ardından ikinci klondan origin/<paramref name="branch"/>'e bir
        /// commit iter (release.ps1'de release commit'i, fetch'ten sonra ve push'tan önce gelir).</summary>
        public void AdvanceOriginRightAfterTheNextCommit(string branch)
        {
            string hook = "#!/bin/sh\n"
                + "unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_PREFIX\n" // hook'a git'in kendi ortamı geçer; öteki klonu bozmasın
                + $"cd \"{Other.Replace('\\', '/')}\" || exit 1\n"
                + string.Concat(CompetingCommit(branch).Select(args => "git " + string.Join(' ', args) + " || exit 1\n"));
            File.WriteAllText(Path.Combine(Work, ".git", "hooks", "post-commit"), hook);
        }

        /// <summary>origin'den <paramref name="branch"/>'i siler ("başka biri sildi"): çalışma klonu habersizdir, uzak izleme ref'i
        /// (<c>origin/&lt;branch&gt;</c>) klonda kalır. Bare repoda doğrudan silinir — origin'in HEAD'i <c>main</c>'dir ve git,
        /// HEAD'in branch'ini push'la sildirmez (<c>deletion of the current branch prohibited</c>).</summary>
        public void DeleteOnOrigin(string branch) => Git(Origin, "update-ref", "-d", "refs/heads/" + branch);

        /// <summary><c>main</c>'i sandbox içindeki ikinci bir worktree'de açar (çalışma klonu develop'ta kalır).</summary>
        public void CheckOutMainInAnotherWorktree() =>
            Git(Work, "worktree", "add", "-q", Path.Combine(_temp.Path, "main-worktree"), "main");

        public void Dispose()
        {
            // git nesne dosyaları salt-okunurdur; TempDir bunları silemez (ve assertion hatasını Dispose istisnasıyla örterdi).
            foreach (string file in Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            _temp.Dispose();
        }
    }
}
