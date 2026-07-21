using System.Windows.Threading;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] WPF Storyboard/AnimationClock'lar compositor tick'iyle (Dispatcher'ın kendi render-öncelikli zamanlayıcısı)
/// ilerler — bir <c>[StaFact]</c> test metodu bunu görebilmek için Dispatcher'ı GERÇEKTEN pompalamalı (aksi halde
/// hiçbir mesaj/tick işlenmez). Bu, sabit bir <c>Thread.Sleep</c> TAHMİNİ DEĞİL — çıkış koşula (ya da güvenlik
/// zaman aşımına) bağlıdır; D8'in "deterministik, sleep/poll değil" ruhuna en yakın pratik karşılık (gerçek WPF
/// animasyon clock'unun kendisi enjekte edilebilir değildir — <c>ClockController</c> manuel seek'i ayrı bir
/// hazır-olmayan API yüzeyi açardı, bu yüzden burada tercih edilmedi).
/// </summary>
internal static class DispatcherPump
{
    public static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition()) return;

        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow + timeout;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
