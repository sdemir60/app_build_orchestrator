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

    private sealed class FakeStore : IUiStateStore
    {
        public UiState State { get; private set; } = new();
        public UiState Load() => State;
        public void Save(UiState state) => State = state;
    }

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

        dialog.Open(run, new FakeStore(), () => null);
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

    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? cur = node; cur is not null; cur = GetParent(cur))
            if (ReferenceEquals(cur, ancestor)) return true;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node) =>
        (node as Visual) is not null ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
