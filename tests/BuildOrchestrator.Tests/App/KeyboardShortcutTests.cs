using System.Windows.Input;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T46 · K6 birebir] Klavye kısayol semantiğinin SAF karar kapısı (<see cref="KeyboardShortcuts"/>) —
/// otorite design-v1 <c>BuildApp.jsx:1305-1315</c> + v7 K6. F5'in duruma göre dallanması (Rebuild/Stop/
/// Continue/Build), Ctrl+F filtre odağı, Esc zincirinin KATMAN sırası ve NEGATİF-PIN'ler (çift-Shift YOK,
/// Ctrl+P YOK) burada pinlenir. WPF gerekmez (enum'lar WindowsBase) → hızlı [Fact].
/// </summary>
public class KeyboardShortcutTests
{
    // ------------------------------------------------------------------ F5 dallanması
    [Fact]
    public void Plain_f5_builds_when_idle()
        => Assert.Equal(ShortcutAction.Build,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.None, midRun: false, stopped: false));

    [Fact]
    public void Plain_f5_stops_while_running()
        => Assert.Equal(ShortcutAction.Stop,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.None, midRun: true, stopped: false));

    [Fact]
    public void Plain_f5_continues_when_stopped()
        => Assert.Equal(ShortcutAction.Continue,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.None, midRun: false, stopped: true));

    [Fact]
    public void Ctrl_f5_rebuilds()
        => Assert.Equal(ShortcutAction.Rebuild,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.Control, midRun: false, stopped: false));

    [Fact]
    public void Shift_f5_rebuilds()
        => Assert.Equal(ShortcutAction.Rebuild,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.Shift, midRun: false, stopped: false));

    [Fact]
    public void Modifier_f5_rebuilds_even_while_running()
        // Ctrl/Shift + F5 her zaman Rebuild'e dallanır (koşarken bile — CanExecute reddeder), BuildApp.jsx:1305.
        => Assert.Equal(ShortcutAction.Rebuild,
            KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.Control, midRun: true, stopped: false));

    // ------------------------------------------------------------------ Ctrl+F
    [Fact]
    public void Ctrl_f_focuses_the_project_filter()
        => Assert.Equal(ShortcutAction.FocusFilter,
            KeyboardShortcuts.Resolve(Key.F, ModifierKeys.Control, midRun: false, stopped: false));

    [Fact]
    public void Plain_f_is_not_a_shortcut()
        => Assert.Equal(ShortcutAction.None,
            KeyboardShortcuts.Resolve(Key.F, ModifierKeys.None, midRun: false, stopped: false));

    // ------------------------------------------------------------------ NEGATİF-PIN'ler
    [Fact]
    public void Ctrl_p_is_not_bound()
        => Assert.Equal(ShortcutAction.None,
            KeyboardShortcuts.Resolve(Key.P, ModifierKeys.Control, midRun: false, stopped: false));

    [Theory]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    public void A_bare_shift_press_is_not_bound(Key shift)
        // "Çift-Shift" bir komut paleti açmaz — Shift tek başına HİÇBİR kısayola bağlı değil.
        => Assert.Equal(ShortcutAction.None,
            KeyboardShortcuts.Resolve(shift, ModifierKeys.Shift, midRun: false, stopped: false));

    // ------------------------------------------------------------------ Esc zinciri (katman sırası)
    [Fact]
    public void Esc_closes_the_dialog_first_even_when_lower_layers_are_open()
        => Assert.Equal(EscAction.CloseDialog,
            KeyboardShortcuts.ResolveEsc(dialogOpen: true, popoverOrMenuOpen: true, hasSelection: true));

    [Fact]
    public void Esc_closes_popovers_before_clearing_selection()
        => Assert.Equal(EscAction.ClosePopovers,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: true, hasSelection: true));

    [Fact]
    public void Esc_clears_selection_when_nothing_else_is_open()
        => Assert.Equal(EscAction.ClearSelection,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: true));

    [Fact]
    public void Esc_does_nothing_when_no_layer_is_open()
        => Assert.Equal(EscAction.None,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false));
}
