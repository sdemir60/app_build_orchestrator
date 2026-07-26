using System.Runtime.InteropServices;
using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Copy-log için pano yazma retry sarmalayıcısı (feasibility §3.10 / Ek A #3). <c>Clipboard.SetText</c>
/// ÇIPLAK çağrılmaz: pano başka bir process tarafından kilitliyken <c>CLIPBRD_E_CANT_OPEN</c>
/// (<see cref="COMException"/>, HRESULT 0x800401D0) fırlatır — dotnet/wpf#9901. Bu geçici kilitte kısa aralıkla
/// yeniden denenir; kalıcı kilitte sessizce başarısız olur (UI çökmez).
///
/// <para><b>Test edilebilirlik:</b> gerçek <c>Clipboard.SetText</c> yerine bir <c>set</c> Action enjekte edilir
/// (fail-then-succeed simülasyonu) ve <c>wait</c> ile bekleme deterministik kılınır (gerçek sleep yok) — D8.</para>
/// </summary>
public static class ClipboardRetry
{
    public const int DefaultAttempts = 10;
    public const int DefaultDelayMs = 10;

    /// <summary>CLIPBRD_E_CANT_OPEN (dotnet/wpf#9901) — pano başka bir process tarafından kilitliyken
    /// <c>Clipboard.SetText</c>'in fırlattığı HRESULT. YALNIZ bu istisna retry edilir.</summary>
    public const int ClipboardCantOpen = unchecked((int)0x800401D0);

    /// <summary><paramref name="set"/>'i çağırır; YALNIZ pano-kilidi (<see cref="COMException"/>/
    /// <see cref="ExternalException"/>, HRESULT <see cref="ClipboardCantOpen"/>) fırlarsa <paramref name="attempts"/>
    /// kez, aralarında <paramref name="wait"/> çağrılarak yeniden dener. Başarılıysa true; tüm denemeler kilit
    /// hatasıyla biterse false. Kilit-DIŞI istisnalar (başka bir COM/HRESULT dahil) retry EDİLMEZ ve YUTULMAZ —
    /// olduğu gibi yayılır (sessizce yutulmuş bir hata değil). </summary>
    public static bool TrySet(Action set, int attempts = DefaultAttempts, Action<int>? wait = null)
        // [T49 fix round 1 · B2] Döngü ortak (SyncRetry, Core) — burada yalnız BU yolun kararları durur: hangi
        // istisna geçici (yalnız CLIPBRD_E_CANT_OPEN), gecikme nereden gelir, bütçe tükenince ne olur (burada:
        // sessizce false — UI çökmez). Kopya YASAK, CLAUDE.md.
        => SyncRetry.Run(
            set, attempts,
            ex => ex is ExternalException external && external.ErrorCode == ClipboardCantOpen,
            wait ?? (_ => { }),
            rethrowWhenExhausted: false);

    /// <summary>Üretim yolu: WPF <c>Clipboard.SetText</c>'i retry ile çağırır (kısa Thread.Sleep bekleme).
    /// UI thread'inde çağrılır; en kötü ~<c>DefaultAttempts×DefaultDelayMs</c> = ~100ms bloklar.</summary>
    public static bool SetText(string text) =>
        TrySet(() => System.Windows.Clipboard.SetText(text), DefaultAttempts,
            _ => System.Threading.Thread.Sleep(DefaultDelayMs));
}
