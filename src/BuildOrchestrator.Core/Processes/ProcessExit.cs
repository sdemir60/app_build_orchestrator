using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace BuildOrchestrator.Core.Processes;

/// <summary>
/// Bir child process'in çıkışını bekleyip çıkış kodunu okumanın TEK yeri — <see cref="ProcessRunner"/> (git, vswhere) ve
/// <c>JobChildProcess</c> (MSBuild) buradan alır.
/// <para><b>Neden ayrı bir onay:</b> <see cref="Process.WaitForExitAsync"/> Exited olayıyla döner; <see cref="Process.ExitCode"/> ise
/// <see cref="Process.HasExited"/>'e, yani sürecin kendi tanıtıcısına sorar. İkisi nadiren ayrışır: CI'da (2026-10-13, windows-2025,
/// .NET 10.0.12) bekleyiş normal döndükten hemen sonra <c>ExitCode</c> "Process must exit before requested information can be
/// determined" attı — o an <c>HasExited</c> false'tu. Runtime içi bir zamanlama; yerelde 2000 süreçlik sondada üretilemedi. Çıkış
/// kodu bu yüzden tanıtıcı çıkışı onaylayınca okunur; olağan yolda onay anındadır, ek bekleme yoktur.</para>
/// <para><b>Bekleme yoklama DEĞİLDİR (D8):</b> onay gelmediyse sürecin kendi kernel nesnesine beklenir (çıkışta sinyallenir) —
/// iptalin tanıtıcısıyla birlikte, uyumadan ve zamanlayıcısız.</para>
/// </summary>
internal static class ProcessExit
{
    /// <summary><see cref="Process.WaitForExitAsync"/>'i bekler, sonra çıkışın onayıyla çıkış kodunu okur
    /// (<see cref="ExitCodeOnceExitedAsync"/>). İptal ya da zaman aşımı <see cref="OperationCanceledException"/> olarak çıkar.</summary>
    public static async Task<int> WaitForExitCodeAsync(Process process, CancellationToken ct)
    {
        await process.WaitForExitAsync(ct);
        return await ExitCodeOnceExitedAsync(process, ct);
    }

    /// <summary>Sürecin çıkış kodu — sürecin kendi tanıtıcısı çıkışı onaylayınca (<see cref="Process.HasExited"/>). Onay yoksa
    /// sürecin kernel nesnesine beklenir; beklemenin tavanı <paramref name="ct"/>'dir (çağıranın iptali ya da zaman aşımı): çıkmayan
    /// bir süreçte sonsuza dek beklemez, <see cref="OperationCanceledException"/> ile çıkar.</summary>
    internal static async Task<int> ExitCodeOnceExitedAsync(Process process, CancellationToken ct)
    {
        if (!process.HasExited)
            await Task.Run(() => WaitForProcessObject(process, ct), CancellationToken.None).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Sürecin kernel nesnesine (çıkınca sinyallenir) ya da iptale bekler. Tanıtıcı bekleme boyunca kapanmasın diye
    /// referansı tutulur; sarmalayıcı ona sahip olmaz (kapatmak <see cref="Process"/>'in işidir).</summary>
    private static void WaitForProcessObject(Process process, CancellationToken ct)
    {
        SafeProcessHandle handle = process.SafeHandle;
        bool held = false;
        handle.DangerousAddRef(ref held);
        try
        {
            using var processObject = new BorrowedWaitHandle(handle.DangerousGetHandle());
            if (WaitHandle.WaitAny([processObject, ct.WaitHandle]) != 0)
                ct.ThrowIfCancellationRequested();
        }
        finally
        {
            if (held) handle.DangerousRelease();
        }
    }

    /// <summary>Sahibi olmadığı bir kernel tanıtıcısını <see cref="WaitHandle"/> olarak bekletir (Dispose tanıtıcıyı kapatmaz).</summary>
    private sealed class BorrowedWaitHandle : WaitHandle
    {
        public BorrowedWaitHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: false);
    }
}
