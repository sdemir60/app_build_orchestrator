using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/A13.2] ConsoleBatcher: IPC arka plan thread'i <c>Post</c> ile satır yazar (kilitsiz, bloklamaz);
/// <c>PumpAsync</c> enjekte edilmiş <c>tick</c>'i bekler, kanalı boşaltır ve VARSA tek bir <c>flush</c>
/// çağırır. Join şekli: her <c>Post</c>'lanan satır tek bir konsol satırıdır — batch, satır başına '\n'
/// EKLENEREK (ayraç değil, sonek olarak) birleştirilir; böylece boş bir batch asla flush edilmez ve
/// editöre eklenen metin her zaman tam satırlarla biter.
///
/// Determinizm [D8]: gerçek 50ms beklenmez — <c>tick</c> testte tamamen kontrol edilir. Burada kullanılan
/// teknik: <c>tick</c> delegate'i her çağrıldığında SENKRON bir yan etki (Post/Complete) yapıp
/// <see cref="Task.CompletedTask"/> döner; tamamlanmış bir Task'ı <c>await</c> etmek askıya almadığı için
/// PumpAsync döngüsü sleep/poll OLMADAN, tek thread üzerinde tamamen sıralı ve deterministik ilerler —
/// her `tick` çağrısı testin istediği TAM noktada gerçekleşir.
/// </summary>
public class ConsoleBatcherTests
{
    [Fact]
    public async Task Three_posts_then_one_tick_produce_exactly_one_flush()
    {
        int callCount = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 2) batcher!.Complete(); // 1. tick veriyi flush eder; 2. tick pump'ı temiz kapatır
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);
        batcher.Post("a");
        batcher.Post("b");
        batcher.Post("c");

        var flushes = new List<string>();
        await batcher.PumpAsync(text => flushes.Add(text), CancellationToken.None);

        Assert.Equal(["a\nb\nc\n"], flushes);
    }

    [Fact]
    public async Task No_post_between_ticks_means_no_flush_that_cycle()
    {
        int callCount = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 2) batcher!.Post("x"); // 1. tick boş kanal görür -> flush YOK
            else if (callCount == 3) batcher!.Complete();
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        var flushes = new List<string>();
        await batcher.PumpAsync(text => flushes.Add(text), CancellationToken.None);

        // Tam olarak TEK flush bekleniyor (1. tick'te DEĞİL) — "x" postlandığı 2. tick'te.
        Assert.Equal(["x\n"], flushes);
    }

    [Fact]
    public async Task Complete_finishes_pump_after_draining_remainder()
    {
        int callCount = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 1) { batcher!.Post("x"); batcher!.Complete(); }
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        var flushes = new List<string>();
        var pump = batcher.PumpAsync(text => flushes.Add(text), CancellationToken.None);

        // Tick tamamen senkron ilerlediği için pump burada zaten bitmiş olmalı; WaitAsync yalnız
        // beklenmeyen bir asılı-kalma karşısında testin sonsuza dek takılmasını önleyen üst sınırdır
        // (gerçek bir 50ms/sleep bekleyişi DEĞİL — D8).
        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(pump.IsCompletedSuccessfully);
        Assert.Equal(["x\n"], flushes);
    }

    [Fact]
    public async Task Ten_thousand_lines_coalesce_into_far_fewer_flushes()
    {
        const int total = 10_000;
        int callCount = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 1) { for (int i = 0; i < total / 2; i++) batcher!.Post($"line{i}"); }
            else if (callCount == 2) { for (int i = total / 2; i < total; i++) batcher!.Post($"line{i}"); }
            else if (callCount == 3) { batcher!.Complete(); }
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        var flushes = new List<string>();
        await batcher.PumpAsync(text => flushes.Add(text), CancellationToken.None);

        // Batching kanıtı: binlerce satır, avuç içi kadar tick'e sığmış olmalı.
        Assert.True(flushes.Count <= 3, $"flush sayısı ({flushes.Count}) satır sayısına ({total}) yakın olmamalı.");
        int totalLines = flushes.Sum(f => f.Count(c => c == '\n'));
        Assert.Equal(total, totalLines);
    }

    [Fact]
    public void Post_never_blocks_even_without_a_reader()
    {
        var batcher = new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite));
        for (int i = 0; i < 1000; i++) batcher.Post($"line{i}"); // TryWrite üzerinde: hiçbir okuyucu koşmasa bile senkron döner
    }

    // ---------------------------------------------------------------- [3b] reseed tek okuyucudan geçer (dup residual kapanır)

    [Fact]
    public async Task Reseed_through_the_single_reader_does_not_duplicate_a_half_dequeued_line()
    {
        // "Yarım-dequeue" residual senaryosu: mod değişiminde bir satır (b) pump tarafından çekilmiş ama henüz
        // flush edilmemişken, taze doküman kurulur. Eski DiscardPending yolu bunu kaçırıp b'yi HEM snapshot'ta
        // HEM in-flight flush'ta bırakabiliyordu (kopya). Reseed artık AYNI tek-okuyucu FIFO'dan (PostReseed
        // sentinel'i) geçtiğinden: sentinel'den önceki satırlar (snapshot'ta zaten var) ATILIR, sonrakiler yeni
        // dokümana akar — her satır TAM BİR kez.
        var doc = new System.Text.StringBuilder();
        ConsoleBatcher? batcher = null;
        int callCount = 0;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 1)
            {
                batcher!.Post("a");
                batcher!.Post("b");
                // reseed snapshot'ı a+b'yi İÇERİR (VM'in _gate altında okuduğu tampon gibi); apply dokümanı KURAR.
                batcher!.PostReseed("a\nb\n", snap => { doc.Clear(); doc.Append(snap); });
                batcher!.Post("c"); // reseed'den SONRAki (yeni mod) satır — yeni dokümana akmalı
            }
            else if (callCount == 2) batcher!.Complete();
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        await batcher.PumpAsync(text => doc.Append(text), CancellationToken.None);

        Assert.Equal("a\nb\nc\n", doc.ToString()); // a,b (snapshot) + c (append) — hiçbir satır iki kez değil
    }

    [Fact]
    public async Task Reseed_discards_pending_lines_captured_in_the_snapshot()
    {
        // Sentinel'den ÖNCE post edilen tüm satırlar snapshot'ta olduğu VARSAYILIR → yeni dokümana TEKRAR
        // eklenmemeli (yalnız apply'ın kurduğu snapshot kalır).
        var doc = new System.Text.StringBuilder();
        ConsoleBatcher? batcher = null;
        int callCount = 0;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 1)
            {
                batcher!.Post("old1");
                batcher!.Post("old2");
                batcher!.PostReseed("SNAPSHOT\n", snap => { doc.Clear(); doc.Append(snap); });
            }
            else if (callCount == 2) batcher!.Complete();
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        await batcher.PumpAsync(text => doc.Append(text), CancellationToken.None);

        Assert.Equal("SNAPSHOT\n", doc.ToString()); // old1/old2 flush EDİLMEDİ (snapshot'ta zaten var)
    }

    [Fact]
    public async Task Drop_only_reseed_discards_prior_lines_without_setting_the_document()
    {
        // [D4/Solution B] Doküman TIKLAMA ANINDA senkron kurulur (burada test dışında); pump'a düşen drop-only
        // sentinel yalnız sentinel'den ÖNCEki (snapshot'a zaten dahil) satırları ATAR — doküman-set YAPMAZ.
        // Sonrasındaki satırlar (senkron kurulan dokümana) akmaya devam eder.
        var appended = new System.Text.StringBuilder();
        ConsoleBatcher? batcher = null;
        int callCount = 0;
        Task Tick(CancellationToken ct)
        {
            callCount++;
            if (callCount == 1)
            {
                batcher!.Post("stale1");
                batcher!.Post("stale2");
                batcher!.PostReseedDrop();  // doküman senkron kuruldu; bunlar atılmalı
                batcher!.Post("fresh1");    // reseed'den SONRA — akmaya devam
            }
            else if (callCount == 2) batcher!.Complete();
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);

        await batcher.PumpAsync(text => appended.Append(text), CancellationToken.None);

        Assert.Equal("fresh1\n", appended.ToString()); // stale1/stale2 atıldı; doküman-set çağrısı YOK
    }
}
