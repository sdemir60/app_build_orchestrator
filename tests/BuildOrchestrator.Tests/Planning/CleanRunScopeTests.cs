using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [Clean] <see cref="CleanRunScope"/>: bir <c>RunMode.Clean</c> koşusunun planı. Clean hiçbir şey derlemez —
/// her projede yalnız <c>msbuild -t:Clean</c> koşar ve bir projenin temizliği başka bir projenin çıktısına
/// ihtiyaç duymaz. Bu yüzden Clean'in BAĞIMLILIK ANLAMI YOKTUR: plan kenar, döngü ve sıra uyarısı taşımaz.
/// Saf fonksiyon, I/O YOK. Düğüm kurucuları koordinatör testlerinin ortak fixture'ıdır (kopya YASAK).
/// </summary>
public class CleanRunScopeTests
{
    /// <summary>Grafta ne varsa kapsamdadır — sıradan, harici ve döngü üyesi — ama hiçbiri kenar ya da döngü
    /// işareti taşımaz: scheduler döngü üyesini "in dependency cycle" diye önden atlamaz, dairesel kenar
    /// kalmadığı için kilitlenme (A6) yapısal olarak imkânsızdır ve her proje ilk anda hazırdır.</summary>
    [Fact]
    public void Every_project_stays_in_order_without_edges_or_cycle_marks()
    {
        var plan = CyclePlanOf(["A", "B"],
            Node("Lib"),
            Node("Ext", deps: ["Lib"]) with { IsExternal = true },
            Node("A", deps: ["B", "Lib"], inCycle: true),
            Node("B", deps: ["A"], inCycle: true)).Plan;

        var clean = CleanRunScope.Of(plan);

        Assert.Equal(["Lib", "Ext", "A", "B"], clean.Nodes.Select(n => n.Name));
        Assert.Equal([0, 1, 2, 3], clean.Nodes.Select(n => n.BuildOrder));
        Assert.All(clean.Nodes, n => Assert.Empty(n.Dependencies));
        Assert.All(clean.Nodes, n => Assert.False(n.InCycle));
        Assert.Empty(clean.Cycles);
        Assert.True(clean.Nodes[1].IsExternal); // harici proje sıradan bir düğüm olarak kalır
        Assert.Equal(plan.Configuration, clean.Configuration);
    }

    /// <summary>Önizleme BU koşunun işini anlatır: her proje temizlenecek (<c>WillBuild = true</c>). Gerekçe ise
    /// bir disk olgusudur ve DOKUNULMAZ — proje temizlenene kadar çıktısı yerindedir; güncel bir satır
    /// "up to date" yazmaya devam eder, temizlendiği anda "never built" okur.</summary>
    [Fact]
    public void Every_project_is_this_runs_work_and_keeps_its_reason()
    {
        var plan = PlanOf(
            Node("Current", willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate },
            Node("Dirty", willBuild: true) with { WillBuildReason = WillBuildReason.SignatureChanged },
            Node("Unknown")).Plan;

        var clean = CleanRunScope.Of(plan);

        Assert.All(clean.Nodes, n => Assert.True(n.WillBuild));
        Assert.Equal([WillBuildReason.UpToDate, WillBuildReason.SignatureChanged, null],
            clean.Nodes.Select(n => n.WillBuildReason));
    }

    /// <summary>Katman ve belirsiz-üretici uyarıları bir SIRA/kenar uyarısıdır; sırası ve kenarı olmayan bir
    /// koşunun konsoluna basılmaz. Belirsiz üretici uyarısını Sync zaten gösterir; katman uyarısı yalnız tüm planın
    /// sırasını izleyen koşuların başında görünür (<c>runStarted.Warnings</c>), Clean'de görünmez.</summary>
    [Fact]
    public void Ordering_warnings_are_not_carried()
    {
        var plan = PlanOf(Node("A")).Plan with
        {
            LayerWarnings = ["A sits above its own dependency"],
            ProducerWarnings = ["warning: A.dll is produced by two projects"],
        };

        var clean = CleanRunScope.Of(plan);

        Assert.Null(clean.LayerWarnings);
        Assert.Null(clean.ProducerWarnings);
    }
}
