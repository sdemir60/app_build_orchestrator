using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [tek proje · design v1.11.0 §3.8] <see cref="ProjectRunScope"/>: satırdan tetiklenen bir koşunun planı —
/// YALNIZ hedef proje, bağımlılıklar derlenmez, kapsam dışına dokunulmaz. Saf fonksiyon, I/O YOK.
///
/// <para>Bayat bağımlılık kuralı <see cref="CycleRunScope"/>'un anlattığı deliği kapatır: kirli bir
/// bağımlılığın eski DLL'ine karşı derlenen hedef yeşil döner ama çıktısı bayattır; imzası persist edilirse
/// bir sonraki Build onu bir daha derlemez. Bayat bağımlılıklar bu yüzden dep-issue olarak hedefe
/// yapışır — kayıt NOTLA yazılır ve bir sonraki Build hedefi yeniden derler (§8.3).</para>
/// </summary>
public class ProjectRunScopeTests
{
    private static ProjectNode Node(string id, bool? willBuild = null, bool inCycle = false, string[]? deps = null) =>
        new(id, id, id, [], deps ?? [], BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: willBuild,
            WillBuildReason: willBuild == true ? WillBuildReason.SignatureChanged : null);

    private static BuildPlan Plan(params ProjectNode[] nodes) =>
        new([.. nodes.Select((n, i) => n with { BuildOrder = i })], Cycles: [], Configuration: "Release",
            LayerWarnings: ["layer warning about someone else"]);

    [Fact]
    public void The_plan_shrinks_to_the_target_alone_and_keeps_the_configuration()
    {
        var plan = Plan(Node("Base", willBuild: false), Node("Target", willBuild: true, deps: ["Base"]), Node("Dependent", willBuild: true, deps: ["Target"]));

        var scope = ProjectRunScope.Of(plan, "Target");

        Assert.NotNull(scope);
        var only = Assert.Single(scope!.Plan.Nodes);
        Assert.Equal("Target", only.Id);
        Assert.Equal("Release", scope.Plan.Configuration);
        Assert.Empty(scope.Plan.Cycles);
        // Kapsam dışı projeleri anlatan katman uyarıları bu koşunun konusu değildir — basılmaz.
        Assert.Null(scope.Plan.LayerWarnings);
    }

    /// <summary>Hedefin kimliği harf-duyarsız eşleşir (proje id'leri Windows yollarıdır) ve planda olmayan
    /// bir hedef için kapsam YOKTUR — çağıran koşuyu hiç başlatmaz.</summary>
    [Fact]
    public void The_target_is_matched_case_insensitively_and_an_unknown_target_yields_no_scope()
    {
        var plan = Plan(Node(@"C:\r\A\A.csproj", willBuild: true));

        Assert.NotNull(ProjectRunScope.Of(plan, @"c:\R\a\a.CSPROJ"));
        Assert.Null(ProjectRunScope.Of(plan, @"C:\r\B\B.csproj"));
    }

    /// <summary>Hedefin incremental kararı KORUNUR: Build modunda güncel bir hedef tam koşudaki gibi
    /// <c>skipped — up to date</c> olur, Rebuild onu koşulsuz derler — "aynı motor yolu, tek fark kapsam".</summary>
    [Fact]
    public void An_ordinary_target_keeps_its_own_will_build_decision()
    {
        var plan = Plan(Node("Dep", willBuild: false), Node("Target", willBuild: false, deps: ["Dep"]));

        var scope = ProjectRunScope.Of(plan, "Target")!;

        Assert.False(scope.Target.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, scope.Target.WillBuildReason ?? WillBuildReason.UpToDate);
        Assert.Empty(scope.StaleDependencies); // güncel bağımlılık bayat DEĞİLDİR
    }

    /// <summary>Kirli (<c>WillBuild=true</c>) ya da bilinmeyen (<c>null</c>) DOĞRUDAN bağımlılıklar bayattır:
    /// bu koşuda derlenmezler, son bilinen çıktıları referans alınır. Bilinmeyen de bayat sayılır — güvenli
    /// yön "bir kez daha derlemek"tir, diğeri "sessizce bayat DLL'e link'lenmek".</summary>
    [Fact]
    public void Dirty_or_unknown_direct_dependencies_are_stale_and_clean_ones_are_not()
    {
        var plan = Plan(
            Node("Clean", willBuild: false),
            Node("Dirty", willBuild: true),
            Node("Unknown", willBuild: null),
            Node("Target", willBuild: true, deps: ["Clean", "Dirty", "Unknown"]));

        var scope = ProjectRunScope.Of(plan, "Target")!;

        Assert.Equal(["Dirty", "Unknown"], scope.StaleDependencies.Select(s => s.Id));
        Assert.All(scope.StaleDependencies, s => Assert.False(s.InCycle));
    }

