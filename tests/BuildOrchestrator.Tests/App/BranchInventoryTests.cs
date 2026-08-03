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

    /// <summary>[T2 fix-1 · I-G] Worktree envanteri de istenir — branch'in birebir simetriği. Gönderilmediği
    /// sürece <see cref="RunViewModel.Worktrees"/> boş kalıyor ve <see cref="RunViewModel.AutoWorktreeName"/>
    /// mevcutları hep 0 sayıp ÇAKIŞAN bir ad öneriyordu.</summary>
    [Fact]
    public async Task Sync_also_asks_for_the_worktree_inventory()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\repo", Assert.Single(sent.OfType<ListWorktreesCommand>()).RootPath);
    }

    /// <summary>[T2 fix-1 · I-G] Envanter geldiğinde otomatik ad ÇAKIŞMAYI önler: havuzda <c>main-1</c> varken
    /// önerilen ad <c>main-2</c> olur. Envanter hiç gelmezse (eski davranış) hep <c>main-1</c> önerilirdi.</summary>
    [Fact]
    public void The_worktree_inventory_makes_the_auto_name_avoid_a_collision()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));
        Assert.Equal("main-1", vm.EffectiveWorktreeName); // envanter yokken

        vm.OnEvent(new WorktreeListEvent([new Worktree("main-1", "main", @"D:\pool\main-1", false, 0)]));

        Assert.Equal("main-2", vm.EffectiveWorktreeName);
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

    /// <summary>
    /// [T2 fix-1 · C1] Kullanıcının POPOVER'DAN yaptığı AÇIK seçim korunur — o bir niyettir, varsayılan değil.
    /// </summary>
    [Fact]
    public void The_inventory_never_overwrites_an_explicit_user_choice()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));                                  // seed → "main"
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));     // AÇIK seçim
        Assert.True(vm.BranchChosenByUser);

        vm.OnEvent(new BranchListEvent(Inventory()));                                  // ikinci Sync

        Assert.Equal("feature/x", vm.Branch);
        Assert.True(vm.IsWorktreeForced);
    }

    /// <summary>
    /// <b>[T2 fix-1 · C1 — kritik regresyon]</b> DİSKTEN gelen bayat <c>Branch</c> (UiState seed'i,
    /// <c>MainWindow.xaml.cs:128</c>) bir açık seçim DEĞİLDİR ve envanterle TAZELENİR.
    ///
    /// <para>Kapatılan senaryo: ilk Sync <c>Branch="main"</c> yazıp diske persist ediyordu; kullanıcı terminalde
    /// <c>git checkout feature/y</c> yapınca seed YALNIZ boşken koştuğu için uygulama kendini ASLA
    /// düzeltemiyordu — build sessizce <c>main</c>'in committed HEAD'ini zorunlu bir worktree'de derliyordu.</para>
    /// </summary>
    [Fact]
    public void A_stale_branch_from_disk_is_refreshed_to_whatever_is_actually_checked_out()
    {
        var vm = NewVm();
        vm.Branch = "main"; // UiState seed'i (açık seçim DEĞİL)
        Assert.False(vm.BranchChosenByUser);

        // Kullanıcı terminalde `git checkout feature/y` yaptı → envanterde aktif branch ARTIK feature/y.
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", false, false),
            new BranchRef("feature/y", "ccccccceeeee", true, false),
        ]));

        Assert.Equal("feature/y", vm.Branch);   // uygulama kendini DÜZELTTİ
        Assert.False(vm.IsWorktreeForced);      // aktif branch → zorlama YOK
        Assert.False(vm.EffectiveUseWorktree);  // ...ve in-place build
    }

    /// <summary>Açık seçim yapılmış branch envanterden SİLİNMİŞSE seçim düşer ve aktife dönülür — aksi halde
    /// build "no commit could be resolved" ile zorunlu-worktree yolunda ölürdü.</summary>
    [Fact]
    public void An_explicit_choice_that_disappeared_from_the_inventory_falls_back_to_the_active_branch()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));

        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)])); // feature/x silindi

        Assert.Equal("main", vm.Branch);
        Assert.False(vm.BranchChosenByUser);
    }

    // -------------------------------------------------- C1 (a): UI motorun yapacağının TERSİNİ göstermez

    /// <summary>
    /// <b>[T2 fix-1 · C1 — regresyon (a)]</b> Bayat <c>Branch</c> + farklı aktif branch senaryosunda chip
    /// <c>"off"</c> göstermez ve komuta <c>UseWorktree=true</c> gider.
    ///
    /// <para>Not: C1 fix'i bu senaryoyu KÖKÜNDEN de kapatır (bayat değer tazelenir). Bu test, zorlamanın
    /// gerçekten oluştuğu yoldan — AÇIK seçim — aynı değişmezi sürer: <b>forced ⇒ UI ve komut worktree
    /// gösterir</b>.</para>
    /// </summary>
    [StaFact]
    public async Task A_forced_worktree_is_never_displayed_or_sent_as_off()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        vm.OnEvent(new BranchListEvent(Inventory()));
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));
        vm.UseWorktree = false; // kullanıcının KENDİ tercihi kapalı olsa bile zorlama üstündedir

        Assert.True(vm.IsWorktreeForced);
        Assert.True(vm.EffectiveUseWorktree);
        Assert.DoesNotContain("off", ChipTexts(bar));   // chip "off" DEMEZ

        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.True(sent.UseWorktree);                  // motora da worktree gider
        Assert.Equal("feature/x", sent.Branch);         // açık seçim NİYET olarak gider
        GC.KeepAlive(window);
    }

    // -------------------------------------------------- C1 (b): yasak kombinasyon üretilemez

    /// <summary><b>[T2 fix-1 · C1 — regresyon (b)]</b> <c>forced == true &amp;&amp; EffectiveUseWorktree == false</c>
    /// kombinasyonu ARTIK ÜRETİLEMEZ — hangi yoldan girilirse girilsin.</summary>
    [Theory]
    [InlineData(true)]   // kullanıcı toggle'ı kapalı
    [InlineData(false)]  // kullanıcı toggle'ına hiç dokunmadı
    public void The_forced_but_worktree_off_combination_cannot_be_produced(bool explicitlyTurnOff)
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));
        if (explicitlyTurnOff) vm.UseWorktree = false;

        Assert.True(vm.IsWorktreeForced);
        Assert.True(vm.EffectiveUseWorktree); // yasak kombinasyon YOK
    }

    /// <summary>Zorlama bir KATMANDIR, kullanıcının tercihini kalıcı olarak EZMEZ: aktif branch'e dönünce
    /// kullanıcının kendi <c>UseWorktree</c> değeri neyse ona geri düşülür (prototip <c>wtActive = forced || wtOn</c>,
    /// <c>BuildApp.jsx:1153</c>). Kalıcı duruma yazılan da kullanıcının kendi değeridir.</summary>
    [Fact]
    public void The_forcing_layer_does_not_permanently_overwrite_the_users_own_toggle()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory()));
        vm.UseWorktree = false;                                                       // kullanıcı: kapalı
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));    // forced katmanı
        Assert.True(vm.EffectiveUseWorktree);

        vm.SelectBranch(new BranchRef("main", "aaaaaaaaaaaa", true, false));          // aktife dön
        vm.UseWorktree = false;                                                        // (SelectBranch açmıştı)

        Assert.False(vm.IsWorktreeForced);
        Assert.False(vm.EffectiveUseWorktree);
    }

    // -------------------------------------------------- C1/I4 (c): in-place Build hâlâ koşar

    /// <summary>
    /// <b>[T2 fix-1 · I4 — regresyon (c)]</b> Açık seçim YOKKEN <see cref="StartRunCommand.Branch"/> BOŞ gider.
    ///
    /// <para>Neden kritik: Supervisor bu alanı bir NİYET olarak okur. Dolu gelirse (a) worktree zorunlu olur
    /// (<c>Program.cs:215-216</c>) ve (b) "aktif branch çözülemedi" (detached HEAD / bozuk git) durumu
    /// <c>warn + in-place</c> yerine run'ı HİÇ BAŞLATMAYAN bir hataya düşer (<c>:207-208</c>). Boş gitmesi,
    /// <c>Program.cs:183</c>'ün "toggle kapalı + branch boş ⇒ tek git çağrısı bile yapmadan in-place" dalını
    /// korur — yani detached HEAD'de de in-place Build koşmaya devam eder.</para>
    /// </summary>
    [Fact]
    public async Task Without_an_explicit_choice_the_run_command_carries_no_branch_intent()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Inventory())); // Branch görüntüleme değeri "main" olur…
        Assert.Equal("main", vm.Branch);
        Assert.False(vm.BranchChosenByUser);

        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal("", sent.Branch);      // …ama NİYET boş gider (in-place korunur)
        Assert.False(sent.UseWorktree);
    }

    /// <summary>Sync ise görüntüleme değerini KULLANIR — orada branch yalnız <c>git fetch origin &lt;ref&gt;</c>'in
    /// ref'ini ve echo'yu besler, worktree matrisini DEĞİL. Boş göndermek fetch'i boş ref'e yollardı.</summary>
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

    /// <summary>
    /// <b>[T2 fix-3 · round-3 bulgu 1]</b> Worktree envanteri geldiğinde <c>ActionBar</c>'ın worktree chip'i
    /// (auto-ad dalı) da tazelenir.
    ///
    /// <para><b>Ölçülen kusur:</b> I-G ile <c>ListWorktreesCommand</c> gönderilip <see cref="RunViewModel.Worktrees"/>
    /// canlı dolmaya başladı; <see cref="RunViewModel.EffectiveWorktreeName"/>'in auto-ad dalı
    /// (<see cref="RunViewModel.AutoWorktreeName"/>) mevcut worktree sayısını bu koleksiyondan sayar, yani
    /// envanter gelince gösterilen ad değişebilir (<c>main-1</c> → <c>main-2</c>). Title bar
    /// (<c>MainWindow.xaml.cs</c>) ve <see cref="WorktreePopover"/> zaten <c>Worktrees.CollectionChanged</c>'e
    /// abone; <c>ActionBar</c> DEĞİLDİ — chip bayat adı göstermeye devam ediyordu (üç yüzey iki farklı ad
    /// söylüyordu).</para>
    /// </summary>
    [StaFact]
    public void The_arriving_worktree_inventory_refreshes_the_worktree_chip()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        vm.OnEvent(new BranchListEvent(Inventory())); // Branch seed → "main" (aktif), havuz henüz boş
        vm.UseWorktree = true; // chip yalnız EffectiveUseWorktree açıkken adı gösterir ("off" değil)
        Assert.Contains("main-1", ChipTexts(bar, bar.WorktreeChip)); // ön-koşul: auto-ad "main-1"

        // main-1 ZATEN dolu → auto-ad "main-2"ye kaymalı. Kablo ActionBar'ın KENDİ Worktrees.CollectionChanged
        // aboneliğinden geçmek ZORUNDA (üretim sırası: bar önce realize, envanter sonra akar).
        vm.OnEvent(new WorktreeListEvent([new Worktree("main-1", "main", @"D:\pool\main-1", false, 0)]));

        Assert.Contains("main-2", ChipTexts(bar, bar.WorktreeChip));
        Assert.DoesNotContain("main-1", ChipTexts(bar, bar.WorktreeChip));
        GC.KeepAlive(window);
    }

    private static IReadOnlyList<string> ChipTexts(ActionBar bar) => ChipTexts(bar, bar.BranchChip);

    private static IReadOnlyList<string> ChipTexts(ActionBar bar, ToggleButton chip) =>
        [.. DsResources.Descendants(chip).OfType<TextBlock>().Select(t => t.Text)];

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
