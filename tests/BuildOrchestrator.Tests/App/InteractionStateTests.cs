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
        // [A13/T2 · 2.4] design-v1 §2.4 — "veri yok" DEĞİL, "veri süzüldü".
        Assert.Equal("No projects match this filter.", InteractionText.NoProjectsMatchFilter);
        Assert.NotEqual(InteractionText.NoProjectsFound, InteractionText.NoProjectsMatchFilter);
    }

    // ---- [A13/T2 · 2.1] Title bar bağlamı (design-v1 §2.1) — SAF karar + verbatim metin ----

    [Fact]
    public void Title_context_texts_are_verbatim()
    {
        Assert.Equal("no repository", TitleBarContext.NoRepository);
        Assert.Equal("no repository", TitleBarContext.Compose("", ""));
        Assert.Equal("OSYS · main", TitleBarContext.Compose(@"D:\Projects\Delta\OSYS", "main"));
        Assert.Equal("· main-2", TitleBarContext.WorktreeSuffix(@"D:\OSYS", useWorktree: true, "main-2"));
    }

    [Theory] // Repo adı = kökün KLASÖR adı; sondaki ayraç(lar) yok sayılır.
    [InlineData(@"D:\Projects\Delta\OSYS", "OSYS")]
    [InlineData(@"D:\Projects\Delta\OSYS\", "OSYS")]
    [InlineData("/home/dev/osys/", "osys")]
    [InlineData("OSYS", "OSYS")]     // ayraç yok → dizenin kendisi
    [InlineData("", "")]
    public void The_repository_name_is_the_folder_name_of_the_root(string root, string expected)
        => Assert.Equal(expected, TitleBarContext.RepositoryName(root));

    [Fact]
    public void The_worktree_suffix_only_appears_for_a_real_repository_with_the_worktree_on()
    {
        Assert.Equal("", TitleBarContext.WorktreeSuffix("", useWorktree: true, "main-2"));      // repo yok
        Assert.Equal("", TitleBarContext.WorktreeSuffix(@"D:\OSYS", useWorktree: false, "main-2")); // worktree kapalı
        Assert.Equal("", TitleBarContext.WorktreeSuffix(@"D:\OSYS", useWorktree: true, null));  // ad henüz yok
    }

    // ---- ListInvite.Resolve kararı ----

    [Fact]
    public void No_repository_resolves_to_the_pick_repository_invitation()
    {
        Assert.Equal(ListInviteState.PickRepository, ListInvite.Resolve(hasWorkspace: false, AppPhase.Empty, projectCount: 0, visibleCount: 0));
    }

    [Fact]
    public void Synced_workspace_with_no_projects_resolves_to_the_no_projects_message()
    {
        Assert.Equal(ListInviteState.NoProjects, ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 0, visibleCount: 0));
    }

    [Fact]
    public void Workspace_with_projects_shows_no_invitation()
    {
        Assert.Equal(ListInviteState.None, ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 5, visibleCount: 5));
    }

    [Theory] // Boot/Syncing = "henüz bilinmiyor" — 0 satır olsa da davet gösterilmez (boş liste bırakılır).
    [InlineData(AppPhase.Boot)]
    [InlineData(AppPhase.Syncing)]
    public void Pre_sync_phases_with_zero_projects_show_no_invitation(AppPhase phase)
    {
        Assert.Equal(ListInviteState.None, ListInvite.Resolve(hasWorkspace: true, phase, projectCount: 0, visibleCount: 0));
    }

    // ---- [A13/T2 · 2.4] "filtre eşleşmedi" AYRI bir durumdur ----

    [Fact]
    public void Projects_that_the_filter_hides_resolve_to_the_no_filter_match_message()
    {
        Assert.Equal(ListInviteState.NoFilterMatch,
            ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 5, visibleCount: 0));
    }

    [Fact] // "Veri yok" kararı "veri süzüldü"den ÖNCE gelir — 0 projeli workspace'te filtreyi suçlamak yanlıştır.
    public void An_empty_workspace_is_never_blamed_on_the_filter()
    {
        Assert.Equal(ListInviteState.NoProjects,
            ListInvite.Resolve(hasWorkspace: true, AppPhase.Idle, projectCount: 0, visibleCount: 0));
    }

    [Theory] // Filtre eşleşmezliği faz-bağımsızdır: koşarken de doğru mesajdır.
    [InlineData(AppPhase.Running)]
    [InlineData(AppPhase.Boot)]
    public void The_no_filter_match_state_does_not_depend_on_the_phase(AppPhase phase)
    {
        Assert.Equal(ListInviteState.NoFilterMatch,
            ListInvite.Resolve(hasWorkspace: true, phase, projectCount: 3, visibleCount: 0));
    }

    [Fact] // Repo yokken davet KAZANIR (filtre mesajı oraya sızmaz).
    public void With_no_repository_the_invitation_wins_over_the_filter_message()
    {
        Assert.Equal(ListInviteState.PickRepository,
            ListInvite.Resolve(hasWorkspace: false, AppPhase.Empty, projectCount: 3, visibleCount: 0));
    }
}
