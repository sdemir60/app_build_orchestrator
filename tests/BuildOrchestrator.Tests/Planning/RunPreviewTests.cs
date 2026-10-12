using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// <see cref="RunPreview.WillBuild"/>: bir koşunun KENDİ önizlemesinde bir projenin <c>WillBuild</c>'i — BU koşunun işi.
/// Saf fonksiyon, I/O YOK. Koşu düzeyindeki kanıtları <c>RunCoordinatorTests</c> (Rebuild, düşen artımlı geçiş),
/// <c>CycleRoundsTests</c> (kökünü bekleyen karışık grup) ve <c>EngineRunReplayTests</c> (ekranın söyledikleri) taşır.
/// </summary>
public class RunPreviewTests
{
    private static ProjectNode Node(bool? willBuild) =>
        new("P", "P", "P", [], [], BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: willBuild);

    public static TheoryData<RunMode> AllModes => [RunMode.Build, RunMode.Rebuild, RunMode.Cycles, RunMode.Clean];

    /// <summary>Önden atlanan proje dispatch edilmez — hiçbir koşuda bu koşunun işi değildir.</summary>
    [Theory]
    [MemberData(nameof(AllModes))]
    public void A_project_the_run_skips_up_front_is_never_its_work(RunMode mode)
    {
        Assert.False(RunPreview.WillBuild(Node(true), mode, preSkipped: true));
        Assert.False(RunPreview.WillBuild(Node(null), mode, preSkipped: true));
    }

    /// <summary>Defteri yok sayan koşu (Rebuild derler, Clean temizler) önden atlamadığı her projeyi işler —
    /// planlayıcının cevabı ne olursa olsun.</summary>
    [Theory]
    [InlineData(RunMode.Rebuild)]
    [InlineData(RunMode.Clean)]
    public void A_run_that_ignores_the_ledger_works_on_every_project_it_does_not_skip(RunMode mode)
    {
        Assert.True(RunPreview.WillBuild(Node(false), mode, preSkipped: false));
        Assert.True(RunPreview.WillBuild(Node(null), mode, preSkipped: false));
        Assert.True(RunPreview.WillBuild(Node(true), mode, preSkipped: false));
    }

    /// <summary>Defteri dinleyen koşu (Build, Cycles) planlayıcının cevabını taşır; cevap yoksa (<c>null</c>: artımlı geçiş
    /// düştü) proje önden atlanmaz ve derlenir. Planlayıcının <c>false</c> dediği ama önden atlanmayan proje — kökünü
    /// bekleyen ya da kısmen güncel bir döngü grubunun güncel üyesi — <c>false</c> kalır: derlenip derlenmeyeceği grubun
    /// kaderine bağlıdır, önizleme kesin söz vermez.</summary>
    [Theory]
    [InlineData(RunMode.Build)]
    [InlineData(RunMode.Cycles)]
    public void A_run_that_follows_the_ledger_carries_the_planners_answer_and_counts_no_answer_as_work(RunMode mode)
    {
        Assert.True(RunPreview.WillBuild(Node(true), mode, preSkipped: false));
        Assert.False(RunPreview.WillBuild(Node(false), mode, preSkipped: false));
        Assert.True(RunPreview.WillBuild(Node(null), mode, preSkipped: false));
    }
}
