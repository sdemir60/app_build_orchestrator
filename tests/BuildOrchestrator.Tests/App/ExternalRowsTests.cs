using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Harici projelerin proje listesindeki hâli: satır mekaniği sıradan projelerinkiyle aynıdır, ama listenin
/// BAŞINDA ve kendi <c>External</c> grubunda dururlar (katman index −1) — ana projeler onların çıktısına
/// bağlıdır, o yüzden önce derlenirler.
/// </summary>
public class ExternalRowsTests
{
    private const string MailTarget = @"D:\ext\mail\Mail.sln";

    private static RunViewModel NewVm(EngineHost engine) =>
        new(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode ExternalNode(string name, string target, int order = 0) =>
        new(target, name, target, [System.IO.Path.GetFileName(target)], [], order,
            ExternalProjectsConventions.LayerIndex, ExternalProjectsConventions.LayerName, false, true,
            WillBuildReason.NeverBuilt, IsExternal: true);

    private static ProjectNode MainNode(string id, string name, int order) =>
        new(id, name, id, ["Osys"], [], order, null, null, false, true);

    [Fact]
    public async Task An_external_node_becomes_a_row_with_its_own_name_and_solution()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.OnEvent(new WorkspaceTopologyEvent([ExternalNode("Mail", MailTarget)], [], [], []));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(MailTarget, row.Id);
        Assert.Equal("Mail", row.Name);
        Assert.Equal("Mail.sln", row.SolutionName);
        Assert.True(row.IsExternal);
    }

    [Fact]
    public async Task Externals_lead_the_list()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [ExternalNode("Mail", MailTarget), MainNode(@"D:\repo\a.csproj", "A", 1)], [], [], []));

        Assert.Equal(["Mail", "A"], vm.Projects.Select(r => r.Name));
        Assert.True(vm.Projects[0].IsExternal);
        Assert.False(vm.Projects[1].IsExternal);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — v1.16.0] Bu yerde eskiden iki test vardı ve ikisi de "ana reponun hedef commit'i
    /// harici satırlara İTİLMEZ" diye pinliyordu (o sha başka bir repoyu anlatır ve harici satırın yanında
    /// yalan söylerdi). Hedef commit artık HİÇBİR satıra itilmiyor: satırın sağ yuvasında commit değil KARAR
    /// duruyor. Geriye kalan — ve asıl önemli olan — iddia şudur: harici satır bu yüzeyde de SIRADAN bir
    /// satırdır, ayrı bir dalı yoktur.
    /// </summary>
    [Fact]
    public async Task An_external_row_reads_the_decision_facts_exactly_like_a_main_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.OnEvent(new WorkspaceTopologyEvent(
            [ExternalNode("Mail", MailTarget), MainNode(@"D:epo.csproj", "A", 1)], [], [], []));

        var builtAt = new System.DateTimeOffset(2026, 9, 10, 12, 0, 0, System.TimeSpan.Zero);
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(MailTarget, "Mail", false, "a1b2c3d", WillBuildReason.UpToDate,
                OwnFilesChanged: false, LastBuiltAt: builtAt),
            new BuildPreviewItem(@"D:epo.csproj", "A", true, "b7e91d4", WillBuildReason.SignatureChanged,
                OwnFilesChanged: true, LastBuiltAt: builtAt),
        ]));

        var external = vm.Projects.Single(r => r.IsExternal);
        var main = vm.Projects.Single(r => !r.IsExternal);

        Assert.Equal(builtAt, external.LastBuiltAt);
        Assert.False(external.OwnFilesChanged);
        Assert.Equal(WillBuildReason.UpToDate, external.WillBuildReason);
        Assert.Equal(builtAt, main.LastBuiltAt);
        Assert.True(main.OwnFilesChanged);
    }

    [Fact]
    public async Task The_external_group_comes_first_and_is_named_External()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        ProjectNode[] topology =
        [
            ExternalNode("Mail", MailTarget),
            MainNode(@"D:\repo\a.csproj", "A", 1) with { LayerIndex = 0, LayerName = "Types" },
        ];
        vm.OnEvent(new WorkspaceTopologyEvent(topology, [], [], []));

        var groups = LayerGrouping.Build([.. vm.Projects], topology);
        Assert.Equal(ExternalProjectsConventions.LayerName, groups[0].Name);
        Assert.Equal(["Mail"], groups[0].Rows.Select(r => r.Name));
        Assert.Equal("Types", groups[1].Name);
    }

    [Fact]
    public async Task An_external_row_is_not_swept_into_the_other_group()
    {
        // Other, ana reponun sınıflanmamış projeleri içindir; harici oraya karışırsa listede en ALTA düşerdi.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        ProjectNode[] topology =
        [
            ExternalNode("Mail", MailTarget),
            MainNode(@"D:\repo\a.csproj", "A", 1) with { LayerIndex = 1, LayerName = "Other" },
        ];
        vm.OnEvent(new WorkspaceTopologyEvent(topology, [], [], []));

        var groups = LayerGrouping.Build([.. vm.Projects], topology);
        Assert.Equal([ExternalProjectsConventions.LayerName, "Other"], groups.Select(g => g.Name));
    }
}
