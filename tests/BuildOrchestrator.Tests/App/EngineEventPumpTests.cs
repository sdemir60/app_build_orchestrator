using System.Diagnostics;
using System.Windows.Threading;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <see cref="EngineEventPump"/>'ın kendi sözleşmesi, süresi bilinen (deterministik) işleyicilerle: patlama dilimler arasında
/// girdiye yol verir, her üreticinin sırası korunur, fırlatan bir işleyici yalnız kendi olayını kaybettirir ve
/// <see cref="EngineEventPump.DrainNow"/> kuyruktakileri dönmeden uygular. Üretim kablajından geçen uçtan uca hâli
/// <see cref="EngineEventBurstTests"/>'te — o test olay maliyetinin bir dilimi aşmasına dayanır, bu sınıf dayanmaz.
/// </summary>
public class EngineEventPumpTests
{
    private static ProjectStartedEvent Ev(int producer, int seq) => new("r", $"{producer}:{seq}", "n");

    private static (int Producer, int Seq) Parse(IpcEvent ev)
    {
        string[] parts = ((ProjectStartedEvent)ev).ProjectId.Split(':');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static void Spin(double ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ms) Thread.SpinWait(20);
    }

    [StaFact]
    public void A_burst_yields_to_input_between_slices()
    {
        const int count = 30;
        int handled = 0;
        double perEventMs = EngineEventPump.SliceBudgetMs / 2.5; // bir dilime üç olay sığar: patlama ~10 dilim
        var pump = new EngineEventPump(Dispatcher.CurrentDispatcher, _ => { Spin(perEventMs); handled++; });
        for (int i = 0; i < count; i++) pump.Post(Ev(0, i));

        int handledWhenInputRan = -1;
        Dispatcher.CurrentDispatcher.InvokeAsync(() => handledWhenInputRan = handled, DispatcherPriority.Input);
        DispatcherPump.PumpUntil(() => handled == count && handledWhenInputRan >= 0, TimeSpan.FromSeconds(10));

        Assert.Equal(count, handled);
        Assert.InRange(handledWhenInputRan, 1, count - 1); // ilk dilim hemen koştu; girdi patlama bitmeden sıra aldı
    }

    [StaFact]
    public void Events_from_several_producers_keep_each_producers_order()
    {
        const int producers = 4, perProducer = 300;
        var seen = new List<(int Producer, int Seq)>(); // yalnız UI thread'inde (işleyicide) yazılır
        var pump = new EngineEventPump(Dispatcher.CurrentDispatcher, ev => { seen.Add(Parse(ev)); Spin(0.02); });

        var posting = Enumerable.Range(0, producers)
            .Select(p => Task.Run(() => { for (int s = 0; s < perProducer; s++) pump.Post(Ev(p, s)); }))
            .ToArray();
        DispatcherPump.PumpUntil(() => posting.All(t => t.IsCompleted) && seen.Count == producers * perProducer,
            TimeSpan.FromSeconds(20));

        Assert.Equal(producers * perProducer, seen.Count); // hiçbir olay kuyrukta kalmadı
        foreach (var byProducer in seen.GroupBy(x => x.Producer))
            Assert.Equal(Enumerable.Range(0, perProducer), byProducer.Select(x => x.Seq));
    }

    [StaFact]
    public void An_event_whose_handler_throws_is_lost_alone_and_the_rest_still_arrive()
    {
        var seen = new List<int>();
        var pump = new EngineEventPump(Dispatcher.CurrentDispatcher, ev =>
        {
            int seq = Parse(ev).Seq;
            if (seq == 1) throw new InvalidOperationException("handler failure");
            seen.Add(seq);
        });
        for (int i = 0; i < 5; i++) pump.Post(Ev(0, i));

        DispatcherPump.PumpUntil(() => seen.Count == 4, TimeSpan.FromSeconds(5));

        Assert.Equal([0, 2, 3, 4], seen);
    }

    [StaFact]
    public void DrainNow_applies_everything_queued_before_returning()
    {
        var seen = new List<int>();
        var pump = new EngineEventPump(Dispatcher.CurrentDispatcher, ev => seen.Add(Parse(ev).Seq));
        for (int i = 0; i < 10; i++) pump.Post(Ev(0, i));

        pump.DrainNow();

        Assert.Equal(Enumerable.Range(0, 10), seen);
        DispatcherPump.DrainToIdle(); // Post'un kurduğu boşaltıcı boş kuyruk bulur ve kapanır — tekrar uygulamaz
        Assert.Equal(10, seen.Count);
        pump.Post(Ev(0, 10));          // pompa sonradan da çalışır (bayrak bırakıldı)
        DispatcherPump.PumpUntil(() => seen.Count == 11, TimeSpan.FromSeconds(5));
        Assert.Equal(10, seen[^1]);
    }
}
