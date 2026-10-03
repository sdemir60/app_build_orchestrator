using System.Windows.Input;

namespace BuildOrchestrator.App.Shell;

/// <summary>[E5/T46] Esc zincirinin kapatacağı EN ÜST açık katman. Yalnız biri döner — alt katmana SIZMAZ.</summary>
public enum EscAction
{
    None,
    CloseDialog,
    ClosePopovers,
    ClearSelection,
    /// <summary>[kullanıcı kararı 2026-09-29] Çalışan Build/Rebuild/Clean'i durdur (graceful).</summary>
    StopRun,
    /// <summary>[kullanıcı kararı 2026-10-03] Durdurma zaten sürüyor: ikinci Esc hard stop'tur ("Stop now" — uçuştaki
    /// derlemeler hemen sonlandırılır).</summary>
    StopNow,
    /// <summary>Durdurulamayan bir iş sürüyor: nedenini konsola bir kez yaz.</summary>
    ExplainUnstoppable,
}

/// <summary>[kullanıcı kararı 2026-09-29] Esc zincirinin koşu katmanının girdisi — VM'in o anki durumu
/// (<c>RunViewModel.EscRunState</c>).</summary>
public enum EscRunState
{
    /// <summary>Durdurulacak ya da anlatılacak bir iş yok.</summary>
    Idle,
    /// <summary>Bir koşu uçuşta ya da işaretleniyor; Stop alınabilir.</summary>
    Stoppable,
    /// <summary>Stop zaten istendi (uçuştakiler bitiyor); ikinci Esc hard stop'tur.</summary>
    Stopping,
    /// <summary>Kullanıcıya görünen, durdurulamayan bir workspace işi sürüyor (Sync, Deep Clean, Optimize, checkout, pull).</summary>
    Unstoppable,
}

/// <summary>[E5/T46 · Fix Wave 1] Bir pencere-seviyesi tuş bağlamasının SEMANTİK NİYETİ — MainWindow bunları
/// InputBinding'lere (<see cref="KeyBinding"/>) çevirir. <see cref="Build"/>/<see cref="Rebuild"/>/<see cref="Clean"/>
/// doğrudan VM komutlarıdır (CanExecute onurlanır); <see cref="FocusFilter"/>/<see cref="ShowAbout"/>/
/// <see cref="Escape"/> kod-tarafı aksiyonlardır.</summary>
public enum WindowIntent
{
    /// <summary>F5 → <c>RunViewModel.BuildCommand</c>. Duruma DALLANMAZ: koşarken Build'in kapısı kapalıdır, yani
    /// F5 hiçbir şey yapmaz (durdurmak Esc'in işidir).</summary>
    Build,
    /// <summary>F6 → <c>RunViewModel.RebuildCommand</c>.</summary>
    Rebuild,
    /// <summary>F7 → <c>RunViewModel.CleanAllCommand</c> — Build menüsünün Clean'i (her projede <c>-t:Clean</c>);
    /// bakım kutusundaki derin Clean DEĞİL.</summary>
    Clean,
    /// <summary>Ctrl+F → proje filtre input'una odak.</summary>
    FocusFilter,
    /// <summary>[About] F1 → About diyaloğu (sürüm, kısayollar, tanı) — Windows'un Help geleneği. TOGGLE'dır
    /// (açıkken tekrar basmak kapatır) ve başka bir modal AÇIKKEN de çalışır: About üste biner, alttaki
    /// taslağı YOK ETMEZ, Esc en üst katmanı indirir (bkz. <see cref="KeyboardShortcuts.ResolveEsc"/>).</summary>
    ShowAbout,
    /// <summary>Esc → EN ÜST açık katmanı kapat (bkz. <see cref="KeyboardShortcuts.ResolveEsc"/>).</summary>
    Escape,
}

/// <summary>[E5/T46 · Fix Wave 1] Bir pencere kısayolu satırı: (tuş + modifier) → niyet. <see cref="KeyboardShortcuts.WindowBindings"/>
/// bu satırların SAF (WPF InputBinding'siz) tablosudur; test yanlış modifier/tuş/niyeti YAKALAR.</summary>
public readonly record struct WindowBinding(Key Key, ModifierKeys Modifiers, WindowIntent Intent);

