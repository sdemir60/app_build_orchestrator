using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [tray indicator/K-4] Tepsi göstergesinin ÇİZİM yüzeyi. Gerçek uygulaması penceresiz, arka plansız bir
/// top-level overlay'dir (<c>Views/TrayBuildOverlayWindow</c>); controller onu yalnız bu beş fiil üzerinden
/// sürer, HWND/pencere bilgisi taşımaz.
/// </summary>
public interface ITrayBuildIndicatorView
{
    /// <summary>Göstergeyi konumla, göster ve döngüyü başlat.</summary>
    void ShowLoop();

    /// <summary>Reduced-motion yolu: göstergeyi konumla, göster — ama döngü HİÇ başlamasın (statik işaret +
    /// okunur sayaç).</summary>
    void ShowStatic();

    void UpdateCounter(int done, int total);

    /// <summary>Yeni tur BAŞLATMA; içindeki döngü doğal bitişine (çıkış evresi) koşsun, son karede
    /// <paramref name="onFinished"/>'ı çağır. Döngü YARIM kesilmez — bkz. K-10.</summary>
    void BeginExit(Action onFinished);

    /// <summary>Animasyonsuz, anında gizle.</summary>
    void HideNow();
}

/// <summary>[K-5] Koşu bitişinin BİLDİRİM yüzeyi — gerçek uygulaması OS tray balloon'udur (uygulama-içi
/// toast design §8'de YASAK).</summary>
public interface ITrayRunNotifier
{
    void ShowRunFinished(string message, bool healthy);
}

/// <summary>
/// [tray indicator/K-4] Tepsi göstergesinin TÜM görünürlük ve bitiş kararları — saf mantık, WPF'siz.
///
/// <para><b>Görünürlük kuralı tek cümledir:</b> gösterge yalnız <i>pencere gizli</i> VE <i>faz aktif kümede</i>
/// (<see cref="AppPhase.Starting"/> · <see cref="AppPhase.Running"/> · <see cref="AppPhase.Stopping"/>) iken
/// durur. <see cref="AppPhase.Syncing"/> KAPSAM DIŞIDIR: gösterge derleme koşularınındır.</para>
///
/// <para><b>İki çıkış yolu ayrıdır.</b> Pencere geri gelirse gösterge ANINDA gizlenir — kullanıcı zaten
/// ekrana döndü, ona bir çıkış animasyonu izletmenin değeri yoktur ve bildirim de üretilmez (şerit oradadır).
/// Aktif kümeden çıkılırsa (koşu bitti) çıkış evresi TAMAMLANIR, sonra gizlenme, sonra kısa bir nefes, sonra
/// balloon. Bu ikisi karışırsa ya yarım kesilmiş bir animasyon ya da pencere açıkken gereksiz bir bildirim
/// olur.</para>
///
/// <para><b>Nefes neden bir dikiş:</b> kaybolma ile bildirim üst üste binmemelidir (K-14), ama süresi bir
/// motion token'ıdır ve token okumak WPF ister. Controller saf kalsın diye bekleme
/// <see cref="ExitBreath"/>'e enjekte edilir; testte sahte dikiş senkron tamamlanır (D8: gerçek bekleme YOK).</para>
///
/// <para><b>Balloon metni burada ÜRETİLMEZ</b> (K-5): şeridin o anki terminal satırı
/// <see cref="SetTerminalText"/> ile verilir ve aynen taşınır. Alan bir ÖNBELLEK değildir, bir teslim
/// kutusudur: bildirim çıkış evresinin sonuna ertelendiği için metin o ana kadar tutulmak zorundadır.</para>
/// </summary>
public sealed class TrayBuildIndicatorController(ITrayBuildIndicatorView view, ITrayRunNotifier notifier)
{
    private bool _mainVisible = true;
    private AppPhase _phase = AppPhase.Empty;
    private bool _animationsEnabled = true;

    private bool _shown;
    private bool _exitPending;
    private bool _notified;

    private int _done;
    private int _total;
    private string _terminalText = "";
    private bool _terminalHealthy;

