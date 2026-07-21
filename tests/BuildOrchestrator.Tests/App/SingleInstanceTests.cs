using System.IO.Pipes;
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

    /// <summary>
    /// [I-1 fix wave] Named pipe ismi MAKİNE-GENELİ bir isim alanındadır — mutex oturum-yerel olsa bile (bkz.
    /// <see cref="SingleInstanceGuard.DefaultKey"/> yorumu). Aynı kullanıcının İKİ oturumu (RDP + konsol) aynı
    /// anahtarla farklı mutex namespace'lerinde ayrı <c>IsFirst=true</c> instance'lar olur — ama pipe adı oturuma
    /// göre AYRILMAZSA ikisi de aynı isimde dinlemeye çalışır ve ikincisi <c>ERROR_PIPE_BUSY</c> ile spin'e girerdi
    /// (bkz. <see cref="Busy_pipe_backs_off_via_the_injected_delay_instead_of_spinning"/>). Anahtara oturum id'si
    /// katılarak bu çakışma imkânsız kılınır.
    /// </summary>
    [Fact]
    public void Pipe_name_is_scoped_to_the_session()
    {
        Assert.Equal("BuildOrchestrator.Test.abc.1234.pipe", SingleInstanceGuard.BuildPipeName("BuildOrchestrator.Test.abc", 1234));
    }

    [Fact]
    public void Different_sessions_get_different_pipe_names_for_the_same_key()
    {
        string a = SingleInstanceGuard.BuildPipeName("k", sessionId: 1);
        string b = SingleInstanceGuard.BuildPipeName("k", sessionId: 2);

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// [I-1 fix wave] Eski davranış: <c>catch (IOException)</c> BOŞTU → pipe meşgulken (başka bir process/oturum
    /// aynı adı tutuyorsa) döngü ANINDA yeniden dener → bir çekirdek %100 spin'e girerdi. Pipe'ı testte GERÇEKTEN
    /// (bir <see cref="NamedPipeServerStream"/> ile) meşgul tutup enjekte edilen geri-çekilme fonksiyonunun
    /// ÇAĞRILDIĞINI VE TAMAMLANMADAN döngünün ikinci kez denemediğini doğruluyoruz — spin olsaydı bu süre
    /// içinde geri-çekilme defalarca (binlerce kez) çağrılırdı.
    /// </summary>
    [Fact]
    public async Task Busy_pipe_backs_off_via_the_injected_delay_instead_of_spinning()
    {
        string key = "BuildOrchestrator.Test." + Guid.NewGuid().ToString("N");
        using var guard = SingleInstanceGuard.Acquire(key);

        // Guard'ın kullanacağı TAM pipe adını önceden işgal et — guard'ın ilk NamedPipeServerStream denemesi
        // (maxNumberOfServerInstances: 1) anında IOException (ERROR_PIPE_BUSY) alacak.
        using var busy = new NamedPipeServerStream(
            guard.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        int delayCalls = 0;
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        guard.StartListening(() => { }, async ct =>
        {
            Interlocked.Increment(ref delayCalls);
            delayEntered.TrySetResult();
            await releaseDelay.Task.WaitAsync(ct); // testi bitirene kadar döngüyü burada TUT (spin olmadığının kanıtı)
        });

        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // ilk IOException'dan sonra geri-çekilmeye GİRDİ

        await Task.Delay(200); // geri-çekilme TAMAMLANMADAN döngü ikinci kez denemesin
        Assert.Equal(1, delayCalls); // spin olsaydı bu 200ms'de defalarca artardı

        releaseDelay.TrySetResult(); // döngüyü serbest bırak (dispose ile temizlenecek)
    }
}
