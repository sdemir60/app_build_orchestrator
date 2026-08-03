using System.Collections.Specialized;
using System.Diagnostics;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Envanter yayınının (<c>branchList</c> / <c>listWorktrees</c>) UI maliyeti. <b>Ölçülen kusur:</b> her Sync
/// envanteri yeniden yayınlar; <c>RunViewModel.Replace</c> N kez <c>RemoveAt</c> + N kez <c>Add</c> yaparak
/// <b>2N bildirim</b> üretir ve DÖRT abone (BranchPopover, WorktreePopover, ActionBar, MainWindow başlığı) her
/// bildirimde tüm satırlarını yeniden kurar → O(n²). Gerçek OSYS reposu 475 branch taşır (<c>refs/heads</c> +
/// <c>refs/remotes</c>) ve ölçüm bu ölçekte <b>Sync başına ~36 saniye</b> UI donması verdi.
///
/// <para>Kural: hiçbir şart ve koşulda UI thread'i bloke olmayacak — bütçe <see cref="BudgetMs"/>.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class InventoryPublishTests(ITestOutputHelper output)
{
    /// <summary>Tek bir UI thread bloğunun tavanı. 60 fps'te bir kare 16.7 ms; 50 ms = üç kare — kullanıcının
    /// "takıldı" diyemeyeceği en geniş pencere.</summary>
    private const double BudgetMs = 50;

    /// <summary>Gerçek OSYS ölçeği: <c>git branch -a</c> → 475.</summary>
    private const int OsysBranchCount = 475;

    private static List<BranchRef> Branches(int count) => Enumerable.Range(0, count)
        .Select(i => new BranchRef(i == 0 ? "main" : $"feature/branch-{i}", $"sha{i:D8}", i == 0, i > 55))
        .ToList();

    private static double MsOf(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Üretim yolu: kabuk realize (dört abone de canlı), sonra AYNI envanterin yeniden yayını —
    /// yani ikinci ve sonraki her Sync.</summary>
    [StaFact]
    public void Republishing_the_inventory_of_a_real_repository_stays_within_the_ui_budget()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var branches = Branches(OsysBranchCount);
        double first = MsOf(() => vm.OnEvent(new BranchListEvent(branches)));
        double republish = MsOf(() => vm.OnEvent(new BranchListEvent(branches)));

        output.WriteLine($"[envanter] {OsysBranchCount} branch → ilk yayın {first:N1} ms · yeniden yayın {republish:N1} ms");
        Assert.True(republish < BudgetMs,
            $"aynı envanterin yeniden yayını UI thread'ini {republish:N1} ms bloke etti — bütçe {BudgetMs:N0} ms.");
        Assert.True(first < BudgetMs,
            $"ilk envanter yayını UI thread'ini {first:N1} ms bloke etti — bütçe {BudgetMs:N0} ms.");
        GC.KeepAlive(window);
    }

    /// <summary>Değişmemiş envanter HİÇBİR bildirim üretmemeli: her Sync aynı listeyi yeniden yayınlar ve
    /// abonelerin o Sync'te yapacak işi YOKTUR.</summary>
    [StaFact]
    public void Republishing_an_unchanged_inventory_raises_no_collection_change()
    {
        var vm = NewVm();
        var branches = Branches(50);
        vm.OnEvent(new BranchListEvent(branches));

        int notifications = 0;
        vm.Branches.CollectionChanged += (_, _) => notifications++;
        vm.OnEvent(new BranchListEvent(branches));

        Assert.Equal(0, notifications);
    }

    /// <summary>DEĞİŞEN envanter de tek bildirimle yayınlanmalı — öğe başına bildirim, abone başına O(n) işi
    /// çarpan hâline getirir (kusurun kendisi budur).</summary>
    [StaFact]
    public void Publishing_a_changed_inventory_raises_exactly_one_collection_change()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Branches(50)));

        int notifications = 0;
        vm.Branches.CollectionChanged += (_, _) => notifications++;
        vm.OnEvent(new BranchListEvent(Branches(60)));

        Assert.Equal(1, notifications);
        Assert.Equal(60, vm.Branches.Count);
    }

    /// <summary>KAPALI bir popover envanter değişiminde satır İNŞA ETMEZ — açılışta zaten
    /// <c>PopoverBase.OnIsOpenChanged</c> içeriği tazeler.</summary>
    [StaFact]
    public void A_closed_branch_popover_builds_no_rows_when_the_inventory_changes()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        int baseline = DsResources.RealizedObjects(popover).Count; // kapalı, envanter boş
        vm.OnEvent(new BranchListEvent(Branches(OsysBranchCount)));
        int afterPublish = DsResources.RealizedObjects(popover).Count;

        output.WriteLine($"[kapalı popover] nesne {baseline} → {afterPublish}");
        Assert.True(afterPublish <= baseline,
            $"kapalı popover envanter yayınında {afterPublish - baseline} nesne kurdu — kapalıyken satır inşa edilmemeli.");
        GC.KeepAlive(window);
    }

    /// <summary>AÇIK bir popover 475 branch'i gösterirken de bütçe içinde kalmalı (virtualization).</summary>
    [StaFact]
    public void Opening_a_branch_popover_on_a_real_repository_stays_within_the_ui_budget()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent(Branches(OsysBranchCount)));

        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm, Width = 260, Height = 320 };
        var window = DsResources.Realize(host, popover);

        double openMs = MsOf(() => { popover.IsOpen = true; host.UpdateLayout(); });
        output.WriteLine($"[popover aç] {OsysBranchCount} branch → {openMs:N1} ms");
        Assert.True(openMs < BudgetMs,
            $"popover açılışı UI thread'ini {openMs:N1} ms bloke etti — bütçe {BudgetMs:N0} ms.");
        GC.KeepAlive(window);
    }

    private static RunViewModel NewVm() =>
        new(new BuildOrchestrator.App.Services.EngineHost(TestPaths.SupervisorExe),
            MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
}
