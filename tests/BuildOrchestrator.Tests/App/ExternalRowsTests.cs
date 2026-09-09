using System;
using System.Linq;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Harici projelerin proje listesindeki hâli: sıradan satırlardır, listenin başında dururlar ve "External"
/// katmanında toplanırlar. Ana reponun hedef commit'i onlara İTİLMEZ — o sha başka bir repoyu anlatır ve
/// harici satırın yanında yalan söylerdi.
/// </summary>
public class ExternalRowsTests
{
    private const string MailTarget = @"D:\ext\mail\Mail.sln";

    private static RunViewModel NewVm(EngineHost engine) =>
        new(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode ExternalNode(string name, string target, int order = 0) =>
        new(target, name, target, [System.IO.Path.GetFileName(target)], [], order, -1, "External", false, true,
            WillBuildReason.NeverBuilt, VcsKind.Git);

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
        Assert.False(vm.Projects[1].IsExternal);
    }

    [Fact]
    public async Task The_repository_target_sha_is_never_pushed_onto_an_external_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.OnEvent(new WorkspaceTopologyEvent(
            [ExternalNode("Mail", MailTarget), MainNode(@"D:\repo\a.csproj", "A", 1)], [], [], []));

        vm.OnEvent(new SyncCompletedEvent("main", "1111111111111111111111111111111111111111", false, 2, 0, 0, 0, 0));

        Assert.Null(vm.Projects[0].TargetSha);                       // harici
        Assert.NotNull(vm.Projects[1].TargetSha);                    // ana repo
    }

    [Fact]
    public async Task A_row_created_while_a_target_sha_is_already_known_still_stays_clean()
    {
        // Satır Sync'ten SONRA doğduğunda da itme yapılmamalı — iki ayrı yol vardır ve ikisi de kapalıdır.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.OnEvent(new WorkspaceTopologyEvent([MainNode(@"D:\repo\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "2222222222222222222222222222222222222222", false, 1, 0, 0, 0, 0));

        vm.OnEvent(new WorkspaceTopologyEvent(
            [ExternalNode("Mail", MailTarget), MainNode(@"D:\repo\a.csproj", "A", 1)], [], [], []));

        Assert.Null(vm.Projects[0].TargetSha);
    }

    [Fact]
    public async Task The_external_group_comes_first_and_is_named_External()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);

        ProjectNode[] topology = [ExternalNode("Mail", MailTarget), MainNode(@"D:\repo\a.csproj", "A", 1)];
        vm.OnEvent(new WorkspaceTopologyEvent(topology, [], [], []));

        var groups = LayerGrouping.Build([.. vm.Projects], topology);
        Assert.Equal("External", groups[0].Name);
        Assert.Equal(["Mail"], groups[0].Rows.Select(r => r.Name));
    }
}
