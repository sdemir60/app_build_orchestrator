using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] App/Resources/Motion.xaml: design-v1 tokens/effects.css birebir Duration + KeySpline
/// kaynakları. Ayrıca MotionSettings.Attach ile bu ResourceDictionary'nin Duration.* girdilerinin reduced-motion
/// sinyaline göre topluca 0'a çevrildiği/geri yüklendiği doğrulanır (uygulama düzeyinde toplu swap mekanizması).
/// </summary>
public class MotionResourcesTests
{
    private sealed class FakeMotionSignal : IMotionSignal
    {
        public bool AnimationsEnabled { get; set; } = true;
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    // pack:// URI'ler gerçek bir Application olmadan (headless test host) çözülmez (FontAssetTests'teki
    // TestAssets deseniyle aynı: dosyadan doğrudan XamlReader ile yükle).
    private static ResourceDictionary LoadMotionDictionary()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Motion.xaml");
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    [StaFact]
    public void Duration_tokens_match_design_v1_effects_css_exactly()
    {
        var resources = LoadMotionDictionary();

        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(80)), resources["Duration.Instant"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(120)), resources["Duration.Fast"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(180)), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(280)), resources["Duration.Slow"]);
    }

    [StaFact]
    public void KeySpline_tokens_match_design_v1_effects_css_control_points_exactly()
    {
        var resources = LoadMotionDictionary();

        var easeOut = Assert.IsType<KeySpline>(resources["KeySpline.EaseOut"]);
        Assert.Equal(new Point(0.22, 1), easeOut.ControlPoint1);
        Assert.Equal(new Point(0.36, 1), easeOut.ControlPoint2);

        var easeStandard = Assert.IsType<KeySpline>(resources["KeySpline.EaseStandard"]);
        Assert.Equal(new Point(0.4, 0), easeStandard.ControlPoint1);
        Assert.Equal(new Point(0.2, 1), easeStandard.ControlPoint2);

        var easeInOut = Assert.IsType<KeySpline>(resources["KeySpline.EaseInOut"]);
        Assert.Equal(new Point(0.65, 0), easeInOut.ControlPoint1);
        Assert.Equal(new Point(0.35, 1), easeInOut.ControlPoint2);
    }

    [StaFact]
    public void Attach_collapses_all_duration_resources_to_zero_when_signal_is_off()
    {
        var resources = LoadMotionDictionary();
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);

        settings.Attach(resources);

        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Instant"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Fast"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Slow"]);
    }

    [StaFact]
    public void Attach_restores_token_durations_live_when_signal_turns_back_on()
    {
        var resources = LoadMotionDictionary();
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);
        settings.Attach(resources);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Base"]);

        signal.AnimationsEnabled = true;
        signal.Raise();

        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(180)), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(280)), resources["Duration.Slow"]);
    }
}
