using System.Drawing;
using System.Windows.Forms;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// System tray icon (Section 2/6.1). Provides Show, Stop Build (works even when the window is closed)
/// and Exit. Built on the WinForms <see cref="NotifyIcon"/>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? ShowRequested;
    public event Action? StopRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Stop Build", null, (_, _) => StopRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Text = "Build Orchestrator",
            Visible = true,
            Icon = LoadIcon(),
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(3000);
        }
        catch
        {
            // best effort
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // fall through to a default icon
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
