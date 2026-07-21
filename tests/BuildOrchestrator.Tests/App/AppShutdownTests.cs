using System.Windows.Threading;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62 fix — tray→Exit process asılı kalıyor] <c>App.OnExit</c> UI/Dispatcher thread'inde koşar ve orada
/// EngineHost'un async disposal'ını BEKLER. EngineHost'un disposal yolundaki await'ler
/// <c>ConfigureAwait(false)</c> içermediği için continuation'lar yakalanan
/// <see cref="DispatcherSynchronizationContext"/>'e post edilir; Dispatcher ise o an bekleyişin İÇİNDE
/// bloklu olduğundan mesajı hiç işlemez → klasik sync-over-async deadlock. Sonuç: tray ikonu kaybolur
/// (OnClosed → tray.Dispose) ama process yaşamaya devam eder ve outer Job hiç kapanmadığı için Supervisor
/// da sağ kalır (§3/D8 kaskat garantisi ihlali).
///
/// <para><b>Testler neden pompalamayan ayrı bir thread kullanıyor:</b> mevcut <see cref="DispatcherPump"/>
/// yardımcısı Dispatcher'ı POMPALAR — bu, deadlock'u tam olarak GİZLER (post edilen continuation koşar).
/// Buradaki hata modu "context kurulu ama pump çalışmıyor" halidir, bu yüzden testler kendi STA thread'lerini
/// kurar: <see cref="DispatcherSynchronizationContext"/> yüklü, <c>PushFrame/Run</c> YOK. Thread
/// <c>IsBackground</c>'dur — regresyonda asılı kalsa bile test host'unu kilitlemez; çağıran taraftaki dış
/// guard (<c>WaitAsync</c>) hızlı FAIL üretir (süresiz asılma yok, sleep tabanlı flake yok).</para>
/// </summary>
public class AppShutdownTests
{
    /// <summary>Verilen işi, <see cref="DispatcherSynchronizationContext"/> KURULU ama mesaj pompası
    /// ÇALIŞMAYAN bir STA thread'inde koşturur — <c>App.OnExit</c>'in gerçek koşulları.</summary>
    private static Task<T> OnBlockedDispatcherThread<T>(Func<T> body)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { tcs.SetResult(body()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "blocked-dispatcher" }; // deadlock regresyonunda test host'u kilitlenmesin
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>EngineHost'un disposal yolu ile AYNI ŞEKİL: <c>ConfigureAwait(false)</c> YOK, iş thread-pool'da
    /// biter. <c>Task.Yield()</c> askıya almayı GARANTİLER (awaiter'ın <c>IsCompleted</c>'i her zaman false) →
    /// continuation yakalanan context'e post edilir; yarış penceresi ve sleep tahmini yok.</summary>
    private sealed class ContextCapturingDisposable : IAsyncDisposable
    {
        public volatile bool Disposed;

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            await Task.Run(() => { });
            Disposed = true;
        }
    }

    private sealed class NeverCompletingDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync() => await _never.Task; // hiç bitmez (wedged engine)
    }

    [Fact]
    public async Task WaitForAsyncDisposal_returns_when_the_disposal_captures_a_blocked_dispatcher_context()
    {
        var disposable = new ContextCapturingDisposable();

        var call = OnBlockedDispatcherThread(
            () => AppShutdown.WaitForAsyncDisposal(disposable, TimeSpan.FromSeconds(2)));

        // Dış guard helper'ın kendi timeout'undan uzun: regresyonda (eski inline GetAwaiter().GetResult() şekli)
        // çağrı HİÇ dönmez → burada TimeoutException → test hızlıca FAIL eder, asılı kalmaz.
        bool completed = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(completed);
        Assert.True(disposable.Disposed);
    }

    [Fact]
    public async Task WaitForAsyncDisposal_gives_up_after_its_timeout_so_a_wedged_engine_cannot_block_exit()
    {
        var call = OnBlockedDispatcherThread(
            () => AppShutdown.WaitForAsyncDisposal(new NeverCompletingDisposable(), TimeSpan.FromMilliseconds(200)));

        bool completed = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(completed); // "tamamlanmadı" raporlanır ama BEKLEYİŞ biter → process çıkabilir
    }

    [Fact]
    public void WaitForAsyncDisposal_tolerates_null() // --font-ab yolu: DI hiç kurulmaz
        => Assert.True(AppShutdown.WaitForAsyncDisposal(null, TimeSpan.FromSeconds(2)));

    [Fact]
    [Trait("Category", "ProcessControl")]
    public async Task EngineHost_disposal_does_not_require_the_callers_synchronization_context()
    {
        var host = new EngineHost(TestPaths.SupervisorExe);
        await host.StartAsync(); // _writer canlı — disposal'da GERÇEKTEN askıya alan await'ler oluşur

        // DisposeAsync'i bloklu dispatcher context'inde BAŞLAT (App.OnExit ile aynı), Task'ı dışarıda bekle.
        var disposal = await OnBlockedDispatcherThread(() => host.DisposeAsync().AsTask())
            .WaitAsync(TimeSpan.FromSeconds(10));

        // ConfigureAwait(false) yoksa continuation o thread'in kuyruğunda ölür → Task hiç tamamlanmaz → FAIL.
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
