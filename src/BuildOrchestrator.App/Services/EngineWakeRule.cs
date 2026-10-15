using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17/30/33 · C4/C13] <b>"Bu yol uyuyan motoru uyandırır mı" — TEK tablo.</b> Soru komut
/// türünden okunur: VM'in motora giden her gönderimi tek kapıdan geçer (<c>RunViewModel.EnsureEngineAwakeAsync</c> — gönderimin tek yolu
/// <c>TrySendAsync</c>, proje log isteği ve koşunun bekleyen-koşu penceresindeki erken uyanması) ve kapı bu soruyu yalnız buraya sorar.
/// WPF'siz ve saf — <see cref="EngineIdleRule"/> ile aynı yerleşim.
/// <list type="bullet">
/// <item><b>Uyandırır</b> (motor uyuyorsa önce uyanır, uyku sürüyorsa bitmesi beklenir, uyanma sürüyorsa ona katılınır):
/// koşu (<see cref="StartRunCommand"/> — Build, Rebuild, Resolve cycles, Build menüsünün Clean'i, satır komutları, tepsi Build kısayolu);
/// Sync, her kipte (<see cref="SyncWorkspaceCommand"/> — Sync düğmesi, pencereye dönüş, commit sonrası, dışarıdan branch değişimi,
/// Debug/Release, Settings Save'in Sync'i, checkout/pull/Clean/Optimize'a zincirlenen Sync); branch envanteri
/// (<see cref="ListBranchesCommand"/>); bakım Clean'i (<see cref="CleanWorkspaceCommand"/>); Optimize
/// (<see cref="OptimizeWorkspaceCommand"/>); checkout (<see cref="CheckoutBranchCommand"/>); pull (<see cref="PullRepositoryCommand"/>);
/// proje logu (<see cref="GetProjectLogCommand"/>).</item>
/// <item><b>Asla uyandırmaz</b> ve motor uyanık değilken (uyku sürerken, uyurken, uyanırken) hiç gönderilmez: Stop
/// (<see cref="StopRunCommand"/> — Stop, Stop now, branch kesmesi, çıkıştaki Stop) — uyuyan motorda koşu yoktur, henüz gönderilmemiş koşuyu
/// bekleyen-koşu penceresi komutsuz geri alır; perf chip (<see cref="SetPerfModeCommand"/>) — koşu yokken motor onu zaten yok sayar ve
/// uyanan motora giden koşu yeni profili taşır.</item>
/// </list>
/// <para>Tablonun dışında kalanlar komut göndermez, yani hiç uyandırmaz: motor sessizlik bekçisi ping atmaz (sessizliği sayar), boşta
/// kuralı yalnız uyutur, çıkıştaki kapanışı konak kendisi yazar (uyuyan konakta gönderecek bir şey yoktur), çalışan bir şey yokken Stop/Esc
/// hiçbir şey göndermez. Açılış ve "Restart engine" motoru zaten başlatır; "Restart engine" uyku için gösterilmez. Settings Save'in kendi
/// uyandırması yoktur: "Stop the engine when idle"ı kapatmak uyuyan motoru uyandırmaz — Save'in uyandırması, gönderiyorsa, Sync'idir.</para>
/// </summary>
public static class EngineWakeRule
{
    /// <summary>Bu türdeki bir komut uyuyan motoru uyandırır mı (özetteki tablo).</summary>
    /// <param name="command">Gönderilecek komutun türü (<see cref="IpcCommand"/>'ten türeyen).</param>
    public static bool Wakes(Type command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command != typeof(StopRunCommand) && command != typeof(SetPerfModeCommand);
    }
}
