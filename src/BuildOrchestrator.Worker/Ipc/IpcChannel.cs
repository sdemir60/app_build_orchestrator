using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Worker.Ipc;

/// <summary>
/// Newline-delimited JSON channel over stdio (Section 2): reads <see cref="WorkerCommand"/> from
/// stdin, writes <see cref="WorkerEvent"/> to stdout. Writes are serialized with a lock so
/// concurrent build threads never interleave output.
/// </summary>
public sealed class IpcChannel
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly object _writeLock = new();

    public IpcChannel(TextReader input, TextWriter output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Blocking enumeration of incoming commands until stdin closes.</summary>
    public IEnumerable<WorkerCommand> ReadCommands()
    {
        string? line;
        while ((line = _input.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            WorkerCommand? cmd = null;
            try
            {
                cmd = IpcJson.DeserializeCommand(line);
            }
            catch
            {
                // Ignore malformed input lines.
            }
            if (cmd != null)
                yield return cmd;
        }
    }

    public void Send(WorkerEvent evt)
    {
        var json = IpcJson.SerializeEvent(evt);
        lock (_writeLock)
        {
            _output.WriteLine(json);
            _output.Flush();
        }
    }
}
