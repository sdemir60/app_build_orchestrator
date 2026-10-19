using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [P3 · Task 2 · Exit] <see cref="RunViewModel"/>'in <b>tam çıkış</b> yüzeyi: kullanıcı uygulamayı gerçekten kapatmak
/// istediğinde (title bar'ın Exit'i ve Ctrl+Q, Close to tray kapalıyken ×, tepsi → Exit — iş varsa onay kartından geçerek)
/// uçuştaki iş <b>durdurulur</b> ve uygulama kapanır. Uçuşta iş yoksa çıkış hemen hazırdır; varsa durdurma bitince hazır
/// olur ve kabuğa <see cref="ExitReady"/> ile BİR kez bildirilir.
///
/// <para><b>Uçuştaki iş</b> mevcut tek sorudur — <see cref="WorkspaceIdle"/> (koşu kilidi + Sync/Clean/Optimize/
/// checkout/pull); ikinci bir tanım yazılmaz. Neyin nasıl bittiği:
/// <list type="bullet">
/// <item><b>Koşu</b> (Build, Rebuild, Resolve, <c>-t:Clean</c>) hard stop'la durur (<see cref="SendHardStopAsync"/> — Stop'un
/// "Stop now"ı): uçuştaki derlemeler sonlandırılır, motor onları <c>failed("stopped")</c> raporlar ve <c>runStopped</c> yazar.
/// Çıkış bu cevabı en çok <see cref="ExitStopWaitMs"/> bekler; gelmezse yine gider — kapanış motoru outer job'la bitirir ve yarıda
/// kalan projeleri motorun in-flight defteri bir sonraki açılışta geçersizler (ARCHITECTURE §8.7). Açılış koreografisindeki henüz
/// gönderilmemiş koşu Stop'un marking kuralıyla geri alınır.</item>
/// <item><b>Sync, Deep Clean, Optimize</b> beklenmez: iptalleri yoktur ve kesilmeleri güvenlidir — kapanış onları motorla birlikte
/// bitirir.</item>
/// <item><b>Checkout ve pull</b> beklenir (<see cref="GitWriteBusy"/>): git çalışma ağacına yazar; yarıda kesilen bir checkout ağacı
/// yarı değiştirilmiş ve <c>.git/index.lock</c>'lu bırakırdı.</item>
/// </list>
/// Bitişlere zincirlenen Sync çıkış istendikten sonra BAŞLAMAZ (<see cref="SyncCoreAsync"/>'in kapısı): zincir işin yüzeyini
/// doğrudan bırakır (<see cref="SyncThenReleaseAsync"/>).</para>
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19 · Exit butonu]</b> ESKİ: tam çıkış uçuştaki işi BEKLERDİ — derleme
/// graceful durdurulur ve drain beklenir, her workspace işi sonuna kadar koşardı; çıkış hard stop'a asla tırmanmazdı. Kullanıcı
/// "Exit dediğimde her şey kill olacak" dedi; bekleyiş yalnız git yazımında kaldı.</para>
///
/// <para><b>Bekleyiş sonsuz değildir:</b> motor susarsa sessizlik bekçisinin uyarısı (<see cref="EngineOverdueMessage"/>)
/// çıkışı serbest bırakır — bekçi çıkışın bulunabileceği her pencerede (Starting, Stopping, Syncing, workspace işi)
/// zaten kuruludur. Motor ölürse <see cref="ReleaseAfterEngineLoss"/> her pencereyi bırakır — kapıdan geçişini tutan (motora henüz teslim
/// edilmemiş) isteklerinkiler hariç: onları kendi geri alımları bırakır — ve koşul kendiliğinden doğrulanır. [idle-memory · dalga 3 · Task 21] Motor kapısında bekleyen
/// istekleri (<see cref="EnsureEngineAwakeAsync"/>: uyku döngüsü) çıkış isteği geri çeker — hiçbiri gönderilmez, her biri kendi geri alımıyla
/// pencereyi bırakır ve koşul kendiliğinden doğrulanır; bekleyen koşu ayrıca Stop'un kuralıyla komutsuz geri alınır.</para>
///
/// <para><b>Değerlendirme noktaları:</b> meşguliyet bildiriminin tek noktası (<see cref="OnWorkspaceBusyChanged"/> —
/// her iş bayrağı ve koşu kilidi geçişi oraya iner), bekçinin uyarısı (<see cref="OnEngineOverdueMessageChanged"/>) ve tick
/// (<see cref="TickElapsed"/> — durdurma süresi saatle dolar). Çıkış istendikten sonra hiçbir Sync başlamaz — kendiliğinden Sync
/// kapanır (<see cref="DisableAutoSync"/>), zincirlenen ve diğer her Sync'i <see cref="SyncCoreAsync"/>'in kapısı keser.</para>
///
/// <para><b>Abone sözleşmesi:</b> <see cref="ExitReady"/> bir durum geçişinin ORTASINDA (ör. <c>IsRunning</c>
/// düşerken) senkron atılabilir; VM abonenin ne yaptığına dayanmaz — kabuk kapanışı kendi kuyruğuna erteler.</para>
/// </summary>
public sealed partial class RunViewModel
{
    /// <summary>Çıkış uçuştaki işi durduruyor — bir kez true olur, geri dönmez. Şerit bu sürede Stopping satırını okur
    /// (<see cref="RibbonLine"/>). Uçuşta iş yokken istenen çıkış bunu HİÇ açmaz.</summary>
    [ObservableProperty] private bool _exitPending;

