using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T60] <c>Resources/Controls.xaml</c> — DS kontrol kütüphanesi. Bu sınıf üç şeyi pinler:
/// (a) C/D/E fazlarının tükettiği <c>Style</c> ANAHTARLARININ tamamı vardır (eksik anahtar derlemede değil,
/// runtime'da patlardı); (b) şablonların ÜRETTİĞİ görsel design-v1 ile birebirdir (renk/ölçü kaynaktan);
/// (c) A13.2: 120ms geçişin hedefi ŞABLON-LOKAL, donmamış bir fırçadır — asla paylaşılan token fırçası.
///
/// <para>Kontroller GERÇEKTEN kurulur (ekran dışı pencere + ApplyTemplate): bir Style'ın setter'ını okumak,
/// o setter'ın şablona ULAŞTIĞINI kanıtlamaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class DsControlTemplateTests
{
    /// <summary>D/E fazlarının tükettiği yayınlanmış sözleşme. Bir anahtarın adı DEĞİŞİRSE burası kırılır.</summary>
    public static readonly string[] PublishedStyleKeys =
    [
        "Ds.Button.Primary.Sm", "Ds.Button.Primary.Md", "Ds.Button.Primary.Lg",
        "Ds.Button.Secondary.Sm", "Ds.Button.Secondary.Md", "Ds.Button.Secondary.Lg",
        "Ds.Button.Ghost.Sm", "Ds.Button.Ghost.Md", "Ds.Button.Ghost.Lg",
        "Ds.Button.Danger.Sm", "Ds.Button.Danger.Md", "Ds.Button.Danger.Lg",
        "Ds.SplitButton", "Ds.Chip", "Ds.Chip.Counter", "Ds.IconButton", "Ds.IconButton.Toggle",
        "Ds.Switch", "Ds.Segment", "Ds.Segment.Item", "Ds.Input", "Ds.Kbd", "Ds.ProgressBar",
        "Ds.Popover", "Ds.Dialog", "Ds.FocusVisual",
    ];

    [StaFact]
    public void Every_style_key_the_later_ui_tasks_consume_is_published()
    {
        var host = DsResources.NewHost();

        var missing = PublishedStyleKeys.Where(k => host.TryFindResource(k) is not Style).ToList();
        Assert.Empty(missing);
    }

    [StaFact]
    public void Primary_button_uses_amber_surface_and_on_accent_text_at_md_height_28()
    {
        // _ds_bundle.js:16 md = 28 · :22-23 amber zemin + text-on-accent metin · :107 radius-sm.
        var host = DsResources.NewHost();
        var button = new Button { Content = "Build", Style = (Style)host.FindResource("Ds.Button.Primary.Md") };
        var window = DsResources.Realize(host, button);

        Assert.Equal(28.0, button.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(button.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextOnAccent"), DsResources.ColorOf(button.Foreground));

        var chrome = DsResources.Descendants(button).OfType<Border>().First();
        Assert.Equal((CornerRadius)host.FindResource("Radius.Sm"), chrome.CornerRadius);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T4 · n5] mono asla dekoratif değil

    /// <summary>[A13/T4 · n5 · fix-1 · D4] design-v1 §1.2 (README:48): <i>"UI = Geist; makine çıktısı (console,
    /// süre, SHA, sayaç, yol) = Geist Mono, DAİMA tabular rakam. <b>Mono asla dekoratif kullanılmaz.</b>"</i> —
    /// <see cref="AppFonts"/> XML doc'u da aynı sözü tekrarlar.
    ///
    /// <para><b>Kapsam (bilinçli, dar):</b> bu test TÜM olası "dekoratif metin" yüzeyini taramaz (öznel bir sınır
    /// olurdu) — bunun yerine İKİ TEK-YERLİ kaynağı pinler: (1) <c>Ds.Button.Base</c> — <b>fix-1'de altı DS buton
    /// VARYANTININ HEPSİNDE</b> (<c>Button_sizes_match_the_design_height_scale</c>'in AYNI altı anahtarı) sınanır,
    /// çünkü uygulamadaki buton metinleri o varyantlar üzerinden akar (Primary/Secondary/Ghost/Danger) — önceki
    /// sürüm yalnız <c>Base</c>'i realize ediyordu ve bir varyanta EKLENECEK bir <c>FontFamily=Mono</c> setter'ı
    /// (taban değişmediği için) YAKALANMAZDI; (2) caps panel/dialog başlıkları (<c>PROJECTS</c>/<c>DEPENDENCY
    /// GRAPH</c>/<c>LAYERS</c>/…) TEK yerden, <see cref="TrackedTextBlock"/>'un varsayılan <c>FontFamily</c>'sinden
    /// beslenir — bu ZATEN <c>TrackedTextBlockTests.Defaults_match_design_v1_caps_label_spec</c>'te pinlidir
    /// (<c>"./#Geist"</c>, Mono DEĞİL); burada yalnız buton yüzeyi eklenir.</para>
    ///
    /// <para><b>fix-1 · D4 (ikinci düzeltme):</b> beklenen değer artık üretim SEMBOLÜ (<c>AppFonts.Ui</c>) değil,
    /// OTORİTE LİTERALİ (<c>"./#Geist"</c>) — <c>AppFonts.Ui</c>'nin kendisi mono aileye kaysa önceki assert
    /// (<c>Assert.Same(AppFonts.Ui, …)</c>) sessizce YEŞİL kalırdı. Mantıksal olarak birinciden çıkan ölü
    /// <c>Assert.NotSame(AppFonts.Mono, …)</c> satırı da kaldırıldı.</para></summary>
    [StaTheory]
    [InlineData("Ds.Button.Primary.Sm")]
    [InlineData("Ds.Button.Primary.Md")]
    [InlineData("Ds.Button.Primary.Lg")]
    [InlineData("Ds.Button.Secondary.Sm")]
    [InlineData("Ds.Button.Ghost.Md")]
    [InlineData("Ds.Button.Danger.Lg")]
    public void Every_button_caption_uses_the_ui_typeface_never_mono(string styleKey)
    {
        var host = DsResources.NewHost();
        var button = new Button { Content = "Cancel", Style = (Style)host.FindResource(styleKey) };
        var window = DsResources.Realize(host, button);

        Assert.Equal("./#Geist", button.FontFamily.Source); // otorite literali — Mono ("./#Geist Mono") DEĞİL
        GC.KeepAlive(window);
    }

    [StaTheory]
    [InlineData("Ds.Button.Primary.Sm", 24.0)]
    [InlineData("Ds.Button.Primary.Md", 28.0)]
    [InlineData("Ds.Button.Primary.Lg", 32.0)]
    [InlineData("Ds.Button.Secondary.Sm", 24.0)]
    [InlineData("Ds.Button.Ghost.Md", 28.0)]
    [InlineData("Ds.Button.Danger.Lg", 32.0)]
    public void Button_sizes_match_the_design_height_scale(string styleKey, double expectedHeight)
    {
        // _ds_bundle.js:14-17 — sm 24 / md 28 / lg 32; ÜÇ boy da DÖRT varyantta aynıdır.
        var host = DsResources.NewHost();
        var button = new Button { Style = (Style)host.FindResource(styleKey) };
        var window = DsResources.Realize(host, button);

        Assert.Equal(expectedHeight, button.Height);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Split_button_halves_share_one_body_with_flat_inner_corners()
    {
        // BuildApp.jsx:1594 sol yarımın SAĞ köşeleri 0 · :1596 sağ yarımın SOL köşeleri 0 · aralarında
        // 1px amber-dim çizgi. Köşeler gövdenin Radius.Sm token'ından TÜRETİLİR (literal yazılmaz).
        var host = DsResources.NewHost();
        var split = (FrameworkElement)XamlReader.Parse("""
            <controls:SplitButton xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                  xmlns:controls="clr-namespace:BuildOrchestrator.App.Controls;assembly=BuildOrchestrator.App"
                                  PrimaryContent="Build" />
            """);
        var window = DsResources.Realize(host, split);

        double r = ((CornerRadius)host.FindResource("Radius.Sm")).TopLeft;
        var corners = DsResources.Descendants(split).OfType<Border>()
            .Select(b => b.CornerRadius)
            .Where(c => c.TopLeft + c.TopRight + c.BottomRight + c.BottomLeft > 0)
            .ToList();

        Assert.Contains(new CornerRadius(r, 0, 0, r), corners); // sol yarım: iç (sağ) köşeler DÜZ
        Assert.Contains(new CornerRadius(0, r, r, 0), corners); // sağ yarım: iç (sol) köşeler DÜZ

        var divider = DsResources.Descendants(split).OfType<Rectangle>().Single(x => x.Width == 1);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberDim"), DsResources.ColorOf(divider.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Chip_active_state_uses_amber_soft_fill_amber_border_and_amber_text()
    {
        // _ds_bundle.js:170-174 — active ? amber-soft zemin / amber-border kenar / amber-text metin.
        // :167-168 height 24 + padding '0 8px'.
        var host = DsResources.NewHost();
        var chip = new ToggleButton { Content = "failed", Style = (Style)host.FindResource("Ds.Chip") };
        var window = DsResources.Realize(host, chip);

        Assert.Equal(24.0, chip.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(chip.Background));

        chip.IsChecked = true;
        chip.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoft"), DsResources.ColorOf(chip.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberBorder"), DsResources.ColorOf(chip.BorderBrush));
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(chip.Foreground));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Switch_is_a_checkbox_template_because_wpf_has_no_toggle_switch()
    {
        // _ds_bundle.js:865 kaynak da `<input type="checkbox" role="switch">`tır. :884-886 ray 28×16,
        // :896-898 başparmak 12×12, :899 açıkken amber zemin, :901 başparmak 12px sağa kayar.
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Switch");
        Assert.Equal(typeof(CheckBox), style.TargetType);

        var toggle = new CheckBox { Content = "worktree", Style = style };
        var window = DsResources.Realize(host, toggle);

        var track = (Border)toggle.Template.FindName("Track", toggle);
        var thumb = (Ellipse)toggle.Template.FindName("Thumb", toggle);
        Assert.Equal(28.0, track.Width);
        Assert.Equal(16.0, track.Height);
        Assert.Equal(12.0, thumb.Width);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceOverlay"), DsResources.ColorOf(track.Background));
        Assert.Equal(0.0, ((TranslateTransform)thumb.RenderTransform).X);

        toggle.IsChecked = true;
        toggle.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(track.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextOnAccent"), DsResources.ColorOf(thumb.Fill));
        Assert.Equal(12.0, ((TranslateTransform)thumb.RenderTransform).X);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Input_renders_a_watermark_and_a_prefix_slot_and_turns_red_when_invalid()
    {
        // _ds_bundle.js:714-720 height 28 / surface-sunken / 1px border-strong · :717 invalid kenarı
        // · :749 prefix varken metin alanı 26'ya kayar · BuildApp.jsx:837 placeholder.
        var host = DsResources.NewHost();
        var input = new TextBox { Style = (Style)host.FindResource("Ds.Input") };
        BuildOrchestrator.App.Controls.DsChrome.SetWatermark(input, "Search branches…");
        var window = DsResources.Realize(host, input);

        Assert.Equal(28.0, input.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceSunken"), DsResources.ColorOf(input.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.BorderStrong"), DsResources.ColorOf(input.BorderBrush));

        var watermark = (TextBlock)input.Template.FindName("Watermark", input);
        Assert.Equal("Search branches…", watermark.Text);
        Assert.Equal(Visibility.Visible, watermark.Visibility);   // metin boşken görünür

        // Prefix YOKken metin alanı 8'den başlar (:749 `paddingLeft: prefix ? 26 : 8`).
        var prefixSlot = (ContentPresenter)input.Template.FindName("Prefix", input);
        Assert.Equal(Visibility.Collapsed, prefixSlot.Visibility);
        Assert.Equal(new Thickness(8, 0, 8, 0), input.Padding);

        input.Text = "main";
        input.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, watermark.Visibility);

        BuildOrchestrator.App.Controls.DsChrome.SetPrefix(input, new TextBlock { Text = "Q" });
        input.UpdateLayout();
        Assert.Equal(Visibility.Visible, prefixSlot.Visibility);
        Assert.Equal(26.0, input.Padding.Left);

        BuildOrchestrator.App.Controls.DsChrome.SetIsInvalid(input, true);
        input.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusFailBorder"), DsResources.ColorOf(input.BorderBrush));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Focus_visual_is_a_two_pixel_amber_ring_with_one_pixel_offset()
    {
        // README:44 "2px rgba(237,161,15,.50) halka, offset 1px" → Tokens.xaml Size.FocusRingWidth /
        // Size.FocusRingOffset / Brush.FocusRing. Adorner öğenin sınırına oturduğundan halka NEGATİF
        // margin ile dışarı itilir: -(offset + kalınlık/2). Değerler token'dan OKUNUR — testte sabit değil.
        var host = DsResources.NewHost();
        var probe = new Control { Style = (Style)host.FindResource("Ds.FocusVisual"), Width = 60, Height = 20 };
        var window = DsResources.Realize(host, probe);

        var ring = (Rectangle)probe.Template.FindName("Ring", probe);
        double width = (double)host.FindResource("Size.FocusRingWidth");
        double offset = (double)host.FindResource("Size.FocusRingOffset");

        Assert.Equal(DsResources.TokenColor(host, "Brush.FocusRing"), DsResources.ColorOf(ring.Stroke));
        Assert.Equal(width, ring.StrokeThickness);
        Assert.Equal(new Thickness(-(offset + width / 2)), ring.Margin);

        GC.KeepAlive(window);

        // Ve gerçekten KULLANILIYOR: DS butonları odak görselini bu stile bağlar (öğe ağaca girmeden
        // {DynamicResource} çözülmez — bu yüzden buton da kurulur).
        var buttonHost = DsResources.NewHost();
        var button = new Button { Style = (Style)buttonHost.FindResource("Ds.Button.Secondary.Md") };
        var buttonWindow = DsResources.Realize(buttonHost, button);
        Assert.Same(buttonHost.FindResource("Ds.FocusVisual"), button.FocusVisualStyle);
        GC.KeepAlive(buttonWindow);
    }

    [StaFact]
    public void A_hover_transition_animates_a_template_local_brush_not_the_shared_token_brush()
    {
        // [A13.2] Tokens.xaml'in fırçaları PAYLAŞILIR ve DONMUŞtur — animasyon hedefi olamazlar (hem
        // InvalidOperationException, hem de aynı token'ı kullanan HER öğe birlikte oynardı). DsTransition
        // her öğeye kendi (donmamış) kopyasını kurar ve rengi O kopyada akıtır.
        var host = DsResources.NewHost();
        var button = new Button { Content = "Rebuild", Style = (Style)host.FindResource("Ds.Button.Secondary.Md") };
        var window = DsResources.Realize(host, button);

        var painted = (SolidColorBrush)button.Background;
        Assert.False(painted.IsFrozen);
        Assert.NotSame(host.FindResource("Brush.SurfaceRaised"), painted);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), painted.Color);

        // Hover'ın yaptığının AYNISI: hedef fırçayı değiştir. Geçiş AYNI instance üzerinde akmalı —
        // fırçayı DEĞİŞTİRMEK (yeni nesne atamak) animasyonu imkânsız kılardı.
        BuildOrchestrator.App.Controls.DsTransition.SetAnimatedBackground(
            button, (Brush)host.FindResource("Brush.SurfaceOverlay"));

        Assert.Same(painted, button.Background);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceOverlay"), painted.Color);
        Assert.False(((SolidColorBrush)host.FindResource("Brush.SurfaceRaised")).Color
            == DsResources.TokenColor(host, "Brush.SurfaceOverlay")); // iki token gerçekten farklı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Progress_bar_fills_its_track_in_proportion_to_the_value()
    {
        // _ds_bundle.js:510 varsayılan yükseklik 4 · :521 zemin surface-overlay · :498 building dolgusu amber
        // · :537 dolgu genişliği yüzdedir. WPF dolguyu PART_Track/PART_Indicator üzerinden KENDİ sürer —
        // şablon o iki parçayı gerçekten sunmazsa bar sessizce boş kalırdı, bu yüzden ölçülür.
        var host = DsResources.NewHost();
        var bar = new ProgressBar { Style = (Style)host.FindResource("Ds.ProgressBar"), Width = 200, Value = 25 };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(4.0, bar.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceOverlay"), DsResources.ColorOf(bar.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(bar.Foreground));

        var track = (FrameworkElement)bar.Template.FindName("PART_Track", bar);
        var indicator = (FrameworkElement)bar.Template.FindName("PART_Indicator", bar);
        Assert.Equal(track.ActualWidth * 0.25, indicator.Width, precision: 6);

        bar.Value = 100;
        bar.UpdateLayout();
        Assert.Equal(track.ActualWidth, indicator.Width, precision: 6);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void No_control_template_declares_a_storyboard()
    {
        // [Step 1 kararı] Şablon trigger'ındaki bir Storyboard canlı süre token'ı taşıyamaz (mühürlenirken
        // dondurulur). Bu guard, ileride "kolay yol" diye bir Storyboard eklenmesini derleme değil TEST
        // düzeyinde yakalar — aksi halde hata yalnız o şablonun ilk kullanıldığı ekranda görülürdü.
        string xaml = System.IO.File.ReadAllText(DsResources.AssetPath("Controls.xaml"));
        Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<BeginStoryboard", xaml, StringComparison.Ordinal);
    }

    [StaFact]
    public void Popover_and_dialog_surfaces_match_the_prototype_including_the_recorded_dialog_deviation()
    {
        // Popover: BuildApp.jsx:820-823 surface-overlay + border-strong + radius-lg + overlay gölgesi.
        // Dialog: _ds_bundle.js:965 surface-RAISED (README §2.9 "surface-overlay" der — KOD KAZANIR,
        // sapma T60 brief'inde kayıtlıdır).
        var host = DsResources.NewHost();
        var popover = new Border { Style = (Style)host.FindResource("Ds.Popover") };
        var dialog = new Border { Style = (Style)host.FindResource("Ds.Dialog") };
        var panel = new StackPanel { Children = { popover, dialog } };
        var window = DsResources.Realize(host, panel);

        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceOverlay"), DsResources.ColorOf(popover.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(dialog.Background));
        foreach (var surface in new[] { popover, dialog })
        {
            Assert.Equal(DsResources.TokenColor(host, "Brush.BorderStrong"), DsResources.ColorOf(surface.BorderBrush));
            Assert.Equal((CornerRadius)host.FindResource("Radius.Lg"), surface.CornerRadius);
            Assert.Same(host.FindResource("Effect.OverlayShadow"), surface.Effect);
        }
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Status_glyph_paints_every_status_with_its_own_colour_and_drawing()
    {
        // _ds_bundle.js:1402-1433 STATUS_META renkleri · :1459-1478 iç glyph'ler · :1515-1518 discovered
        // AYNI halkanın kesiklisi · ·:1505 building dönen yay (halka yok).
        var host = DsResources.NewHost();
        var glyph = (FrameworkElement)XamlReader.Parse("""
            <controls:StatusGlyph xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                  xmlns:controls="clr-namespace:BuildOrchestrator.App.Controls;assembly=BuildOrchestrator.App"
                                  Status="Failed" Size="16" />
            """);
        var window = DsResources.Realize(host, glyph);

        var paths = DsResources.Descendants(glyph).OfType<Path>().ToList();
        var ring = paths.Single(p => ReferenceEquals(p.Data, host.FindResource("Icon.StatusRing")));
        var inner = paths.Single(p => ReferenceEquals(p.Data, host.FindResource("Icon.StatusCross")));

        Assert.Equal(Visibility.Visible, ring.Visibility);
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusFailText"), DsResources.ColorOf(inner.Stroke));
        Assert.Empty(ring.StrokeDashArray);          // yalnız discovered kesiklidir
        Assert.Equal(0.6, ring.Opacity);             // _ds_bundle.js:1452

        glyph.SetValue(BuildOrchestrator.App.Controls.StatusGlyph.StatusProperty,
            BuildOrchestrator.App.Controls.GraphStatus.Discovered);
        glyph.UpdateLayout();
        Assert.NotEmpty(ring.StrokeDashArray);       // _ds_bundle.js:1517 dasharray "2.3 2.5"
        Assert.Equal(0.9, ring.Opacity);

        glyph.SetValue(BuildOrchestrator.App.Controls.StatusGlyph.StatusProperty,
            BuildOrchestrator.App.Controls.GraphStatus.Building);
        glyph.UpdateLayout();
        var spinner = DsResources.Descendants(glyph).OfType<BuildOrchestrator.App.Controls.BuildingSpinner>().Single();
        Assert.Equal(Visibility.Visible, spinner.Visibility);
        Assert.Equal(Visibility.Collapsed, ring.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Will_build_dot_is_filled_when_known_and_a_hollow_ring_when_sync_has_not_run()
    {
        // _ds_bundle.js:1859-1868 — dirty/clean DOLU, unknown içi boş + 1px halka; çap Size.DotSize (8).
        var host = DsResources.NewHost();
        var dot = (FrameworkElement)XamlReader.Parse("""
            <controls:WillBuildDot xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                   xmlns:controls="clr-namespace:BuildOrchestrator.App.Controls;assembly=BuildOrchestrator.App" />
            """);
        var window = DsResources.Realize(host, dot);

        var ellipse = DsResources.Descendants(dot).OfType<Ellipse>().Single();
        Assert.Equal((double)host.FindResource("Size.DotSize"), ellipse.Width);
        // Varsayılan durum unknown'dır: Sync'ten ÖNCE hiçbir şey bilinmez (README:224).
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotUnknown"), DsResources.ColorOf(ellipse.Fill));
        Assert.NotNull(ellipse.Stroke);

        dot.SetValue(BuildOrchestrator.App.Controls.WillBuildDot.StateProperty, true);
        dot.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotDirty"), DsResources.ColorOf(ellipse.Fill));
        Assert.Null(ellipse.Stroke);

        dot.SetValue(BuildOrchestrator.App.Controls.WillBuildDot.StateProperty, false);
        dot.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotClean"), DsResources.ColorOf(ellipse.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Segment_is_a_sunken_track_whose_selected_option_rises_to_the_overlay_surface()
    {
        // _ds_bundle.js:311-317 dış ray: h+2 (sm → 24), surface-sunken + 1px border, radius-sm, padding 1
        // · :337-341 seçili seçenek surface-overlay + text-primary, seçili değil saydam + text-dim.
        var host = DsResources.NewHost();
        var debug = new RadioButton { Content = "Debug", Style = (Style)host.FindResource("Ds.Segment.Item") };
        var release = new RadioButton { Content = "Release", Style = (Style)host.FindResource("Ds.Segment.Item") };
        var segment = new ItemsControl { Style = (Style)host.FindResource("Ds.Segment") };
        segment.Items.Add(debug);
        segment.Items.Add(release);
        var window = DsResources.Realize(host, segment);

        Assert.Equal(24.0, segment.Height);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceSunken"), DsResources.ColorOf(segment.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.Border"), DsResources.ColorOf(segment.BorderBrush));
        Assert.Equal(new Thickness(1), segment.Padding);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(debug.Foreground));

        debug.IsChecked = true;
        debug.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceOverlay"), DsResources.ColorOf(debug.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), DsResources.ColorOf(debug.Foreground));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(release.Foreground));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Kbd_has_a_thicker_bottom_edge_so_it_reads_as_a_key_cap()
    {
        // _ds_bundle.js:281-287 — surface-raised zemin, 1px border-strong ama ALT kenar 2px (tuş derinliği),
        // minWidth 16 / height 18 / padding '0 5px', mono + text-2xs.
        var host = DsResources.NewHost();
        var kbd = new ContentControl { Content = "F5", Style = (Style)host.FindResource("Ds.Kbd") };
        var window = DsResources.Realize(host, kbd);

        Assert.Equal(16.0, kbd.MinWidth);
        Assert.Equal(18.0, kbd.Height);
        Assert.Equal((double)host.FindResource("FontSize.2xs"), kbd.FontSize);

        var chrome = DsResources.Descendants(kbd).OfType<Border>().First();
        Assert.Equal(new Thickness(1, 1, 1, 2), chrome.BorderThickness);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(chrome.Background));
        Assert.Equal(DsResources.TokenColor(host, "Brush.BorderStrong"), DsResources.ColorOf(chrome.BorderBrush));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Will_build_dot_descriptions_are_english_not_the_turkish_source_labels()
    {
        // Kaynak DS Türkçe etiketler taşır (`aria-label="Kaldır"`, `title="Kapat"`, WillBuildDot'un
        // "Değişti — derlenecek"i). Uygulamanın kullanıcı-görünür metni İNGİLİZCEDİR; çeviri kopyalamayla
        // atlanmasın diye pinlenir (kod YORUMLARI Türkçe kalır — burada yalnız görünür metin denetlenir).
        foreach (bool? state in new bool?[] { true, false, null })
        {
            string text = BuildOrchestrator.App.Controls.WillBuildDot.DescriptionFor(state);
            Assert.DoesNotMatch("[ğüşıöçĞÜŞİÖÇ]", text); // kaynaktaki Türkçe etiketler çevrilmiş olmalı
            Assert.NotEmpty(text);
        }
    }
}
