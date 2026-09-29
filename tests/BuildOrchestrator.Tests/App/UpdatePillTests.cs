using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.1 · §2.12 · §9 "Uygulama sayıları — güncelleme"] Title bar'daki güncelleme hapı. Güncelleme
/// motoru yoktur: hap örnek teklifle her zaman görünür (plan U1) — teklif başlangıçta hazır olduğu için İLK karede
/// görünür, giriş animasyonu yalnız teklif sonradan gelirse oynar (gelecekteki motorun dikişi).
///
/// <para><b>Yer:</b> sağ kümenin BAŞI — <c>[Update v] | [quad][list][focus] | [⚙][✦][ⓘ]</c>; arkasında mevcut
/// ayraçla aynı stilde 1×14 ayraç. Küme sağa yaslı olduğundan hap boş alana doğru büyür ve mevcut ikonlar yerinden
/// OYNAMAZ (kas hafızası).</para>
///
/// <para><b>Ölçü:</b> 22px, padding <c>0 8px 0 6px</c>, gap 6, <c>radius-sm</c>, <c>surface-raised</c> + 1px
/// <c>border-strong</c>; içerik circle-arrow-up 13px + <c>Update</c> (12px/500 <c>text-primary</c>) + mono 11px
/// tabular sürüm (<c>text-dim</c>). Hover/açık: <c>surface-overlay</c> + <c>neutral-500</c>, sürüm
/// <c>text-secondary</c>; geçiş <c>Duration.Fast</c> (DS standardı — README §2.12'deki 80ms kendi §9 sayılarıyla
/// çelişir, plan U5). Amber yok.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UpdatePillTests
{
    private static (MainWindow window, RunViewModel vm) Realized(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        return (window, vm);
    }

    /// <summary>Sağ kümenin kendisi — gear'ın ebeveyni (AboutWiringTests ile aynı seçici).</summary>
    private static Panel RightCluster(MainWindow window) => (Panel)LogicalTreeHelper.GetParent(window.GearButton);

    /// <summary>Bir butonun sağ kenarının pencerenin sağ kenarına uzaklığı — kümenin sağa yaslı kaldığının ölçüsü.</summary>
    private static double RightInset(MainWindow window, FrameworkElement button) =>
        window.RootShell.ActualWidth - (button.TranslatePoint(new Point(0, 0), window.RootShell).X + button.ActualWidth);

    private static FrameworkElement[] ExistingButtons(MainWindow window) =>
        [window.LayQuadButton, window.LayListButton, window.LayFocusButton, window.GearButton, window.NotesButton, window.InfoButton];

    // ---------------------------------------------------------------- yer

    /// <summary>Hap kümenin İLK öğesidir, ardından ayracı gelir; mevcut altı buton aynı sırada arkasında durur.</summary>
    [StaFact]
    public void The_pill_and_its_separator_open_the_right_cluster()
    {
        using var temp = new TempDir();
        var (window, _) = Realized(temp);

        var cluster = RightCluster(window);
        Assert.Same(window.UpdatePillSlot, cluster.Children[0]);
        var slot = window.UpdatePillSlot.Children.Cast<UIElement>().ToList();
        Assert.True(DsResources.IsSelfOrDescendantOf(window.UpdatePill, slot[0], includeLogical: true),
            "hap slotun ilk öğesi değil");
        Assert.Same(window.UpdatePillSeparator, slot[^1]);

        var order = ExistingButtons(window).Select(b => cluster.Children.IndexOf((UIElement)b)).ToList();
        Assert.Equal([1, 2, 3, 5, 6, 7], order); // [slot] quad list focus | gear notes info
        GC.KeepAlive(window);
    }

    /// <summary>Kas hafızası: mevcut ikonların pencerenin sağ kenarına göre konumu, hap görünürken de gizliyken de
    /// AYNIDIR — hap boş alana (sola) doğru büyür.</summary>
    [StaFact]
    public void The_existing_title_bar_buttons_do_not_move_when_the_pill_appears()
    {
        using var temp = new TempDir();
        var (window, _) = Realized(temp);
        Assert.Equal(Visibility.Visible, window.UpdatePillSlot.Visibility); // ön-koşul: hap görünür
        var withPill = ExistingButtons(window).Select(b => RightInset(window, b)).ToList();

        window.UpdatePillSlot.Visibility = Visibility.Collapsed;
        window.RootShell.UpdateLayout();
        var withoutPill = ExistingButtons(window).Select(b => RightInset(window, b)).ToList();

        Assert.Equal(withoutPill, withPill);
        Assert.True(window.UpdatePill.ActualWidth > 0, "hap hiç yer kaplamadı — ölçüm vakum");
        GC.KeepAlive(window);
    }

    /// <summary>Hapın ayracı mevcut ayraçla TEK stildir (literal ikinci kez yazılmaz): 1×14, <c>border</c>, yatay 5px.</summary>
    [StaFact]
    public void The_pill_separator_shares_the_one_title_bar_separator_style()
    {
        using var temp = new TempDir();
        var (window, _) = Realized(temp);

        var style = (Style)window.FindResource("TitleBarSeparator");
        var separators = RightCluster(window).Children.OfType<Rectangle>().Append(window.UpdatePillSeparator).ToList();
        Assert.Equal(2, separators.Count);
        Assert.All(separators, s =>
        {
            Assert.Same(style, s.Style);
            Assert.Equal((1.0, 14.0), (s.Width, s.Height));
            Assert.Equal(new Thickness(5, 0, 5, 0), s.Margin);
            Assert.Equal(DsResources.TokenColor(window, "Brush.Border"), DsResources.ColorOf(s.Fill));
        });
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- ölçü ve renk

    [StaFact]
    public void The_pill_is_drawn_to_the_design_numbers()
    {
        using var temp = new TempDir();
        var (window, vm) = Realized(temp);
        var pill = window.UpdatePill;

        Assert.Equal(22.0, pill.ActualHeight, precision: 3);
        Assert.Equal(new Thickness(6, 0, 8, 0), pill.Padding);
        Assert.Equal(new Thickness(1), pill.BorderThickness);
        Assert.Equal((CornerRadius)window.FindResource("Radius.Sm"), DsChrome.GetCornerRadius(pill));
        Assert.Equal(4.0, DsChrome.GetCornerRadius(pill).TopLeft);
        Assert.Equal(DsResources.TokenColor(window, "Brush.SurfaceRaised"), DsResources.ColorOf(pill.Background));
        Assert.Equal(DsResources.TokenColor(window, "Brush.BorderStrong"), DsResources.ColorOf(pill.BorderBrush));

        var icon = DsResources.Descendants(pill).OfType<Viewbox>().Single();
        Assert.Equal((13.0, 13.0), (icon.Width, icon.Height));
        var glyph = DsResources.Descendants(icon).OfType<Path>().Single();
        Assert.Same(window.FindResource("Icon.UpdateReady"), glyph.Data);
        Assert.Equal(1.7, glyph.StrokeThickness);
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextPrimary"), DsResources.ColorOf(glyph.Stroke));

        var label = DsResources.Descendants(pill).OfType<TextBlock>().Single(t => t.Text == UpdateText.PillLabel);
        Assert.Equal(12.0, label.FontSize);
        Assert.Equal(FontWeights.Medium, label.FontWeight);
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextPrimary"), DsResources.ColorOf(label.Foreground));

        var version = window.UpdatePillVersion;
        Assert.Equal(vm.AvailableUpdate!.Version, version.Text);
        Assert.Equal(AppFonts.Mono, version.FontFamily);
        Assert.Equal(11.0, version.FontSize);
        Assert.Equal(FontWeights.Normal, version.FontWeight);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(version));
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextDim"), DsResources.ColorOf(version.Foreground));

        // gap 6: ikon → etiket → sürüm
        double IconRight() => icon.TranslatePoint(new Point(icon.ActualWidth, 0), pill).X;
        Assert.Equal(6.0, label.TranslatePoint(new Point(0, 0), pill).X - IconRight(), precision: 3);
        Assert.Equal(6.0, version.TranslatePoint(new Point(0, 0), pill).X
                          - label.TranslatePoint(new Point(label.ActualWidth, 0), pill).X, precision: 3);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(pill));
        GC.KeepAlive(window);
    }

    /// <summary>Kart açıkken hap yükselir: <c>surface-overlay</c> + <c>neutral-500</c>, sürüm <c>text-secondary</c>.
    /// Hover AYNI görünümü verir — stilin iki tetikleyicisi aynı üç değeri yazar (fare headless sürülemez).</summary>
    [StaFact]
    public void The_open_pill_lifts_to_the_overlay_surface_and_hover_looks_the_same()
    {
        using var temp = new TempDir();
        var (window, _) = Realized(temp);
        var pill = window.UpdatePill;

        pill.IsChecked = true;
        pill.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(window, "Brush.SurfaceOverlay"), DsResources.ColorOf(pill.Background));
        Assert.Equal(DsResources.TokenColor(window, "Brush.Neutral500"), DsResources.ColorOf(pill.BorderBrush));
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextSecondary"),
            DsResources.ColorOf(window.UpdatePillVersion.Foreground));

        static IEnumerable<(DependencyProperty, object)> SettersOf(Style style, DependencyProperty trigger) =>
            style.Triggers.OfType<Trigger>().Single(t => t.Property == trigger && Equals(t.Value, true))
                .Setters.OfType<Setter>().Select(s => (s.Property, s.Value is DynamicResourceExtension d ? d.ResourceKey : s.Value));
        Assert.Equal(SettersOf(pill.Style, ToggleButton.IsCheckedProperty), SettersOf(pill.Style, UIElement.IsMouseOverProperty));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- erişilebilirlik

    /// <summary>Ekran okuyucu hapı <c>Update to &lt;sürüm&gt;</c> diye okur ve kartın açık olup olmadığını duyar
    /// (<c>aria-expanded</c> karşılığı: UIA ExpandCollapse deseni hapın durumunu izler).</summary>
    [StaFact]
    public void The_pill_is_named_update_to_the_version_and_reports_whether_its_card_is_expanded()
    {
        using var temp = new TempDir();
        var (window, vm) = Realized(temp);
        var pill = window.UpdatePill;

        var peer = UIElementAutomationPeer.CreatePeerForElement(pill);
        Assert.Equal("Update to " + vm.AvailableUpdate!.Version, peer.GetName());
        Assert.Equal(AccessibilityNames.UpdateTo(vm.AvailableUpdate.Version), peer.GetName());

        var expander = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Equal(ExpandCollapseState.Collapsed, expander.ExpandCollapseState);
        pill.IsChecked = true;
        Assert.Equal(ExpandCollapseState.Expanded, expander.ExpandCollapseState);
        expander.Collapse();
        Assert.False(pill.IsChecked);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- görünürlük ve giriş

    /// <summary>Örnek teklif açılışta hazırdır: hap ilk karede görünür ve giriş OYNAMAZ (animasyonlar açıkken bile).</summary>
    [StaFact]
    public void The_sample_offer_shows_the_pill_from_the_first_frame_without_an_entrance()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        using var temp = new TempDir();
        var (window, _) = Realized(temp);
        var slot = window.UpdatePillSlot;

        Assert.Equal(Visibility.Visible, slot.Visibility);
        Assert.Equal(1.0, slot.Opacity);
        Assert.False(slot.HasAnimatedProperties);
        Assert.True(slot.RenderTransform is null || slot.RenderTransform.Value.IsIdentity);
        GC.KeepAlive(window);
    }

    /// <summary>Teklif yoksa hap yoktur; sonradan gelen teklif hapı bir kez girişle getirir (opaklık 0 + 4px yukarıdan,
    /// <c>Duration.Slow</c> — hap ile ayracı birlikte). Hap görünürken gelen yeni bir teklif girişi yeniden OYNATMAZ,
    /// yalnız sürümü günceller.</summary>
    [StaFact]
    public void An_offer_that_arrives_later_plays_the_entrance_once()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        using var temp = new TempDir();
        var (window, vm) = Realized(temp);
        var slot = window.UpdatePillSlot;

        vm.AvailableUpdate = null;
        Assert.Equal(Visibility.Collapsed, slot.Visibility);

        vm.AvailableUpdate = UpdateOffer.Sample;
        Assert.Equal(Visibility.Visible, slot.Visibility);
        var entrance = Assert.IsType<TranslateTransform>(slot.RenderTransform);
        Assert.Equal(-4.0, entrance.Y);
        Assert.True(slot.HasAnimatedProperties, "giriş oynamadı");

        vm.AvailableUpdate = UpdateOffer.Sample with { Version = "9.9.0" };
        Assert.Same(entrance, slot.RenderTransform); // ikinci giriş kurulmadı
        Assert.Equal("9.9.0", window.UpdatePillVersion.Text);
        Assert.Equal(AccessibilityNames.UpdateTo("9.9.0"), AutomationProperties.GetName(window.UpdatePill));
        GC.KeepAlive(window);
    }

    /// <summary>Hapın girişi <c>Duration.Slow</c> token'ından çözülür (tasarım: <c>bo-upd-in</c> 280ms ease-out).</summary>
    [StaFact]
    public void The_entrance_lasts_the_slow_duration_token()
    {
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, new Border());

        Assert.Equal(((Duration)host.FindResource("Duration.Slow")).TimeSpan, PopIn.EntranceDuration(host));
        Assert.Equal(TimeSpan.FromMilliseconds(280), PopIn.EntranceDuration(host));
        GC.KeepAlive(window);
    }

    /// <summary>Girişin gerçek geometrisi (<c>bo-upd-in</c>: <c>opacity 0; translateY(-4px)</c> → düz), canlı bir
    /// animasyon saatinde: 4px YUKARIDAN iner (popover'ın pop-in'i aşağıdan yükselir), ölçek yoktur ve sonunda düz
    /// durur — sonrasında hareket yok.</summary>
    [StaFact]
    public void The_entrance_drops_4px_from_above_and_fades_in_without_scaling()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var host = DsResources.NewHost();
        var element = new Border { Width = 40, Height = 20 };
        var window = DsResources.Realize(host, element);

        PopIn.PlayEntrance(element);

        var translate = Assert.IsType<TranslateTransform>(element.RenderTransform);
        Assert.Equal(-4.0, translate.Y);
        Assert.True(element.HasAnimatedProperties, "opaklık/konum GERÇEKTEN animasyonlu değil");
        DispatcherPump.PumpUntil(() => element.Opacity >= 1.0 && translate.Y >= 0.0, TimeSpan.FromSeconds(3));
        Assert.Equal(1.0, element.Opacity, precision: 3);
        Assert.Equal(0.0, translate.Y, precision: 3);
        GC.KeepAlive(window);
    }
}
