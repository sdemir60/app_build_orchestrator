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
/// [design v1.23.0 §2.1 · §2.12 · §9 "Uygulama sayıları — güncelleme"] Title bar'daki güncelleme hapı. Hap yalnız
/// kuruluma hazır (indirilmiş) bir teklif varken görünür (tasarım §2.12): uygulama teklifsiz açılır, teklif sonradan
/// gelince giriş animasyonu bir kez oynar. Görünür hapı sınayan testler teklifi fixture'dan alır
/// (<see cref="MainWindowHost.NewRealizedWithOffer"/>).
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
    /// <summary>Sağ kümenin kendisi — gear'ın ebeveyni (AboutWiringTests ile aynı seçici).</summary>
    private static Panel RightCluster(MainWindow window) => (Panel)LogicalTreeHelper.GetParent(window.GearButton);

    /// <summary>Bir butonun sağ kenarının pencerenin sağ kenarına uzaklığı — kümenin sağa yaslı kaldığının ölçüsü.</summary>
    private static double RightInset(MainWindow window, FrameworkElement button) =>
        window.RootShell.ActualWidth - (button.TranslatePoint(new Point(0, 0), window.RootShell).X + button.ActualWidth);

    private static FrameworkElement[] ExistingButtons(MainWindow window) =>
        [window.LayQuadButton, window.LayListButton, window.LayFocusButton, window.GearButton, window.NotesButton, window.InfoButton];

    /// <summary>Ölçüm testlerinin koştuğu ekran ölçekleri (Windows'un %100–%200 adımları): kabuk her birinde ölçülür
    /// (<see cref="MainWindowHost.NewRealizedWithOffer"/>), makinenin kendi ölçeği sonucu değiştirmez.</summary>
    public static TheoryData<double> DisplayScales => [1.0, 1.25, 1.5, 1.75, 2.0];

    // ---------------------------------------------------------------- yer

    /// <summary>Hap kümenin İLK öğesidir, ardından ayracı gelir; mevcut altı buton aynı sırada arkasında durur.</summary>
    [StaFact]
    public void The_pill_and_its_separator_open_the_right_cluster()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

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
    /// AYNIDIR — hap boş alana (sola) doğru büyür. Her ekran ölçeğinde (<see cref="DisplayScales"/>), aynı pencerede ölçülür.
    /// <para><b>[DEĞİŞEN ÖLÇÜM — temizlik madde 1]</b> Eski iddia iki listenin double eşitliğiydi (<c>Assert.Equal(withoutPill, withPill)</c>)
    /// ve yalnız makinenin ölçeğinde koşardı: %125/%150 ekranlarda her tam süitte kırmızı, CI'da (%100) yeşil. Ölçüm (zorlanan ölçek, pencere
    /// Maximized, 1400 dip): 125%'te LayFocus'un sağ boşluğu hapla <c>231,99999999999977</c>, hapsız <c>232</c>; 150%'de gear, What's new ve
    /// About'unki hapla <c>197,33333333333326</c> · <c>175,33333333333326</c> · <c>153,33333333333326</c>, hapsız aynı sayıların <c>…348</c> ile
    /// biten hâli. Fark 1e-13 mertebesindedir: konumlar cihaz pikseli ızgarasındadır ve iki
    /// ayrı toplamın double artığı kayma değildir. Konumlar artık ızgarada karşılaştırılır (<see cref="DsResources.InDevicePixels"/>): gerçek
    /// bir kayma en az bir cihaz pikselidir ve yakalanır (mutasyon: hap görünürken sağ kümeye 1 dip sağ boşluk — her ölçekte kırmızı).</para></summary>
    [StaTheory]
    [MemberData(nameof(DisplayScales))]
    public void The_existing_title_bar_buttons_do_not_move_when_the_pill_appears(double scale)
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealizedWithOffer(temp, new DpiScale(scale, scale));
        Assert.Equal(Visibility.Visible, window.UpdatePillSlot.Visibility); // ön-koşul: hap görünür
        Assert.Equal(scale, VisualTreeHelper.GetDpi(window.InfoButton).DpiScaleX); // ön-koşul: ölçek düğmelere indi
        var withPill = ExistingButtons(window).Select(b => RightInset(window, b)).ToList();

        window.UpdatePillSlot.Visibility = Visibility.Collapsed;
        window.RootShell.UpdateLayout();
        var withoutPill = ExistingButtons(window).Select(b => RightInset(window, b)).ToList();

        var moved = ExistingButtons(window)
            .Select((button, i) => (button.Name, Pixels: DsResources.InDevicePixels(withPill[i] - withoutPill[i], scale)))
            .Where(m => m.Pixels != 0)
            .ToList();
        Assert.True(moved.Count == 0, $"hap gelince kayan düğmeler (ölçek {scale}, cihaz pikseli): {string.Join(", ", moved)}");
        Assert.True(window.UpdatePill.ActualWidth > 0, "hap hiç yer kaplamadı — ölçüm vakum");
        GC.KeepAlive(window);
    }

    /// <summary>Hapın ayracı mevcut ayraçla TEK stildir (literal ikinci kez yazılmaz): 1×14, <c>border</c>, yatay 5px.</summary>
    [StaFact]
    public void The_pill_separator_shares_the_one_title_bar_separator_style()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

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

    /// <summary>Hap tasarımın sayılarıyla çizilir — 22 yükseklik, ikon → etiket → sürüm arası 6 — her ekran ölçeğinde (<see cref="DisplayScales"/>).
    /// <para><b>[DEĞİŞEN ÖLÇÜM — temizlik madde 1]</b> Eski iddialar ölçülen sayıları dip cinsinden ±0,0005 ile beklerdi
    /// (<c>Assert.Equal(22.0, pill.ActualHeight, precision: 3)</c>, boşluklar metnin kenarından kenarına) ve yalnız makinenin ölçeğinde koşardı:
    /// %125/%150 ekranlarda kırmızı, CI'da (%100) yeşil. <c>UseLayoutRounding</c> her boyu ve ofseti cihaz pikseline oturtur; ölçüm (zorlanan
    /// ölçek; yükseklik · ikon→etiket · etiket→sürüm, dip):</para>
    /// <list type="bullet">
    /// <item>100% · 150% · 200%: 22 · 6 · 6 — ölçek tasarım sayılarını tam piksele çevirir.</item>
    /// <item>125%: 22,4 (27,5 → 28 px) · 6,4 · 6,4.</item>
    /// <item>175%: 21,714 (38,5 → 38 px) · 6,286 · 5,143 — etiket→sürüm kenardan kenara 9 px: WPF etiketin 6'lık sol boşluğunu ölçümde 10,
    /// yerleşimde 11 px sayar ve metin kendi yerleşim yuvasından 1 px taşar.</item>
    /// </list>
    /// <para>Yeni ölçüm: her sayı TEK yuvarlama adımıyla ölçülür ve tasarım sayısından en çok YARIM cihaz pikseli sapabilir — pay sabit bir dip
    /// değil, ölçekten gelir (<see cref="DsResources.InDevicePixels"/>). Boşluk, sonraki öğenin sol kenarından ÖNCEKİ öğenin yerleşim yuvasının
    /// sağına ölçülür (StackPanel yuvaları bitişiktir); metnin kendi kenarına ölçmek yukarıdaki taşmayı katardı. 1 dip'lik gerçek bir sapma bu
    /// payı her ölçekte aşar (100%'de 1 piksel, üstünde en az ölçek − 0,5 piksel) — mutasyonla doğrulandı: yükseklik 23, etiketin ya da sürümün
    /// boşluğu 7, sol padding 7 her ölçekte kırmızı. Özellik iddiaları (padding, kenar, radius, renk, yazı) ölçekten bağımsızdır, aynen kalır.</para>
    /// <para>Yuvadan ölçülen boşluk yalnız SONRAKİ öğenin sol boşluğunu görür; önceki öğenin SAĞ boşluğu yuvanın içinde kalır ve ölçüme girmez
    /// (bütün-dal incelemesi: ikona <c>0,0,1,0</c> verilince test yeşil kalıyordu). Bu yüzden üç öğenin Margin'i ayrıca tam değeriyle pinlenir —
    /// <c>ResourceStripRealizeTests</c>'teki "tasarım sayıları kendileri + ölçülen boşluk" deseni; ikonun ya da etiketin sağına 1 eklemek her
    /// ölçekte kırmızı.</para></summary>
    [StaTheory]
    [MemberData(nameof(DisplayScales))]
    public void The_pill_is_drawn_to_the_design_numbers(double scale)
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealizedWithOffer(temp, new DpiScale(scale, scale));
        var pill = window.UpdatePill;

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

        // Tasarım sayıları KENDİLERİ (yuvarlamasız, ölçekten bağımsız): ölçülen boşluk önceki öğenin sağ boşluğunu görmez (özet).
        Assert.Equal(new Thickness(0), icon.Margin);
        Assert.Equal(new Thickness(6, 0, 0, 0), label.Margin);
        Assert.Equal(new Thickness(6, 0, 0, 0), version.Margin);

        // Ölçülen sayılar: tek yuvarlama adımı, yarım cihaz pikseli pay (özetteki ölçüm). Ön-koşullar: ölçek hapın bütün görsellerine —
        // şablondan sonradan doğanlar dahil — indi ve üç öğe aynı satırın çocuğudur (yuva o satırın koordinatındadır).
        Assert.All(DsResources.Descendants(pill).OfType<Visual>(), v => Assert.Equal(scale, VisualTreeHelper.GetDpi(v).DpiScaleX));
        var row = (Panel)VisualTreeHelper.GetParent(label);
        Assert.Same(row, VisualTreeHelper.GetParent(icon));
        Assert.Same(row, VisualTreeHelper.GetParent(version));
        double GapAfter(FrameworkElement previous, FrameworkElement next) =>
            next.TranslatePoint(new Point(0, 0), row).X - LayoutInformation.GetLayoutSlot(previous).Right;
        void AssertDesignNumber(double design, double measured, string what)
        {
            double off = DsResources.InDevicePixels(measured - design, scale);
            Assert.True(off <= 0.5, $"{what}: {measured:0.###} dip, tasarım {design} — {off} cihaz pikseli sapma (ölçek {scale}, pay 0,5)");
        }
        AssertDesignNumber(22, pill.ActualHeight, "hap yüksekliği");
        AssertDesignNumber(6, GapAfter(icon, label), "ikon → etiket");
        AssertDesignNumber(6, GapAfter(label, version), "etiket → sürüm");
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(pill));
        GC.KeepAlive(window);
    }

    /// <summary>Kart açıkken hap yükselir: <c>surface-overlay</c> + <c>neutral-500</c>, sürüm <c>text-secondary</c>.
    /// Hover AYNI görünümü verir — stilin iki tetikleyicisi aynı üç değeri yazar (fare headless sürülemez).</summary>
    [StaFact]
    public void The_open_pill_lifts_to_the_overlay_surface_and_hover_looks_the_same()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealizedWithOffer(temp);
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
        var (window, vm) = MainWindowHost.NewRealizedWithOffer(temp);
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

    /// <summary>Uygulama teklifsiz açılır: hap (ve ayracı) ilk karede YOKTUR ve hiçbir giriş oynamaz (animasyonlar
    /// açıkken bile).
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: "örnek teklif açılışta hazırdır, hap ilk karede görünür" — motor yokken
    /// tasarımın aktarımı için VM açılışta placeholder bir teklif (<c>UpdateOffer.Sample</c>) taşıyordu. Gerekçe: motor
    /// geldi; hap yalnız indirilmiş, kuruluma hazır bir teklif varken görünür (tasarım §2.12 ilkesi), teklifi motor yazar.
    /// Placeholder kalktı.</para></summary>
    [StaFact]
    public void Without_an_offer_the_pill_is_hidden_from_the_first_frame()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        var slot = window.UpdatePillSlot;

        Assert.Null(vm.AvailableUpdate);
        Assert.Equal(Visibility.Collapsed, slot.Visibility);
        Assert.False(slot.HasAnimatedProperties);
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
        var (window, vm) = MainWindowHost.NewRealized(temp);
        var slot = window.UpdatePillSlot;

        Assert.Null(vm.AvailableUpdate); // ön-koşul: uygulama teklifsiz açılır
        Assert.Equal(Visibility.Collapsed, slot.Visibility);

        vm.AvailableUpdate = UpdateOffers.Sample("9.8.0");
        Assert.Equal(Visibility.Visible, slot.Visibility);
        var entrance = Assert.IsType<TranslateTransform>(slot.RenderTransform);
        Assert.Equal(-4.0, entrance.Y);
        Assert.True(slot.HasAnimatedProperties, "giriş oynamadı");

        vm.AvailableUpdate = UpdateOffers.Sample("9.9.0");
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
