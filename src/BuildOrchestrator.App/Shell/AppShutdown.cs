namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62 fix] <c>App.OnExit</c>'in engelleyici disposal bekleyişi — hem test edilebilir olsun diye ayrı tutulur
/// hem de UI thread'ini KİLİTLEMEYECEK şekilde yazılır.
///
/// <para><b>Sorun (bulundu, düzeltildi):</b> <c>OnExit</c> UI/Dispatcher thread'inde koşar. Disposal doğrudan
/// orada <c>GetAwaiter().GetResult()</c> ile beklenirse ve disposal yolundaki await'ler
/// <c>ConfigureAwait(false)</c> içermiyorsa, continuation'lar Dispatcher'a post edilir — Dispatcher ise tam o
/// bekleyişin içinde bloklu olduğundan hiç işlenmez: sync-over-async deadlock. Belirti: tray ikonu kaybolur
/// (OnClosed → tray dispose) ama process (ve outer Job hiç kapanmadığı için Supervisor) yaşamaya devam eder.</para>
/// </summary>
public static class AppShutdown
{
    /// <summary>
    /// [§3/D8 kabul: "app ölür → ≤2s, orphan yok"] Çıkışta disposal için tanınan ÜST sınır. Graceful yol zaten
    /// kendi içinde 500ms ile sınırlı (ShutdownCommand) + kill; 2s bunun ~4 katı pay bırakır ve kabul
    /// penceresini aşmaz. Süre dolsa bile process çıkışı outer Job handle'ını kapatır → <c>KILL_ON_JOB_CLOSE</c>
    /// kaskadı yine çalışır; yani timeout "orphan" ANLAMINA GELMEZ, sadece "nazik kapanışı bekleme"yi keser.
    /// </summary>
    public static readonly TimeSpan DisposalTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Verilen disposable'ı dispose eder ve en fazla <paramref name="timeout"/> kadar bekler.
    /// Dönen değer: true = disposal süresi içinde tamamlandı; false = tamamlanmadı/hata verdi (bekleyiş yine de
    /// biter — takılı bir engine process'in çıkmasını ASLA engelleyemez).
    /// </summary>
    public static bool WaitForAsyncDisposal(IAsyncDisposable? disposable, TimeSpan timeout)
    {
        if (disposable is null) return true; // --font-ab yolunda DI hiç kurulmaz
        try
        {
            // Task.Run: disposal, çağıranın SynchronizationContext'i (Dispatcher) OLMADAN, thread-pool'da başlar —
            // context yakalayan bir await kalsa bile UI thread'ine bağımlı olamaz.
            return Task.Run(async () => await disposable.DisposeAsync().ConfigureAwait(false)).Wait(timeout);
        }
        catch
        {
            // Çıkış yolunda hata fırlatmak süreci kilitlemekten/çökertmekten başka işe yaramaz; kaskat garantisi
            // zaten Job handle kapanışına dayanır.
            return false;
        }
    }
}
