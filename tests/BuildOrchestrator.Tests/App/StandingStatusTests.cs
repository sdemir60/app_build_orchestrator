using BuildOrchestrator.App.Controls;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.20.0 §2.3 · §5] <b>Çıktının durumu</b> önizleme kararından TEK eşleme yerinde türetilir
/// (<see cref="StandingStatuses.From"/>). Karar yoksa durum bilinmez; varsa renk yalnız gerekçeden okunur —
/// <see cref="WillBuildReason.WaitingForDependency"/> yeşildir ("bekliyor" üçgende söylenir).
/// </summary>
public class StandingStatusTests
{
    [Theory]
    [InlineData(false, WillBuildReason.UpToDate, StandingStatus.Current)]
    [InlineData(false, WillBuildReason.WaitingForDependency, StandingStatus.Current)]
    [InlineData(true, WillBuildReason.WaitingForDependency, StandingStatus.Current)]
    [InlineData(true, WillBuildReason.LastFailed, StandingStatus.Failed)]
    [InlineData(true, WillBuildReason.NeverBuilt, StandingStatus.Stale)]
    [InlineData(true, WillBuildReason.SignatureChanged, StandingStatus.Stale)]
    [InlineData(true, WillBuildReason.DepIssue, StandingStatus.Stale)]
    public void A_decision_reads_its_colour_from_the_reason(bool willBuild, WillBuildReason reason, StandingStatus expected)
        => Assert.Equal(expected, StandingStatuses.From(willBuild, reason));

    /// <summary>Karar eksikse (Sync yok ya da karar düşürüldü) durum bilinmez — gerekçe tek başına, plan tek
    /// başına yetmez.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, WillBuildReason.UpToDate)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void Without_a_decision_the_standing_is_unknown(bool? willBuild, WillBuildReason? reason)
        => Assert.Equal(StandingStatus.Unknown, StandingStatuses.From(willBuild, reason));
}
