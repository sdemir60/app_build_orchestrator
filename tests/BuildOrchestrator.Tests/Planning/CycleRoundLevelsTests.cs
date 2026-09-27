using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [seviyeli turlar] <see cref="CycleRoundLevels"/>: bir SCC turunun bariyerli seviye planı. Pinlenen sözleşme:
/// (1) HERHANGİ yönde doğrudan komşular aynı seviyede olamaz (torn read yapısal olarak imkânsız), (2) en çok
/// okunan üye önce yerleşir — okuyucuları onun bu turdaki taze çıktısını okur; eşitlikte daha çok komşusu olan,
/// sonra build-order, (3) her üye çakışmadığı en erken seviyeye girer, (4) başkasının paylaşılan kopyasını
/// yazabilen üye o kopyanın sahibiyle ve okuyucularıyla aynı seviyede olamaz, (5) seviye içi sıra build-order'dır,
/// plan deterministiktir, kapsam dışı bağımlılık kısıt üretmez. Saf fonksiyon — WPF/process YOK.
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

    [Fact] // Üçlü halka (A→B→C→A): her çift komşu — hiç paralellik yoktur, seviyeler tekil kalır.
    public void a_ring_of_three_stays_fully_sequential()
    {
        var levels = CycleRoundLevels.Compute(["A", "B", "C"],
            Deps(("A", ["C"]), ("B", ["A"]), ("C", ["B"])));

        Assert.Equal([["A"], ["B"], ["C"]], Levels(levels));
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — en çok okunan önce.]</b> Eski kural seviyeleri build-order'da tek geçişte kurardı:
    /// build-order'da ÖNCE gelen üreticiyi okuyan tüketici ondan sonraki seviyeye itilir, geri kenar sıralamazdı.
    /// Build-order döngü içinde keyfidir (alfabetik yol), bu yüzden plan keyfi uzuyordu. Sahada ölçüldü: 17 üyeli
    /// UI grubu 9 dalgada derleniyor, dördünde tek üye çalışıyordu; 15 kardeşin okuduğu UI.General yedinci
    /// dalgadaydı ve 12 okuyucusu onun eski neslini okuyordu. Yeni kural en çok okunanı önce yerleştirir: aynı
    /// grup mümkün olan en az dalgada (6) biter, UI.General'i eski nesliyle okuyan kalmaz.
    /// </summary>
    [Fact]
    public void the_most_read_member_compiles_first_so_its_readers_see_this_rounds_output()
    {
        // Hub build-order'da SONDA ama üç üye onu okuyor.
        var levels = CycleRoundLevels.Compute(["A", "B", "C", "Hub"],
            Deps(("A", ["Hub"]), ("B", ["Hub"]), ("C", ["Hub"]), ("Hub", ["A"])));

        Assert.Equal([["Hub"], ["A", "B", "C"]], Levels(levels));
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — okunan önce.]</b> Eski iddia: A, B'nin eski neslini okur (geri kenar) ve build-order'da
    /// önce geldiği için ÖNCE derlenir — [[A], [B]]. Yeni kural okunanı öne alır: B önce derlenir ve A onun bu
    /// turdaki çıktısını okur. Komşu ayrımı aynen durur: ikisi asla aynı seviyede değildir.
    /// </summary>
    [Fact]
    public void a_producer_compiles_before_a_reader_nobody_reads()
    {
        var levels = CycleRoundLevels.Compute(["A", "B"], Deps(("A", ["B"])));

        Assert.Equal([["B"], ["A"]], Levels(levels));
    }

    [Fact] // Dörtlü halka: build-order'daki ileri zincir artık seviye sayısını dikte etmez — komşu olmayanlar birleşir.
    public void a_ring_of_four_compiles_in_two_levels()
    {
        // B→A, C→B, D→C, A→D: eski kural build-order'daki ileri kenarlarla dört seviye kurardı.
        var levels = CycleRoundLevels.Compute(["A", "B", "C", "D"],
            Deps(("A", ["D"]), ("B", ["A"]), ("C", ["B"]), ("D", ["C"])));

        Assert.Equal([["A", "C"], ["B", "D"]], Levels(levels));
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

    [Fact] // Her üye tam bir kez yerleşir, seviye içi sıra build-order'dır, plan deterministiktir.
    public void every_member_is_placed_once_each_level_keeps_build_order_and_the_plan_is_deterministic()
    {
        string[] round = ["A", "B", "C", "Hub"];
        var deps = Deps(("A", ["Hub"]), ("B", ["Hub"]), ("C", ["Hub"]), ("Hub", ["A"]));

        var first = CycleRoundLevels.Compute(round, deps);
        var second = CycleRoundLevels.Compute(round, deps);

        Assert.Equal(round.Order(), first.SelectMany(l => l).Order());
        Assert.All(first, level => Assert.Equal(round.Where(level.Contains), level));
        Assert.Equal(Levels(first), Levels(second));
    }

    // ---------------------------------------------------------------- paylaşılan kopya çakışması

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "Acme.Sales", ["sp"] = "Acme.Sales.Pricing", ["r"] = "Acme.Report", ["x"] = "Acme.Salesforce",
        ["print"] = "Acme.Sales.Print",
    };

    private static Func<string, string, bool> Collisions(params (string Id, string[] DependsOn)[] edges) =>
        CycleRoundLevels.SharedCopyCollisions(id => Names[id], Deps(edges));

    /// <summary>Yaygın post-build <c>copy $(TargetName).*</c> kendi çıktısının yanında bin'deki "Ad.*" dosyalarını
    /// da kopyalar: "Acme.Sales" derlenirken paylaşılan klasördeki Acme.Sales.Pricing kopyası da yeniden yazılır
    /// (sahada: UI.General → UI.General.Common, UI.NewSales → NewSales.Stock ve NewSales.Pricing). Kenar yoktur
    /// ama iki derleme aynı dosyaya dokunur — aynı seviyede olamazlar.</summary>
    [Fact]
    public void a_member_that_may_rewrite_another_members_copy_never_shares_its_level()
    {
        var levels = CycleRoundLevels.Compute(["s", "sp"], Deps(), Collisions());

        Assert.Equal([["s"], ["sp"]], Levels(levels));
    }

    [Fact] // Kopyayı OKUYAN da çakışır — kopyanın sahibi turda olmasa, hatta gruptan olmasa da.
    public void a_member_that_may_rewrite_a_copy_never_shares_a_level_with_its_reader()
    {
        var levels = CycleRoundLevels.Compute(["s", "r"], Deps(), Collisions(("r", ["print"])));

        Assert.Equal([["s"], ["r"]], Levels(levels));
    }

    [Fact] // Önek noktayla biter: "Acme.Sales" ile "Acme.Salesforce" aynı kopyaya dokunmaz.
    public void a_name_that_merely_starts_the_same_does_not_collide()
    {
        var levels = CycleRoundLevels.Compute(["s", "x"], Deps(), Collisions());

        Assert.Equal([["s", "x"]], Levels(levels));
    }
}
