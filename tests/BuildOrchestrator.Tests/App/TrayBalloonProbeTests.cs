using System.Globalization;
using System.Windows.Threading;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/3. tur teşhis] ÖLÇÜM — bir pin DEĞİL, bir SONDA. Kullanıcı koşu bitişinde HİÇBİR bildirim
/// görmediğini iki kez bildirdi (önce 48×48 yüklenen ikon, sonra %125 ekran ölçeğinde sabit 32 reddedildi). Bildirim
/// yolu üretimde <c>_ = CompleteExitAsync()</c> ile beklenmeden başlatılır ve ürün ikonu düşerse
/// <see cref="AppTrayIcon.ShowRunFinished"/> bildirimi ikonsuz gösterir — yani ekranda bir ikon reddi en fazla
/// "ikonsuz bildirim" olarak görünür, NEDENİ görünmez.
///
/// <para>Sonda nedeni görünür kılar: gerçek bir tepsi ikonu kurar ve iki balloon yolunu ayrı ayrı çağırıp
/// istisnayı RAPORLAR — (1) ürün ikonlu bitiş bildirimi YEDEKSİZ
/// (<see cref="AppTrayIcon.ShowRunFinishedWithProductIcon"/>; kütüphanenin reddi burada mesajıyla görünür),
/// (2) özel ikonsuz, OS ikonlu klasik bildirim. Biri patlayıp diğeri geçerse suçlu bellidir. Hiçbiri
/// patlamazsa kusur çağrıda değil, ya Windows'un bildirim ayarındadır ya da çağrının hiç yapılmamış
/// olmasındadır.</para>
///
/// <para>Varsayılan koşuda ÇALIŞMAZ (<c>BO_PROBE_TRAY=1</c> yoksa <c>Skip</c>): gerçek bir tepsi ikonu kurar
/// ve ekranda balloon gösterir.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class TrayBalloonProbeTests(ITestOutputHelper output)
{
    [SkippableFact]
    public void Does_the_run_finished_balloon_throw()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_TRAY") == "1",
            "Creates a real tray icon and shows balloons — opt in with BO_PROBE_TRAY=1.");

        foreach (string line in StaThread.RunAsync(Probe, "tray-balloon-probe").GetAwaiter().GetResult())
            output.WriteLine(line);
    }

    private static List<string> Probe()
    {
        // `pack://` şemasını WPF `PackUriHelper`'ın statik ctor'unda kaydeder; headless süitte Application yoktur, şemaya
        // ilk dokunan sonda olmalıdır — yoksa ikon adresi "Invalid port specified" ile reddedilir (TrayBalloonIconTests ile aynı).
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        var log = new List<string>();
        AppTrayIcon? tray = null;
        try
        {
            log.Add(Try("tepsi ikonunu kur",
                () => tray = new AppTrayIcon(AppTrayIcon.NoRunToStop)));
            if (tray is null) return log;

            // (1) Ürünün büyük ikonu + satırın baş/gövde ayrımı, YEDEKSİZ: üretimin yedek yolu bir reddi ikonsuz
            // bildirimle örterdi, sonda örtüyü kaldırır.
            var completed = new RibbonLine(
                "Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s", "Brush.StatusFailText", "failed");
            log.Add(Try("ShowRunFinishedWithProductIcon (ürün ikonu, yedeksiz)",
                () => tray!.ShowRunFinishedWithProductIcon(completed)));
            log.Add(string.Format(CultureInfo.InvariantCulture,
                "    başlık='{0}' gövde='{1}'",
                AppTrayIcon.RunFinishedTitle(completed), AppTrayIcon.RunFinishedBody(completed)));

            Pump(TimeSpan.FromSeconds(3));

            // (2) Bu işten ÖNCE de var olan yol: OS ikonu, özel ikon YOK.
            log.Add(Try("ShowNotification (OS ikonu)", () => tray!.ShowNotification("Probe", "OS icon, no custom handle.")));

            Pump(TimeSpan.FromSeconds(3));
        }
        finally
        {
            tray?.Dispose();
        }
        return log;
    }

    private static string Try(string what, Action action)
    {
        try
        {
            action();
            return $"  OK      — {what}";
        }
        catch (Exception ex)
        {
            return $"  PATLADI — {what}: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Balloon'un görünmesi için mesaj döngüsü gerekir; sonda onu kısa süre pompalar.</summary>
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        new DispatcherTimer(duration, DispatcherPriority.Background, (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher).Start();
        Dispatcher.PushFrame(frame);
    }
}
