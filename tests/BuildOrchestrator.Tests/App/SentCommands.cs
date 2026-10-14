using System.Collections;
using System.Collections.Concurrent;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 son inceleme · defter 4] VM'in gönderdiği komutların THREAD-SAFE kaydı (<c>RunViewModel.DebugOnCommandSent</c>) —
/// gerçek motorla koşan testlerin TEK kaydedicisi. Gerçek motorun gönderimi asenkron G/Ç'dir: gönderimin devamı thread havuzunda
/// koşabilir, test thread'i de aynı anda kaydı okur — düz bir <see cref="List{T}"/> eşzamanlı eklemede "Collection was modified"
/// fırlatır ya da kayıt kaybeder (bir kez görüldü: <c>RunViewModelTests.When_the_marker_goes_the_waiting_sync_runs</c>). Kayıt bir
/// <see cref="ConcurrentQueue{T}"/>'dur; sayım anlık bir görüntü üzerinden yapılır. Gönderimi motor yerine sahte bir dikişin senkron
/// "yaptığı" testler (<see cref="MainWindowHost.AcceptSends"/>) düz listeyle yetinir: orada ikinci bir thread yoktur.
/// </summary>
internal sealed class SentCommands : IEnumerable<IpcCommand>
{
    private readonly ConcurrentQueue<IpcCommand> _sent = new();

    /// <summary>VM'in gönderdiği her komut kaydedilir — <c>DebugOnCommandSent</c> bu kayda bağlanır.</summary>
    public static SentCommands RecordOn(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var sent = new SentCommands();
        vm.DebugOnCommandSent = sent.Record;
        return sent;
    }

    /// <summary>Bir gönderimi kaydeder — gönderim anında başka bir şeyi de ölçen ve dikişi kendisi kuran testler bunu çağırır.</summary>
    public void Record(IpcCommand command) => _sent.Enqueue(command);

    public IEnumerator<IpcCommand> GetEnumerator() => _sent.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
