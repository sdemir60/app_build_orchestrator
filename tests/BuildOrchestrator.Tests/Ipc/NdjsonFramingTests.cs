using BuildOrchestrator.Contracts.Ipc;
using System.IO;
using Xunit;

namespace BuildOrchestrator.Tests.Ipc;

public class NdjsonFramingTests
{
    static (NdjsonWriter w, MemoryStream s) W() { var s = new MemoryStream(); return (new NdjsonWriter(s), s); }

    [Fact]
    public async Task Writer_then_reader_roundtrips_two_messages_back_to_back()
    {
        var (w, s) = W();
        await w.WriteAsync(new PongEvent(1));
        await w.WriteAsync(new ErrorEvent("a", "b"));
        s.Position = 0;
        var r = new NdjsonReader(s);
        Assert.Equal(new PongEvent(1), await r.ReadAsync<IpcEvent>());
        Assert.Equal(new ErrorEvent("a", "b"), await r.ReadAsync<IpcEvent>());
        Assert.Null(await r.ReadAsync<IpcEvent>()); // EOF
    }

    [Fact]
    public async Task Writer_rejects_oversize_message()
    {
        var (w, _) = W();
        var big = new ErrorEvent("x", new string('y', NdjsonWriter.MaxLineBytes));
        await Assert.ThrowsAsync<IpcFramingException>(() => w.WriteAsync(big));
    }

    [Fact]
    public async Task Reader_rejects_oversize_line()
    {
        var s = new MemoryStream(); var bytes = new byte[NdjsonWriter.MaxLineBytes + 10];
        Array.Fill(bytes, (byte)'a'); s.Write(bytes); s.Position = 0;
        await Assert.ThrowsAsync<IpcFramingException>(() => new NdjsonReader(s).ReadAsync<IpcEvent>());
    }

    [Fact]
    public async Task Reader_recovers_after_garbage_line()
    {
        var s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("bu json degil\n{\"type\":\"pong\",\"seq\":5}\n"));
        var r = new NdjsonReader(s);
        await Assert.ThrowsAnyAsync<Exception>(() => r.ReadAsync<IpcEvent>()); // çöp satır
        Assert.Equal(new PongEvent(5), await r.ReadAsync<IpcEvent>());        // framing bozulmadı
    }

    [Fact]
    public async Task WriteAsync_concrete_type_still_emits_polymorphic_discriminator()
    {
        using var ms = new MemoryStream();
        var writer = new NdjsonWriter(ms);
        await writer.WriteAsync(new PongEvent(7)); // concrete tip — base overload'a bağlanmalı
        string line = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"type\":\"pong\"", line); // discriminator yazıldı
        ms.Position = 0;
        var ev = await new NdjsonReader(ms).ReadAsync<IpcEvent>();
        Assert.IsType<PongEvent>(ev); // round-trip base tipe deserialize oldu
    }
}
