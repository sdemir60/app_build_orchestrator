using System.IO;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>[Faz 2/T1] <see cref="GitDirectory.Resolve"/>: klasör, gitfile (göreli/mutlak), yok, bozuk gitfile.</summary>
public class GitDirectoryTests
{
    [Fact]
    public void Resolve_returns_path_when_dot_git_is_a_directory()
    {
        using var temp = new TempDir();
        string gitDir = Path.Combine(temp.Path, ".git");
        Directory.CreateDirectory(gitDir);

        Assert.Equal(gitDir, GitDirectory.Resolve(temp.Path));
    }

    [Fact]
    public void Resolve_follows_relative_gitdir_file()
    {
        using var temp = new TempDir();
        string target = Path.Combine(temp.Path, "real-git-dir");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(temp.Path, ".git"), "gitdir: real-git-dir\n");

        string? result = GitDirectory.Resolve(temp.Path);

        Assert.Equal(Path.GetFullPath(target), result);
    }

    [Fact]
    public void Resolve_follows_absolute_gitdir_file()
    {
        using var temp = new TempDir();
        using var target = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, ".git"), $"gitdir: {target.Path}\n");

        string? result = GitDirectory.Resolve(temp.Path);

        Assert.Equal(Path.GetFullPath(target.Path), result);
    }

    [Fact]
    public void Resolve_returns_null_when_no_marker_present()
    {
        using var temp = new TempDir();

        Assert.Null(GitDirectory.Resolve(temp.Path));
    }

    [Fact]
    public void Resolve_returns_null_when_gitdir_file_is_malformed()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, ".git"), "not a gitdir line\n");

        Assert.Null(GitDirectory.Resolve(temp.Path));
    }
}
