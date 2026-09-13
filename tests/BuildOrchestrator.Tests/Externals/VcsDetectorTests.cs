using System;
using System.IO;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Kök keşfi [D3 · design v1.14.0 §9]: kullanıcı yolu verir, çalışma kopyasının kökü yoldan yukarı yürünerek
/// bulunur. Kök hiçbir yerde persist edilmez — her koşuda burada yeniden bulunur.
///
/// <para><b>Eski iddia:</b> çağrı bir de sürüm kontrol TÜRÜ alıyordu ve aranan işaret o seçime göre
/// <c>.git</c> ya da <c>$tf</c> oluyordu; ayrı testler "TFVC seçiliyken üstteki <c>.git</c> önemsizdir" ve
/// "<c>$tf</c> bir TFVC köküdür" kurallarını pinliyordu. TFVC kolu tümden kaldırıldı (kullanıcı kararı):
/// aranan tek işaret <c>.git</c>, dolayısıyla tür parametresi de yok.</para>
/// </summary>
public class VcsDetectorTests
{
    [Fact]
    public void A_git_root_is_found_from_a_subdirectory()
    {
        using var repo = new GitTestRepo();
        string nested = Path.Combine(repo.RootPath, "src", "Mail", "deep");
        Directory.CreateDirectory(nested);

        Assert.Equal(repo.RootPath, VcsDetector.FindRoot(nested));
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

        Assert.Equal(workingCopy, VcsDetector.FindRoot(Path.Combine(workingCopy, "src")));
    }

    /// <summary>EN YAKIN kök kazanır: iç içe çalışma kopyalarında üstteki repo alttakini yutmaz.</summary>
    [Fact]
    public void The_nearest_git_root_wins_over_an_outer_one()
    {
        using var outer = new GitTestRepo();
        string inner = Path.Combine(outer.RootPath, "vendor", "mail");
        Directory.CreateDirectory(Path.Combine(inner, ".git"));
        string project = Path.Combine(inner, "src");
        Directory.CreateDirectory(project);

        Assert.Equal(inner, VcsDetector.FindRoot(project));
    }

    /// <summary>
    /// Bir <c>$tf</c> dizini artık HİÇBİR anlam taşımaz — TFVC kolu kaldırıldıktan sonra o klasör sıradan bir
    /// klasördür ve arama onu görmeden sürücü köküne kadar yürür.
    /// </summary>
    [Fact]
    public void A_tfvc_metadata_folder_is_no_longer_a_root()
    {
        using var temp = new TempDir();
        string workspace = Path.Combine(temp.Path, "tfs-workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf"));
        string project = Path.Combine(workspace, "Customer", "Ocr");
        Directory.CreateDirectory(project);

        Assert.Null(VcsDetector.FindRoot(project));
    }

    [Fact]
    public void No_git_marker_up_to_the_drive_root_means_no_root()
    {
        using var temp = new TempDir();
        string project = Path.Combine(temp.Path, "loose", "Mail");
        Directory.CreateDirectory(project);

        Assert.Null(VcsDetector.FindRoot(project));
    }

    [Fact]
    public void A_directory_that_does_not_exist_has_no_root()
        => Assert.Null(VcsDetector.FindRoot(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_directory_has_no_root(string startDirectory)
        => Assert.Null(VcsDetector.FindRoot(startDirectory));
}
