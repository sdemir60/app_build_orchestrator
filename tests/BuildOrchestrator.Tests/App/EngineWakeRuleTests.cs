using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17] "Bu yol uyuyan motoru uyandırır mı" tablosu (<see cref="EngineWakeRule"/>) — VM'in
/// motora gönderdiği her komut türü tek tek. Tablonun kullanıldığı yer (her gönderimin kapıdan geçtiği, uyanmanın sessiz olduğu, yarışlar,
/// düşen uyanma) <c>EngineWakeTests</c>'tedir.
/// </summary>
public class EngineWakeRuleTests
{
    [Theory]
    [InlineData(typeof(StartRunCommand), true)]          // Build, Rebuild, Resolve cycles, Build menüsünün Clean'i, satır komutları, tepsi kısayolu
    [InlineData(typeof(SyncWorkspaceCommand), true)]     // Sync her kipte: düğme, pencereye dönüş, commit sonrası, branch değişimi, Debug/Release, Save
    [InlineData(typeof(ListBranchesCommand), true)]      // branch envanteri
    [InlineData(typeof(CleanWorkspaceCommand), true)]    // bakım Clean'i
    [InlineData(typeof(OptimizeWorkspaceCommand), true)] // Optimize
    [InlineData(typeof(CheckoutBranchCommand), true)]    // branch chip'inden checkout
    [InlineData(typeof(PullRepositoryCommand), true)]    // N behind chip'inden pull
    [InlineData(typeof(GetProjectLogCommand), true)]     // proje logu
    [InlineData(typeof(StopRunCommand), false)]          // uyuyan motorda koşu yok; gönderilmemiş koşuyu bekleyen-koşu penceresi geri alır
    [InlineData(typeof(SetPerfModeCommand), false)]      // koşu yokken motor yok sayar; uyanan motora giden koşu yeni profili taşır
    public void The_table(Type command, bool wakes) => Assert.Equal(wakes, EngineWakeRule.Wakes(command));
}
