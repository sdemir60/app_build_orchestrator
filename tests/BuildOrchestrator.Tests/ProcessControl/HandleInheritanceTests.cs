using System.IO;
using System.IO.Pipes;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

[Trait("Category", "ProcessControl")]
public class HandleInheritanceTests
{
    private static string CmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact] // It-2 giriş kriteri: redirected child YALNIZ kendi 3 pipe ucunu miras almalı
    public async Task Redirected_child_does_not_inherit_unrelated_inheritable_handles()
    {
        using var job = JobObject.CreateKillOnClose();
        // Kardeş bir launch'ın client-handle'ını taklit eder: parent READ ucunu tutar,
        // WRITE (client) ucu inheritable — paralel launch penceresinde parent'ta AÇIK olan tam da budur.
        using var sentinel = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        using var child = JobProcessLauncher.Launch(job,
            WindowsCommandLine.Build(CmdExe, "/c", "pause"), // stdin redirected → EOF gelene dek yaşar
            new LaunchOptions(RedirectStdio: true));
        sentinel.DisposeLocalCopyOfClientHandle(); // parent artık write ucunu tutmuyor

        var buf = new byte[1];
        int n = await sentinel.ReadAsync(buf, 0, 1).WaitAsync(TimeSpan.FromSeconds(5));
        // EOF (0) ⇒ hiçbir process write ucunu tutmuyor ⇒ child sentinel'ı miras ALMADI.
        // Sızsaydı write ucu child'da yaşar, EOF gelmez, WaitAsync TimeoutException ile patlardı.
        Assert.Equal(0, n);
    }

    [Fact] // Paralel launch: 5 kısa ömürlü ("echo") child + 1 UZUN ömürlü ("pause") child aynı anda redirected
           // launch edilir. Pre-fix, uzun ömürlü child snapshot anında diğerlerinin henüz kapatılmamış stdout
           // write-ucunu miras alabilir ve süresiz açık tuttuğu için o child'ın ReadToEnd'i EOF'a ulaşamaz
           // (timeout). Kısa ömürlü child'lar birbirlerine sızsa bile hemen çıkıp write-ucunu kendileri
           // kapattığından bu senaryoyu YAKALAMAZ — ayrım gücü uzun ömürlü child'tan gelir.
    public async Task Parallel_redirected_launches_each_reach_eof_on_their_own_stdout()
    {
        using var job = JobObject.CreateKillOnClose();

        // TaskCreationOptions.LongRunning: gerçek eşzamanlı OS thread'leri garanti eder (ThreadPool
        // enjeksiyon gecikmesi launch pencerelerinin çakışma olasılığını düşürmesin diye).
        var pauseTask = Task.Factory.StartNew(
            () => JobProcessLauncher.Launch(job, WindowsCommandLine.Build(CmdExe, "/c", "pause"),
                new LaunchOptions(RedirectStdio: true)),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var echoTasks = Enumerable.Range(0, 5).Select(i => Task.Factory.StartNew(
            () => (Index: i, Child: JobProcessLauncher.Launch(job,
                WindowsCommandLine.Build(CmdExe, "/c", $"echo child{i}"), new LaunchOptions(RedirectStdio: true))),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        // StartNew çağrıları zaten eşzamanlı başladı; await sırası yalnız sonuç toplamayı etkiler,
        // launch pencerelerinin çakışmasını değil.
        using var pause = await pauseTask; // stdin redirected → biz kapatana dek yaşar (bkz. üstteki test)
        var echoes = await Task.WhenAll(echoTasks);

        try
        {
            foreach (var (i, c) in echoes)
            {
                using var reader = new StreamReader(c.StandardOutput!);
                string text = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains($"child{i}", text);
            }
        }
        finally { foreach (var (_, c) in echoes) c.Dispose(); }
        // pause: using var → metot sonunda dispose edilir (stdin EOF alır, süreç çıkar); job.Dispose()
        // KILL_ON_JOB_CLOSE ile her ihtimalde (pass/fail fark etmez) kalıntı süreç bırakmaz.
    }
}