/// <summary>
/// [E5/T46 · kullanıcı kararı 2026-09-29] Pencere kısayollarının SAF (WPF'siz, test edilebilir) tablosu ve Esc
/// zincirinin kararı. MainWindow yalnız UYGULAR (InputBinding kablajı + CanExecute).
///
/// <para><b>Kısayollar yalnız kullanıcının onayladığı tablodakilerdir</b> — F5 Build, F6 Rebuild, F7 Clean, Ctrl+F,
/// F1, Esc (global iki kısayol <see cref="GlobalHotkeys"/>'te). F5 duruma dallanmaz: VS'te F5 koşan bir şeyi asla
/// durdurmaz. Eski Ctrl+F5 / Shift+F5 (Rebuild) ve Ctrl+F1 (What's new) yoktur — Shift+F5 VS'te Stop Debugging'dir
/// ve alışkanlıkla basıldığında Rebuild başlatıyordu. <b>Negatif-pin:</b> çift-Shift ve Ctrl+P BİLİNÇLİ olarak
/// bağlı DEĞİL (yanlışlıkla eklenmesin).</para>
/// </summary>
public static class KeyboardShortcuts
{
    /// <summary>[E5/T46 · kullanıcı kararı 2026-09-29] Pencere geneli TUŞ→NİYET bağlama tablosu (SAF). MainWindow.
    /// SetupKeyboardShortcuts bunu iterasyonla <see cref="KeyBinding"/>'lere çevirir — "hangi tuş+modifier hangi
    /// niyete bağlı" kararı BURADA tek yerde pinlenir (yanlış modifier/tuş/niyet testte kırar).</summary>
    public static IReadOnlyList<WindowBinding> WindowBindings { get; } =
    [
        new(Key.F5, ModifierKeys.None, WindowIntent.Build),            // F5     → Build (yalnız başlatır)
        new(Key.F6, ModifierKeys.None, WindowIntent.Rebuild),          // F6     → Rebuild
        new(Key.F7, ModifierKeys.None, WindowIntent.Clean),            // F7     → Clean (-t:Clean)
        new(Key.F, ModifierKeys.Control, WindowIntent.FocusFilter),    // Ctrl+F → proje filtre odağı
        new(Key.F1, ModifierKeys.None, WindowIntent.ShowAbout),        // F1     → About (Windows Help geleneği)
        new(Key.Escape, ModifierKeys.None, WindowIntent.Escape),       // Esc    → EN ÜST açık katman
    ];

    /// <summary>Esc zinciri: EN ÜST açık katmanı kapatır (dialog &gt; popover/menü &gt; seçim), diğerine sızmaz
    /// (BuildApp.jsx:1311-1315). Filtre input'undaki Esc bu zincire ULAŞMAZ (yerel temizle+blur, handled).
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: açık katman yoksa Esc hiçbir şey
    /// yapmazdı. Zincirin son halkası artık KOŞUDUR: çalışan Build/Rebuild/Clean durdurulur (graceful — biten
    /// projeler kaydedilir, sonraki Build kaldığı yerden devam eder), durdurulamayan bir iş sürüyorsa nedeni söylenir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-03]</b> ESKİ İDDİA: durdurma zaten sürüyorsa ikinci Esc ikinci
    /// bir stop GÖNDERMEZ, şerit "duyuldu" der. GEREKÇE: drain en yavaş projenin kalan süresi kadar sürebilir; ikinci Esc
    /// beklemek istemediğini söyler ve hard stop'tur (<see cref="EscAction.StopNow"/> — uçuştakiler öldürülür, sonraki
    /// Build onları baştan derler). Üçüncü Esc'i komutun kapısı yutar.</para></summary>
    public static EscAction ResolveEsc(bool dialogOpen, bool popoverOrMenuOpen, bool hasSelection, EscRunState run)
    {
        if (dialogOpen) return EscAction.CloseDialog;
        if (popoverOrMenuOpen) return EscAction.ClosePopovers;
        if (hasSelection) return EscAction.ClearSelection;
        return run switch
        {
            EscRunState.Stoppable => EscAction.StopRun,
            EscRunState.Stopping => EscAction.StopNow,
            EscRunState.Unstoppable => EscAction.ExplainUnstoppable,
            _ => EscAction.None,
        };
    }
}
