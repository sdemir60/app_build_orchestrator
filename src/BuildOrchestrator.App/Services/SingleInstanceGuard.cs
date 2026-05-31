using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Enforces a single running instance (Section 2). The first instance owns a named mutex and listens
/// on a named pipe; a second launch connects to the pipe to ask the first to bring its window to the
/// foreground, then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\BuildOrchestrator.SingleInstance";
    private const string PipeName = "BuildOrchestrator.Activate";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCts;

    public bool IsPrimaryInstance { get; }

    /// <summary>Raised on the primary instance when another launch requests activation.</summary>
    public event Action? ActivationRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>Primary instance: start listening for activation requests from future launches.</summary>
    public void StartListener()
    {
        if (!IsPrimaryInstance)
        {
            return;
        }

        _listenerCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
    }

    /// <summary>Secondary instance: signal the primary to come forward. Returns true on success.</summary>
    public static bool SignalPrimary()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("activate");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                _ = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // ignore and keep listening
            }
        }
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        try
        {
            if (IsPrimaryInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
            // ignore
        }
        _mutex.Dispose();
    }
}
