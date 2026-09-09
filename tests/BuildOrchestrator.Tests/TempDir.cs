using System;
using System.IO;

namespace BuildOrchestrator.Tests;

/// <summary>Geçici bir dizin — <c>using</c> ömrü bitince kaskatla silinir (persist/keşif testleri için).</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "bo-temp-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* CI'da kilitli dosya — sızıntı testin sonucunu etkilemez */ }
    }
}
