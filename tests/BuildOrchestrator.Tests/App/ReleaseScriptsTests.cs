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
    /// aynı adlı cmdlet'i gölgeler — ağ çağrısı (Invoke-RestMethod) script'e dokunmadan taklit edilir.</summary>
    private static (int ExitCode, string Output) RunCommand(string command)
    {
        var psi = NewPowerShell(RepoPaths.RepoRoot);
        psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(command);
        return Execute(psi);
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
    /// -ReleaseCount'u VERMEZ.</summary>
    [SkippableFact]
    public void An_empty_release_list_from_the_api_is_counted_as_no_release()
    {
        RequirePowerShell();
        string script = Path.Combine(Scripts, "package.ps1");
        var (code, output) = RunCommand(
            $"function Invoke-RestMethod {{ '[]' | ConvertFrom-Json }}; & '{script}' -DownloadPrevious -WhatIf");
        Assert.True(code == 0, output);
        Assert.Contains("no previous release", output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void A_release_list_with_an_entry_from_the_api_still_downloads_the_previous_package()
    {
        RequirePowerShell();
        string script = Path.Combine(Scripts, "package.ps1");
        var (code, output) = RunCommand(
            $"function Invoke-RestMethod {{ '[{{\"tag_name\":\"v1.7.0\"}}]' | ConvertFrom-Json }}; & '{script}' -DownloadPrevious -WhatIf");
        Assert.True(code == 0, output);
        Assert.DoesNotContain("no previous release", output, StringComparison.Ordinal);
        Assert.Contains("vpk download github", output, StringComparison.Ordinal); // -WhatIf: çalıştırılmaz, yalnız yazılır
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

        var r = RunIn(box.Work, box.ReleaseScript, "-Version", "1.8.0", "-SkipTests");

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

        var r = RunIn(box.Work, box.ReleaseScript, "-Version", "1.8.0", "-SkipTests");

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

        var r = RunIn(box.Work, box.ReleaseScript, "-Version", "1.8.0", "-SkipTests");

        Assert.True(r.ExitCode == 0, r.Output);
        Assert.Equal(box.WorkHead, box.OriginMain);
        Assert.Equal("release: v1.8.0", box.Git(box.Origin, "log", "-1", "--format=%s", "main").Trim());
        Assert.Equal("tag", box.Git(box.Origin, "cat-file", "-t", "refs/tags/v1.8.0").Trim()); // annotated: tag nesnesi
        Assert.Contains("<Version>1.8.0</Version>", File.ReadAllText(box.PropsPath), StringComparison.Ordinal);
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
        public string PropsPath => Path.Combine(Work, "Directory.Build.props");
        public string OriginMain => Git(Origin, "rev-parse", "main").Trim();
        public string WorkHead => Git(Work, "rev-parse", "HEAD").Trim();
        public string OtherHead => Git(Other, "rev-parse", "HEAD").Trim();

        public ReleaseSandbox(string nextVersion)
        {
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