    /// <summary>
    /// [K-14 · D8] Gizlenme ile balloon arasındaki NEFES. Üretimde <c>Duration.Slow</c> token'ından beslenir
    /// (reduced-motion'da token 0'a düştüğü için nefes kendiliğinden kaybolur — animasyon istemeyen kullanıcı
    /// bekletilmez); testte senkron tamamlanan bir sahtedir, yani süitte GERÇEK bekleme oluşmaz.
    /// </summary>
    internal Func<Task> ExitBreath { get; set; } = () => Task.CompletedTask;

    /// <summary>Bir derleme koşuyor mu — göstergenin var olma gerekçesi. Sync bilerek DIŞARIDA.</summary>
    private static bool IsActive(AppPhase phase) =>
        phase is AppPhase.Starting or AppPhase.Running or AppPhase.Stopping;

    public void SetMainWindowVisible(bool visible)
    {
        if (_mainVisible == visible) return;
        _mainVisible = visible;
        Apply();
    }

    public void SetPhase(AppPhase phase)
    {
        if (_phase == phase) return;
        bool enteringRun = !IsActive(_phase) && IsActive(phase);
        _phase = phase;
        // Balloon bütçesi YALNIZ yeni bir koşu başlarken tazelenir: motor tek bir bitiş için iki terminal
        // sinyali üretse de ikinci balloon bastırılır.
        if (enteringRun) _notified = false;
        Apply();
    }

    /// <summary>
    /// [K-11] Reduced-motion sinyali. Değer çağıran tarafından <c>IMotionSettings.AnimationsEnabled</c>'dan
    /// TAZE okunup itilir (<c>AnimationsEnabledChanged</c> aboneliği) — controller bir tarih değil, o anki
    /// gerçeği tutar. Gösterge EKRANDAYKEN değişirse mod yerinde takas edilir; kapalıyken hiçbir yüzeye
    /// dokunulmaz.
    /// </summary>
    public void SetAnimationsEnabled(bool enabled)
    {
        if (_animationsEnabled == enabled) return;
        _animationsEnabled = enabled;
        if (!_shown) return;
        ApplyMode();
    }

    /// <summary>[K-6] Sayaç değerleri şeridin kullandığı <c>fin/wb</c> çiftidir — ikinci bir hesap YOK.
    /// Gösterge kapalıyken view'a İTİLMEZ (§14.5: görünmeyen yüzeyde measure/draw kirletilmez); açılışta son
    /// değer bir kez akar.</summary>
    public void SetCounter(int done, int total)
    {
        _done = done;
        _total = total;
        if (_shown) view.UpdateCounter(done, total);
    }

    /// <summary>[K-5] Şeridin O ANKİ satırı + sağlık bayrağı. <c>healthy</c> şeridin glyph'inin
    /// <c>"failed"</c> OLMAMASIDIR — glyph zaten tek kaynaklı statü sinyalidir.</summary>
    public void SetTerminalText(string text, bool healthy)
    {
        _terminalText = text;
        _terminalHealthy = healthy;
    }

    private void Apply()
    {
        if (!_mainVisible && IsActive(_phase))
        {
            if (_shown) return;      // zaten ekranda — mod takası SetAnimationsEnabled'ın işi
            Show();
            return;
        }

        if (!_shown) return;         // gösterilecek bir şey yok

        if (_mainVisible)
        {
            // Pencere geri geldi: ANINDA gizle. Uçuşta bir çıkış varsa BALLOON TAAHHÜDÜ KORUNUR — terminal
            // anında tepsideydik; callback yine gelecek ve bildirimi bir kez gösterecek.
            _shown = false;
            view.HideNow();
            return;
        }

        StartExit();
    }

    private void Show()
    {
        _shown = true;
        ApplyMode();
        view.UpdateCounter(_done, _total);
    }

    private void ApplyMode()
    {
        if (_animationsEnabled) view.ShowLoop();
        else view.ShowStatic();
    }

    private void StartExit()
    {
        if (_exitPending) return;
        _exitPending = true;

        if (_animationsEnabled) view.BeginExit(OnExitFinished);
        else OnExitFinished(); // reduced-motion: oynatılacak çıkış evresi yok, sıra aynen sürer
    }

    private void OnExitFinished() => _ = CompleteExitAsync();

    private async Task CompleteExitAsync()
    {
        _exitPending = false;
        if (_shown)
        {
            _shown = false;
            view.HideNow();
        }

        await ExitBreath();

        if (_notified) return;
        _notified = true;
        notifier.ShowRunFinished(_terminalText, _terminalHealthy);
    }
}
