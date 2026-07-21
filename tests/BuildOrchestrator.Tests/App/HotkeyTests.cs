using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62 / v7Δ-5] Global kısayol <b>Alt+B</b> (ayarlanabilir) — <c>RegisterHotKey</c>'e giden modifier/vk çevirisi
/// saf, kaydın kendisi P/Invoke. Kural: <b>çakışmada sessiz devre dışı</b> — başka bir uygulama aynı kombinasyonu
/// tutuyorsa kayıt başarısız döner ve uygulama bunu YUTAR (çökme/dialog YOK, yalnız hotkey çalışmaz).
/// </summary>
public class HotkeyTests
{
    [Fact]
    public void Alt_b_parses_to_mod_alt_plus_norepeat_and_vk_42()
    {
        Assert.True(HotkeyBinding.TryParse(HotkeyBinding.DefaultGesture, out var binding));

        Assert.Equal("Alt+B", HotkeyBinding.DefaultGesture);
        Assert.Equal(HotkeyBinding.MOD_ALT | HotkeyBinding.MOD_NOREPEAT, binding.Modifiers);
        Assert.Equal(0x42u, binding.VirtualKey); // VK_B
    }

    [Theory]
    [InlineData("ctrl+shift+f5", HotkeyBinding.MOD_CONTROL | HotkeyBinding.MOD_SHIFT | HotkeyBinding.MOD_NOREPEAT, 0x74u)]
    [InlineData("Win + Alt + 7", HotkeyBinding.MOD_WIN | HotkeyBinding.MOD_ALT | HotkeyBinding.MOD_NOREPEAT, 0x37u)]
    [InlineData("Control+B", HotkeyBinding.MOD_CONTROL | HotkeyBinding.MOD_NOREPEAT, 0x42u)]
    public void Configured_gestures_parse_case_and_space_insensitively(string gesture, uint modifiers, uint vk)
    {
        Assert.True(HotkeyBinding.TryParse(gesture, out var binding));
        Assert.Equal(modifiers, binding.Modifiers);
        Assert.Equal(vk, binding.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("B")]          // modifier'sız — global hotkey olarak kabul edilmez
    [InlineData("Alt+")]
    [InlineData("Alt+Nope")]
    [InlineData("Alt+F25")]
    public void Unusable_gestures_are_rejected_without_throwing(string? gesture)
        => Assert.False(HotkeyBinding.TryParse(gesture, out _));

    [Fact]
    public void Successful_registration_is_active_and_unregisters_once_on_dispose()
    {
        var unregistered = new List<int>();
        var registration = HotkeyRegistration.Register(hwnd: 7, id: 9000,
            binding: new HotkeyBinding(HotkeyBinding.MOD_ALT, 0x42),
            register: (_, _, _, _) => true,
            unregister: (_, id) => unregistered.Add(id));

        Assert.True(registration.IsRegistered);

        registration.Dispose();
        registration.Dispose();

        Assert.Equal([9000], unregistered);
    }

    [Fact]
    public void Conflicting_hotkey_is_silently_disabled() // başka uygulama Alt+B'yi tutuyor
    {
        var unregistered = new List<int>();
        var registration = HotkeyRegistration.Register(hwnd: 7, id: 9000,
            binding: new HotkeyBinding(HotkeyBinding.MOD_ALT, 0x42),
            register: (_, _, _, _) => false,
            unregister: (_, id) => unregistered.Add(id));

        Assert.False(registration.IsRegistered);

        registration.Dispose();

        Assert.Empty(unregistered); // kaydedilmediyse geri alınacak bir şey de yok
    }

    [Fact]
    public void A_throwing_register_call_is_swallowed_too()
    {
        var registration = HotkeyRegistration.Register(hwnd: 7, id: 9000,
            binding: new HotkeyBinding(HotkeyBinding.MOD_ALT, 0x42),
            register: (_, _, _, _) => throw new InvalidOperationException("P/Invoke patladı"),
            unregister: (_, _) => { });

        Assert.False(registration.IsRegistered);
    }
}
