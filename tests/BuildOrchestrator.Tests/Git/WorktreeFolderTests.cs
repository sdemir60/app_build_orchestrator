using System.IO;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [worktree] Ayardaki worktree klasörünün denetimi (<see cref="WorktreeFolder.Inspect"/>): Save'in ve alt bardaki anahtarın
/// TEK sorusu — klasör yoksa oluşturulabilir, bu reponun linked worktree'siyse geçilebilir, başka bir şeyse dokunulmaz.
///
/// <para>Denetim saf dosya okumasıdır (process yok): <c>.git</c> dosyası → gitdir → <c>commondir</c> ana reponun git
/// dizinine çıkmalıdır. Linked worktree'ler gerçek git ile kurulur (<see cref="GitTestRepo.AddWorktree"/>) — elle yazılmış
/// işaret dosyaları gerçek düzeni taklit etmekten öteye geçmezdi.</para>
/// </summary>
public class WorktreeFolderTests
{
    [Fact]
    public void A_folder_that_does_not_exist_can_be_created()
    {
        using var repo = new GitTestRepo();
        using var parent = new TempDir();

        Assert.Equal(WorktreeFolderState.Creatable,
            WorktreeFolder.Inspect(repo.RootPath, Path.Combine(parent.Path, "repo-worktree")));
    }

    [Fact]
    public void An_empty_folder_can_be_created()
    {
        using var repo = new GitTestRepo();
        using var folder = new TempDir();

        Assert.Equal(WorktreeFolderState.Creatable, WorktreeFolder.Inspect(repo.RootPath, folder.Path));
    }

    [Fact]
    public void A_linked_worktree_of_the_repository_is_linked()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "a");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");

        Assert.Equal(WorktreeFolderState.Linked, WorktreeFolder.Inspect(repo.RootPath, worktree));
    }

    /// <summary>[reftable] Ref'leri tablolarda tutan repoda da worktree'nin <c>.git</c> dosyası ve <c>commondir</c> aynıdır;
    /// denetim ref biçimine bakmaz.</summary>
    [Fact]
    public void A_linked_worktree_of_a_reftable_repository_is_linked()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "a");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");

        Assert.Equal(WorktreeFolderState.Linked, WorktreeFolder.Inspect(repo.RootPath, worktree));
    }

    [Fact]
    public void A_worktree_of_another_repository_is_foreign()
    {
        using var repo = new GitTestRepo();
        using var other = new GitTestRepo();
        other.WriteFile("a.txt", "a");
        other.CommitAll("first");
        other.CreateBranch("side");
        string othersWorktree = other.AddWorktree("side");

        Assert.Equal(WorktreeFolderState.Foreign, WorktreeFolder.Inspect(repo.RootPath, othersWorktree));
    }

    [Fact]
    public void A_separate_clone_is_foreign()
    {
        using var repo = new GitTestRepo();
        using var other = new GitTestRepo();

        Assert.Equal(WorktreeFolderState.Foreign, WorktreeFolder.Inspect(repo.RootPath, other.RootPath));
    }

    [Fact]
    public void A_folder_with_files_is_foreign()
    {
        using var repo = new GitTestRepo();
        using var folder = new TempDir();
        File.WriteAllText(Path.Combine(folder.Path, "notes.txt"), "mine");

        Assert.Equal(WorktreeFolderState.Foreign, WorktreeFolder.Inspect(repo.RootPath, folder.Path));
    }

    /// <summary>Repo kökünün içindeki bir worktree'yi keşif ana kopyanın taramasında da bulur — projeler iki kez görünürdü.</summary>
    [Fact]
    public void A_folder_inside_the_repository_is_nested()
    {
        using var repo = new GitTestRepo();

        Assert.Equal(WorktreeFolderState.Nested,
            WorktreeFolder.Inspect(repo.RootPath, Path.Combine(repo.RootPath, "worktrees", "side")));
    }

    [Fact]
    public void The_repository_itself_is_nested_whatever_the_case_and_trailing_separator()
    {
        using var repo = new GitTestRepo();

        Assert.Equal(WorktreeFolderState.Nested,
            WorktreeFolder.Inspect(repo.RootPath, repo.RootPath.ToUpperInvariant() + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void A_folder_that_holds_the_repository_is_nested()
    {
        using var repo = new GitTestRepo();

        Assert.Equal(WorktreeFolderState.Nested,
            WorktreeFolder.Inspect(repo.RootPath, Path.GetDirectoryName(repo.RootPath)!));
    }

    /// <summary>Yan yana duran ve adı repo adıyla başlayan klasör (<c>D:\src\acme</c> → <c>D:\src\acme-worktree</c>) iç içe
    /// değildir: önek bir yol bileşeni değil, metindir.</summary>
    [Fact]
    public void A_sibling_whose_name_starts_with_the_repository_name_is_not_nested()
    {
        using var repo = new GitTestRepo();
        string sibling = repo.RootPath + "-worktree";

        Assert.Equal(WorktreeFolderState.Creatable, WorktreeFolder.Inspect(repo.RootPath, sibling));
    }

    [Theory]
    [InlineData(@"acme-worktree")]
    [InlineData(@"..\acme-worktree")]
    [InlineData(@"\src\acme-worktree")]
    [InlineData(@"D:acme-worktree")]
    public void A_path_that_is_not_fully_qualified_is_not_rooted(string folder)
    {
        using var repo = new GitTestRepo();

        Assert.Equal(WorktreeFolderState.NotRooted, WorktreeFolder.Inspect(repo.RootPath, folder));
    }

    [Fact]
    public void A_repository_root_without_git_has_no_repository()
    {
        using var root = new TempDir();
        using var parent = new TempDir();

        Assert.Equal(WorktreeFolderState.NoRepository,
            WorktreeFolder.Inspect(root.Path, Path.Combine(parent.Path, "acme-worktree")));
    }

    // ---------------------------------------------------------------- GitDirectory.ResolveCommon

    [Fact]
    public void The_common_directory_of_a_linked_worktree_is_the_repository_git_directory()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "a");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");

        string common = GitDirectory.ResolveCommon(GitDirectory.Resolve(worktree)!);

        Assert.Equal(Path.GetFullPath(Path.Combine(repo.RootPath, ".git")), Path.TrimEndingDirectorySeparator(common),
            ignoreCase: true);
    }

    [Fact]
    public void The_common_directory_of_a_plain_repository_is_its_own_git_directory()
    {
        using var repo = new GitTestRepo();
        string gitDir = GitDirectory.Resolve(repo.RootPath)!;

        Assert.Equal(gitDir, GitDirectory.ResolveCommon(gitDir));
    }
}
