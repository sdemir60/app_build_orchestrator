using System.Windows.Input;

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
    Continue,
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

/// <summary>
/// [E5/T46 · K6 birebir] Klavye kısayol semantiğinin SAF (WPF'siz, test edilebilir) karar kapısı — otorite
/// design-v1 prototip <c>BuildApp.jsx:1302-1319</c> + v7 K6. MainWindow yalnız bu kararı UYGULAR (InputBinding
/// kablajı + CanExecute).
///
/// <para><b>F5 (BuildApp.jsx:1305 + v7 K6 "koşarken Stop"):</b> Ctrl/Shift'li F5 → <see cref="ShortcutAction.Rebuild"/>
/// (koşarken bile — CanExecute reddeder); aksi halde koşuyorsa → <see cref="ShortcutAction.Stop"/>, stopped'ta →
/// <see cref="ShortcutAction.Continue"/>, değilse → <see cref="ShortcutAction.Build"/>. <b>Ctrl+F</b> → filtre.
/// <b>Negatif-pin:</b> çift-Shift ve Ctrl+P BİLİNÇLİ olarak bağlı DEĞİL (yanlışlıkla eklenmesin).</para>
/// </summary>
public static class KeyboardShortcuts
{
    /// <summary>Tuş + modifier + VM durumundan hangi kısayol eyleminin tetikleneceğini verir. Bağlı değilse
    /// <see cref="ShortcutAction.None"/> (Ctrl+P/çıplak Shift dahil).</summary>
    /// <param name="midRun">Bir run uçuşta mı (IsRunning || IsStarting = RunViewModel.IsMidRunLocked).</param>
    /// <param name="stopped">Faz <c>Stopped</c> mı (Continue erişilebilir).</param>
    public static ShortcutAction Resolve(Key key, ModifierKeys modifiers, bool midRun, bool stopped)
    {
        if (key == Key.F5)
        {
            // Ctrl VEYA Shift + F5 → her zaman Rebuild (koşarken bile — CanExecute reddeder), BuildApp.jsx:1305.
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return ShortcutAction.Rebuild;
            if (midRun) return ShortcutAction.Stop;      // v7 K6: koşarken F5 = Stop
            if (stopped) return ShortcutAction.Continue; // stopped'ta F5 = Continue
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
