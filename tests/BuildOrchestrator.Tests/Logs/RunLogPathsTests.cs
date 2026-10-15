using BuildOrchestrator.Core.Logs;
using Xunit;

namespace BuildOrchestrator.Tests.Logs;

/// <summary>
/// [PERF Faz C/C2 · düzeltme 1 · M-1] <see cref="RunLogPaths.TryParseRunDirName"/> doğrudan sınanır: saklama budaması
/// neyin koşu klasörü olduğunu ve ne zaman başladığını buradan öğrenir (<see cref="RunLogRetentionTests"/> onu yalnız
/// dolaylı görürdü). Damga, yazıcıya verilen anın YEREL duvar saatidir (<c>RunCoordinator</c> <c>DateTimeOffset.Now</c>
/// verir); ters işlem onu yerel ofsetle çözer — UTC'yle değil.
/// </summary>
public sealed class RunLogPathsTests
{
    [Fact]
    public void A_run_folder_name_parses_back_to_the_local_wall_clock_instant_it_was_made_from()
    {
        var wall = new DateTime(2026, 7, 20, 12, 34, 56, 789, DateTimeKind.Unspecified);
        var started = new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall)); // RunCoordinator: DateTimeOffset.Now

        Assert.True(RunLogPaths.TryParseRunDirName(RunLogPaths.RunDirName(started), out var parsed));

        Assert.Equal(started, parsed);                                       // aynı AN
        Assert.Equal(wall, parsed.DateTime);                                 // duvar saati değişmez
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(wall), parsed.Offset);  // yerel ofset (UTC'ye çevrilmez)
    }

    [Fact]
    public void The_stamp_reads_year_month_day_hour_minute_second_millisecond()
    {
        Assert.True(RunLogPaths.TryParseRunDirName("run-20260720-123456-789", out var parsed));

        Assert.Equal(new DateTime(2026, 7, 20, 12, 34, 56, 789), parsed.DateTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("run-")]
    [InlineData("run-garbage")]
    [InlineData("run-20260720-123456")]             // milisaniyesiz
    [InlineData("run-20261340-000000-000")]         // geçersiz tarih (13. ay)
    [InlineData("run-20260720-123456-789-2")]       // sonuna ek
    [InlineData("run-20260720-123456-789.bak")]     // yedek kopya
    [InlineData("xrun-20260720-123456-789")]        // başına ek
    [InlineData(" run-20260720-123456-789")]        // başına boşluk
    [InlineData("RUN-20260720-123456-789")]         // büyük harf: ad kalıbı harf duyarlıdır
    public void A_name_outside_the_run_folder_pattern_does_not_parse(string name)
    {
        Assert.False(RunLogPaths.TryParseRunDirName(name, out var parsed));

        Assert.Equal(default(DateTimeOffset), parsed);
    }

    // Uç damgalarda yerel ofset DateTimeOffset aralığının dışına taşabilir (saat diliminin işaretine göre biri taşar, biri
    // taşmaz): taşan 'false' döner. FIRLATMAMALI — tek bir tuhaf klasör adı bütün süpürmeyi düşürürdü.
    [Theory]
    [InlineData("run-00010101-000000-000")]
    [InlineData("run-99991231-235959-999")]
    public void A_stamp_at_the_edge_of_the_calendar_never_throws(string name)
    {
        bool parsed = false;
        DateTimeOffset startedAt = default;

        var thrown = Record.Exception(() => parsed = RunLogPaths.TryParseRunDirName(name, out startedAt));

        Assert.Null(thrown);
        if (!parsed) Assert.Equal(default(DateTimeOffset), startedAt);
    }

    // ---------------------------------------------------------------- [idle-memory · dalga 3 · karar 37] isteğin adını verdiği klasör

    /// <summary>Kuralın sınandığı log kökü — saf kural diske bakmaz, kökün var olması gerekmez.</summary>
    private const string LogsRoot = @"C:\bo\logs";

    /// <summary>[karar 37] Kendi koşusu olmayan motor (uykudan uyanmış ya da yeniden başlatılmış) proje log isteğinin adını verdiği klasörü
    /// yalnız kendi log kökünün koşu klasörlerinden biriyse okur (<see cref="RunLogPaths.IsRunDirectoryOf"/>): tam nitelikli, kökün DOĞRUDAN
    /// altında ve adı koşu klasörü kalıbına tam uyuyor — yazıcının açtığı klasörün biçimi (<c>logsRoot\run-…</c>). Yol normalleştirilerek
    /// karşılaştırılır: sondaki ayırıcı, ileri eğik çizgi, kökün harf büyüklüğü ve kökün içinde kalan bir <c>..</c> sonucu değiştirmez.
    /// <para>KIRMIZI: kural yoktu (gövde <c>NotImplementedException</c>).</para></summary>
    [Theory]
    [InlineData(@"C:\bo\logs\run-20261015-101010-123")]
    [InlineData(@"C:\bo\logs\run-20261015-101010-123\")]       // sonda ayırıcı
    [InlineData(@"C:/bo/logs/run-20261015-101010-123")]        // ileri eğik çizgi
    [InlineData(@"c:\BO\LOGS\run-20261015-101010-123")]        // kök harf duyarsız (Windows yolu)
    [InlineData(@"C:\bo\logs\x\..\run-20261015-101010-123")]   // normalleşince kökün doğrudan altında
    public void A_run_folder_directly_under_the_logs_root_is_accepted(string path) =>
        Assert.True(RunLogPaths.IsRunDirectoryOf(LogsRoot, path));

    /// <summary>[karar 37] Başka her şey reddedilir — motor o zaman bugünkü "log yok" cevabını verir: klasörsüz istek, göreli ya da sürücüye
    /// göreli yol (sürecin çalışma klasörüne düşerdi), kökün kendisi, kalıba uymayan ad, kökün doğrudan altında olmayan (iç içe) koşu klasörü,
    /// başka bir kök, kökten dışarı çıkan yol ve adı kökle aynı başlayan komşu klasör. Okunamaz bir yol (gömülü null) FIRLATMAZ, reddedilir.
    /// <para>KIRMIZI: kural yoktu (gövde <c>NotImplementedException</c>).</para></summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("run-20261015-101010-123")]                    // göreli
    [InlineData(@"logs\run-20261015-101010-123")]              // göreli
    [InlineData(@"C:run-20261015-101010-123")]                 // sürücüye göreli: tam nitelikli değil
    [InlineData(@"C:\bo\logs")]                                // kökün kendisi
    [InlineData(@"C:\bo\logs\evil")]                           // ad kalıba uymuyor
    [InlineData(@"C:\bo\logs\sub\run-20261015-101010-123")]    // kökün doğrudan altında değil
    [InlineData(@"C:\other\run-20261015-101010-123")]          // başka kök
    [InlineData(@"C:\bo\logs\..\run-20261015-101010-123")]     // kökten dışarı çıkıyor (C:\bo\run-…)
    [InlineData(@"C:\bo\logs-old\run-20261015-101010-123")]    // adı kökle aynı başlayan komşu klasör
    [InlineData("C:\\bo\\logs\\run-20261015-101010-123\0")]    // gömülü null
    public void Any_other_folder_is_refused(string? path)
    {
        bool accepted = true;

        var thrown = Record.Exception(() => accepted = RunLogPaths.IsRunDirectoryOf(LogsRoot, path));

        Assert.Null(thrown);
        Assert.False(accepted);
    }

    /// <summary>[karar 37] Kökün kendisi de normalleştirilir: sonunda ayırıcı taşıyan bir kök (<c>--logs C:\bo\logs\</c>) altındaki koşu klasörünü
    /// yine kabul eder.
    /// <para>KIRMIZI: kural yoktu (gövde <c>NotImplementedException</c>).</para></summary>
    [Fact]
    public void A_logs_root_with_a_trailing_separator_still_accepts_its_run_folders() =>
        Assert.True(RunLogPaths.IsRunDirectoryOf(LogsRoot + @"\", @"C:\bo\logs\run-20261015-101010-123"));
}
