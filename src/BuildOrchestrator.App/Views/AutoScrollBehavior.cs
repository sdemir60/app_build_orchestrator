using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// Auto-follow behaviour for the console (Section 7): new log lines scroll to the bottom; when the
/// user scrolls up, following pauses and resumes after ~2s of inactivity. Every scroll resets the
/// timer.
/// </summary>
public static class AutoScrollBehavior
{
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(2);

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private sealed class State
    {
        public bool Following = true;
        public DispatcherTimer? Timer;
    }

    private static readonly Dictionary<ListBox, State> States = new();

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
            return;

        if (e.NewValue is true)
        {
            var state = new State();
            States[list] = state;

            if (list.ItemsSource is INotifyCollectionChanged incc)
                incc.CollectionChanged += (_, _) =>
                {
                    if (state.Following)
                        ScrollToEnd(list);
                };

            list.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, args) => OnScrollChanged(list, state, args)));
        }
        else
        {
            States.Remove(list);
        }
    }

    private static void OnScrollChanged(ListBox list, State state, ScrollChangedEventArgs e)
    {
        // Content growth (new lines) is not a user gesture.
        bool contentGrew = e.ExtentHeightChange != 0;
        if (contentGrew)
        {
            if (state.Following)
                ScrollToEnd(list);
            return;
        }

        // A real user scroll: pause following and (re)start the resume timer.
        if (e.VerticalChange != 0)
        {
            bool atBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2;
            state.Following = atBottom;

            state.Timer ??= CreateTimer(state, list);
            state.Timer.Stop();
            if (!atBottom)
                state.Timer.Start();
        }
    }

    private static DispatcherTimer CreateTimer(State state, ListBox list)
    {
        var timer = new DispatcherTimer { Interval = ResumeDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            state.Following = true;
            ScrollToEnd(list);
        };
        return timer;
    }

    private static void ScrollToEnd(ListBox list)
    {
        if (list.Items.Count == 0)
            return;
        var scroll = GetScrollViewer(list);
        scroll?.ScrollToEnd();
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            var result = GetScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
