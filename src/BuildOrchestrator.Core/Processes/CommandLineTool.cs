using System.ComponentModel;
using BuildOrchestrator.Core.Git;

namespace BuildOrchestrator.Core.Processes;

/// <summary>
/// [Review fix — Task 9] Dış komut satırı araçlarının (git, tf) çalıştırılmasının TEK kaynağı: aynı
/// exception filtresi, aynı hata şablonu, aynı stderr fallback. Önce <c>GitService</c> ile
/// <c>WorktreeManager</c> bunu birebir aynı iki kopya halinde taşıyordu; harici projelerle birlikte
/// TFVC de aynı sarmalayıcıya ihtiyaç duydu ve mesajdaki araç adı parametreleşti — davranış hâlâ tek yerde.
///
/// <para>Timeout çağıran tarafından verilir: salt-okur sorgular 30 sn, çalışma ağacını yeniden yazan
/// komutlar (<c>worktree add/remove</c>, <c>merge --ff-only</c>, <c>tf vc get</c>) dakikalar sürebilir.</para>
/// </summary>
internal static class CommandLineTool
{
    /// <summary>Hata metinlerinde geçen araç adları — literal ikinci bir yerde yazılmaz.</summary>
    public const string Git = "git";
    public const string Tf = "tf";

    /// <summary>
    /// Komutu çalıştırır — araç bulunamazsa/başlatılamazsa (<see cref="Win32Exception"/>/<see
    /// cref="InvalidOperationException"/>) exception YUKARI SIZMAZ, tanımlı <see cref="GitResult{T}.Fail"/> döner.
    /// </summary>
    /// <param name="toolName">Hata metninde geçecek araç adı ("git"/"tf") — kullanıcıya hangi aracın
    /// başlatılamadığını söyler.</param>
    public static async Task<GitResult<ProcessResult>> RunAsync(
        IProcessRunner runner, string toolName, string executable, IReadOnlyList<string> args,
        string workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(new ProcessSpec(executable, args, workingDirectory, timeout), ct);
            return GitResult<ProcessResult>.Ok(result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return GitResult<ProcessResult>.Fail($"the {toolName} command could not be started ('{executable}'): {ex.Message}");
        }
    }

    /// <summary>Sıfır olmayan exit kodlu bir komut için okunabilir hata metni — stderr varsa onu, yoksa exit kodunu döner.</summary>
    public static string DescribeFailure(string toolName, ProcessResult r)
        => string.IsNullOrEmpty(r.StandardError)
            ? $"the {toolName} command exited with an unexpected exit code: {r.ExitCode}"
            : r.StandardError.Trim();
}
