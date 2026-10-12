using System.IO;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [Faz 2/T1] <see cref="HeadReader.Read"/>: branch + loose sha, <c>git pack-refs --all</c> sonrası
/// packed sha, detached, unborn, linked worktree (gerçek <c>git worktree add</c> — yalnız fixture) ve HEAD'i dosyadan
/// okunamayan reftable repo.
/// </summary>
public class HeadReaderTests
{
    [Fact]
    public void Read_returns_branch_and_loose_sha_on_normal_repo()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();

        string? gitDir = GitDirectory.Resolve(repo.RootPath);
        Assert.NotNull(gitDir);
        var state = HeadReader.Read(gitDir!);

        Assert.NotNull(state);
        Assert.Equal(branch, state!.Branch);
        Assert.Equal(sha, state.Sha);
    }

    [Fact]
    public void Read_falls_back_to_packed_refs_when_loose_ref_missing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        GitTestRepo.RunGitAt(repo.RootPath, "pack-refs", "--all");

        string? gitDir = GitDirectory.Resolve(repo.RootPath);
        Assert.NotNull(gitDir);
        var state = HeadReader.Read(gitDir!);

        Assert.NotNull(state);
        Assert.Equal(branch, state!.Branch);
        Assert.Equal(sha, state.Sha);
    }

    [Fact]
    public void Read_returns_null_branch_when_detached()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        repo.Checkout(sha);

        string? gitDir = GitDirectory.Resolve(repo.RootPath);
        Assert.NotNull(gitDir);
        var state = HeadReader.Read(gitDir!);

        Assert.NotNull(state);
        Assert.Null(state!.Branch);
        Assert.Equal(sha, state.Sha);
    }

    [Fact]
    public void Read_returns_branch_with_null_sha_on_unborn_head()
    {
        using var repo = new GitTestRepo(); // init only — henüz hiç commit yok

        string? gitDir = GitDirectory.Resolve(repo.RootPath);
        Assert.NotNull(gitDir);
        var state = HeadReader.Read(gitDir!);

        Assert.NotNull(state);
        Assert.Null(state!.Sha);
        Assert.False(string.IsNullOrEmpty(state.Branch)); // varsayılan dal adı (main/master, git config'e bağlı)
    }

    [Fact]
    public void Read_resolves_refs_via_commondir_in_linked_worktree()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        repo.CreateBranch("feature");
        string worktreePath = repo.AddWorktree("feature");

        string? gitDir = GitDirectory.Resolve(worktreePath);
        Assert.NotNull(gitDir);
        var state = HeadReader.Read(gitDir!);

        Assert.NotNull(state);
        Assert.Equal("feature", state!.Branch);
        Assert.Equal(sha, state.Sha);
    }

    /// <summary>[reftable] reftable biçimli bir repoda gerçek HEAD git'in tablolarındadır; <c>HEAD</c> dosyası yalnız eski
    /// git sürümleri için bir yer tutucu taşır (<c>ref: refs/heads/.invalid</c>). Bu ad bir branch DEĞİLDİR (git'in ref adı
    /// kuralında bileşen '.' ile başlayamaz), yani dosyadan HEAD okunamaz ve okuma "bilinmiyor" (<c>null</c>) döner.
    /// <para><b>Ölçülen kusur:</b> okuma <c>.invalid</c>'i branch adı sanıyordu. Koordinatör onu motorun bildirdiği gerçek
    /// adla kıyaslayıp pencereye her dönüşte ve her build bitişinde "branch değişti" diyordu: konsol temizleniyor, liste ve
    /// graf Sync'e basılmış gibi baştan kuruluyordu (bir kullanıcının makinesinde, aynı sürümde).</para></summary>
    [Fact]
    public void Read_returns_null_on_a_reftable_repository_whose_HEAD_file_is_a_placeholder()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");

        string? gitDir = GitDirectory.Resolve(repo.RootPath);
        Assert.NotNull(gitDir);
        // Ön-koşul: dosyadaki gerçekten git'in yer tutucusu (git bu biçimi değiştirirse test vakumda yeşil kalmaz).
        Assert.Equal("ref: refs/heads/.invalid", File.ReadAllText(Path.Combine(gitDir!, "HEAD")).Trim());

        Assert.Null(HeadReader.Read(gitDir!));
    }
}
