using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/T10] Etkileşim/boş-durum metinleri (design-v1 README §"empty" + BuildApp.jsx birebir) BYTE-EXACT pinlenir
/// ve <see cref="ListInvite.Resolve"/> davet kararı doğrulanır. SAF — WPF YOK.
/// </summary>
public class InteractionStateTests
{
    // ---- Verbatim (byte-exact) davet/boş-durum metinleri ----

    [Fact]
    public void Pick_repository_invitation_texts_are_verbatim()
    {
        Assert.Equal("Pick a repository to get started", InteractionText.PickRepositoryTitle);
        Assert.Equal(
            "Point to the OSYS solution root — projects and the dependency graph are discovered automatically.",
            InteractionText.PickRepositorySubtitle);
        Assert.Equal("Choose Folder", InteractionText.ChooseFolderButton);
    }

    [Fact]
    public void Zero_project_and_panel_empty_texts_are_verbatim()
    {
        Assert.Equal("No projects found under this folder.", InteractionText.NoProjectsFound);
        Assert.Equal("Graph appears after Sync", InteractionText.GraphEmpty);
        Assert.Equal("No events yet.", InteractionText.StreamEmpty);
    }

    // ---- ListInvite.Resolve kararı ----

    [Fact]
    public void No_repository_resolves_to_the_pick_repository_invitation()
    {
        Assert.Equal(ListInviteState.PickRepository, ListInvite.Resolve(hasWorkspace: false, AppPhase.Empty, projectCount: 0));
    }

    [Fact]
    public void Synced_workspace_with_no_projects_resolves_to_the_no_projects_message()
    {
        Assert.Equal(ListInviteState.NoProjects, ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 0));
    }

    [Fact]
    public void Workspace_with_projects_shows_no_invitation()
    {
        Assert.Equal(ListInviteState.None, ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 5));
    }

    [Theory] // Boot/Syncing = "henüz bilinmiyor" — 0 satır olsa da davet gösterilmez (boş liste bırakılır).
    [InlineData(AppPhase.Boot)]
    [InlineData(AppPhase.Syncing)]
    public void Pre_sync_phases_with_zero_projects_show_no_invitation(AppPhase phase)
    {
        Assert.Equal(ListInviteState.None, ListInvite.Resolve(hasWorkspace: true, phase, projectCount: 0));
    }
}
