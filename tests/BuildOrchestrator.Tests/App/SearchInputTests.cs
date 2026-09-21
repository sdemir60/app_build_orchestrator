using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
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
