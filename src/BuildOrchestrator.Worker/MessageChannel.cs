using System.Text;
using System.Text.Json;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Worker;

/// <summary>
/// Newline-delimited JSON channel over a reader/writer pair (stdio by default). One <see cref="Message"/>
/// per line. A dedicated lock serializes writes so concurrent build events don't interleave.
/// </summary>
public sealed class MessageChannel
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly object _writeGate = new();

    public MessageChannel(TextReader reader, TextWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public static MessageChannel Stdio()
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        return new MessageChannel(stdin, stdout);
    }

    /// <summary>Reads the next message, or null at end of stream.</summary>
    public async Task<Message?> ReadAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                return JsonSerializer.Deserialize<Message>(line, ProtocolJson.Options);
            }
            catch (JsonException)
            {
                // Skip malformed lines rather than crashing the worker.
                continue;
            }
        }
    }

    public void Write(Message message)
    {
        var line = JsonSerializer.Serialize(message, ProtocolJson.Options);
        lock (_writeGate)
        {
            _writer.Write(line);
            _writer.Write('\n');
            _writer.Flush();
        }
    }

    public void WriteEvent(string name, object payload, string? correlationId = null)
        => Write(Message.Event(name, payload, correlationId));
}
