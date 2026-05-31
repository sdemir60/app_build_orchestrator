using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Behaviors;

/// <summary>
/// Auto-follow behavior for the console (Section 7): scrolls to the newest line as logs arrive; when
/// the user scrolls manually it stops following, and resumes after ~2s of no scrolling (each scroll
/// resets the timer).
/// </summary>
public static class AutoFollowBehavior
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(AutoFollowBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(FollowState), typeof(AutoFollowBehavior),
            new PropertyMetadata(null));

    private sealed class FollowState
    {
        public bool Following = true;
        public DispatcherTimer? ResumeTimer;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        if (e.NewValue is true)
        {
            var state = new FollowState();
            d.SetValue(StateProperty, state);

            list.Loaded += (_, _) =>
            {
                var sv = FindScrollViewer(list);
                if (sv is null)
                {
                    return;
                }

                sv.ScrollChanged += (_, args) =>
                {
                    // Distinguish user scroll from content-growth scroll.
                    var contentGrew = Math.Abs(args.ExtentHeightChange) > 0.5;
                    if (!contentGrew && Math.Abs(args.VerticalChange) > 0.5)
                    {
                        // User scrolled: pause following and (re)start the resume timer.
                        state.Following = sv.VerticalOffset >= sv.ScrollableHeight - 2;
                        RestartResumeTimer(sv, state);
                    }

                    if (state.Following && contentGrew)
                    {
                        sv.ScrollToEnd();
                    }
                };
            };

            if (list.ItemsSource is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged += (_, _) =>
                {
                    if (state.Following)
                    {
                        FindScrollViewer(list)?.ScrollToEnd();
                    }
                };
            }
        }
        else
        {
            d.ClearValue(StateProperty);
        }
    }

    private static void RestartResumeTimer(ScrollViewer sv, FollowState state)
    {
        state.ResumeTimer?.Stop();
        state.ResumeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        state.ResumeTimer.Tick -= ResumeTick;
        state.ResumeTimer.Tag = (sv, state);
        state.ResumeTimer.Tick += ResumeTick;
        state.ResumeTimer.Start();
    }

    private static void ResumeTick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer { Tag: ValueTuple<ScrollViewer, FollowState> tuple } timer)
        {
            timer.Stop();
            tuple.Item2.Following = true;
            tuple.Item1.ScrollToEnd();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
        {
            return sv;
        }
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            var result = FindScrollViewer(child);
            if (result is not null)
            {
                return result;
            }
        }
        return null;
    }
}
