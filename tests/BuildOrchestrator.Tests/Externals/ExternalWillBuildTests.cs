using System;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici projenin "derlenecek mi" kararı — ana repo değerlendiricisinin bilinçli mini aynası: haricilerin
/// bağımlılığı yoktur, bu yüzden cycle ve DepIssue kolları burada YOKTUR. Karar dört koldan birine düşer ve
/// gerekçeler ana repo ile aynı sözlükten (<see cref="WillBuildReason"/>) konuşur.
/// </summary>
public class ExternalWillBuildTests
{
    private static BuildState State(string? signature, BuildResult? result) =>
        new("D:\\ext\\mail\\Mail.sln", signature, LastResult: result, LastRunAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void No_record_means_never_built()
    {
        var decision = ExternalWillBuild.Decide(null, "SIG");

        Assert.True(decision.WillBuild);
        Assert.Equal(WillBuildReason.NeverBuilt, decision.Reason);
    }

    [Fact]
    public void A_record_without_a_signature_means_never_built()
    {
        var decision = ExternalWillBuild.Decide(State(null, BuildResult.Succeeded), "SIG");

        Assert.True(decision.WillBuild);
        Assert.Equal(WillBuildReason.NeverBuilt, decision.Reason);
    }

    [Theory]
    [InlineData(BuildResult.Failed)]
    [InlineData(BuildResult.Skipped)]
    [InlineData(null)]
    public void A_run_that_did_not_end_green_is_rebuilt(BuildResult? lastResult)
    {
        var decision = ExternalWillBuild.Decide(State("SIG", lastResult), "SIG");

        Assert.True(decision.WillBuild);
        Assert.Equal(WillBuildReason.LastFailed, decision.Reason);
    }

    [Fact]
    public void A_different_signature_is_rebuilt()
    {
        var decision = ExternalWillBuild.Decide(State("OLD", BuildResult.Succeeded), "NEW");

        Assert.True(decision.WillBuild);
        Assert.Equal(WillBuildReason.SignatureChanged, decision.Reason);
    }

    [Fact]
    public void The_same_signature_after_a_green_run_is_up_to_date()
    {
        var decision = ExternalWillBuild.Decide(State("SIG", BuildResult.Succeeded), "SIG");

        Assert.False(decision.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, decision.Reason);
    }
}
