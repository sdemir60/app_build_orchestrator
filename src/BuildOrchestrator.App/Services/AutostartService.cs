using Microsoft.Win32;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [E2/T16] Autostart registry seam. GERÇEK registry erişimi bu arayüzün ARKASINDADIR → testler
/// <see cref="AutostartService"/>'i in-memory bir fake ile doğrular, gerçek <c>HKCU\...\Run</c>'a ASLA yazmaz.
/// </summary>
public interface IAutostartRegistry
{
    /// <summary>Autostart değerini yaz/üzerine yaz (login'de çalışacak komut).</summary>
    void Set(string name, string command);
    /// <summary>Autostart değerini kaldır (yoksa no-op).</summary>
    void Remove(string name);
    /// <summary>Autostart değeri var mı.</summary>
    bool Exists(string name);
}

/// <summary>
/// [E2/T16] <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run</c> gerçek yazıcısı — kullanıcı
/// login'inde uygulamayı başlatan standart Windows autostart konumu (admin/HKLM GEREKMEZ). Yalnız ÜRETİMde
/// kullanılır; testler <see cref="IAutostartRegistry"/> fake'ini enjekte eder.
/// </summary>
public sealed class RegistryAutostartRegistry : IAutostartRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void Set(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool Exists(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) is not null;
    }
}

/// <summary>
/// [E2/T16] <see cref="Shell.UiState.Autostart"/> tercihini registry ile uzlaştırır: <c>true</c> iken değer
/// yazılır, <c>false</c> iken silinir. <see cref="Apply"/> IDEMPOTENT'tir — her açılışta güvenle çağrılabilir
/// (tercih ile registry'yi hizalar). Değer adı ve komut (exe yolu + autostart argümanı) çağırandan enjekte edilir
/// (App.xaml.cs) — servis konum/komut bilmez, yalnız seam'i sürer.
/// </summary>
public sealed class AutostartService(IAutostartRegistry registry, string valueName, string command)
{
    /// <summary>Registry değer adı (HKCU\...\Run altındaki değerin adı).</summary>
    public const string DefaultValueName = "BuildOrchestrator";

    /// <summary>Tercihe göre autostart değerini yazar (enabled) ya da kaldırır (disabled).</summary>
    public void Apply(bool autostartEnabled)
    {
        if (autostartEnabled) registry.Set(valueName, command);
        else registry.Remove(valueName);
    }
}
