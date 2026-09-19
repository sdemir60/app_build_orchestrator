using BuildOrchestrator.Core.Git;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Faz 2/T7] Sahte HEAD izleyicisi — koordinatör ve VM testlerinin TEK sahtesi (kopya YASAK). Kurulumu testin
/// elinde (<see cref="Starts"/>), geri çağrıyı yakalar (<see cref="OnSettled"/>) — test onu elle ateşler.
/// </summary>
internal sealed class FakeHeadWatcher : IHeadWatcher
{
    public const string Reason = "access denied";

    /// <summary>Başlatma başarılı olsun mu; değilse <see cref="UnavailableReason"/> = <see cref="Reason"/>.</summary>
    public bool Starts { get; init; } = true;

    public string? UnavailableReason { get; private set; }

    /// <summary>Son başarılı <see cref="Start"/>'ın geri çağrısı.</summary>
    public Action<HeadMove>? OnSettled { get; private set; }

    public bool Disposed { get; private set; }

    public bool Start(string gitDir, Action<HeadMove> onSettled)
    {
        if (!Starts)
        {
            UnavailableReason = Reason;
            return false;
        }
        OnSettled = onSettled;
        return true;
    }

    public void Dispose() => Disposed = true;
}
