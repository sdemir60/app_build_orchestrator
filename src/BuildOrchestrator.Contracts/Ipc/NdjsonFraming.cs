using System.Text.Json;

namespace BuildOrchestrator.Contracts.Ipc;

public sealed class IpcFramingException(string message) : Exception(message);

public sealed class NdjsonWriter(Stream stream)
{
    public const int MaxLineBytes = 1_048_576; // 1 MiB — chunk'lar 64K olduğundan bol pay
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Base-type kısıtı: discriminator daima yazılır (polimorfizm footgun kapandı). [it0-devir]
    public Task WriteAsync(IpcCommand message, CancellationToken ct = default) => WriteCoreAsync(message, ct);
    public Task WriteAsync(IpcEvent message, CancellationToken ct = default) => WriteCoreAsync(message, ct);

    private async Task WriteCoreAsync<T>(T message, CancellationToken ct)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Options); // JSON escape → payload'da ham \n olamaz
        if (payload.Length + 1 > MaxLineBytes)
            throw new IpcFramingException($"IPC message is {payload.Length} bytes — MaxLineBytes ({MaxLineBytes}) exceeded.");
        await _gate.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(payload, ct);
            await stream.WriteAsync(NewLine, ct);
            await stream.FlushAsync(ct);
        }
        finally { _gate.Release(); }
    }
}

public sealed class NdjsonReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly MemoryStream _line = new();
    private int _start, _end;

    /// <summary>null = stream kapandı (EOF). Deserialize hatası fırlar ama satır tüketilmiştir (framing korunur).</summary>
    public async Task<T?> ReadAsync<T>(CancellationToken ct = default) where T : class
    {
        while (true)
        {
            int nl = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (nl >= 0)
            {
                _line.Write(_buffer, _start, nl - _start);
                _start = nl + 1;
                if (_line.Length == 0) continue; // boş satır tolere
                try
                {
                    return JsonSerializer.Deserialize<T>(_line.GetBuffer().AsSpan(0, (int)_line.Length), IpcJson.Options)
                           ?? throw new IpcFramingException("null IPC message.");
                }
                finally { _line.SetLength(0); }
            }
            _line.Write(_buffer, _start, _end - _start);
            _start = _end = 0;
            if (_line.Length > NdjsonWriter.MaxLineBytes)
                throw new IpcFramingException($"IPC line exceeded MaxLineBytes ({NdjsonWriter.MaxLineBytes}).");
            int read = await stream.ReadAsync(_buffer, ct);
            if (read == 0)
            {
                if (_line.Length > 0) throw new IpcFramingException("EOF: incomplete IPC line.");
                return null;
            }
            _end = read;
        }
    }
}
