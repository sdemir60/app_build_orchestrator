namespace BuildOrchestrator.App.Shell;

/// <summary>[P3 · Task 3] Pencereyi kapatma isteğine verilen cevap — <see cref="WindowCloseRule.Decide"/> seçer,
/// <c>MainWindow.OnClosing</c> uygular.</summary>
internal enum CloseAction
{
    /// <summary>Pencere gerçekten kapanır — kapanış iptal edilmez.</summary>
    Close,

    /// <summary>Kapanış iptal edilir, başka hiçbir şey yapılmaz: çıkış zaten uçuştaki işi bekliyor.</summary>
    Stay,

    /// <summary>Kapanış iptal edilir ve pencere tepsiye gizlenir — uygulama arka planda sürer.</summary>
    HideToTray,

    /// <summary>Kapanış iptal edilir ve güvenli tam çıkış istenir — uygulamayı kapatan, çıkış hazır olunca gelen
    /// Shutdown'dır.</summary>
    RequestExit,
}

/// <summary>
/// [P3 · Task 3] Pencere kapanış kararının TEK yeri. × / Alt+F4 / sistem menüsü Kapat ve Shutdown'ın kendi kapanışı
/// aynı <c>Closing</c>'e iner; kabuk yalnız uygular. Saftır (WPF İÇERMEZ) — <see cref="SecondInstanceGate"/> ile
/// AYNI desen ve gerekçe: <c>MainWindow</c> headless süitte gösterilemez, bu yüzden KARAR kabuktan ayrılır.
///
/// <para><b>Öncelik kararın kendisidir.</b> (1) Gerçek kapanış (<c>exiting</c>: çıkış hazır ve Shutdown kuyrukta, ya
/// da Windows oturumu kapanıyor) her şeyi geçer — <c>exitPending</c> bir kez açılınca geri dönmez, yani bekleyişin
/// ardından gelen Shutdown'ın kendi kapanışı da bu kapıdan geçer ve iptal edilmemelidir. (2) Bekleyen çıkış pencereyi
/// yerinde tutar, Close to tray açık olsa bile: kullanıcı kapatmayı istedi ve bekleyişi izliyor; ikinci × ikinci bir
/// çıkış istemez. (3) İkisi de yoksa anahtar söz alır: açık → tepsiye (K5), kapalı → güvenli tam çıkış.</para>
/// </summary>
internal static class WindowCloseRule
{
    public static CloseAction Decide(bool exiting, bool exitPending, bool closeToTray) =>
        exiting ? CloseAction.Close
        : exitPending ? CloseAction.Stay
        : closeToTray ? CloseAction.HideToTray
        : CloseAction.RequestExit;
}
