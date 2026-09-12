using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// Gerçek bir <c>SupervisorHost</c>'u process açmadan koşturan dispatch testlerinin ORTAK parçaları: gözlenen
/// bir stdout, komutlarla doldurulup EOF'a bırakılan bir stdin ve testlerin asılı kalmaması için ortak bir
/// süre sınırı.
///
/// <para>Tek yerde durmalarının sebebi kopya yasağıdır: <c>CleanDispatchTests</c> ve
/// <c>OptimizeDispatchTests</c> aynı üç parçayı birebir taşıyordu. Servis fabrikaları burada DEĞİLDİR —
/// her dispatch testi hangi Core servisini gerçek, hangisini sahte kurduğuna kendisi karar verir ve o karar
/// testin okunabilir kısmıdır.</para>
/// </summary>
internal static class SupervisorHostHarness
{
    /// <summary>Host'un bir komut turunu bitirmesi için verilen üst sınır — aşılırsa test ASILMAZ, düşer.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>Verilen komutları NDJSON olarak yazıp başa saran bir stdin. Komutlar tükenince EOF gelir ve
    /// host düzenli çıkar — testin host'u durdurmak için ayrı bir sinyale ihtiyacı olmaz.</summary>
    public static async Task<MemoryStream> StdinWith(params IpcCommand[] commands)
    {
        var stdin = new MemoryStream();
        var writer = new NdjsonWriter(stdin);
        foreach (var cmd in commands) await writer.WriteAsync(cmd);
        stdin.Position = 0;
        return stdin;
    }

    /// <summary>Yalnız-yazılır stdout taklidi: yazılan her baytı biriktirir, <see cref="Text"/> ile okunur.</summary>
    public sealed class CollectingStream : Stream
    {
        private readonly StringBuilder _text = new();
        private readonly object _gate = new();

        public string Text { get { lock (_gate) return _text.ToString(); } }

        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            lock (_gate) _text.Append(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Test klasörlerini best-effort siler: kilitli bir dosya testi DÜŞÜRMEZ.</summary>
    public static void TryDeleteDirectories(params string[] dirs)
    {
        foreach (string dir in dirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
