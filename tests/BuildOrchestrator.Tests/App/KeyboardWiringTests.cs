using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T46 · Fix Wave 1] Kısayol KABLAJININ regresyon guard'ı: <see cref="KeyboardShortcuts.WindowBindings"/>
/// (tuş+modifier→niyet) ve global kısayolun VM komutu (<see cref="GlobalHotkeys.CommandFor"/>). Niyetlerin GERÇEK
/// VM komutlarına bağlandığı katman <see cref="MainWindowInputTests"/>'tedir (pencerenin kendi KeyBinding'leri).
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: burada bir de
/// <c>Command_for_each_shortcut_action_maps_to_the_matching_vm_command</c> vardı — F5'in duruma-dallı kararı
/// (<c>ShortcutAction</c> Build/Rebuild/Stop) kod-tarafında <c>KeyboardShortcuts.CommandFor</c> ile VM komutuna
/// çevrilirdi ve test o eşlemeyi pinlerdi. F5 artık dallanmadığı için o ara katman kalktı: F5/F6/F7 KeyBinding'leri
/// DOĞRUDAN VM komutlarına bağlanır; aynı takas-yakalama iddiası (<c>ReferenceEquals</c>)
/// <see cref="MainWindowInputTests"/>'in F5/F6/F7 testine taşındı.</para>
/// </summary>
[Collection("Console UI (serial)")] // NewVm EngineHost/VM kurar — diğer WPF StaFact'larla seri (kaynak çekişmesi deseni)
public class KeyboardWiringTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    /// <summary>[kullanıcı kararı 2026-09-29] Global Build, pencere içindeki Build düğmesiyle AYNI komuttur
    /// (<c>ReferenceEquals</c>) — kapısı (topoloji, uçuşta koşu, workspace işi) ikinci kez yazılmaz. Getir/gizle bir VM
    /// komutu DEĞİLDİR (pencerenin kendi işi).</summary>
    [StaFact]
    public void The_background_build_hotkey_runs_the_view_models_own_build_command()
    {
        var vm = NewVm();
        Assert.Same(vm.BuildCommand, GlobalHotkeys.CommandFor(GlobalHotkeyAction.Build, vm));
        Assert.Null(GlobalHotkeys.CommandFor(GlobalHotkeyAction.ShowHide, vm));
    }

    // ------------------------------------------------------------------ tuş+modifier → niyet (SetupKeyboardShortcuts kablajı)

    /// <summary>
    /// [About] ESKİ İDDİA: "tabloda TAM 5 satır var". O sayı bir bütçe değil, günün kısayol kümesinin
    /// negatif-pin'iydi (yanlışlıkla eklenen bir bağlamayı yakalamak için). About ekranı F1'i ekledi, yani
    /// KURAL BİLEREK DEĞİŞTİ: satır sayısı 6'ya çıktı ve tablo <see cref="WindowIntent.ShowAbout"/>'u da
    /// taşıdı.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.0 §2.1/§2.11, D4/T8]</b> What's new About'un sekmesi olmaktan
    /// çıkıp kendi kısayolunu (Ctrl+F1) kazandı: satır sayısı 6'dan <b>7</b>'ye çıktı ve tablo artık
    /// <see cref="WindowIntent.ShowNotes"/>'u da taşıyor. Negatif-pin'in NİYETİ yine korunuyor — sayı tablodan
    /// türetilmiyor, açıkça yazılıyor ki fazladan ya da kayıp bir bağlama yine kırsın.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> Kısayollar YALNIZ kullanıcının onayladığı
    /// tablodakilerdir: F5 Build (duruma dallanmaz), F6 Rebuild, F7 Clean, Ctrl+F, F1, Esc — <b>6</b> satır.
    /// Ctrl+F5 / Shift+F5 (Rebuild) ve Ctrl+F1 (What's new) KALKTI; gerekçe
    /// <see cref="KeyboardShortcutTests.F5_is_bound_to_build_only"/>'da.</para>
    /// </summary>
    [Fact]
    public void The_window_binding_table_maps_each_key_gesture_to_the_correct_intent()
    {
        // Single: tam olarak BİR satır eşleşmezse (yanlış/eksik modifier veya tuş) fırlatır → yanlış kablaj kırar.
        WindowIntent Intent(Key key, ModifierKeys mods) =>
            KeyboardShortcuts.WindowBindings.Single(b => b.Key == key && b.Modifiers == mods).Intent;

        Assert.Equal(WindowIntent.Build, Intent(Key.F5, ModifierKeys.None));          // F5     → Build
        Assert.Equal(WindowIntent.Rebuild, Intent(Key.F6, ModifierKeys.None));        // F6     → Rebuild
        Assert.Equal(WindowIntent.Clean, Intent(Key.F7, ModifierKeys.None));          // F7     → Clean
        Assert.Equal(WindowIntent.FocusFilter, Intent(Key.F, ModifierKeys.Control));  // Ctrl+F → filtre odağı
        Assert.Equal(WindowIntent.ShowAbout, Intent(Key.F1, ModifierKeys.None));      // F1     → About
        Assert.Equal(WindowIntent.Escape, Intent(Key.Escape, ModifierKeys.None));     // Esc    → katman zinciri

        // Negatif-pin: tabloda TAM 6 satır — fazladan/kayıp bir bağlama (ör. yanlışlıkla eklenen Ctrl+P) kırar.
        Assert.Equal(6, KeyboardShortcuts.WindowBindings.Count);
    }
}
