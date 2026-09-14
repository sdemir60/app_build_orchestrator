using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
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
/// deseni) — bu yüzden hover HEDEFLERİ stil trigger'ları ÜZERİNDEN okunur (tetikleyicinin varlığı GERÇEK
/// setter'larıyla kanıtlanır).</para>
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

    /// <summary>Bir <see cref="Style"/>'ın (BasedOn zincirinde) <paramref name="conditionCount"/> koşullu ve
    /// <paramref name="hasCondition"/>'ın TÜMÜNÜ sağlayan İLK <see cref="MultiTrigger"/>'ı — yoksa fırlatır.
    /// En TÜRETİLMİŞ stilden başlar (WPF de aynı sırayı uygular, <c>HoverBackgroundKeyOf</c> deseni).</summary>
    private static MultiTrigger MultiTriggerWhere(Style style, int conditionCount, params (DependencyProperty Property, object Value)[] hasCondition)
    {
        for (var s = style; s is not null; s = s.BasedOn)
            foreach (var mt in s.Triggers.OfType<MultiTrigger>())
            {
                var conditions = mt.Conditions.OfType<Condition>().ToList();
                if (conditions.Count != conditionCount) continue;
                if (hasCondition.All(hc => conditions.Any(c => c.Property == hc.Property && Equals(c.Value, hc.Value))))
                    return mt;
            }
        throw new InvalidOperationException(
            $"{style.TargetType?.Name} stilinde {conditionCount} koşullu, aranan koşulları taşıyan bir MultiTrigger yok.");
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

    // ---------------------------------------------------------------- nötr hover: zemin/kenar/metin BİRLİKTE

    /// <summary>[hover tablosu satır 1] Nötr kontrol grupları — Σ/sayaç çipleri, branch/worktree/perf (hepsi
    /// <c>Ds.Bar.Chip</c>), "N behind" (<c>Ds.Bar.Chip.Action</c>), Sync (<c>Ds.Bar.Button.Secondary.Sm</c>) —
    /// hover'da neutral-700 zemin + neutral-500 kenar + text-primary metin/ikon BİRLİKTE değişir. Üçü de AYNI
    /// üç setter'ı taşıdığı için TEK yardımcıyla üç stil de doğrulanır (kopya YASAK).</summary>
    [StaFact]
    public void Chip_chip_action_and_secondary_button_bar_styles_resolve_the_same_neutral_hover_surface()
    {
        var host = DsResources.NewHost();
        foreach (string key in new[] { "Ds.Bar.Chip", "Ds.Bar.Chip.Action", "Ds.Bar.Button.Secondary.Sm" })
        {
            var style = (Style)host.FindResource(key);
            var mt = MultiTriggerWhere(style, 2, (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true));
            var setters = mt.Setters.OfType<Setter>().ToList();

            var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
            var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
            var border = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);

            Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), ColorOf(host, bg));
            Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, fg));
            Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral500"), ColorOf(host, border));
        }
    }

    /// <summary>[hover tablosu satır 1 — nota göre] Bakım kutusunun üç ikon butonu (<c>Ds.Bar.IconButton</c>)
    /// KENDİ kenarı yoktur (kutu ortak çerçeveyi taşır) → hover yalnız zemin + ikon; bu yüzden bu stilin nötr
    /// hover'ında bir <c>AnimatedBorderBrush</c> setter'ı YOKTUR (Ds.Bar.Chip'in aksine — kasıtlı fark).</summary>
    [StaFact]
    public void The_maintenance_icon_button_bar_style_hovers_with_surface_and_icon_only_no_border()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.IconButton");
        var mt = MultiTriggerWhere(style, 2, (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var fg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextPrimary"), ColorOf(host, fg));
        Assert.DoesNotContain(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);
    }

    /// <summary>Disabled kontrol nötr hover ALMAZ: her nötr-hover MultiTrigger'ı <c>IsEnabled=True</c> ŞARTI
    /// TAŞIR (brief notu: "Base zaten opaklık .45 — hover setter'ı IsEnabled=False'ta etkisiz olmalı:
    /// MultiTrigger IsMouseOver+IsEnabled"). <see cref="MultiTriggerWhere"/> zaten bu şartı ARAYARAK bulduğu
    /// için önceki iki test dolaylı kanıttır; bu test kanıtı DOĞRUDAN ve olumsuz biçimde de sabitler — koşul
    /// SİLİNSE önceki testler <see cref="InvalidOperationException"/> ile KIRMIZI verirdi, ama mesaj neden
    /// kırıldığını söylemez; bu test nedeni İSİMLENDİRİR.</summary>
    [StaFact]
    public void Neutral_hover_multitriggers_require_is_enabled_true()
    {
        var host = DsResources.NewHost();
        foreach (string key in new[] { "Ds.Bar.Chip", "Ds.Bar.Chip.Action", "Ds.Bar.Button.Secondary.Sm", "Ds.Bar.IconButton" })
        {
            var style = (Style)host.FindResource(key);
            var mt = MultiTriggerWhere(style, 2, (UIElement.IsMouseOverProperty, true));
            Assert.Contains(mt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty && Equals(c.Value, true));
        }
    }

    // ---------------------------------------------------------------- açık/aktif kontrol: amber-soft-hover + amber kenar

    /// <summary>[hover tablosu satır 2] Açık/aktif kontrol (IsChecked filtre çipi, açık branch/worktree popover
    /// chip'i) hover'da amber-soft-hover zemin + amber kenar alır; METİN <c>amber-text</c> SABİT kalır —
    /// tetikleyici Foreground'u hiç YAZMAZ (Ds.Chip'in kendi IsChecked tetikleyicisi zaten amber-text yazmıştır,
    /// bu tetikleyici SONRA gelip yalnız zemin/kenarı ezer).</summary>
    [StaFact]
    public void The_checked_chip_hover_multitrigger_turns_amber_soft_hover_with_an_amber_border_and_leaves_text_alone()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Chip");
        var mt = MultiTriggerWhere(style, 3,
            (UIElement.IsMouseOverProperty, true), (Control.IsEnabledProperty, true), (ToggleButton.IsCheckedProperty, true));
        var setters = mt.Setters.OfType<Setter>().ToList();

        var bg = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var border = Assert.Single(setters, s => s.Property == DsTransition.AnimatedBorderBrushProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoftHover"), ColorOf(host, bg));
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), ColorOf(host, border));
        // metin amber-text SABİT: bu tetikleyici Foreground'a HİÇ dokunmaz.
        Assert.DoesNotContain(setters, s => s.Property == DsTransition.AnimatedForegroundProperty);
    }

    /// <summary>Koşan bakım görevi/Resolve düğmesi (<c>DsChrome.IsActive</c>) de AYNI dili konuşur — rest'te
    /// amber-soft, hover'da amber-soft-hover. Komut kapısı uçuşta disabled'dır ama düğme CANLI görünmeye devam
    /// etmelidir; bu yüzden hover eşleniği <c>IsEnabled</c> ŞARTI ARAMAZ (nötr hover'ın aksine — kasıtlı fark,
    /// [kullanıcı kararı 2026-09-12]'nin bar hover diline genişletilmiş hâli).</summary>
    [StaFact]
    public void The_active_maintenance_icon_hover_multitrigger_does_not_require_is_enabled()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.IconButton");

        var resting = TriggerWhere(style, DsChrome.IsActiveProperty, true);
        var restingBg = Assert.Single(resting.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoft"), ColorOf(host, restingBg));

        var hoverMt = MultiTriggerWhere(style, 2, (DsChrome.IsActiveProperty, true), (UIElement.IsMouseOverProperty, true));
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty);
        var hoverBg = Assert.Single(hoverMt.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberSoftHover"), ColorOf(host, hoverBg));
    }

    /// <summary>Aynı istisna Sync düğmesi için de geçerlidir — [kullanıcı kararı 2026-09-12] koşan Sync bu
    /// bar'ın "açık/aktif kontrol"üdür; etiket ("Sync") amber'a DÖNMEZ (kimliğini korur, ActionBarTests'in
    /// <c>The_sync_button_spins_in_amber_while_a_sync_is_in_flight</c> testiyle AYNI kural) — bu yüzden hiçbir
    /// IsActive tetikleyicisi Foreground'u YAZMAZ.</summary>
    [StaFact]
    public void The_active_sync_hover_multitrigger_does_not_require_is_enabled_and_never_writes_foreground()
    {
        var host = DsResources.NewHost();
        var style = (Style)host.FindResource("Ds.Bar.Button.Secondary.Sm");

        var resting = TriggerWhere(style, DsChrome.IsActiveProperty, true);
        Assert.DoesNotContain(resting.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);

        var hoverMt = MultiTriggerWhere(style, 2, (DsChrome.IsActiveProperty, true), (UIElement.IsMouseOverProperty, true));
        Assert.DoesNotContain(hoverMt.Conditions.OfType<Condition>(), c => c.Property == Control.IsEnabledProperty);
        Assert.DoesNotContain(hoverMt.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);
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
        var mt = MultiTriggerWhere(style, 3,
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
        foreach (var item in bar.Segment.Items.Cast<System.Windows.Controls.RadioButton>())
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
        var primary = (System.Windows.Controls.Button)bar.Split.Template.FindName("PART_Primary", bar.Split);
        Assert.Same(bar.FindResource("Ds.Button.Primary.Md"), primary.Style);
        GC.KeepAlive(window);
    }
}
