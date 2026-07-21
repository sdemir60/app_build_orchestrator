using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62/K8] Maximize butonunun glyph'i tasarımda tanımsızdı; v7 kararı K8: <c>WindowState=Maximized</c> iken
/// "restore" (iç içe/kaydırılmış iki kare), normalde tek kare. Kablaj <see cref="CaptionGlyphs.BindMaxButton"/>
/// içinde TEK yerdedir — MainWindow ile bu test BİREBİR aynı yolu kullanır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RestoreGlyphTests
{
    [Fact]
    public void Pure_glyph_choice_follows_the_window_state()
    {
        Assert.Equal(CaptionGlyphs.Maximize, CaptionGlyphs.MaxButtonGlyph(WindowState.Normal));
        Assert.Equal(CaptionGlyphs.Maximize, CaptionGlyphs.MaxButtonGlyph(WindowState.Minimized));
        Assert.Equal(CaptionGlyphs.Restore, CaptionGlyphs.MaxButtonGlyph(WindowState.Maximized));
        Assert.NotEqual(CaptionGlyphs.Maximize, CaptionGlyphs.Restore);
    }

    [StaFact]
    public void Bound_button_starts_with_the_state_it_is_bound_in()
    {
        var window = new Window { WindowState = WindowState.Maximized };
        var button = new Button();

        CaptionGlyphs.BindMaxButton(window, button);

        Assert.Equal(CaptionGlyphs.Restore, button.Content);
    }

    [StaFact]
    public void Glyph_swaps_both_ways_when_the_window_state_changes()
    {
        var window = new Window();
        var button = new Button();
        CaptionGlyphs.BindMaxButton(window, button);
        Assert.Equal(CaptionGlyphs.Maximize, button.Content);

        window.WindowState = WindowState.Maximized;
        Assert.Equal(CaptionGlyphs.Restore, button.Content);

        window.WindowState = WindowState.Normal;
        Assert.Equal(CaptionGlyphs.Maximize, button.Content);
    }
}
