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
}
