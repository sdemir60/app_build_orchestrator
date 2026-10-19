using System.Windows;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App;

/// <summary>
/// [Exit] Title bar'ın <b>Exit butonu</b> ve Ctrl+Q: uygulamayı tamamen kapatır. Uçuşta iş yoksa onay sormadan hemen kapatır
/// (<see cref="RunViewModel.RequestExit"/> → <see cref="RunViewModel.ExitReady"/> → <see cref="ExitNow"/>). Kararın sorusu VM'in
/// <see cref="RunViewModel.ExitWork"/>'üdür — <see cref="ExitWork.None"/> "uçuşta iş yok"un tek cevabıdır (<c>WorkspaceIdle</c>).
/// </summary>
public partial class MainWindow
{
    /// <summary>Ctor'dan bir kez: tooltip kataloğun cümlesi + jesti ("Exit — quit and end every process (Ctrl+Q)").</summary>
    private void SetupExitButton() => ExitButton.ToolTip = CatalogTooltip(ShortcutId.Exit);

    private void OnExitButtonClick(object sender, RoutedEventArgs e) => OnExitRequested();

    /// <summary>Exit butonu ve Ctrl+Q — AYNI yol.</summary>
    private void OnExitRequested() => RequestExitFromUser();

    /// <summary>Kullanıcı uygulamayı kapatmak istedi: uçuşta iş yoksa hemen kapanır.</summary>
    private void RequestExitFromUser()
    {
        _vm.RequestExit(); // uçuşta iş yoksa ExitReady → ExitNow bu çağrının içinden gelir
    }
}