    /// <summary>Uçuşta beklenecek iş kalmadı, durdurma süresi doldu ya da motor sustu/öldü — kabuk artık kapanabilir. TEK atım.</summary>
    public event EventHandler? ExitReady;

    /// <summary>Bekleyiş açılırken konsola düşen tek satır — kapatma isteğinin kalıcı kaydı (şerit yalnız anlık
    /// durumu gösterir; bkz. <see cref="StopRequestedLine"/>).</summary>
    internal static string ExitPendingLine => "exit requested — ending the work in flight, then closing";

    /// <summary>Çıkış istendi: ikinci istek (ikinci ×, tepsi → Exit) durum ne olursa olsun tam no-op'tur. [Task 21 · Fix round 1 · R3] Motor
    /// kapısının (<see cref="EnsureEngineAwakeAsync"/>) ve Sync'in kapısının (<see cref="SyncCoreAsync"/>) çıkış sorusu da budur — TEK bayrak:
    /// <see cref="ExitPending"/> yalnız uçuşta iş varken açılır; kapıda yalnız bir proje logu yüklemesi bekliyorsa ya da uçuşta hiç iş yokken
    /// hiç açılmaz.</summary>
    private bool _exitRequested;

    /// <summary><see cref="ExitReady"/> atıldı — birden çok değerlendirme noktası aynı anı görebilir.</summary>
    private bool _exitReadyRaised;

    /// <summary>Çıkışın koşunun durdurma cevabını (<c>runStopped</c>) en çok bekleyeceği süre (ms). Hard stop inner job'u hemen
    /// sonlandırır ve cevap normalde milisaniyeler içinde gelir; süre, planlamada takılı ya da cevabı yolda kalmış bir motor içindir.
    /// Sessizlik eşiği (<see cref="EngineSilenceThresholdMs"/>) burada çok uzundur: kullanıcı çıkmak istedi ve kapanış ekranını izliyor.</summary>
    internal const long ExitStopWaitMs = 3_000;

    /// <summary>Durdurma süresinin dolduğu an (<see cref="_nowMs"/>); bekleyiş bir koşuyla açılmadıysa <c>null</c>.</summary>
    private long? _exitStopDeadlineMs;

    /// <summary>Git'in çalışma ağacına yazdığı iş uçuşta — çıkış bunu kesmez, bekler.</summary>
    private bool GitWriteBusy => CheckoutBusy || PullBusy;

    /// <summary>
    /// Tam çıkış iste. Sıra: (1) ikinci istek hiçbir şey yapmaz; (2) kendiliğinden Sync kapanır; (3) uçuşta iş yoksa
    /// <see cref="ExitReady"/> hemen — bekleyiş açılmaz, satır yazılmaz, komut gitmez; (4) varsa bekleyiş açılır, konsola
    /// <see cref="ExitPendingLine"/> düşer ve koşu durdurulur: marking fazında istek geri alınır, aksi hâlde hard stop gider
    /// (Stop zaten istendiyse de — drain beklenmez; hard zaten gittiyse ikincisi gitmez); (5) koşul hemen yeniden değerlendirilir
    /// (beklenecek bir şey kalmamış ya da motor zaten susmuş olabilir).
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
        if (IsMidRunLocked) _exitStopDeadlineMs = _nowMs() + ExitStopWaitMs;
        StopRunForExit();
        EvaluateExit();
    }

    /// <summary>Koşuyu çıkış için durdurur: komut henüz gitmediyse istek geri alınır (Stop'un marking kuralı), gittiyse ve hard stop
    /// henüz istenmediyse hard stop gider. Koşu yoksa ya da hard zaten gittiyse hiçbir şey yapmaz.</summary>
    private void StopRunForExit()
    {
        if (_pendingRunId is not null)
        {
            CancelPendingRun();
            return;
        }
        if (_currentRunId is { } runId && StopCommand.CanExecute(null)) _ = SendHardStopAsync(runId);
    }

    /// <summary>Bekleyiş sürüyor ve ya beklenecek iş kalmadı (koşu bitti ya da durdurma süresi doldu, git yazımı yok) ya da
    /// motor sustu → <see cref="ExitReady"/>.</summary>
    private void EvaluateExit()
    {
        if (!ExitPending) return;
        bool runSettled = !IsMidRunLocked || _exitStopDeadlineMs is { } deadline && _nowMs() >= deadline;
        if (runSettled && !GitWriteBusy || EngineOverdueMessage is not null) RaiseExitReady();
    }

    private void RaiseExitReady()
    {
        if (_exitReadyRaised) return;
        _exitReadyRaised = true; // bayrak ÖNCE: abonenin içinden gelen yeniden giriş ikinci bir atım üretmez
        ExitReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sessizlik bekçisinin uyarısı değişti — motor sustuysa çıkış beklemez.</summary>
    partial void OnEngineOverdueMessageChanged(string? value) => EvaluateExit();

    /// <summary>Bekleyiş açıldı: run komutlarının kapısı (<see cref="CanRequestRun"/>) kapanır — bekleyiş kapanmak içindir;
    /// o sırada başlayan bir koşu çıkışı bekletirdi. Kapıların yeniden sorulduğu liste TEKTİR
    /// (<see cref="NotifySyncGatedCommands"/>).</summary>
    partial void OnExitPendingChanged(bool value) => NotifySyncGatedCommands();
}
