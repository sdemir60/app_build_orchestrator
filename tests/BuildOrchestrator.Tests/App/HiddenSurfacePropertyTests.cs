using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz A · A1] <b>"Yüzey gizli" sinyalinin kalıtımı.</b> Sinyal pencereye TEK yerden yazılır
/// (<c>MainWindow.SetSurfaceHidden</c>) ve tüm torunlar onu miras alır — görünümler kendi bayrağını taşımaz,
/// <see cref="HiddenSurface.GetIsHidden"/> okur. Bu dosya o kalıtımın kendisini pinler: DP'nin
/// <c>Inherits</c> bayrağı düşerse hiçbir görünüm penceresinin gizlendiğini bilmez ve her kapı sessizce açık kalır.
///
/// <para>Ağaç gerçek bir <see cref="Window"/> içindedir ama pencere HİÇ gösterilmez: kalıtım mantıksal/görsel
/// ebeveyn zincirinden gelir, HWND gerektirmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HiddenSurfacePropertyTests
{
    /// <summary>Pencere → Grid → Border → Grid: en içteki öğe pencereden üç kuşak uzaktır.</summary>
    private static (Window Window, FrameworkElement Outer, FrameworkElement Middle, FrameworkElement Inner) NestedTree()
    {
        var inner = new Grid();
        var middle = new Border { Child = inner };
        var outer = new Grid();
        outer.Children.Add(middle);
        return (new Window { Content = outer }, outer, middle, inner);
    }

    [StaFact]
    public void The_hidden_signal_defaults_to_visible_for_the_window_and_every_descendant()
    {
        var (window, outer, middle, inner) = NestedTree();

        Assert.False(HiddenSurface.GetIsHidden(window));
        Assert.False(HiddenSurface.GetIsHidden(outer));
        Assert.False(HiddenSurface.GetIsHidden(middle));
        Assert.False(HiddenSurface.GetIsHidden(inner));
    }

    [StaFact]
    public void The_hidden_signal_written_on_the_window_reaches_every_descendant_and_clears_again()
    {
        var (window, outer, middle, inner) = NestedTree();

        HiddenSurface.SetIsHidden(window, true);
        Assert.True(HiddenSurface.GetIsHidden(outer));
        Assert.True(HiddenSurface.GetIsHidden(middle));
        Assert.True(HiddenSurface.GetIsHidden(inner));

        HiddenSurface.SetIsHidden(window, false);
        Assert.False(HiddenSurface.GetIsHidden(outer));
        Assert.False(HiddenSurface.GetIsHidden(middle));
        Assert.False(HiddenSurface.GetIsHidden(inner));
    }
}
