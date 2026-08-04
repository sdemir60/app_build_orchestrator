using System.Windows.Input;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Shell;

/// <summary>[E5/T46] Bir kısayolun tetiklediği eylem — MainWindow bunu ilgili VM komutuna eşler (CanExecute'i
/// ONURLANDIRARAK; disabled ise no-op).</summary>
public enum ShortcutAction
{
    /// <summary>Bağlı bir kısayol yok (negatif-pin: Ctrl+P, çıplak Shift, vb.).</summary>
    None,
    Build,
    Rebuild,
    Stop,
    /// <summary>Proje filtre input'una odak (Ctrl+F).</summary>
    FocusFilter,
}

/// <summary>[E5/T46] Esc zincirinin kapatacağı EN ÜST açık katman. Yalnız biri döner — alt katmana SIZMAZ.</summary>
public enum EscAction
{
    None,
    CloseDialog,
    ClosePopovers,
    ClearSelection,
}

/// <summary>[E5/T46 · Fix Wave 1] Bir pencere-seviyesi tuş bağlamasının SEMANTİK NİYETİ — MainWindow bunları
/// InputBinding'lere (<see cref="KeyBinding"/>) çevirir. Çıplak F5'in duruma-dallı hali (<see cref="F5StateBranch"/>)
/// ile doğrudan Rebuild AYRI niyetlerdir; <see cref="FocusFilter"/>/<see cref="Escape"/> kod-tarafı aksiyonlardır.</summary>
public enum WindowIntent
{
    /// <summary>Ctrl/Shift+F5 → doğrudan <see cref="RunViewModel.RebuildCommand"/> (CanExecute onurlanır).</summary>
    Rebuild,
    /// <summary>Çıplak F5 → duruma göre Stop/Build (OnF5Pressed → <see cref="Resolve"/> → DispatchShortcut).</summary>
    F5StateBranch,
    /// <summary>Ctrl+F → proje filtre input'una odak.</summary>
    FocusFilter,
    /// <summary>Esc → EN ÜST açık katmanı kapat (dialog &gt; popover/menü &gt; seçim; bkz. <see cref="ResolveEsc"/>).</summary>
    Escape,
}

/// <summary>[E5/T46 · Fix Wave 1] Bir pencere kısayolu satırı: (tuş + modifier) → niyet. <see cref="KeyboardShortcuts.WindowBindings"/>
/// bu satırların SAF (WPF InputBinding'siz) tablosudur; test yanlış modifier/tuş/niyeti YAKALAR.</summary>
public readonly record struct WindowBinding(Key Key, ModifierKeys Modifiers, WindowIntent Intent);

/// <summary>
/// [E5/T46 · K6 birebir] Klavye kısayol semantiğinin SAF (WPF'siz, test edilebilir) karar kapısı — otorite
/// design-v1 prototip <c>BuildApp.jsx:1302-1319</c> + v7 K6. MainWindow yalnız bu kararı UYGULAR (InputBinding
/// kablajı + CanExecute).
///
/// <para><b>F5 (BuildApp.jsx:1305 + v7 K6 "koşarken Stop"):</b> Ctrl/Shift'li F5 → <see cref="ShortcutAction.Rebuild"/>
/// (koşarken bile — CanExecute reddeder); aksi halde koşuyorsa → <see cref="ShortcutAction.Stop"/>, değilse →
/// <see cref="ShortcutAction.Build"/>. <b>Ctrl+F</b> → filtre.
/// <b>Negatif-pin:</b> çift-Shift ve Ctrl+P BİLİNÇLİ olarak bağlı DEĞİL (yanlışlıkla eklenmesin).</para>
/// </summary>
public static class KeyboardShortcuts
{
    /// <summary>[E5/T46 · K6 birebir · Fix Wave 1] Pencere geneli TUŞ→NİYET bağlama tablosu (SAF). MainWindow.
    /// SetupKeyboardShortcuts bunu iterasyonla <see cref="KeyBinding"/>'lere çevirir — "hangi tuş+modifier hangi
    /// niyete bağlı" kararı BURADA tek yerde pinlenir (yanlış modifier/tuş/niyet testte kırar). Ctrl+F5 ve
    /// Shift+F5 ikisi de Rebuild; çıplak F5 duruma-dallı; Ctrl+F filtre; Esc katman zinciri. Otorite v7 K6 +
    /// BuildApp.jsx:1302-1319. Negatif-pin: Ctrl+P ve çıplak Shift bu tabloda YOK.</summary>
    public static IReadOnlyList<WindowBinding> WindowBindings { get; } =
    [
        new(Key.F5, ModifierKeys.Control, WindowIntent.Rebuild),        // Ctrl+F5  → Rebuild (doğrudan)
        new(Key.F5, ModifierKeys.Shift, WindowIntent.Rebuild),         // Shift+F5 → Rebuild (doğrudan)
        new(Key.F5, ModifierKeys.None, WindowIntent.F5StateBranch),    // çıplak F5 → Stop/Build (duruma göre)
        new(Key.F, ModifierKeys.Control, WindowIntent.FocusFilter),    // Ctrl+F   → proje filtre odağı
        new(Key.Escape, ModifierKeys.None, WindowIntent.Escape),       // Esc      → EN ÜST açık katman
    ];

