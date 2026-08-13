using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
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
    /// <summary>
    /// [T49 fix round 2] <b>Tip denetiminin KENDİ kanıtı — <c>c6e9a21</c>'in tam olarak patladığı özellikler.</b>
    /// O bug bir <b>Double</b> token'ı bir <c>RowDefinition.Height</c>'a (<c>GridLength</c>) veriyordu; guard'ın
    /// kapatmayı iddia ettiği bug'ın özelliği listede YOKTU (fix round 2 ile eklendi). Ayrıca <c>RowDefinition</c>/
    /// <c>ColumnDefinition</c> Grid'in ne görsel ne mantıksal çocuğudur — ağaç gezintisine hiç girmezler, bu
    /// yüzden <see cref="DsResources.DynamicResourceTypeMismatches"/> her Grid için onları AYRICA ziyaret eder.
    /// Bu test o iki yolu da (kök öğe DAHİL) sentetik bir ağaçla doğrudan kanıtlar.
    /// </summary>
    [StaFact]
    public void The_type_check_catches_a_double_token_bound_to_grid_definitions_and_to_the_root_itself()
    {
        // ŞABLON yolu bilinçli seçildi: ölçüldü ki WPF, bir DynamicResource YEREL değer/ifade olarak
        // değerlendirilirken tipi DOĞRULAR (orada zaten fırlar), ama ŞABLONDAN gelen değeri doğrulamaz —
        // sessizce saklar. Bu testin kanıtladığı net tam olarak o boşluğu kapatır.
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ContentControl">
              <Grid Background="{DynamicResource Size.TitleBarHeight}">
                <Grid.RowDefinitions>
                  <RowDefinition />
                </Grid.RowDefinitions>
              </Grid>
            </ControlTemplate>
            """;

        var host = DsResources.NewHost();
        var control = new ContentControl { Template = (ControlTemplate)XamlReader.Parse(xaml) };
        host.Child = control;
        control.ApplyTemplate(); // Measure/Arrange YOK: yerleşim bu sapmayı zaten fırlatarak yakalardı

        var grid = (Grid)VisualTreeHelper.GetChild(control, 0);
        var offenders = DsResources.DynamicResourceTypeMismatches(grid); // grid = KÖK

        // KÖKÜN KENDİ bağı: RealizedObjects kökü gezmeseydi bu ihlal görünmez kalırdı (fix round 2, kalem 4).
        Assert.Contains(offenders, o => o.StartsWith("Grid.Background", StringComparison.Ordinal));

        // c6e9a21'in TAM OLARAK patladığı özellikler listede — savunma derinliği olarak (fix round 2, kalem 2).
        // ÖLÇÜLEN GERÇEK: bu ikisine yanlış tipli bir token bağlamanın DENENEN HER yolu WPF'in kendi
        // doğrulamasıyla zaten fırlıyor (XAML parse · SetResourceReference · şablon değeri) — yani bugün
        // ulaşılamaz bir kutu. Listede olmalarının sebebi guard'ın bu WPF ayrıntısının doğru KALMASINA
        // bağımlı olmamasıdır; ayrıca bu sınıfı yakalayan asıl ağlar realize + yerleşimdir (M3 kaydı).
        Assert.Contains(RowDefinition.HeightProperty, DsResources.CheckedProperties);
        Assert.Contains(ColumnDefinition.WidthProperty, DsResources.CheckedProperties);
    }

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
        // [DEĞİŞEN KURAL] Prompt imleci eskiden Brush.TextPrimary'di. Artık amber: event stream'in aktif satır
        // imleciyle AYNI ton — iki panel aynı dili konuşur (kullanıcı kararı; tasarımın "dim" prompt'undan
        // bilinçli sapma, gerekçesi ConsoleView.xaml'de). "build in progress" imleci zaten amberdi.
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(view.ActiveCursor.Fill));
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
