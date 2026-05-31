using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Behaviors;

/// <summary>
/// Auto-focus behavior for the project list (Section 7): scrolls the active build card into view as
/// it changes; if the user scrolls manually, automatic following pauses for ~2s of idle before
/// resuming. Bind <see cref="ActiveIdProperty"/> to the MainViewModel's ActiveProjectId.
/// </summary>
public static class AutoFocusBehavior
{
    public static readonly DependencyProperty ActiveIdProperty =
        DependencyProperty.RegisterAttached(
            "ActiveId", typeof(string), typeof(AutoFocusBehavior),
            new PropertyMetadata(null, OnActiveIdChanged));

    public static void SetActiveId(DependencyObject o, string? value) => o.SetValue(ActiveIdProperty, value);
    public static string? GetActiveId(DependencyObject o) => (string?)o.GetValue(ActiveIdProperty);

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(AutoFocusBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static readonly DependencyProperty FollowingProperty =
        DependencyProperty.RegisterAttached("Following", typeof(bool), typeof(AutoFocusBehavior),
            new PropertyMetadata(true));

    private static readonly DependencyProperty TimerProperty =
        DependencyProperty.RegisterAttached("Timer", typeof(DispatcherTimer), typeof(AutoFocusBehavior),
            new PropertyMetadata(null));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list || e.NewValue is not true)
        {
            return;
        }

        list.Loaded += (_, _) =>
        {
            var sv = FindScrollViewer(list);
            if (sv is not null)
            {
                sv.ScrollChanged += (_, args) =>
                {
                    if (Math.Abs(args.VerticalChange) > 0.5 && Math.Abs(args.ExtentHeightChange) < 0.5)
                    {
                        // User scroll: pause and schedule resume.
                        d.SetValue(FollowingProperty, false);
                        var timer = (DispatcherTimer?)d.GetValue(TimerProperty)
                                    ?? new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        d.SetValue(TimerProperty, timer);
                        timer.Stop();
                        timer.Tick -= ResumeTick;
                        timer.Tag = d;
                        timer.Tick += ResumeTick;
                        timer.Start();
                    }
                };
            }
        };
    }

    private static void ResumeTick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer { Tag: DependencyObject d } timer)
        {
            timer.Stop();
            d.SetValue(FollowingProperty, true);
        }
    }

    private static void OnActiveIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list || e.NewValue is not string id || string.IsNullOrEmpty(id))
        {
            return;
        }
        if (!(bool)d.GetValue(FollowingProperty))
        {
            return; // user is browsing; don't yank the view
        }

        foreach (var item in list.Items)
        {
            // Match by Id property via reflection-free dynamic cast to the known card type.
            if (item is ViewModels.ProjectCardViewModel card &&
                string.Equals(card.Id, id, StringComparison.Ordinal))
            {
                list.ScrollIntoView(item);
                break;
            }
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
            var result = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (result is not null)
            {
                return result;
            }
        }
        return null;
    }
}
