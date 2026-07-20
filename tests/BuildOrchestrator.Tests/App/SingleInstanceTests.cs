using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62 / feasibility §4.3] Single-instance'ın KRİTİK doğruluk noktası sıradır: tepside bekleyen ilk instance
/// background'dur, kendi <c>Activate()</c>'ı yalnız taskbar'ı yakıp söndürür. İkinci instance, sinyali
/// göndermeden ÖNCE <c>AllowSetForegroundWindow(ilk pid)</c> çağırmak ZORUNDADIR — sonra çağırırsa ilk instance
/// öne gelme hakkını sinyali işlerken henüz almamış olur. Sıra burada assert edilir; gerçek çok-process
/// senaryosu manuel doğrulanır.
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void Second_instance_calls_allow_set_foreground_window_before_signalling()
    {
        var calls = new List<string>();

        SingleInstanceProtocol.ActivateExisting(
            readOwnerPid: () => { calls.Add("read-pid"); return 4242; },
            allowSetForeground: pid => { calls.Add($"allow({pid})"); return true; },
            signal: b => calls.Add($"signal({b})"));

        Assert.Equal(["read-pid", "allow(4242)", $"signal({SingleInstanceProtocol.ActivateSignal})"], calls);
    }

    [Fact]
    public void First_instance_owns_the_mutex_and_a_second_acquire_is_not_first()
    {
        string key = "BuildOrchestrator.Test." + Guid.NewGuid().ToString("N");

        using var first = SingleInstanceGuard.Acquire(key);
        using var second = SingleInstanceGuard.Acquire(key);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public async Task Signal_reaches_the_first_instance_and_hands_it_the_owner_pid_first()
    {
        string key = "BuildOrchestrator.Test." + Guid.NewGuid().ToString("N");
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();

        using var first = SingleInstanceGuard.Acquire(key);
        first.StartListening(() => { lock (order) order.Add("activated"); activated.TrySetResult(); });

        using var second = SingleInstanceGuard.Acquire(key);
        bool sent = second.ActivateExistingInstance(TimeSpan.FromSeconds(5), pid =>
        {
            lock (order) order.Add($"allow({pid})");
            return true;
        });

        Assert.True(sent);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (order)
            Assert.Equal([$"allow({Environment.ProcessId})", "activated"], order); // pid = ilk instance, sıra korunur
    }

    [Fact]
    public void Signalling_when_nobody_listens_fails_quietly()
    {
        string key = "BuildOrchestrator.Test." + Guid.NewGuid().ToString("N");
        using var lonely = SingleInstanceGuard.Acquire(key); // IsFirst — karşıda dinleyen yok

        Assert.False(lonely.ActivateExistingInstance(TimeSpan.FromMilliseconds(200), _ => true));
    }
}
