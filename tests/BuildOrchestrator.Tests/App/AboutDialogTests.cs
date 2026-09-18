using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About modali (design v1.19.0 §2.10). Kabuk üç dialogun ORTAK kabuğudur (<c>Controls/ModalDialog</c>, bkz.
/// DialogShellTests); bu XAML kökünün realize kanıtı ve içerik kuralları burada durur. Headless süit XAML runtime
/// çözümlemesini görmez — bu yüzden realize ZORUNLU (CLAUDE.md).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AboutDialogTests
{
    private const int AboutTab = 0;
    private const int EnvironmentTab = 1;
    private const int ShortcutsTab = 2;

    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    // Gerçek makinedeki LOCALAPPDATA yollarının uzunluğuna bel bağlamaz: hangi font gerçekten çözülürse
    // çözülsün (headless testte AppFonts.Mono'nun pack:// kaynağı yoktur, WPF bir yedeğe düşer) bu uzunluk
    // Environment hücresinin ~440px'lik görünür genişliğini KESİNLİKLE taşırır.
    private static readonly string OverflowingRootPath = @"D:\" + new string('a', 200) + @"\repo";

    private static IReadOnlyList<RadioButton> Tabs(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<RadioButton>()];

    private static List<string> VisibleTexts(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text)];

    private static void Select(BuildOrchestrator.App.Views.AboutDialog dialog, int index)
    {
        Tabs(dialog)[index].IsChecked = true;
        dialog.UpdateLayout();
    }

    private static double TopIn(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0, 0), root).Y;

    /// <summary>Görünür bir metnin (TextBlock ya da caps TrackedTextBlock) elemanı.</summary>
    private static FrameworkElement VisibleText(FrameworkElement root, string text) =>
        DsResources.Descendants(root).OfType<FrameworkElement>()
            .Where(e => e.IsVisible)
            .Single(e => e is TextBlock t && t.Text == text || e is TrackedTextBlock c && c.Text == text);

    /// <summary>Environment satırları (Runtime + Paths) — sekmenin çizdiği TÜM satırlar.</summary>
    private static IReadOnlyList<DiagnosticsLine> EnvironmentLines(BuildOrchestrator.App.Views.AboutDialog dialog) =>
        [.. dialog.Diagnostics!.Runtime, .. dialog.Diagnostics!.Paths];

    /// <summary>Bir Environment satırının değer hücresini ETİKETİNDEN bulur — <c>DataContext</c> şablonun
    /// köküne bağlanan <see cref="DiagnosticsLine"/>'dan ScrollViewer'a KADAR aynen akar (WPF değer
    /// kalıtımı), bu yüzden hücre kendi satırının verisiyle güvenle eşleştirilir.</summary>
    private static ScrollViewer EnvironmentValueScroller(FrameworkElement dialog, string label) =>
        DsResources.Descendants(dialog).OfType<ScrollViewer>()
            .Single(sv => sv.DataContext is DiagnosticsLine line && line.Label == label);

    /// <summary>Gerçek bir <c>MouseWheel</c> routed event'i — HWND/SendMessage GEREKMEZ (yalnız
    /// <c>HorizontalWheelScroll</c>'un çözdüğü <c>WM_MOUSEHWHEEL</c> için gerekirdi, bkz. o sınıfın XML
    /// doc'u); WPF düz dikey tekerleği zaten routed event olarak dağıttığı için doğrudan
    /// <see cref="UIElement.RaiseEvent"/> yeterlidir. Kaydırma senkrondur (OnEnvironmentValueWheel bir
    /// Dispatcher turu ERTELEMEZ), <c>UpdateLayout</c> yalnız yayınlanan <c>HorizontalOffset</c>'in bir
    /// layout turu istediği ihtimaline karşı savunmacıdır.
    ///
    /// <para><b>İKİ faz raise edilir, çünkü WPF'in <c>InputManager</c>'ı da öyle yapar:</b> önce tünel
    /// (<c>PreviewMouseWheel</c>), preview YUTMADIYSA baloncuk (<c>MouseWheel</c>). Baloncuk fazı ŞARTTIR:
    /// <see cref="ScrollViewer"/>'ın olayı yutan class handler'ı (<c>OnMouseWheel</c>) YALNIZ orada koşar —
    /// tek başına preview raise etmek "hücre olayı dışarı bırakıyor mu" sorusunu HİÇ sormaz.</para></summary>
    private static MouseWheelEventArgs RaiseWheel(
        BuildOrchestrator.App.Views.AboutDialog dialog, ScrollViewer target, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.MouseWheelEvent });
        dialog.UpdateLayout();
        return preview;
    }

    /// <summary>Bir değer hücresini SARAN sekme paneli — Environment satırlarının dikey kaydırıcısı.</summary>
    private static ScrollViewer EnvironmentTabScroller(ScrollViewer valueCell) =>
        DsResources.Ancestors(valueCell).OfType<ScrollViewer>().First();

    // ---------------------------------------------------------------- kabuk

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> ESKİ İDDİA (design v1.13.1): About 660px genişti — en uzun
    /// yol (MSBuild) tek satıra sığsın diye 600'den büyütülmüştü ve Third-party listesi dikeyde büyüyecekti.
    /// v1.19.0 Third-party sekmesini kaldırdı ve About'u sadeleştirdi: genişlik <b>620px</b>. Uzun yollar zaten
    /// görünmez yatay kaydırmayla okunuyor (<c>The_wheel_*</c> testleri), genişliğin onları kovalaması gerekmez.
    /// </summary>
    [StaFact]
    public void The_dialog_realizes_and_is_six_hundred_twenty_pixels_wide()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.Equal(620.0, dialog.Frame.Width);
            Assert.Equal(620.0, dialog.Frame.ActualWidth); // realize zorunlu — literal okumak yetmez
            Assert.True(double.IsNaN(dialog.Frame.Height), "About'un yüksekliği içerikten doğar");
        }
    }

    /// <summary>Yapısal kanıt: scrim bir Cycle klavye-gezinme kapsayıcısı ve bir odak kapsamı.</summary>
    [StaFact]
    public void The_scrim_is_a_cyclic_keyboard_focus_scope()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog.Scrim));
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(dialog.Scrim));
            Assert.True(FocusManager.GetIsFocusScope(dialog.Scrim));
        }
    }

    /// <summary>Gerçek gezinme kanıtı: About açıkken arka plandaki odaklanabilir bir kontrole Tab ile
    /// ULAŞILAMAZ. İddia <see cref="FocusTrap.AssertCannotEscape"/> ile Settings'inkiyle PAYLAŞILIR.</summary>
    [StaFact]
    public void Tab_navigation_cannot_escape_the_open_dialog()
    {
        var background = new Button { Content = "Background Build", Focusable = true, Width = 90, Height = 24 };
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(backgroundSibling: background);
        using (scope)
            FocusTrap.AssertCannotEscape(dialog.Scrim, background);
    }

    /// <summary>[design-v1.2.1 §2.10] Diyalog giriş animasyonu: 180ms fade + 6px yukarı. Süre bir
    /// TOKEN'dır (<c>Duration.Base</c> = 0.18s) — çağrı yerinde ms literali YASAK.</summary>
    [StaFact]
    public void The_dialog_enters_with_a_180ms_fade_and_a_6px_rise()
    {
        Assert.Equal(180.0, PopIn.DialogDurationMs);
        Assert.Equal(6.0, PopIn.DialogRiseFromPx);

        var host = DsResources.NewHost();
        Assert.Equal(TimeSpan.FromMilliseconds(PopIn.DialogDurationMs),
            MotionTokens.ResolveDuration(host, "Duration.Base", fallbackMs: -1).TimeSpan);
    }

    /// <summary>Giriş GERÇEKTEN kuruluyor: animasyon açıkken kabuğa bir YÜKSELME transform'u takılır.</summary>
    [StaFact]
    public void Opening_the_dialog_installs_the_entrance_transform_on_the_shell()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var (dialog, _run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
            Assert.IsType<TranslateTransform>(dialog.Frame.RenderTransform);
    }

    /// <summary>Reduced-motion: hiç animasyon KURULMAZ, diyalog son duruma snap eder (motion sözleşmesi).</summary>
    [StaFact]
    public void Reduced_motion_snaps_the_dialog_to_its_final_state()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = false }));
        var (dialog, _run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(1.0, dialog.Frame.Opacity);
            Assert.Equal(Transform.Identity, dialog.Frame.RenderTransform);
        }
    }

    [StaFact]
    public void Close_dialog_hides_it()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.CloseDialog();
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        }
    }

    // ---------------------------------------------------------------- kimlik bloğu

    /// <summary>
    /// [design v1.19.0 §2.10] Kimlik bloğu (padding 20/18/20): ürün markası <b>28px</b> · ürün adı + 9px yanında
    /// <b>sürüm çipi</b> (19px yüksek, yatay padding 6, 1px kenar, mono 11px) · altında tagline.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ İDDİA (design-v1.2.1): marka 30px'ti ve adın altında
    /// TEK mono satır <c>{sürüm} · {telif}</c> dururdu. v1.19.0 sürümü başlıktaki çipe, telifi About sekmesinin
    /// Copyright satırına taşıdı — eski satır KALKTI (geri sızmasın diye yokluğu da assert edilir). Motor sürümü
    /// başlıkta hâlâ GEÇMEZ: yeri About sekmesinin Engine satırıdır.</para></summary>
    [StaFact]
    public void The_identity_block_carries_a_28px_mark_the_name_a_version_chip_and_the_tagline()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            var head = (Border)dialog.Head!;
            Assert.Equal(new Thickness(18, 20, 18, 20), head.Padding);

            Assert.Equal(28.0, DsResources.Descendants(head).OfType<AppMark>().Single().Height);

            var headTexts = DsResources.Descendants(head).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(AppIdentity.Product, headTexts);
            Assert.Contains(AppIdentity.Tagline, headTexts);

            var chip = dialog.VersionChip;
            Assert.Same(dialog.FindResource("Ds.Tag"), chip.Style); // kutu What's new çipleriyle ortak stildir (kopya YASAK)
            Assert.Equal(19.0, chip.ActualHeight);
            Assert.Equal(new Thickness(6, 0, 6, 0), chip.Padding);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
            var chipText = (TextBlock)chip.Child;
            Assert.Equal(AppIdentity.Version, chipText.Text);
            Assert.Equal(11.0, chipText.FontSize);
            Assert.Equal(AppFonts.Mono, chipText.FontFamily);

            // Eski mono satır YOK; motor sürümü başlıkta GEÇMEZ.
            Assert.DoesNotContain($"{AppIdentity.Version} · {AppIdentity.Copyright}", VisibleTexts(dialog));
            Assert.DoesNotContain(headTexts, t => t.Contains("9.9.9+test", StringComparison.Ordinal));
        }
    }

    /// <summary>[design-v1.2.1 §2.10] Başlıkta İKİ logo tek kompozisyonda: solda ürün markası, sağda
    /// <c>LICENSED TO</c> bloğu + firma logosu 13px %80. Ürün önde.</summary>
    [StaFact]
    public void The_identity_block_locks_the_product_mark_against_a_licensed_to_company_block()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var mark = DsResources.Descendants(dialog).OfType<AppMark>().Single();
            var logo = DsResources.Descendants(dialog).OfType<BrandLogo>().Single();

            Assert.Equal(13.0, logo.Height);
            Assert.Equal(0.8, logo.Opacity, precision: 2);

            // Caps etiketi izli (tracked) çizilir — TrackedTextBlock bir TextBlock DEĞİL (§14.2).
            Assert.Single(DsResources.Descendants(dialog).OfType<TrackedTextBlock>(),
                t => t.Text.Equals("LICENSED TO", StringComparison.OrdinalIgnoreCase));

            double markX = mark.TranslatePoint(new Point(0, 0), dialog).X;
            double logoX = logo.TranslatePoint(new Point(0, 0), dialog).X;
            Assert.True(markX < logoX, $"ürün markası firma bloğunun solunda değil ({markX} ≥ {logoX})");
        }
    }

    // ---------------------------------------------------------------- sekmeler

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> ESKİ İDDİA (design v1.13.0): üç sekme <c>Shortcuts |
    /// Environment | Third-party</c> sırasındaydı ve açılış Shortcuts'taydı. v1.19.0 Third-party'yi KALDIRDI
    /// (placeholder bir lisans listesiydi) ve sırayı mantıksal kıldı: <c>About | Environment | Shortcuts</c>;
    /// ⓘ ve F1 her açılışta <b>About</b>'ta başlar. Önceki açılışta başka sekme seçilmiş olsa da.
    /// </summary>
    [StaFact]
    public void The_tabs_are_about_environment_shortcuts_and_about_is_selected_on_every_open()
    {
        var (dialog, run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var tabs = Tabs(dialog);
            Assert.Equal(["About", "Environment", "Shortcuts"], tabs.Select(t => (string)t.Content));
            Assert.True(tabs[AboutTab].IsChecked);
            Assert.All(tabs.Skip(1), t => Assert.False(t.IsChecked));

            Select(dialog, ShortcutsTab);
            dialog.CloseDialog();
            dialog.Open(run, true, () => Task.FromResult(AboutDialogHost.FakeMsBuild));
            Assert.True(Tabs(dialog)[AboutTab].IsChecked);
        }
    }

    /// <summary>[design v1.19.0 §2.10] Third-party YOK: ne sekmesi ne de tipi (atıf tablosu ve satır modeli
    /// silindi; <c>Assets/GEIST-LICENSE.txt</c> dağıtımda kalır — FontAssetTests/PublishLayoutTests).</summary>
    /// <summary>[kopya YASAK] Satır ölçüleri (27px satır, 124px etiket, 18px aralık) AboutDialog.xaml'de TEK kez
    /// tanımlanır: kimlik ve ortam satırları aynı satır şablonunu, Shortcuts satırı aynı satır stilini kullanır.
    /// ESKİ HÂL: <c>IdentityRow</c> ve <c>EnvironmentRow</c> şablonları etiket hücresini ve ölçüleri ikişer kez
    /// taşıyordu, 27 Shortcuts satırında üçüncü kez yazılıydı (review bulgusu).</summary>
    [Fact]
    public void About_row_measures_are_defined_once_in_the_dialog_xaml()
    {
        string xaml = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoPaths.AppSrcRoot, "Views", "AboutDialog.xaml"));
        foreach (string measure in new[] { "\"27\"", "Width=\"124\"", "\"18,0,0,0\"" })
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(xaml, System.Text.RegularExpressions.Regex.Escape(measure)).Count);
        Assert.DoesNotContain("x:Key=\"IdentityRow\"", xaml);
    }

    [StaFact]
    public void There_is_no_third_party_tab_and_no_notices_type()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.DoesNotContain(Tabs(dialog), t => (string)t.Content == "Third-party");
            var assembly = typeof(AppIdentity).Assembly;
            Assert.Null(assembly.GetType("BuildOrchestrator.App.Services.ThirdPartyNotices"));
            Assert.Null(assembly.GetType("BuildOrchestrator.App.Services.ThirdPartyComponent"));
            Assert.Null(assembly.GetType("BuildOrchestrator.App.Views.NoticeRow"));
        }
    }

    /// <summary>[design v1.19.0 §2.10] Sekme bandı: padding 6/18/14, altında TAM GENİŞLİK 1px <c>border-subtle</c>
    /// hairline; segment DS <c>md</c> boyunda (dış yükseklik 26 — action bar'ın <c>sm</c> segmenti 24 kalır,
    /// DsControlTemplateTests).</summary>
    [StaFact]
    public void The_tab_band_is_padded_over_a_full_width_hairline_with_a_26px_segment()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var band = dialog.TabBand;
            Assert.Equal(new Thickness(18, 6, 18, 14), band.Padding);
            Assert.Equal(new Thickness(0, 0, 0, 1), band.BorderThickness);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(band.BorderBrush));

            var frame = dialog.Frame;
            Assert.Equal(frame.ActualWidth - frame.BorderThickness.Left - frame.BorderThickness.Right,
                band.ActualWidth, precision: 3);

            var segment = DsResources.Descendants(band).OfType<ItemsControl>().Single();
            Assert.Equal(26.0, segment.ActualHeight);
        }
    }

    /// <summary>Her an TAM BİR panel görünür — "sekme değişince boy değişmez" iddiasının ÖN KOŞULU.</summary>
    [StaFact]
    public void Exactly_one_pane_is_visible_at_a_time()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var panes = dialog.Body.Children.Cast<FrameworkElement>().ToList();
            Assert.Equal(3, panes.Count);

            for (int i = 0; i < Tabs(dialog).Count; i++)
            {
                Select(dialog, i);
                Assert.Equal(1, panes.Count(p => p.Visibility == Visibility.Visible));
            }
        }
    }

    /// <summary>
    /// [design v1.19.0 §2.10] Gövde SABİT 284px (bir MinHeight değil), üç sekmede de aynı; içerik kendi içinde
    /// kayar ve dialog sekme değişince zıplamaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ SAYI 236px idi (660px'lik düzende en uzun sekmeyi —
    /// 10 satırlık düz Environment listesini — sığdıran değer). v1.19.0 Environment'ı iki caps gruba ayırdı ve
    /// About sekmesini ekledi; tasarım gövdeyi 284px'e sabitler.</para></summary>
    [StaFact]
    public void The_body_is_fixed_at_284px_on_every_tab_and_the_dialog_never_resizes()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(284.0, dialog.Body.Height);
            var heights = new List<double>();
            for (int i = 0; i < Tabs(dialog).Count; i++)
            {
                Select(dialog, i);
                Assert.Equal(284.0, dialog.Body.ActualHeight);
                heights.Add(dialog.Frame.ActualHeight);
            }
            Assert.All(heights, h => Assert.True(h > 0, "diyalog hiç yerleşmedi"));
            Assert.Single(heights.Distinct());
        }
    }

    // ---------------------------------------------------------------- About sekmesi

    /// <summary>[design v1.19.0 §2.10] Tanım paragrafı: metin <see cref="AppIdentity.Overview"/>'dan, 13px
    /// <c>text-secondary</c>, satır yüksekliği 13 × 1.62 = 21.06, en çok 470px, sarılır.</summary>
    [StaFact]
    public void The_about_tab_opens_with_the_overview_paragraph_at_its_reading_measure()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var paragraph = dialog.OverviewText;
            Assert.True(paragraph.IsVisible);
            Assert.Equal(AppIdentity.Overview, paragraph.Text);
            Assert.Equal(13.0, paragraph.FontSize);
            Assert.Equal(21.06, paragraph.LineHeight, precision: 6);
            Assert.Equal(470.0, paragraph.MaxWidth);
            Assert.Equal(TextWrapping.Wrap, paragraph.TextWrapping);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextSecondary"), DsResources.ColorOf(paragraph.Foreground));
        }
    }

    /// <summary>[design v1.19.0 §2.10] Kimlik satırları Version · Engine · Copyright — değerler GERÇEK kaynaktan:
    /// uygulama sürümü ve telif assembly'den (<see cref="AppIdentity"/>), motor sürümü motorun KENDİ bildirdiği
    /// değerden. Satır: etiket 124px, 18px aralık, değer mono.</summary>
    [StaFact]
    public void The_about_tab_rows_read_version_engine_and_copyright_from_their_sources()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            Assert.Equal(["Version", "Engine", "Copyright"], dialog.Diagnostics!.Identity.Select(l => l.Label));
            Assert.Equal([AppIdentity.Version, "9.9.9+test", AppIdentity.Copyright],
                dialog.Diagnostics!.Identity.Select(l => l.Value));

            foreach (var line in dialog.Diagnostics!.Identity)
            {
                var label = VisibleText(dialog, line.Label);
                var value = (TextBlock)DsResources.Descendants(dialog).OfType<TextBlock>()
                    .Single(t => t.IsVisible && t.Text == line.Value && !ReferenceEquals(t, dialog.VersionChip.Child));
                Assert.Equal(124.0, label.ActualWidth);
                Assert.Equal(142.0, value.TranslatePoint(new Point(0, 0), label).X, precision: 3);
                Assert.Equal(AppFonts.Mono, value.FontFamily);
                Assert.True(((FrameworkElement)VisualTreeHelper.GetParent(label)).ActualHeight >= 27.0);
            }
        }
    }

    /// <summary>Motor henüz doğmamışken Engine satırı KAYBOLMAZ, <c>not started</c> der.</summary>
    [StaFact]
    public void The_engine_row_reads_not_started_before_the_engine_reports()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(DiagnosticsReport.NotStarted, dialog.Diagnostics!.Engine.Value);
            Assert.Contains(DiagnosticsReport.NotStarted, VisibleTexts(dialog));
        }
    }

    /// <summary>[design v1.19.0 §2.10] Satırların altında ghost sm <c>What's new in {sürüm}</c> butonu (sol margin
    /// −10: etiket satır etiketleriyle hizalanır). Tıklanınca About KAPANIR ve istek bildirilir — What's new'i
    /// açan ve görüldü işaretini yazan MainWindow'dur (AboutWiringTests).</summary>
    [StaFact]
    public void The_whats_new_button_names_the_version_closes_about_and_raises_the_request()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var button = dialog.WhatsNewButton;
            Assert.True(button.IsVisible);
            Assert.Equal(-10.0, button.Margin.Left);
            string label = ReleaseNotes.WhatsNewInLabel(AppIdentity.Version);
            Assert.Contains(DsResources.Descendants(button).OfType<TextBlock>(), t => t.Text == label);
            Assert.Equal(label, System.Windows.Automation.AutomationProperties.GetName(button));

            int requests = 0;
            dialog.WhatsNewRequested += () => requests++;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(1, requests);
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        }
    }

    // ---------------------------------------------------------------- Environment sekmesi

    /// <summary>
    /// [design v1.19.0 §2.10] İki caps grup: <b>RUNTIME</b> (Engine PID · .NET runtime · OS) ve <b>PATHS</b>
    /// (MSBuild · Repository root · State file · Logs); her satırın etiketi ve değeri görünür.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ İDDİA: sekme tek düz listeydi ve <c>App version</c> /
    /// <c>Engine version</c> satırlarıyla başlardı. Sürümler About sekmesine taşındı (tekrar yok) — yoklukları
    /// da assert edilir.</para></summary>
    [StaFact]
    public void The_environment_tab_draws_runtime_and_paths_groups_without_version_rows()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            Select(dialog, EnvironmentTab);

            var runtimeTitle = VisibleText(dialog, DiagnosticsReport.RuntimeTitle);
            var pathsTitle = VisibleText(dialog, DiagnosticsReport.PathsTitle);
            Assert.True(TopIn(runtimeTitle, dialog) < TopIn(pathsTitle, dialog));

            var texts = VisibleTexts(dialog);
            foreach (var line in dialog.Diagnostics!.Runtime)
            {
                Assert.Contains(line.Value, texts);
                double y = TopIn(VisibleText(dialog, line.Label), dialog);
                Assert.True(y > TopIn(runtimeTitle, dialog) && y < TopIn(pathsTitle, dialog), $"{line.Label} RUNTIME grubunda değil");
            }
            foreach (var line in dialog.Diagnostics!.Paths)
            {
                Assert.Contains(line.Value, texts);
                Assert.True(TopIn(VisibleText(dialog, line.Label), dialog) > TopIn(pathsTitle, dialog), $"{line.Label} PATHS grubunda değil");
            }
            // Yollar YENİDEN YAZILMAZ — üretimin kendi static'lerinden gelir.
            Assert.Contains(dialog.Diagnostics!.Paths, l => l.Value == JsonUiStateStore.DefaultPath);

            Assert.DoesNotContain("App version", texts);
            Assert.DoesNotContain("Engine version", texts);
            // Sürüm başlıktaki çipte görünür; gövdede (Environment paneli) GEÇMEZ.
            Assert.DoesNotContain(AppIdentity.Version, VisibleTexts(dialog.Body));
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.13.1 §2.10]</b> ESKİ İDDİA: değer hücresi
    /// <c>TextTrimming="CharacterEllipsis"</c> ile kırpılır, tam metin <c>ToolTip</c>'te dururdu. YENİ kural:
    /// hiçbir değer KIRPILMAZ ve hiçbirinde tooltip YOKTUR; uzun bir yol bunun yerine yatay kayar.
    /// </summary>
    [StaFact]
    public void Environment_values_are_not_truncated_and_carry_no_tooltip()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.RootPath = OverflowingRootPath);
        using (scope)
        {
            Select(dialog, EnvironmentTab);
            var lines = EnvironmentLines(dialog);

            var valueCells = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Where(t => t.IsVisible && lines.Any(l => l.Value == t.Text))
                .ToList();

            Assert.Equal(lines.Count, valueCells.Count); // her satırın değeri BULUNDU
            Assert.All(valueCells, t => Assert.Equal(TextTrimming.None, t.TextTrimming));
            Assert.All(valueCells, t => Assert.Null(t.ToolTip));
        }
    }

    /// <summary>Taşan hücrede tekerlek yatay ofseti ARTIRIR ve olayı YUTAR.</summary>
    [StaFact]
    public void The_wheel_scrolls_an_overflowing_environment_value_sideways()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.RootPath = OverflowingRootPath);
        using (scope)
        {
            Select(dialog, EnvironmentTab);
            var scroller = EnvironmentValueScroller(dialog, "Repository root");
            Assert.True(scroller.ScrollableWidth > 0); // ön-koşul: gerçekten taşıyor
            Assert.Equal(0.0, scroller.HorizontalOffset);

            var args = RaiseWheel(dialog, scroller, -Mouse.MouseWheelDeltaForOneLine);

            Assert.True(scroller.HorizontalOffset > 0);
            Assert.True(args.Handled); // taşan hücre olayı YUTAR — dışarıdaki dikey scroll'a sızmaz
        }
    }

    /// <summary>
    /// Taşmayan hücrede tekerlek yatay ofsete DOKUNMAZ ve sekmenin kendi dikey kaydırması ÇALIŞIR.
    ///
    /// <para><b>[DEĞİŞEN TEST — ölçüm]</b> ESKİ İDDİA: preview fazında raise edilen bir tekerlek olayının
    /// <c>Handled</c>'ının false kalması bu davranışı pinlerdi. Ölçüm bunun yanlış olduğunu gösterdi:
    /// <see cref="ScrollViewer"/> olayı BALONCUK fazındaki class handler'ında yutar. Test gerçek soruyu soruyor:
    /// dış <see cref="ScrollViewer"/>'ın <c>VerticalOffset</c>'i ARTIYOR MU. Dış panelin kaydırılabilir olması
    /// <c>MaxHeight</c> ile KURULUR.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> Hücre eskiden <c>App version</c> satırıydı; o satır
    /// Environment'tan çıktı — kısa fixture kökünü taşıyan <c>Repository root</c> kullanılır.</para>
    /// </summary>
    [StaFact]
    public void The_wheel_over_a_non_overflowing_environment_value_still_scrolls_the_tab()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Select(dialog, EnvironmentTab);
            var scroller = EnvironmentValueScroller(dialog, "Repository root");
            var tab = EnvironmentTabScroller(scroller);
            tab.MaxHeight = 60;
            dialog.UpdateLayout();

            Assert.Equal(0.0, scroller.ScrollableWidth);   // ön-koşul: hücre taşmıyor
            Assert.True(tab.ScrollableHeight > 0);         // ön-koşul: sekme gerçekten kaydırılabilir
            Assert.Equal(0.0, tab.VerticalOffset);

            RaiseWheel(dialog, scroller, -Mouse.MouseWheelDeltaForOneLine);

            Assert.Equal(0.0, scroller.HorizontalOffset);  // hücre yatayda oynamadı
            Assert.True(tab.VerticalOffset > 0, "tekerlek sekmenin dikey kaydırmasına HİÇ ulaşmadı");
        }
    }

    /// <summary>MSBuild çözümü ASYNC'tir: Environment sekmesi açılana kadar HİÇ tetiklenmez (About'u açmak bir
    /// child process başlatmamalı) ve sonuç gelene kadar satır "resolving…" der. Sonuç bir kez çözülür.</summary>
    [StaFact]
    public void Msbuild_is_resolved_lazily_when_the_environment_tab_is_first_opened()
    {
        var gate = new TaskCompletionSource<string>();
        int calls = 0;
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(
            resolveMsBuild: () => { calls++; return gate.Task; });
        using (scope)
        {
            Assert.Equal(0, calls); // açılışta HİÇ çağrılmadı
            Assert.Contains(dialog.Diagnostics!.Paths, l => l.Value == DiagnosticsReport.Resolving);

            Select(dialog, EnvironmentTab);
            Assert.Equal(1, calls);

            gate.SetResult(AboutDialogHost.FakeMsBuild);
            DispatcherPump.PumpUntil(
                () => dialog.Diagnostics!.Paths.Any(l => l.Value == AboutDialogHost.FakeMsBuild), PumpTimeout);
            Assert.Contains(dialog.Diagnostics!.Paths, l => l.Value == AboutDialogHost.FakeMsBuild);

            // Sekmeye geri dönmek yeniden çözmez.
            Select(dialog, AboutTab);
            Select(dialog, EnvironmentTab);
            Assert.Equal(1, calls);
        }
    }

    // ---------------------------------------------------------------- Shortcuts sekmesi

    /// <summary>
    /// [design v1.19.0 §2.10] İki caps grup — <b>BUILD</b> ve <b>APPLICATION</b>; grup bilgisi ve açıklama
    /// metinleri <see cref="ShortcutCatalog"/>'dan (birebir), her satırın jestleri <c>Ds.Kbd</c> rozeti.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ İDDİA: sekme gruplanmamış tek bir listeydi ve
    /// dialogun İLK sekmesiydi (açılışta görünürdü). Artık üçüncü sekmedir ve iki gruba ayrılır.</para></summary>
    [StaFact]
    public void The_shortcuts_tab_groups_catalog_entries_under_build_and_application()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Select(dialog, ShortcutsTab);

            var badges = DsResources.Descendants(dialog).OfType<ContentControl>()
                .Where(c => c.IsVisible).Select(c => c.Content as string).Where(c => c is not null).ToList();
            var groupTops = ShortcutCatalog.GroupOrder
                .Select(g => TopIn(VisibleText(dialog, ShortcutCatalog.GroupTitle(g)), dialog)).ToList();
            Assert.True(groupTops[0] < groupTops[1], "BUILD grubu APPLICATION'dan önce değil");

            foreach (var entry in ShortcutCatalog.All)
            {
                double y = TopIn(VisibleText(dialog, entry.Description), dialog);
                int group = ShortcutCatalog.GroupOrder.ToList().IndexOf(entry.Group);
                Assert.True(y > groupTops[group], $"{entry.Id} kendi grup başlığının altında değil");
                if (group + 1 < groupTops.Count)
                    Assert.True(y < groupTops[group + 1], $"{entry.Id} sonraki grubun içine taşmış");
                foreach (string gesture in entry.Gestures) Assert.Contains(gesture, badges);
            }
        }
    }

    /// <summary>Global kısayol kaydı çakışma yüzünden düştüğünde bu GÖRÜNÜR olur — README'nin "sessizce devre
    /// dışı" davranışını kullanıcının anlamasının başka bir yolu yok.</summary>
    [StaFact]
    public void An_unregistered_global_hotkey_is_marked_unavailable()
    {
        var (registered, _, scope1) = AboutDialogHost.OpenRealized(hotkeyRegistered: true);
        using (scope1)
        {
            Select(registered, ShortcutsTab);
            Assert.DoesNotContain(VisibleTexts(registered), t => t.Contains("unavailable", StringComparison.Ordinal));
        }

        var (disabled, _, scope2) = AboutDialogHost.OpenRealized(hotkeyRegistered: false);
        using (scope2)
        {
            Select(disabled, ShortcutsTab);
            Assert.Contains(VisibleTexts(disabled), t => t.Contains("unavailable", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------- footer / copy diagnostics

    /// <summary>[design v1.19.0 §2.10] Pano metni AYNI modelden (<see cref="DiagnosticsReport.ToText"/>):
    /// <c>{ürün} {sürüm}</c> başlığı, ardından Engine + Runtime + Paths satırları.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ İDDİA: başlığın altına Environment sekmesinin TÜM
    /// satırları (App version, Engine version dahil) gelirdi. Sürüm başlıkta zaten var; motor sürümü tek bir
    /// <c>Engine</c> satırıdır.</para></summary>
    [StaFact]
    public void Copy_diagnostics_writes_the_report_from_the_same_model_and_shows_feedback()
    {
        string? written = null;
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            dialog.ClipboardWriter = text => { written = text; return true; };
            dialog.CopyDiagnostics();

            Assert.NotNull(written);
            Assert.Equal(DiagnosticsReport.ToText(dialog.Diagnostics!), written);
            var rows = written!.Split(Environment.NewLine);
            Assert.Equal($"{AppIdentity.Product} {AppIdentity.Version}", rows[0]);
            Assert.StartsWith("Engine ", rows[1], StringComparison.Ordinal);
            Assert.EndsWith("9.9.9+test", rows[1], StringComparison.Ordinal);
            foreach (var line in EnvironmentLines(dialog))
                Assert.Contains(line.Value, written, StringComparison.Ordinal);
            Assert.True(dialog.IsShowingCopied);
        }
    }

    /// <summary>[design v1.19.0 §2.10] Footer: solda ghost sm <c>Copy diagnostics</c> — sol margin −10 ile etiket
    /// 18px gutter'a hizalanır; sağda secondary <c>Close</c>.</summary>
    [StaFact]
    public void The_copy_button_is_pulled_left_so_its_label_sits_on_the_gutter()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
            Assert.Equal(-10.0, dialog.CopyButton.Margin.Left);
    }

    /// <summary>[design-v1.2.1 §2.10] Kopyalandı geri bildirimi GÖRSELDİR: ikon copy → ✓ döner ve buton
    /// başarı rengine geçer.</summary>
    [StaFact]
    public void Copy_feedback_swaps_the_icon_to_a_check_and_turns_green()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var host = DsResources.NewHost();
            Assert.False(dialog.IsShowingCheckIcon);

            dialog.ClipboardWriter = _ => true;
            dialog.CopyDiagnostics();

            Assert.True(dialog.IsShowingCheckIcon);
            Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSuccessText"),
                DsResources.ColorOf(dialog.CopyButtonForeground));
        }
    }

    /// <summary>Pano kilitliyse (kalıcı CLIPBRD_E_CANT_OPEN) UI çökmez ve "kopyalandı" YALANI söylemez.</summary>
    [StaFact]
    public void A_failed_clipboard_write_shows_no_copied_feedback()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.ClipboardWriter = _ => false;
            dialog.CopyDiagnostics();
            Assert.False(dialog.IsShowingCopied);
        }
    }

    // ---------------------------------------------------------------- saf karar (ofset aritmetiği)

    /// <summary>WPF'in dikey kuralıyla AYNI çevrilir: pozitif <c>Delta</c> YUKARI'dır (dikeyde ofset azalır);
    /// prototipin <c>scrollLeft += deltaY</c> hissiyle (tekerlek AŞAĞI ⇒ yol SAĞA) aynı fiziksel yöne
    /// ulaşmak için "aşağı" (negatif <c>Delta</c>) burada yatay ofseti, notch'un TAM büyüklüğü kadar,
    /// ARTIRIR — <see cref="HorizontalWheelScroll"/>'un aksine bir satır/adım ölçeklemesi YOKTUR, prototip
    /// de kırpmaz (<c>scrollLeft += deltaY</c> düz toplamdır).</summary>
    [Fact]
    public void A_downward_notch_increases_the_horizontal_offset_by_its_full_magnitude()
    {
        Assert.Equal(120.0, BuildOrchestrator.App.Views.AboutDialog.EnvironmentValueWheelOffset(
            0, -Mouse.MouseWheelDeltaForOneLine, 1000));
    }

    [Fact]
    public void An_upward_notch_decreases_the_horizontal_offset()
    {
        Assert.Equal(880.0, BuildOrchestrator.App.Views.AboutDialog.EnvironmentValueWheelOffset(
            1000, Mouse.MouseWheelDeltaForOneLine, 1000));
    }

    [Fact]
    public void The_offset_never_goes_negative()
    {
        Assert.Equal(0.0, BuildOrchestrator.App.Views.AboutDialog.EnvironmentValueWheelOffset(
            0, Mouse.MouseWheelDeltaForOneLine, 1000));
    }

    [Fact]
    public void The_offset_is_clamped_to_the_scrollable_width()
    {
        Assert.Equal(1000.0, BuildOrchestrator.App.Views.AboutDialog.EnvironmentValueWheelOffset(
            950, -Mouse.MouseWheelDeltaForOneLine, 1000));
    }

    [Fact]
    public void A_non_overflowing_cell_always_clamps_to_zero()
    {
        Assert.Equal(0.0, BuildOrchestrator.App.Views.AboutDialog.EnvironmentValueWheelOffset(
            0, -Mouse.MouseWheelDeltaForOneLine, 0));
    }
}
