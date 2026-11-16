using System;
using System.Collections.Generic;
using System.IO;
using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use] Kilitli kopya testlerinin ORTAK yardımcıları (kopya YASAK — Core testleri ve koordinatör testleri aynı simülasyonu
/// kullanır).
/// <list type="bullet">
/// <item><see cref="HoldLikeALoadedDll"/>: görüntü olarak yüklenmiş bir DLL'in kilidinin birebir taklidi — üzerine yazılamaz, yazmak için
/// açılamaz, ama yeniden adlandırılabilir (silmeyi paylaşan, yazmayı paylaşmayan tutamaç).</item>
/// <item><see cref="HoldWithoutDeleteSharing"/>: yeniden adlandırılamayan kilit (silmeyi de paylaşmayan tutamaç).</item>
/// <item><see cref="Run"/>: bir post-build olayını GERÇEKTEN çalıştırır — MSBuild'in normal verbosity'deki gibi komutları
/// <c>PostBuildEvent:</c> başlığı altında yankılar, dosyaları <c>cmd</c>'nin kopyası gibi değiştirilme zamanını koruyarak kopyalar,
/// kilitli hedefte <c>cmd</c>'nin satırını yazar ve çıkış kodunu <c>cmd</c>'nin toplu dosya kuralıyla — SON satırın sonucu — verir;
/// düşen son satırda MSBuild'in <c>MSB3073</c> satırını da yazar.</item>
/// </list>
/// </summary>
internal static class PostBuildCopy
{
    public static FileStream HoldLikeALoadedDll(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    public static FileStream HoldWithoutDeleteSharing(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <param name="lines">Olayın satırları: her biri <c>copy /y "&lt;desen&gt;" "&lt;klasör&gt;\"</c>.</param>
    public static MsBuildInvokeResult Run(string project, IReadOnlyList<(string Pattern, string Folder)> lines, Action<string> onLine)
    {
        onLine("PostBuildEvent:");
        foreach (var (pattern, folder) in lines) onLine($"  copy /y \"{pattern}\" \"{folder}\\\"");
        bool lastFailed = false;
        foreach (var (pattern, folder) in lines)
        {
            lastFailed = false;
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(pattern)!, Path.GetFileName(pattern)))
            {
                onLine("  " + file);
                try
                {
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
                }
                catch (IOException)
                {
                    onLine("  The process cannot access the file because it is being used by another process.");
                    lastFailed = true;
                }
                catch (UnauthorizedAccessException)
                {
                    onLine("  Access is denied.");
                    lastFailed = true;
                }
            }
        }
        if (lastFailed)
            onLine($"C:\\MSBuild\\Microsoft.Common.CurrentVersion.targets(6155,5): error MSB3073: The command \"copy /y \"{lines[^1].Pattern}\" "
                + $"\"{lines[^1].Folder}\\\"\" exited with code 1. [{project}]");
        return new MsBuildInvokeResult(lastFailed ? 1 : 0, DurationMs: 100, TimedOut: false, Killed: false);
    }
}
