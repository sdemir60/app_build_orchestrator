using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62 / feasibility §4.3] İkinci instance'ın ilk instance'ı ÖNE GETİRME protokolü — sırası kritik olduğu için
/// taşımadan (named pipe) ayrılmış SAF adım dizisi.
///
/// <para>İlk instance tepside/arka planda beklerken kendi <c>Activate()</c>'ı yalnız taskbar'ı yakıp söndürür;
/// öne gelme hakkını ancak O AN foreground olan process (kullanıcının yeni başlattığı ikinci instance)
/// <c>AllowSetForegroundWindow(ilk pid)</c> ile DEVREDEBİLİR. Devir, sinyalden ÖNCE olmalıdır: sinyal ilk
/// instance'ta anında işlenir, hak o an verilmiş olmalıdır.</para>
/// </summary>
public static class SingleInstanceProtocol
{
    /// <summary>Pipe üzerinden gönderilen tek bayt: "pencereni öne getir".</summary>
    public const byte ActivateSignal = 1;

    /// <summary>Sıra: ilk instance'ın pid'ini öğren → <c>AllowSetForegroundWindow(pid)</c> → sinyal.</summary>
    public static void ActivateExisting(Func<int> readOwnerPid, Func<int, bool> allowSetForeground, Action<byte> signal)
    {
        int ownerPid = readOwnerPid();
        allowSetForeground(ownerPid); // dönüş değeri bilinçli yok sayılır — başarısızlık yalnız "öne gelmeyebilir"
        signal(ActivateSignal);
    }
}

