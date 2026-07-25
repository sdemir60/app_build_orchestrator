using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E3 fold'ları] Motion sahibi hijyeni: (1) BuildingSpinner'ın 900ms/270° dönüşünü sayısal PİNLER (C-2 kararı —
/// bundle'ın 900ms'i, README'nin 1.4s'i DEĞİL — sessizce kaymasın); (2) motion-signal aboneliğinin idempotent
/// (subscribe-once) guard'ını kanıtlar — Loaded iki kez ateşlense de sahip TEK abonelik tutar (çift Refresh/
/// ApplyBreathing birikmez). Aynı <c>-= sonra +=</c> idiomu ProjectRow/StickyRibbon/BuildingSpinner/StatusGlyph'te
/// paylaşılır; burada seam'li ProjectRow üstünden pinlenir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class MotionOwnerHygieneTests
{
    [Fact]
    public void The_building_spinner_rotates_a_full_turn_over_900ms_at_30fps()
    {
        // C-2: 900ms (bundle) — README'nin 1.4s'i DEĞİL. 270°'lik YAY görseli 0→360° tam tur döner.
        Assert.Equal(900.0, BuildingSpinner.RotationMs);
        var spin = BuildingSpinner.BuildSpinAnimation();
        Assert.Equal(0.0, spin.From);
        Assert.Equal(360.0, spin.To);
        Assert.Equal(TimeSpan.FromMilliseconds(900), spin.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, spin.RepeatBehavior);
        Assert.Equal(30, Timeline.GetDesiredFrameRate(spin)); // dekoratif sonsuz → 30fps tavanı (feasibility §3.4)
    }

    [StaFact]
    public void A_motion_owner_subscribes_to_the_signal_exactly_once_across_repeated_loads()
    {
        // [E3 fold — subscribe-once] -= sonra += idempotent guard'ı: Loaded iki kez ateşlense de (unload olmadan)
        // tek abonelik kalır. Guard olmasa SubscriberCount 2 olurdu → her sinyalde çift ApplyBreathing.
        var motion = new CountingMotion();
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, MotionSettings = motion, DataContext = vm };

        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.Equal(1, motion.SubscriberCount);
    }

    [StaFact]
    public void The_building_spinner_subscribes_to_the_static_signal_exactly_once_across_repeated_loads()
        => AssertSubscribesOnce(new BuildingSpinner());

    [StaFact]
    public void The_status_glyph_subscribes_to_the_static_signal_exactly_once_across_repeated_loads()
        => AssertSubscribesOnce(new StatusGlyph());

    /// <summary>[fix — #3/#5] BuildingSpinner/StatusGlyph seam'li DEĞİL: motion sinyalini statik <c>App.Motion</c>'dan
    /// DOĞRUDAN okur → subscribe-once guard'ının gövdesi yalnız <c>App.Motion</c> null DEĞİLKEN koşar. Headless'ta
    /// null olduğundan guard hiç çalışmaz ve plain <c>+=</c>'e geri dönmek HİÇBİR testi düşürmezdi. Bu yüzden
    /// static'i geçici set/restore et (Console UI serial collection → mutasyon serileştirilir) ve Loaded'ı iki kez
    /// ateşle: guard varsa abonelik TEK kalır, olmasa 2 olurdu.</summary>
    private static void AssertSubscribesOnce(FrameworkElement owner)
    {
        var motion = new CountingMotion();
        var original = BuildOrchestrator.App.App.Motion;
        BuildOrchestrator.App.App.Motion = motion;
        try
        {
            owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal(1, motion.SubscriberCount);
        }
        finally
        {
            BuildOrchestrator.App.App.Motion = original; // headless varsayılanı (null) geri yükle
        }
    }

    /// <summary>Abone olan delege SAYISINI (guard'ı) gözlemleyen IMotionSettings — çift-abonelik burada görünür.</summary>
    private sealed class CountingMotion : IMotionSettings
    {
        private EventHandler? _handlers;
        public bool AnimationsEnabled => false;
        public TimeSpan Effective(TimeSpan token) => TimeSpan.Zero;
        public event EventHandler? AnimationsEnabledChanged
        {
            add => _handlers += value;
            remove => _handlers -= value;
        }
        public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;
    }
}
