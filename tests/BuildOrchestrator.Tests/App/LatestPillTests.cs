using System.Windows;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] `⌄ latest` pill — Ek A-15: dipten ≥48px iken görünür (560ms jumping penceresi); tıkla → yumuşak dibe;
/// dibe dönünce kaybolur. Görünürlük kararı <see cref="BottomAnchorDecision.ShouldShowPill"/>'de (pür, ayrıca
/// bkz. BottomAnchorTests); burada pill'e ÖZGÜ pür bir kanıt + gerçek <see cref="LatestPill"/> kontrolünün host
/// (ConsoleView'daki BİREBİR aynı) kablajla uçtan uca doğrulaması.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class LatestPillTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(48, false)]
    [InlineData(48.01, true)]
    [InlineData(200, true)]
    public void Pure_pill_visibility_visible_only_beyond_the_48px_threshold_and_never_while_jumping(double distance, bool expectedVisible)
    {
        var free = new BottomAnchorState(IsStuck: false, IsJumping: false);
        Assert.Equal(expectedVisible, BottomAnchorDecision.ShouldShowPill(free, distance));

        // Uçuşta bir "dibe git" animasyonu varken (560ms pencere) — dipten NE KADAR uzak olursa olsun görünmez.
        var jumping = new BottomAnchorState(IsStuck: true, IsJumping: true);
        Assert.False(BottomAnchorDecision.ShouldShowPill(jumping, distance));
    }

    private sealed class Wiring
    {
        public double Offset;
        public double Extent = 1000;
        public double Viewport = 200;
        public bool AnimatesOnJump = true;
        public double? LastSmoothTarget;
        public readonly LatestPill Pill = new();
        public readonly BottomAnchorBehavior Anchor;

        public Wiring()
        {
            Anchor = new BottomAnchorBehavior(
                getOffset: () => Offset, getExtent: () => Extent, getViewport: () => Viewport,
                scrollInstant: v => Offset = v,
                scrollSmooth: target => { LastSmoothTarget = target; if (AnimatesOnJump) return true; Offset = target; return false; },
                scheduleOnce: (_, _) => { }); // 560ms penceresi bu testlerde önemsiz — StaFact hemen sonrasını kontrol eder
            // [T59] ConsoleView.xaml.cs'teki KABLAJIN BİREBİR AYNISI: Changed → Visibility, Click → JumpToBottom.
            Anchor.Changed += (_, _) => Pill.Visibility = Anchor.ShowPill ? Visibility.Visible : Visibility.Collapsed;
            Pill.Click += (_, _) => Anchor.JumpToBottom();
        }

        public void Click() => Pill.PillButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    [StaFact]
    public void Pill_starts_collapsed_matching_the_initial_stuck_state()
    {
        var w = new Wiring();
        Assert.Equal(Visibility.Collapsed, w.Pill.Visibility);
    }

    [StaFact]
    public void Pill_becomes_visible_once_the_host_reports_a_scroll_beyond_the_threshold()
    {
        var w = new Wiring { Offset = 0 }; // distance = 1000-0-200 = 800

        w.Anchor.OnScrollChanged(extentHeightChange: 0);

        Assert.Equal(Visibility.Visible, w.Pill.Visibility);
    }

    [StaFact]
    public void Clicking_the_pill_scrolls_to_bottom_and_hides_the_pill()
    {
        var w = new Wiring { Offset = 0, AnimatesOnJump = false }; // reduced-motion path: instant, no jumping window
        w.Anchor.OnScrollChanged(extentHeightChange: 0);
        Assert.Equal(Visibility.Visible, w.Pill.Visibility);

        w.Click();

        Assert.Equal(800, w.LastSmoothTarget); // extent(1000) - viewport(200)
        Assert.True(w.Anchor.IsStuck);
        Assert.Equal(Visibility.Collapsed, w.Pill.Visibility); // dibe dönünce kaybolur
    }
}
