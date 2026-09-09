using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [K5 · design v1.14.0 §9] Resources/Controls.xaml SELECT bölümü (<c>Ds.Select</c>) — DS'in <c>Select</c>
/// bileşeninin WPF portu (_ds_bundle.js:779-834 Select.jsx + :714-728 inputBase, Select paylaşır). Uygulamada
/// bir ComboBox stili yoktu; ilk (ve bugün tek) tüketici Settings'in harici proje kartındaki Source (Git/TFVC)
/// seçimidir (bkz. <c>SettingsDialogFocusTests.External_cards_are_36px_tall_with_a_6px_gap_a_grip_and_a_96px_source_select</c>).
///
/// <para><c>ScrollBarStyleTests</c>'in desenini izler: ADLANDIRILMIŞ (implicit değil) bir stil olduğu için
/// kontrol GERÇEKTEN kurulur (host'un <c>FindResource</c>'ından okunup atanır, sonra <c>DsResources.Realize</c>
/// ekran dışı bir pencerede gösterir) — bir Style'ın var olduğunu okumak şablonun ürettiği GERÇEK sonucu
/// kanıtlamaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SelectStyleTests
{
    private static ComboBox NewSelect(Border host)
    {
        var box = new ComboBox { Style = (Style)host.FindResource("Ds.Select") };
        box.Items.Add(new ComboBoxItem { Content = "Git" });
        box.Items.Add(new ComboBoxItem { Content = "TFVC" });
        box.SelectedIndex = 0;
        return box;
    }

    private static Border ChromeOf(ComboBox box) =>
        (Border)box.Template.FindName("Chrome", box);

    private static Popup PopupOf(ComboBox box) =>
        (Popup)box.Template.FindName("PART_Popup", box);

    [StaFact]
    public void The_box_is_28px_tall_with_surface_sunken_fill_and_a_border_strong_hairline()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        Assert.Equal(28.0, box.ActualHeight);
        var chrome = ChromeOf(box);
        Assert.Equal(DsResources.TokenColor(box, "Brush.SurfaceSunken"), DsResources.ColorOf(chrome.Background));
        Assert.Equal(DsResources.TokenColor(box, "Brush.BorderStrong"), DsResources.ColorOf(chrome.BorderBrush));
        Assert.Equal(new Thickness(1), chrome.BorderThickness);
        Assert.Equal(Cursors.Hand, box.Cursor);
        GC.KeepAlive(window);
    }

    /// <summary>Chevron 12×12'de, sağdan 8px, text-dim renginde — Icons.xaml'in ZATEN kayıtlı
    /// <c>Icon.Chevron</c>'unu (Chip chevron'uyla AYNI geometri, "Select'in ok'u da AYNI" yorumu) sarar; ikinci
    /// bir ikon anahtarı İCAT EDİLMEDİ (kopya YASAK).</summary>
    [StaFact]
    public void The_chevron_is_12px_text_dim_and_reuses_the_existing_chevron_geometry()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        var viewbox = DsResources.Descendants(box).OfType<Viewbox>()
            .Single(v => v.Width == 12 && v.Height == 12);
        var path = DsResources.Descendants(viewbox).OfType<System.Windows.Shapes.Path>().Single();

        Assert.Equal(host.FindResource("Icon.Chevron"), path.Data);
        Assert.Equal(DsResources.TokenColor(box, "Brush.TextDim"), DsResources.ColorOf(path.Stroke));
        Assert.Equal(PenLineCap.Round, path.StrokeStartLineCap);
        GC.KeepAlive(window);
    }

    /// <summary>Odak halkası Ds.Input'un DESENİ: kenarın hemen dışında (offset 0), focus'ta kenar amber-border'a
    /// döner. Headless'ta gerçek klavye odağı taşınamaz — bu yüzden trigger'ın KENDİSİ okunur (ScrollBarStyleTests
    /// deseni: bir trigger'ın varlığı GERÇEK setter'larıyla kanıtlanır, IsMouseOver/IsKeyboardFocusWithin
    /// simülasyonu değil).</summary>
    [StaFact]
    public void Keyboard_focus_shows_the_focus_ring_and_turns_the_border_amber()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        // Odak trigger'ı ControlTemplate.Triggers'tadır (Style.Triggers DEĞİL — Ds.Input'un AYNI deseni).
        var trigger = box.Template.Triggers.OfType<Trigger>()
            .Single(t => t.Property == UIElement.IsKeyboardFocusWithinProperty && Equals(t.Value, true));
        var focusRingSetter = trigger.Setters.OfType<Setter>()
            .Single(s => s.TargetName == "FocusRing" && s.Property == UIElement.VisibilityProperty);
        Assert.Equal(Visibility.Visible, focusRingSetter.Value);

        var borderSetter = trigger.Setters.OfType<Setter>()
            .Single(s => s.Property == DsTransition.AnimatedBorderBrushProperty);
        var amber = (SolidColorBrush)box.FindResource(((DynamicResourceExtension)borderSetter.Value).ResourceKey!);
        Assert.Equal(DsResources.TokenColor(box, "Brush.AmberBorder"), amber.Color);
        GC.KeepAlive(window);
    }

    /// <summary>Açılır liste popover kromu taşır: surface-overlay zemin, 1px border-strong, radius-md, overlay
    /// gölgesi — Branch popover'ın kabuğuyla AYNI token AİLESİ (yarıçap İSTİSNASI: brief radius-md'yi pinler,
    /// Ds.Popover'ın radius-lg'si DEĞİL).</summary>
    [StaFact]
    public void The_dropdown_carries_popover_chrome_with_a_medium_radius()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        box.IsDropDownOpen = true;
        box.UpdateLayout();

        var popup = PopupOf(box);
        var shell = (Border)popup.Child!;
        Assert.Equal(DsResources.TokenColor(box, "Brush.SurfaceOverlay"), DsResources.ColorOf(shell.Background));
        Assert.Equal(DsResources.TokenColor(box, "Brush.BorderStrong"), DsResources.ColorOf(shell.BorderBrush));
        Assert.Equal(new Thickness(1), shell.BorderThickness);
        Assert.Equal((CornerRadius)box.FindResource("Radius.Md"), shell.CornerRadius);
        Assert.NotNull(shell.Effect);
        GC.KeepAlive(window);
    }

    /// <summary>Satırlar 28px, hover'da surface-hover — 120ms'lik DS geçiş idiomunun ta kendisi (Ds.Input/
    /// Ds.IconButton'ın PAYLAŞTIĞI <c>DsTransition.AnimatedBackground</c>); ayrı bir popup-açılış animasyonu
    /// İCAT EDİLMEDİ (brief'in "giriş varsa 120ms'lik DS geçişi" cümlesi budur, bounce/overshoot zaten yok).</summary>
    [StaFact]
    public void Dropdown_rows_are_28px_and_hover_to_surface_hover_via_the_shared_120ms_transition()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        box.IsDropDownOpen = true;
        box.UpdateLayout();
        var item = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromIndex(0)!;
        item.ApplyTemplate();

        Assert.Equal(28.0, item.ActualHeight);

        var hoverTrigger = item.Style.Triggers.OfType<Trigger>()
            .Single(t => t.Property == UIElement.IsMouseOverProperty && Equals(t.Value, true));
        var backgroundSetter = hoverTrigger.Setters.OfType<Setter>()
            .Single(s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var hover = (SolidColorBrush)box.FindResource(((DynamicResourceExtension)backgroundSetter.Value).ResourceKey!);
        Assert.Equal(DsResources.TokenColor(box, "Brush.SurfaceHover"), hover.Color);
        GC.KeepAlive(window);
    }

    /// <summary>Fonksiyonel duman testi: iki sabit öğe (Git/TFVC) arasında seçim GERÇEKTEN değişir — şablonun
    /// yalnız görsel değil, ComboBox'ın kendi seçim sözleşmesiyle (SelectionBoxItem/PART_Popup) de çalıştığının
    /// kanıtı.</summary>
    [StaFact]
    public void Selecting_the_second_item_updates_the_selection()
    {
        var host = DsResources.NewHost();
        var box = NewSelect(host);
        var window = DsResources.Realize(host, box);

        Assert.Equal("Git", ((ComboBoxItem)box.SelectedItem).Content);

        box.SelectedIndex = 1;
        box.UpdateLayout();

        Assert.Equal("TFVC", ((ComboBoxItem)box.SelectedItem).Content);
        // SelectionBoxItem, seçili öğe bir ComboBoxItem'sa Content'ini AÇAR (WPF sözleşmesi) — şablonun
        // ContentPresenter'ı BUNU okur, kutuda "ComboBoxItem: TFVC" değil düz "TFVC" görünür.
        Assert.Equal("TFVC", box.SelectionBoxItem);
        GC.KeepAlive(window);
    }
}
