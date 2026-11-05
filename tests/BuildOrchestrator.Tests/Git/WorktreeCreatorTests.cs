using System.IO;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [worktree] <see cref="WorktreeCreator"/> — Settings Save'in kurduğu worktree. Kodun tek <c>worktree</c> yazımıdır ve
/// <c>RepositoryWriter.cs</c>'te yaşar (mutasyon yüzeyi tek dosya). Gerçek <c>git.exe</c> ile koşar (D8).
///
/// <para>Kural: yalnız yok ya da boş klasör oluşturulur (<see cref="WorktreeFolder.Inspect"/> → <c>Creatable</c>); worktree
/// ana kopyanın HEAD commit'inde <b>detached</b> doğar — yeni branch açılmaz, branch sonra chip'ten seçilir. Başka her durumda
/// klasöre dokunulmaz ve neden veri olarak döner.</para>
/// </summary>
public class WorktreeCreatorTests
{
    private static WorktreeCreator Creator(string repositoryRoot) => new(new ProcessRunner(), repositoryRoot);

    private static string HeadOf(string root) => GitTestRepo.RunGitAt(root, "rev-parse", "HEAD").Trim();

    private static GitTestRepo RepoWithCommit()
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        return repo;
    }

    private static int WorktreeCount(string root) =>
        GitTestRepo.RunGitAt(root, "worktree", "list", "--porcelain").Split('\n').Count(l => l.StartsWith("worktree ", StringComparison.Ordinal));

    [Fact]
    public async Task A_missing_folder_becomes_a_detached_worktree_at_the_repository_head()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");

        var result = await Creator(repo.RootPath).CreateAsync(folder);

        Assert.True(result.Created, result.Detail);
        Assert.Equal(HeadOf(repo.RootPath), result.Revision);
        Assert.Equal(WorktreeFolderState.Linked, WorktreeFolder.Inspect(repo.RootPath, folder));
        Assert.Equal(HeadOf(repo.RootPath), HeadOf(folder));
        Assert.NotEqual(0, GitTestRepo.RunGitAllowingFailure(folder, "symbolic-ref", "-q", "HEAD")); // detached: branch yok
        Assert.Equal("one", File.ReadAllText(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public async Task An_empty_folder_is_used()
    {
        using var repo = RepoWithCommit();
        using var folder = new TempDir();

        var result = await Creator(repo.RootPath).CreateAsync(folder.Path);

        Assert.True(result.Created, result.Detail);
        Assert.Equal(WorktreeFolderState.Linked, WorktreeFolder.Inspect(repo.RootPath, folder.Path));
    }

    /// <summary>Dolu bir klasöre git hiç çağrılmaz: içerik olduğu gibi kalır, repoya worktree eklenmez.</summary>
    [Fact]
    public async Task A_folder_with_files_is_left_alone()
    {
        using var repo = RepoWithCommit();
        using var folder = new TempDir();
        File.WriteAllText(Path.Combine(folder.Path, "notes.txt"), "mine");

        var result = await Creator(repo.RootPath).CreateAsync(folder.Path);

        Assert.False(result.Created);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Equal(new[] { "notes.txt" }, Directory.GetFileSystemEntries(folder.Path).Select(p => Path.GetFileName(p)));
        Assert.Equal(1, WorktreeCount(repo.RootPath));
    }

    [Fact]
    public async Task A_folder_inside_the_repository_is_refused()
    {
        using var repo = RepoWithCommit();
        string folder = Path.Combine(repo.RootPath, "inner");

        var result = await Creator(repo.RootPath).CreateAsync(folder);

        Assert.False(result.Created);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(1, WorktreeCount(repo.RootPath));
    }

    /// <summary>git'in kendi reddi (commit'siz repoda HEAD yok) exception değil veridir: Detail git'in anlatımını taşır.</summary>
    [Fact]
    public async Task A_git_failure_comes_back_as_data()
    {
        using var repo = new GitTestRepo(); // commit yok — HEAD çözülmez
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");

        var result = await Creator(repo.RootPath).CreateAsync(folder);

        Assert.False(result.Created);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Null(result.Revision);
    }
}
