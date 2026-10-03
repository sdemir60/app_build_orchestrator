using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Arama kutularının iki davranışı: (1) yazılınca beliren temizleme ✕'i (şablonu
/// <see cref="DsControlTemplateTests"/> pinler; burada iki arama kutusunun onu AÇTIĞI pinlenir) ve
/// (2) <see cref="ClickAwayBlur"/> — odaktaki bir metin kutusunun dışına, odak ALMAYAN bir yere (panel zemini,
/// odaklanamayan düğme) tıklanınca odak kutudan bırakılır.
///
/// <para><b>Kusur (kullanıcı):</b> proje filtresine odaklanıp graf panelinin düz zeminine tıklayınca odak
/// kutuda kalıyordu. WPF'te odağı yalnız odak ALABİLEN bir öğe değiştirir; zemin ve <c>Focusable=False</c>
/// kontroller tıklamayı alır ama odağı taşımaz.</para>
///
/// <para><b>[T17]</b> Yukarıdaki iki davranış burada GENEL bir <see cref="TextBox"/>/<see cref="Border"/>
/// iskeletiyle sınanır — kutunun bir VM'e bağlı olup olmadığından bağımsız mekanik. Aşağıdaki üçlü ise AYNI
/// mekanizmaların proje filtresi ÜZERİNDEN, GERÇEK bir <see cref="RunViewModel"/> <c>DataContext</c>'iyle
/// <c>ProjectQuery</c>'e GERÇEKTEN ulaştığını pinler (Esc / ✕ komutu / click-away) — ölçülen fark: yukarıdaki
/// testler kutunun kendi <c>Text</c>'ini görür, VM'in sorgusunun GERÇEKTEN temizlenip temizlenmediğini ya da
/// görünür proje listesinin GERÇEKTEN tam listeye dönüp dönmediğini görmez.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class SearchInputTests
{
    [StaFact]
    public void Both_search_boxes_opt_into_the_clear_button()
    {
        Assert.True(DsChrome.GetIsClearable(new ShellRoot().ProjectFilterBox));
        Assert.True(DsChrome.GetIsClearable(new BranchPopover().SearchBox));
    }

    [StaFact]
    public void Clicking_a_non_focusable_background_releases_focus_from_the_text_box()
    {
        var (box, background, _, window) = Realize();
        Assert.True(box.Focus());

        RaiseMouseDown(background);

        Assert.False(box.IsKeyboardFocusWithin);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_inside_the_text_box_keeps_its_focus()
    {
        var (box, _, _, window) = Realize();
        Assert.True(box.Focus());

        RaiseMouseDown(box);

        Assert.True(box.IsKeyboardFocusWithin);
        GC.KeepAlive(window);
    }

    /// <summary>Odak, tıklanan yerin odak alabilen en yakın atasına geçer — bir modal kendi içine odak
    /// tuzağı kurduğunda (<see cref="ModalDialog"/> odaklanabilir) odak tuzaktan dışarı düşmez.</summary>
    [StaFact]
    public void Focus_moves_to_the_nearest_focusable_ancestor_of_the_click()
    {
        var (box, _, scopedBackground, window) = Realize();
        Assert.True(box.Focus());

        RaiseMouseDown(scopedBackground);

        Assert.Same(scopedBackground.Parent, Keyboard.FocusedElement);
        GC.KeepAlive(window);
    }

    /// <summary>Üretim kablajı: ana pencere davranışı açar.</summary>
    [StaFact]
    public void The_main_window_enables_click_away_blur()
    {
        using var dir = new TempDir();
        var (window, _) = MainWindowHost.New(dir);
        Assert.True(ClickAwayBlur.GetIsEnabled(window));
    }

    // ---------------------------------------------------------------- [T17] gerçek VM'e bağlı proje filtresi

    /// <summary>[T17 · 1] Kutudaki Esc, ShellRoot ctor'da bağlanan GERÇEK <c>OnFilterKeyDown</c>'ı tetikler
    /// (<c>box.Clear()</c> → iki-yönlü binding <c>ProjectQuery</c>'yi temizler, <c>ShellRoot.xaml.cs:86-95</c>).
    /// Kanıt kutunun kendi <c>Text</c>'i DEĞİL, VM'in sorgusu VE ondan türeyen görünür listedir — sorgu önce
    /// listeyi GERÇEKTEN daraltır (ön-koşul, aksi halde "tam listeye döndü" iddiası 0==0 üzerinde YALANCI-YEŞİL
    /// olurdu), Esc sonra onu tam listeye geri döndürür.
    /// <para><b>[DEĞİŞEN KURAL — perf Faz B · B5]</b> Eskiden kutudaki yazı <c>ProjectQuery</c>'ye ANINDA ulaşıyordu ve test ön-koşulu
    /// ile sonucu hemen okuyordu. Kutunun bağlaması artık gecikmeli yazar (her tuş vuruşu bir liste yeniden kurulumuydu; bkz.
    /// <c>ProjectListFilterTests.A_burst_of_keystrokes_in_the_filter_box_reaches_the_model_once</c>): yazı da Esc'in temizlemesi
    /// de modele gecikme dolunca ulaşır. Test beklemeyi pompa ile AÇIKÇA yapar; iddialar aynıdır, hiçbir eşik gevşetilmedi.</para></summary>
    [StaFact]
    public void Escape_clears_the_bound_query_and_the_visible_list_returns_to_full()
    {
        using var temp = new TempDir();
        var (box, _, vm, window) = RealizeFilterBoxWithVm(temp);
        box.Focus();
        box.Text = "Alpha";
        DispatcherPump.PumpUntil(() => vm.ProjectQuery == "Alpha", TimeSpan.FromSeconds(2)); // yazı modele gecikmeyle ulaşır (B5)
        Assert.Single(vm.VisibleProjects); // ön-koşul: sorgu listeyi GERÇEKTEN daraltıyor

        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(esc); // ShellRoot ctor'da box'a asılı GERÇEK OnFilterKeyDown handler'ı
        DispatcherPump.PumpUntil(() => vm.ProjectQuery == "", TimeSpan.FromSeconds(2)); // temizleme de aynı gecikmeli yoldan gider (B5)

        Assert.Equal("", vm.ProjectQuery);
        Assert.Equal(2, vm.VisibleProjects.Count);
        GC.KeepAlive(window);
    }

    /// <summary>[T17 · 2] ✕ butonunun komutu (<see cref="DsChrome.ClearTextCommand"/>, <c>DsChrome.cs:57-68</c>):
    /// <c>box.Clear()</c> + <c>box.Focus()</c>. Temizleme VM'e ULAŞIR (binding) ve — ✕'in Esc'ten FARKI — odak
    /// kutuda KALIR (Esc'in AKSİNE <c>Keyboard.ClearFocus()</c> hiç çağrılmaz).
    /// <para><b>[DEĞİŞEN KURAL — perf Faz B · B5]</b> Test eskiden yazıyı ve temizlemeyi anında okurdu; bağlama gecikmeli olunca
    /// modele hiç ulaşmamış bir yazıyı temizlemek de "sorgu boş" iddiasını boş sorgu üzerinde YALANCI-YEŞİL yapar. Test önce yazının
    /// modele ulaştığını ("Alpha") sınar, sonra temizlemenin de gecikmeyle ulaşmasını bekler; iddia aynıdır.</para></summary>
    [StaFact]
    public void The_clear_button_command_empties_the_bound_query_and_keeps_focus_in_the_box()
    {
        using var temp = new TempDir();
        var (box, _, vm, window) = RealizeFilterBoxWithVm(temp);
        box.Focus();
        box.Text = "Alpha";
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        DispatcherPump.PumpUntil(() => vm.ProjectQuery == "Alpha", TimeSpan.FromSeconds(2)); // ön-koşul: yazı modele ULAŞTI (B5)
        Assert.Equal("Alpha", vm.ProjectQuery);
        DsChrome.ClearTextCommand.Execute(box);
        DispatcherPump.PumpUntil(() => vm.ProjectQuery == "", TimeSpan.FromSeconds(2)); // temizleme de aynı gecikmeli yoldan gider (B5)

        Assert.Equal("", vm.ProjectQuery);
        Assert.True(box.IsKeyboardFocused);
        GC.KeepAlive(window);
    }

    /// <summary>[T17 · 3] <see cref="ClickAwayBlur"/> (<c>ClickAwayBlur.cs:40-52</c>) boş bir zemine tıklanınca
    /// odağı kutudan bırakır — ama SORGUYA DOKUNMAZ (mekanizma yalnız <c>Focus()</c>/<c>ClearFocus()</c> çağırır,
    /// metne hiç dokunmaz). Esc/✕'in AKSİNE burada temizleme YOKTUR: yalnız odak gider, VM'in
    /// <c>ProjectQuery</c>'si aynı kalır.
    /// <para><b>[DEĞİŞEN KURAL — perf Faz B · B5]</b> Eskiden <c>box.Text</c> atanır atanmaz VM'in sorgusu da değişirdi; kutunun
    /// bağlaması artık gecikmeli yazar (bkz. <c>ProjectListFilterTests.A_burst_of_keystrokes_in_the_filter_box_reaches_the_model_once</c>).
    /// Bekleyen yazım odak kaybından SONRA da modele ulaşmalı — blur onu ne siler ne erkene çeker; test bunu pompa ile açıkça
    /// bekler, iddia aynıdır.</para></summary>
    [StaFact]
    public void Clicking_away_blurs_the_box_but_keeps_the_bound_query()
    {
        using var temp = new TempDir();
        var (box, background, vm, window) = RealizeFilterBoxWithVm(temp);
        box.Focus();
        box.Text = "Alpha";
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        RaiseMouseDown(background);

        Assert.False(box.IsKeyboardFocused);
        DispatcherPump.PumpUntil(() => vm.ProjectQuery == "Alpha", TimeSpan.FromSeconds(2)); // bekleyen gecikmeli yazım blur'dan sonra da ulaşır (B5)
        Assert.Equal("Alpha", vm.ProjectQuery);
        GC.KeepAlive(window);
    }

    /// <summary>[T17] <see cref="AccessibilityTests.RealizeFilterBox"/> ile AYNI ayırma (ShellRoot'un TAMAMI
    /// headless realize edilemez) — ama kutuya BURADA gerçek bir VM bağlanır (<see cref="MainWindowHost.NewWithProjects"/>,
    /// iki proje: sorgu listeyi GERÇEKTEN daraltsın ki "tam listeye döndü" iddiası boş kümede YALANCI-YEŞİL
    /// olmasın). Reparent SONRASI inherited <c>DataContext</c> kesildiği için <c>box</c>'a doğrudan atanır.
    ///
    /// <para>Bir zemin de eklenir (<see cref="Realize"/>'daki <c>background</c> ile AYNI şekil) — yalnız
    /// click-away testinin tıklayacağı, odak ALMAYAN bir yüzey.</para></summary>
    private static (TextBox Box, Border Background, RunViewModel Vm, Window Window) RealizeFilterBoxWithVm(TempDir uiStateDir)
    {
        var shell = new ShellRoot();
        var box = shell.ProjectFilterBox;
        if (LogicalTreeHelper.GetParent(box) is ContentPresenter cp) cp.Content = null; // kutuyu PanelHeader'dan ayır
        var (_, vm, _) = MainWindowHost.NewWithProjects(uiStateDir, ("Alpha", null), ("Beta", null));
        box.DataContext = vm;

        var background = new Border { Width = 40, Height = 20, Background = System.Windows.Media.Brushes.Transparent };
        var root = new StackPanel();
        root.Children.Add(box);
        root.Children.Add(background);

        var window = DsResources.Realize(DsResources.NewHost(), root);
        ClickAwayBlur.SetIsEnabled(window, true);
        return (box, background, vm, window);
    }

    private static (TextBox Box, Border Background, Border ScopedBackground, Window Window) Realize()
    {
        var box = new TextBox { Width = 120 };
        var background = new Border { Width = 50, Height = 20, Background = System.Windows.Media.Brushes.Transparent };
        var scopedBackground = new Border { Width = 50, Height = 20, Background = System.Windows.Media.Brushes.Transparent };
        var scope = new ContentControl { Focusable = true, Content = scopedBackground };
        var root = new StackPanel();
        root.Children.Add(box);
        root.Children.Add(background);
        root.Children.Add(scope);
        var window = DsResources.Realize(DsResources.NewHost(), root);
        ClickAwayBlur.SetIsEnabled(window, true);
        return (box, background, scopedBackground, window);
    }

    private static void RaiseMouseDown(UIElement target) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent,
        });
}
