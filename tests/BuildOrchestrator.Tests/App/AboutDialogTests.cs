using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About modali. Kabuk Settings ile AYNI DESENDİR (scrim + Ds.Dialog + odak tuzağı + Esc) ama genişlik
/// BİLEREK farklı (660px — design v1.13.1 §2.10: üç dialog artık bugünkü içeriğine değil büyüme yönüne göre
/// ölçülüyor); farkı ayrıca sekmeli gövdesidir. Headless süit XAML runtime çözümlemesini görmez — bu yüzden
/// realize ZORUNLU (CLAUDE.md).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AboutDialogTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    // Gerçek makinedeki LOCALAPPDATA yollarının uzunluğuna bel bağlamaz: hangi font gerçekten çözülürse
    // çözülsün (headless testte AppFonts.Mono'nun pack:// kaynağı yoktur, WPF bir yedeğe düşer) bu uzunluk
    // Environment hücresinin ~450px'lik görünür genişliğini KESİNLİKLE taşırır.
    private static readonly string OverflowingRootPath = @"D:\" + new string('a', 200) + @"\repo";

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
    /// <b>[DEĞİŞEN KURAL — design v1.13.1 §2.10]</b> ESKİ İDDİA: About, Settings'le AYNI 620px kalıbını
    /// paylaşıyordu (üç dialog da 620px'ti). v1.13.1 bunu ayırdı: her dialog artık bugünkü içeriğine değil
    /// BÜYÜME YÖNÜNE göre ölçülüyor. About statik bir referanstır (sürüm, kısayollar, environment,
    /// third-party) ve zamanla yalnız third-party listesi uzar → dikeyde büyür — üçünün en darı olması bu
    /// yüzden doğrudur: en az iş yapan dialog odur. YENİ genişlik 660px: en uzun yol (85 karakterlik MSBuild
    /// yolu, 12px mono'da ~610px) tek satıra genişlik büyüyünce bile hâlâ sığmıyor, o yüzden genişliğin
    /// yanına Environment'taki yatay kaydırma kondu (aşağıdaki <c>Environment_*</c>/<c>The_wheel_*</c>
    /// testleri).
    /// </summary>
    [StaFact]
    public void The_dialog_realizes_and_is_six_hundred_sixty_pixels_wide()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.Equal(660.0, Shell(dialog).Width);
            Assert.Equal(660.0, Shell(dialog).ActualWidth); // realize zorunlu — literal okumak yetmez
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

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.13.0 §2.1/§2.10/§2.11, D4/T9]</b> ESKİ İDDİA (design v1.9.0): sekme sayısı
    /// ÜÇTEN DÖRDE çıkmıştı — sürüm notları ayrı bir pencere ya da açılış pop-up'ı değil, About'un dördüncü
    /// sekmesi olarak eklenmişti. v1.13.0 bunu GERİ ALDI: What's new kendi diyalogu (<see cref="BuildOrchestrator.App.Views.NotesDialog"/>)
    /// ve kendi title bar butonu (sparkle) oldu — About DÖRTTEN ÜÇE döndü: <c>Shortcuts | Environment |
    /// Third-party</c>. Liste kurma kodu KOPYALANMADI, <c>NotesDialog.xaml.cs</c>'e TAŞINDI (bkz.
    /// <c>NotesDialogTests</c>).
    /// </summary>
    [StaFact]
    public void It_has_three_tabs_and_the_first_one_is_selected()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var tabs = Tabs(dialog);
            Assert.Equal(3, tabs.Count);
            Assert.Equal(["Shortcuts", "Environment", "Third-party"], tabs.Select(t => (string)t.Content));
            Assert.True(tabs[0].IsChecked);
            Assert.All(tabs.Skip(1), t => Assert.False(t.IsChecked));
        }
    }

    /// <summary>Her an TAM BİR panel görünür. Bu, "sekme değişince boy değişmez" iddiasının ÖN KOŞULUdur:
    /// üç panel birden görünür kalsaydı boy zaten sabit olurdu ve o test hiçbir şeyi ayırt etmezdi.
    /// <b>[DEĞİŞEN KURAL — design v1.13.0]</b> panel sayısı DÖRTTEN ÜÇE döndü (What's new NotesDialog'a
    /// taşındı).</summary>
    [StaFact]
    public void Exactly_one_pane_is_visible_at_a_time()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var panes = DsResources.Descendants(dialog).OfType<ScrollViewer>().ToList();
            Assert.Equal(3, panes.Count); // [v1.13.0] dördüncü panel (What's new) kalktı

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

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.13.1 §2.10]</b> ESKİ İDDİA: değer hücresi
    /// <c>TextTrimming="CharacterEllipsis"</c> ile kırpılır, tam metin <c>ToolTip</c>'te dururdu.
    /// "Ellipsis + title ipucu" olarak denendi, İSTENMEDİ — tam metni okumanın zaten bir yolu var
    /// (footer'daki Copy diagnostics), kırpma+tooltip fazladan bir etkileşim katmanıydı. YENİ kural: hiçbir
    /// değer KIRPILMAZ ve hiçbirinde tooltip YOKTUR; uzun bir yol bunun yerine yatay kayar (aşağıdaki
    /// <c>The_wheel_*</c> testleri).
    /// </summary>
    [StaFact]
    public void Environment_values_are_not_truncated_and_carry_no_tooltip()
    {
        // Kısa bir değerde TextTrimming=None zaten anlamsız olurdu (kırpma etkinleşmez ki) — taşan bir kökle
        // iddia GERÇEK bir senaryoyu kapsar: kullanıcı bu satırı görünce yol gerçekten kırpılmıyor.
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.RootPath = OverflowingRootPath);
        using (scope)
        {
            Select(dialog, 1);

            var valueCells = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Where(t => dialog.DiagnosticsLines.Any(l => l.Value == t.Text))
                .ToList();

            Assert.Equal(dialog.DiagnosticsLines.Count, valueCells.Count); // her satırın değeri BULUNDU
            Assert.All(valueCells, t => Assert.Equal(TextTrimming.None, t.TextTrimming));
            Assert.All(valueCells, t => Assert.Null(t.ToolTip));
        }
    }

    /// <summary>Taşan hücrede tekerlek yatay ofseti ARTIRIR ve olayı YUTAR — brief T10 testler listesi.
    /// Taşma <see cref="OverflowingRootPath"/> ile GARANTİ edilir; gerçek makinedeki LOCALAPPDATA yollarının
    /// o an ne kadar uzun olduğuna bel bağlamaz.</summary>
    [StaFact]
    public void The_wheel_scrolls_an_overflowing_environment_value_sideways()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.RootPath = OverflowingRootPath);
        using (scope)
        {
            Select(dialog, 1);
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
    /// <see cref="ScrollViewer"/> olayı BALONCUK fazındaki class handler'ında (<c>OnMouseWheel</c>) yutar ve
    /// bunu dikeyde kaydıracak bir şeyi olup olmadığına BAKMADAN yapar — yani preview'daki <c>Handled</c>
    /// false olsa bile dış panel HİÇ kaymıyordu. Test artık gerçek soruyu soruyor: dış
    /// <see cref="ScrollViewer"/>'ın <c>VerticalOffset</c>'i ARTIYOR MU.</para>
    ///
    /// <para>Dış panelin gerçekten kaydırılabilir olması KURULUR (<c>MaxHeight</c>): Environment sekmesi
    /// bugünkü tanı satırlarıyla 236px'lik kutusunu doldurmuyor, oysa kusur listenin taştığı ilk anda
    /// görünür olur — <see cref="OverflowingRootPath"/>'in yatay taşma için yaptığının dikey eşi.</para>
    /// </summary>
    [StaFact]
    public void The_wheel_over_a_non_overflowing_environment_value_still_scrolls_the_tab()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Select(dialog, 1);
            var scroller = EnvironmentValueScroller(dialog, "App version");
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

    /// <summary>[design v1.9.0 §2.10] Gövdenin yüksekliği SABİTTİR (bir MinHeight değil) — hangi sekme uzarsa
    /// uzasın, dialog büyümez, panel kendi içinde kayar.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.0 §2.10/§2.11, D4/T9]</b> ESKİ İDDİA (design v1.9.0): bu test
    /// <b>What's new</b> sekmesini seçip ("en uzun panel") gövdenin BÜYÜMEDİĞİNİ ve o panelin GERÇEKTEN taştığını
    /// (<c>ExtentHeight &gt;= ViewportHeight</c>) ölçüyordu — What's new sürüm biriktikçe uzayan TEK sekmeydi.
    /// v1.13.0 What's new'i About'tan çıkarıp <see cref="BuildOrchestrator.App.Views.NotesDialog"/>'a taşıdı (bkz.
    /// <c>NotesDialogTests.The_body_height_is_fixed_at_400px</c> — taşan-panel kanıtı ORADA yaşıyor, kendi
    /// 400px sabit gövdesiyle). Geriye kalan üç sekmenin (Shortcuts/Environment/Third-party) HİÇBİRİ bugünkü
    /// içerikle 236px'i doldurmuyor, yani "gerçekten taşıyor" iddiası burada artık KANITLANAMAZ — sahte bir
    /// taşma iddia etmek yerine bu test YAPISAL kalır: <c>Height==236.0</c> araması (About'un kendi 660px
    /// genişliği gibi) bir <c>MinHeight</c> DEĞİL gerçek bir <c>Height</c> olduğunu doğrular — <c>Grid.Height</c>
    /// okunur, <c>Grid.MinHeight</c> DEĞİL; MinHeight olsaydı <c>Height</c> NaN kalır ve <c>Single()</c>
    /// eşleşmezdi. "Sekme değişince boy değişmez" davranışı zaten kardeş test
    /// <see cref="Switching_tabs_never_resizes_the_dialog"/>'ta ayrıca ölçülüyor.</para></summary>
    [StaFact]
    public void The_body_uses_a_fixed_height_not_a_minimum_height()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var body = DsResources.Descendants(dialog).OfType<Grid>().Single(g => g.Height == 236.0);
            Assert.True(body.ActualHeight > 0, "gövde hiç yerleşmedi");
            Assert.Equal(236.0, body.ActualHeight);
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
