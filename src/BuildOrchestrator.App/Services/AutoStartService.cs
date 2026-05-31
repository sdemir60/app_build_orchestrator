using Microsoft.Win32;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Opt-in autostart via the Windows <c>Run</c> registry key (Section 2/3). All operations are
/// best-effort and never throw into the UI.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BuildOrchestrator";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrEmpty(s);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    // --tray so an autostarted instance launches minimized to the tray.
                    key.SetValue(ValueName, $"\"{exe}\" --tray");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // best effort
        }
    }
}
