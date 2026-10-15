using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17] "Bu yol uyuyan motoru uyandırır mı" tablosu (<see cref="EngineWakeRule"/>) — her
/// komut türü tek tek ve tablonun eksiksizliği. Tablonun kullanıldığı yer (her gönderimin kapıdan geçtiği, uyanmanın sessiz olduğu, yarışlar,
/// düşen uyanma) <c>EngineWakeTests</c>'tedir.
/// </summary>
public class EngineWakeRuleTests
{
    [Theory]
    [InlineData(typeof(StartRunCommand), true)]            // Build, Rebuild, Resolve cycles, Build menüsünün Clean'i, satır komutları, tepsi kısayolu
    [InlineData(typeof(SyncWorkspaceCommand), true)]       // Sync her kipte: düğme, pencereye dönüş, commit sonrası, branch değişimi, Debug/Release, Save
    [InlineData(typeof(ListBranchesCommand), true)]        // branch envanteri
    [InlineData(typeof(CleanWorkspaceCommand), true)]      // bakım Clean'i
    [InlineData(typeof(OptimizeWorkspaceCommand), true)]   // Optimize
    [InlineData(typeof(CheckoutBranchCommand), true)]      // branch chip'inden checkout
    [InlineData(typeof(PullRepositoryCommand), true)]      // N behind chip'inden pull
    [InlineData(typeof(GetProjectLogCommand), true)]       // proje logu
    [InlineData(typeof(StopRunCommand), false)]            // uyuyan motorda koşu yok; gönderilmemiş koşuyu bekleyen-koşu penceresi geri alır
    [InlineData(typeof(SetPerfModeCommand), false)]        // koşu yokken motor yok sayar; uyanan motora giden koşu yeni profili taşır
    [InlineData(typeof(PingCommand), false)]               // [Fix round 1 · Mi3] App göndermez; sessizlik bekçisi ping atmaz
    [InlineData(typeof(ShutdownCommand), false)]           // [Mi3] konak kendisi yazar (uyku, Restart, çıkış) — VM göndermez
    [InlineData(typeof(DebugSpawnChildrenCommand), false)] // [Mi3] yalnız testlerin motor komutu
    public void The_table(Type command, bool wakes) => Assert.Equal(wakes, EngineWakeRule.Wakes(command));

    /// <summary>[Fix round 1 · Mi3] Tablo eksiksizdir: sözleşmedeki HER komut türünün kendi satırı vardır (uyandırır ya da uyandırmaz — açıkça) ve
    /// tabloda sözleşmede olmayan bir tür yoktur. Yeni bir komut türü eklendiğinde bu test, onu uyuyan motorun karşısında nasıl davranacağına
    /// karar verilmeden geçirmez.
    /// <para>KIRMIZI (e843d516 tablosu): <c>PingCommand</c>, <c>ShutdownCommand</c> ve <c>DebugSpawnChildrenCommand</c>'ın satırı yoktu — kural
    /// onları sessizce "uyandırır" sayıyordu.</para></summary>
    [Fact]
    public void Every_command_type_has_an_explicit_row()
    {
        var commands = typeof(IpcCommand).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IpcCommand).IsAssignableFrom(t))
            .ToHashSet();

        Assert.All(commands, t => Assert.True(EngineWakeRule.Table.ContainsKey(t), $"{t.Name}: uyandırma tablosunda satırı yok"));
        Assert.All(EngineWakeRule.Table.Keys, t => Assert.Contains(t, commands));
    }
}
