namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Core.Planning;
using Xunit;

public class CycleRoundPolicyTests
{
    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    // Tur 1 yeşil geçse bile DURULMAZ: A, tur 1'de B'nin ESKİ dll'ine karşı derlenmiş olabilir.
    // Yakınsama ölçütü İKİ ARDIŞIK yeşil turdur (spec §5).
    [Fact]
    public void first_green_round_alone_does_not_converge()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set(), null));
    }

    [Fact]
    public void two_consecutive_green_rounds_converge()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(2, Set(), Set()));
    }

    // Aynı KÜME iki turdur patlıyorsa ilerleme yok. (Sayı değil küme — {A,C}→{B,D} salınımdır.)
    [Fact]
    public void identical_failure_set_two_rounds_is_no_progress()
    {
        Assert.Equal(CycleRoundDecision.NoProgress, CycleRoundPolicy.Decide(2, Set("a"), Set("a")));
    }

    [Fact]
    public void same_count_different_members_is_not_no_progress()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a", "c"), Set("b", "d")));
    }

    [Fact]
    public void shrinking_failure_set_continues()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a"), Set("a", "b")));
    }

    // Tavan: tur 1 patladı, tur 2 düzeldi ama tur 3'e kadar iki ardışık yeşil görülemedi.
    [Fact]
    public void cap_stops_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.CapReached, CycleRoundPolicy.Decide(3, Set(), Set("a")));
    }

    // Converged, cap'ten ÖNCE değerlendirilir: 3. turda iki ardışık yeşil varsa yakınsamıştır.
    [Fact]
    public void converged_wins_over_cap_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(3, Set(), Set()));
    }
}
