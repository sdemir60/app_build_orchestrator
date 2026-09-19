using System.IO;
using BuildOrchestrator.Core.Paths;

namespace BuildOrchestrator.Tests.Paths;

/// <summary>[Task 11] Eski worktree havuzu artık kurulmuyor; <see cref="LegacyWorktreePool.Hint"/> yalnız
/// klasör diskte kaldıysa bir hatırlatma döner.</summary>
public sealed class LegacyWorktreePoolTests
{
    [Fact]
    public void A_leftover_pool_folder_gets_a_one_line_hint_naming_the_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-legacy-pool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string? hint = LegacyWorktreePool.Hint(root);

            Assert.Equal(
                $"The old worktree pool at {root} is no longer used — delete it to reclaim space, then run " +
                "`git worktree prune` in the repository", hint);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void No_folder_means_no_hint()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-legacy-pool-" + Guid.NewGuid().ToString("N"));

        Assert.Null(LegacyWorktreePool.Hint(root));
    }
}
