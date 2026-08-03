using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D7 re-review][Fix1] Settings modal'ının klavye odak tuzağı (focus trap). Scrim FAREYİ bloklar ama Tab
/// öntanımlı olarak arka plandaki kontrollere KAÇAR (ör. Settings açıkken Tab ile arka plandaki Build butonuna
/// gidip Space/Enter ile bir run başlatmak mümkündü). <c>SettingsDialog.xaml</c>'daki
/// <c>KeyboardNavigation.TabNavigation/ControlTabNavigation="Cycle"</c> + <c>FocusManager.IsFocusScope="True"</c>
/// (Scrim kökü) bunu düzeltir. Gerçek <see cref="Window"/> içinde kurulur (StaFact) — bkz. <see cref="DsResources"/>.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SettingsDialogFocusTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // [A13/T3 fix-1 · C13] FakeStore ARTIK tek yerde: SettingsDialogHost.FakeStore (iki dosyada ikizdi).
    private static SettingsDialogHost.FakeStore NewStore() => new();

    /// <summary>Yapısal asgari kanıt: diyaloğun scrim kökü gerçekten bir Cycle klavye-gezinme kapsayıcısı ve
    /// bir odak kapsamı (FocusScope) mı — Tab'ın alt-ağaç dışına kaçamayacağının doğrudan kanıtı.</summary>
    [StaFact]
    public void Settings_dialog_scrim_is_a_cyclic_keyboard_focus_scope()
    {
        var host = DsResources.NewHost();
        var dialog = new SettingsDialog();
        var window = DsResources.Realize(host, dialog);

        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog.Scrim));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(dialog.Scrim));
        Assert.True(FocusManager.GetIsFocusScope(dialog.Scrim));
        GC.KeepAlive(window);
    }

    /// <summary>Gerçek gezinme kanıtı: aynı pencerede arka planda odaklanabilir bir kontrol (Build butonunun
    /// yerini tutan) + açık diyalog dururken, diyalog alt-ağacından başlayarak tekrar tekrar "Sonraki" gezinme
    /// (Tab'ın WPF içindeki gerçek mekanizması — <see cref="UIElement.MoveFocus"/>) yapılır: kontrol sayısından
    /// FAZLA turda ne odak arka plan kontrolüne kaçar ne de diyalog alt-ağacının dışına çıkar (Cycle sarar).</summary>
    [StaFact]
    public async Task Tab_navigation_cannot_escape_the_open_dialog_to_reach_a_background_control()
    {
        var host = DsResources.NewHost();
        var background = new Button { Content = "Background Build", Focusable = true, Width = 90, Height = 24 };
        var root = new Grid();
        root.Children.Add(background);

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var dialog = new SettingsDialog();
        root.Children.Add(dialog);

        var window = DsResources.Realize(host, root);

        dialog.Open(run, NewStore(), () => null);
        root.UpdateLayout(); // diyalog artık Visible — satır/buton container'ları yerleşsin

        Assert.True(dialog.Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
        for (int i = 0; i < 25; i++) // kontrol sayısından kesinlikle fazla — Cycle sarmalıyor, kaçmıyor
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            Assert.NotNull(focused);
            Assert.NotSame(background, focused);
            Assert.True(IsDescendantOf(focused!, dialog.Scrim), "odak diyalog alt-ağacının DIŞINA çıktı");
            (focused as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
        GC.KeepAlive(window);
    }

    // [A13/T3 fix-2 · 7] Ata yürüyüşü DsResources'a toplandı. Bu çağıran GÖRSEL+MANTIKSAL kipi kullanır ve bu
    // fark BİLEREK korunmuştur: odaklanan öğe bir Popup/ContentElement altındaysa görsel zincir kopar, mantıksal
    // zincir devam eder — kardeş iki çağıran (salt görsel) bu kipe geçirilmedi.
    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor) =>
        DsResources.IsSelfOrDescendantOf(node, ancestor, includeLogical: true);

    // ================================================================ [A13/T3b] ölçü/geometri (b2/b3)

    /// <summary>[A13/T3b · b2] design-v1 README §2.9: "Settings dialog (620px)". <c>DesignTokenScaleTests.cs:141</c>
    /// içinde geçen 620 AYRI bir kalemdir (<c>Size.WindowMinHeight</c>) — karıştırılmaz (brief notu). Diyaloğun
    /// KENDİ genişliği testsizdi.</summary>
    [StaFact]
    public void Settings_dialog_shell_is_six_hundred_twenty_pixels_wide()
    {
        // [fix-1 · B6/C9] Kurulum + EngineHost sahipliği tek yerde (SettingsDialogHost).
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            var shell = (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);
            Assert.Equal(620.0, shell.Width);
            Assert.Equal(620.0, shell.ActualWidth); // realize zorunlu — literal okumak yetmez (kural 5)
        }
    }

    /// <summary>[A13/T3b · b3] design-v1 README §2.9: "Katman kartları (36px + 6px boşluk) ... ad inputu
    /// (170px)". Önceden yalnız 42px (=36+6) aritmetiği DragReorderTests.cs'teki sürükleme eşiğinden DOLAYLI
    /// pinliydi (RowStep=42 — sürükleme mantığının kendi sabiti, kartın GERÇEK yerleşimi değil). Bu test iki
    /// gerçek kartı realize edip aralarındaki GERÇEK piksel farkını ve ad inputunun GERÇEK genişliğini ölçer.</summary>
    [StaFact]
    public void Layer_cards_are_36px_tall_with_a_6px_gap_and_a_170px_name_input()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run =>
            run.LayerPatterns = [new LayerPattern(0, "^A", "Layer A"), new LayerPattern(1, "^B", "Layer B")]);
        using var _scope = scope;

        var editor = (LayerEditorViewModel)dialog.DataContext;
        Assert.Equal(2, editor.Layers.Count); // ön-koşul: iki kart gerçekten var

        var card0 = CardBorder(dialog.LayersList, editor.Layers[0]);
        var card1 = CardBorder(dialog.LayersList, editor.Layers[1]);

        Assert.Equal(36.0, card0.ActualHeight);
        Assert.Equal(new Thickness(0, 0, 0, 6), card0.Margin);

        // GERÇEK dikey mesafe: kart1'in üst kenarı − kart0'ın üst kenarı = 36 (yükseklik) + 6 (alt boşluk) = 42.
        double top0 = card0.TranslatePoint(new Point(0, 0), dialog).Y;
        double top1 = card1.TranslatePoint(new Point(0, 0), dialog).Y;
        Assert.Equal(42.0, top1 - top0, precision: 1);

        var nameBox = DsResources.Descendants(card0).OfType<TextBox>()
            .Single(t => BuildOrchestrator.App.Controls.DsChrome.GetWatermark(t) == "Layer name");
        Assert.Equal(170.0, nameBox.Width);
        Assert.Equal(170.0, nameBox.ActualWidth);
    }

    private static Border CardBorder(ItemsControl list, LayerRowViewModel row)
    {
        var presenter = (ContentPresenter)list.ItemContainerGenerator.ContainerFromItem(row)!;
        presenter.ApplyTemplate();
        return (Border)VisualTreeHelper.GetChild(presenter, 0);
    }
}
