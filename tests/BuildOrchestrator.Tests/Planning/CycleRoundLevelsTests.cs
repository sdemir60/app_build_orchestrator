using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [seviyeli turlar] <see cref="CycleRoundLevels"/>: bir SCC turunun bariyerli seviye planı. Pinlenen sözleşme:
/// (1) ileri kenar sıralar (tüketici üreticiden sonraki seviyede), (2) HERHANGİ yönde doğrudan komşular aynı
/// seviyede olamaz (torn read yapısal olarak imkânsız), (3) geri kenar sıralamaz (eski nesil okunur — sıralı
/// turun semantiği), (4) determinizm ve build-order korunur, (5) kapsam dışı bağımlılık kısıt üretmez.
/// Saf fonksiyon — WPF/process YOK.
/// </summary>
public class CycleRoundLevelsTests
{
    private static Func<string, IReadOnlyList<string>> Deps(params (string Id, string[] DependsOn)[] edges)
    {
        var map = edges.ToDictionary(e => e.Id, e => (IReadOnlyList<string>)e.DependsOn, StringComparer.OrdinalIgnoreCase);
        return id => map.TryGetValue(id, out var deps) ? deps : [];
    }

    private static string[][] Levels(IReadOnlyList<IReadOnlyList<string>> levels) =>
        [.. levels.Select(l => l.ToArray())];

    [Fact] // Yıldız SCC: uydular yalnız Hub'a bağlı — Hub tek başına 1. seviye, uydular BİRLİKTE 2. seviye.
    public void satellites_that_only_read_the_hub_share_one_level()
    {
        var levels = CycleRoundLevels.Compute(["Hub", "S1", "S2", "S3"],
            Deps(("Hub", ["S1", "S2", "S3"]), ("S1", ["Hub"]), ("S2", ["Hub"]), ("S3", ["Hub"])));

        Assert.Equal([["Hub"], ["S1", "S2", "S3"]], Levels(levels));
    }

    [Fact] // Halka (A→B→C→A): ileri kenarlar zinciri sıralar — hiç paralellik yoktur, seviyeler tekil kalır.
    public void a_ring_stays_fully_sequential()
    {
        var levels = CycleRoundLevels.Compute(["A", "B", "C"],
            Deps(("A", ["C"]), ("B", ["A"]), ("C", ["B"])));

        Assert.Equal([["A"], ["B"], ["C"]], Levels(levels));
    }

    [Fact] // Komşu ayrımı yönden bağımsızdır: yalnız GERİ kenarla bağlı çift bile aynı seviyeye konmaz.
    public void a_back_edge_neighbor_is_pushed_to_a_later_level()
    {
        // A, B'nin eski neslini okur (geri kenar); B'nin ileri bağımlılığı yok — sıralama kısıtı da yok.
        // Yine de A ile B aynı seviyede OLAMAZ: B derlenirken A aynı DLL'i okuyor olurdu.
        var levels = CycleRoundLevels.Compute(["A", "B"], Deps(("A", ["B"])));

        Assert.Equal([["A"], ["B"]], Levels(levels));
    }

    [Fact] // İki bağımsız ikili tek SCC'de: çiftler kendi içinde sıralı, çapraz üyeler aynı seviyede birleşir.
    public void independent_pairs_interleave_across_shared_levels()
    {
        // A↔B ve C↔D, tek gruba köprü kenarlarıyla bağlı olmadan verilmiş olsun (fonksiyon SCC doğrulamaz —
        // girdisi plan.Cycles'tır): A ve C komşu değil → seviye 1'i paylaşır; B ve D → seviye 2.
        var levels = CycleRoundLevels.Compute(["A", "B", "C", "D"],
            Deps(("A", ["B"]), ("B", ["A"]), ("C", ["D"]), ("D", ["C"])));

        Assert.Equal([["A", "C"], ["B", "D"]], Levels(levels));
    }

    [Fact] // Kapsam dışı bağımlılık kısıt üretmez: bu turda yazılmayan dosya ne sıralar ne komşu ayrımına girer.
    public void a_dependency_outside_the_round_imposes_no_constraint()
    {
        // Tur yalnız {B, C}: ikisinin de bağımlılığı A kapsam dışı (bu turda derlenmiyor) ve birbirlerine
        // kenarları yok → tek seviyede birlikte derlenirler.
        var levels = CycleRoundLevels.Compute(["B", "C"], Deps(("B", ["A"]), ("C", ["A"])));

        Assert.Equal([["B", "C"]], Levels(levels));
    }

    [Fact] // Birleşim tam turdur, sıra build-order kalır, plan deterministiktir.
    public void the_union_is_the_round_in_build_order_and_the_plan_is_deterministic()
    {
        string[] round = ["Hub", "S1", "S2", "S3"];
        var deps = Deps(("Hub", ["S1", "S2", "S3"]), ("S1", ["Hub"]), ("S2", ["Hub"]), ("S3", ["Hub"]));

        var first = CycleRoundLevels.Compute(round, deps);
        var second = CycleRoundLevels.Compute(round, deps);

        Assert.Equal(round, first.SelectMany(l => l));
        Assert.Equal(Levels(first), Levels(second));
    }
}
