using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Ayarlar'daki EXTERNAL PROJECTS editörünün taslağı — saf VM (Window yok). Kullanıcı bir DİZİN seçer;
/// sürüm kontrol rozeti ve derlenecek hedef oradan TÜRETİLİR, elle yazılmaz. Taslak bir kopyadır: Save'e
/// basılmadıkça ne canlı liste ne disk değişir.
/// </summary>
public class ExternalDraftTests
{
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");
    private static readonly ExternalProject Ocr = new("Ocr", @"D:\ext\ocr", @"D:\ext\ocr\Ocr.sln");

    private static SettingsDraftViewModel Draft(params ExternalProject[] externals)
        => new(null, @"D:\repo", externals);

    [Fact]
    public void The_draft_seeds_the_saved_externals_in_order()
    {
        var draft = Draft(Ocr, Mail);

        Assert.Equal(["Ocr", "Mail"], draft.Externals.Select(r => r.Name));
        Assert.Equal(@"D:\ext\ocr\Ocr.sln", draft.Externals[0].TargetPath);
    }

    [Fact]
    public void Adding_a_git_working_copy_fills_in_the_badge_and_the_target()
    {
        using var repo = new GitTestRepo();
        File.WriteAllText(Path.Combine(repo.RootPath, "Mail.sln"), "");
        var draft = Draft();

        draft.AddExternal(repo.RootPath);

        var row = Assert.Single(draft.Externals);
        Assert.Equal(Path.GetFileName(repo.RootPath), row.Name);   // varsayılan ad = klasör adı
        Assert.Equal(Path.Combine(repo.RootPath, "Mail.sln"), row.TargetPath);
        Assert.Equal("git", row.VcsLabel);
        Assert.False(row.IsIncomplete);
    }

    [Fact]
    public void A_folder_with_two_solutions_leaves_the_target_for_the_user_to_pick()
    {
        using var repo = new GitTestRepo();
        File.WriteAllText(Path.Combine(repo.RootPath, "Mail.sln"), "");
        File.WriteAllText(Path.Combine(repo.RootPath, "Mail.Tools.sln"), "");
        var draft = Draft();

        draft.AddExternal(repo.RootPath);

        var row = Assert.Single(draft.Externals);
        Assert.Equal(string.Empty, row.TargetPath);
        Assert.True(row.IsIncomplete);
    }

    [Fact]
    public void The_badge_follows_the_project_path()
    {
        using var temp = new TempDir();
        string workspace = Path.Combine(temp.Path, "tfs");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf"));
        using var repo = new GitTestRepo();
        var draft = Draft();
        draft.AddExternal(repo.RootPath);
        Assert.Equal("git", draft.Externals[0].VcsLabel);

        draft.Externals[0].ProjectPath = workspace;

        Assert.Equal("tfvc", draft.Externals[0].VcsLabel);
    }

    [Fact]
    public void A_folder_without_version_control_is_labelled_unknown()
    {
        using var temp = new TempDir();
        var draft = Draft();

        draft.AddExternal(temp.Path);

        Assert.Equal("unknown", draft.Externals[0].VcsLabel);
    }

    [Fact]
    public void An_incomplete_row_blocks_save()
    {
        var draft = Draft();
        Assert.True(draft.CanSave);

        draft.AddExternal(@"D:\ext\no-solution-here");

        Assert.True(draft.Externals[0].IsIncomplete);
        Assert.False(draft.CanSave);
    }

    [Fact]
    public void Clearing_a_name_blocks_save_and_filling_it_unblocks()
    {
        var draft = Draft(Mail);
        draft.Externals[0].Name = "   ";
        Assert.False(draft.CanSave);

        draft.Externals[0].Name = "Mail";

        Assert.True(draft.CanSave);
    }

    [Fact]
    public void Removing_a_row_drops_it_from_the_built_list()
    {
        var draft = Draft(Mail, Ocr);

        draft.RemoveExternal(draft.Externals[0]);

        Assert.Equal(["Ocr"], draft.BuildExternals().Select(e => e.Name));
    }

    [Fact]
    public void The_built_list_keeps_the_editor_order_and_trims_names()
    {
        var draft = Draft(Mail, Ocr);
        draft.Externals[0].Name = "  Mail  ";

        var built = draft.BuildExternals();

        Assert.Equal(["Mail", "Ocr"], built.Select(e => e.Name));
        Assert.Equal(Mail.TargetPath, built[0].TargetPath);
    }

    [Fact]
    public async Task Commit_persists_the_list_and_applies_it_through_the_single_entry_point()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var draft = Draft(Mail);

        await draft.CommitAsync(run, store);

        Assert.Equal([Mail], store.Load().ExternalProjects);
        Assert.Equal(["Mail"], run.ExternalProjects!.Select(e => e.Name));
    }

    [Fact]
    public async Task Cancelling_leaves_the_live_list_untouched()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.ExternalProjects = [Mail];
        var draft = Draft(Mail);

        draft.RemoveExternal(draft.Externals[0]); // Save'e BASILMADI

        Assert.Equal(["Mail"], run.ExternalProjects.Select(e => e.Name));
        Assert.Empty(store.Load().ExternalProjects);
    }
}
