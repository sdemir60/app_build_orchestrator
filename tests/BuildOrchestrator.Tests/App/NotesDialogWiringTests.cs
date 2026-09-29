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
/// dişli ile ⓘ arasında), Esc katman zinciri (What's new en üstte) ve okunmadı noktası.
/// <see cref="AboutWiringTests"/>'in AYNI deseniyle yazılmıştır — kopya değil, AYRI bir kablaj yüzeyi
/// (sparkle butonu About'un info butonundan bağımsız bir kontroldür). [kullanıcı kararı 2026-09-29] What's new'in
/// klavye kısayolu (Ctrl+F1) YOKTUR — gerekçe <see cref="Whats_new_has_no_keyboard_shortcut"/>'ta.
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

    /// <summary>Butonun tooltip'i metni ELLE yazmaz — cümlenin tek yeri <see cref="ReleaseNotes.WhatsNewTooltip"/>'tır
    /// (kopya YASAK).
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: cümle kısayol kataloğundan gelirdi ve
    /// sonuna jest eklenirdi — <c>"What's new — release notes (Ctrl+F1)"</c>. Ctrl+F1 kalktı; What's new artık bir
    /// kısayol DEĞİLDİR, katalogda satırı yoktur ve tooltip jest taşımaz.</para></summary>
    [StaFact]
    public void The_notes_button_tooltip_carries_no_gesture_when_seen()
    {
        using var temp = new TempDir();
        var store = MainWindowHost.UiStateStore(temp);
        var state = store.Load();
        state.SeenVersion = AppIdentity.Version; // görülmüş — nokta yok, sabit cümle
        store.Save(state);

        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var tooltip = (ToolTip)window.NotesButton.ToolTip;
        Assert.Equal(ReleaseNotes.WhatsNewTooltip, tooltip.Content);
        Assert.Equal(Visibility.Collapsed, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.13.1 §2.11] Görülmemiş sürümde cümle DEĞİŞİR: "What's new in &lt;sürüm&gt;". Taze
    /// bir TempDir'de (SeenVersion yazılmamış) durum TAM OLARAK budur. [kullanıcı kararı 2026-09-29] Eskiden
    /// sonuna <c>(Ctrl+F1)</c> eklenirdi; kısayol kalktığı için jest yoktur.</summary>
    [StaFact]
    public void The_notes_button_tooltip_names_the_version_when_unseen()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var tooltip = (ToolTip)window.NotesButton.ToolTip;
        Assert.Equal(ReleaseNotes.WhatsNewInLabel(AppIdentity.Version), tooltip.Content);
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

    // ---------------------------------------------------------------- klavye kısayolu YOK

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA (design v1.13.0 §2.11): Ctrl+F1 What's new'i
    /// açan bir TOGGLE'dı ve üç test onu pinlerdi — tabloda ShowNotes niyetine bağlı olması, hiçbir şey açık değilken
    /// açması ve tekrar basınca kapatması. Kullanıcı kısayolları kendi onayladığı tabloyla sınırladı ve Ctrl+F1 o
    /// tabloda yok: What's new artık yalnız sparkle butonundan ve About'un "What's new in …" butonundan açılır.
    /// Bu test, pencerenin GERÇEK bağlamalarında Ctrl+F1'in bulunmadığını pinler.
    /// </summary>
    [StaFact]
    public void Whats_new_has_no_keyboard_shortcut()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);

        Assert.DoesNotContain(window.InputBindings.OfType<KeyBinding>(),
            k => k.Key == Key.F1 && k.Modifiers == ModifierKeys.Control);
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

        Click(window.NotesButton);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [design v1.13.0 §2.11] Esc sırası: <b>What's new → About → Settings</b> — What's new en üst katmandır
    /// (prototipte zIndex 101, About 100); Esc her zaman EN ÜST katmanı indirir, alta sızmaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: üç diyalog BİRLİKTE açık
    /// durabilirdi (Settings → F1 → Ctrl+F1) ve üç Esc onları sırayla indirirdi. Üçlü yığına yalnız Ctrl+F1
    /// ulaşıyordu; o kısayol kalktı. Bugünkü yol Settings'in üstüne F1 ile About, About'un "What's new in …"
    /// butonuyla What's new'dir — About kendini kapattığı için yığın Settings + What's new'dir. Zincir aynı:
    /// Esc önce What's new'i, sonra Settings'i indirir.</para>
    /// </summary>
    [StaFact]
    public void Escape_closes_notes_before_settings_when_whats_new_is_opened_from_about_over_settings()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.GearButton);
        Invoke(window, Key.F1, ModifierKeys.None);
        window.AboutOverlay.UpdateLayout();
        Click(window.AboutOverlay.WhatsNewButton);

        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.NotesOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.NotesOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility); // ALT katman DURUYOR

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
        var store = MainWindowHost.UiStateStore(temp);
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
        var store = MainWindowHost.UiStateStore(temp);
        var state = store.Load();
        state.SeenVersion = "0.0.0-not-the-running-version";
        store.Save(state);

        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Visible, window.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.13.0/v1.13.1 §2.11] Diyalog GÖRÜLÜNCE (açıldığı anda) nokta söner, tooltip sabit
    /// cümleye döner ve karar kalıcı duruma yazılır (uygulama yeniden açılınca nokta geri gelmez) — About'un
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
        Assert.Equal(ReleaseNotes.WhatsNewTooltip, ((ToolTip)window.NotesButton.ToolTip).Content);

        // ...ve karar KALICI: aynı state dizinini okuyan yeni bir pencerede nokta hiç doğmaz.
        var (again, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(again);
        Assert.Equal(Visibility.Collapsed, again.UnseenNotesDot.Visibility);
        GC.KeepAlive(window);
        GC.KeepAlive(again);
    }
}
