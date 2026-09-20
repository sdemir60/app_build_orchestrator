using BuildOrchestrator.App.Console;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>[Faz 2/T1] <see cref="GitOperationText"/>: tooltip/build-warning metinleri tek yerde pinlenir
/// (kopya YASAK, CLAUDE.md).</summary>
public class GitOperationTextTests
{
    [Theory]
    [InlineData(GitOperation.Merge, "Merge in progress — finish or abort it in git")]
    [InlineData(GitOperation.Rebase, "Rebase in progress — finish or abort it in git")]
    [InlineData(GitOperation.CherryPick, "Cherry-pick in progress — finish or abort it in git")]
    [InlineData(GitOperation.Revert, "Revert in progress — finish or abort it in git")]
    [InlineData(GitOperation.CommandRunning, "A git command is running")]
    public void Tooltip_returns_expected_text(GitOperation operation, string expected)
    {
        Assert.Equal(expected, GitOperationText.Tooltip(operation));
    }

    [Fact]
    public void Tooltip_returns_null_for_none()
    {
        Assert.Null(GitOperationText.Tooltip(GitOperation.None));
    }

    [Theory]
    [InlineData(GitOperation.Merge, "a merge is in progress — files with conflict markers will not compile")]
    [InlineData(GitOperation.Rebase, "a rebase is in progress — files with conflict markers will not compile")]
    [InlineData(GitOperation.CherryPick, "a cherry-pick is in progress — files with conflict markers will not compile")]
    [InlineData(GitOperation.Revert, "a revert is in progress — files with conflict markers will not compile")]
    public void BuildWarning_returns_expected_text(GitOperation operation, string expected)
    {
        Assert.Equal(expected, GitOperationText.BuildWarning(operation));
    }

    [Theory]
    [InlineData(GitOperation.None)]
    [InlineData(GitOperation.CommandRunning)]
    public void BuildWarning_returns_null_for_none_and_command_running(GitOperation operation)
    {
        Assert.Null(GitOperationText.BuildWarning(operation));
    }

    [Fact]
    public void StuckLock_has_expected_text()
    {
        Assert.Equal(
            "git's index.lock has been there for 30 s — a git process may have crashed; delete it only if no git command is running",
            GitOperationText.StuckLock);
    }

    /// <summary>[eksik negatif pin] Satırın METNİ pinliydi, konsoldaki TONU değil. Takılı kilit satırı çevresindeki
    /// git reddetmeleri amberken (<c>warning:</c> öneki — <c>PlanProgressLinesTests</c>) DÜZ satır kalır: bu bir
    /// reddetme değil TANIdır — araç kilide dokunmaz, bir şey de reddetmemiştir, yalnız gördüğünü söyler.
    /// Satırın "git's" ile başlaması onu KOMUT satırı da yapmaz (<c>CommandHeads</c> "git " arar).</summary>
    [Fact]
    public void StuckLock_stays_a_plain_console_line()
    {
        Assert.DoesNotContain("warning:", GitOperationText.StuckLock, StringComparison.Ordinal);
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify(GitOperationText.StuckLock));
    }
}
