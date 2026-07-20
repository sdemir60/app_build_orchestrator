using System.Globalization;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62 / v7Δ-5] Global kısayolun (varsayılan <b>Alt+B</b>, ayarlanabilir) <c>RegisterHotKey</c> karşılığı —
/// SAF çeviri, P/Invoke yok.
/// </summary>
public readonly record struct HotkeyBinding(uint Modifiers, uint VirtualKey)
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    /// <summary>Tuş basılı tutulurken tekrar tekrar WM_HOTKEY üretilmesini engeller (pencereyi bir kez getir).</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>[v7Δ-5] Kısayol şemasının global kısayolu.</summary>
    public const string DefaultGesture = "Alt+B";

    /// <summary>
    /// "Alt+B", "ctrl+shift+f5", "Win + Alt + 7" — büyük/küçük harf ve boşluk duyarsız. En az BİR modifier
    /// zorunludur (modifier'sız global hotkey tüm sistemde o tuşu çalar). Tanınmayan her şey <c>false</c> döner;
    /// çağıran varsayılana düşer (bkz. <see cref="HotkeyRegistration"/> — sessiz devre dışı kuralı).
    /// </summary>
    public static bool TryParse(string? gesture, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        uint modifiers = 0;
        uint? vk = null;
        foreach (string raw in gesture.Split('+'))
        {
            string token = raw.Trim();
            if (token.Length == 0) return false;
            if (vk is not null) return false; // tuştan sonra başka token olamaz

            switch (token.ToLowerInvariant())
            {
                case "alt": modifiers |= MOD_ALT; break;
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": modifiers |= MOD_WIN; break;
                default:
                    if (!TryParseKey(token, out uint parsed)) return false;
                    vk = parsed;
                    break;
            }
        }

        if (vk is null || modifiers == 0) return false;
        binding = new HotkeyBinding(modifiers | MOD_NOREPEAT, vk.Value);
        return true;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }            // VK_A..VK_Z = 'A'..'Z'
            if (c is >= '0' and <= '9') { vk = c; return true; }            // VK_0..VK_9 = '0'..'9'
            return false;
        }
        if ((token[0] is 'F' or 'f') &&
            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) &&
            n is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + n - 1); // VK_F1 = 0x70
            return true;
        }
        return false;
    }
}

/// <summary>
/// [T62] <c>RegisterHotKey</c> kaydının ömrü. <b>Çakışmada sessiz devre dışı:</b> başka bir uygulama aynı
/// kombinasyonu tutuyorsa kayıt başarısız olur; uygulama bunu YUTAR (çökme/dialog YOK) ve yalnız global kısayol
/// çalışmaz. Kayıt/geri-alma fonksiyonları enjekte edilebilir (P/Invoke'suz test).
/// </summary>
public sealed class HotkeyRegistration : IDisposable
{
    public delegate bool RegisterFn(nint hwnd, int id, uint modifiers, uint virtualKey);
    public delegate void UnregisterFn(nint hwnd, int id);

    private readonly nint _hwnd;
    private readonly int _id;
    private readonly UnregisterFn _unregister;
    private bool _registered;

    private HotkeyRegistration(nint hwnd, int id, UnregisterFn unregister, bool registered)
    {
        _hwnd = hwnd;
        _id = id;
        _unregister = unregister;
        _registered = registered;
    }

    public bool IsRegistered => _registered;

    public static HotkeyRegistration Register(nint hwnd, int id, HotkeyBinding binding) =>
        Register(hwnd, id, binding, Win32.RegisterHotKey, (h, i) => Win32.UnregisterHotKey(h, i));

    public static HotkeyRegistration Register(
        nint hwnd, int id, HotkeyBinding binding, RegisterFn register, UnregisterFn unregister)
    {
        bool ok;
        try { ok = register(hwnd, id, binding.Modifiers, binding.VirtualKey); }
        catch { ok = false; } // sessiz devre dışı: hiçbir hata kullanıcıya/başlatmaya yansımaz
        return new HotkeyRegistration(hwnd, id, unregister, ok);
    }

    public void Dispose()
    {
        if (!_registered) return;
        _registered = false;
        try { _unregister(_hwnd, _id); } catch { /* pencere zaten yok */ }
    }
}
