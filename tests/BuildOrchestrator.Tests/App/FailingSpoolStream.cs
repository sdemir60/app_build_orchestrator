using System.IO;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.3] İstenince yazımı düşen spool akışı — dolan disk, kopan ağ sürücüsü. Üretimin açtığı gerçek
/// dosyayı sarar (<c>NarrativeStore.OpenSpoolFile</c>): <see cref="Fail"/> açılana dek her bayt dosyaya gider, açıldıktan sonra her
/// yazım ve boşaltma <see cref="IOException"/> fırlatır. Kapatma iç dosyayı da kapatır — tutamaç sızmaz. Store testleri ve VM'in uyarı
/// sırası testi aynı akışı kullanır (kopya YASAK).
/// </summary>
internal sealed class FailingSpoolStream(Stream inner) : Stream
{
    /// <summary>[dalga 2 son inceleme O4] Store'un spool'u açılırken üretimin dosyasını bu akışla sarar (<c>NarrativeStore.OpenSpoolStream</c>)
    /// — dikişin TEK kurulumu (store ve VM testleri). Dönen çağrı açılan akışı verir; spool açılmadan çağrılırsa fırlatır.</summary>
    public static Func<FailingSpoolStream> InstallOn(NarrativeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        FailingSpoolStream? opened = null;
        store.OpenSpoolStream = path => opened = new FailingSpoolStream(NarrativeStore.OpenSpoolFile(path));
        return () => opened ?? throw new InvalidOperationException("the spool was not opened");
    }

    /// <summary>Açıkken her yazım ve boşaltma düşer.</summary>
    public bool Fail { get; set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ThrowIfFailing();
        inner.Flush();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowIfFailing();
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfFailing();
        inner.Write(buffer);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    private void ThrowIfFailing()
    {
        if (Fail) throw new IOException("There is not enough space on the disk.");
    }
}