    /// <summary>Bayatlık DOĞRUDAN bağımlılıkta okunur; transitif bir kirlilik Safe yayılımıyla zaten doğrudan
    /// bağımlılığa iner. Kapsam dışı bir dependent'ın kirliliği hedefi ilgilendirmez.</summary>
    [Fact]
    public void Only_direct_dependencies_are_examined()
    {
        var plan = Plan(
            Node("Far", willBuild: true),
            Node("Near", willBuild: false, deps: ["Far"]),
            Node("Target", willBuild: true, deps: ["Near"]),
            Node("Downstream", willBuild: true, deps: ["Target"]));

        var scope = ProjectRunScope.Of(plan, "Target")!;

        Assert.Empty(scope.StaleDependencies);
    }

    /// <summary>
    /// Döngü üyesi bir hedef satırından TEK BAŞINA derlenir (design §3.8 "koşul yok"): düğüm koşuya döngü
    /// dışıymış gibi girer (scheduler onu pre-skip etmesin) ve planın "kapsam dışı" anlamındaki
    /// <c>WillBuild=false</c>'u taşımaz — o değer güncel demek değildir, Build modunda hedefi
    /// <c>up to date</c> diye atlatırdı.
    /// </summary>
    [Fact]
    public void A_cycle_member_target_enters_the_run_as_a_plain_node_that_will_build()
    {
        var plan = Plan(
            Node("A", willBuild: false, inCycle: true, deps: ["B"]),
            Node("B", willBuild: false, inCycle: true, deps: ["A"]));

        var scope = ProjectRunScope.Of(plan, "A")!;

        Assert.False(scope.Target.InCycle);
        Assert.Null(scope.Target.WillBuild);
        Assert.Null(scope.Target.WillBuildReason);
    }

    /// <summary>
    /// Döngü üyesi hedefin döngüdeki bağımlılıkları HER ZAMAN bayattır: üye tek başına derlendiğinde
    /// kardeşlerinin son bilinen çıktısına karşı derlenir — turlar koşmadı, yakınsama kanıtı yok. Bu yüzden
    /// dep-issue notu düşer ve bir sonraki Resolve cycles grubu yeniden derler (grup "hepsi güncel" olamaz).
    /// Planın onlara verdiği <c>WillBuild=false</c> (kapsam dışı) bunu gizleyemez.
    /// </summary>
    [Fact]
    public void A_cycle_member_targets_in_cycle_dependencies_are_always_stale_and_say_so()
    {
        var plan = Plan(
            Node("Lib", willBuild: false),
            Node("A", willBuild: false, inCycle: true, deps: ["B", "Lib"]),
            Node("B", willBuild: false, inCycle: true, deps: ["A"]));

        var scope = ProjectRunScope.Of(plan, "A")!;

        var stale = Assert.Single(scope.StaleDependencies);
        Assert.Equal("B", stale.Id);
        Assert.True(stale.InCycle);
    }

    /// <summary>Döngü DIŞI bir hedefin döngü üyesi bağımlılığı tam Build'dekiyle AYNI muameleyi görür: orada
    /// da üye derlenmez ve dependent'ı dep-issue almaz (Skipped bağımlılık issue üretmez, v7 A6) — tek
    /// proje koşusu bu kuralı değiştirmez.</summary>
    [Fact]
    public void An_ordinary_targets_cycle_member_dependency_is_not_flagged_just_like_in_a_full_build()
    {
        var plan = Plan(
            Node("A", willBuild: false, inCycle: true, deps: ["B"]),
            Node("B", willBuild: false, inCycle: true, deps: ["A"]),
            Node("Target", willBuild: true, deps: ["A"]));

        var scope = ProjectRunScope.Of(plan, "Target")!;

        Assert.Empty(scope.StaleDependencies);
    }
}
