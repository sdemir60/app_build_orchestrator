using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;
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
    /// <summary>[A13/T4 fix-1 · C2] <paramref name="animations"/> eklendi — üç yeni test (T4) bu satırları inline
    /// kopyalıyordu (<c>Breathing_runs_a_real_opacity_clock…</c>'daki T4-öncesi kopyayla birlikte DÖRDÜNCÜ kez).
    /// Kardeş dosyalardaki AYNI desen: <c>SuccessFlourishTests.Realize(vm, animations)</c>,
    /// <c>StickyRibbonTests.Realize(vm, forceAnimations)</c>.</summary>
    private static (ProjectRow row, Window window, Border host) Realize(ProjectRowViewModel vm, bool animations = false)
    {
        var host = DsResources.NewHost();
        var row = animations
            ? new ProjectRow { AnimationsEnabledProvider = () => true, DataContext = vm }
            : new ProjectRow { DataContext = vm };
        var window = DsResources.Realize(host, row);
        return (row, window, host);
    }

    /// <summary>
    /// Şerit satırın TAM yüksekliğince uzanır — dikey iç boşluğu yoktur. Kartları birbirinden ayıran şey
    /// yalnız YATAY ayraçtır (satırın alt çizgisi, <c>border-subtle</c>).
    ///
    /// <para>[DEĞİŞEN KURAL — kullanıcı kararı] design v1.7.0 §2.4 şeride <b>1px dikey iç boşluk</b> veriyordu
    /// ("bitişik satırlarda tek kesintisiz çizgiye kaynamasın"). Sahada bakıldığında istenmedi: kesilen şerit
    /// aynı 2px'i daha hafif gösteriyor ve satır "ince" okunuyordu. Karar: dikey kesinti kalkar, ayrımı
    /// yataydaki ayraç yapar — o zaten her satırın altında var ve şeridi geçerken kendisi böler.</para>
    /// </summary>
    [StaFact]
    public void The_status_stripe_runs_the_full_row_height_with_no_vertical_inset()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var (row, window, _) = Realize(vm);

        Assert.Equal(new Thickness(0), row.Stripe.Margin);

        // Ayrım yataydadır: satırın kendi alt çizgisi. Şerit onun ÜSTÜNDEKİ tüm alanı kaplar.
        var root = (Border)row.Content;
        Assert.Equal(new Thickness(0, 0, 0, 1), root.BorderThickness);
        Assert.Equal(LayoutMetrics.DefaultRowHeight - root.BorderThickness.Bottom, row.Stripe.ActualHeight);
        GC.KeepAlive(window);
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

    // ---------------------------------------------------------------- [A13/T4 · n6] rakamlar tabular

    /// <summary>[A13/T4 · n6 · fix-1 · B3/C3] design-v1 README:48 (§1.2): <i>"makine çıktısı (console, süre, SHA,
    /// sayaç, yol) = Geist Mono, <b>DAİMA tabular rakam</b>."</i> — üretimde <c>Typography.NumeralAlignment</c>
    /// ALTI yerde SET edilir: <c>ProjectRow.xaml:74</c> (sha, burada) · <c>:107</c> (süre, burada) ·
    /// <c>EventStreamView.xaml:41</c> (aktif satır — <see cref="EventStreamTests.The_active_line_and_row_text_are_tabular"/>)
    /// · <c>EventStreamView.xaml.cs</c>'in <c>_text</c> için yaptığı <c>NumeralAlignment</c> ataması (satırın kendi
    /// metni — AYNI test, fix-1'de eklenen ALTINCI yer, önceki
    /// sürüm bunu kaçırıyordu ve doc'u yanlışlıkla "dört yer" sayıyordu) · <c>StickyRibbon.xaml:38</c> (faz metni —
    /// <see cref="StickyRibbonTests.The_phase_text_is_tabular"/>) · <c>ActionBar.xaml.cs:258</c> (sayaç chip değeri
    /// — <see cref="ActionBarTests.The_sigma_chip_value_is_tabular"/>).
    ///
    /// <para><b>fix-1 · C3:</b> önceki sürüm bunları TEK yeni dosyada (<c>TabularFiguresTests.cs</c>) topluyordu ve
    /// dört realize kurulumunu + <c>NewVm</c>/<c>NeverTickingBatcher</c> ikilisini SIFIRDAN yeniden yazıyordu
    /// (kopya YASAK) — o dosya SİLİNDİ; her assert artık KONTROLÜN KENDİ test sınıfına, kendi <c>Realize</c>
    /// yardımcısıyla tek satır olarak eklendi (m5 deseni). Kapsam beyanı TEK yerde (burada) tutulur, diğerleri
    /// <c>&lt;see cref&gt;</c> ile buraya bağlanır — yeni bir mono alan eklenirse bu ALTI test OTOMATİK kapsamaz
    /// (bilinçli — kural kod incelemesiyle korunur).</para></summary>
    [StaFact]
    public void The_project_row_sha_and_duration_columns_are_tabular()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var (row, window, _) = Realize(vm);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(row.ShaText));
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(row.DurationText));
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

    /// <summary>
    /// [design v1.12.0 §2.4-2 · §9-1] <b>Nokta artık statü kanalıdır.</b> Sol şeritle AYNI rengi taşır ve
    /// TOOLTIP TAŞIMAZ; başlangıç modunda onun yerine dört yaylı bir HALKA görünür.
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Başlangıç modu eskiden AYNI daire üstünde dolgu silinip KESİKLİ
    /// çember çizilerek anlatılıyordu; 8px'te kesikler tırtıklıydı. Artık iki eleman üst üste durur ve
    /// aralarında yalnız opaklık değişir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Burada iki test vardı ve ikisi de kalkan ORTOGONAL plan kanalını
    /// pinliyordu: <c>The_will_build_dot_says_why_the_project_will_build</c> (noktanın tooltip'i gerekçeyi
    /// söyler: "Never built — will build" vb.) ve
    /// <c>Will_build_dot_is_amber_when_dirty_grey_when_clean_and_a_hollow_ring_when_unknown</c> (dolu amber /
    /// dolu gri / içi boş halka). v1.11.0 §9-1 o kanalı kaldırdı — <i>renk yalnız son işlemin hikâyesini
    /// anlatır</i>; plan bilgisi satırın çift SHA metnine indi (aşağıdaki SHA testleri onu pinler). İki eski
    /// iddia bu tek teste indi.</para>
    /// </summary>
    [StaFact]
    public void The_status_dot_follows_the_stripe_colour_and_carries_no_tooltip()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true, Fresh = true };
        var (row, window, host) = Realize(vm);

        Assert.Null(row.Dot.ToolTip);

        // Başlangıç modu: dolu daire GÖRÜNMEZ, halka görünür — plan (WillBuild=true) hiçbir renk üretmez.
        Assert.Equal(0.0, row.Dot.Fill.Opacity);
        Assert.Equal(StartMode.RingOpacity, row.Dot.Ring.Opacity);
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSkippedBorder"), DsResources.ColorOf(row.Dot.Ring.Stroke));
        Assert.NotEmpty(row.Dot.Ring.StrokeDashArray);

        // İşlem başladı (başlangıç modu düştü) ve satır kapsamda: nokta AMBER — şeridin ta kendisi.
        vm.Fresh = false;
        vm.Marked = true;
        row.UpdateLayout();
        var dot = row.Dot.Fill;
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(dot.Fill));
        Assert.Equal(DsResources.ColorOf(row.Stripe.Fill), DsResources.ColorOf(dot.Fill)); // şerit == nokta
        Assert.Equal(0.0, row.Dot.Ring.Opacity);

        // Sonuç geldi: ikisi birlikte sonuç rengine döner.
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSuccess"), DsResources.ColorOf(dot.Fill));
        Assert.Equal(DsResources.ColorOf(row.Stripe.Fill), DsResources.ColorOf(dot.Fill));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_is_shown_on_every_row_and_is_replaced_by_the_two_hover_icons()
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

        // [DEĞİŞEN KURAL — design v1.7.0 §2.4] Eski iddia: "clean/unknown satırda sha ASLA gösterilmez".
        // SHA artık HER satırda görünür (clean satırda tek sha, faint) — "yalnız derleneceklerde göster"
        // hover'dan çıkışta satırlar arası layout sıçraması yaratıyordu.
        vm.WillBuild = false;
        row.UpdateLayout();
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);
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
        // [DEĞİŞEN KURAL — design v1.11.0 §2.4-4] Blok eskiden İKİ düğme (folder + VS) ve TEK popup (VS
        // seçici) taşıyordu. v1.11.0 satır aksiyonlarını ekledi: play (Button) + ⋯ (ToggleButton) ve ⋯'in
        // menüsü ikinci bir popup'tır. Tembellik iddiası DEĞİŞMEDİ — hepsi hâlâ İLK HOVER'da doğar.
        Assert.Equal(3, after.OfType<Button>().Count());          // play + folder + VS
        Assert.Single(after.OfType<ToggleButton>());              // ⋯
        Assert.Equal(2, after.OfType<Popup>().Count());           // satır menüsü + VS seçici

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
        // [DEĞİŞEN KURAL — design v1.11.0 §2.4-4 · §9-13] Tooltip'ler DS <c>ToolTip</c> nesnesi olmaktan
        // çıktı: satırda DS tooltip'i taşıyan TEK öğe uyarı üçgenidir. İkon butonlarınınki HTML'in native
        // `title`'ının WPF karşılığıdır — düz metin + OS'un fare-üzerinde-bekleme gecikmesi.
        Assert.Equal("Reveal in Explorer", AutomationProperties.GetName(actions.RevealButton));
        Assert.Equal("Open in Visual Studio", AutomationProperties.GetName(actions.VsButton));
        Assert.Equal("Reveal in Explorer", actions.RevealButton.ToolTip);
        Assert.Equal("Open in Visual Studio", actions.VsButton.ToolTip);
        Assert.Equal(BuildOrchestrator.App.Controls.AppTooltipDefaults.NativeDelayMs,
            ToolTipService.GetInitialShowDelay(actions.RevealButton));
        Assert.Equal(BuildOrchestrator.App.Controls.AppTooltipDefaults.NativeDelayMs,
            ToolTipService.GetInitialShowDelay(actions.VsButton));

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
        var (row, window, _) = Realize(vm, animations: true);

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
        // [A13/T4 · m5 · fix-1 · D6] Tepe opaklık 0.32 — BuildApp.jsx:34 `@keyframes bo-breath { 0%,100% opacity:0;
        // 50% opacity:0.32; }`. Süre/frame-rate ÖNCEDEN pinliydi; tepe DEĞER testsizdi (ProjectRow.xaml.cs:32
        // BreathPeakOpacity). Sabit `KeyFrames[1]` indeksi yerine dinamik tepe (fabrikaya bir keyframe eklenirse
        // konum kayabilir, m1#2'nin deseniyle tutarlı) — max DEĞER okunur, sıra/indeks varsayılmaz.
        double peakOpacity = 0;
        foreach (DoubleKeyFrame frame in anim.KeyFrames) peakOpacity = Math.Max(peakOpacity, frame.Value);
        Assert.Equal(0.32, peakOpacity);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [cycles] bekleyen SCC üyesi nefes/süre almaz

    /// <summary>[cycles] Nefes katmanı ham <c>State==Started</c> okuyordu — sırasını bekleyen SCC üyesi (motor
    /// durumu Started ama <c>CycleWaiting=true</c>) de amber nefesle yanıp sönüyordu. Tek doğruluk kaynağı
    /// <see cref="ProjectRowViewModel.IsCompiling"/>'tir; bekleyen üyede o false'tur.</summary>
    [StaFact]
    public void A_waiting_cycle_member_does_not_breathe()
    {
        var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { CycleWaiting = true };
        var (row, window, _) = Realize(vm);

        Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[cycles] Süre sütunu ham <c>State==Started</c> okuyordu — bekleyen üye de canlı sayıyordu (üstelik
    /// her turda sıfırlanıp yeniden koşan bir sayaç, bilgi değil gürültüydü). <c>IsCompiling</c> false'ken satır
    /// "—" göstermeli.</summary>
    [StaFact]
    public void A_waiting_cycle_member_shows_no_live_elapsed()
    {
        var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { CycleWaiting = true, DurationMs = 5000 };
        var (row, window, _) = Realize(vm);

        Assert.Equal("—", row.DurationText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>[cycles] Kontrol grubu: grubun sırası GERÇEKTEN kendisindeyken (<c>CycleWaiting=false</c>) satır
    /// eskisi gibi nefes alır ve canlı sayar — fix yalnız bekleyen üyeyi susturur, derleneni DEĞİL.</summary>
    [StaFact]
    public void The_compiling_member_still_breathes_and_counts()
    {
        var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { DurationMs = 5000 };
        var (row, window, _) = Realize(vm);

        Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility);
        Assert.NotEqual("—", row.DurationText.Text); // canlı elapsed
        GC.KeepAlive(window);
    }

    /// <summary>[cycles] Sıra kardeşe geçtiği ANDA (motor durumu HÂLÂ Started, yalnız <c>CycleWaiting</c> flip'i)
    /// nefes ve süre birlikte susmalı — <c>CycleWaiting</c> setter'ı <c>Status</c>'u tetikler, <c>OnVmPropertyChanged</c>'in
    /// Status case'i bunu ApplyBreathing/ApplyDuration'a taşımalı.</summary>
    [StaFact]
    public void Breathing_stops_the_moment_the_turn_passes_to_a_sibling()
    {
        var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started);
        var (row, window, _) = Realize(vm);

        vm.CycleWaiting = true; // kardeş başladı — sıra artık onda
        row.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);
        Assert.Equal("—", row.DurationText.Text);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T4 · m1] shake: 360ms · X ekseni · ±3px · bir kez

    /// <summary>[A13/T4 · m1] Otorite <c>BuildApp.jsx:18,30</c>: <c>.bo-shake { animation: bo-shake .36s
    /// var(--ease-standard) 1; }</c> + <c>@keyframes bo-shake { 10%,90% translateX(-2px); 25%,75% translateX(3px);
    /// 50% translateX(-3px); }</c> — satır <c>shake &amp;&amp; !REDUCED</c> iken (yalnız hata ANINDA) X ekseninde
    /// sallanır. <b>Önceki tek kanıt</b> (<c>:300,:314</c> — bu dosyanın <c>PlayReveal</c> testleri)
    /// <see cref="ProjectRow.ShakeTranslate"/>'i yalnız <b>Y</b> ekseninde okuyordu (o da reveal'in kendi kaymasıdır,
    /// shake DEĞİL) — <c>PlayShake</c>'in KENDİSİ (state Failed'e geçtiğinde) hiçbir testte tetiklenmiyordu.
    /// Üretim tetikleyicisi <see cref="ProjectRowViewModel.State"/> ataması (VM property), doğrudan <c>PlayShake()</c>
    /// çağrısı DEĞİL.</summary>
    [StaFact]
    public void Shake_moves_the_x_axis_translate_not_y_when_a_row_fails()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started); // _prevState tohumu: Failed DEĞİL
        var (row, window, _) = Realize(vm, animations: true);

        vm.State = ProjectRowState.Failed; // ÜRETİM tetikleyicisi: OnVmPropertyChanged → ApplyStateTransition → PlayShake

        DispatcherPump.PumpUntil(() => Math.Abs(row.ShakeTranslate.X) > 0.05, TimeSpan.FromSeconds(1));

        Assert.True(Math.Abs(row.ShakeTranslate.X) > 0.05, "shake X ekseninde hiç hareket etmedi (yanlış eksene mi bağlı?)");
        Assert.Equal(0.0, row.ShakeTranslate.Y); // PlayReveal hiç çağrılmadı — Y bu testte ASLA dokunulmamalı
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T4 · m1 · fix-1 · A2/D7/D8/D9] Genlik + süre + tekrarsızlık, KAYNAKTAN (deterministik — sub-ms
    /// hassasiyette bir spline tepesini gerçek saatle avlamak yük-hassastır, D8 "yeni flake yasak"; ölçüldü, bkz.
    /// rapor). <see cref="ProjectRow.BuildShakeAnimation"/> KONTROLÜN KENDİSİNİN kullandığı fabrikadır
    /// (<see cref="PlayShake"/> onu doğrudan çağırır — bu ayrı bir kopya yol DEĞİL); otorite <c>BuildApp.jsx:18,30</c>:
    /// 10/90%→∓2px · 25/75%→±3px · 50%→∓3px · 100%→0, toplam 360ms, <b>BİR KEZ</b> (<c>animation-iteration-count:1</c>).
    ///
    /// <para><b>fix-1 · D7:</b> fabrika artık REALIZE EDİLMİŞ bir satırla çağrılıyor (<c>Realize(vm)</c>) —
    /// <c>KeySpline.EaseStandard</c> token'ı GERÇEK kaynak ağacından çözülür (önceki <c>new ProjectRow()</c> çıplak
    /// ağaçta fallback'e düşüyordu, ease token'ı hiç sınanmıyordu).</para>
    ///
    /// <para><b>fix-1 · A2:</b> "bir kez" iddiası artık <see cref="RepeatBehavior"/>'ı DOĞRUDAN okur —
    /// <c>FillBehavior.Stop</c> yalnız "clock takılı kalmaz" demektir, tekrar sayısıyla ilgisi YOKTUR (önceki
    /// yorum bunu yanlış iddia ediyordu; <c>RepeatBehavior</c> 5x'e çevrilse eski assert bunu YAKALAMAZDI).</para>
    ///
    /// <para><b>fix-1 · D8:</b> önceki sürümdeki <c>observedPeak</c>/son-<c>KeyTime</c> assert'leri, aşağıdaki
    /// per-index döngünün ZATEN kanıtladığı şeyi (i=2'de -3, i=5'te 360ms) yeniden hesaplıyordu — ölü kod,
    /// silindi.</para></summary>
    [StaFact]
    public void Shake_keyframes_match_the_bo_shake_authority_peaking_at_three_pixels_for_360ms()
    {
        var (row, window, _) = Realize(new ProjectRowViewModel("id", "Foo", ProjectRowState.Started));
        var anim = ProjectRow.BuildShakeAnimation(row);

        Assert.Equal(FillBehavior.Stop, anim.FillBehavior); // clock BİTİNCE takılı kalmaz ("bir kez" DEĞİL — o iddia aşağıda)
        Assert.False(anim.AutoReverse);
        Assert.Equal(new RepeatBehavior(1.0), anim.RepeatBehavior); // BİR KEZ — CSS animation-iteration-count:1 karşılığı

        Assert.Equal(6, anim.KeyFrames.Count);
        double[] expectedValues = [-2, 3, -3, 3, -2, 0];
        double[] expectedPct = [0.10, 0.25, 0.50, 0.75, 0.90, 1.0];
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(expectedValues[i], anim.KeyFrames[i].Value);
            Assert.Equal(TimeSpan.FromMilliseconds(360 * expectedPct[i]), anim.KeyFrames[i].KeyTime.TimeSpan);
        }
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T4 · m1 · fix-1 · A1] Davranışsal kanıt: gerçekten oynar ve kendiliğinden sıfıra oturur.
    /// Süre/genlik/RepeatBehavior iddiası artık TAMAMEN yukarıdaki deterministik testte — burası yalnız "üretim
    /// yolundan tetiklenir + kendi kendine biter" iddiasını taşır.
    ///
    /// <para><b>fix-1 · A1:</b> önceki sürüm <c>PumpUntil(() =&gt; false, 450ms)</c> ile KOŞULSUZ bekliyordu —
    /// bu, animasyonun süresini DEĞİL pompanın kendi timeout'unu ölçen bir gizli <c>Thread.Sleep</c>'ti.
    /// <c>HasAnimatedProperties</c>'i bitiş sinyali olarak kullanmak da DENENDİ ve YANLIŞ çıktı: ÖLÇÜLDÜ —
    /// <c>FillBehavior.Stop</c> sonrası bu bayrak kendiliğinden false'a DÜŞMÜYOR (WPF'in "fire-and-forget"
    /// <c>BeginAnimation</c> clock'u, açıkça <c>BeginAnimation(prop,null)</c> çağrılmadıkça iliştirilmiş kalıyor —
    /// bu kod tabanında hiçbir yerde bu varsayıma dayanan bir örnek YOK, dolayısıyla varsayım DOĞRULANMAMIŞTI).
    /// Bunun yerine gerçek DEĞER gözlenir: X önce hareket eder (<c>sawMotion</c>), sonra TAM <c>0.0</c>'a oturur —
    /// bu, animasyonun ORTASINDAKİ geçici sıfır-geçişlerinden (curve sürekli olduğundan ara değerlerde de 0'dan
    /// geçer) ayırt edilir çünkü kalıcı 0 yalnız 360ms'den SONRA (son keyframe + taban değer, ikisi de 0.0) durur;
    /// ara geçiş anları kesikli 5ms'lik pompa örneklemesiyle pratikte YAKALANAMAZ (ölçü-sıfır olay).</para>
    ///
    /// <para><b>ön-koşul (lens2):</b> tetik SONRASI, bekleme ÖNCESİ <c>HasAnimatedProperties</c> true assert
    /// edilir — aksi halde shake hiç BAŞLAMASA da (seam kopsa) <c>FillBehavior.Stop</c>'un taban değeri (X=0)
    /// testi vakumda yeşile düşürürdü.</para></summary>
    [StaFact]
    public void Shake_plays_exactly_once_and_settles_back_to_zero()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var (row, window, _) = Realize(vm, animations: true);

        vm.State = ProjectRowState.Failed;

        Assert.True(row.ShakeTranslate.HasAnimatedProperties, "ön-koşul: shake saati kurulmadı — sonraki assert vakum olurdu");

        // Animasyonun GERÇEKTEN hareket ettiğini VE kalıcı olarak sıfıra oturduğunu gözle (koşul-tabanlı, D8) —
        // sabit bir süre BEKLEMİYORUZ; koşul "hareket görüldü + şu an tam 0" ikisi birden sağlanınca çıkılır.
        bool sawMotion = false;
        DispatcherPump.PumpUntil(() =>
        {
            if (Math.Abs(row.ShakeTranslate.X) > 0.05) sawMotion = true;
            return sawMotion && row.ShakeTranslate.X == 0.0;
        }, TimeSpan.FromSeconds(2));

        Assert.True(sawMotion, "shake hiç hareket etmedi");
        Assert.Equal(0.0, row.ShakeTranslate.X); // son keyframe 0 (BuildApp.jsx:30 örtük %100 = kimlik dönüşüm)

        // "BİR KEZ" DAVRANIŞI: bir pencere DAHA pompala — X yeniden sıçramamalı (RepeatBehavior.Forever regresyonu;
        // RepeatBehavior'ın KENDİSİ zaten yukarıdaki deterministik testte pinli — bu yalnız gerçek clock'u sınar).
        bool restarted = false;
        DispatcherPump.PumpUntil(() =>
        {
            if (Math.Abs(row.ShakeTranslate.X) > 0.05) restarted = true;
            return restarted;
        }, TimeSpan.FromMilliseconds(300));

        Assert.False(restarted, "shake YENİDEN BAŞLADI — RepeatBehavior 'bir kez' değil");
        Assert.Equal(0.0, row.ShakeTranslate.X);
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

    // [DEĞİŞEN KURAL — design v1.11.0 §2.4-1] Sol şerit TEK statü kanalıdır ve HER satırda vardır. Üç iddia
    // sırayla değişti: (a) v1.7.0'da döngü üyeliği şeridi ARTIK EZMEZ; (b) discovered ile skipped AYNI gridir;
    // (c) v1.11.0'da QUEUED da AMBER'dır — kuyruk bir sonuç değil, işlemin kapsamıdır ve işaretleme dalgasıyla
    // yanan renk koşu başlayınca sönmez (eski değer `Brush.StatusQueued` idi).
    [StaTheory]
    [InlineData(ProjectRowState.Started, false, false, "Brush.Amber")]
    [InlineData(ProjectRowState.Succeeded, false, false, "Brush.StatusSuccess")]
    [InlineData(ProjectRowState.Failed, false, false, "Brush.StatusFail")]
    [InlineData(ProjectRowState.Skipped, false, false, "Brush.StatusSkippedBorder")]
    [InlineData(ProjectRowState.Pending, true, false, "Brush.StatusSkippedBorder")] // üyelik şeridi ezmez
    [InlineData(ProjectRowState.Pending, false, true, "Brush.Amber")]  // willBuild + run uçuşta → queued
    public void Status_stripe_uses_the_right_token_brush_per_status(
        ProjectRowState state, bool inCycle, bool queued, string expectedKey)
    {
        var vm = new ProjectRowViewModel("id", "Foo", state) { InCycle = inCycle };
        if (queued) { vm.WillBuild = true; vm.IsRunActive = true; }
        var (row, window, host) = Realize(vm);

        Assert.Equal(DsResources.TokenColor(host, expectedKey), DsResources.ColorOf(row.Stripe.Fill));
        GC.KeepAlive(window);
    }

    /// <summary>[DEĞİŞEN KURAL — design v1.7.0 §2.4] Eski iddia: "discovered satırın şeridi ŞEFFAFTIR".
    /// Şerit artık hiç kaybolmaz: workspace açıldığı andan itibaren gri durur ve skipped ile AYNI gridir.</summary>
    [StaFact]
    public void Discovered_stripe_is_the_same_grey_as_skipped()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending); // pending, no run, no cycle
        var (row, window, host) = Realize(vm);

        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSkippedBorder"), DsResources.ColorOf(row.Stripe.Fill));
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.12.0 §2.4-1 · §9-3] Başlangıç modunda şerit DÜZ bir token fırçasıyla dolar —
    /// kesikli desen YOKTUR. Renk iki durumda da AYNI nötr gridir — Sync bir plan göstermez.
    /// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Eski iddia: şerit başlangıç modunda KESİKLİ çizilir (3px dolu /
    /// 4px boş) ve desen, WPF'te bir dolgu kesikli olamadığı için tile'lanmış bir <c>DrawingBrush</c>'tır.
    /// Değişme gerekçesi (ölçüm): 2px'lik bir şeritte kesikli desen piksel ızgarasına oturmuyor, tırtıklı
    /// görünüyordu. Ayrım OPAKLIĞA taşındı ve dolgu her durumda tek bir token fırçası oldu.</para>
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> "Sync sonrası liste silik görünüyordu." O opaklık ayrımı
    /// da kaldırıldı: eski iddia şeridin başlangıç modunda <see cref="StartMode.FaintOpacity"/> (eski değeri
    /// 0.5) ile SOLUK çizildiğiydi. Artık başlangıç modu da TAM OPAK (1.0) — şerit hiçbir zaman
    /// soluklaşmaz.</para></summary>
    [StaFact]
    public void The_fresh_start_mode_draws_the_stripe_fully_opaque()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { Fresh = true };
        var (row, window, host) = Realize(vm);

        Assert.IsNotType<System.Windows.Media.DrawingBrush>(row.Stripe.Fill);   // kesikli desen YOK (v1.12.0)
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSkippedBorder"), DsResources.ColorOf(row.Stripe.Fill));
        Assert.Equal(1.0, row.Stripe.Opacity);                                  // v1.13.2: silik değil, TAM opak

        // İşlem başlayınca (fresh düşünce) opaklık zaten 1'di — değişmez, rengi de değişmez.
        vm.Fresh = false;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusSkippedBorder"), DsResources.ColorOf(row.Stripe.Fill));
        Assert.Equal(1.0, row.Stripe.Opacity);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.11.0 §2.4-6] Uyarı tooltip'i TEK SATIRDIR: ilk dep'in kısa adı + kalanların SAYISI.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski metin tüm adları <c>", "</c> ile listeliyor ve
    /// <c>" — last successful output referenced"</c> kuyruğunu taşıyordu. v1.11.0 tooltip'i kısalttı: tam liste
    /// ve gerekçe proje LOGUNDADIR. Ortak önek atma kuralı (D5) DEĞİŞMEDİ.</para></summary>
    [StaFact]
    public void Dep_tooltip_is_one_line_with_the_first_name_and_a_plus_count()
    {
        // [D5] Kısa-ad öneki VERİ-TÜREVLİ ve satıra RunViewModel'den itilir (NamePrefix) — hardcode "OSYS." yok.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Failed) { NamePrefix = "OSYS." };
        var (row, window, _) = Realize(vm);

        // Tek dep: ortak önek atılır, sayaç YOK.
        vm.DepIssues = new[] { "OSYS.Sales.Core" };
        row.UpdateLayout();
        Assert.Equal("Dependency issue: Sales.Core", row.DepTooltip);

        // Üç dep: ilk ad + "+2" (tam liste logda).
        vm.DepIssues = new[] { "OSYS.Sales.Core", "OSYS.Billing.Core", "OSYS.Base" };
        row.UpdateLayout();
        Assert.Equal("Dependency issue: Sales.Core +2", row.DepTooltip);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [cycle rounds/Task 9] döngü görselleri















    /// <summary>[review fix Minor] Dep-slot'ta gösterilecek hiçbir sinyal kalmayınca <c>PART_DepTip.Content</c>
    /// DEFANSİF olarak temizlenir — slot bugün sıfır yükseklikte çöktüğü için zararsız, ama slot'un layout'u
    /// yarın değişirse hayalet bir tooltip metni kalmasın.</summary>
    [StaFact]
    public void Dep_tooltip_is_defensively_cleared_once_the_dep_signal_disappears()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Failed)
        { DepIssues = ["OSYS.Sales.Core"], NamePrefix = "OSYS." };
        var (row, window, _) = Realize(vm);
        Assert.NotNull(row.DepTooltip); // ön-koşul: tooltip gerçekten dolu

        vm.DepIssues = null;
        row.UpdateLayout();
        Assert.Null(row.DepTooltip);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.11.0 §2.4-5 · §9-13] <b>Statü glyph'i TOOLTIP TAŞIMAZ.</b>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Burada dört test vardı ve hepsi glyph tooltip'ini pinliyordu: statü etiketi
    /// + building'de canlı süre (<c>Building — 5s</c>), <c>— dependency issue</c> eki, iki döngü eki ve
    /// aralarındaki öncelik. v1.11.0 tooltip'i tamamen kaldırdı — üçü de satırda ZATEN vardı (süre kolonu,
    /// uyarı üçgeni) ve aynı şey iki yerde okunuyordu. Listede tooltip taşıyan TEK öğe uyarı üçgenidir.</para>
    ///
    /// <para>Ekran okuyucu KAYBETMEZ: statü metni glyph'in UIA adına yazılır (eşleme
    /// <see cref="StatusGlyph.LabelFor"/> — kopya YASAK).</para></summary>
    [StaFact]
    public void The_status_glyph_has_no_tooltip_and_announces_its_status_through_the_automation_name()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started)
        { DurationMs = 5000, DepIssues = ["OSYS.Sales.Core"], NamePrefix = "OSYS." };
        var (row, window, _) = Realize(vm);

        Assert.Null(row.Glyph.ToolTip);
        Assert.Equal(StatusGlyph.LabelFor(GraphStatus.Building),
            System.Windows.Automation.AutomationProperties.GetName(row.Glyph));

        vm.State = ProjectRowState.Skipped;
        vm.CycleUnconverged = true;
        row.UpdateLayout();
        Assert.Null(row.Glyph.ToolTip);
        Assert.Equal(StatusGlyph.LabelFor(GraphStatus.Skipped),
            System.Windows.Automation.AutomationProperties.GetName(row.Glyph));
        // ...gerekçe uyarı üçgeninde, TEK satır.
        Assert.Equal(RowWarning.CycleUnconverged, row.DepTooltip);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_shows_the_current_half_alone_when_the_target_is_not_known()
    {
        // [DEĞİŞEN KURAL] Bu test eskiden yarım bir ok pinliyordu ("a3f81c2 → "): hedef bilinmezken cur yarısı
        // ok'la birlikte basılıyordu. Kardeş kural ise ters yöndeydi — sol yarı yokken ok ÜRETİLMEZ diyordu
        // (bkz. Sha_shows_the_target_alone_when_the_project_was_never_built) — yani iki eksik-yarı vakası
        // birbirinden farklı davranıyordu.
        //
        // Harici projelerle birlikte bu tutarsızlık görünür bir kusura döndü: harici satırlara ana reponun
        // hedef commit'i İTİLMEZ (o sha başka bir repoyu anlatır), dolayısıyla her harici satır kalıcı olarak
        // yarım bir ok gösterirdi. Kural artık simetrik: eksik olan hangi yarı olursa olsun, elde ne varsa o
        // basılır.
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true, CurrentSha = "a3f81c2" };
        var (row, window, _) = Realize(vm);

        Assert.Equal("a3f81c2", row.ShaText.Text);
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
        Assert.Equal("a3f81c2", row.ShaText.Text); // hedef henüz bilinmiyor → tek yarı (bkz. yukarıdaki DEĞİŞEN KURAL)

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

    // ================================================================ [A13/T3b] ölçü/geometri (b4–b7)

    /// <summary>[A13/T3b · b4] design-v1 README §2.3 slot 7: "Süre mono 12px sağa yaslı 46px" (BuildApp.jsx:414
    /// <c>minWidth 46</c>). Yalnız 118px'lik sağ blok sığması (Sha testi) pinliydi; süre kolonunun KENDİ
    /// MinWidth'i ve mono ailesi testsizdi.</summary>
    [StaFact]
    public void Duration_column_has_a_fortysix_pixel_minimum_width_and_uses_the_mono_family()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded) { DurationMs = 4200 };
        var (row, window, _) = Realize(vm);

        Assert.Equal(46.0, row.DurationText.MinWidth);
        Assert.Same(AppFonts.Mono, row.DurationText.FontFamily); // makinenin ürettiği değer → HER ZAMAN mono

        // Realize zorunlu (kural 5): kısa içerikle (46'yı doldurmayan "4.2s") GERÇEK arrange yine de MinWidth'i
        // uygular — yalnız XAML literalini okumak bunu kanıtlamaz.
        Assert.Equal("4.2s", row.DurationText.Text); // ön-koşul: içerik gerçekten kısa
        Assert.Equal(46.0, row.DurationText.ActualWidth);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3b · b5] design-v1 README §2.3 slot 5: "Statü glyph'i 14px" (BuildApp.jsx:405
    /// <c>size={14}</c>). Yalnız dep-slot'un KENDİ 14px'i (DepSlot) pinliydi; glyph'in KENDİ boyutu
    /// testsizdi.</summary>
    [StaFact]
    public void Status_glyph_is_fourteen_pixels_both_as_a_property_and_after_real_layout()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var (row, window, _) = Realize(vm);

        Assert.Equal(14.0, row.Glyph.Size);
        // Realize zorunlu (kural 5): Size, DS StatusGlyph şablonundaki Grid/Viewbox Width/Height'e
        // TemplateBinding ile akar (Controls.xaml:781/789) — ActualWidth/Height GERÇEKTEN bu değeri taşıyor mu.
        Assert.Equal(14.0, row.Glyph.ActualWidth);
        Assert.Equal(14.0, row.Glyph.ActualHeight);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3b · b6] design-v1 README §2.3 slot 6: sabit 14px SLOT içindeki dep-hata üçgeni "12px"
    /// (aynı ölçek Icon.AlertTri kullanımı, ProjectRow.xaml:90). Slot'un KENDİ 14px'i zaten pinliydi
    /// (<c>Dep_issue_slot_is_fourteen_pixels_...</c>); İÇİNDEKİ üçgen ikonun KENDİ boyutu testsizdi.</summary>
    [StaFact]
    public void Dep_issue_triangle_icon_is_twelve_by_twelve_pixels_when_visible()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded) { DepIssues = ["OSYS.Sales.Core"] };
        var (row, window, _) = Realize(vm);

        Assert.Equal(Visibility.Visible, row.DepIcon.Visibility); // ön-koşul: ikon gerçekten görünür
        Assert.Equal(12.0, row.DepIcon.Width);
        Assert.Equal(12.0, row.DepIcon.Height);
        Assert.Equal(12.0, row.DepIcon.ActualWidth);   // realize zorunlu — Collapsed'ken 0 olurdu
        Assert.Equal(12.0, row.DepIcon.ActualHeight);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3b · b7] design-v1 README §2.3 slot 4: "mono 10.5px" (BuildApp.jsx:399 <c>fontSize: 10.5</c>).
    /// 118px'e sığma ZATEN pinliydi (yukarıdaki test); PUNTONUN KENDİSİ (10.5 — token ölçeğinde YOK, kasıtlı
    /// literal, ProjectRow.xaml:75 yorumu) testsizdi.</summary>
    [StaFact]
    public void Sha_pairs_font_size_is_the_deliberate_ten_point_five_literal_not_a_token_size()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending)
        { WillBuild = true, CurrentSha = "a3f81c2" };
        var (row, window, _) = Realize(vm);

        Assert.Equal(10.5, row.ShaText.FontSize);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [cycles] döngü rozeti koşu-sonrası







    // [KALDIRILDI — design v1.7.0 §2.4] Uyarı slotu TEK üçgene indi: ayrı bir "cycle rozeti" ve onunla
    // üçgen arasındaki öncelik dansı kalktı. Yeni kural — slotta tek üçgen vardır, rengi en ağır nedeni
    // söyler (döngü üyesi turuncu, yalnız dep-issue amber), tooltip nedenleri alt alta listeler ve satır
    // building iken slot gizlidir.

    /// <summary>[design v1.11.0 §2.4-6] Sıradan döngü üyeliği de TEK satırlık uyarı tooltip'inde duyurulur.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: "glyph tooltip'i de üyeliği duyurur (<c>Skipped — In a
    /// dependency cycle</c>)" — gerekçesi dep-slot'un boşken sıfır yükseklikte çökmesiydi. Glyph tooltip'i
    /// kalktı; slot ise burada BOŞ DEĞİLDİR (üyelik tam da onu doldurur), yani duyuru kaybolmadı, tek yere
    /// indi.</para></summary>
    [StaFact]
    public void Plain_cycle_membership_is_announced_by_the_warning_triangle_alone()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Skipped) { InCycle = true };
        var (row, window, _) = Realize(vm);

        Assert.Equal(Visibility.Visible, row.DepIcon.Visibility);
        Assert.Equal(RowWarning.InCycle, row.DepTooltip);
        Assert.Null(row.Glyph.ToolTip);
        GC.KeepAlive(window);
    }
}
