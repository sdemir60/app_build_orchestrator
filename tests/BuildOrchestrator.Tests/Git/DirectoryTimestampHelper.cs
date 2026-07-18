using System;
using System.IO;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [Review fix — Task 9] Bir dizin ağacındaki TÜM dosyaların (+ dizinin kendisinin) <c>LastWriteTimeUtc</c>'sini
/// deterministik biçimde geriye tarihler (D8 — sleep-poll YERİNE). <see
/// cref="BuildOrchestrator.Core.Git.WorktreeManager"/>'ın <c>ComputeDirStats</c>'i LRU <c>LastUsedUtc</c>'yi
/// worktree içindeki TÜM dosyaların (recursive) MAX <c>LastWriteTimeUtc</c>'si olarak hesapladığından, LRU
/// sırasını testte sabitlemek için TEK bir sidecar dosyasını backdate etmek YETERSİZDİR — <c>git worktree
/// add</c>'in yazdığı diğer tüm dosyalar hâlâ "şimdi" zamanına sahip olur ve max() bunları ezer. Bu yüzden bu
/// yardımcı AĞACIN TAMAMINI backdate eder.
/// </summary>
internal static class DirectoryTimestampHelper
{
    public static void SetAllTimestampsUtc(string rootDir, DateTime utc)
    {
        foreach (string file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, utc);

        foreach (string dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories))
            Directory.SetLastWriteTimeUtc(dir, utc);

        Directory.SetLastWriteTimeUtc(rootDir, utc);
    }
}
