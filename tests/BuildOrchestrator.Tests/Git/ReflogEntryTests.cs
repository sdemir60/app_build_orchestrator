using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>[Faz 2/T1] <see cref="ReflogEntry.Classify"/>: mesaj tablosu + bozuk satır → Other, gerçek git
/// reflog çıktısına karşı (commit, checkout) sınama.</summary>
public class ReflogEntryTests
{
    private const string LinePrefix = "0000000000000000000000000000000000000000 " +
        "1111111111111111111111111111111111111111 Test User <test@example.com> 0 +0000\t";

    [Theory]
    [InlineData("commit: add feature", HeadMove.Commit)]
    [InlineData("commit (amend): fix message", HeadMove.Commit)]
    [InlineData("commit (initial): first commit", HeadMove.Commit)]
    [InlineData("commit (merge): merge branch 'x'", HeadMove.Commit)]
    [InlineData("checkout: moving from main to feature", HeadMove.BranchSwitch)]
    [InlineData("checkout: moving from main to main", HeadMove.Other)]
    [InlineData("pull: Fast-forward", HeadMove.Other)]
    [InlineData("merge feature: Fast-forward", HeadMove.Other)]
    [InlineData("reset: moving to HEAD~1", HeadMove.Other)]
    [InlineData("rebase (finish): returning to refs/heads/main", HeadMove.Other)]
    [InlineData("cherry-pick: applied", HeadMove.Other)]
    [InlineData("revert: applied", HeadMove.Other)]
    public void Classify_maps_reflog_message_to_head_move(string message, HeadMove expected)
    {
        Assert.Equal(expected, ReflogEntry.Classify(LinePrefix + message));
    }

    [Fact]
    public void Classify_returns_other_for_line_without_tab()
    {
        Assert.Equal(HeadMove.Other, ReflogEntry.Classify("garbage line without a tab"));
    }

    [Fact]
    public void Classify_returns_other_for_empty_line()
    {
        Assert.Equal(HeadMove.Other, ReflogEntry.Classify(""));
    }

    [Fact]
    public void Classify_maps_real_git_commit_reflog_line_to_commit()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");

        Assert.Equal(HeadMove.Commit, ReflogEntry.Classify(repo.LastHeadReflogLine()));
    }

    [Fact]
    public void Classify_maps_real_git_checkout_reflog_line_to_branch_switch()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");
        repo.CreateBranch("feature");
        repo.Checkout("feature");

        Assert.Equal(HeadMove.BranchSwitch, ReflogEntry.Classify(repo.LastHeadReflogLine()));
    }
}
