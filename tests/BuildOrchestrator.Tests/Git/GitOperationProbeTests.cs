using System.IO;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>[Faz 2/T1] <see cref="GitOperationProbe.Inspect"/>: her işaret dosyası/klasörü, hiçbiri, öncelik.</summary>
public class GitOperationProbeTests
{
    [Fact]
    public void Inspect_returns_none_when_no_markers_present()
    {
        using var temp = new TempDir();

        Assert.Equal(GitOperation.None, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_command_running_on_index_lock()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.CommandRunning, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_merge_on_merge_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "MERGE_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.Merge, GitOperationProbe.Inspect(temp.Path));
    }

    [Theory]
    [InlineData("rebase-merge")]
    [InlineData("rebase-apply")]
    public void Inspect_returns_rebase_on_rebase_directories(string dirName)
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, dirName));

        Assert.Equal(GitOperation.Rebase, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_cherry_pick_on_cherry_pick_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "CHERRY_PICK_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.CherryPick, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_revert_on_revert_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "REVERT_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.Revert, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_prioritizes_merge_over_index_lock()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "MERGE_HEAD"), "deadbeef\n");
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.Merge, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_prioritizes_rebase_over_index_lock()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "rebase-merge"));
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.Rebase, GitOperationProbe.Inspect(temp.Path));
    }
}
