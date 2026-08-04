using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About'un kabuğa bağlanması: title bar butonu, F1, Esc katman zinciri ve modal dışlama.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AboutWiringTests
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

    /// <summary>Buton gear'ın SAĞINDA, aynı grupta durur: grup kullanım sıklığı azalan sırada dizilir
    /// (layout &gt; settings &gt; about) ve Windows/Office geleneğinde Help/About uygulama komutlarının en
    /// sonundadır.</summary>
    [StaFact]
    public void The_info_button_sits_immediately_to_the_right_of_the_gear()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var group = (Panel)LogicalTreeHelper.GetParent(window.GearButton);
        int gear = group.Children.IndexOf(window.GearButton);
        int info = group.Children.IndexOf(window.InfoButton);

        Assert.True(gear >= 0, "gear butonu beklenen grupta değil");
        Assert.True(info >= 0, "info butonu gear ile AYNI grupta değil");
        Assert.Equal(gear + 1, info);
        GC.KeepAlive(window);
    }

    /// <summary>Butonun tooltip'i metni ELLE yazmaz — kısayol kataloğundan gelir (kopya YASAK); UIA adı ise
    /// kontrolün işlevini KISA tarif eder ve <see cref="AccessibilityNames"/>'tedir.</summary>
    [StaFact]
    public void The_info_button_reads_its_tooltip_from_the_shortcut_catalog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        // [design-v1.2.1 §2.1] Tooltip, katalog cümlesinin SONUNA jesti ekler: "… (F1)". Cümlenin kendisi
        // yine tek kaynaktan gelir — burada yazılan yalnız parantezli jest, o da katalogdan okunur.
        var tooltip = (ToolTip)window.InfoButton.ToolTip;
        var about = ShortcutCatalog.Get(ShortcutId.About);
        Assert.Equal($"{about.Description} ({about.Gestures[0]})", tooltip.Content);
        Assert.Equal(AccessibilityNames.About, AutomationProperties.GetName(window.InfoButton));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_info_button_opens_the_about_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        Click(window.InfoButton);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- F1

    [Fact]
    public void F1_is_bound_to_the_show_about_intent()
    {
        var binding = KeyboardShortcuts.WindowBindings.Single(b => b.Key == Key.F1);
        Assert.Equal(ModifierKeys.None, binding.Modifiers);
        Assert.Equal(WindowIntent.ShowAbout, binding.Intent);
    }

    /// <summary>Tablo doğru ama kablaj eksik olabilirdi: her satır GERÇEKTEN bir
    /// <see cref="KeyBinding"/>'e dönüşmüş mü.</summary>
    [StaFact]
    public void The_window_installs_a_key_binding_for_every_row_in_the_table()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);

        foreach (var row in KeyboardShortcuts.WindowBindings)
            Assert.Contains(window.InputBindings.OfType<KeyBinding>(),
                k => k.Key == row.Key && k.Modifiers == row.Modifiers);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void F1_opens_the_about_dialog_when_nothing_else_is_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.None);

        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [design-v1.2.1 §2.10] <b>F1 bir TOGGLE'dır</b> — açıkken kapatır.
    ///
    /// <para><b>ESKİ İDDİA:</b> "F1 yalnız açar; bir modal açıkken NO-OP'tur." O kural, kaydedilmemiş bir
    /// Settings taslağını korumak için konmuş AŞIRI TEDBİRLİ bir kapıydı. Tasarım aynı korumayı daha iyi
    /// sağlıyor: About Settings'in ÜSTÜNE açılır, Esc önce About'u kapatır, taslak yerinde kalır — F1'i
    /// büsbütün sağır etmeye gerek yok. Test silinmedi; yeni kuralı pinliyor.</para></summary>
    [StaFact]
    public void F1_toggles_the_about_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.None);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);

        Invoke(window, Key.F1, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[design-v1.2.1 §2.10] About, Settings'in ÜSTÜNE açılır ve taslağı YOK ETMEZ: iki katman
    /// birlikte durur, About üstte (XAML'de sonra geldiği için z-sırası doğru).</summary>
    [StaFact]
    public void F1_opens_about_on_top_of_settings_without_discarding_the_draft()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.GearButton);
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);
        var draft = window.SettingsOverlay.DataContext;

        Invoke(window, Key.F1, ModifierKeys.None);

        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);   // About AÇILDI
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility); // Settings DURUYOR
        Assert.Same(draft, window.SettingsOverlay.DataContext);              // taslak AYNI örnek — atılmadı

        // Z-sırası: About, Settings'ten SONRA gelir → üstte çizilir.
        var shell = (Panel)LogicalTreeHelper.GetParent(window.AboutOverlay);
        Assert.True(shell.Children.IndexOf(window.SettingsOverlay) < shell.Children.IndexOf(window.AboutOverlay));
        GC.KeepAlive(window);
    }

    /// <summary>[design-v1.2.1 §2.10] Esc önceliği: About açıkken Esc ÖNCE About'u kapatır, Settings'i
    /// bırakır. Zincir tek katman iner, alta sızmaz.</summary>
    [StaFact]
    public void Escape_closes_about_before_settings_when_both_are_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.GearButton);
        Invoke(window, Key.F1, ModifierKeys.None);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility); // ALT katman DURUYOR

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.SettingsOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Simetrik kapı: About açıkken gear Settings'i açmaz (iki modal aynı anda duramaz).</summary>
    [StaFact]
    public void The_gear_does_nothing_while_the_about_dialog_is_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.InfoButton);
        Click(window.GearButton);

        Assert.Equal(Visibility.Collapsed, window.SettingsOverlay.Visibility);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Esc zinciri

    /// <summary>Esc zinciri artık İKİ diyalogu da kapsar. Eskiden <c>dialogOpen</c> yalnız
    /// <c>SettingsOverlay</c>'e bakıyordu — About açıkken Esc alt katmana (popover/seçim) SIZARDI.</summary>
    [StaFact]
    public void Escape_closes_the_about_dialog_too()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.None);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Esc, About açıkken ALT KATMANA SIZMAZ: seçim duruyorsa bile önce diyalog kapanır
    /// (BuildApp.jsx:1311-1315 katman sırası).</summary>
    [StaFact]
    public void Escape_closes_the_about_dialog_before_clearing_a_selection()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("A", null));
        vm.SelectProject(MainWindowHost.IdOf("A"));

        Invoke(window, Key.F1, ModifierKeys.None);
        Invoke(window, Key.Escape, ModifierKeys.None);

        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        Assert.NotNull(vm.SelectedProjectId); // seçim DOKUNULMADAN duruyor
        GC.KeepAlive(window);
    }

    /// <summary>Settings açıkken Esc HÂLÂ Settings'i kapatır (regresyon ağı — iki dallı hâle geçerken
    /// yanlış dalı seçmek kolaydı).</summary>
    [StaFact]
    public void Escape_still_closes_the_settings_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Click(window.GearButton);
        Invoke(window, Key.Escape, ModifierKeys.None);

        Assert.Equal(Visibility.Collapsed, window.SettingsOverlay.Visibility);
        GC.KeepAlive(window);
    }
}
