using System.Diagnostics;

namespace BuildOrchestrator.Core.Processes;

/// <summary>
/// Bir child process'in çıkışını bekleyip çıkış kodunu okumanın TEK yeri — <see cref="ProcessRunner"/> (git, vswhere) ve
/// <c>JobChildProcess</c> (MSBuild) buradan alır.
/// <para><b>Neden ayrı bir onay:</b> <see cref="Process.WaitForExitAsync"/> Exited olayıyla döner; <see cref="Process.ExitCode"/> ise
/// <see cref="Process.HasExited"/>'e, yani sürecin kendi tanıtıcısına sorar. İkisi nadiren ayrışır: CI'da (2026-10-13, windows-2025,
/// .NET 10.0.12) bekleyiş normal döndükten hemen sonra <c>ExitCode</c> "Process must exit before requested information can be
/// determined" attı — o an <c>HasExited</c> false'tu. Runtime içi bir zamanlama; yerelde 2000 süreçlik sondada üretilemedi. Çıkış
/// kodu bu yüzden tanıtıcı çıkışı onaylayınca okunur; olağan yolda onay anında gelir (döngü hiç dönmez).</para>
/// </summary>
internal static class ProcessExit
{
    /// <summary>Onay beklerken iki yoklama arası: erken dönen bir bekleyiş birkaç milisaniyelik ek beklemeye döner.</summary>
    internal static readonly TimeSpan ConfirmPoll = TimeSpan.FromMilliseconds(10);

    /// <summary><see cref="Process.WaitForExitAsync"/>'i bekler, sonra çıkışın onayıyla çıkış kodunu okur
    /// (<see cref="ExitCodeOnceExitedAsync"/>). İptal ya da zaman aşımı <see cref="OperationCanceledException"/> olarak çıkar.</summary>
    public static async Task<int> WaitForExitCodeAsync(Process process, CancellationToken ct)
    {
        await process.WaitForExitAsync(ct);
        return await ExitCodeOnceExitedAsync(process, ct);
    }

    /// <summary>Sürecin çıkış kodu — sürecin kendi tanıtıcısı çıkışı onaylayınca (<see cref="Process.HasExited"/>); onaylamadıkça
    /// <see cref="ConfirmPoll"/> aralıkla yeniden sorar. Beklemenin tavanı <paramref name="ct"/>'dir (çağıranın iptali ya da zaman
    /// aşımı): çıkmayan bir süreçte sonsuza dek beklemez, <see cref="OperationCanceledException"/> ile çıkar.</summary>
    internal static async Task<int> ExitCodeOnceExitedAsync(Process process, CancellationToken ct)
    {
        while (!process.HasExited)
            await Task.Delay(ConfirmPoll, ct);
        return process.ExitCode;
    }
}
