using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D11] Harici projeler tel üzerinde SIRADAN <see cref="ProjectNode"/> olarak akar: ayrı bir düğüm tipi
/// yoktur. Böylece liste gruplaması, graf bandı ve filtreler onları bedavaya taşır — tek fark katman
/// ataması ve VCS rozetidir.
/// </summary>
public class ExternalNodeBuilderTests
{
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");

    private static ExternalInspection Inspection(bool? willBuild = true, VcsKind vcs = VcsKind.Git) =>
        new(Mail, vcs, "abc123", willBuild, willBuild is true ? WillBuildReason.NeverBuilt : null, false, null);

    [Fact]
    public void The_node_is_identified_by_its_build_target()
    {
        // Kimlik TargetPath'tir: build-state anahtarı, proje logu ve satır Id'si aynı değerdir.
        var node = ExternalNodeBuilder.ToNode(Inspection(), order: 0);

        Assert.Equal(Mail.TargetPath, node.Id);
        Assert.Equal(Mail.TargetPath, node.ProjectPath);
        Assert.Equal("Mail", node.Name);
    }

    [Fact]
    public void The_node_sits_in_the_external_layer()
    {
        var node = ExternalNodeBuilder.ToNode(Inspection(), order: 3);

        Assert.Equal(ExternalProjectsConventions.LayerName, node.LayerName);
        Assert.Equal(ExternalProjectsConventions.LayerIndex, node.LayerIndex);
        Assert.Equal(3, node.BuildOrder);
    }

    [Fact]
    public void The_node_has_no_dependencies_and_is_never_in_a_cycle()
    {
        var node = ExternalNodeBuilder.ToNode(Inspection(), order: 0);

        Assert.Empty(node.Dependencies);
        Assert.False(node.InCycle);
    }

    [Fact]
    public void The_solution_name_is_what_the_user_sees_in_the_group_chip()
    {
        var node = ExternalNodeBuilder.ToNode(Inspection(), order: 0);

        Assert.Equal(["Mail.sln"], node.SolutionNames);
    }

    [Fact]
    public void The_vcs_badge_comes_from_the_inspection()
    {
        Assert.Equal(VcsKind.Tfvc, ExternalNodeBuilder.ToNode(Inspection(vcs: VcsKind.Tfvc), 0).ExternalVcs);
        Assert.Equal(VcsKind.Unknown, ExternalNodeBuilder.ToNode(Inspection(vcs: VcsKind.Unknown), 0).ExternalVcs);
    }

    [Fact]
    public void A_hollow_inspection_produces_a_hollow_node()
    {
        var node = ExternalNodeBuilder.ToNode(Inspection(willBuild: null, vcs: VcsKind.Tfvc), order: 0);

        Assert.Null(node.WillBuild);
        Assert.Null(node.WillBuildReason);
    }
}
