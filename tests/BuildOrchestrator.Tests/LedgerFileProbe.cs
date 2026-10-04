using System;
using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>
/// Defter dosyasının "yeniden yazıldı mı" sondası — <c>EvaluationCache</c> ve <c>SourceHashCache</c> kirli bayrağı
/// testlerinin ORTAK yardımcısı (kopya YASAK). Dosyanın mtime'ı çok eski bir damgaya sabitlenir; defter dosyayı
/// yeniden yazarsa (temp + <c>File.Move</c> → dosya yer değiştirir) damga bugüne gelir. Saat ve uyku yok:
/// deterministik.
/// </summary>
internal sealed class LedgerFileProbe
{
    private static readonly DateTime PinnedUtc = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _path;

    private LedgerFileProbe(string path) => _path = path;

    /// <summary>Dosyanın mtime'ını eski damgaya çeker ve sondayı döner (dosya VAR olmalı).</summary>
    public static LedgerFileProbe Pin(string path)
    {
        File.SetLastWriteTimeUtc(path, PinnedUtc);
        return new LedgerFileProbe(path);
    }

    /// <summary>Sonda kurulduğundan beri dosya yeniden yazıldı (yer değiştirdi) mi.</summary>
    public bool WasRewritten => File.GetLastWriteTimeUtc(_path) != PinnedUtc;
}
