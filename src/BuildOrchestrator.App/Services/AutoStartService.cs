using System.Diagnostics;
using Microsoft.Win32;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Opt-in Windows autostart via the HKCU Run registry key (Section 2).
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BuildOrchestrator";

    private static string ExecutablePath
        => Process.GetCurrentProcess().MainModule?.FileName
           ?? System.Reflection.Assembly.GetEntryAssembly()!.Location;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key == null)
            return;

        if (enabled)
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
