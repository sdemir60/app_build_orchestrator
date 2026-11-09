using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [indexing ilerlemesi] İlk indekslemenin raporlama POLİTİKASI: ne zaman konsola satır yazılır, ilerleme ne zaman
/// bildirilir. Saftır — dosya, saat, thread yok; gözlemci çağrıları ve geçen süre testten verilir.
/// </summary>
public sealed class IndexingReportTests
{
    private readonly List<string> _lines = [];
    private readonly List<(int Done, int Total)> _progress = [];

    private IndexingReport NewReport() => new(_lines.Add, (done, total) => _progress.Add((done, total)));

    [Fact]
    public void A_small_index_reports_nothing()
    {
        // Her koşuda değişen birkaç dosya için satır yazmak gürültü olurdu: eşiğin altı tümüyle sessizdir.
        int total = SourceHashCache.NoisyPrefillThreshold - 1;
        var report = NewReport();

        report.Started(total);
        report.Progress(10, total, 20_000);
        report.Finished(total, 30_000);

        Assert.Empty(_lines);
        Assert.Empty(_progress);
        Assert.False(report.Reported);
    }

    [Fact]
    public void A_noisy_index_announces_the_existing_line_and_starts_the_count_at_zero()
    {
        var report = NewReport();

        report.Started(600);

        Assert.Equal([PlanProgressLines.IndexingSources(600)], _lines);
        Assert.Equal([(0, 600)], _progress);
        Assert.True(report.Reported);
    }

    [Fact]
    public void A_heartbeat_line_follows_every_ten_seconds_with_the_count()
    {
        var report = NewReport();
        report.Started(600);
        _lines.Clear();

        report.Progress(100, 600, 9_999);                       // ilk sınır 10 sn: henüz değil
        Assert.Empty(_lines);

        report.Progress(200, 600, 10_000);                      // sınır: tek satır, metin sayı + süreyi taşır
        Assert.Equal(["indexing source files — 200/600 · 10s"], _lines);

        report.Progress(300, 600, 19_000);                      // sonraki sınır 20 sn
        Assert.Single(_lines);

        report.Progress(400, 600, 35_000);                      // birkaç sınır atlandı: TEK satır, sınır elapsed'in üstündeki ilk 10 sn katına
        Assert.Equal(2, _lines.Count);
        Assert.Equal(PlanProgressLines.IndexingHeartbeat(400, 600, 35_000), _lines[^1]);

        report.Progress(500, 600, 39_999);                      // yeni sınır 40 sn
        Assert.Equal(2, _lines.Count);

        report.Progress(550, 600, 40_000);
        Assert.Equal(3, _lines.Count);
        Assert.Equal(PlanProgressLines.IndexingHeartbeat(550, 600, 40_000), _lines[^1]);
    }

    [Fact]
    public void Progress_is_reported_only_when_the_count_moves()
    {
        var report = NewReport();
        report.Started(600);
        _progress.Clear();

        report.Progress(0, 600, 500);                           // sayı yerinde: bildirim yok
        Assert.Empty(_progress);

        report.Progress(5, 600, 1_000);
        Assert.Equal([(5, 600)], _progress);

        report.Progress(5, 600, 1_500);                         // aynı sayı yine bildirilmez
        Assert.Equal([(5, 600)], _progress);
    }

    [Fact]
    public void The_closing_line_names_the_total_and_the_duration()
    {
        var report = NewReport();
        report.Started(600);
        report.Progress(300, 600, 25_000);
        _lines.Clear();
        _progress.Clear();

        report.Finished(600, 51_400);

        Assert.Equal(["Indexed 600 source files in 51s"], _lines);
        Assert.Equal([(600, 600)], _progress);                  // sayaç sona kadar tamamlanır (son tik kaçırmış olsa bile)
    }

    [Fact]
    public void The_closing_does_not_repeat_a_count_that_was_already_reported_as_complete()
    {
        var report = NewReport();
        report.Started(600);
        report.Progress(600, 600, 20_000);
        _progress.Clear();

        report.Finished(600, 20_400);

        Assert.Empty(_progress);
    }
}
