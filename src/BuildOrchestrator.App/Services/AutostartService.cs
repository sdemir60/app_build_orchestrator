using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;
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
    /// <summary>[P4] Kullanıcı bu değeri Görev Yöneticisi → Başlangıç uygulamaları'nda (ya da Ayarlar → Uygulamalar →
    /// Başlangıç'ta) devre dışı bırakmış mı — <see cref="StartupApproval"/>.</summary>
    bool IsStartupDisabled(string name);
    /// <summary>[P4] Görev Yöneticisi'nin "devre dışı" işaretini kaldırır (yoksa no-op) — Windows işaretsiz değeri
    /// etkin sayar.</summary>
    void ClearStartupDisabled(string name);
}

/// <summary>[P4] Görev Yöneticisi → Başlangıç uygulamaları'nın (ve Ayarlar → Uygulamalar → Başlangıç'ın) kararı
/// <c>Run</c> değerinin YANINDA, <see cref="KeyPath"/> altında AYNI adlı ikili bir değerde durur: Windows kapatınca
/// <c>03 00 00 00</c> + kapatıldığı anı (FILETIME), açınca <c>02 00 00 00</c> + sıfırları yazar. Değer yoksa Windows
/// kaydı etkin sayar.</summary>
internal static class StartupApproval
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>İlk bayt TEK ise devre dışı (03/07), çift ise etkin (02/06); değer yoksa ya da boşsa etkin.</summary>
    public static bool IsDisabled(byte[]? data) => data is { Length: > 0 } && (data[0] & 1) == 1;
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

    public bool IsStartupDisabled(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApproval.KeyPath, writable: false);
        return StartupApproval.IsDisabled(key?.GetValue(name) as byte[]);
    }

    public void ClearStartupDisabled(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApproval.KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
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

    /// <summary>Açılışın uzlaştırması: tercihe göre autostart değerini yazar (enabled) ya da kaldırır (disabled).
    /// [P4] Windows kaydı yazamazsa (bir politika ya da güvenlik yazılımı <c>HKCU\...\Run</c>'ı kilitlemiş) SESSİZCE
    /// geçer — uygulamanın açılışı bir tercih yüzünden düşmez; bir sonraki açılış yeniden dener.</summary>
    public void Apply(bool autostartEnabled) => TryWrite(() => WriteRunValue(autostartEnabled), out _);

    /// <summary>[P4] Windows'un GERÇEK durumu — Settings'in Start with Windows anahtarı bundan açılır (kayıtlı
    /// tercihten değil): Run değeri yoksa <see cref="AutostartState.Off"/>; varsa ve Görev Yöneticisi'nde devre dışı
    /// bırakılmışsa <see cref="AutostartState.DisabledInStartupApps"/>; aksi hâlde <see cref="AutostartState.On"/>.
    /// Okunamazsa <see cref="AutostartState.Off"/>.</summary>
    public AutostartState State
    {
        get
        {
            try
            {
                if (!registry.Exists(valueName)) return AutostartState.Off;
                return registry.IsStartupDisabled(valueName) ? AutostartState.DisabledInStartupApps : AutostartState.On;
            }
            catch (Exception ex) when (IsRegistryRefusal(ex)) { return AutostartState.Off; }
        }
    }

    /// <summary>[P4] Save'in yolu: kullanıcı Start with Windows'u değiştirdiğinde kayıt ANINDA yazılır (açık) ya da
    /// silinir (kapalı) — yeniden başlatma beklenmez. Açarken Görev Yöneticisi'nin "devre dışı" işareti de kaldırılır
    /// (kullanıcı kararı, seçenek 1: en son ve açıkça verilen karar "aç"tır); <see cref="Apply"/> bunu YAPMAZ.
    /// Yazılamazsa <c>false</c> ve Windows'un nedeni (<paramref name="failure"/>); fırlatmaz.</summary>
    public bool TryTurn(bool on, [NotNullWhen(false)] out string? failure) => TryWrite(() =>
    {
        WriteRunValue(on);
        if (on && registry.IsStartupDisabled(valueName)) registry.ClearStartupDisabled(valueName);
    }, out failure);

    private void WriteRunValue(bool on)
    {
        if (on) registry.Set(valueName, command);
        else registry.Remove(valueName);
    }

    private static bool TryWrite(Action write, [NotNullWhen(false)] out string? failure)
    {
        try
        {
            write();
            failure = null;
            return true;
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            failure = ex.Message;
            return false;
        }
    }

    /// <summary>Registry'nin "yazamazsın/okuyamazsın" dediği üç istisna — başka bir istisna bir HATADIR ve yutulmaz.</summary>
    private static bool IsRegistryRefusal(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException;
}

/// <summary>[P4] Windows'un başlangıç kaydının gerçek durumu (<see cref="AutostartService.State"/>).</summary>
public enum AutostartState
{
    /// <summary>Kayıt yok — Windows oturumu açılınca uygulama başlamaz.</summary>
    Off,
    /// <summary>Kayıt var — Windows oturumu açılınca uygulama başlar.</summary>
    On,
    /// <summary>Kayıt var ama kullanıcı Görev Yöneticisi'nde devre dışı bırakmış — Windows uygulamayı başlatmaz.</summary>
    DisabledInStartupApps,
}
