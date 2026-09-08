using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Bir popover AÇIKKEN tetikleyicisine basmak onu <b>KAPATIR</b> — tasarımın kuralı budur ve prototipte
/// açıkça yazılıdır: chip'ler <c>setBranchPop(!branchPop)</c> ile DEĞİŞTİRİR (BuildApp.jsx:2399, :2404,
/// :2420), satır menüsü ise erken çıkışla <c>if (menuOpen) { setMenu(null); return; }</c> der
/// (BuildApp.jsx:657).
///
/// <para><b>Ölçülen kusur.</b> WPF'te <c>StaysOpen="False"</c> bir <see cref="Popup"/>, dışına düşen bir fare
/// basışında KENDİ kapanma yolundan geçer (<c>IsOpen=false</c>) ve bu, iki-yönlü bağ üzerinden tetikleyicinin
/// <c>IsChecked</c>'ını da düşürür. Basış oradan tetikleyiciye ULAŞIR ve onu yeniden işaretler → popover
/// kapanıp AYNI jestte tekrar açılır. Kullanıcı bunu "tekrar tıklayınca yine açılıyor" diye tarif etti.
/// Repoda bu ikili adımı kesen hiçbir kod yoktu.</para>
///
/// <para><b>Testin sürdüğü sıra ÜRETİMİN sırasıdır</b>, tahmini değil: (1) popover açık, (2) WPF'in capture
/// yolu popup'ı kapatır, (3) aynı basış tetikleyiciye ulaşır. (3)'ün önizleme yarısı ayrı yükseltilir çünkü
/// karar YALNIZ orada verilebilir — handled bir <c>PreviewMouseLeftButtonDown</c>'da <c>ButtonBase</c> fareyi
/// hiç yakalamaz, dolayısıyla bırakmada <c>Click</c> (ve <c>ToggleButton</c>'ın native toggle'ı) hiç doğmaz.
/// Önizleme yutulmadıysa test o native toggle'ı süitin yerleşik kuralıyla taklit eder
/// (<c>ActionBarTests:523</c> "native toggle-on-click'i simüle et").</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class PopoverToggleTests
{
    private const string RowId = @"C:\p\a.csproj";

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };

    private static (ActionBar bar, Window window) RealizeBar(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        return (bar, DsResources.Realize(host, bar, 1200, 200));
    }

    private static ProjectRowActions RealizeRowActions(RunViewModel vm, out Window window)
    {
        var row = new ProjectRow { DataContext = new ProjectRowViewModel(RowId, "A", ProjectRowState.Pending) };
        var shell = new Border { DataContext = vm, Child = row };
        window = DsResources.Realize(DsResources.NewHost(), shell, 900, 120);
        row.SimulateHover(true); // hover bloğu TALEP ÜZERİNE kurulur (ProjectRow.EnsureActions)
        row.UpdateLayout();
        return row.Actions!;
    }

    /// <summary>
    /// Açık bir popover'ın tetikleyicisine basmanın ÜRETİMDEKİ tam sırası. Kapalı kaldıysa yeşil.
    /// </summary>
    /// <summary>
    /// Popup'ı GERÇEKTEN açar. <c>IsOpen=true</c> tek başına yetmez: kendi penceresi ancak dispatcher
    /// pompalanınca doğar ve <see cref="Popup.Closed"/> de ancak o zaman ateşlenir — <c>IsOpen</c>'a bakan
    /// bir bekleme koşulu anında doğru çıkar ve hiç pompalamaz, yani popup hiç açılmamış olur.
    /// </summary>
    private static void OpenForReal(Popup popup)
    {
        bool opened = false;
        void OnOpened(object? s, EventArgs e) => opened = true;
        popup.Opened += OnOpened;
        popup.IsOpen = true;
        DispatcherPump.PumpUntil(() => opened, TimeSpan.FromSeconds(2));
        popup.Opened -= OnOpened;
        Assert.True(opened, "ön-koşul: popover GERÇEKTEN açılmadı");
    }

    private static MouseButtonEventArgs ClickTriggerWhilePopupIsOpen(ButtonBase trigger, Popup popup)
    {
        OpenForReal(popup);

        // (2) WPF'in StaysOpen=False capture yolu: dışa düşen basış popup'ı kapatır.
        popup.IsOpen = false;

        // (3) Aynı basış tetikleyiciye ulaşır — önce tünelleyen yarısı (karar burada verilir).
        var preview = MouseInput.PreviewPressLeft(trigger);
        if (!preview.Handled && trigger is ToggleButton toggle) toggle.IsChecked = !toggle.IsChecked;
        return preview;
    }

    [StaFact]
    public void Clicking_the_branch_chip_while_its_popover_is_open_closes_it_instead_of_reopening()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        ClickTriggerWhilePopupIsOpen(bar.BranchChip, bar.BranchPopup);

        Assert.False(bar.BranchPopup.IsOpen, "branch popover'ı aynı tıkta yeniden açıldı");
        Assert.False(bar.BranchChip.IsChecked);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_worktree_chip_while_its_popover_is_open_closes_it_instead_of_reopening()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        ClickTriggerWhilePopupIsOpen(bar.WorktreeChip, bar.WorktreePopup);

        Assert.False(bar.WorktreePopup.IsOpen, "worktree popover'ı aynı tıkta yeniden açıldı");
        Assert.False(bar.WorktreeChip.IsChecked);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_build_chevron_while_its_menu_is_open_closes_it_instead_of_reopening()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        ClickTriggerWhilePopupIsOpen(bar.Split.MenuToggle!, bar.Split.MenuPopup!);

        Assert.False(bar.Split.MenuPopup!.IsOpen, "build menüsü aynı tıkta yeniden açıldı");
        Assert.False(bar.Split.IsMenuOpen);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_row_ellipsis_while_its_menu_is_open_closes_it_instead_of_reopening()
    {
        var vm = NewVm();
        var actions = RealizeRowActions(vm, out var window);

        ClickTriggerWhilePopupIsOpen(actions.MoreButton, actions.RowMenu);

        Assert.False(actions.RowMenu.IsOpen, "satır menüsü aynı tıkta yeniden açıldı");
        Assert.False(actions.MoreButton.IsChecked);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// VS seçici bir <see cref="ToggleButton"/> DEĞİL düz bir <c>Button</c> ile açılır (popover'ı
    /// <c>ProjectRow.OnVsClick</c> açar), yani aynı kusur burada <c>Click</c> handler'ı üzerinden çıkar.
    /// Kapıyı bu yüzden <c>ButtonBase</c> seviyesinde tutmak gerekir — <c>ToggleButton</c>'a özel bir çözüm
    /// bu beşinci yeri kaçırırdı.
    /// </summary>
    [StaFact]
    public void Clicking_the_vs_button_while_its_chooser_is_open_closes_it_instead_of_reopening()
    {
        var vm = NewVm();
        var actions = RealizeRowActions(vm, out var window);

        var preview = ClickTriggerWhilePopupIsOpen(actions.VsButton, actions.VsChooser);

        Assert.True(preview.Handled,
            "VS seçici açıkken düğmeye basış yutulmadı — Click handler'ı seçiciyi aynı jestte yeniden açar");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Koruma penceresi JESTE aittir, düğmeye değil. Popover başka bir nedenle kapandıysa (Esc, başka bir
    /// yere tık) SONRAKİ tık onu normal şekilde AÇMALIDIR — aksi halde kapı kullanıcıyı bir tık boyunca
    /// sağır bırakırdı. Bayrağı "bir sonraki basışta temizle" diyen zamansız bir çözüm tam da bunu yapardı;
    /// pencerenin zamana bağlı olmasının nedeni budur.
    /// </summary>
    [StaFact]
    public void A_click_that_arrives_after_the_guard_window_opens_the_popover_normally()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        OpenForReal(bar.BranchPopup);
        bar.BranchPopup.IsOpen = false;

        double closedAt = PopoverToggle.DefaultClock();
        PopoverToggle.Now = () => closedAt + PopoverToggle.GuardMs + 1; // jestin penceresi kapandı
        try
        {
            var preview = MouseInput.PreviewPressLeft(bar.BranchChip);
            Assert.False(preview.Handled, "koruma penceresi geçtikten sonra gelen tık yutulmamalı");

            bar.BranchChip.IsChecked = true; // native toggle-on-click
            Assert.True(bar.BranchPopup.IsOpen, "pencere geçtikten sonraki tık popover'ı AÇMALI");
        }
        finally
        {
            PopoverToggle.Now = PopoverToggle.DefaultClock;
            bar.BranchChip.IsChecked = false;
        }

        GC.KeepAlive(window);
    }
}
