using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    /// (<c>ChangeRepositoryAsync</c> → <c>SyncAsync</c>), Settings→Save ve elle Sync hepsi buradan akar.
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
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-7/8]</b> Branch değeri checkout edilmiş branch'in KENDİSİDİR:
    /// envanterin aktif branch'i neyse o. Popover'dan aktif olmayan bir branch'e tıklamak (checkout Task 5'te
    /// gelene dek) değeri DEĞİŞTİRMEZ; terminalde yapılan checkout bir sonraki envanterle değere yansır;
    /// detached HEAD'de (aktif branch yok) son bilinen değer durur.
    ///
    /// <para><b>Eski iddia</b> (<c>The_inventory_never_overwrites_an_explicit_user_choice</c>): kullanıcının
    /// popover'dan yaptığı AÇIK seçim bir niyetti ve envanter onu ezmezdi — aktif olmayan branch'in committed
    /// HEAD'i worktree'de derlenirdi. <b>Değişme gerekçesi:</b> worktree kalktı (§1-1); araç yalnız çalışma
    /// ağacında derler, dolayısıyla "seçili ama checkout edilmemiş branch" diye bir hedef yoktur. Branch
    /// değiştirmenin tek yolu gerçek bir checkout'tur (§1-8).</para>
    /// </summary>
    [Fact]
    public void The_branch_value_follows_the_checked_out_branch()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));                                  // aktif = main
        Assert.Equal("main", vm.Branch);

        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));     // tık: checkout YOK
        Assert.Equal("main", vm.Branch);

        vm.OnEvent(new BranchListEvent([                                               // terminalde checkout
            new BranchRef("main", "aaaaaaaaaaaa", false, false),
            new BranchRef("feature/x", "bbbbbbbccccc", true, false),
        ]));
        Assert.Equal("feature/x", vm.Branch);

        vm.OnEvent(new BranchListEvent([                                               // detached HEAD
            new BranchRef("main", "aaaaaaaaaaaa", false, false),
            new BranchRef("feature/x", "bbbbbbbccccc", false, false),
        ]));
        Assert.Equal("feature/x", vm.Branch);                                          // son değer durur
    }

    /// <summary>
    /// [spec 2026-09-18 §1-8] <c>origin/HEAD</c> bir branch değil, uzak deponun varsayılan branch'ine işaret
    /// eden bir sembolik ref'tir; checkout hedefi olarak sunulmaz. Süzme VM'de yapılır — popover ve bar aynı
    /// envanteri okur.
    /// </summary>
    [Fact]
    public void Remote_head_is_not_offered()
    {
        var vm = NewVm();

        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("origin/HEAD", "aaaaaaaaaaaa", false, true),
            new BranchRef("origin/main", "aaaaaaaaaaaa", false, true),
        ]));

        Assert.Equal(["main", "origin/main"], vm.Branches.Select(b => b.Name));
    }

    /// <summary>
    /// <b>[T2 fix-1 · C1 — kritik regresyon]</b> Bayat bir <c>Branch</c> değeri envanterle TAZELENİR.
    ///
    /// <para>Kapatılan senaryo: ilk Sync <c>Branch="main"</c> yazıyordu; kullanıcı terminalde
    /// <c>git checkout feature/y</c> yapınca seed YALNIZ boşken koştuğu için uygulama kendini ASLA
    /// düzeltemiyordu. [spec 2026-09-18 §1-7] Değer artık diskten seed edilmez; kural her envanterde
    /// geçerlidir.</para>
    /// </summary>
    [Fact]
    public void A_stale_branch_from_disk_is_refreshed_to_whatever_is_actually_checked_out()
    {
        var vm = NewVm();
        vm.Branch = "main"; // bayat değer

        // Kullanıcı terminalde `git checkout feature/y` yaptı → envanterde aktif branch ARTIK feature/y.
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", false, false),
            new BranchRef("feature/y", "ccccccceeeee", true, false),
        ]));

        Assert.Equal("feature/y", vm.Branch);   // uygulama kendini DÜZELTTİ
    }

    /// <summary>Sync branch değerini taşır — orada branch <c>git fetch origin &lt;ref&gt;</c>'in ref'ini ve
    /// echo'yu besler. Boş göndermek fetch'i boş ref'e yollardı.</summary>
    [Fact]
    public async Task Sync_still_carries_the_display_branch_because_it_only_drives_the_fetch_ref()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Equal("main", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Branch);
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
}
