using System.ComponentModel;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Review fix — Task 9] <see cref="GitService"/> ve <see cref="WorktreeManager"/>'ın önceden BİREBİR AYNI
/// (aynı exception filtresi <c>Win32Exception or InvalidOperationException</c>, aynı hata şablonu, aynı
/// stderr fallback) iki kopya halinde tuttuğu git-process çalıştırma mantığının DRY edilmiş TEK kaynağı
/// (bkz. <c>HashText</c> emsali — aynı davranış iki yerde elle senkron tutulmaz). Timeout çağıran tarafından
/// verilir (<see cref="GitService"/> 30s salt-okur sorgular için, <see cref="WorktreeManager"/> 5dk —
/// <c>worktree add/remove</c> büyük repo'da uzun sürebilir).
/// </summary>
internal static class GitCommandExecutor
{
    /// <summary>Git komutunu çalıştırır — git bulunamazsa/başlatılamazsa (<see cref="Win32Exception"/>/<see cref="InvalidOperationException"/>) exception YUKARI SIZMAZ, tanımlı <see cref="GitResult{T}.Fail"/> döner.</summary>
    public static async Task<GitResult<ProcessResult>> RunAsync(
        IProcessRunner runner, string gitExecutable, IReadOnlyList<string> args, string repoRoot, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(new ProcessSpec(gitExecutable, args, repoRoot, timeout), ct);
            return GitResult<ProcessResult>.Ok(result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return GitResult<ProcessResult>.Fail($"git komutu çalıştırılamadı ('{gitExecutable}'): {ex.Message}");
        }
    }

    /// <summary>Sıfır olmayan exit kodlu bir git komutu için okunabilir hata metni — stderr varsa onu, yoksa exit kodunu döner.</summary>
    public static string DescribeGitFailure(ProcessResult r)
        => string.IsNullOrEmpty(r.StandardError)
            ? $"git komutu beklenmeyen exit kodu ile sonlandı: {r.ExitCode}"
            : r.StandardError.Trim();
}
