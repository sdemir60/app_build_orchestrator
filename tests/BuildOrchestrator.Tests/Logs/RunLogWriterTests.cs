using System.IO;
using BuildOrchestrator.Core.Logs;
using Xunit;

namespace BuildOrchestrator.Tests.Logs;

public class RunLogWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bo-runlog-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static readonly DateTimeOffset Ts = new(2026, 7, 17, 14, 5, 9, 250, TimeSpan.Zero);

    [Fact] // [D4] per-run dizin; InvariantCulture, deterministik ad
    public void Run_directory_name_is_timestamped_and_created()
    {
        Assert.Equal("run-20260717-140509-250", RunLogPaths.RunDirName(Ts));
        using var w = new RunLogWriter(_root, Ts);
        Assert.Equal(Path.Combine(_root, "run-20260717-140509-250"), w.RunDirectory);
        Assert.True(Directory.Exists(w.RunDirectory));
    }

    [Fact] // [D4] bellek ring buffer YOK: satırlar diske gider, numaralar 1'den monotonik
    public void Project_log_lines_go_to_disk_and_are_numbered()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using (var log = w.OpenProjectLog(id))
        {
            Assert.Equal(1, log.AppendLine("first"));
            Assert.Equal(2, log.AppendLine("second"));
        }
        string path = w.ProjectLogPath(id);
        Assert.Equal(Path.Combine(w.RunDirectory, ProjectLogNaming.FileNameFor(id)), path);
        Assert.Equal(["first", "second"], File.ReadAllLines(path));
    }

    [Fact] // Task 10 dikişi: snapshot metin + o ana kadar yazılmış satır sayısını ATOMİK verir
    public void Snapshot_returns_text_and_through_line_number()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using var log = w.OpenProjectLog(id);
        log.AppendLine("a");
        log.AppendLine("b");
        var snap = w.SnapshotProjectLog(id);
        Assert.NotNull(snap);
        Assert.Equal("a\r\nb\r\n".Replace("\r\n", Environment.NewLine), snap!.Value.Text);
        Assert.Equal(2, snap.Value.ThroughLineNumber);
        log.AppendLine("c"); // snapshot sonrası satır snapshot'ı etkilemez
        Assert.Equal(2, snap.Value.ThroughLineNumber);
    }

    [Fact]
    public void Snapshot_of_unknown_project_is_null()
    {
        using var w = new RunLogWriter(_root, Ts);
        Assert.Null(w.SnapshotProjectLog(@"C:\repo\Nope\Nope.csproj"));
    }

    [Fact] // decision log çok worker'dan yazılır: satır kaybı/karışması YOK
    public void Decision_log_is_thread_safe()
    {
        using var w = new RunLogWriter(_root, Ts);
        Parallel.For(0, 200, i => w.AppendDecision($"decision-{i:D3}"));
        w.Dispose();
        var lines = File.ReadAllLines(Path.Combine(w.RunDirectory, "decision.log"));
        Assert.Equal(200, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3} decision-\d{3}$", l));
    }
}
