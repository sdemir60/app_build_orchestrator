using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.24.0 · plan K1/K4/K5] <see cref="RunViewModel"/>'in <b>keşif durumu</b>: bir Sync proje kümesini
/// keşfederken ekrandaki liste boşsa, graf ve proje listesi boş kalmaz — "ne oluyor"u söyleyen bir blok ve bulunan
/// proje sayısı gösterilir (kabuk: <c>ShellRoot</c>/<c>GraphView</c>). Burada yalnız DURUM vardır: ne zaman açılır,
/// ne zaman kapanır, sayaç ne der.
///
/// <para><b>Ne zaman açılır (K1):</b> Sync isteği penceresinin açıldığı AN (<see cref="SyncCoreAsync"/>, istek
/// bayrağıyla aynı yerde), yalnız ekranda bir işlem olarak görünen kiplerde (<see cref="SyncModeRules.IsVisible"/> —
/// sessiz Sync ekranda iz bırakmaz) ve ekrandaki liste o an boşsa: VM'de satır yoktur (açılış, Clean/Optimize devri,
/// kök değişimi) YA DA plan yüzeyi baştan başlıyordur (<see cref="PlanSurfaceRestarting"/> — Sync düğmesi, branch
/// değişimi, Debug|Release). Satırlar dururken gelen Appended Sync (pull, yalnız harici/katman Save) listeyi korur,
/// blok çıkmaz. Clean/Optimize'ın kendi penceresi (Sync'ten önce) keşif DEĞİLDİR (K2): orada Sync yoktur.</para>
///
/// <para><b>Ne zaman kapanır:</b> topoloji geldiğinde (<see cref="OnWorkspaceTopology"/>'nin başında — yüzey
/// yeniden kurulmadan ve reveal oynamadan ÖNCE; aksi hâlde reveal gizli bir yüzeye oynardı) ve Sync'in her bitiş
/// yolunda (<see cref="EndSyncMode"/> — gönderim düştü, <c>planFailed</c>, motor kaybı, topolojisiz tamamlanma;
/// hepsi oradan geçer, düşürme TEK yerdedir).</para>
///
/// <para><b>Bildirim:</b> durumun TEK yazıcısı bu dosyadaki üç metottur ve her biri kendi özelliklerini duyurur
/// (<see cref="SetPullBusy"/> deseni). Meşguliyet listesi (<see cref="NotifySyncGatedCommands"/>) bunu taşıyamaz:
/// topolojinin kapanışı o noktadan hiç geçmez ve reveal'den ÖNCE duyurulmak zorundadır.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Keşif bloğu görünür mü — proje kümesi bilinmiyor, Sync onu keşfediyor ve ekrandaki liste boş.</summary>
    public bool IsDiscovering { get; private set; }

    /// <summary>Keşfin o ana kadar bulduğu ana repo projeleri — motorun kümülatif raporundan (<see cref="SyncDiscoveryEvent"/>).</summary>
    public int DiscoveredRepositoryProjects { get; private set; }

    /// <summary>Keşfin o ana kadar çözdüğü harici projeler — motorun kümülatif raporundan.</summary>
    public int DiscoveredExternalProjects { get; private set; }

    /// <summary>Sayacın toplamı (<c>{n} found</c>) — telde taşınmaz, iki alandan türetilir (K4).</summary>
    public int DiscoveredProjects => DiscoveredRepositoryProjects + DiscoveredExternalProjects;

    /// <summary>[K5] Sayaç kırılımı (<c> · N repository · N external</c>) gösterilsin mi: Sync İSTEĞİ anında en az bir
    /// harici tanım vardı (o an çözülmüş olmasa da). İstek anının görüntüsüdür — Sync sürerken değişmez.</summary>
    public bool DiscoveryShowsBreakdown { get; private set; }

    /// <summary>Bu kipteki Sync isteği keşif bloğunu açar mı (K1) — kural TEK yerde.</summary>
    private bool OpensDiscovery(SyncMode mode) =>
        mode.IsVisible() && HasWorkspace && (Projects.Count == 0 || PlanSurfaceRestarting);

    /// <summary>Keşfi açar: sayaç sıfırdan başlar ("keşif başında 0 found"), kırılımın görüntüsü alınır. Çağıran
    /// <see cref="SyncCoreAsync"/>'tir — istek bayrağıyla aynı anda, yalnız <see cref="OpensDiscovery"/> doğruysa.</summary>
    private void BeginDiscovery()
    {
        DiscoveredRepositoryProjects = 0;
        DiscoveredExternalProjects = 0;
        DiscoveryShowsBreakdown = ExternalProjects.Count > 0;
        IsDiscovering = true;
        // Önce değerler, EN SON bayrak: bayrağın dinleyicisi (kabuk) sayacı da o anda okur.
        OnPropertyChanged(nameof(DiscoveredRepositoryProjects));
        OnPropertyChanged(nameof(DiscoveredExternalProjects));
        OnPropertyChanged(nameof(DiscoveredProjects));
        OnPropertyChanged(nameof(DiscoveryShowsBreakdown));
        OnPropertyChanged(nameof(IsDiscovering));
    }

    /// <summary>Keşfi kapatır — açık değilse no-op. Çağıranlar: <see cref="OnWorkspaceTopology"/> (reveal'den önce)
    /// ve <see cref="EndSyncMode"/> (Sync'in her bitiş yolu). Sayaç son değerinde kalır; bir sonraki keşif sıfırlar.</summary>
    private void EndDiscovery()
    {
        if (!IsDiscovering) return;
        IsDiscovering = false;
        OnPropertyChanged(nameof(IsDiscovering));
    }

    /// <summary>Motorun kümülatif keşif raporu: değerler o ana kadarki toplamlardır (delta DEĞİL), App son geleni
    /// okur. Keşif açık değilse (sessiz Sync, topolojiden sonra) yok sayılır — ekranda gösterecek bir blok yoktur.</summary>
    private void OnSyncDiscovery(SyncDiscoveryEvent e)
    {
        if (!IsDiscovering) return;
        DiscoveredRepositoryProjects = e.RepositoryProjects;
        DiscoveredExternalProjects = e.ExternalProjects;
        OnPropertyChanged(nameof(DiscoveredRepositoryProjects));
        OnPropertyChanged(nameof(DiscoveredExternalProjects));
        OnPropertyChanged(nameof(DiscoveredProjects));
    }
}
