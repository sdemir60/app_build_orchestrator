using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.17.0 §9 "Alt barda tek hover dili" — Task 4] Bardaki kontroller ÜÇ farklı hover davranışı
/// gösteriyordu (Sync zemin açar, bakım ikonları saydamdan surface-raised'a geçer, çipler zemin açar ama
/// kenar/metni sabit tutar, segment hiç yanıt vermez). Tek kural BasedOn zinciriyle paylaşılan Ds.Chip/
/// Ds.Button.Secondary/Ds.IconButton/Ds.Segment.Item'ın ÜSTÜNE eklenir (<c>Ds.Bar.*</c>) — paylaşılan stiller
/// DEĞİŞMEZ (ShellRoot filtre chip'i, satır ikonları, dialog'lar onlardan etkilenmemeli).
///
/// <para>Headless'ta gerçek mouse hover simüle edilemez (<c>SelectStyleTests</c>/<c>ProjectRowInputTests</c>
/// deseni) — bu yüzden hover HEDEFLERİ ÇOĞU zaman stil trigger'ları ÜZERİNDEN okunur (tetikleyicinin varlığı
/// GERÇEK setter'larıyla kanıtlanır). [fix round 1] İstisna: <see cref="DsChrome.IsHoverProxyProperty"/>'nin
/// kendisi GERÇEK bir routed MouseEnter/MouseLeave ile sürülebildiğinden (kaynağı disabled düğmenin KENDİSİ
/// değil, her zaman etkin sarmalayıcı <c>Border</c>'dır), o mekanizma CANLI bir realize testiyle de doğrulanır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ActionBarHoverTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (ActionBar bar, Window window) RealizeBar(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        return (bar, DsResources.Realize(host, bar));
    }

    private static (MaintenanceBox box, Window window) RealizeBox(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var box = new MaintenanceBox { DataContext = vm };
        return (box, DsResources.Realize(host, box));
    }

    // ---------------------------------------------------------------- helpers: stil trigger okuma

    /// <summary>Bir <see cref="Style"/>'ın (BasedOn zincirinde) TAM OLARAK <paramref name="conditions"/>'ın
    /// taşıdığı koşul kümesiyle eşleşen (ne eksik ne fazla) İLK <see cref="MultiTrigger"/>'ı — yoksa fırlatır.
    /// Koşul SAYISI da örtük olarak eşleşme kriteri: bir stilin İKİ farklı MultiTrigger'ı aynı koşul ALT
    /// kümesini paylaşabilir (ör. Ds.Bar.Chip'in nötr [IsMouseOver,IsEnabled,IsChecked=False] ve aktif+hover
    /// [IsMouseOver,IsEnabled,IsChecked=True] tetikleyicileri), tam eşleşme bu ikisini AYIRT eder.</summary>
    private static MultiTrigger MultiTriggerWhere(Style style, params (DependencyProperty Property, object Value)[] conditions)
    {
        for (var s = style; s is not null; s = s.BasedOn)
            foreach (var mt in s.Triggers.OfType<MultiTrigger>())
            {
                var actual = mt.Conditions.OfType<Condition>().ToList();
                if (actual.Count != conditions.Length) continue;
                if (conditions.All(hc => actual.Any(c => c.Property == hc.Property && Equals(c.Value, hc.Value))))
                    return mt;
            }
        throw new InvalidOperationException(
            $"{style.TargetType?.Name} stilinde tam olarak [{string.Join(", ", conditions.Select(c => $"{c.Property.Name}={c.Value}"))}] " +
            "koşullarını taşıyan bir MultiTrigger yok.");
    }

    /// <summary>Tek koşullu (<see cref="Trigger"/>) — <see cref="DsChrome.IsActiveProperty"/>'nin resting
    /// (hover'sız) hâli gibi.</summary>
    private static Trigger TriggerWhere(Style style, DependencyProperty property, object value)
    {
        for (var s = style; s is not null; s = s.BasedOn)
            foreach (var t in s.Triggers.OfType<Trigger>())
                if (t.Property == property && Equals(t.Value, value))
                    return t;
        throw new InvalidOperationException($"{style.TargetType?.Name} stilinde {property.Name}={value} tetikleyicisi yok.");
    }

    private static object? ResourceKeyOf(Setter setter) =>
        (setter.Value as DynamicResourceExtension)?.ResourceKey ?? setter.Value;

    private static Color ColorOf(FrameworkElement host, Setter setter) =>
        DsResources.TokenColor(host, (string)ResourceKeyOf(setter)!);

    /// <summary>
    /// [fix round 1 · I-1] WPF'in GERÇEK trigger BİRLEŞTİRME sırasını simüle eder — <c>BasedOn</c> zincirinde
    /// BASE ÖNCE, en TÜRETİLMİŞ EN SONRA değerlendirilir (bir stilin KENDİ <c>Triggers</c>'ı da yazıldığı
    /// sırada) — ve verilen durum (hangi DP'nin hangi değeri taşıdığı) için <paramref name="property"/>'yi EN
    /// SON yazan (yani KAZANAN) Setter'ın kaynak anahtarını döner. "Bir tetikleyici var mı" sorusu DEĞİL,
    /// "WPF gerçekte HANGİ DEĞERİ uygular" sorusunu cevaplar — I-1'in ta kendisi budur: eski kodda İKİ
    /// tetikleyici de (nötr + checked) aynı anda eşleşiyordu ve SONRA gelen (nötr, çünkü derived) kazanıyordu.
    /// </summary>
    private static object? ResolveViaMergedTriggers(Style style, DependencyProperty property, IReadOnlyDictionary<DependencyProperty, object> state)
    {
        var chain = new List<Style>();
        for (var s = style; s is not null; s = s.BasedOn) chain.Add(s);
        chain.Reverse(); // en tabandaki (base) ÖNCE değerlendirilir, WPF'in BasedOn önceliğiyle AYNI sıra.

        object? winner = null;
        foreach (var s in chain)
            foreach (var trigger in s.Triggers)
            {
                bool matches;
                IEnumerable<Setter> setters;
                switch (trigger)
                {
                    case Trigger t:
                        matches = state.TryGetValue(t.Property, out var v) && Equals(v, t.Value);
                        setters = t.Setters.OfType<Setter>();
                        break;
                    case MultiTrigger mt:
                        matches = mt.Conditions.OfType<Condition>()
                            .All(c => state.TryGetValue(c.Property, out var cv) && Equals(cv, c.Value));
                        setters = mt.Setters.OfType<Setter>();
                        break;
                    default:
                        continue;
                }
                if (!matches) continue;
                foreach (var setter in setters)
                    if (setter.Property == property)
                        winner = ResourceKeyOf(setter);
            }
        return winner;
    }

    // ---------------------------------------------------------------- nötr hover: zemin/kenar/metin BİRLİKTE

    /// <summary>[hover tablosu satır 1] "N behind" (<c>Ds.Bar.Chip.Action</c>) ve Sync (<c>Ds.Bar.Button.
    /// Secondary.Sm</c>) — IsChecked'i OLMAYAN kontroller — hover'da neutral-700 zemin + neutral-500 kenar +
    /// text-primary metin/ikon BİRLİKTE değişir. İkisi de AYNI üç setter'ı taşıdığı için TEK yardımcıyla
    /// doğrulanır (kopya YASAK). Σ/sayaç/branch/worktree/perf'in stili (<c>Ds.Bar.Chip</c>) AYRI test edilir
    /// (aşağıda) — onun nötr tetikleyicisi ÜÇÜNCÜ bir koşul (IsChecked=False) taşır (fix round 1 · I-1).</summary>
    [StaFact]
    public void Chip_action_and_secondary_button_bar_styles_resolve_the_same_neutral_hover_surface()
    {
        var host = DsResources.NewHost();
        foreach (string key in new[] { "Ds.Bar.Chip.Action", "Ds.Bar.Button.Secondary.Sm" })
        {
            var style = (Style)host.FindResource(key);
            var mt = MultiTriggerWhere(style, (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true));
            var setters = mt.Setters.OfType<Setter>().ToList();

            var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
            var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
            var border = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);

            Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), ColorOf(host, bg));
            Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, fg));
            Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral500"), ColorOf(host, border));
        }
    }

    /// <summary>[hover tablosu satır 1] <c>Ds.Bar.Chip</c> (Σ/sayaç çipleri, branch/worktree/perf) — AYNI üç
    /// setter, ama nötr tetikleyici ÜÇÜNCÜ bir koşul taşır: <c>IsChecked=False</c> (fix round 1 · I-1 — bkz.
    /// <see cref="A_checked_and_hovered_chip_resolves_its_foreground_to_amber_text_not_text_primary"/>).</summary>
    [StaFact]
    public void The_chip_bar_style_resolves_the_same_neutral_hover_surface_when_unchecked()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Chip");
        var mt = MultiTriggerWhere(style,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, false));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
        var border = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);

        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, fg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral500"), ColorOf(host, border));
    }

    /// <summary>[hover tablosu satır 1 — nota göre] Bakım kutusunun üç ikon butonu (<c>Ds.Bar.IconButton</c>)
    /// KENDİ kenarı yoktur (kutu ortak çerçeveyi taşır) → hover yalnız zemin + ikon; bu yüzden bu stilin nötr
    /// hover'ında bir <c>AnimatedBorderBrush</c> setter'ı YOKTUR (Ds.Bar.Chip'in aksine — kasıtlı fark).</summary>
    [StaFact]
    public void The_maintenance_icon_button_bar_style_hovers_with_surface_and_icon_only_no_border()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.IconButton");
        var mt = MultiTriggerWhere(style, (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, fg));
        Assert.DoesNotContain(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);
    }

    /// <summary>Disabled kontrol nötr hover ALMAZ: her nötr-hover MultiTrigger'ı <c>IsEnabled=True</c> ŞARTI
    /// TAŞIR (brief notu: "Base zaten opaklık .45 — hover setter'ı IsEnabled=False'ta etkisiz olmalı:
    /// MultiTrigger IsMouseOver+IsEnabled").</summary>
    [StaFact]
    public void Neutral_hover_multitriggers_require_is_enabled_true()
    {
        var host = DsResources.NewHost();

        var chipStyle = (Style)host.FindResource("Ds.Bar.Chip");
        var chipMt = MultiTriggerWhere(chipStyle,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, false));
        Assert.Contains(chipMt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty && Equals(c.Value, true));

        foreach (string key in new[] { "Ds.Bar.Chip.Action", "Ds.Bar.Button.Secondary.Sm", "Ds.Bar.IconButton" })
        {
            var style = (Style)host.FindResource(key);
            var mt = MultiTriggerWhere(style, (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true));
            Assert.Contains(mt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty && Equals(c.Value, true));
        }
    }

    // ---------------------------------------------------------------- açık/aktif kontrol: amber-soft-hover + amber kenar

    /// <summary>[hover tablosu satır 2] Açık/aktif kontrol (IsChecked filtre çipi, açık branch/worktree popover
    /// chip'i) hover'da amber-soft-hover zemin + amber kenar alır; METİN <c>amber-text</c> SABİT kalır —
    /// tetikleyici Foreground'u hiç YAZMAZ.</summary>
    [StaFact]
    public void The_checked_chip_hover_multitrigger_turns_amber_soft_hover_with_an_amber_border_and_leaves_text_alone()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Chip");
        var mt = MultiTriggerWhere(style,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, true));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var border = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoftHover"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), ColorOf(host, border));
        // metin amber-text SABİT: bu tetikleyici Foreground'a HİÇ dokunmaz.
        Assert.DoesNotContain(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
    }

    /// <summary>
    /// [fix round 1 · I-1 — MANDATED test] Review bulgusu: eski kodda <c>Ds.Bar.Chip</c>'in nötr-hover
    /// MultiTrigger'ı yalnız <c>[IsMouseOver, IsEnabled]</c> istiyordu — İŞARETLİ bir chip hover alınca da
    /// eşleşiyordu, ve BasedOn zincirinde DERİVE edilen tetikleyiciler BASE'in (Ds.Chip) IsChecked=True
    /// tetikleyicisinden SONRA değerlendirildiği için o nötr tetikleyicinin Foreground=text-primary setter'ı
    /// KAZANIYORDU — açık bir filtre çipi hover alınca metni amber-text'ten BEYAZA döndürüyordu (spec: "metin
    /// amber-text SABİT kalır"). Bu test WPF'in GERÇEK birleştirme sırasını simüle eder
    /// (<see cref="ResolveViaMergedTriggers"/>) ve IsMouseOver=IsEnabled=IsChecked=true durumunda Foreground'un
    /// GERÇEKTE neye çözüldüğünü sorar — yalnız "bir tetikleyici var mı" değil.
    /// </summary>
    [StaFact]
    public void A_checked_and_hovered_chip_resolves_its_foreground_to_amber_text_not_text_primary()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Chip");
        var state = new Dictionary<DependencyProperty, object>
        {
            [UIElement.IsMouseOverProperty] = true,
            [Control.IsEnabledProperty] = true,
            [ToggleButton.IsCheckedProperty] = true,
        };

        var resolvedForeground = ResolveViaMergedTriggers(style, DsTransition.AnimatedForegroundProperty, state);

        Assert.Equal("Brush.AmberText", resolvedForeground);
    }

    /// <summary>Koşan bakım görevi/Resolve düğmesi (<c>DsChrome.IsActive</c>) de AYNI dili konuşur — rest'te
    /// amber-soft, hover'da amber-soft-hover.
    ///
    /// <para><b>[fix round 1 · I-2 — DEĞİŞEN KURAL]</b> Eski iddia hover eşleniğinin <c>IsMouseOver</c> okuduğunu
    /// ve yalnız <c>IsEnabled</c> ŞARTI ARAMADIĞINI söylüyordu — ama düğme TAM DA bu pencerede (komut kapısı
    /// kapalı) GERÇEKTEN disabled'dır ve WPF disabled bir öğeyi hit-test'ten TAMAMEN dışlar (aynı gerekçeyle
    /// <c>ToolTipService.ShowOnDisabled</c> vardır), yani <c>IsMouseOver</c> gerçek fareyle asla true olmazdı —
    /// tetikleyici ERİŞİLEMEZDİ. Artık <see cref="DsChrome.IsHoverProxyProperty"/> okur: HER ZAMAN etkin bir
    /// sarmalayıcı Border'ın MouseEnter/Leave'i bunu sürer (canlı kanıt aşağıda,
    /// <see cref="The_maintenance_icon_answers_hover_through_its_wrapper_while_genuinely_disabled"/>).</para>
    /// </summary>
    [StaFact]
    public void The_active_maintenance_icon_hover_multitrigger_reads_hover_proxy_not_mouse_over()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.IconButton");

        var resting = TriggerWhere(style, DsChrome.IsActiveProperty, true);
        var restingBg = Assert.Single(resting.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoft"), ColorOf(host, restingBg));

        var hoverMt = MultiTriggerWhere(style, (DsChrome.IsActiveProperty, true), (DsChrome.IsHoverProxyProperty, true));
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty);
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == UIElement.IsMouseOverProperty);
        var hoverBg = Assert.Single(hoverMt.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoftHover"), ColorOf(host, hoverBg));
    }

    /// <summary>Aynı istisna Sync düğmesi için de geçerlidir — [kullanıcı kararı 2026-09-12] koşan Sync bu
    /// bar'ın "açık/aktif kontrol"üdür; etiket ("Sync") amber'a DÖNMEZ (kimliğini korur) — bu yüzden hiçbir
    /// IsActive tetikleyicisi Foreground'u YAZMAZ. [fix round 1 · I-2] bkz. yukarıdaki bakım ikonu notu —
    /// AYNI gerekçe: Sync de o an gerçekten disabled'dır, hover eşleniği IsHoverProxy okur.</summary>
    [StaFact]
    public void The_active_sync_hover_multitrigger_reads_hover_proxy_and_never_writes_foreground()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Button.Secondary.Sm");

        var resting = TriggerWhere(style, DsChrome.IsActiveProperty, true);
        Assert.DoesNotContain(resting.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);

        var hoverMt = MultiTriggerWhere(style, (DsChrome.IsActiveProperty, true), (DsChrome.IsHoverProxyProperty, true));
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty);
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == UIElement.IsMouseOverProperty);
        Assert.DoesNotContain(hoverMt.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);
    }

    // ---------------------------------------------------------------- [fix round 1 · I-2] canlı erişilebilirlik kanıtı

    /// <summary>
    /// [fix round 1 · I-2 — MANDATED test] Review bulgusu: koşan Sync'in aktif+hover yüzeyi GERÇEKTE hiç
    /// ERİŞİLEMEZDİ, çünkü düğme tam o pencerede disabled'dır ve WPF disabled bir öğeyi hit-test'ten dışlar.
    /// Bu test GERÇEK bir üretim yolundan (SyncStartedEvent) düğmeyi disabled+aktif duruma getirir, sonra onu
    /// saran (ActionBar ctor'unda <c>DsChrome.WireHoverProxy</c> ile kablolu) Border üzerinde GERÇEK bir routed
    /// <c>MouseEnter</c>/<c>MouseLeave</c> yükseltir — düğmenin KENDİSİNDE DEĞİL, sarmalayıcıda — ve düğmenin
    /// GERÇEKTEN disabled kaldığını (ön-koşul) ve buna RAĞMEN hover yüzeyinin amber-soft-hover'a döndüğünü
    /// doğrular. Bu, mekanizmanın UÇTAN UCA (wiring + stil) çalıştığının TEK canlı kanıtıdır.
    /// </summary>
    [StaFact]
    public void The_sync_button_answers_hover_through_its_wrapper_while_genuinely_disabled()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.False(bar.SyncButton.IsEnabled);          // ön-koşul: GERÇEKTEN disabled (komut kapısı kapalı)
        Assert.True(DsChrome.GetIsActive(bar.SyncButton)); // ön-koşul: gerçekten aktif/koşan

        var wrapper = Assert.IsType<Border>(bar.SyncButton.Parent);
        wrapper.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

        Assert.True(DsChrome.GetIsHoverProxy(bar.SyncButton));
        Assert.Equal(DsResources.TokenColor(bar, "Brush.AmberSoftHover"),
            DsResources.ColorOf(DsTransition.GetAnimatedBackground(bar.SyncButton)));
        Assert.Equal(DsResources.TokenColor(bar, "Brush.Amber"),
            DsResources.ColorOf(DsTransition.GetAnimatedBorderBrush(bar.SyncButton)));

        wrapper.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.False(DsChrome.GetIsHoverProxy(bar.SyncButton));
        GC.KeepAlive(window);
    }

    /// <summary>Aynı kanıt <see cref="MaintenanceBox"/>'ın Clean düğmesi için — AYRI bir kablolama çağrısı
    /// (<c>MaintenanceBox.Build</c>), AYRI bir testte doğrulanır.</summary>
    [StaFact]
    public void The_maintenance_icon_answers_hover_through_its_wrapper_while_genuinely_disabled()
    {
        var vm = NewVm();
        var (box, window) = RealizeBox(vm);

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        Assert.False(box.CleanButton.IsEnabled);
        Assert.True(DsChrome.GetIsActive(box.CleanButton));

        var wrapper = Assert.IsType<Border>(box.CleanButton.Parent);
        wrapper.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

        Assert.True(DsChrome.GetIsHoverProxy(box.CleanButton));
        Assert.Equal(DsResources.TokenColor(box, "Brush.AmberSoftHover"),
            DsResources.ColorOf(DsTransition.GetAnimatedBackground(box.CleanButton)));

        wrapper.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.False(DsChrome.GetIsHoverProxy(box.CleanButton));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [fix round 1 · I-3] Σ ikonu hover'da beyazlar

    /// <summary>
    /// [fix round 1 · I-3 — MANDATED test] Review bulgusu: hover tablosu Σ'yı "text-primary (ikon dâhil)"
    /// nötr grubuna sokuyor ve prototip (BuildApp.jsx:58, _ds_bundle Chip icon span) ikonu hover'da beyazlatıyor
    /// — önceki round Σ'nin ikonunu TAMAMEN sabit bıraktı (hover'da hiç değişmiyordu). Rest text-dim KORUNUR
    /// (chip'in Foreground'u rest'te text-secondary'dir, DOĞRUDAN bağ bu farkı kaybederdi) ama artık AYRI bir
    /// kanaldan (<see cref="DsChrome.IconForegroundProperty"/>) hover'da text-primary'ye geçer. Yapısal
    /// tetikleyici kanıtı + CANLI ikon-Stroke kanıtı BİRLİKTE.
    /// </summary>
    [StaFact]
    public void The_chip_bar_style_carries_a_hover_icon_foreground_setter_that_turns_text_primary()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Chip");

        // Rest: stilin KENDİSİ (Style.Setters, tetikleyici DEĞİL) text-dim ilan eder.
        var restSetter = Assert.Single(style.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedIconForegroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), ColorOf(host, restSetter));

        // Nötr hover (unchecked): AYNI MultiTrigger zemin/kenar/metinle BİRLİKTE ikon-özel kanalı da text-primary'ye taşır.
        var hoverMt = MultiTriggerWhere(style,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, false));
        var hoverIconSetter = Assert.Single(hoverMt.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedIconForegroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, hoverIconSetter));
    }

    /// <summary>Canlı kanıt: Σ'nin GERÇEK ikon <see cref="Path"/>'i rest'te text-dim'dir ve
    /// <see cref="DsChrome.IconForegroundProperty"/> değiştiğinde (stil tetikleyicisinin yapacağı ŞEY BUDUR)
    /// GERÇEKTEN takip eder — bağın kendisi (statik bir Stroke değil, canlı bir Binding) burada kanıtlanır.</summary>
    [StaFact]
    public void The_sigma_icon_rests_at_text_dim_and_tracks_its_own_icon_foreground_channel()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        var path = SigmaIconPath(bar);
        Assert.Equal(DsResources.TokenColor(bar, "Brush.TextDim"), DsResources.ColorOf(path.Stroke));

        DsChrome.SetIconForeground(bar.SigmaChip, (Brush)bar.FindResource("Brush.TextPrimary"));
        Assert.Equal(DsResources.TokenColor(bar, "Brush.TextPrimary"), DsResources.ColorOf(path.Stroke));
        GC.KeepAlive(window);
    }

    private static Path SigmaIconPath(ActionBar bar)
    {
        var content = (StackPanel)bar.SigmaChip.Content;
        var viewbox = (Viewbox)content.Children[0];
        var canvas = (Canvas)viewbox.Child;
        return (Path)canvas.Children[0];
    }

    // ---------------------------------------------------------------- segment: yalnız seçili OLMAYAN hover alır

    /// <summary>[hover tablosu satır 3] Segment (Debug|Release): yalnız seçili OLMAYAN seçenek hover alır —
    /// MultiTrigger <c>IsChecked=False</c> ŞARTI TAŞIR, seçili seçenek kendi (base) <c>IsChecked=True</c>
    /// tetikleyicisinde kalır ve hover'ı hiç GÖRMEZ (README §9 v1.17.0 madde 1: "seçili seçenek surface-overlay'da
    /// durduğu için ikisi karışmaz").</summary>
    [StaFact]
    public void Only_the_unselected_segment_option_carries_a_hover_multitrigger()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Segment.Item");
        var mt = MultiTriggerWhere(style,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, false));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextSecondary"), ColorOf(host, fg));

        // Seçili seçeneğin KENDİ (base Ds.Segment.Item) IsChecked=True tetikleyicisi hover'dan bağımsızdır —
        // 1 koşullu olmalı (yalnız IsChecked), hover koşulu içermez.
        var checkedTrigger = style.Triggers.OfType<Trigger>()
            .Concat(style.BasedOn!.Triggers.OfType<Trigger>())
            .Single(t => t.Property == ToggleButton.IsCheckedProperty && Equals(t.Value, true));
        Assert.NotNull(checkedTrigger);
    }

    // ---------------------------------------------------------------- gerçek kontrollerin bar stilini taşıdığı

    /// <summary>Realize edilmiş <see cref="ActionBar"/>'da her nötr/aktif kontrol GRUBU gerçekten <c>Ds.Bar.*</c>
    /// stilini taşır — stil doğru tanımlanmış olsa da yanlış anahtara Style atanırsa (XAML'de eski "Ds.Chip"
    /// unutulsa) yukarıdaki stil testleri hâlâ yeşil kalırdı; bu test KABLOLAMAYI doğrular.</summary>
    [StaFact]
    public void The_action_bar_wires_every_neutral_and_active_control_to_its_bar_specific_style()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        Assert.Same(bar.FindResource("Ds.Bar.Chip"), bar.SigmaChip.Style);
        Assert.Same(bar.FindResource("Ds.Bar.Chip"), bar.BranchChip.Style);
        Assert.Same(bar.FindResource("Ds.Bar.Chip"), bar.WorktreeChip.Style);
        Assert.Same(bar.FindResource("Ds.Bar.Chip"), bar.PerfChip.Style);
        Assert.Same(bar.FindResource("Ds.Bar.Chip.Action"), bar.BehindChip.Style);
        Assert.Same(bar.FindResource("Ds.Bar.Button.Secondary.Sm"), bar.SyncButton.Style);
        foreach (var item in bar.Segment.Items.Cast<RadioButton>())
            Assert.Same(bar.FindResource("Ds.Bar.Segment.Item"), item.Style);
        GC.KeepAlive(window);
    }

    /// <summary>Aynı kablolama <see cref="MaintenanceBox"/>'ın üç ikon düğmesi için.</summary>
    [StaFact]
    public void The_maintenance_box_wires_its_three_icon_buttons_to_the_bar_icon_button_style()
    {
        var vm = NewVm();
        var (box, window) = RealizeBox(vm);

        Assert.Same(box.FindResource("Ds.Bar.IconButton"), box.CleanButton.Style);
        Assert.Same(box.FindResource("Ds.Bar.IconButton"), box.OptimizeButton.Style);
        Assert.Same(box.FindResource("Ds.Bar.IconButton"), box.ResolveButton.Style);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Build/Stop DEĞİŞMEZ

    /// <summary>Build split-button ve Stop kendi variant hover'ında kalır — bar'ın tek hover dili onları
    /// KAPSAMAZ (brief: "Build split-button ve Stop DEĞİŞMEZ"). Stil ANAHTARLARI referansça pinlidir; biri
    /// yanlışlıkla <c>Ds.Bar.*</c>'a taşınsa bu test KIRMIZI verir.</summary>
    [StaFact]
    public void The_stop_and_build_primary_buttons_keep_their_own_variant_styles()
    {
        var vm = NewVm();
        var (bar, window) = RealizeBar(vm);

        Assert.Same(bar.FindResource("Ds.Button.Danger.Md"), bar.StopButton.Style);
        var primary = (Button)bar.Split.Template.FindName("PART_Primary", bar.Split);
        Assert.Same(bar.FindResource("Ds.Button.Primary.Md"), primary.Style);
        GC.KeepAlive(window);
    }
}
