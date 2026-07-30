using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.2] Branch chip'i boş kalıyordu — çünkü <b>App <c>listBranches</c>'ı HİÇ göndermiyordu.</b>
///
/// <para><b>Ölçülen zincir (T2 envanteri):</b> <c>ActionBar.xaml.cs:362</c> chip'in değerini
/// <c>_vm.Branch</c>'ten okur · <c>RunViewModel.cs:383</c> <c>Branch</c> BOŞ başlar ·
/// <c>syncCompleted</c> <c>Branch</c>'i YAZMAZ (<c>RunViewModel.Workspace.cs:93-100</c>) ve zaten bir
/// <b>echo</b>'dur (<c>SyncWorkspaceService.cs:140</c> App'in gönderdiği branch'i geri yayınlar — boş
/// gönderilirse boş döner) · gerçek aktif branch YALNIZ <see cref="RunViewModel.Branches"/>'ten bilinebilir,
/// o da yalnız <see cref="BranchListEvent"/> ile dolar ·
/// <c>rg 'ListBranchesCommand' src/BuildOrchestrator.App</c> → <b>SIFIR SONUÇ</b> (Supervisor tarafı HAZIR:
/// <c>SupervisorHost.cs:84</c> dispatch, <c>:134</c> handler — bugüne dek yalnız TESTLER gönderiyordu).</para>
///
/// <para>Bu sınıf iki halkayı da pinler: komut GERÇEKTEN gönderiliyor mu, ve gelen envanter chip'e
/// ULAŞIYOR mu (kablo ActionBar'ın kendi <c>Branches.CollectionChanged</c> aboneliğinden geçer).</para>
/// </summary>
[Collection("Console UI (serial)")]
public class BranchInventoryTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static IReadOnlyList<BranchRef> Inventory() =>
    [
        new("main", "aaaaaaaaaaaa", true, false),
        new("feature/x", "bbbbbbbccccc", false, false),
    ];

    // ---------------------------------------------------------------- gönderim (SAF VM)

    /// <summary>Sync = "workspace bilgisini tazele" anıdır ve TEK huniden geçer: ilk repo seçimi
    /// (<c>ChangeRepositoryAsync</c> → <c>SyncAsync</c>), Settings→Change ve elle Sync hepsi buradan akar.
    /// Envanter ORADA istenmezse chip sonsuza dek boş kalır.</summary>
    [Fact]
    public async Task Sync_also_asks_the_supervisor_for_the_branch_inventory()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SyncCommand.ExecuteAsync(null);

        var list = Assert.Single(sent.OfType<ListBranchesCommand>());
        Assert.Equal(@"D:\repo", list.RootPath);
        Assert.Single(sent.OfType<SyncWorkspaceCommand>()); // Sync'in kendisi de gitmeye devam eder
    }

    /// <summary>Repo değişince liste BAYATLAR — yeni kökün envanteri istenmeli (yeni kökün yoluyla).</summary>
    [Fact]
    public async Task Changing_the_repository_re_asks_for_the_inventory_with_the_new_root()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.ChangeRepositoryAsync(@"D:\other-repo");

        var list = Assert.Single(sent.OfType<ListBranchesCommand>());
        Assert.Equal(@"D:\other-repo", list.RootPath);
    }

    // ---------------------------------------------------------------- seed (SAF VM)

    /// <summary>Envanter gelince hedef branch AKTİF branch'e seed edilir — chip'in ilk kez bir değeri olur.</summary>
    [Fact]
    public void The_inventory_seeds_the_target_branch_from_the_active_branch()
    {
        var vm = NewVm();
        Assert.Equal("", vm.Branch); // ön-koşul: bugünkü başlangıç BOŞ

        vm.OnEvent(new BranchListEvent(Inventory()));

        Assert.Equal("main", vm.Branch);
        Assert.False(vm.IsWorktreeForced); // aktif branch seçili → worktree ZORLANMAZ
    }

    /// <summary>Seed YALNIZ boşluğu doldurur: kullanıcının popover'dan yaptığı seçim ya da UiState'ten gelen
    /// seed (<c>MainWindow.xaml.cs:126</c>) EZİLMEZ — aksi halde hatırlanan branch her Sync'te kaybolurdu.</summary>
    [Fact]
    public void The_inventory_never_overwrites_a_branch_that_was_already_chosen()
    {
        var vm = NewVm();
        vm.Branch = "feature/x"; // UiState seed'i ya da kullanıcı seçimi

        vm.OnEvent(new BranchListEvent(Inventory()));

        Assert.Equal("feature/x", vm.Branch);
        Assert.True(vm.IsWorktreeForced); // aktif-OLMAYAN branch → worktree zorunlu (bu dal ilk kez canlı)
    }

    /// <summary>Envanterde aktif branch yoksa (detached HEAD / boş liste) seed YAPILMAZ — uydurma değer yok.</summary>
    [Fact]
    public void An_inventory_without_an_active_branch_seeds_nothing()
    {
        var vm = NewVm();

        vm.OnEvent(new BranchListEvent([new BranchRef("feature/x", "bbbbbbbccccc", false, false)]));

        Assert.Equal("", vm.Branch);
    }

    // ---------------------------------------------------------------- chip kablosu (GERÇEK ActionBar)

    /// <summary>ÜRETİM SIRASI (A12 dersi): bar ÖNCE realize edilir, envanter SONRA akar — kablo ActionBar'ın
    /// <c>Branches.CollectionChanged</c> + <c>PropertyChanged(Branch)</c> aboneliklerinden geçmek ZORUNDA.</summary>
    [StaFact]
    public void The_arriving_inventory_really_fills_the_branch_chip()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        // Ön-koşul: bugünkü kusur — chip BOŞ (hiçbir yerde bir branch adı yok).
        Assert.DoesNotContain("main", ChipTexts(bar));

        vm.OnEvent(new BranchListEvent(Inventory()));

        Assert.Contains("main", ChipTexts(bar));
        GC.KeepAlive(window);
    }

    private static IReadOnlyList<string> ChipTexts(ActionBar bar) =>
        [.. DsResources.Descendants(bar.BranchChip).OfType<TextBlock>().Select(t => t.Text)];

    // ---------------------------------------------------------------- 2.2'nin YAN ETKİSİ: forced dalı canlandı

    /// <summary>
    /// <b>[T2 · kayda geçmiş risk]</b> <see cref="RunViewModel.Branches"/> bugüne dek HEP boş olduğu için
    /// <see cref="RunViewModel.IsWorktreeForced"/> <b>her zaman <c>false</c></b> dönüyordu — yani
    /// <see cref="WorktreePopover"/>'ın "forced" dalı (<c>WorktreePopover.xaml.cs:84-94</c>: switch DISABLED +
    /// ZORUNLU açık + ayrı açıklama metni) üretimde HİÇ KOŞMADI ve <b>hiçbir testi de yoktu</b>
    /// (<c>rg IsWorktreeForced tests</c> yalnız iki saf VM assert'i buluyordu).
    ///
    /// <para>2.2 ile envanter gerçekten dolduğundan bu dal ilk kez erişilebilir oldu; bu yüzden GÖRSEL
    /// sonucu burada pinlenir — aksi halde "ilk kez canlanan" dal testsiz kalırdı. Metin
    /// <c>BuildApp.jsx:880-884</c>'ten BİREBİR.</para>
    /// </summary>
    [StaFact]
    public void A_non_active_branch_makes_the_worktree_switch_forced_on_and_disabled()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var popover = new WorktreePopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        vm.OnEvent(new BranchListEvent(Inventory())); // aktif = main → seed
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));

        Assert.True(vm.IsWorktreeForced);
        Assert.True(popover.PART_Switch.IsChecked);
        Assert.False(popover.PART_Switch.IsEnabled); // zorunlu → kapatılamaz (BuildApp.jsx:878)
        Assert.Equal(
            "Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched.",
            popover.PART_Desc.Text);
        GC.KeepAlive(window);
    }

    /// <summary>Aktif branch'e dönülünce zorlama KALKAR (switch yeniden kullanıcıya ait olur).</summary>
    [StaFact]
    public void Going_back_to_the_active_branch_releases_the_forced_worktree()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var popover = new WorktreePopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        vm.OnEvent(new BranchListEvent(Inventory()));
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));
        Assert.False(popover.PART_Switch.IsEnabled); // ön-koşul: zorunlu

        vm.SelectBranch(new BranchRef("main", "aaaaaaaaaaaa", true, false));

        Assert.False(vm.IsWorktreeForced);
        Assert.True(popover.PART_Switch.IsEnabled);
        GC.KeepAlive(window);
    }
}