    /// <summary>[E5/T46 · Fix Wave 1] Bir <see cref="ShortcutAction"/>'ı ilgili VM komutuna eşler (DispatchShortcut
    /// bunu kullanır). <see cref="ShortcutAction.None"/>/<see cref="ShortcutAction.FocusFilter"/> bir VM komutu
    /// DEĞİLDİR → <c>null</c> (FocusFilter'ı MainWindow ayrı ele alır). SAF: yalnız VM'in MEVCUT komut referanslarını
    /// döndürür — CanExecute'i ÇAĞIRAN onurlandırır (burada tetiklenmez).</summary>
    public static ICommand? CommandFor(ShortcutAction action, RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return action switch
        {
            ShortcutAction.Build => vm.BuildCommand,
            ShortcutAction.Rebuild => vm.RebuildCommand,
            ShortcutAction.Stop => vm.StopCommand,
            _ => null, // None, FocusFilter
        };
    }

    /// <summary>Tuş + modifier + VM durumundan hangi kısayol eyleminin tetikleneceğini verir. Bağlı değilse
    /// <see cref="ShortcutAction.None"/> (Ctrl+P/çıplak Shift dahil).</summary>
    /// <param name="midRun">Bir run uçuşta mı (IsRunning || IsStarting = RunViewModel.IsMidRunLocked).</param>
    public static ShortcutAction Resolve(Key key, ModifierKeys modifiers, bool midRun)
    {
        if (key == Key.F5)
        {
            // Ctrl VEYA Shift + F5 → her zaman Rebuild (koşarken bile — CanExecute reddeder), BuildApp.jsx:1305.
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return ShortcutAction.Rebuild;
            if (midRun) return ShortcutAction.Stop; // v7 K6: koşarken F5 = Stop
            // [B4] Eskiden stopped fazı burada Continue'ya dallanırdı; o yüzey kaldırıldı (Stop'tan sonra Build
            // baştan koşar), dolayısıyla koşmayan HER durumda F5 = Build. Faz parametresi de gerekmez.
            return ShortcutAction.Build;
        }
        // Ctrl+F (meta yok — Windows) → proje filtre input'u. Shift önemsiz (BuildApp.jsx:1306 ctrl||meta && f).
        if (key == Key.F && (modifiers & ModifierKeys.Control) != 0) return ShortcutAction.FocusFilter;
        return ShortcutAction.None;
    }

    /// <summary>Esc zinciri: EN ÜST açık katmanı kapatır (dialog &gt; popover/menü &gt; seçim), diğerine sızmaz
    /// (BuildApp.jsx:1311-1315). Filtre input'undaki Esc bu zincire ULAŞMAZ (yerel temizle+blur, handled).</summary>
    public static EscAction ResolveEsc(bool dialogOpen, bool popoverOrMenuOpen, bool hasSelection)
    {
        if (dialogOpen) return EscAction.CloseDialog;
        if (popoverOrMenuOpen) return EscAction.ClosePopovers;
        if (hasSelection) return EscAction.ClearSelection;
        return EscAction.None;
    }
}
