using System;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Kök keşfi [D3 · design v1.14.0 §9]: kullanıcı yolu VE sürüm kontrol türünü verir; çalışma kopyasının kökü
/// yoldan yukarı yürünerek bulunur ama yalnız SEÇİLEN türün işareti aranır. Kök hiçbir yerde persist edilmez —
/// her koşuda burada yeniden bulunur.
/// </summary>
public class VcsDetectorTests
{
    [Fact]
    public void A_git_root_is_found_from_a_subdirectory()
    {
        using var repo = new GitTestRepo();
        string nested = Path.Combine(repo.RootPath, "src", "Mail", "deep");
        Directory.CreateDirectory(nested);

        Assert.Equal(repo.RootPath, VcsDetector.FindRoot(nested, VcsKind.Git));
    }

    [Fact]
    public void A_gitfile_worktree_counts_as_a_git_root()
    {
        // Linked worktree'de .git bir DİZİN değil, gitdir'i gösteren bir DOSYADIR — dizin-only kontrol
        // burayı ıskalar ve kök arayışı sürücü köküne kadar yürürdü.
        using var temp = new TempDir();
        string workingCopy = Path.Combine(temp.Path, "wt-mail");
        Directory.CreateDirectory(Path.Combine(workingCopy, "src"));
        File.WriteAllText(Path.Combine(workingCopy, ".git"), "gitdir: D:/repo/.git/worktrees/wt-mail");

        Assert.Equal(workingCopy, VcsDetector.FindRoot(Path.Combine(workingCopy, "src"), VcsKind.Git));
    }

    [Fact]
    public void A_tfvc_root_is_found_from_a_subdirectory()
    {
        using var temp = new TempDir();
        string workspace = Path.Combine(temp.Path, "tfs-workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf")); // local workspace metadata dizini
        string project = Path.Combine(workspace, "Customer", "Ocr");
        Directory.CreateDirectory(project);

        Assert.Equal(workspace, VcsDetector.FindRoot(project, VcsKind.Tfvc));
    }

    [Fact]
    public void Only_the_selected_kind_is_looked_for()
    {
        // Kullanıcı "TFVC" dediyse yolun üstündeki .git önemsizdir: git kökü TFVC projesini yutmaz —
        // ve tersi: git seçiliyken $tf hiç görülmez.
        using var repo = new GitTestRepo();
        string workspace = Path.Combine(repo.RootPath, "vendor", "tfs");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf"));
        string project = Path.Combine(workspace, "Ocr");
        Directory.CreateDirectory(project);

        Assert.Equal(workspace, VcsDetector.FindRoot(project, VcsKind.Tfvc));
        Assert.Equal(repo.RootPath, VcsDetector.FindRoot(project, VcsKind.Git));
    }

    [Fact]
    public void No_marker_of_the_selected_kind_up_to_the_drive_root_means_no_root()
    {
        using var temp = new TempDir();
        string project = Path.Combine(temp.Path, "loose", "Mail");
        Directory.CreateDirectory(project);

        Assert.Null(VcsDetector.FindRoot(project, VcsKind.Git));
        Assert.Null(VcsDetector.FindRoot(project, VcsKind.Tfvc));
    }

    [Fact]
    public void A_directory_that_does_not_exist_has_no_root()
        => Assert.Null(VcsDetector.FindRoot(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")), VcsKind.Git));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_directory_has_no_root(string startDirectory)
        => Assert.Null(VcsDetector.FindRoot(startDirectory, VcsKind.Git));
}
