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
}
