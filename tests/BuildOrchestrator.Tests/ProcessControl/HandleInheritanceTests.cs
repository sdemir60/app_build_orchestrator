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

    [Fact] // Paralel launch: her child'ın stdout'u KENDİ EOF'una ulaşmalı (çapraz sızıntı = ReadToEnd askıda kalır)
    public async Task Parallel_redirected_launches_each_reach_eof_on_their_own_stdout()
    {
        using var job = JobObject.CreateKillOnClose();
        var children = new List<JobChildProcess>();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() =>
        {
            var c = JobProcessLauncher.Launch(job,
                WindowsCommandLine.Build(CmdExe, "/c", $"echo child{i}"), new LaunchOptions(RedirectStdio: true));
            lock (children) children.Add(c);
        })));
        try
        {
            foreach (var c in children)
            {
                using var reader = new StreamReader(c.StandardOutput!);
                string text = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains("child", text);
            }
        }
        finally { foreach (var c in children) c.Dispose(); }
    }
}
