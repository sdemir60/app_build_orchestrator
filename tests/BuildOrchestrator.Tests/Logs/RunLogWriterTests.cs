using System.Globalization;
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

    [Fact] // Bulgu 1: Dispose sonrası AppendLine sessizce satır düşürmek yerine fail-fast eder — çağıran "yazıldı" ile "düşürüldü"yü ayırt edebilsin
    public void AppendLine_after_dispose_throws()
    {
        using var w = new RunLogWriter(_root, Ts);
        var log = w.OpenProjectLog(@"C:\repo\A\A.csproj");
        log.AppendLine("first");
        log.Dispose();
        Assert.Throws<ObjectDisposedException>(() => log.AppendLine("second"));
    }

    [Fact] // Bulgu 2: gömülü CR/LF tek fiziksel satıra indirgenir — tüketici modeli '\n' ile böler (LogChunker), bir AppendLine == bir fiziksel satır
    public void AppendLine_with_embedded_newline_produces_one_physical_line()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using (var log = w.OpenProjectLog(id))
        {
            Assert.Equal(1, log.AppendLine("a\nb"));
        } // dosya kapansın ki File.ReadAllLines paylaşım çakışması olmadan okuyabilsin (mevcut testteki desenle aynı)

        Assert.Equal(["a b"], File.ReadAllLines(w.ProjectLogPath(id)));

        var snap = w.SnapshotProjectLog(id);
        Assert.NotNull(snap);
        Assert.Equal(1, snap!.Value.ThroughLineNumber);
        Assert.Equal(RunLogWriter.CountLines(snap.Value.Text), snap.Value.ThroughLineNumber);
    }

    [Fact] // Bulgu 3: run bitti — orijinal RunLogWriter Dispose edildi; aynı run dizini üzerinde YENİ bir RunLogWriter açılıp geçmiş projenin logu diskten okunur
    public void SnapshotProjectLog_reads_from_disk_via_a_fresh_writer_over_the_same_run_directory()
    {
        string id = @"C:\repo\A\A.csproj";
        var w1 = new RunLogWriter(_root, Ts);
        var log = w1.OpenProjectLog(id);
        log.AppendLine("first");
        log.AppendLine("second");
        var liveSnap = w1.SnapshotProjectLog(id);
        Assert.NotNull(liveSnap);
        var expected = liveSnap!.Value;
        w1.Dispose(); // run bitti: proje logu + decision.log kapandı

        using var w2 = new RunLogWriter(_root, Ts); // aynı logsRoot + aynı startedAt => aynı run dizini
        var diskSnap = w2.SnapshotProjectLog(id);
        Assert.NotNull(diskSnap);
        Assert.Equal(expected.Text, diskSnap!.Value.Text);
        Assert.Equal(expected.ThroughLineNumber, diskSnap.Value.ThroughLineNumber);
    }

    [Fact] // Bulgu 4: Snapshot canlı yazıcıyla ATOMİK — her snapshot'ta CountLines(Text) == ThroughLineNumber invariant'ı korunmalı (sleep-poll YOK, [D8])
    public async Task Snapshot_is_atomic_against_a_concurrently_appending_writer()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using var log = w.OpenProjectLog(id);
        const int iterations = 500;

        var writerTask = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
                log.AppendLine("line-" + i.ToString(CultureInfo.InvariantCulture));
        });

        var readerTask = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                var snap = w.SnapshotProjectLog(id);
                Assert.NotNull(snap);
                Assert.Equal(RunLogWriter.CountLines(snap!.Value.Text), snap.Value.ThroughLineNumber);
            }
        });

        await Task.WhenAll(writerTask, readerTask);
    }

    [Fact] // Bulgu 6: RunLogWriter Dispose sonrası OpenProjectLog/AppendDecision tutarsız (biri leak, biri incidental exception) yerine fail-fast eder
    public void OpenProjectLog_and_AppendDecision_after_RunLogWriter_dispose_throw()
    {
        var w = new RunLogWriter(_root, Ts);
        w.Dispose();
        Assert.Throws<ObjectDisposedException>(() => w.OpenProjectLog(@"C:\repo\A\A.csproj"));
        Assert.Throws<ObjectDisposedException>(() => w.AppendDecision("x"));
    }
}
