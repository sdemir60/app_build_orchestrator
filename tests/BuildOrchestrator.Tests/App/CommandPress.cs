using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Bir komuta kullanıcının bastığı gibi basar: WPF'in <c>ButtonBase</c>'i ve <c>KeyBinding</c>'i komutu yalnız
/// <c>CanExecute</c> true iken çalıştırır. <c>Execute</c>'u kapıya bakmadan çağırmak, ekranda hiç olamayacak bir
/// tıklamayı taklit eder — kapısı kapalı bir düğmeye basmak orada hiçbir şey yapmaz.
/// </summary>
internal static class CommandPress
{
    /// <returns>Komut çalıştı mı — kapı kapalıysa <c>false</c> ve hiçbir şey olmaz.</returns>
    public static bool Press(ICommand command, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.CanExecute(parameter)) return false;
        command.Execute(parameter);
        return true;
    }

    /// <summary>Realize edilmiş bir düğmeye kullanıcı gibi basar: UI Automation Invoke dispatcher'a <c>Input</c>
    /// önceliğiyle post edilir ve <c>ButtonBase.OnClick</c>'e iner — komutu yalnız <c>CanExecute</c> true iken
    /// çalıştıran üretim yolu. Pasif bir düğmede Invoke fırlatır (sönük düğmeye basılamaz); çağıran dispatcher'ı
    /// pompalar (<see cref="DispatcherPump"/>).</summary>
    public static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    /// <summary>Bir düğmenin <c>Click</c> olayını SENKRON yükseltir — yalnız <c>Click</c> handler'ları koşar (bağlı bir
    /// komut ve bir <c>ToggleButton</c>'ın kendi işaret değişimi koşmaz). Pencere gösterilmeyen kabuk testleri içindir;
    /// orada <see cref="Invoke"/>'un dispatcher'a bıraktığı basış, bir popup'ı da gerçekten açtırırdı.</summary>
    public static void Click(System.Windows.Controls.Primitives.ButtonBase button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
}
