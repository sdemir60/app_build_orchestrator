using System.Threading;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Single-instance lock (Section 2). The first instance owns a named mutex and listens on a
/// named event; a second launch signals the event (so the first can foreground its window) and
/// then exits.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Global\\BuildOrchestrator.SingleInstance.Mutex";
    private const string EventName = "Global\\BuildOrchestrator.SingleInstance.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _listenThread;
    private volatile bool _running;

    /// <summary>Raised (on a background thread) when another instance asks to activate.</summary>
    public event Action? ActivationRequested;

    public bool IsFirstInstance { get; private set; }

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        if (createdNew)
        {
            _running = true;
            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "SingleInstanceListener" };
            _listenThread.Start();
        }
        return createdNew;
    }

    /// <summary>Called by the second instance to wake the first one.</summary>
    public void SignalExistingInstance()
    {
        try
        {
            var handle = EventWaitHandle.OpenExisting(EventName);
            handle.Set();
        }
        catch
        {
            // First instance may be shutting down.
        }
    }

    private void ListenLoop()
    {
        while (_running)
        {
            if (_activateEvent!.WaitOne(500))
                ActivationRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _running = false;
        _activateEvent?.Set();
        _activateEvent?.Dispose();
        if (_mutex != null && IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex?.Dispose();
    }
}
