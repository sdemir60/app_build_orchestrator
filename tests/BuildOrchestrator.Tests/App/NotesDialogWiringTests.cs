using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.13.0/v1.13.1 §2.1/§2.11 · D4/T8] What's new'in kabuğa bağlanması: title bar butonu (sparkle,
/// dişli ile ⓘ arasında), Ctrl+F1 (toggle), Esc katman zinciri (What's new en üstte) ve okunmadı noktası.
/// <see cref="AboutWiringTests"/>'in AYNI deseniyle yazılmıştır — kopya değil, AYRI bir kablaj yüzeyi
/// (sparkle butonu About'un info butonundan bağımsız bir kontroldür).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class NotesDialogWiringTests
{
    private static void Click(ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    /// <summary>Pencere-seviyesi bir tuş bağlamasını, üretimdeki yolun AYNISIYLA (InputBinding'in komutu)
    /// tetikler — WPF olay yönlendirmesi gerçek bir HWND olmadan güvenilir değildir.</summary>
    private static void Invoke(MainWindow window, Key key, ModifierKeys modifiers)
    {
        var binding = window.InputBindings.OfType<KeyBinding>()
            .Single(k => k.Key == key && k.Modifiers == modifiers);
        if (binding.Command.CanExecute(null)) binding.Command.Execute(null);
    }

    // ---------------------------------------------------------------- title bar butonu

    /// <summary>[design v1.13.0 §2.1] Buton dişli ile ⓘ ARASINDA durur — ikisiyle AYNI grupta, ikisinin tam
    /// ortasında.</summary>
    [StaFact]
    public void The_notes_button_sits_between_the_gear_and_the_info_button()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var group = (Panel)LogicalTreeHelper.GetParent(window.GearButton);
        int gear = group.Children.IndexOf(window.GearButton);
        int notes = group.Children.IndexOf(window.NotesButton);
        int info = group.Children.IndexOf(window.InfoButton);

        Assert.True(gear >= 0, "gear butonu beklenen grupta değil");
        Assert.True(notes >= 0, "sparkle butonu gear ile AYNI grupta değil");
        Assert.Equal(gear + 1, notes);
        Assert.Equal(notes + 1, info);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_notes_button_has_the_expected_uia_name()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(AccessibilityNames.WhatsNew, AutomationProperties.GetName(window.NotesButton));
        GC.KeepAlive(window);
    }

    /// <summary>Butonun tooltip'i metni ELLE yazmaz — kısayol kataloğundan gelir (kopya YASAK); "About'un
    /// deseni birebir budur" (brief) — <see cref="MainWindow"/>'daki <c>SetupAboutButtonTooltip</c> ile
    /// AYNI kalıp.</summary>
    [StaFact]
    public void The_notes_button_reads_its_tooltip_from_the_shortcut_catalog_when_seen()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        var state = store.Load();
        state.SeenVersion = AppIdentity.Version; // görülmüş — nokta yok, cümle katalogdan
        store.Save(state);

        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var tooltip = (ToolTip)window.NotesButton.ToolTip;
        var notes = ShortcutCatalog.Get(ShortcutId.WhatsNew);
        Assert.Equal($"{notes.Description} ({notes.Gestures[0]})", tooltip.Content);
        Assert.Equal(Visibility.Collapsed, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.13.1 §2.11] Görülmemiş sürümde cümle DEĞİŞİR: "What's new in &lt;sürüm&gt;". Taze
    /// bir TempDir'de (SeenVersion yazılmamış) durum TAM OLARAK budur.</summary>
    [StaFact]
    public void The_notes_button_tooltip_names_the_version_when_unseen()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var tooltip = (ToolTip)window.NotesButton.ToolTip;
        var notes = ShortcutCatalog.Get(ShortcutId.WhatsNew);
        Assert.Equal($"What's new in {AppIdentity.Version} ({notes.Gestures[0]})", tooltip.Content);
        Assert.Equal(Visibility.Visible, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_notes_button_opens_the_notes_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        Click(window.NotesButton);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Ctrl+F1

    [Fact]
    public void Ctrl_f1_is_bound_to_the_show_notes_intent()
    {
        var binding = KeyboardShortcuts.WindowBindings.Single(b => b.Key == Key.F1 && b.Modifiers == ModifierKeys.Control);
        Assert.Equal(WindowIntent.ShowNotes, binding.Intent);
    }

    [StaFact]
    public void Ctrl_f1_opens_the_notes_dialog_when_nothing_else_is_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.Control);

        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.13.0 §2.11] Ctrl+F1 bir TOGGLE'dır — açıkken tekrar basmak kapatır (About'un
    /// F1'inden FARKI budur: About toggle'ı zaten vardı, What's new de AYNI deseni alır).</summary>
    [StaFact]
    public void Ctrl_f1_toggles_the_notes_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.Control);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);

        Invoke(window, Key.F1, ModifierKeys.Control);
        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Simetrik kapı: What's new açıkken gear Settings'i açmaz (About'un kendi kapısıyla AYNI kural —
    /// <c>AnyDialogOpen</c> ÜÇ diyaloğu da kapsar).</summary>
    [StaFact]
    public void The_gear_does_nothing_while_the_notes_dialog_is_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.NotesButton);
        Click(window.GearButton);

        Assert.Equal(Visibility.Collapsed, window.SettingsOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Esc zinciri

    [StaFact]
    public void Escape_closes_the_notes_dialog_too()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.Control);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [design v1.13.0 §2.11] Esc sırası: <b>What's new → About → Settings</b> — What's new en üst katmandır
    /// (prototipte zIndex 101, About 100). Üçü birlikte açık durabilir (yığılır); Esc her zaman EN ÜST
    /// katmanı indirir. About'un kendi Esc-önceliği testiyle AYNI desen, bir katman daha üsttedir.
    /// </summary>
    [StaFact]
    public void Escape_closes_notes_before_about_and_about_before_settings_when_all_three_are_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.GearButton);
        Invoke(window, Key.F1, ModifierKeys.None);
        Invoke(window, Key.F1, ModifierKeys.Control);

        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility); // üçü BİRLİKTE açık

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);   // ALT katmanlar DURUYOR
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.SettingsOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- okunmadı noktası — üç durum

    /// <summary>[design v1.13.1 §2.11] Kayıt yoksa (ilk kurulum) nokta VARDIR.</summary>
    [StaFact]
    public void The_dot_shows_when_no_version_was_ever_recorded_as_seen()
    {
        using var temp = new TempDir(); // taze — SeenVersion hiç yazılmadı
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Visible, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Kayıtlı sürüm ÇALIŞAN sürümle eşleşiyorsa nokta YOKTUR.</summary>
    [StaFact]
    public void The_dot_hides_when_the_recorded_version_matches_the_running_version()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        var state = store.Load();
        state.SeenVersion = AppIdentity.Version;
        store.Save(state);

        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Collapsed, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Kayıtlı sürüm ÇALIŞAN sürümden FARKLIYSA (eski bir sürümde görüldü) nokta VARDIR — "kayıt yok"
    /// durumuyla AYNI sonuç ama FARKLI bir dal (null değil, eşleşmeyen bir değer); bu test ikisini ayırır.</summary>
    [StaFact]
    public void The_dot_shows_when_the_recorded_version_differs_from_the_running_version()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        var state = store.Load();
        state.SeenVersion = "0.0.0-not-the-running-version";
        store.Save(state);

        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Visible, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.13.0/v1.13.1 §2.11] Diyalog GÖRÜLÜNCE (açıldığı anda) nokta söner, tooltip katalog
    /// cümlesine döner ve karar kalıcı duruma yazılır (uygulama yeniden açılınca nokta geri gelmez) — About'un
    /// eski <c>Seeing_the_whats_new_tab_clears_the_unseen_mark_for_good</c> testiyle AYNI kural, artık bir
    /// SEKME değil DİYALOĞUN AÇILIŞI tetikleyici.</summary>
    [StaFact]
    public void Opening_the_notes_dialog_clears_the_unseen_mark_for_good()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        Assert.Equal(Visibility.Visible, window.UnseenNotesDot.Visibility); // ön-koşul

        Click(window.NotesButton);

        Assert.Equal(Visibility.Collapsed, window.UnseenNotesDot.Visibility);
        var notes = ShortcutCatalog.Get(ShortcutId.WhatsNew);
        Assert.Equal($"{notes.Description} ({notes.Gestures[0]})", ((ToolTip)window.NotesButton.ToolTip).Content);

        // ...ve karar KALICI: aynı state dizinini okuyan yeni bir pencerede nokta hiç doğmaz.
        var (again, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(again);
        Assert.Equal(Visibility.Collapsed, again.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
        GC.KeepAlive(again);
    }
}
