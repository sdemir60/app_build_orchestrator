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
}
