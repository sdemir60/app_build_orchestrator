using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/T10] Etkileşim/boş-durum metinleri (design-v1 README §"empty" + BuildApp.jsx birebir) BYTE-EXACT pinlenir
/// ve <see cref="ListInvite.Resolve"/> davet kararı doğrulanır. SAF — WPF YOK.
/// </summary>
public class InteractionStateTests
{
    // ---- Verbatim (byte-exact) davet/boş-durum metinleri ----

    /// <summary>[design v1.8.0 §2.4 · v1.10.0] First run kurulum davetinin metinleri BİREBİR.
    /// <para><b>[DEĞİŞEN KURAL]</b> Davet eskiden bir klasör seçiciye açılıyordu ve üç metni vardı:
    /// <c>Pick a repository to get started</c> · <c>Point to the OSYS solution root — …</c> ·
    /// <c>Choose Folder</c>. v1.8.0 onu KALDIRDI — başlamak için gereken ayar sayısı arttığından (repository
    /// root + katman tanımları) boş durum Settings'e yönlendiriyor; v1.10.0 ikinci bir düğme ekledi.</para></summary>
    [Fact]
    public void First_run_setup_invitation_texts_are_verbatim()
    {
        Assert.Equal("Configure the workspace", InteractionText.ConfigureWorkspaceTitle);
        Assert.Equal(
            "Set the repository root — and, if projects should be grouped, the layers. Discovery starts right after.",
            InteractionText.ConfigureWorkspaceSubtitle);
        Assert.Equal("Repository root", InteractionText.SetupRepositoryRootLabel);
        Assert.Equal("Layers", InteractionText.SetupLayersLabel);
        Assert.Equal("Not set", InteractionText.SetupRootNotSet);
        Assert.Equal("Optional", InteractionText.SetupLayersOptional);
        Assert.Equal("6 defined", InteractionText.SetupLayersDefined(6));
        Assert.Equal("Open settings", InteractionText.OpenSettingsButton);
        Assert.Equal("Import settings…", InteractionText.ImportSettingsButton);
        Assert.Equal(
            "Import fills the form from a settings file — nothing is applied until you save.",
            InteractionText.ImportSettingsNote);
    }

    [Fact]
    public void Zero_project_and_panel_empty_texts_are_verbatim()
    {
        Assert.Equal("No projects found under this folder.", InteractionText.NoProjectsFound);
        Assert.Equal("Graph appears after Sync", InteractionText.GraphEmpty);
        // Event stream'in boş-durum metni KALDIRILDI (kullanıcı kararı) — panel boşken bekleme satırı konuşur;
        // pinleyen test: EventStreamIdlePromptTests.The_prompt_line_is_there_from_the_first_frame_...
        // [A13/T2 · 2.4] design-v1 §2.4 — "veri yok" DEĞİL, "veri süzüldü".
        Assert.Equal("No projects match this filter.", InteractionText.NoProjectsMatchFilter);
        Assert.NotEqual(InteractionText.NoProjectsFound, InteractionText.NoProjectsMatchFilter);
    }

    // ---- [design v1.11.0 §2.7-5a] Alt bardaki workspace etiketi — SAF karar ----

    /// <summary><b>[DEĞİŞEN KURAL]</b> Burada eskiden title bar'ın mono BAĞLAM metni pinleniyordu
    /// (<c>no repository</c> · <c>OSYS · main</c> · worktree eki <c>· main-2</c>). design-v1.11.0 §2.1 o metni
    /// KALDIRDI — branch ve worktree zaten alt bardaki chip'lerdeydi — ve geriye kalan tek yeni bilgiyi,
    /// workspace adını, alt bara taşıdı (§2.7-5a). <c>Compose</c>/<c>WorktreeSuffix</c>/<c>NoRepository</c>
    /// tüketicisiz kaldıkları için silindi; onları pinleyen üç test de bu tek teste indi. Adın kendisi
    /// (kökün klasör adı) DEĞİŞMEDİ.</summary>
    [Theory] // Repo adı = kökün KLASÖR adı; sondaki ayraç(lar) yok sayılır.
    [InlineData(@"D:\Projects\Delta\OSYS", "OSYS")]
    [InlineData(@"D:\Projects\Delta\OSYS\", "OSYS")]
    [InlineData("/home/dev/osys/", "osys")]
    [InlineData("OSYS", "OSYS")]     // ayraç yok → dizenin kendisi
    [InlineData("", "")]
    public void The_repository_name_is_the_folder_name_of_the_root(string root, string expected)
        => Assert.Equal(expected, TitleBarContext.RepositoryName(root));

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
