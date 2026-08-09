namespace BuildOrchestrator.Tests.Scheduling;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;
using Xunit;

public class CycleGroupsTests
{
    private static ProjectNode Node(string id, int order, bool inCycle, params string[] deps) =>
        new(id, id, id, [], deps, order, null, null, inCycle, null);

    // plan.Cycles ordinal sıralı gelir ("a","b"); build-order ise b(0) → a(1).
    // MembersOf BUILD-ORDER vermeli — dispatch sırası buna dayanır.
    [Fact]
    public void members_are_in_build_order_not_ordinal_order()
    {
        var plan = new BuildPlan(
            [Node("b", 0, true, "a"), Node("a", 1, true, "b")],
            [new[] { "a", "b" }],
            "Debug");

        var groups = CycleGroups.From(plan);

        Assert.Equal(1, groups.Count);
        Assert.Equal(["b", "a"], groups.MembersOf("a"));
        Assert.Equal(["b", "a"], groups.MembersOf("b"));
    }

    [Fact]
    public void non_member_reports_empty_and_is_not_member()
    {
        var plan = new BuildPlan(
            [Node("b", 0, true, "a"), Node("a", 1, true, "b"), Node("c", 2, false)],
            [new[] { "a", "b" }],
            "Debug");

        var groups = CycleGroups.From(plan);

        Assert.False(groups.IsMember("c"));
        Assert.Empty(groups.MembersOf("c"));
        Assert.True(groups.IsMember("a"));
    }

    [Fact]
    public void plan_without_cycles_yields_no_groups()
    {
        var plan = new BuildPlan([Node("a", 0, false)], [], "Debug");

        Assert.Equal(0, CycleGroups.From(plan).Count);
    }
}