/// <summary>
/// [T62] Named Mutex (ilk mi?) + named pipe (sinyal kanalı) ile single-instance. İlk instance pipe'ı dinler ve
/// bağlanan her istemciye ÖNCE kendi pid'ini yazar (istemci <see cref="SingleInstanceProtocol"/> sırasını
/// uygulayabilsin diye), sonra aktivasyon baytını bekler.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>Oturum-yerel (Global\ DEĞİL) + kullanıcıya özel: aynı makinede farklı kullanıcılar birbirini
    /// engellemez.</summary>
    public static string DefaultKey { get; } = $"BuildOrchestrator.SingleInstance.{Environment.UserName}";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool isFirst, string pipeName)
    {
        _mutex = mutex;
        IsFirst = isFirst;
        _pipeName = pipeName;
    }

    /// <summary>Bu process bu anahtarın İLK sahibi mi.</summary>
    public bool IsFirst { get; }

    /// <summary>[I-1 fix wave] Bekleme süresi: pipe meşgulken (ERROR_PIPE_BUSY) her denemeden önce beklenir —
    /// aksi halde boş <c>catch (IOException)</c> anında yeniden dener ve bir çekirdeği %100 spin'e sokar.</summary>
    private static readonly TimeSpan PipeRetryDelay = TimeSpan.FromMilliseconds(250);

    public static SingleInstanceGuard Acquire(string key)
    {
        var mutex = new Mutex(initiallyOwned: true, name: key, out bool createdNew);
        return new SingleInstanceGuard(mutex, createdNew, BuildPipeName(key, CurrentSessionId()));
    }

    /// <summary>[I-1 fix wave] Named pipe adı <b>makine-geneli</b> bir isim alanındadır — mutex oturum-yerel olsa
    /// bile (<see cref="DefaultKey"/> yorumu) aynı kullanıcının İKİ oturumu (RDP + konsol, fast user switching)
    /// AYNI pipe adını paylaşırdı: ikinci oturumun instance'ı kendi oturumunda <c>IsFirst=true</c> olur, dinlemeye
    /// başlar ve ilk oturumun ZATEN dinlediği pipe adında <c>NamedPipeServerStream</c> açmaya çalışıp
    /// <c>ERROR_PIPE_BUSY</c> alır. Anahtara oturum id'sini de katmak bu çakışmayı imkânsız kılar — istemci
    /// (<see cref="ActivateExistingInstance(TimeSpan, Func{int,bool})"/>) AYNI <c>_pipeName</c> alanını kullanır,
    /// çünkü o da kendi <c>Acquire</c> çağrısından (kendi oturumunda) gelir.</summary>
    internal static string BuildPipeName(string key, int sessionId) => $"{key}.{sessionId}.pipe";

    private static int CurrentSessionId()
    {
        using var current = Process.GetCurrentProcess();
        return current.SessionId;
    }

    /// <summary>Testte pipe'ı dışarıdan meşgul etmek/adı doğrulamak için — üretim kodu bunu okumaz.</summary>
    internal string PipeName => _pipeName;

    /// <summary>İlk instance: aktivasyon sinyallerini dinlemeye başlar. <paramref name="onActivateRequested"/>
    /// bir arka plan thread'inden çağrılır — çağıran taraf UI thread'ine kendi marshal'ını yapar.</summary>
    public void StartListening(Action onActivateRequested) =>
        StartListening(onActivateRequested, ct => Task.Delay(PipeRetryDelay, ct));

    /// <summary>Test kancası: pipe meşgulken uygulanan geri-çekilme enjekte edilebilir (spin-yok assert'i için).</summary>
    internal void StartListening(Action onActivateRequested, Func<CancellationToken, Task> retryDelay)
    {
        _ = Task.Run(() => ListenLoopAsync(onActivateRequested, retryDelay, _cts.Token), CancellationToken.None);
    }

    /// <summary>İkinci instance: çalışan ilk instance'ı öne getirir. Karşıda kimse yoksa/hata olursa
    /// <c>false</c> — uygulama yine de sessizce kapanır (kullanıcıya hata gösterilmez).</summary>
    public bool ActivateExistingInstance(TimeSpan timeout) =>
        ActivateExistingInstance(timeout, pid => Win32.AllowSetForegroundWindow(pid));

    /// <summary>Test kancası: <c>AllowSetForegroundWindow</c> enjekte edilebilir (sıra assert'i için).</summary>
    internal bool ActivateExistingInstance(TimeSpan timeout, Func<int, bool> allowSetForeground)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            client.Connect((int)timeout.TotalMilliseconds);
            SingleInstanceProtocol.ActivateExisting(
                readOwnerPid: () => ReadInt32(client),
                allowSetForeground: allowSetForeground,
                signal: b => { client.WriteByte(b); client.Flush(); client.WaitForPipeDrain(); });
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task ListenLoopAsync(Action onActivateRequested, Func<CancellationToken, Task> retryDelay, CancellationToken ct)
    {
        var pid = BitConverter.GetBytes(Environment.ProcessId);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await server.WriteAsync(pid, ct).ConfigureAwait(false); // §4.3 — istemci ÖNCE pid'i alır
                await server.FlushAsync(ct).ConfigureAwait(false);

                var signal = new byte[1];
                int read = await server.ReadAsync(signal, ct).ConfigureAwait(false);
                if (read != 1 || signal[0] != SingleInstanceProtocol.ActivateSignal) continue;

                // Geri çağırım UI'a marshal edilir; kapanış yarışında (dispatcher kapanmış) atacağı hata bu
                // fire-and-forget döngüyü GÖZLENMEMİŞ bir exception ile öldürmemeli.
                try { onActivateRequested(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[single-instance] aktivasyon hatası: {ex}"); }
            }
            catch (OperationCanceledException) { return; }
            catch (IOException)
            {
                // [I-1 fix wave] istemci yarıda koptu YA DA pipe meşgul (ERROR_PIPE_BUSY — başka bir oturum/process
                // aynı adı tutuyor). Boş dönüp anında yeniden denemek CPU'yu spin'e sokardı; geri-çekilerek bekle.
                // ct iptal edilirse Task.Delay OperationCanceledException fırlatır — döngüye SIZDIRILMAZ, direkt döner.
                try { await retryDelay(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static int ReadInt32(Stream stream)
    {
        var buffer = new byte[sizeof(int)];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new IOException("The single-instance handshake was cut short.");
            offset += read;
        }
        return BitConverter.ToInt32(buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* sahibi değiliz */ }
        }
        _mutex.Dispose();
        _cts.Dispose();
    }
}
