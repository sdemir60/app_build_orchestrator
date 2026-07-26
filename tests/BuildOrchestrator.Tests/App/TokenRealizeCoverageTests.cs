using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 FINAL PASS · It-4b dersi (c6e9a21)] Bugüne dek SÖZLÜKSÜZ (<c>new X()</c>) test edilen XAML köklerinin
/// gerçek merge zinciriyle realize edilmesi.
///
/// <para><b>Neden gerekli:</b> headless host'ta bir <c>DynamicResource</c> çözülemezse WPF SESSİZCE geçer —
/// değer varsayılanda kalır, test yeşil kalır. <c>LatestPill.xaml</c>'deki yorum bu boşluğu açıkça sömürüyordu
/// ("headless test host'ta DynamicResource sessizce çözümsüz kalır"): <c>Radius.Overlay</c>,
/// <c>Brush.BorderStrong</c> ve <c>Effect.PopoverShadow</c> bağlantıları ÜRETİMDE hiç doğrulanmıyordu. Bu sınıf
/// dört kökü <see cref="DsResources.NewHost"/> + <see cref="DsResources.Realize"/> ile besler ve tüketilen
/// token'ın GERÇEKTEN o özelliğe ulaştığını (yalnız "patlamadı"yı değil) iddia eder.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class TokenRealizeCoverageTests
{
    [StaFact]
    public void Panel_header_realizes_and_takes_its_height_and_surface_from_tokens()
    {
        var host = DsResources.NewHost();
        var header = new PanelHeader();
        var window = DsResources.Realize(host, header);

        // UserControl'un KENDİ içeriği (görsel ağaçtaki ilk çocuk UserControl şablonunun kabuğudur, XAML kökü DEĞİL).
        var root = (Border)header.Content;
        Assert.Equal((double)host.FindResource("Size.PanelHeaderHeight"), root.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Surface"), DsResources.ColorOf(root.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.BorderSubtle"), DsResources.ColorOf(root.BorderBrush));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Latest_pill_realizes_and_its_shell_takes_radius_border_and_shadow_from_tokens()
    {
        var host = DsResources.NewHost();
        // Üretimde pill Visibility'yi host (ConsoleView/EventStreamView) sürer; şablonun GERÇEKTEN genişlemesi
        // için burada görünür kılınır — Collapsed bir dalın şablonu hiç uygulanmaz ve test yine hiçbir şey görmezdi.
        var pill = new LatestPill { Visibility = Visibility.Visible };
        var window = DsResources.Realize(host, pill);

        var shell = (Border)pill.PillButton.Template.FindName("Root", pill.PillButton);
        Assert.Equal((CornerRadius)host.FindResource("Radius.Overlay"), shell.CornerRadius);
        Assert.Equal(DsResources.TokenColor(host, "Brush.BorderStrong"), DsResources.ColorOf(shell.BorderBrush));
        Assert.NotNull(shell.Effect); // Effect.PopoverShadow — çözülmezse null kalırdı (sessiz kayıp)
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Console_view_realizes_and_its_editor_and_cursors_take_font_size_and_colours_from_tokens()
    {
        var host = DsResources.NewHost();
        var view = new ConsoleView();
        var window = DsResources.Realize(host, view);

        var root = (Grid)view.Content;
        Assert.Equal(DsResources.TokenColor(host, "Brush.ConsoleBg"), DsResources.ColorOf(root.Background));
        Assert.Equal((double)host.FindResource("FontSize.Xs"), view.EditorControl.FontSize);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), DsResources.ColorOf(view.ActiveCursor.Fill));
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(view.BuildProgressCursor.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Console_header_realizes_and_its_dep_issue_badge_resolves_geometry_and_status_colour()
    {
        var host = DsResources.NewHost();
        var header = new ConsoleHeader();
        var window = DsResources.Realize(host, header);

        var root = (Border)header.Content;
        Assert.Equal(DsResources.TokenColor(host, "Brush.Surface"), DsResources.ColorOf(root.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(header.LinesText.Foreground));

        // Collapsed dal da olsa DynamicResource'lar okununca çözülür: ▲ dep-warn geometrisi (Icons.xaml) ve
        // statü rengi bağlantısı burada kanıtlanır — anahtar adı sürüklenirse Data null kalır.
        var badge = header.DepIssueBadge.Children.OfType<Viewbox>().Single();
        var glyph = ((Canvas)badge.Child).Children.OfType<System.Windows.Shapes.Path>().Single();
        Assert.NotNull(glyph.Data);
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusFailText"), DsResources.ColorOf(glyph.Fill));
        GC.KeepAlive(window);
    }
}
