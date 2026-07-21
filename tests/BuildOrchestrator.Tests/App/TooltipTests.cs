using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T61] Tooltip altyapısı — A13.2 harfiyen: <c>ToolTipService.InitialShowDelay=0</c> (WPF'in ~400-700ms
/// varsayılan gecikmesi kapatılır) ve <c>Placement=Custom</c> + <c>CustomPopupPlacementCallback</c> (WPF'in
/// <c>PlacementMode.Top/Bottom</c>'u hedefi SOLA hizalar, ORTALAMAZ — bu yüzden callback şart).
/// </summary>
[Collection("Console UI (serial)")]
public class TooltipTests
{
    // ---- Step 1 (brief, verbatim — adapte edilen tek şey pseudo-helper'ın DsResources.Load karşılığı) ----

    [StaFact]
    public void Tooltip_opens_with_zero_delay_and_stays_open()
    {
        var style = (Style)DsResources.Load("Controls.xaml")[typeof(ToolTip)];
        Assert.Contains(style.Setters.OfType<Setter>(),
            s => s.Property == ToolTipService.InitialShowDelayProperty && (int)s.Value! == 0);
    }

    [Theory]
    [InlineData(100.0, 40.0, 22.0, -28.0)]   // popup 100 genis, hedef 40 genis → x = (40-100)/2 = -30 ... y = -(22+6)
    public void Placement_centres_horizontally_and_offsets_six_pixels(double pw, double tw, double ph, double expectedY)
    {
        var p = AppTooltip.Placement(new Size(pw, ph), new Size(tw, 20), default)[0];
        Assert.Equal((tw - pw) / 2, p.Point.X, 3);
        Assert.Equal(expectedY, p.Point.Y, 3);
    }

    // ---- Self-review ek kapsam: "ShowDuration sonsuz" ve "Placement=Custom" ayrı ayrı kanıtlanır — tek bir
    // InitialShowDelay testi bunları göremez (gecikme varsayılana dönse bile bu iki setter hâlâ dursaydı). ----

    [StaFact]
    public void Tooltip_show_duration_is_effectively_infinite()
    {
        var style = (Style)DsResources.Load("Controls.xaml")[typeof(ToolTip)];
        Assert.Contains(style.Setters.OfType<Setter>(),
            s => s.Property == ToolTipService.ShowDurationProperty && (int)s.Value! == int.MaxValue);
    }

    [StaFact]
    public void Tooltip_uses_custom_placement_mode()
    {
        var style = (Style)DsResources.Load("Controls.xaml")[typeof(ToolTip)];
        Assert.Contains(style.Setters.OfType<Setter>(),
            s => s.Property == ToolTip.PlacementProperty && (PlacementMode)s.Value! == PlacementMode.Custom);
    }

    // ---- [T35 B4-fold] AppTooltip.Side trigger'larının DOĞRU callback'i çözdüğü — C1 title-bar butonları ilk
    // gerçek <ToolTip> tüketicisidir. Yanlış bağlı bir trigger (ör. Left → PlacementRight) suite'i sessizce
    // yeşil bırakırdı: her yönün AYRI callback'e gittiğini bu test pinler. ----

    [StaFact]
    public void Each_tooltip_side_trigger_resolves_the_matching_placement_callback()
    {
        var style = (Style)DsResources.Load("Controls.xaml")[typeof(ToolTip)];

        // Side verilmemiş (varsayılan Top): baz Setter PlacementTop'a gitmeli.
        var baseSetter = style.Setters.OfType<Setter>()
            .Single(s => s.Property == ToolTip.CustomPopupPlacementCallbackProperty);
        Assert.Same(AppTooltip.PlacementTop, baseSetter.Value);

        var expected = new (string Side, CustomPopupPlacementCallback Callback)[]
        {
            (AppTooltip.Bottom, AppTooltip.PlacementBottom),
            (AppTooltip.Left, AppTooltip.PlacementLeft),
            (AppTooltip.Right, AppTooltip.PlacementRight),
        };

        foreach (var (side, callback) in expected)
        {
            var trigger = style.Triggers.OfType<Trigger>()
                .Single(t => t.Property == AppTooltip.SideProperty && (string)t.Value! == side);
            var setter = trigger.Setters.OfType<Setter>()
                .Single(s => s.Property == ToolTip.CustomPopupPlacementCallbackProperty);
            Assert.Same(callback, setter.Value);
        }
    }

    // ---- Self-review ek kapsam: Side=Left/Right/Bottom matematiği (brief'in test'i yalnız varsayılan Top'u
    // kanıtlıyor; callback un-centered'a dönerse veya Left/Right karışırsa bunlar kırmızı olmalı). ----

    [Theory]
    [InlineData(AppTooltip.Bottom)]
    public void Placement_for_bottom_side_centres_horizontally_and_sits_six_pixels_below_target(string side)
    {
        var popup = new Size(80, 24);
        var target = new Size(40, 20);
        var p = AppTooltip.PlacementForSide(side, popup, target, default)[0];
        Assert.Equal((target.Width - popup.Width) / 2, p.Point.X, 3);
        Assert.Equal(target.Height + 6, p.Point.Y, 3);
    }

    [Theory]
    [InlineData(AppTooltip.Left)]
    [InlineData(AppTooltip.Right)]
    public void Placement_for_left_and_right_side_centres_vertically_and_offsets_six_pixels(string side)
    {
        var popup = new Size(80, 24);
        var target = new Size(40, 20);
        var p = AppTooltip.PlacementForSide(side, popup, target, default)[0];
        Assert.Equal((target.Height - popup.Height) / 2, p.Point.Y, 3);
        double expectedX = side == AppTooltip.Left ? -(popup.Width + 6) : target.Width + 6;
        Assert.Equal(expectedX, p.Point.X, 3);
    }

    [Fact]
    public void Placement_honours_the_offset_parameter()
    {
        var p = AppTooltip.Placement(new Size(100, 22), new Size(40, 20), new Point(3, -2))[0];
        Assert.Equal((40 - 100) / 2.0 + 3, p.Point.X, 3);
        Assert.Equal(-(22 + 6) - 2, p.Point.Y, 3);
    }

    // ---- Self-review ek kapsam: "canlı içerik binding ile çözülür" — stil ContentPresenter'ı ELE GEÇİRMEZ,
    // bir DataContext değişince tooltip içeriği görsel ağaçta GERÇEKTEN güncellenir (`Building — 24s` gibi). ----

    private sealed class ToolTipContentStub : DependencyObject
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(ToolTipContentStub), new PropertyMetadata("Building — 24s"));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }

    [StaFact]
    public void Live_bound_tooltip_content_updates_through_the_style_without_being_blocked()
    {
        // ToolTip mantıksal/görsel bir üst öğeye EKLENEMEZ (Popup dışında) — DsResources.Realize'ın
        // Border.Child yolu burada kullanılamaz; şablon/layout parent'sız uygulanır (ölçüm bir
        // PresentationSource GEREKTİRMEZ, yalnız gerçek zamanlı animasyon clock'u gerektirir — AnimationHost.cs).
        var host = DsResources.NewHost();
        var vm = new ToolTipContentStub();
        var tooltip = new ToolTip
        {
            Style = (Style)host.FindResource(typeof(ToolTip)),
            DataContext = vm,
        };
        tooltip.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ToolTipContentStub.Text)));
        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(300, 100));
        tooltip.Arrange(new Rect(0, 0, 300, 100));
        tooltip.UpdateLayout();

        var text = DsResources.Descendants(tooltip).OfType<TextBlock>().First();
        Assert.Equal("Building — 24s", text.Text);

        vm.Text = "Copy → Copied";
        tooltip.UpdateLayout();
        Assert.Equal("Copy → Copied", text.Text);
    }
}
