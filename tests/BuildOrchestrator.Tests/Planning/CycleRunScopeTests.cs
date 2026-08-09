using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [cycles] <see cref="CycleRunScope"/>: bir <c>RunMode.Cycles</c> koşusunun derleyeceği düğüm kümesi —
/// SCC üyeleri + transitif upstream, downstream HARİÇ. Saf fonksiyon, WPF/process YOK.
/// </summary>
public class CycleRunScopeTests
{
    private static ProjectNode Node(string id, bool inCycle, params string[] deps) =>
        new(id, id, id, [], deps, BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static BuildPlan Plan(IReadOnlyList<IReadOnlyList<string>> cycles, params ProjectNode[] nodes) =>
        new([.. nodes.Select((n, i) => n with { BuildOrder = i })], Cycles: cycles, Configuration: "Debug");

    /// <summary>Kapsam ZİNCİRİN TAMAMINI alır: A↔B döngüsü X'e, X de Y'ye bağlıysa dördü de kapsamdadır.
    /// Tek atlamayla yetinmek, ikinci seviyedeki bayat bir DLL'i görmezden gelmek olurdu.</summary>
    [Fact]
    public void The_scope_is_the_members_plus_their_transitive_upstream()
    {
        var plan = Plan([["A", "B"]],
            Node("Y", false),
            Node("X", false, "Y"),
            Node("A", true, "B", "X"),
            Node("B", true, "A"));

        Assert.Equal(["A", "B", "X", "Y"], CycleRunScope.Of(plan).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>DOWNSTREAM kapsam DIŞIdır — gerekçe tipin özetinde: bir çekirdek kütüphanenin dependent
    /// kümesi pratikte tüm repodur, onu da almak düğmenin var oluş sebebini ortadan kaldırırdı.</summary>
    [Fact]
    public void A_dependent_of_the_cycle_stays_out_of_scope()
    {
        var plan = Plan([["A", "B"]],
            Node("A", true, "B"),
            Node("B", true, "A"),
            Node("Z", false, "A"));   // Z, döngüye BAĞLI — ama onu Build derler

        var scope = CycleRunScope.Of(plan);
        Assert.Contains("A", scope);
        Assert.DoesNotContain("Z", scope);
    }

    /// <summary>Dairesel kenar geçişte SONSUZ DÖNGÜ yapmaz — kapsamın kendisi tanımı gereği döngülüdür.</summary>
    [Fact]
    public void A_cycle_edge_terminates_the_walk()
    {
        var plan = Plan([["A", "B", "C"]],
            Node("A", true, "B"), Node("B", true, "C"), Node("C", true, "A"));

        Assert.Equal(["A", "B", "C"], CycleRunScope.Of(plan).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Hiç SCC yoksa kapsam BOŞTUR: o koşu hiçbir şey derlemez ve bu doğrudur (App düğmeyi zaten
    /// pasif tutar). Plan'da karşılığı olmayan bir bağımlılık id'si sessizce atlanır.</summary>
    [Fact]
    public void No_cycle_means_an_empty_scope_and_a_dangling_dependency_is_tolerated()
    {
        Assert.Empty(CycleRunScope.Of(Plan([], Node("A", false), Node("B", false, "A"))));

        var dangling = Plan([["A", "B"]], Node("A", true, "B", "ghost"), Node("B", true, "A"));
        Assert.Equal(["A", "B", "ghost"], CycleRunScope.Of(dangling).OrderBy(x => x, StringComparer.Ordinal));
    }
}
