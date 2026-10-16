using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17/30/33 · C4/C13] <b>"Bu komut uyuyan motoru uyandırır mı" — TEK tablo.</b> Soru komut
/// türünden okunur; motor kapısı (<c>RunViewModel.EnsureEngineAwakeAsync</c> — uyandıran ve uyandırmayan komutun uyku döngüsünde ne
/// olduğunun tek ifadesi orada) soruyu yalnız buraya sorar. WPF'siz ve saf — <see cref="EngineIdleRule"/> ile aynı yerleşim.
/// <list type="bullet">
/// <item><b>Uyandırır:</b> koşu (<see cref="StartRunCommand"/> — Build, Rebuild, Resolve cycles, Build menüsünün Clean'i, satır komutları, tepsi
/// Build kısayolu); Sync, her kipte (<see cref="SyncWorkspaceCommand"/> — Sync düğmesi, pencereye dönüş, commit sonrası, dışarıdan branch
/// değişimi, Debug/Release, Settings Save'in Sync'i, checkout/pull/Clean/Optimize'a zincirlenen Sync); branch envanteri
/// (<see cref="ListBranchesCommand"/>); bakım Clean'i (<see cref="CleanWorkspaceCommand"/>); Optimize (<see cref="OptimizeWorkspaceCommand"/>);
/// checkout (<see cref="CheckoutBranchCommand"/>); pull (<see cref="PullRepositoryCommand"/>); proje logu (<see cref="GetProjectLogCommand"/>).</item>
/// <item><b>Asla uyandırmaz:</b> Stop (<see cref="StopRunCommand"/> — Stop, Stop now, branch kesmesi, çıkıştaki Stop: uyuyan motorda koşu yoktur,
/// gönderilmemiş koşuyu bekleyen-koşu penceresi komutsuz geri alır); perf chip (<see cref="SetPerfModeCommand"/> — koşu yokken motor onu
/// zaten yok sayar, uyanan motora giden koşu yeni profili taşır); <see cref="PingCommand"/> (App göndermez — sessizlik bekçisi ping atmaz,
/// sessizliği sayar); <see cref="ShutdownCommand"/> (konak kendisi yazar: uyku, Restart, çıkış — uyuyan konakta gönderecek bir şey yoktur);
/// <see cref="DebugSpawnChildrenCommand"/> (yalnız testlerin motor komutu).</item>
/// </list>
/// <para>Tablo eksiksizdir: sözleşmedeki her komut türünün açık bir satırı vardır (<c>EngineWakeRuleTests</c> denetler). Tablonun dışında
/// kalanlar komut göndermez: boşta kuralı yalnız uyutur, çalışan bir şey yokken Stop/Esc hiçbir şey göndermez; açılış ve "Restart engine"
/// motoru zaten başlatır, "Restart engine" uyku için gösterilmez. Settings Save'in kendi uyandırması yoktur: "Stop the engine when idle"ı
/// kapatmak uyuyan motoru uyandırmaz — Save'in uyandırması, gönderiyorsa, Sync'idir.</para>
/// </summary>
public static class EngineWakeRule
{
    /// <summary>Tablonun kendisi: komut türü → uyuyan motoru uyandırır mı. Her satır özetteki gerekçeyle açıktır.</summary>
    public static IReadOnlyDictionary<Type, bool> Table { get; } = new Dictionary<Type, bool>
    {
        [typeof(StartRunCommand)] = true,
        [typeof(SyncWorkspaceCommand)] = true,
        [typeof(ListBranchesCommand)] = true,
        [typeof(CleanWorkspaceCommand)] = true,
        [typeof(OptimizeWorkspaceCommand)] = true,
        [typeof(CheckoutBranchCommand)] = true,
        [typeof(PullRepositoryCommand)] = true,
        [typeof(GetProjectLogCommand)] = true,
        [typeof(StopRunCommand)] = false,
        [typeof(SetPerfModeCommand)] = false,
        [typeof(PingCommand)] = false,
        [typeof(ShutdownCommand)] = false,
        [typeof(DebugSpawnChildrenCommand)] = false,
    };

    /// <summary>Bu türdeki bir komut uyuyan motoru uyandırır mı (<see cref="Table"/>). Satırı olmayan tür — eksiksizlik testi yeni bir türü
    /// satırsız geçirmez — uyandırır sayılır: komut bugünkü gibi bir motora ulaşır.</summary>
    /// <param name="command">Gönderilecek komutun türü (<see cref="IpcCommand"/>'ten türeyen).</param>
    public static bool Wakes(Type command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Table.GetValueOrDefault(command, true);
    }
}
