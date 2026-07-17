using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

[Trait("Category", "MsBuild")]
public class MsBuildInvokerTests
{
    // MSBuild yoksa (VS/Build Tools kurulu değil) SkippableFact ile atla — mevcut MsBuildResolverTests deseni.
    private static async Task<string> ResolveMsBuildExeOrSkipAsync()
    {
        try
        {
            var loc = await new MsBuildResolver(new ProcessRunner()).ResolveAsync();
            return loc.MsBuildExePath;
        }
        catch (MsBuildResolveException ex)
        {
            Skip.If(true, ex.Message);
            throw; // ulaşılmaz — Skip.If(true, …) her zaman fırlatır
        }
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        string dir = Path.Combine(Path.GetTempPath(), "boi-msbuild-tests", caller, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [SkippableFact] // (a) LegacyFixture v4.6 classlib → exit 0 + satır aktı + DLL üretildi
    public async Task Legacy_classlib_builds_green_streams_lines_and_produces_dll()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "SampleLib");

        using var job = JobObject.CreateKillOnClose();
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Killed);
        Assert.NotEmpty(lines);
        Assert.True(File.Exists(Path.Combine(dir, "bin", "Debug", "SampleLib.dll")), "beklenen DLL üretilmedi");
    }

    [SkippableFact] // (b) derleme hatası olan fixture → exit≠0 + satırlarda "error CS"
    public async Task Compile_error_fixture_fails_with_error_CS_in_lines()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "BrokenLib");
        // Class1.cs'in üzerine kasıtlı sözdizimi hatası yaz (LegacyFixture'ın kendisi hep geçerli üretir).
        File.WriteAllText(Path.Combine(dir, "Class1.cs"), "public class Class1 { this is not valid C# ][ }");

        using var job = JobObject.CreateKillOnClose();
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(lines, l => l.Contains("error CS", StringComparison.Ordinal));
    }

    [SkippableFact] // (c) ct iptali → Killed=true + process gerçekten yok (deterministik: IOCP ile pid izlenir)
    public async Task Cancellation_kills_child_and_process_is_gone()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "CancelLib");

        using var job = JobObject.CreateKillOnClose();
        using var iocp = job.AttachCompletionPort(); // Launch'tan ÖNCE bağlanmalı — kaçırılan bildirim olmasın
        var invoker = new MsBuildInvoker(job, exe);
        using var cts = new CancellationTokenSource();

        var invokeTask = invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            _ => { }, cts.Token);

        // İlk NEW_PROCESS bildirimi = doğan MSBuild.exe child (NeedsRestore:false → tek child). Doğar doğmaz iptal et.
        var born = iocp.WaitNext(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("child doğum bildirimi gelmedi");
        Assert.Equal(NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS, born.MessageId);
        int pid = born.Pid;
        cts.Cancel();

        var result = await invokeTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(result.Killed);
        Assert.False(result.TimedOut); // caller iptali — PerProjectTimeout değil

        // pid'in job'dan çıktığını IOCP bildirimiyle doğrula (sleep-poll YOK — bloklu bekleme).
        while (true)
        {
            var n = iocp.WaitNext(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException("exit bildirimi gelmedi");
            if (n.Pid == pid && n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS
                                             or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS)
                break;
        }
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact] // (d) — gerçek MSBuild gerektirmez: saf encoding round-trip
    public void OutputEncoding_is_system_acp_and_round_trips_turkish_text()
    {
        // Fix wave 1 / Finding 4: kaynak artık CultureInfo.CurrentCulture.TextInfo.ANSICodePage (kullanıcı
        // culture'ı) DEĞİL, GetACP() (sistem ACP'si) — test de aynı kaynağı doğrulamalı, yoksa bu iki değer
        // aynı makinede tesadüfen eşit olduğu için fix'i gerçekten sınamaz.
        int expectedCodePage = (int)NativeMethods.GetACP();
        Assert.Equal(expectedCodePage, MsBuildOutputEncoding.Value.CodePage);

        const string turkish = "İstanbul: Özgün Çözüm Üretiliyor — Şükrü Iğdır";
        byte[] encoded = MsBuildOutputEncoding.Value.GetBytes(turkish);
        string roundTripped = MsBuildOutputEncoding.Value.GetString(encoded);
        Assert.Equal(turkish, roundTripped);
    }

    [SkippableFact] // Fix wave 1 / Finding 1 regresyon: post-build event MSBuild.exe'den sonra da yaşayan
                     // detached bir grandchild (ping.exe) bırakır — stdout/stderr pipe'ının write-end kopyasını
                     // MSBuild.exe çıktıktan SONRA da tutar. Fix ÖNCESİ RunChildAsync'in başarı yolundaki
                     // unbounded `Task.WhenAll(stdoutTask, stderrTask)` bu EOF'u hiç göremez (RED — dış
                     // WaitAsync(20s) TimeoutException fırlatır); fix SONRASI WaitPumpsBoundedAsync
                     // PostKillWait(5s) içinde döner (GREEN). Bkz. task-5-report.md "Fix wave 1" RED/GREEN kanıtı.
    public async Task LingeringPostBuildGrandchild_does_not_stall_success_path()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLibWithLingeringPostBuild(dir, "LingerLib", sleepSeconds: 60);

        using var job = JobObject.CreateKillOnClose(); // grandchild breakaway YAPAMAZ — test sonunda Dispose kaskadıyla temizlenir
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var sw = Stopwatch.StartNew();
        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)); // hang'i sonsuza kadar değil, teste çevirir
        sw.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Killed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"başarı yolu sınırlı sürede dönmedi: {sw.Elapsed} (grandchild hâlâ ayakta olabilir — fix eksik/bozuk)");
    }
}
