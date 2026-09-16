using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.19.0 §2.9] Settings'in "sol raylı iki panel" düzeni: 880×576 sabit kabuk, başlık satırı + kapat,
/// 196px bölüm rayı (General · Workspace · External projects · Layers), PaneHead ile açılan sayfalar ve footer'ın
/// neden satırı. Davranışlar (Save/Export/Import/Clear) <see cref="SettingsDialogTests"/> ve
/// <see cref="SettingsPortabilityTests"/>'tedir; burada yalnız yeni düzenin realize kanıtı durur.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SettingsDialogLayoutTests
{
    private static readonly SettingsSection[] Sections =
        [SettingsSection.General, SettingsSection.Workspace, SettingsSection.External, SettingsSection.Layers];

    private static Border Frame(SettingsDialog dialog) => (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static TextBlock RailCount(RadioButton item)
    {
        item.ApplyTemplate();
        return (TextBlock)item.Template.FindName("Count", item);
    }

    // ---------------------------------------------------------------- kabuk

    /// <summary>880×576 SABİT; bölüm değişince dialog zıplamaz (yükseklik içerikten doğmaz).</summary>
    [StaFact]
    public void The_dialog_is_880_by_576_and_keeps_that_size_on_every_section()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r =>
            r.LayerPatterns = [.. Enumerable.Range(0, 20).Select(i => new LayerPattern(i, "^A" + i, "Layer " + i))]);
        using var _scope = scope;

        foreach (var section in Sections)
        {
            dialog.ShowSection(section);
            dialog.UpdateLayout();
            Assert.Equal(880.0, Frame(dialog).ActualWidth);
            Assert.Equal(576.0, Frame(dialog).ActualHeight);
        }
    }

    /// <summary>Başlık satırı: padding <c>12 12 12 18</c>, altta 1px <c>border-subtle</c>; solda <c>Settings</c>
    /// (14px/600 text-primary), sağda kapat ikon butonu — Cancel ile AYNI yol (taslak atılır).</summary>
    [StaFact]
    public void The_head_row_carries_the_title_and_a_close_button_that_discards_like_cancel()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var head = dialog.HeadRow;
        Assert.Equal(new Thickness(18, 12, 12, 12), head.Padding);
        Assert.Equal(new Thickness(0, 0, 0, 1), head.BorderThickness);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(head.BorderBrush));

        var title = DsResources.Descendants(head).OfType<TextBlock>().Single(t => t.Text == "Settings");
        Assert.Equal(14.0, title.FontSize);
        Assert.Equal(FontWeights.SemiBold, title.FontWeight);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextPrimary"), DsResources.ColorOf(title.Foreground));

        var close = dialog.CloseButton;
        Assert.True(DsResources.IsSelfOrDescendantOf(close, head));
        Assert.Same(dialog.FindResource("Ds.IconButton"), close.Style);
        Assert.Equal(22.0, close.ActualWidth);
        Assert.Equal(AccessibilityNames.CloseSettings, AutomationProperties.GetName(close));
        Assert.Equal("Close", close.ToolTip);
        var glyph = DsResources.Descendants(close).OfType<System.Windows.Shapes.Path>().Single();
        Assert.Same(dialog.FindResource("Icon.Close"), glyph.Data);

        dialog.Draft!.RepositoryRoot = @"D:\other";
        Click(close);

        Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        Assert.Equal(@"D:\repo", run.RootPath);     // taslak UYGULANMADI
        Assert.Empty(store.State.LayerPatterns);    // diske yazılmadı
    }

    // ---------------------------------------------------------------- ray

    /// <summary>Ray 196px, <c>surface</c> zemin, sağda 1px <c>border-subtle</c>, padding <c>10 8</c>; satırlar 30px,
    /// aralarında 2px, sıra General · Workspace · External projects · Layers, UIA adı etiket.</summary>
    [StaFact]
    public void The_rail_is_196px_on_surface_with_the_four_sections_in_order()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var rail = dialog.Rail;
        Assert.Equal(196.0, rail.ActualWidth);
        Assert.Equal(new Thickness(8, 10, 8, 10), rail.Padding);
        Assert.Equal(new Thickness(0, 0, 1, 0), rail.BorderThickness);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.Surface"), DsResources.ColorOf(rail.Background));
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(rail.BorderBrush));

        var items = Sections.Select(dialog.RailItem).ToList();
        Assert.Equal(["General", "Workspace", "External projects", "Layers"],
            items.Select(i => AutomationProperties.GetName(i) is { Length: > 0 } n ? n : i.Content as string));
        var style = dialog.FindResource("Ds.Settings.RailItem");
        Assert.All(items, i =>
        {
            Assert.Same(style, i.Style);
            Assert.True(DsResources.IsSelfOrDescendantOf(i, rail));
            Assert.Equal(30.0, i.ActualHeight);
            Assert.True(i.Focusable, "ray satırı klavyeyle erişilebilir olmalı");
            Assert.Equal(13.0, i.FontSize);
            Assert.Equal(FontWeights.Medium, i.FontWeight);
        });
        var tops = items.Select(i => i.TranslatePoint(new Point(0, 0), rail).Y).ToList();
        for (int k = 1; k < tops.Count; k++) Assert.Equal(32.0, tops[k] - tops[k - 1], precision: 1);
    }

    /// <summary>Aktif satır <c>surface-overlay</c> + <c>text-primary</c>; pasifler saydam + <c>text-dim</c>. Hover
    /// yalnız pasif satırda <c>surface-raised</c> verir (stil tetikleyicisi).</summary>
    [StaFact]
    public void The_active_rail_item_is_overlay_and_primary_while_the_rest_are_transparent_and_dim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        foreach (var active in Sections)
        {
            dialog.ShowSection(active);
            dialog.UpdateLayout();
            foreach (var section in Sections)
            {
                var item = dialog.RailItem(section);
                if (section == active)
                {
                    Assert.Same(dialog.FindResource("Brush.SurfaceOverlay"), DsTransition.GetAnimatedBackground(item));
                    Assert.Same(dialog.FindResource("Brush.TextPrimary"), DsTransition.GetAnimatedForeground(item));
                }
                else
                {
                    Assert.Equal(Colors.Transparent, DsResources.ColorOf(DsTransition.GetAnimatedBackground(item)));
                    Assert.Same(dialog.FindResource("Brush.TextDim"), DsTransition.GetAnimatedForeground(item));
                }
            }
        }

        var style = (Style)dialog.FindResource("Ds.Settings.RailItem");
        var hover = style.Triggers.OfType<MultiTrigger>().Single(t =>
            t.Conditions.Any(c => c.Property == UIElement.IsMouseOverProperty && Equals(c.Value, true))
            && t.Conditions.Any(c => c.Property == ToggleButton.IsCheckedProperty && Equals(c.Value, false)));
        var bg = hover.Setters.OfType<Setter>().Single(s => s.Property == DsTransition.AnimatedBackgroundProperty);
        Assert.Equal("Brush.SurfaceRaised", ((DynamicResourceExtension)bg.Value).ResourceKey);
    }

    /// <summary>Sayaç (mono 11px, tabular) yalnız &gt;0 iken görünür: External projects = taslak kart sayısı, Layers
    /// = taslak katman sayısı; taslakla CANLI güncellenir. Renk: aktif satırda text-secondary, pasifte text-faint.
    /// General ve Workspace sayaç taşımaz.</summary>
    [StaFact]
    public void Rail_counts_appear_only_above_zero_and_follow_the_draft_live()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r => r.LayerPatterns =
            [new LayerPattern(0, "^A", "A"), new LayerPattern(1, "^B", "B")]);
        using var _scope = scope;

        var external = RailCount(dialog.RailItem(SettingsSection.External));
        var layers = RailCount(dialog.RailItem(SettingsSection.Layers));
        Assert.Equal(Visibility.Collapsed, RailCount(dialog.RailItem(SettingsSection.General)).Visibility);
        Assert.Equal(Visibility.Collapsed, RailCount(dialog.RailItem(SettingsSection.Workspace)).Visibility);

        Assert.Equal(Visibility.Collapsed, external.Visibility); // 0 → görünmez
        Assert.Equal(Visibility.Visible, layers.Visibility);
        Assert.Equal("2", layers.Text);
        Assert.Equal(AppFonts.Mono, layers.FontFamily);
        Assert.Equal(11.0, layers.FontSize);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(layers));

        dialog.Draft!.AddExternal();
        dialog.Draft!.RemoveLayer(dialog.Draft!.Layers[0]);
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Visible, external.Visibility);
        Assert.Equal("1", external.Text);
        Assert.Equal("1", layers.Text);

        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextSecondary"), DsResources.ColorOf(layers.Foreground));
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextFaint"), DsResources.ColorOf(external.Foreground));

        dialog.Draft!.RemoveLayer(dialog.Draft!.Layers[0]);
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, layers.Visibility); // tekrar 0 → gizlenir
    }

    /// <summary>Açılış bölümü: first run (workspace yok) → Workspace; sonraki açılışlar → General. Tam bir sayfa
    /// görünür.</summary>
    [StaFact]
    public void The_opening_section_is_workspace_on_first_run_and_general_afterwards()
    {
        var (firstRun, _, _, firstScope) = SettingsDialogHost.OpenRealized(r => r.RootPath = "");
        using (firstScope)
        {
            Assert.True(firstRun.RailItem(SettingsSection.Workspace).IsChecked);
            Assert.Equal(Visibility.Visible, firstRun.Page(SettingsSection.Workspace).Visibility);
            Assert.Single(Sections, s => firstRun.Page(s).Visibility == Visibility.Visible);
        }

        var (later, run, store, laterScope) = SettingsDialogHost.OpenRealized();
        using (laterScope)
        {
            Assert.True(later.RailItem(SettingsSection.General).IsChecked);
            Assert.Single(Sections, s => later.Page(s).Visibility == Visibility.Visible);

            later.ShowSection(SettingsSection.Layers);
            later.CloseDialog();
            later.Open(run, store, () => null);
            Assert.True(later.RailItem(SettingsSection.General).IsChecked); // her açılış yeniden karar verir
        }
    }

    // ---------------------------------------------------------------- sayfalar

    /// <summary>Her sayfa PaneHead ile açılır: başlık 14px/600 text-primary + 5px altında tek satır açıklama (12px
    /// text-dim, snug 16.2, en çok 520, sarılır), altı 18px. Metinler birebir (External git-only uyarlama; Layers'ta
    /// <c>Other</c> mono).</summary>
    [StaFact]
    public void Every_page_opens_with_its_pane_head()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var expected = new Dictionary<SettingsSection, (string Title, string Description)>
        {
            [SettingsSection.General] = ("General", "How the app starts and behaves. Every setting applies when you save."),
            [SettingsSection.Workspace] = ("Workspace", "The folder that holds the solutions. Projects and the dependency graph are discovered from it on Sync."),
            [SettingsSection.External] = ("External projects", "Projects outside the repository root — a folder, a solution or a project file; the git working copy is found from the path. They are built before everything else."),
            [SettingsSection.Layers] = ("Layers", "Projects are grouped by the first matching pattern (regex on the project name), top to bottom. Non-matching projects fall under Other."),
        };
        var titleStyle = dialog.FindResource("Ds.Settings.PaneTitle");
        var descriptionStyle = dialog.FindResource("Ds.Settings.PaneDescription");

        foreach (var section in Sections)
        {
            dialog.ShowSection(section);
            dialog.UpdateLayout();
            var page = dialog.Page(section);
            var blocks = DsResources.RealizedObjects(page).OfType<TextBlock>().ToList();

            var title = blocks.Single(b => ReferenceEquals(b.Style, titleStyle));
            var description = blocks.Single(b => ReferenceEquals(b.Style, descriptionStyle));
            Assert.Equal(expected[section].Title, title.Text);
            Assert.Equal(expected[section].Description, Flatten(description));

            Assert.Equal(14.0, title.FontSize);
            Assert.Equal(FontWeights.SemiBold, title.FontWeight);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextPrimary"), DsResources.ColorOf(title.Foreground));
            Assert.Equal(12.0, description.FontSize);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(description.Foreground));
            Assert.Equal(16.2, description.LineHeight, precision: 6);
            Assert.Equal(520.0, description.MaxWidth);
            Assert.Equal(TextWrapping.Wrap, description.TextWrapping);
            Assert.Equal(new Thickness(0, 5, 0, 18), description.Margin);

            // Başlık sayfanın EN ÜSTÜNDE (padding 20 20 24 — ilk satır sayfanın 20px içinde).
            Assert.Equal(20.0, title.TranslatePoint(new Point(0, 0), dialog.Body).Y, precision: 1);
            Assert.Equal(20.0, title.TranslatePoint(new Point(0, 0), dialog.Body).X, precision: 1);
        }

        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();
        var layersDescription = DsResources.RealizedObjects(dialog.Page(SettingsSection.Layers)).OfType<TextBlock>()
            .Single(b => ReferenceEquals(b.Style, descriptionStyle));
        var other = layersDescription.Inlines.OfType<Run>().Single(r => r.Text == "Other");
        Assert.Equal(AppFonts.Mono, other.FontFamily);
    }

    private static string Flatten(TextBlock block) =>
        block.Inlines.Count == 0 ? block.Text : string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>Workspace: caps <c>REPOSITORY ROOT</c> (altı 7), mono input (watermark <c>D:\src\myapp</c> — ürüne özel
    /// değil) + 8px arayla secondary <c>Browse…</c>, 9px altında 11px text-faint zorunluluk notu.</summary>
    [StaFact]
    public void The_workspace_page_has_the_caps_label_a_product_neutral_watermark_and_the_required_note()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.Workspace);
        dialog.UpdateLayout();

        var page = dialog.Page(SettingsSection.Workspace);
        var blocks = DsResources.RealizedObjects(page).OfType<TextBlock>().ToList();
        var caps = blocks.Single(b => b.Text == "REPOSITORY ROOT");
        Assert.Same(dialog.FindResource("Ds.Settings.CapsHeading"), caps.Style);
        Assert.Equal(7.0, caps.Margin.Bottom);

        Assert.Equal(@"D:\src\myapp", DsChrome.GetWatermark(dialog.RootInput));
        Assert.Equal(AppFonts.Mono, dialog.RootInput.FontFamily);

        var note = blocks.Single(b => b.Text == "Required — nothing is discovered without it.");
        Assert.Equal(11.0, note.FontSize);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextFaint"), DsResources.ColorOf(note.Foreground));
        Assert.Equal(new Thickness(0, 9, 0, 0), note.Margin);

        var browse = DsResources.RealizedObjects(page).OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == AccessibilityNames.BrowseRepositoryRoot);
        double gap = dialog.RootInput.TranslatePoint(new Point(0, 0), browse).X + dialog.RootInput.ActualWidth;
        Assert.Equal(-8.0, gap, precision: 1); // input'un sağ kenarı düğmenin 8px solunda
    }

    /// <summary>External projects: <c>PROJECT PATH</c> kolon başlığı YOK, path watermark ürün-bağımsız
    /// (<c>MyApp.Common</c>); <c>Add external project</c> ghost sm, üstü 10, solu −10.</summary>
    [StaFact]
    public void The_external_page_has_no_column_header_and_a_product_neutral_path_watermark()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r => r.ExternalProjects = [new ExternalProject(@"C:\a")]);
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.External);
        dialog.UpdateLayout();

        var page = dialog.Page(SettingsSection.External);
        Assert.DoesNotContain(DsResources.RealizedObjects(page).OfType<TextBlock>(), t => t.Text == "PROJECT PATH");

        var path = DsResources.Descendants(dialog.ExternalsList).OfType<TextBox>().Single();
        Assert.Equal(@"C:\src\shared\MyApp.Common\MyApp.Common.csproj", DsChrome.GetWatermark(path));

        var add = DsResources.RealizedObjects(page).OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Add external project");
        Assert.Equal(new Thickness(-10, 10, 0, 0), add.Margin);
        Assert.Equal(24.0, add.ActualHeight); // ghost sm
    }

    /// <summary>Layers: kolon başlıkları (<c>LAYER NAME</c> 170 + <c>PATTERN</c>, padding <c>0 34 5 30</c>) yalnız
    /// katman varken; kayıtlı katman yokken boş-durum kutusu; <c>Add layer</c> ghost sm, üstü 10, solu −10 ve BOŞ
    /// satır ekler; kartın input'ları satır indeksinin placeholder'ını gösterir (7. satırda başa döner).</summary>
    [StaFact]
    public void The_layers_page_starts_empty_and_add_layer_realizes_rows_with_index_placeholders()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();

        var page = dialog.Page(SettingsSection.Layers);
        Assert.Empty(dialog.Draft!.Layers);
        Assert.Equal(Visibility.Visible, dialog.EmptyState.Visibility);
        Assert.Equal(Visibility.Collapsed, dialog.LayerColumnHeader.Visibility);
        Assert.Equal(new Thickness(30, 0, 34, 5), dialog.LayerColumnHeader.Margin);

        var add = DsResources.RealizedObjects(page).OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == AccessibilityNames.AddLayer);
        Assert.Equal(new Thickness(-10, 10, 0, 0), add.Margin);
        for (int i = 0; i < 7; i++) Click(add);
        dialog.UpdateLayout();

        Assert.Equal(7, dialog.Draft!.Layers.Count);
        Assert.All(dialog.Draft!.Layers, r => Assert.Equal(("", ""), (r.Name, r.Regex)));
        Assert.Equal(Visibility.Collapsed, dialog.EmptyState.Visibility);
        Assert.Equal(Visibility.Visible, dialog.LayerColumnHeader.Visibility);

        (string Name, string Pattern) Watermarks(LayerRowViewModel row)
        {
            var presenter = (ContentPresenter)dialog.LayersList.ItemContainerGenerator.ContainerFromItem(row)!;
            var boxes = DsResources.Descendants(presenter).OfType<TextBox>().ToList();
            return (DsChrome.GetWatermark(boxes.Single(b => AutomationProperties.GetName(b) == AccessibilityNames.LayerName))!,
                DsChrome.GetWatermark(boxes.Single(b => AutomationProperties.GetName(b) == AccessibilityNames.LayerPattern))!);
        }
        Assert.Equal(("Core", @"^MyApp\.(Core|Common)\."), Watermarks(dialog.Draft!.Layers[0]));
        Assert.Equal(("Client", @"^MyApp\.(Web|Client|Mobile)\."), Watermarks(dialog.Draft!.Layers[5]));
        Assert.Equal(("Core", @"^MyApp\.(Core|Common)\."), Watermarks(dialog.Draft!.Layers[6]));

        // Sürükle-bırak (Move) sonrası kart YENİ indeksinin placeholder'ını gösterir.
        var moved = dialog.Draft!.Layers[0];
        dialog.Draft!.Layers.Move(0, 2);
        dialog.UpdateLayout();
        Assert.Equal(("Domain", @"^MyApp\.Domain\."), Watermarks(moved));
    }

    /// <summary>Katman ve harici kartları grip'i, sil düğmesini ve Add düğmesini AYNI paylaşılan stillerden alır —
    /// iki kartın ölçüleri iki yerde tanımlanmaz (kopya YASAK).</summary>
    [StaFact]
    public void Layer_and_external_cards_share_the_grip_remove_and_add_styles()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r =>
        {
            r.LayerPatterns = [new LayerPattern(0, "^A", "A")];
            r.ExternalProjects = [new ExternalProject(@"C:\a")];
        });
        using var _scope = scope;

        foreach (var (section, list) in new[] { (SettingsSection.Layers, (ItemsControl)dialog.LayersList), (SettingsSection.External, dialog.ExternalsList) })
        {
            dialog.ShowSection(section);
            dialog.UpdateLayout();
            var card = DsResources.Descendants(list).OfType<Border>().First(b => ReferenceEquals(b.Style, dialog.FindResource("Ds.Settings.Card")));
            var grip = Assert.Single(DsResources.Descendants(card).OfType<ContentControl>(),
                c => ReferenceEquals(c.Style, dialog.FindResource("Ds.Settings.CardGrip")));
            var handle = Assert.Single(DsResources.Descendants(grip).OfType<Border>(), DragReorderBehavior.GetIsDragHandle);
            Assert.Equal(20.0, handle.ActualWidth);
            var remove = Assert.Single(DsResources.Descendants(card).OfType<Button>(),
                b => ReferenceEquals(b.Style, dialog.FindResource("Ds.Settings.CardRemove")));
            // Şablon GERÇEKTEN çizildi: içeriksiz düğmede ContentTemplate'in çöp kutusu var.
            Assert.Single(DsResources.Descendants(remove).OfType<System.Windows.Shapes.Path>(),
                p => ReferenceEquals(p.Data, dialog.FindResource("Icon.Trash")));
            Assert.Single(DsResources.RealizedObjects(dialog.Page(section)).OfType<Button>(),
                b => ReferenceEquals(b.Style, dialog.FindResource("Ds.Settings.AddRow")));
        }
    }

    /// <summary>Title bar dişlisinin tooltip'i dört bölümü sayar.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ metin <c>Settings — repository root and layer
    /// definitions</c> idi (tek kolonlu diyalog); ray dört bölüm taşıdığı için metin onları adlandırır.</para></summary>
    [StaFact]
    public void The_gear_tooltip_names_the_four_sections()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal("Settings — general, workspace, external projects and layers",
            ((ToolTip)window.GearButton.ToolTip).Content);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- footer

    /// <summary>Footer'da <c>Load sample layers</c> ve ayracı YOK; üç ikonun tooltip'leri birebir.</summary>
    [StaFact]
    public void The_footer_has_no_sample_layers_button_and_carries_the_design_tooltips()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var objects = DsResources.RealizedObjects(dialog).ToList();
        Assert.DoesNotContain(objects.OfType<Button>(), b => Equals(b.Content, "Load sample layers"));
        Assert.DoesNotContain(objects.OfType<TextBlock>(), t => t.Text == "Load sample layers");

        Assert.Equal("Export settings — the whole form as a JSON file", dialog.Export.ToolTip);
        Assert.Equal("Import settings — fill this form from a JSON file", dialog.Import.ToolTip);
        Assert.Equal("Clear settings — empty the form", dialog.Clear.ToolTip);
    }

    /// <summary>Save kapalıyken ve geri bildirim YOKKEN footer tek satır neden gösterir (12px text-faint, kırpılır);
    /// geri bildirim belirince neden gizlenir, Save açılınca neden kalmaz. Hangi sayfada olunduğu fark etmez.</summary>
    [StaFact]
    public void The_footer_shows_the_blocked_reason_only_while_save_is_off_and_there_is_no_feedback()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r => r.RootPath = "");
        using var _scope = scope;
        var reason = dialog.BlockedReason;

        Assert.Equal("Repository root is required", reason.Text);
        Assert.Equal(Visibility.Visible, reason.Visibility);
        Assert.Equal(12.0, reason.FontSize);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextFaint"), DsResources.ColorOf(reason.Foreground));
        Assert.Equal(TextTrimming.CharacterEllipsis, reason.TextTrimming);

        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Visible, reason.Visibility); // sayfa fark etmez

        Click(dialog.Clear); // armed → geri bildirim satırı dolu
        dialog.UpdateLayout();
        Assert.NotEqual("", dialog.Feedback.Text);
        Assert.Equal(Visibility.Collapsed, reason.Visibility);

        DispatcherPump.PumpUntil(() => dialog.Feedback.Text == "",
            TimeSpan.FromMilliseconds(SettingsDialog.FeedbackMs) + TimeSpan.FromSeconds(1));
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Visible, reason.Visibility);

        dialog.Draft!.RepositoryRoot = @"D:\repo";
        dialog.UpdateLayout();
        Assert.True(dialog.Save.IsEnabled);
        Assert.True(reason.Visibility != Visibility.Visible || string.IsNullOrEmpty(reason.Text),
            "Save açıkken neden satırı görünmemeli");
    }
}
