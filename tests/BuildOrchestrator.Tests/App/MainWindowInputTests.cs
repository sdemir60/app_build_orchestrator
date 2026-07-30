using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · maddeler 1.5 + 1.6] <see cref="MainWindow"/>'un GERÇEK girdi kablajı.
///
/// <para><b>1.5 ölçülmüş boşluk:</b> iki SAF seam pinliydi (<c>KeyboardShortcutTests</c> tuş→niyet,
/// <c>KeyboardWiringTests</c> niyet→komut) ama <c>MainWindow.xaml.cs:234-235</c>'teki foreach — tabloyu
/// GERÇEK <see cref="InputBindings"/>'e çeviren tek satır — testsizdi. O foreach silinse iki saf test de yeşil
/// kalır, uygulamada HİÇBİR kısayol çalışmazdı.</para>
///
/// <para><b>Tetik nasıl "gerçek":</b> pencere <c>Show()</c> EDİLEMEZ (<c>OnSourceInitialized</c> tepsi ikonu +
/// global hotkey + Snap Layouts hook'u kurar — bir testin yan etkisi olamaz; bkz. <c>MainWindowRealizeTests</c>).
/// HWND olmadan <see cref="KeyEventArgs"/> kurulamaz (ctor bir <see cref="PresentationSource"/> ister), yani
/// fiziksel tuş basışı bu kapsamda ÜRETİLEMEZ. Bunun yerine kısayolun ÜRETİMDEKİ nesnesi —
/// <see cref="KeyBinding"/> — pencerenin kendi koleksiyonundan okunur: WPF bir tuşa basıldığında tam olarak bu
/// nesneyi bulup <c>Command</c>'ını çalıştırır. Test hem eşlemeyi hem de komutun GERÇEK etkisini o nesne
/// üzerinden sürer.</para>
///
/// <para><b>1.6 ölçülmüş boşluk:</b> <c>MainWindowRealizeTests.cs:114-117</c> layout ikonları için yalnız
/// <c>Assert.NotNull(...Style)</c> diyordu — klasik "hep yeşil". Buradaki testler üç butona GERÇEK
/// <see cref="ButtonBase.ClickEvent"/> yükseltir ve kabuğun modunun değiştiğini doğrular.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class MainWindowInputTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>MainWindowRealizeTests.NewMainWindow ile AYNI kurulum: var olmayan supervisor yolu + pencere
    /// hiç <c>Show()</c> edilmez (Loaded/OnSourceInitialized tetiklenmez).</summary>
    private static (MainWindow window, RunViewModel vm) NewMainWindow()
    {
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        return (new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope()), vm);
    }

    private static IReadOnlyList<KeyBinding> KeyBindingsOf(MainWindow window) =>
        [.. window.InputBindings.OfType<KeyBinding>()];

    // ---------------------------------------------------------------- 1.5 pencere kısayolları

    /// <summary>Tablo → <see cref="InputBindings"/> dönüşümü BİREBİR: her satır için TAM BİR
    /// <see cref="KeyBinding"/> ve fazlası yok. Foreach silinirse koleksiyon boş kalır → kırmızı.</summary>
    [StaFact]
    public void Every_window_shortcut_row_becomes_a_real_key_binding_on_the_window()
    {
        var (window, _) = NewMainWindow();
        var bindings = KeyBindingsOf(window);

        Assert.Equal(KeyboardShortcuts.WindowBindings.Count, bindings.Count);
        foreach (var row in KeyboardShortcuts.WindowBindings)
        {
            var binding = Assert.Single(bindings, b => b.Key == row.Key && b.Modifiers == row.Modifiers);
            Assert.NotNull(binding.Command);
        }
        GC.KeepAlive(window);
    }

    /// <summary>Aynı niyetin İKİ tuşu (Ctrl+F5 / Shift+F5) AYNI komut nesnesine — ve o nesne VM'in GERÇEK
    /// <see cref="RunViewModel.RebuildCommand"/>'ıdır (<c>ReferenceEquals</c>). Yanlış bir sözlük arm'ı
    /// (ör. Rebuild→BuildCommand) burada kırar.</summary>
    [StaFact]
    public void Both_rebuild_gestures_are_bound_to_the_view_models_own_rebuild_command()
    {
        var (window, vm) = NewMainWindow();
        var bindings = KeyBindingsOf(window);

        var ctrlF5 = bindings.Single(b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.Control);
        var shiftF5 = bindings.Single(b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.Shift);

        Assert.Same(vm.RebuildCommand, ctrlF5.Command);
        Assert.Same(vm.RebuildCommand, shiftF5.Command);
        // Çıplak F5 duruma-dallı bir kod-tarafı komuttur — Rebuild ile KARIŞTIRILMAMALI.
        var bareF5 = bindings.Single(b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.None);
        Assert.NotSame(vm.RebuildCommand, bareF5.Command);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Esc bağlaması sadece "var" değil, GERÇEKTEN Esc zincirini sürüyor: seçili bir proje varken pencerenin
    /// KENDİ <see cref="KeyBinding"/> nesnesinin komutu çalıştırılınca seçim temizlenir
    /// (<c>OnEscapePressed</c> → <c>EscAction.ClearSelection</c>). Esc yanlış bir aksiyona bağlansaydı
    /// (ör. FocusFilter ile takas) seçim yerinde kalır → kırmızı.
    /// </summary>
    [StaFact]
    public void The_escape_key_binding_really_runs_the_layer_chain_and_clears_the_selection()
    {
        var (window, vm) = NewMainWindow();
        vm.SelectProject(@"C:\p\a.csproj");
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId); // ön-koşul

        var escape = KeyBindingsOf(window).Single(b => b.Key == Key.Escape && b.Modifiers == ModifierKeys.None);
        Assert.True(escape.Command.CanExecute(null));
        escape.Command.Execute(null);

        Assert.Null(vm.SelectedProjectId);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Niyetler komutlara BİRE BİR gider: aynı niyeti paylaşan iki tuş (Ctrl+F5 / Shift+F5) AYNI nesneyi,
    /// FARKLI niyetler ise FARKLI nesneleri alır — yani 5 bağlama tam 4 ayrı komuta düşer. Bir sözlük arm'ı
    /// takas edilse (ör. Ctrl+F ile Esc aynı komuta bağlansa) bu sayı 3'e düşer → kırmızı.
    ///
    /// <para><b>Neden davranışsal koşum değil:</b> <c>FocusFilter</c> ve <c>Escape</c>'in etkileri (odak / katman
    /// zinciri) gerçek bir HWND ister ya da hiç açık katman yokken gözlemlenemez; <see cref="Escape"/> kolunun
    /// GERÇEK etkisi yukarıdaki ayrı testte (seçim temizleme) zaten sürülür.</para></summary>
    [StaFact]
    public void The_five_gestures_collapse_onto_exactly_four_distinct_commands()
    {
        var (window, _) = NewMainWindow();
        var bindings = KeyBindingsOf(window);

        int distinct = bindings.Select(b => b.Command).Distinct(ReferenceEqualityComparer.Instance).Count();

        Assert.Equal(5, bindings.Count);
        Assert.Equal(4, distinct); // Ctrl+F5 ve Shift+F5 TEK komutu paylaşır; kalan üçü ayrı
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 1.6 title-bar layout ikonları

    /// <summary>Gerçek <see cref="ButtonBase.ClickEvent"/> — XAML'deki <c>Click="OnLayoutList"</c> kablosu
    /// koparsa (ya da yanlış moda bağlanırsa) burası kırar.</summary>
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [StaFact]
    public void Clicking_the_list_layout_icon_hides_the_graph_and_applies_the_list_preset()
    {
        var (window, _) = NewMainWindow();
        window.Shell.ApplyLayout(new LayoutState(LayoutMode.Quad, 60, 74, 76)); // bilinen başlangıç (persist'ten bağımsız)

        Click(window.LayListButton);

        Assert.Equal(LayoutMode.List, window.Shell.Layout.Mode);
        Assert.Equal(Visibility.Collapsed, window.Shell.GraphHost.Visibility);
        Assert.Equal(50, window.Shell.Layout.RightPct); // list preset: yalnız sağ split 50'ye
        Assert.Equal(60, window.Shell.Layout.ColPct);   // ...kolon/sol split KORUNUR
        Assert.Equal(74, window.Shell.Layout.LeftPct);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_focus_layout_icon_hides_the_graph_and_applies_the_focus_preset()
    {
        var (window, _) = NewMainWindow();
        window.Shell.ApplyLayout(new LayoutState(LayoutMode.Quad, 60, 74, 50));

        Click(window.LayFocusButton);

        Assert.Equal(LayoutMode.Focus, window.Shell.Layout.Mode);
        Assert.Equal(Visibility.Collapsed, window.Shell.GraphHost.Visibility);
        Assert.Equal(76, window.Shell.Layout.RightPct); // focus preset: sağ split 76
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_quad_layout_icon_brings_the_graph_back_and_resets_all_three_splits()
    {
        var (window, _) = NewMainWindow();
        window.Shell.ApplyLayout(new LayoutState(LayoutMode.Focus, 60, 74, 76));
        Assert.Equal(Visibility.Collapsed, window.Shell.GraphHost.Visibility); // ön-koşul: graf gizli

        Click(window.LayQuadButton);

        Assert.Equal(LayoutMode.Quad, window.Shell.Layout.Mode);
        Assert.Equal(Visibility.Visible, window.Shell.GraphHost.Visibility);
        Assert.Equal(new LayoutState(LayoutMode.Quad, 50, 50, 50), window.Shell.Layout); // quad preset: üç split de 50
        GC.KeepAlive(window);
    }
}
