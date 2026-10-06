using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf B4 · karar 5] İmleç saatinin üçüncü kapısının saf çekirdeği: son girdinin üstünden zaman aşımı geçince boşta, girdi
/// gelince uyanık. Zaman dışarıdan verilir (D8: gerçek bekleme yok). Ölçüm ve gerekçe <see cref="CaretIdleGate"/>'te.
/// </summary>
public class CaretIdleGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Input_keeps_the_gate_awake_until_the_timeout_has_passed_since_the_last_input()
    {
        var gate = new CaretIdleGate(Timeout);
        var changes = new List<bool>();
        gate.IdleChanged += changes.Add;

        gate.NoteInput(0);
        Assert.False(gate.Tick(4_999));
        Assert.False(gate.IsIdle);
        gate.NoteInput(3_000);                // araya giren girdi süreyi baştan başlatır
        Assert.False(gate.Tick(7_999));
        Assert.True(gate.Tick(8_000));        // 3000 + 5000
        Assert.True(gate.IsIdle);
        Assert.Equal([true], changes);
    }

    [Fact]
    public void Once_idle_the_gate_stays_idle_without_repeating_the_event_and_wakes_on_the_next_input()
    {
        var gate = new CaretIdleGate(Timeout);
        var changes = new List<bool>();
        gate.IdleChanged += changes.Add;
        gate.NoteInput(0);
        gate.Tick(5_000);
        Assert.True(gate.IsIdle);

        Assert.True(gate.Tick(9_000));        // boştayken yoklama bir daha olay üretmez
        Assert.Equal([true], changes);

        gate.NoteInput(10_000);
        Assert.False(gate.IsIdle);
        Assert.Equal([true, false], changes);

        gate.NoteInput(10_500);               // uyanıkken girdi olay üretmez
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void The_timeout_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaretIdleGate(TimeSpan.Zero));
    }
}
