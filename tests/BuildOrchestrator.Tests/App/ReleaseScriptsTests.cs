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
        var (code, output) = Run("package.ps1", "-DownloadPrevious", "-ReleaseCount", "0", "-WhatIf");
        Assert.True(code == 0, output);
        Assert.Contains("no previous release", output, StringComparison.Ordinal);
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
