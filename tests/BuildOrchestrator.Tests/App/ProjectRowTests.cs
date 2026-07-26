using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T53] design-v1 proje kartı (Views/ProjectRow, BuildApp.jsx:355-416): 7 slot + geometri. Kart GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) — bir setter'ı okumak değeri şablona ulaştırdığını kanıtlamaz.
/// Headless'ta <c>App.Motion</c> null → animasyonlar INSTANT (nihai değerler sleep/poll olmadan görünür, D8).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ProjectRowTests
{
    private static (ProjectRow row, Window window, Border host) Realize(ProjectRowViewModel vm)
    {
        var host = DsResources.NewHost();
        var row = new ProjectRow { DataContext = vm };
        var window = DsResources.Realize(host, row);
        return (row, window, host);
    }

    [StaFact]
    public void Row_is_thirtysix_pixels_with_a_two_pixel_status_stripe_that_becomes_three_when_selected()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var (row, window, _) = Realize(vm);

        Assert.Equal(LayoutMetrics.DefaultRowHeight, ((Border)row.Content).Height); // 36 (sticky aritmetiği varsayar)
        Assert.Equal(2.0, row.Stripe.Width);

        vm.IsSelected = true;
        row.UpdateLayout();
        Assert.Equal(3.0, row.Stripe.Width);

        vm.IsSelected = false;
        row.UpdateLayout();
        Assert.Equal(2.0, row.Stripe.Width);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Dep_issue_slot_is_fourteen_pixels_even_when_empty_so_columns_never_shift()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var (row, window, _) = Realize(vm);

        // Boşken: slot 14px durur, ikon gizli.
        Assert.Equal(14.0, row.DepSlot.Width);
        Assert.Equal(Visibility.Collapsed, row.DepIcon.Visibility);

        // Doluyken: slot HÂLÂ 14px (hiza kaymaz), ikon görünür.
        vm.DepIssues = new[] { "OSYS.Sales.Core" };
        row.UpdateLayout();
        Assert.Equal(14.0, row.DepSlot.Width);
        Assert.Equal(Visibility.Visible, row.DepIcon.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Will_build_dot_is_amber_when_dirty_grey_when_clean_and_a_hollow_ring_when_unknown()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true };
        var (row, window, host) = Realize(vm);
        var dot = DsResources.Descendants(row.Dot).OfType<Ellipse>().Single();

        // dirty → dolu amber (DS WillBuildDot, olduğu gibi tüketilir).
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotDirty"), DsResources.ColorOf(dot.Fill));
        Assert.Null(dot.Stroke);

        // clean → dolu gri, kontursuz.
        vm.WillBuild = false;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotClean"), DsResources.ColorOf(dot.Fill));
        Assert.Null(dot.Stroke);

        // unknown(null) → içi boş + halka. Halka fırçası kontrolün KENDİ kararıdır (Brush.DotOutline, hakemlik
        // bekleyen Ç-1) — kart onu EZMEZ, olduğu gibi tüketir.
        vm.WillBuild = null;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotUnknown"), DsResources.ColorOf(dot.Fill));
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotOutline"), DsResources.ColorOf(dot.Stroke));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_pair_is_shown_only_for_dirty_rows_and_is_replaced_by_the_two_hover_icons()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true, CurrentSha = "a3f81c2" };
        var (row, window, _) = Realize(vm);

        // dirty + hover yok → sha çifti görünür, aç-ikonları YOK ([L1] artık Collapsed bile değil: hiç kurulmamış).
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);
        Assert.Null(row.HoverIcons);

        // hover → sha yerini folder + VS ikonlarına bırakır (aynı 118px blok).
        row.SimulateHover(true);
        Assert.Equal(Visibility.Collapsed, row.ShaText.Visibility);
        Assert.Equal(Visibility.Visible, row.HoverIcons!.Visibility);

        // hover biter → yine sha (ikon bloğu kurulu kalır, yalnız gizlenir → hover/leave döngüsü yeniden inşa etmez).
        row.SimulateHover(false);
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);
        Assert.Equal(Visibility.Collapsed, row.HoverIcons!.Visibility);

        // clean/unknown satır → sha ASLA gösterilmez (yalnız WillBuild==true).
        vm.WillBuild = false;
        row.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, row.ShaText.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [L1/It-5 perf] lazy hover eylem bloğu

    [StaFact]
    public void The_hover_actions_are_not_built_until_the_row_is_hovered_for_the_first_time()
    {
        var vm = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending) { WillBuild = true };
        var (row, window, _) = Realize(vm);

        // Hover ÖNCESİ: ikon butonları ve chooser popup'ı satırın ağacında (görsel VEYA mantıksal) HİÇ YOK —
        // eskiden Collapsed olarak her satırda kuruluyorlardı (191 satırda ~3056 nesne).
        Assert.Null(row.Actions);
        var before = DsResources.RealizedObjects(row);
        Assert.Empty(before.OfType<Button>());
        Assert.Empty(before.OfType<Popup>());

        row.SimulateHover(true);
        row.UpdateLayout();

        // Hover SONRASI: blok var, görünür ve satırın sağ bloğunun İÇİNDE (sha ile aynı 118px slot).
        Assert.NotNull(row.Actions);
        Assert.Equal(Visibility.Visible, row.HoverIcons!.Visibility);
        var after = DsResources.RealizedObjects(row);
        Assert.Equal(2, after.OfType<Button>().Count());
        Assert.Single(after.OfType<Popup>());

        // İkinci hover YENİDEN İNŞA ETMEZ (aynı instance).
        var built = row.Actions;
        row.SimulateHover(false);
        row.SimulateHover(true);
        Assert.Same(built, row.Actions);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_lazily_built_hover_actions_realize_their_icons_tooltips_and_tokens()
    {
        // [It-4b dersi / realize testi zorunlu] Yeni bir XAML kökü (Views/ProjectRowActions.xaml) eklendi. Nesnenin
        // VAR olması yetmez: headless suite XAML runtime çözümlemesini görmez → ilk hover'da kurulan ağacın
        // GERÇEKTEN realize olduğu ve token'larını (ikon geometrisi, kalınlık, stil, tooltip metni) çözdüğü pinlenir.
        var vm = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending);
        var (row, window, host) = Realize(vm);

        row.SimulateHover(true);
        row.UpdateLayout();
        var actions = row.Actions!;

        // Erişilebilirlik adları + tooltip metinleri BİREBİR (design-v1) — kopya metinler değişmedi.
        Assert.Equal("Reveal in Explorer", AutomationProperties.GetName(actions.RevealButton));
        Assert.Equal("Open in Visual Studio", AutomationProperties.GetName(actions.VsButton));
        Assert.Equal("Reveal in Explorer", ((ToolTip)actions.RevealButton.ToolTip).Content);
        Assert.Equal("Open in Visual Studio", ((ToolTip)actions.VsButton.ToolTip).Content);

        // Ds.IconButton stili çözüldü (şablon genişledi → Foreground'a bağlı ikon konturu boyanabilir).
        Assert.NotNull(actions.RevealButton.Style);
        Assert.NotNull(actions.VsButton.Style);

        // İkon geometrileri PAYLAŞILAN token nesneleridir (Icons.xaml) — kalınlık da token'dan.
        AssertIcon(host, actions.RevealButton, "Icon.FolderOpen");
        AssertIcon(host, actions.VsButton, "Icon.Vs");

        // VS-chooser popover'ı: kapalı doğar, chrome stili (Ds.Popover) çözülür, satır kabı boş başlar.
        Assert.False(actions.VsChooser.IsOpen);
        Assert.NotNull(actions.VsChooserContent.Style);
        Assert.Empty(actions.VsChooserRows.Children);
        GC.KeepAlive(window);
    }

    private static void AssertIcon(FrameworkElement host, Button button, string iconKey)
    {
        var path = DsResources.RealizedObjects(button).OfType<Path>().Single();
        Assert.Same(host.FindResource(iconKey), path.Data);
        Assert.Equal((double)host.FindResource(iconKey + ".StrokeThickness"), path.StrokeThickness);
        Assert.NotNull(path.Stroke); // {Binding Foreground, AncestorType=Button} çözüldü (kontur görünür)
    }

    [StaFact]
    public void The_row_applies_its_full_state_once_per_data_context_not_twice()
    {
        // [L1] Eskiden ApplyAll üretimde satır başına İKİ kez koşuyordu (DataContextChanged + Loaded) → ~10
        // SetResourceReference ve 3 animasyon kurulumu boşuna tekrarlanıyordu. Artık "hazır olduğunda bir kez".
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, DataContext = vm };
        Assert.Equal(1, row.ApplyAllCount);

        var window = DsResources.Realize(host, row); // Loaded → TEKRAR ETMEZ
        Assert.Equal(1, row.ApplyAllCount);

        // Yeni bir VM (container yeniden kullanımı) → tam tazeleme yeniden gerekir.
        row.DataContext = new ProjectRowViewModel("id2", "Bar", ProjectRowState.Pending);
        Assert.Equal(2, row.ApplyAllCount);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Duration_column_uses_the_shared_formatter_and_turns_red_on_failure()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded) { DurationMs = 4200 };
        var (row, window, host) = Realize(vm);

        // Paylaşılan DurationFormat (C2) — kart kendi biçimlemesini uydurmaz.
        Assert.Equal(DurationFormat.Duration(4200), row.DurationText.Text); // "4.2s"
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(row.DurationText.Foreground));

        // Failed → kırmızı (Brush.StatusFailText), metin yine paylaşılan biçimleyiciden.
        vm.State = ProjectRowState.Failed;
        row.UpdateLayout();
        Assert.Equal(DurationFormat.Duration(4200), row.DurationText.Text);
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusFailText"), DsResources.ColorOf(row.DurationText.Foreground));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Breathing_runs_a_real_opacity_clock_while_building_and_releases_it_after()
    {
        // [Fix wave 1, Finding 2] Görünürlük saatin sahte proxy'siydi: StopBreathing silinse bile Visibility
        // testi yeşil kalırdı (Visibility ApplyBreathing'in EN BAŞINDA koşulsuz set edilir). GraphRenderTests
        // (nabız) deseniyle: gerçek 30fps opaklık saatini HasAnimatedProperties ile ölç — motion enjekte edilir
        // (headless'ta App.Motion null → hiç saat başlamazdı; GraphView.AnimationsEnabledProvider deseni).
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => true, DataContext = vm };
        var window = DsResources.Realize(host, row);

        // Building iken: nefes katmanında GERÇEK bir (dönen) opaklık saati var.
        Assert.True(row.BreathLayer.HasAnimatedProperties);

        // Building'i terk edince saat SERBEST kalır (yalnız Visibility'yi Collapse etmek yetmez).
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();
        Assert.False(row.BreathLayer.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Breathing_layer_only_shows_while_building_and_is_capped_at_thirty_fps()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var (row, window, _) = Realize(vm);

        // Yalnız building iken katman görünür; durum building'i terk edince gizlenir.
        Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility);
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);

        // 30fps sınırı + 3.8s süre — kontrolün kullandığı AYNI fabrika.
        var anim = ProjectRow.BuildBreathingAnimation(row);
        Assert.Equal(30, Timeline.GetDesiredFrameRate(anim));
        Assert.Equal(TimeSpan.FromMilliseconds(3800), anim.KeyFrames[^1].KeyTime.TimeSpan);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [E3/T42] liste mount reveal (bo-reveal)

    [Fact]
    public void The_list_row_reveal_delay_is_10ms_per_row_capped_at_380ms()
    {
        Assert.Equal(0.0, ProjectRow.RevealDelayMs(0));
        Assert.Equal(10.0, ProjectRow.RevealDelayMs(1));
        Assert.Equal(370.0, ProjectRow.RevealDelayMs(37));
        Assert.Equal(380.0, ProjectRow.RevealDelayMs(38));   // tavana ilk ulaşım
        Assert.Equal(380.0, ProjectRow.RevealDelayMs(1000)); // tavan (BuildApp.jsx:367 min(i*10, 380))
    }

    [StaFact]
    public void A_reveal_holds_opacity_at_zero_during_the_delay_and_runs_a_real_clock()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => true, DataContext = vm };
        var window = DsResources.Realize(host, row);

        row.PlayReveal(5);

        // Gecikme boyunca opacity 0 TUTULUR (flash yok) + kayma -5px'ten başlar; ikisi de GERÇEK saatler.
        Assert.Equal(0.0, row.Root.Opacity);
        Assert.True(row.Root.HasAnimatedProperties);
        Assert.True(row.ShakeTranslate.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Reduced_motion_places_the_row_instantly_with_no_reveal_clock()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var (row, window, _) = Realize(vm); // headless App.Motion null → reduced-motion

        row.PlayReveal(5);

        Assert.Equal(1.0, row.Root.Opacity);
        Assert.False(row.Root.HasAnimatedProperties);
        Assert.Equal(0.0, row.ShakeTranslate.Y);
        GC.KeepAlive(window);
    }

    // [Fix wave 1, Finding 1 + lens-3 Minor] Şerit rengi TÜM statüler için pinlenir (cycle + queued dahil) —
    // satır gerçekten kurulur, Stripe.Fill'in çözdüğü fırça statü başına doğru token'dır. Discovered → transparent
    // (token DEĞİL) ayrı test edilir.
    [StaTheory]
    [InlineData(ProjectRowState.Started, false, false, "Brush.Amber")]
    [InlineData(ProjectRowState.Succeeded, false, false, "Brush.StatusSuccess")]
    [InlineData(ProjectRowState.Failed, false, false, "Brush.StatusFail")]
    [InlineData(ProjectRowState.Skipped, false, false, "Brush.StatusSkipped")]
    [InlineData(ProjectRowState.Pending, true, false, "Brush.StatusCycle")]   // InCycle → cycle (skipped/pending'i ezer)
    [InlineData(ProjectRowState.Pending, false, true, "Brush.StatusQueued")]  // willBuild + run uçuşta → queued
    public void Status_stripe_uses_the_right_token_brush_per_status(
        ProjectRowState state, bool inCycle, bool queued, string expectedKey)
    {
        var vm = new ProjectRowViewModel("id", "Foo", state) { InCycle = inCycle };
        if (queued) { vm.WillBuild = true; vm.IsRunActive = true; }
        var (row, window, host) = Realize(vm);

        Assert.Equal(DsResources.TokenColor(host, expectedKey), DsResources.ColorOf(row.Stripe.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Discovered_stripe_is_transparent_not_a_token_brush()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending); // pending, no run, no cycle
        var (row, window, _) = Realize(vm);

        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Stripe.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Dep_tooltip_is_the_verbatim_brief_text_with_the_common_prefix_stripped()
    {
        // [D5] Kısa-ad öneki artık VERİ-TÜREVLİ ve satıra RunViewModel'den itilir (NamePrefix) — hardcode "OSYS."
        // yok. İzole kart testinde ata RunViewModel olmadığından öneki doğrudan satıra veririz (Sha testindeki
        // "izole kartta run VM yok" deseninin eşi).
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Failed) { NamePrefix = "OSYS." };
        var (row, window, _) = Realize(vm);

        // Tek dep: ortak önek atılır.
        vm.DepIssues = new[] { "OSYS.Sales.Core" };
        row.UpdateLayout();
        Assert.Equal("Failed dependency: Sales.Core — last successful output referenced", row.DepTooltip);

        // İki dep: ", " ile birleşir (brief slot 6 birebir).
        vm.DepIssues = new[] { "OSYS.Sales.Core", "OSYS.Billing.Core" };
        row.UpdateLayout();
        Assert.Equal("Failed dependency: Sales.Core, Billing.Core — last successful output referenced", row.DepTooltip);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Glyph_tooltip_is_the_status_label_with_building_elapsed_and_dependency_issue_suffix()
    {
        // Building: "Building — {Elapsed}" (paylaşılan biçimleyici).
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started) { DurationMs = 5000 };
        var (row, window, _) = Realize(vm);
        Assert.Equal($"Building — {DurationFormat.Elapsed(5000)}", row.GlyphTooltip);

        // Non-building, dep sorunsuz: yalın etiket.
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();
        Assert.Equal("Succeeded", row.GlyphTooltip);

        // Non-building + dep sorunu: " — dependency issue" eki.
        vm.State = ProjectRowState.Failed;
        vm.DepIssues = new[] { "OSYS.Sales.Core" };
        row.UpdateLayout();
        Assert.Equal("Failed — dependency issue", row.GlyphTooltip);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_text_interpolates_current_and_target_with_an_arrow()
    {
        // [lens Minor] "{cur} → {target}". [W1] Target da satır VM'inden gelir; burada set EDİLMEDİĞİ için
        // (henüz syncCompleted gelmemiş satır) boştur — ok + cur yarısı yine de pinlenir.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true, CurrentSha = "a3f81c2" };
        var (row, window, _) = Realize(vm);

        Assert.Equal("a3f81c2 → ", row.ShaText.Text);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_shows_the_target_alone_when_the_project_was_never_built()
    {
        // [W1 KARAR] Hiç derlenmemiş proje (BuildState kaydı yok ⇒ BuiltCommit null) sol yarısını BOŞ bırakır:
        // kart o satırda çift yerine YALNIZ hedefi basar — yalın-ok pürüzü (" → a3f81c2") ÜRETİLMEZ ve "—"
        // gibi bir yer tutucu UYDURULMAZ. Design-v1'de bu durumun karşılığı yoktur (prototip her projeye sentetik
        // bir curSha üretir), bu yüzden E6 interim davranışı KORUNUR: en az sürprizli seçenek.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending)
        { WillBuild = true, TargetSha = "a3f81c2" }; // CurrentSha boş = hiç derlenmemiş
        var (row, window, _) = Realize(vm);

        Assert.Equal("a3f81c2", row.ShaText.Text);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Both_halves_of_the_sha_pair_are_shortened_to_seven_hex_digits()
    {
        // [W1] ÜRETİM KUSURU: her iki kaynak da HAM 40-hex'tir (CurrentSha = BuildState.BuiltCommit, TargetSha =
        // remote-tracking ref) ve It-4b'de olduğu gibi basılıyordu. design-v1 README: "SHA 7 hane a3f81c2".
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending)
        {
            WillBuild = true,
            CurrentSha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b",
            TargetSha = "b7e91d4c0affee1122334455667788990aabbccd",
        };
        var (row, window, _) = Realize(vm);

        Assert.Equal("a3f81c2 → b7e91d4", row.ShaText.Text);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_late_arriving_target_sha_refreshes_an_already_rendered_row()
    {
        // [W1] Olay sırası SABİT: buildPreview (CurrentSha) → syncCompleted (TargetSha). Kart hedefi render anında
        // ata ağaçtan ÇEKSEYDİ, satır sha'sını target daha null'ken hesaplar ve bir daha tazelenmezdi (ilk Sync'ten
        // sonra slot BOŞ kalırdı). Değer satıra İTİLDİĞİ için geç gelen taraf satırı GERÇEKTEN tazeler.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending)
        { WillBuild = true, CurrentSha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b" };
        var (row, window, _) = Realize(vm);
        Assert.Equal("a3f81c2 → ", row.ShaText.Text); // hedef henüz bilinmiyor

        vm.TargetSha = "b7e91d4c0affee1122334455667788990aabbccd"; // syncCompleted
        row.UpdateLayout();

        Assert.Equal("a3f81c2 → b7e91d4", row.ShaText.Text);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_rendered_sha_pair_fits_inside_the_118px_right_block()
    {
        // [W1] design-v1 sağ blok min 118px (README §kart slot 4). 7+7 haneye kısaltılmış çift GERÇEKTEN ölçülür —
        // ham 40-hex hâli sığmazdı. pack:// aileler headless çözülmez → aynı OTF file:// üzerinden enjekte edilir
        // (GraphCullTests/TrackedTextBlockTests deseni); üretimde bu seam ASLA set edilmez.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending)
        {
            WillBuild = true,
            CurrentSha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b",
            TargetSha = "b7e91d4c0affee1122334455667788990aabbccd",
        };
        var (row, window, _) = Realize(vm);
        row.ShaText.FontFamily = DsResources.MonoFontFamily;
        row.UpdateLayout();

        double width = row.ShaText.DesiredSize.Width;
        Assert.True(width > 0, "sha metni hiç ölçülemedi (font çözülmedi mi?)");
        Assert.True(width <= 118, $"kısaltılmış sha çifti 118px slota sığmadı: {width}px");

        // Kontrol grubu: ham (kısaltılmamış) hâli AYNI ölçümle slota SIĞMAZ — yani iddia önemsizce doğru değil.
        row.ShaText.Text = $"{vm.CurrentSha} → {vm.TargetSha}";
        row.UpdateLayout();
        Assert.True(row.ShaText.DesiredSize.Width > 118, "ham 40-hex çift beklenmedik biçimde 118px'e sığdı");
        GC.KeepAlive(window);
    }
}
