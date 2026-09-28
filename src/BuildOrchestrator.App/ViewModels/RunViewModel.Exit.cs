using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [P3 · Task 2] <see cref="RunViewModel"/>'in <b>güvenli tam çıkış</b> yüzeyi: kullanıcı uygulamayı gerçekten
/// kapatmak istediğinde (Close to tray kapalıyken ×, tepsi → Exit) arkada yarım iş BIRAKILMAZ. Uçuşta iş yoksa
/// çıkış hemen hazırdır; varsa iş bitince hazır olur ve kabuğa <see cref="ExitReady"/> ile BİR kez bildirilir.
///
/// <para><b>Uçuştaki iş</b> mevcut tek sorudur — <see cref="WorkspaceIdle"/> (koşu kilidi + Sync/Clean/Optimize/
/// checkout/pull); ikinci bir tanım yazılmaz. Derleme graceful durdurulur — Stop'un kendisi: yeni proje başlamaz,
/// uçuştaki <c>MSBuild.exe</c>'ler post-build copy dahil biter, ortak çıktı dizininde yarım DLL kalmaz. Açılış
/// koreografisindeki henüz gönderilmemiş koşu Stop'un marking kuralıyla geri alınır. Workspace işlerinin iptali
/// yoktur ve yarıda kesilmeleri ağacı bozar: beklenirler — bitişlerinde zincirlenen Sync dahil (devir kapıyı
/// bırakmadan olur, bkz. <see cref="SyncThenReleaseAsync"/>).</para>
///
/// <para><b>Bekleyiş sonsuz değildir:</b> motor susarsa sessizlik bekçisinin uyarısı (<see cref="EngineOverdueMessage"/>)
/// çıkışı serbest bırakır — bekçi çıkışın bulunabileceği her pencerede (Starting, Stopping, Syncing, workspace işi)
/// zaten kuruludur. Motor ölürse <see cref="ReleaseAfterEngineLoss"/> her pencereyi bırakır ve koşul kendiliğinden
/// doğrulanır.</para>
///
/// <para><b>Değerlendirme noktaları:</b> meşguliyet bildiriminin tek noktası (<see cref="NotifyAutoSyncGate"/> — her
/// iş bayrağı ve koşu kilidi geçişi oraya iner) ve bekçinin uyarısı (<see cref="OnEngineOverdueMessageChanged"/>).
/// Bekleyiş boyunca kendiliğinden Sync başlamaz (<see cref="DisableAutoSync"/>): başlasaydı drain'in hemen ardından
/// yeni bir iş açılır ve çıkış onu da beklerdi.</para>
///
/// <para><b>Abone sözleşmesi:</b> <see cref="ExitReady"/> bir durum geçişinin ORTASINDA (ör. <c>IsRunning</c>
/// düşerken) senkron atılabilir; VM abonenin ne yaptığına dayanmaz — kabuk kapanışı kendi kuyruğuna erteler.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Çıkış uçuştaki işi bekliyor — bir kez true olur, geri dönmez. Şerit bu sürede Stopping satırını okur
    /// (<see cref="RibbonLine"/>). Uçuşta iş yokken istenen çıkış bunu HİÇ açmaz.</summary>
    [ObservableProperty] private bool _exitPending;

    /// <summary>Uçuşta iş kalmadı ya da motor sustu/öldü — kabuk artık kapanabilir. TEK atım.</summary>
    public event EventHandler? ExitReady;

    /// <summary>Bekleyiş açılırken konsola düşen tek satır — kapatma tıklamasının kalıcı kaydı (şerit yalnız anlık
    /// durumu gösterir; bkz. <see cref="StopRequestedLine"/>).</summary>
    internal static string ExitPendingLine => "exit requested — the app closes when the work in flight finishes";

    /// <summary>Çıkış istendi: ikinci istek (ikinci ×, tepsi → Exit) durum ne olursa olsun tam no-op'tur.</summary>
    private bool _exitRequested;

    /// <summary><see cref="ExitReady"/> atıldı — birden çok değerlendirme noktası aynı anı görebilir.</summary>
    private bool _exitReadyRaised;

    /// <summary>
    /// Tam çıkış iste. Sıra: (1) ikinci istek hiçbir şey yapmaz; (2) kendiliğinden Sync kapanır; (3) uçuşta iş yoksa
    /// <see cref="ExitReady"/> hemen — bekleyiş açılmaz, satır yazılmaz, komut gitmez; (4) varsa bekleyiş açılır,
    /// konsola <see cref="ExitPendingLine"/> düşer ve Stop yapılabiliyorsa yapılır (marking fazında isteği geri alır,
    /// aksi hâlde graceful <c>stopRun</c>; Stop zaten istendiyse ikincisi gitmez — <see cref="CanStop"/>); (5) koşul
    /// hemen yeniden değerlendirilir (motor zaten susmuş olabilir).
    /// </summary>
    public void RequestExit()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        DisableAutoSync();
        if (WorkspaceIdle)
        {
            RaiseExitReady();
            return;
        }
        ExitPending = true;
        AppendRunLine(ExitPendingLine);
        if (StopCommand.CanExecute(null)) StopCommand.Execute(null);
        EvaluateExit();
    }

    /// <summary>Bekleyiş sürüyor ve ya uçuşta iş kalmadı ya da motor sustu → <see cref="ExitReady"/>.</summary>
    private void EvaluateExit()
    {
        if (ExitPending && (WorkspaceIdle || EngineOverdueMessage is not null)) RaiseExitReady();
    }

    private void RaiseExitReady()
    {
        if (_exitReadyRaised) return;
        _exitReadyRaised = true; // bayrak ÖNCE: abonenin içinden gelen yeniden giriş ikinci bir atım üretmez
        ExitReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sessizlik bekçisinin uyarısı değişti — motor sustuysa çıkış drain'i beklemez.</summary>
    partial void OnEngineOverdueMessageChanged(string? value) => EvaluateExit();
}
