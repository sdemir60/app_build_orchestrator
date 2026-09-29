using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62 / v7Δ-5] Global kısayollar (ayarlanabilir) — <c>RegisterHotKey</c>'e giden modifier/vk çevirisi saf, kaydın
/// kendisi P/Invoke. Kural: <b>çakışmada sessiz devre dışı</b> — başka bir uygulama aynı kombinasyonu tutuyorsa kayıt
/// başarısız döner ve uygulama bunu YUTAR (çökme/dialog YOK, yalnız o hotkey çalışmaz).
/// </summary>
public class HotkeyTests
{
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: tek bir global kısayol vardı, varsayılanı
    /// <c>Alt+B</c>'ydi ve pencereyi yalnız GETİRİRDİ. Kullanıcı Türkçe Q klavyede <c>Alt+B</c>'yi ters buldu
    /// (başparmak Alt'ta, işaret parmağı B'ye doğru kıvrılıyor) ve pencereyi açmadan derleme istedi. Artık İKİ global
    /// kısayol var: <c>Shift+Space</c> getir/gizle, <c>Ctrl+Shift+Space</c> pencere gelmeden Build.
    /// </summary>
    [Fact]
    public void The_two_global_hotkeys_default_to_shift_space_and_ctrl_shift_space()
    {
        var showHide = GlobalHotkeys.Get(GlobalHotkeyAction.ShowHide);
        var build = GlobalHotkeys.Get(GlobalHotkeyAction.Build);

        Assert.Equal("Shift+Space", showHide.DefaultGesture);
        Assert.Equal("Ctrl+Shift+Space", build.DefaultGesture);

        Assert.True(HotkeyBinding.TryParse(showHide.DefaultGesture, out var toggle));
        Assert.Equal(HotkeyBinding.MOD_SHIFT | HotkeyBinding.MOD_NOREPEAT, toggle.Modifiers);
        Assert.Equal(0x20u, toggle.VirtualKey); // VK_SPACE

        Assert.True(HotkeyBinding.TryParse(build.DefaultGesture, out var background));
        Assert.Equal(HotkeyBinding.MOD_CONTROL | HotkeyBinding.MOD_SHIFT | HotkeyBinding.MOD_NOREPEAT, background.Modifiers);
        Assert.Equal(0x20u, background.VirtualKey);
    }

    /// <summary>İki kayıt aynı pencereye düşer; <c>WM_HOTKEY</c>'in <c>wParam</c>'ı hangisinin basıldığını ancak
    /// id'ler ayrıysa söyler. Tablonun sırası da sabittir (About'un GLOBAL grubu bu sırayı çizer).</summary>
    [Fact]
    public void Each_global_hotkey_has_its_own_registration_id()
    {
        Assert.Equal([GlobalHotkeyAction.ShowHide, GlobalHotkeyAction.Build], GlobalHotkeys.All.Select(h => h.Action));
        Assert.Equal(GlobalHotkeys.All.Count, GlobalHotkeys.All.Select(h => h.Id).Distinct().Count());
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Getir/gizle: pencere YALNIZ gerçekten öndeyken (görünür, küçültülmemiş,
    /// aktif) gizlenir; tepsideyse, küçültülmüşse ya da başka bir pencerenin (ör. VS) ARKASINDAYSA öne gelir.
    /// Arkadaki görünür pencereyi gizlemek, kullanıcının "getir" dediği anda pencereyi kaybettirirdi.</summary>
    [Theory]
    [InlineData(false, false, false, WindowToggleAction.Show)] // tepside (gizli)
    [InlineData(true, true, false, WindowToggleAction.Show)]   // görev çubuğunda küçültülmüş
    [InlineData(true, false, false, WindowToggleAction.Show)]  // görünür ama VS'in arkasında
    [InlineData(true, false, true, WindowToggleAction.Hide)]   // önde
    public void The_show_hide_hotkey_hides_only_a_window_that_is_really_in_front(
        bool visible, bool minimized, bool active, WindowToggleAction expected)
        => Assert.Equal(expected, WindowToggle.Decide(visible, minimized, active));

    [Theory]
    [InlineData("ctrl+shift+f5", HotkeyBinding.MOD_CONTROL | HotkeyBinding.MOD_SHIFT | HotkeyBinding.MOD_NOREPEAT, 0x74u)]
    [InlineData("Win + Alt + 7", HotkeyBinding.MOD_WIN | HotkeyBinding.MOD_ALT | HotkeyBinding.MOD_NOREPEAT, 0x37u)]
    [InlineData("Control+B", HotkeyBinding.MOD_CONTROL | HotkeyBinding.MOD_NOREPEAT, 0x42u)]
    [InlineData("shift + space", HotkeyBinding.MOD_SHIFT | HotkeyBinding.MOD_NOREPEAT, 0x20u)]
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
    [InlineData("Space")]      // modifier'sız Space tüm sistemde boşluk tuşunu çalardı
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
    public void Conflicting_hotkey_is_silently_disabled() // başka uygulama aynı kombinasyonu tutuyor
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
