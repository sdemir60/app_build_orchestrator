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

    /// <summary>[idle-memory · dalga 3 · B1 fix 3] Okunamayan bir proje logu (başka bir süreç dosyayı paylaşımsız tutuyor) FIRLATIR: "okunamadı"
    /// "yok" değildir. Okumanın hatasını "log yok"a çeviren ve tanı satırını yazan TEK yer koordinatördür (<c>RunCoordinator.TryGetProjectLogSnapshot</c>);
    /// Core yalnız yokluğu söyler (<see cref="An_absent_project_log_reads_as_no_log"/>).
    /// <para><b>[DEĞİŞEN KURAL — B1 fix 3]</b> Eski iddia (<c>A_project_log_that_cannot_be_read_reads_as_no_log</c>, B1 fix 1 · m-5): paylaşım ihlali
    /// "log yok" (<c>null</c>) okunur, istisna yükselmez. Değişme gerekçesi (kontrolcü kararı): Core ilkeli "yok" ile "okunamadı"yı tek <c>null</c>'a
    /// karıştırıyordu ve "okunamadı → log yok" kuralı üç katmana yayılmıştı (Core, host, koordinatör); kural koordinatörde tek yere toplandı ve
    /// okunamayan log artık iz bırakır (tanı satırı).</para>
    /// <para>KIRMIZI (f611591c): istisna yükselmiyordu — Core paylaşım ihlalini sessizce <c>null</c>'a çeviriyordu.</para></summary>
    [Fact]
    public void A_project_log_that_cannot_be_read_throws()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using (var log = w.OpenProjectLog(id)) log.AppendLine("first");
        using var held = new FileStream(w.ProjectLogPath(id), FileMode.Open, FileAccess.Read, FileShare.None); // başka bir sürecin kilidi

        Assert.Throws<IOException>(() => RunLogWriter.ReadProjectLogFromDisk(w.RunDirectory, id));
    }

    /// <summary>[B1 fix 3] YOK olan log <c>null</c>'dır, istisna değil: koşu klasöründe dosya yok ya da klasörün kendisi yok — uyanan motorun açılıştaki
    /// saklama süpürmesi klasörü okunurken siler (beklenen yarış). Yokluğu okumanın kendisi söyler (<see cref="FileNotFoundException"/>,
    /// <see cref="DirectoryNotFoundException"/>): önceden <c>File.Exists</c> sorulmaz, "hiç yoktu" ile "okunurken gitti" tek daldır ve bu test o dalı sınar.
    /// <para>PİN: bugünkü kod da <c>null</c> döner (ön kontrolle). Mutasyon: yokluğun yakalaması kaldırılınca kırmızı.</para></summary>
    [Theory]
    [InlineData(false)] // klasör var, dosya yok
    [InlineData(true)]  // klasörün kendisi yok
    public void An_absent_project_log_reads_as_no_log(bool folderGone)
    {
        string runDirectory = Path.Combine(_root, RunLogPaths.RunDirName(Ts));
        if (!folderGone) Directory.CreateDirectory(runDirectory);

        Assert.Null(RunLogWriter.ReadProjectLogFromDisk(runDirectory, @"C:\repo\A\A.csproj"));
    }

    /// <summary>[B1 fix 4 · PİN] Erişimi reddedilen log da FIRLATIR — "yok" değildir: logun yerinde, onun adını taşıyan bir klasör var (okuma
    /// <see cref="UnauthorizedAccessException"/> atar). Okumanın hatasını "log yok"a çeviren tek yer koordinatördür.
    /// <para>PİN: bugünkü kod fırlatır. Mutasyon (Core'un yokluk süzgecine <c>or UnauthorizedAccessException</c> eklemek — erişim reddi yine sessizce
    /// <c>null</c> olur) kırmızı.</para></summary>
    [Fact]
    public void A_project_log_whose_access_is_denied_throws()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        Directory.CreateDirectory(w.ProjectLogPath(id)); // logun yerinde, adını taşıyan bir klasör

        Assert.Throws<UnauthorizedAccessException>(() => RunLogWriter.ReadProjectLogFromDisk(w.RunDirectory, id));
    }

    /// <summary>[B1 fix 4 · PİN] Canlı dalın sözleşmesi (<see cref="RunLogWriter.SnapshotProjectLog"/>): kayıtlı bir projenin dosyası diskten gittiyse
    /// (yazıcısı kapandıktan sonra silindi) anlık görüntü <see cref="FileNotFoundException"/> fırlatır — disk dalının yokluk <c>null</c>'ı canlı dala
    /// uzanmaz; "log yok"a çeviren tek yer koordinatördür.
    /// <para>PİN: bugünkü kod fırlatır. Mutasyon (canlı dalda yokluğu yutmak) kırmızı.</para></summary>
    [Fact]
    public void A_registered_project_log_that_vanished_throws()
    {
        using var w = new RunLogWriter(_root, Ts);
        string id = @"C:\repo\A\A.csproj";
        using (var log = w.OpenProjectLog(id)) log.AppendLine("first");
        File.Delete(w.ProjectLogPath(id)); // kayıt durur, dosya gitti

        Assert.Throws<FileNotFoundException>(() => w.SnapshotProjectLog(id));
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
