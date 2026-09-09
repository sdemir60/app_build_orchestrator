using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Realize edilmiş + açılmış bir <see cref="NotesDialog"/> kuran TEK yer (<see cref="AboutDialogHost"/>/
/// <c>SettingsDialogHost</c> deseninin eşi — kopya YASAK, CLAUDE.md). About'un aksine bir <c>RunViewModel</c>
/// ya da MSBuild çözümü GEREKMEZ: içerik yalnız <c>AppIdentity.Version</c> ve <c>ReleaseNotes.All</c>'tan
/// gelir, ikisi de saf/statiktir.
/// </summary>
internal static class NotesDialogHost
{
    /// <param name="configure">Realize edildikten ama <c>Open()</c> ÇAĞRILMADAN önce çalışır — ör.
    /// <see cref="NotesDialog.NotesSeen"/>'e Open() tetiklenmeden ÖNCE abone olmak için (AboutDialogHost'un
    /// <c>configure</c> parametresiyle AYNI desen).</param>
    /// <param name="backgroundSibling">Verilirse diyalog, bu kontrolle AYNI kökün altında realize edilir —
    /// odak tuzağı testi "Tab arka plandaki bir kontrole kaçıyor mu" sorusunu ancak böyle sorabilir.</param>
    public static (NotesDialog dialog, IDisposable scope) OpenRealized(
        Action<NotesDialog>? configure = null, FrameworkElement? backgroundSibling = null)
    {
        var host = DsResources.NewHost();
        var dialog = new NotesDialog();

        FrameworkElement content = dialog;
        if (backgroundSibling is not null)
        {
            var root = new Grid();
            root.Children.Add(backgroundSibling);
            root.Children.Add(dialog);
            content = root;
        }
        // AboutDialogHost'taki AYNI gerekçe: varsayılan 400×200'de 620px'lik modal dikeyde kırpılır ve
        // ActualHeight/ActualWidth içerik ne olursa olsun aynı doymuş değeri döner.
        var window = DsResources.Realize(host, content, width: 800, height: 700);

        configure?.Invoke(dialog);
        dialog.Open();
        content.UpdateLayout(); // Visibility Collapsed→Visible sonrası GERÇEK arrange

        return (dialog, new Scope(window));
    }

    private sealed class Scope(Window window) : IDisposable
    {
        public void Dispose() => GC.KeepAlive(window);
    }
}
