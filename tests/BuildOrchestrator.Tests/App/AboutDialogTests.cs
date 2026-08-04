using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About modali. Kabuk Settings ile AYNI (scrim + 620px Ds.Dialog + odak tuzağı + Esc); farkı sekmeli
/// gövdesidir. Headless süit XAML runtime çözümlemesini görmez — bu yüzden realize ZORUNLU (CLAUDE.md).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AboutDialogTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    private static Border Shell(BuildOrchestrator.App.Views.AboutDialog dialog) =>
        (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);

    private static IReadOnlyList<RadioButton> Tabs(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<RadioButton>()];

    private static List<string> VisibleTexts(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text)];

    private static void Select(BuildOrchestrator.App.Views.AboutDialog dialog, int index)
    {
        Tabs(dialog)[index].IsChecked = true;
        dialog.UpdateLayout();
    }

    // ---------------------------------------------------------------- kabuk

    [StaFact]
    public void The_dialog_realizes_and_is_six_hundred_twenty_pixels_wide()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.Equal(620.0, Shell(dialog).Width);
            Assert.Equal(620.0, Shell(dialog).ActualWidth); // realize zorunlu — literal okumak yetmez
        }
    }

    /// <summary>Yapısal kanıt: scrim bir Cycle klavye-gezinme kapsayıcısı ve bir odak kapsamı. Odak tuzağı
    /// XAML dosyası BAŞINA kurulur — Settings'te düzeltilen kusur burada kendiliğinden düzelmiş sayılmaz.</summary>
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

    /// <summary>Giriş GERÇEKTEN kuruluyor: animasyon açıkken kabuğa bir YÜKSELME transform'u takılır
    /// (ölçek YOK — diyalog girişi yalnız fade + 6px). Motion sinyali headless'ta varsayılan olarak KAPALI,
    /// bu yüzden açıkça açılır (PopoverTests deseni).</summary>
    [StaFact]
    public void Opening_the_dialog_installs_the_entrance_transform_on_the_shell()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var (dialog, _run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
            Assert.IsType<TranslateTransform>(Shell(dialog).RenderTransform);
    }

    /// <summary>Reduced-motion: hiç animasyon KURULMAZ, diyalog son duruma snap eder (motion sözleşmesi).</summary>
    [StaFact]
    public void Reduced_motion_snaps_the_dialog_to_its_final_state()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = false }));
        var (dialog, _run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(1.0, Shell(dialog).Opacity);
            Assert.Equal(Transform.Identity, Shell(dialog).RenderTransform);
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

    // ---------------------------------------------------------------- sekmeler

    [StaFact]
    public void It_has_three_tabs_and_the_first_one_is_selected()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var tabs = Tabs(dialog);
            Assert.Equal(3, tabs.Count);
            Assert.True(tabs[0].IsChecked);
            Assert.All(tabs.Skip(1), t => Assert.False(t.IsChecked));
        }
    }

    /// <summary>Her an TAM BİR panel görünür. Bu, "sekme değişince boy değişmez" iddiasının ÖN KOŞULUdur:
    /// üç panel birden görünür kalsaydı boy zaten sabit olurdu ve o test hiçbir şeyi ayırt etmezdi.</summary>
    [StaFact]
    public void Exactly_one_pane_is_visible_at_a_time()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var panes = DsResources.Descendants(dialog).OfType<ScrollViewer>().ToList();
            Assert.Equal(3, panes.Count);

            for (int i = 0; i < Tabs(dialog).Count; i++)
            {
                Select(dialog, i);
                Assert.Equal(1, panes.Count(p => p.Visibility == Visibility.Visible));
            }
        }
    }

    /// <summary>Sekme değişince diyalog BOYU DEĞİŞMEZ — footer'ın yeri her sekmede aynı kalır. Test SAYIYI
    /// değil DAVRANIŞI pinler: üç sekmenin ölçülen yüksekliği birbirine eşit olmalı.
    /// <para>Ayırt ediciliği <see cref="Exactly_one_pane_is_visible_at_a_time"/>'a bağlıdır: paneller
    /// gerçekten tek tek göründüğü için içerik alanının SABİT yüksekliği olmasaydı boy sekmeye göre
    /// değişirdi.</para></summary>
    [StaFact]
    public void Switching_tabs_never_resizes_the_dialog()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var heights = new List<double>();
            for (int i = 0; i < Tabs(dialog).Count; i++)
            {
                Select(dialog, i);
                heights.Add(Shell(dialog).ActualHeight);
            }
            Assert.All(heights, h => Assert.True(h > 0, "diyalog hiç yerleşmedi"));
            Assert.Single(heights.Distinct());
        }
    }

    // ---------------------------------------------------------------- içerik

    /// <summary>
    /// [design-v1.2.1 §2.10] Kimlik bloğu: ürün markası 30px + ad + tagline + <b>TEK</b> mono sürüm satırı
    /// <c>{sürüm} · {telif}</c>.
    ///
    /// <para><b>ESKİ İDDİA:</b> sürüm satırı <c>{app} · engine {engine} · {telif}</c> idi. design-v1.1.0 bunu
    /// BİLEREK kaldırdı ("Eski `1.0.0+it5 · engine 1.0.0+it5` tekrarı kaldırıldı"): app/engine ayrımı
    /// Environment sekmesinde zaten var, başlıkta tekrarı gürültü. Test silinmedi, YENİ kuralı pinliyor —
    /// ve "engine" sözcüğünün hero'da GEÇMEDİĞİNİ ayrıca assert ediyor ki eski biçim geri sızmasın.</para></summary>
    [StaFact]
    public void The_hero_shows_one_version_line_without_repeating_the_engine()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            var texts = VisibleTexts(dialog);
            Assert.Contains(AppIdentity.Product, texts);
            Assert.Contains(AppIdentity.Tagline, texts);
            Assert.Contains($"{AppIdentity.Version} · {AppIdentity.Copyright}", texts);

            // Motor sürümü hero'da GEÇMEZ — yeri Environment sekmesidir.
            Assert.DoesNotContain(texts, t => t.Contains("engine", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(texts, t => t.Contains("9.9.9+test", StringComparison.Ordinal));
        }
    }

    /// <summary>[design-v1.2.1 §2.10] Başlıkta İKİ logo tek kompozisyonda: solda ürün markası 30px (tam renk),
    /// sağda <c>LICENSED TO</c> bloğu + firma logosu 13px %80. Ürün önde.</summary>
    [StaFact]
    public void The_hero_locks_a_30px_product_mark_against_a_licensed_to_company_block()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var mark = DsResources.Descendants(dialog).OfType<AppMark>().Single();
            var logo = DsResources.Descendants(dialog).OfType<BrandLogo>().Single();

            Assert.Equal(30.0, mark.Height);
            Assert.Equal(13.0, logo.Height);
            Assert.Equal(0.8, logo.Opacity, precision: 2);

            // Caps etiketi izli (tracked) çizilir — TrackedTextBlock bir TextBlock DEĞİL, GlyphRun çizen
            // bir FrameworkElement'tir (§14.2), bu yüzden metin ondan okunur.
            var licensedTo = DsResources.Descendants(dialog).OfType<TrackedTextBlock>()
                .Single(t => t.Text.Equals("LICENSED TO", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(licensedTo);

            // Ürün markası firma logosunun SOLUNDA.
            double markX = mark.TranslatePoint(new Point(0, 0), dialog).X;
            double logoX = logo.TranslatePoint(new Point(0, 0), dialog).X;
            Assert.True(markX < logoX, $"ürün markası firma bloğunun solunda değil ({markX} ≥ {logoX})");
        }
    }

    [StaFact]
    public void The_shortcuts_tab_lists_every_catalog_entry_with_its_key_badges()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var texts = VisibleTexts(dialog);
            var badges = DsResources.Descendants(dialog).OfType<ContentControl>()
                .Select(c => c.Content as string)
                .Where(c => c is not null)
                .ToList();

            foreach (var entry in ShortcutCatalog.All)
            {
                Assert.Contains(entry.Description, texts);
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
            Assert.DoesNotContain(
                DsResources.Descendants(registered).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text),
                t => t.Contains("unavailable", StringComparison.Ordinal));

        var (disabled, _, scope2) = AboutDialogHost.OpenRealized(hotkeyRegistered: false);
        using (scope2)
            Assert.Contains(
                DsResources.Descendants(disabled).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text),
                t => t.Contains("unavailable", StringComparison.Ordinal));
    }

    [StaFact]
    public void The_environment_tab_draws_every_diagnostics_line()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            Select(dialog, 1);

            var texts = VisibleTexts(dialog);
            Assert.NotEmpty(dialog.DiagnosticsLines);
            foreach (var line in dialog.DiagnosticsLines)
            {
                Assert.Contains(line.Label, texts);
                Assert.Contains(line.Value, texts);
            }
            // Yollar YENİDEN YAZILMAZ — üretimin kendi static'lerinden gelir.
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == JsonUiStateStore.DefaultPath);
        }
    }

    /// <summary>MSBuild çözümü ASYNC'tir: sekme açılana kadar HİÇ tetiklenmez (About'u açmak bir child process
    /// başlatmamalı) ve sonuç gelene kadar satır "resolving…" der. Sonuç bir kez çözülür, cache'lenir.</summary>
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
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == DiagnosticsReport.Resolving);

            Select(dialog, 1);
            Assert.Equal(1, calls);

            gate.SetResult(AboutDialogHost.FakeMsBuild);
            DispatcherPump.PumpUntil(
                () => dialog.DiagnosticsLines.Any(l => l.Value == AboutDialogHost.FakeMsBuild), PumpTimeout);
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == AboutDialogHost.FakeMsBuild);

            // Sekmeye geri dönmek yeniden çözmez.
            Select(dialog, 0);
            Select(dialog, 1);
            Assert.Equal(1, calls);
        }
    }

    [StaFact]
    public void The_third_party_tab_lists_every_component_with_its_licence()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Select(dialog, 2);

            var texts = VisibleTexts(dialog);
            foreach (var component in ThirdPartyNotices.All)
            {
                Assert.Contains(component.DisplayName, texts);
                Assert.Contains(component.License, texts);
            }
            Assert.Contains(ThirdPartyNotices.FontLicenseNote, texts);
        }
    }

    // ---------------------------------------------------------------- copy diagnostics

    /// <summary>[design-v1.2.1 §2.10] Panoya giden metin ürün ve sürümle BAŞLAR, ardından tüm Environment
    /// satırları gelir — destek talebine yapıştırıldığında neyin çıktısı olduğu ilk satırda okunur.</summary>
    [StaFact]
    public void Copy_diagnostics_writes_a_titled_report_and_shows_feedback()
    {
        string? written = null;
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.ClipboardWriter = text => { written = text; return true; };
            dialog.CopyDiagnostics();

            Assert.NotNull(written);
            Assert.StartsWith($"{AppIdentity.Product} {AppIdentity.Version}", written, StringComparison.Ordinal);
            foreach (var line in dialog.DiagnosticsLines)
                Assert.Contains(line.Value, written, StringComparison.Ordinal);
            Assert.True(dialog.IsShowingCopied);
        }
    }

    /// <summary>[design-v1.2.1 §2.10] Kopyalandı geri bildirimi GÖRSELDİR: ikon copy → ✓ döner ve buton
    /// başarı rengine geçer. Yalnız metin değişimi tasarımın istediği şey değil.</summary>
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

    /// <summary>[design-v1.2.1 §2.10] Third-party satırı üç kolondur: ad (esner) · mono sürüm 70px ·
    /// sağa yaslı mono lisans 92px. Üstünde tek satırlık açıklama.</summary>
    [StaFact]
    public void The_third_party_rows_use_the_designed_column_widths()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Select(dialog, 2);

            Assert.Contains("Bundled components and their licenses.", VisibleTexts(dialog));

            var versionCells = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Where(t => t.Width == 70.0).ToList();
            var licenceCells = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Where(t => t.Width == 92.0).ToList();

            Assert.Equal(ThirdPartyNotices.All.Count, versionCells.Count);
            Assert.Equal(ThirdPartyNotices.All.Count, licenceCells.Count);
            Assert.All(licenceCells, c => Assert.Equal(TextAlignment.Right, c.TextAlignment));
        }
    }

    /// <summary>[design-v1.2.1 §2.10] Gövde MIN-yükseklik 236'dır — sabit değil. Sekme değişince zıplamaz
    /// (o iddia ayrı testte), ama içerik büyürse alan da büyüyebilir.</summary>
    [StaFact]
    public void The_body_uses_a_minimum_height_not_a_fixed_one()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var body = DsResources.Descendants(dialog).OfType<Grid>()
                .Single(g => g.MinHeight == 236.0);
            Assert.True(double.IsNaN(body.Height), "gövde SABİT yükseklikte — tasarım min-height istiyor");
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
}
