using System.IO;
using System.Windows.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] Settings → Workspace → <c>Worktree folder</c>: tek bir yol alanı (isim yolun son parçasıdır) ve Browse. Save'in üç hali:
/// klasör bu reponun worktree'siyse kaydedilir; yoksa ya da boşsa Save onu kurar (<see cref="CreateWorktreeCommand"/>) ve Save'in tek
/// Sync'i kurulumun cevabını bekler; başka bir şeyse Save kapalıdır ve footer nedenini söyler (<see cref="WorktreeFolder.Describe"/>).
/// Settings alt bardaki Worktree düğmesinden açıldıysa (worktree yokken) başarılı Save worktree'ye geçer.
///
/// <para>Taslak ve VM testleri WPF'sizdir; kurulum uçuşu <c>DebugSendOverride</c> ile gözlenir (gönderim başarılı, cevabı test
/// verir). Repo ve worktree gerçek git ile kurulur — denetim diskteki işaretlere bakar.</para>
/// </summary>
public class WorktreeSettingsTests
{
    private const string FullSha = "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1";

    /// <summary>Bir commit'li gerçek repo — worktree kurulumu testlerinin ORTAK fixture'ı (<see cref="EscStopTests"/> da bunu
    /// çağırır; kopyası yoktur).</summary>
    internal static GitTestRepo RepoWithCommit()
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        return repo;
    }

    /// <summary>Gönderimi başarılı bir VM — komutlar <paramref name="sent"/>'e düşer, cevabı test verir. Ortak host
    /// (<see cref="EscStopTests"/> da çağırır).</summary>
    internal static RunViewModel NewVm(string root, List<IpcCommand> sent, string worktree = "")
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = root,
            WorktreeFolder = worktree,
        };
        vm.DebugOnCommandSent = sent.Add;
        vm.DebugSendOverride = _ => Task.CompletedTask;
        return vm;
    }

    private static string Reason(WorktreeFolderState state)
    {
        string text = WorktreeFolder.Describe(state)!;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string[] Lines(RunViewModel vm) =>
        vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ================================================================ taslak

    [Fact]
    public void A_draft_opens_with_the_live_worktree_folder()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\src\acme", worktreeFolder: @"D:\src\acme-worktree");

        Assert.Equal(@"D:\src\acme-worktree", draft.WorktreeFolder);
    }

    [Fact]
    public void An_empty_worktree_folder_never_blocks_save()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\src\acme") { WorktreeFolder = "   " };

        Assert.True(draft.CanSave);
        Assert.Null(draft.SaveBlockedReason);
    }

    [Fact]
    public void A_missing_folder_or_a_linked_worktree_allows_save()
    {
        using var repo = RepoWithCommit();
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        using var parent = new TempDir();
        var draft = new SettingsDraftViewModel(null, repo.RootPath);

        draft.WorktreeFolder = Path.Combine(parent.Path, "acme-worktree");
        Assert.True(draft.CanSave);

        draft.WorktreeFolder = worktree;
        Assert.True(draft.CanSave);
    }

    [Fact]
    public void A_folder_inside_the_repository_blocks_save_with_its_reason()
    {
        using var repo = new GitTestRepo();
        var draft = new SettingsDraftViewModel(null, repo.RootPath);

        draft.WorktreeFolder = Path.Combine(repo.RootPath, "worktree");

        Assert.False(draft.CanSave);
        Assert.Equal(Reason(WorktreeFolderState.Nested), draft.SaveBlockedReason);
    }

    [Fact]
    public void A_folder_that_holds_something_else_blocks_save_with_its_reason()
    {
        using var repo = new GitTestRepo();
        using var folder = new TempDir();
        File.WriteAllText(Path.Combine(folder.Path, "notes.txt"), "mine");
        var draft = new SettingsDraftViewModel(null, repo.RootPath);

        draft.WorktreeFolder = folder.Path;

        Assert.False(draft.CanSave);
        Assert.Equal(Reason(WorktreeFolderState.Foreign), draft.SaveBlockedReason);
    }

    [Fact]
    public void A_relative_folder_blocks_save_with_its_reason()
    {
        using var repo = new GitTestRepo();
        var draft = new SettingsDraftViewModel(null, repo.RootPath);

        draft.WorktreeFolder = "acme-worktree";

        Assert.Equal(Reason(WorktreeFolderState.NotRooted), draft.SaveBlockedReason);
    }

    /// <summary>Denetim repo köküne de bakar: kök değişince karar yeniden sorulur ve Save düğmesi haber alır.</summary>
    [Fact]
    public void Changing_the_repository_root_rechecks_the_worktree_folder()
    {
        using var repo = new GitTestRepo();
        using var plain = new TempDir();
        using var parent = new TempDir();
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = Path.Combine(parent.Path, "acme-worktree") };
        Assert.True(draft.CanSave);
        int notifications = 0;
        draft.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingsDraftViewModel.CanSave)) notifications++; };

        draft.RepositoryRoot = plain.Path; // git çalışma kopyası değil

        Assert.True(notifications > 0);
        Assert.Equal(Reason(WorktreeFolderState.NoRepository), draft.SaveBlockedReason);
    }

    /// <summary>Kök boşken worktree denetimi yoktur: boş kök "workspace yok" demektir ve Save workspace'i kapatır — worktree alanı onu
    /// engellemez (kökü olmayan bir worktree saklanmaz, <see cref="Saving_a_blank_root_closes_the_workspace_and_drops_the_worktree_folder"/>).</summary>
    [Fact]
    public void A_blank_repository_root_never_blocks_save_over_the_worktree_folder()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\src\acme", worktreeFolder: @"D:\src\acme-worktree") { RepositoryRoot = "" };

        Assert.True(draft.CanSave);
        Assert.Null(draft.SaveBlockedReason);
    }

    /// <summary>Save kapısının iki yüzü (düğme ve footer) aynı (kök, klasör) için diski BİR kez okur — kapı her değişimde iki kez, bağlamalar
    /// da ayrıca sorar. Değer değişince yeniden okunur.</summary>
    [Fact]
    public void The_save_gate_reads_the_disk_once_per_root_and_folder()
    {
        int reads = 0;
        var draft = new SettingsDraftViewModel(null, @"D:\src\acme")
        {
            InspectWorktreeFolder = (_, _) => { reads++; return WorktreeFolderState.Creatable; },
        };

        draft.WorktreeFolder = @"D:\src\acme-worktree";
        _ = draft.CanSave;
        _ = draft.SaveBlockedReason;
        _ = draft.CanSave;
        Assert.Equal(1, reads);

        draft.WorktreeFolder = @"D:\src\acme-other";
        _ = draft.CanSave;
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Clear_empties_the_worktree_folder()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\src\acme", worktreeFolder: @"D:\src\acme-worktree");

        draft.ClearAll();

        Assert.True(string.IsNullOrEmpty(draft.WorktreeFolder));
    }

    /// <summary>Browse'un başlangıç klasörü: tanımlı bir worktree'nin klasörü (yoksa üst klasörü), tanımsızsa repo kökünün üst
    /// klasörü — worktree reponun yanında durur.</summary>
    [Fact]
    public void Browse_starts_next_to_the_repository()
    {
        using var repo = new GitTestRepo();
        var draft = new SettingsDraftViewModel(null, repo.RootPath);

        Assert.Equal(Path.GetDirectoryName(repo.RootPath), draft.WorktreePickerStart);

        draft.WorktreeFolder = Path.Combine(Path.GetDirectoryName(repo.RootPath)!, "not-there-yet");
        Assert.Equal(Path.GetDirectoryName(repo.RootPath), draft.WorktreePickerStart);
    }

    // ================================================================ Save → oluşturma

    [Fact]
    public async Task Saving_a_missing_folder_creates_the_worktree_and_holds_the_sync_until_it_answers()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = folder };

        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Equal(new CreateWorktreeCommand(repo.RootPath, folder), Assert.Single(sent.OfType<CreateWorktreeCommand>()));
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>()); // Save'in Sync'i kurulumun cevabını bekler
        Assert.True(vm.WorktreeBusy);
        Assert.False(vm.SyncCommand.CanExecute(null));      // butonlar pasif
        Assert.False(vm.CanSwitchCopy);
        Assert.Equal(OperationLabel.CreatingWorktree, vm.CurrentOperation);
        Assert.Equal(OperationLabel.ForWork(vm.WorkInFlight), vm.CurrentOperation); // pill sözcüğü = Esc'in adlandırdığı iş
        Assert.Equal(WorkingCopyText.Creating(folder), Lines(vm)[^1]);

        vm.OnEvent(new WorktreeCreatedEvent(folder, Created: true, FullSha, null));

        Assert.False(vm.WorktreeBusy);
        Assert.Contains(WorkingCopyText.Created(folder, "b7e91d4"), Lines(vm));
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy); // Settings'ten kurmak geçiş değildir
    }

    [Fact]
    public async Task A_failed_creation_writes_the_reason_and_still_syncs()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = folder, SwitchToWorktreeOnSave = true };
        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        vm.OnEvent(new WorktreeCreatedEvent(folder, Created: false, null, "fatal: something went wrong"));

        Assert.False(vm.WorktreeBusy);
        Assert.Contains(WorkingCopyText.NotCreated("fatal: something went wrong"), Lines(vm));
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy); // geçilecek worktree yok
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    /// <summary>Alt bardaki Worktree düğmesi worktree yokken Settings'i açar; oradan kaydetmek kurar VE geçer. Geçişin bölümü konsolu
    /// temizler ama bu Save'in notlarını ve kurulumun sonucunu silmez: ikisi de bölümün geçiş satırından önceki satırlarıdır.
    /// <para><b>[DEĞİŞEN KURAL — inceleme K3]</b> Eskiden bölüm kurulum satırıyla başlıyordu ve Save'in kendi notları (katman, klasör, General
    /// anahtarları) geçişin temizliğinde kayboluyordu; aynı Save geçiş yapmadığında o notlar konsolda kalır. Artık bölüm onlarla başlar.</para></summary>
    [Fact]
    public async Task Saving_from_the_worktree_switch_creates_the_worktree_and_switches_to_it()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = folder, SwitchToWorktreeOnSave = true };
        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());
        GitTestRepo.RunGitAt(repo.RootPath, "worktree", "add", "-q", "--detach", folder); // motorun yaptığı

        vm.OnEvent(new WorktreeCreatedEvent(folder, Created: true, FullSha, null));

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(folder, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        var lines = Lines(vm).ToList();
        int note = lines.IndexOf(WorkingCopyText.FolderChanged(folder));
        int created = lines.IndexOf(WorkingCopyText.Created(folder, "b7e91d4"));
        int switched = lines.IndexOf(WorkingCopyText.Switched(WorkingCopy.Worktree, folder));
        Assert.True(note >= 0 && note < created && created + 1 == switched, string.Join(" | ", lines));
        Assert.False(vm.WorktreeBusy);
    }

    /// <summary>Var olan worktree'ye Save'den geçiş: kurulum yok, bölüm Save'in notlarıyla başlar ve geçiş satırı onları izler.</summary>
    [Fact]
    public async Task Saving_an_existing_worktree_from_the_switch_switches_without_creating()
    {
        using var repo = RepoWithCommit();
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = worktree, SwitchToWorktreeOnSave = true };

        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Empty(sent.OfType<CreateWorktreeCommand>());
        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(worktree, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        var lines = Lines(vm).ToList();
        int note = lines.IndexOf(WorkingCopyText.FolderChanged(worktree));
        int switched = lines.IndexOf(WorkingCopyText.Switched(WorkingCopy.Worktree, worktree));
        Assert.True(note >= 0 && note < switched, string.Join(" | ", lines));
    }

    /// <summary>Kurulum yalnız bu Save'in işidir: alan bu Save'de değiştiyse ya da Settings anahtardan/karttan açıldıysa. Kayıtlı klasör
    /// diskte yoksa (elle silindi) ilgisiz bir Save (ör. bir katman) onu sessizce yeniden kurmaz — Save'in Sync'i normal gider.</summary>
    [Fact]
    public async Task An_unrelated_save_does_not_recreate_a_missing_worktree()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree"); // kayıtlı ama diskte yok
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent, folder);
        var draft = new SettingsDraftViewModel(null, repo.RootPath, worktreeFolder: folder);

        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Empty(sent.OfType<CreateWorktreeCommand>());
        Assert.False(vm.WorktreeBusy);
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    /// <summary>Kuralın öbür yarısı: Settings alt bardaki anahtardan açıldıysa kayıtlı ama diskte olmayan klasör, alan değişmese de kurulur —
    /// kullanıcı worktree'ye gitmek istedi.</summary>
    [Fact]
    public async Task Saving_from_the_switch_recreates_a_missing_worktree_even_when_the_folder_is_unchanged()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent, folder);
        var draft = new SettingsDraftViewModel(null, repo.RootPath, worktreeFolder: folder) { SwitchToWorktreeOnSave = true };

        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Equal(new CreateWorktreeCommand(repo.RootPath, folder), Assert.Single(sent.OfType<CreateWorktreeCommand>()));
        Assert.True(vm.WorktreeBusy);
    }

    /// <summary>Kök boş kaydedilince workspace kapanır ve worktree tanımı da düşer: worktree o reponundur, kökü olmayan bir worktree
    /// saklansaydı yeni bir kök seçildiğinde başka reponun klasörü Save'i kapatırdı.</summary>
    [Fact]
    public async Task Saving_a_blank_root_closes_the_workspace_and_drops_the_worktree_folder()
    {
        using var repo = RepoWithCommit();
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent, worktree);
        var draft = new SettingsDraftViewModel(null, repo.RootPath, worktreeFolder: worktree) { RepositoryRoot = "" };

        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Equal("", vm.RootPath);
        Assert.Equal("", vm.WorktreeFolder);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
    }

    /// <summary>Hazır satırı yalnız bilinen revizyonu söyler ve "detached" iddiası taşımaz: zaten var olan bir worktree bir branch'te olabilir,
    /// revizyonu okunamamış olabilir.</summary>
    [Fact]
    public void The_ready_line_names_the_revision_only_when_known()
    {
        Assert.Equal(@"Worktree ready at D:\src\acme-worktree (b7e91d4)", WorkingCopyText.Created(@"D:\src\acme-worktree", "b7e91d4"));
        Assert.Equal(@"Worktree ready at D:\src\acme-worktree", WorkingCopyText.Created(@"D:\src\acme-worktree", ""));
        Assert.Equal(@"Worktree ready at D:\src\acme-worktree", WorkingCopyText.Created(@"D:\src\acme-worktree", null));
    }

    [Fact]
    public async Task Saving_an_existing_worktree_from_settings_just_records_it()
    {
        using var repo = RepoWithCommit();
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = worktree };

        await draft.CommitAsync(vm, store);

        Assert.Empty(sent.OfType<CreateWorktreeCommand>());
        Assert.Equal(worktree, vm.WorktreeFolder);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    /// <summary>Aktif worktree'nin klasörü değişir ya da silinirse çalışılan kopya artık o değildir: kopya Repo'ya döner, Save'in Sync'i
    /// repo kökünde koşar ve eski kopyanın planı gider. Worktree'nin son Sync'i de unutulur: HEAD'i repo kopyasında kıyas tabanı olsaydı ilk
    /// HEAD tetiği sahte bir "Switched to" bölümü açardı (alt bardaki geçişle aynı kural).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changing_or_clearing_the_active_worktree_folder_returns_to_the_repository(bool clear)
    {
        using var repo = RepoWithCommit();
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        repo.CreateBranch("other");
        string otherWorktree = repo.AddWorktree("other");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);
        vm.OnEvent(new SyncStartedEvent(worktree, "side"));
        vm.OnEvent(new SyncCompletedEvent("side", "sha1234", false, 0, 0));
        Assert.NotNull(vm.LastSyncHead);
        sent.Clear();

        var draft = new SettingsDraftViewModel(null, repo.RootPath, worktreeFolder: worktree) { WorktreeFolder = clear ? "" : otherWorktree };
        await draft.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal(clear ? "" : otherWorktree, vm.WorktreeFolder);
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Null(vm.LastSyncHead);
    }

    /// <summary>Motorun reddi (koşu uçuşta — App'in kapısıyla yarışan bir koşu): kilit açılır, Save'in Sync'i de gitmez — motor meşgul.</summary>
    [Fact]
    public async Task A_rejected_creation_releases_the_buttons_and_sends_no_sync()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        await new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = folder }.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        vm.OnEvent(new ErrorEvent("worktreeRejected", "A run is in flight — stop it before creating the worktree."));

        Assert.False(vm.WorktreeBusy);
        Assert.Null(vm.CurrentOperation);
        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
    }

    /// <summary>Motorun beklenmeyen hatası başarısız bir kurulumdur: hata satırı konsoldadır, Save'in ertelenen Sync'i yine gider ve kilit
    /// Sync kapıyı devraldıktan sonra açılır (<see cref="A_failed_creation_writes_the_reason_and_still_syncs"/>'in aynısı).
    /// <para><b>[DEĞİŞEN KURAL — inceleme K2]</b> Eskiden <c>worktreeFailed</c> ret gibi ele alınıyordu: kilit açılır, Sync gitmezdi. Ama Save
    /// tek Sync'ini kurulumun cevabına ertelemişti — kök ya da kopya değiştiyse eski kopyanın planı Sync'siz ekranda kalıyordu.</para></summary>
    [Fact]
    public async Task An_engine_failure_during_creation_still_runs_the_save_s_sync()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        string folder = Path.Combine(parent.Path, "acme-worktree");
        var sent = new List<IpcCommand>();
        var vm = NewVm(repo.RootPath, sent);
        await new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = folder }.CommitAsync(vm, new SettingsDialogHost.FakeStore());

        vm.OnEvent(new ErrorEvent("worktreeFailed", "unexpected"));

        Assert.False(vm.WorktreeBusy);
        Assert.Contains("[error] worktreeFailed: unexpected", Lines(vm));
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    /// <summary>Gönderim düştüyse (motor hazır değil) cevap gelmeyecek: kilit hemen açılır.</summary>
    [Fact]
    public async Task A_creation_that_cannot_be_sent_releases_the_buttons()
    {
        using var repo = RepoWithCommit();
        using var parent = new TempDir();
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = repo.RootPath,
        };

        await new SettingsDraftViewModel(null, repo.RootPath) { WorktreeFolder = Path.Combine(parent.Path, "acme-worktree") }
            .CommitAsync(vm, new SettingsDialogHost.FakeStore());

        Assert.False(vm.WorktreeBusy);
        Assert.Null(vm.CurrentOperation);
    }

    // ================================================================ diyalog

    [StaFact]
    public void The_workspace_page_has_the_worktree_field_and_browse_fills_it()
    {
        var (dialog, run, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        string? startedAt = "unset";
        dialog.PickWorktreeFolder = start => { startedAt = start; return @"D:\src\acme-worktree"; };
        dialog.ShowSection(SettingsSection.Workspace);
        dialog.UpdateLayout();
        string? expectedStart = dialog.Draft!.WorktreePickerStart; // repo kökü D:\repo → yanındaki klasörler D:\ altında
        Assert.Equal(@"D:\", expectedStart);

        CommandPress.Click(dialog.WorktreeBrowse);
        dialog.UpdateLayout();

        Assert.Equal(@"D:\src\acme-worktree", dialog.WorktreeInput.Text);
        Assert.Equal(@"D:\src\acme-worktree", dialog.Draft.WorktreeFolder);
        Assert.Equal(expectedStart, startedAt);
        Assert.Equal("", run.WorktreeFolder); // Save'e kadar yalnız taslak
    }

    [StaFact]
    public void Opening_from_the_worktree_switch_lands_on_the_worktree_field_and_arms_the_switch()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(forWorktree: true);
        using var _scope = scope;

        Assert.True(dialog.RailItem(SettingsSection.Workspace).IsChecked);
        Assert.True(dialog.Draft!.SwitchToWorktreeOnSave);
        Assert.Same(dialog.WorktreeInput, System.Windows.Input.Keyboard.FocusedElement);
    }
}
