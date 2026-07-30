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
    /// hiç <c>Show()</c> edilmez (Loaded/OnSourceInitialized tetiklenmez).
    ///
    /// <para>[fix-1 · C1] <b>Kalıcı durum store'u AÇIKÇA temp'e yönlendirilir.</b> Ölçülen yan etki: layout
    /// düğmesine basmak <c>Shell.LayoutChanged → MainWindow.OnShellLayoutChanged → _uiState.Save(...)</c>
    /// zincirini sürer ve bu zincir pencerenin <c>Show()</c> edilmesine BAĞLI DEĞİLDİR (abonelik ctor'da
    /// kurulur) — yani testler KULLANICININ GERÇEK
    /// <c>%LOCALAPPDATA%\BuildOrchestrator\ui-state.json</c> dosyasını yeniden yazıyordu. Store'u olmayan bir
    /// klasöre vermek yeterlidir: <c>JsonUiStateStore.Load</c> dosya yoksa varsayılan durumu döndürür ve
    /// <c>Save</c> klasörü kendisi oluşturur — temp klasörü testten sonra <see cref="TempDir"/> ile silinir.</para></summary>
    private static (MainWindow window, RunViewModel vm) NewMainWindow(TempDir uiStateDir)
    {
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = new JsonUiStateStore(Path.Combine(uiStateDir.Path, "ui-state.json"));
        return (new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope(), store), vm);
    }

    private static IReadOnlyList<KeyBinding> KeyBindingsOf(MainWindow window) =>
        [.. window.InputBindings.OfType<KeyBinding>()];

    // ---------------------------------------------------------------- 1.5 pencere kısayolları

    /// <summary>Tablo → <see cref="InputBindings"/> dönüşümü BİREBİR: her satır için TAM BİR
    /// <see cref="KeyBinding"/> ve fazlası yok. Foreach silinirse koleksiyon boş kalır → kırmızı.
    /// [fix-1 · I-F] <c>Assert.NotEmpty</c> vakum-yeşili kapatır; <c>Assert.NotNull(binding.Command)</c>
    /// KALDIRILDI (komut bir sözlük lookup'ından kurulur, ASLA null olamazdı — hep-yeşil assert).</summary>
    [StaFact]
    public void Every_window_shortcut_row_becomes_a_real_key_binding_on_the_window()
    {
        using var temp = new TempDir();
        var (window, _) = NewMainWindow(temp);
        var bindings = KeyBindingsOf(window);

        Assert.NotEmpty(bindings);
        Assert.Equal(KeyboardShortcuts.WindowBindings.Count, bindings.Count);
        foreach (var row in KeyboardShortcuts.WindowBindings)
            Assert.Single(bindings, b => b.Key == row.Key && b.Modifiers == row.Modifiers);
        GC.KeepAlive(window);
    }

    /// <summary>Aynı niyetin İKİ tuşu (Ctrl+F5 / Shift+F5) AYNI komut nesnesine — ve o nesne VM'in GERÇEK
    /// <see cref="RunViewModel.RebuildCommand"/>'ıdır (<c>ReferenceEquals</c>). Yanlış bir sözlük arm'ı
    /// (ör. Rebuild→BuildCommand) burada kırar.</summary>
    [StaFact]
    public void Both_rebuild_gestures_are_bound_to_the_view_models_own_rebuild_command()
    {
        using var temp = new TempDir();
        var (window, vm) = NewMainWindow(temp);
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
        using var temp = new TempDir();
        var (window, vm) = NewMainWindow(temp);
        vm.SelectProject(@"C:\p\a.csproj");
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId); // ön-koşul

        var escape = KeyBindingsOf(window).Single(b => b.Key == Key.Escape && b.Modifiers == ModifierKeys.None);
        Assert.True(escape.Command.CanExecute(null));
        escape.Command.Execute(null);

        Assert.Null(vm.SelectedProjectId);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [fix-1 · I-F] Ctrl+F bağlamasının kimliği DAVRANIŞLA pinlenir: komutu çalıştırmak seçime DOKUNMAZ.
    /// Ayırt edici tam olarak review'ün işaret ettiği takas: <c>FocusFilter</c> arm'ı Esc'in komutuyla
    /// değiştirilseydi bu koşum seçimi temizler → KIRMIZI. (Önceki hâli "5 gesture → 4 komut" sayımıydı;
    /// adı davranış değil API şekli anlatıyordu ve bir arm'ın GÖVDESİ takas edilse sayı yine 4 kalırdı.)
    ///
    /// <para>Odağın kendisi (<c>Shell.FocusProjectFilter</c>) burada gözlemlenemez: <c>Focus()</c> gerçek bir
    /// <c>PresentationSource</c> ister ve pencere <c>Show()</c> EDİLEMEZ (bkz. sınıf özeti).</para></summary>
    [StaFact]
    public void Running_the_filter_shortcut_leaves_the_selection_alone_so_it_cannot_be_the_escape_action()
    {
        using var temp = new TempDir();
        var (window, vm) = NewMainWindow(temp);
        vm.SelectProject(@"C:\p\a.csproj");
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId); // ön-koşul

        var bindings = KeyBindingsOf(window);
        var ctrlF = bindings.Single(b => b.Key == Key.F && b.Modifiers == ModifierKeys.Control);
        var escape = bindings.Single(b => b.Key == Key.Escape && b.Modifiers == ModifierKeys.None);
        ctrlF.Command.Execute(null);

        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId); // Esc ile takas edilseydi temizlenirdi
        Assert.NotSame(escape.Command, ctrlF.Command);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 1.6 title-bar layout ikonları

    /// <summary>Gerçek <see cref="ButtonBase.ClickEvent"/> — XAML'deki <c>Click="OnLayoutList"</c> kablosu
    /// koparsa (ya da yanlış moda bağlanırsa) burası kırar.</summary>
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [StaFact]
    public void Clicking_the_list_layout_icon_hides_the_graph_and_applies_the_list_preset()
    {
        using var temp = new TempDir();
        var (window, _) = NewMainWindow(temp);
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
        using var temp = new TempDir();
        var (window, _) = NewMainWindow(temp);
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
        using var temp = new TempDir();
        var (window, _) = NewMainWindow(temp);
        window.Shell.ApplyLayout(new LayoutState(LayoutMode.Focus, 60, 74, 76));
        Assert.Equal(Visibility.Collapsed, window.Shell.GraphHost.Visibility); // ön-koşul: graf gizli

        Click(window.LayQuadButton);

        Assert.Equal(LayoutMode.Quad, window.Shell.Layout.Mode);
        Assert.Equal(Visibility.Visible, window.Shell.GraphHost.Visibility);
        Assert.Equal(new LayoutState(LayoutMode.Quad, 50, 50, 50), window.Shell.Layout); // quad preset: üç split de 50
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [fix-1 · C1] Layout tıklaması KALICI duruma yazar — ve yazdığı yer ENJEKTE EDİLEN store'dur.
    ///
    /// <para>Bu test iki şeyi birden pinler: (a) düğme→persist zinciri gerçekten koşuyor (dolayısıyla yukarıdaki
    /// üç testin temp store enjeksiyonu bir SÜS DEĞİL — o zincir olmasa yan etki de olmazdı), (b) enjeksiyon
    /// GERÇEKTEN devrede, yani üretim varsayılanı (kullanıcının <c>%LOCALAPPDATA%</c> dosyası) BY-PASS ediliyor.
    /// Enjeksiyon parametresi yok sayılsaydı temp dosyası hiç oluşmaz → KIRMIZI.</para></summary>
    [StaFact]
    public void A_layout_click_persists_through_the_injected_ui_state_store_not_the_default_one()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        var (window, _) = NewMainWindow(temp);
        Assert.False(File.Exists(path)); // ön-koşul: henüz yazılmadı

        Click(window.LayFocusButton);

        Assert.True(File.Exists(path), "layout tıklaması enjekte edilen store'a YAZMADI");
        Assert.Equal(LayoutMode.Focus, new JsonUiStateStore(path).Load().LayoutMode);
        GC.KeepAlive(window);
    }
}
