using System;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Kök keşfi [D3]: kullanıcı harici projenin DİZİNİNİ verir, çalışma kopyasının kökü oradan yukarı
/// yürünerek bulunur. Kök ve VCS türü hiçbir yerde persist edilmez — her koşuda burada yeniden bulunur.
/// </summary>
public class VcsDetectorTests
{
    [Fact]
    public void Detect_finds_git_root_from_a_subdirectory()
    {
        using var repo = new GitTestRepo();
        string nested = Path.Combine(repo.RootPath, "src", "Mail", "deep");
        Directory.CreateDirectory(nested);

        var root = VcsDetector.DetectRoot(nested);

        Assert.Equal(VcsKind.Git, root.Kind);
        Assert.Equal(repo.RootPath, root.RootPath);
    }

    [Fact]
    public void Detect_reports_git_for_a_gitfile_worktree()
    {
        // Linked worktree'de .git bir DİZİN değil, gitdir'i gösteren bir DOSYADIR — dizin-only kontrol
        // burayı ıskalar ve kök arayışı sürücü köküne kadar yürürdü.
        using var temp = new TempDir();
        string workingCopy = Path.Combine(temp.Path, "wt-mail");
        Directory.CreateDirectory(Path.Combine(workingCopy, "src"));
        File.WriteAllText(Path.Combine(workingCopy, ".git"), "gitdir: D:/repo/.git/worktrees/wt-mail");

        var root = VcsDetector.DetectRoot(Path.Combine(workingCopy, "src"));

        Assert.Equal(VcsKind.Git, root.Kind);
        Assert.Equal(workingCopy, root.RootPath);
    }

    [Fact]
    public void Detect_finds_tfvc_root_from_a_subdirectory()
    {
        using var temp = new TempDir();
        string workspace = Path.Combine(temp.Path, "tfs-workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf")); // local workspace metadata dizini
        string project = Path.Combine(workspace, "Customer", "Ocr");
        Directory.CreateDirectory(project);

        var root = VcsDetector.DetectRoot(project);

        Assert.Equal(VcsKind.Tfvc, root.Kind);
        Assert.Equal(workspace, root.RootPath);
    }

    [Fact]
    public void Detect_prefers_the_nearest_marker_when_a_tfvc_workspace_sits_inside_a_git_repo()
    {
        // İç içe çalışma kopyalarında EN YAKIN işaret kazanır — yoksa git kökü TFVC projesini yutardı.
        using var repo = new GitTestRepo();
        string workspace = Path.Combine(repo.RootPath, "vendor", "tfs");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf"));
        string project = Path.Combine(workspace, "Ocr");
        Directory.CreateDirectory(project);

        var root = VcsDetector.DetectRoot(project);

        Assert.Equal(VcsKind.Tfvc, root.Kind);
        Assert.Equal(workspace, root.RootPath);
    }

    [Fact]
    public void Detect_reports_unknown_when_no_marker_up_to_drive_root()
    {
        using var temp = new TempDir();
        string project = Path.Combine(temp.Path, "loose", "Mail");
        Directory.CreateDirectory(project);

        var root = VcsDetector.DetectRoot(project);

        Assert.Equal(VcsKind.Unknown, root.Kind);
        Assert.Null(root.RootPath);
    }

    [Fact]
    public void Detect_reports_unknown_for_a_directory_that_does_not_exist()
    {
        var root = VcsDetector.DetectRoot(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(VcsKind.Unknown, root.Kind);
        Assert.Null(root.RootPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_reports_unknown_for_a_blank_directory(string startDirectory)
    {
        var root = VcsDetector.DetectRoot(startDirectory);

        Assert.Equal(VcsKind.Unknown, root.Kind);
        Assert.Null(root.RootPath);
    }
}
