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
    /// <summary>Çok sürümlü sentetik liste — en yenisi KURULU sürümdür (<c>AppIdentity.Version</c>), her sürümde
    /// iki kategori ve kategori başına birden çok, sarılacak kadar uzun madde vardır. Sürümler arası ayraç,
    /// katlı kısım ve sticky kayma ancak birden çok sürümle ölçülebilir; gerçek <c>ReleaseNotes.All</c> tek
    /// sürüm taşıyabilir.</summary>
    public static IReadOnlyList<BuildOrchestrator.App.Services.ReleaseEntry> SyntheticReleases(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i => new BuildOrchestrator.App.Services.ReleaseEntry(
            i == 0 ? BuildOrchestrator.App.Services.AppIdentity.Version : $"0.{count - i}.0",
            $"2026-01-{i + 1:00}",
            [
                .. Enumerable.Range(0, 4).Select(n => new BuildOrchestrator.App.Services.ReleaseNote(
                    BuildOrchestrator.App.Services.NoteKind.Added,
                    $"Synthetic added note {n} of version {i}, long enough to wrap across the measured column of the dialog body.")),
                .. Enumerable.Range(0, 3).Select(n => new BuildOrchestrator.App.Services.ReleaseNote(
                    BuildOrchestrator.App.Services.NoteKind.Fixed,
                    $"Synthetic fixed note {n} of version {i}.")),
            ])),
    ];

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
        // AboutDialogHost'taki AYNI gerekçe: varsayılan 400×200'de 720×600'lük modal kırpılır ve
        // ActualHeight/ActualWidth içerik ne olursa olsun aynı doymuş değeri döner. 800×700 dialogu kabuğun
        // host − 48 kelepçesine TAKILMADAN sığdırır (istemci alanı çerçeve payı kadar küçüktür, ölçüldü: ~786×686).
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
