using System.Runtime.InteropServices;

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

    /// <summary><paramref name="set"/>'i çağırır; pano-kilit sınıfı bir <see cref="ExternalException"/>
    /// (COMException/CLIPBRD_E_CANT_OPEN dahil) fırlarsa <paramref name="attempts"/> kez, aralarında
    /// <paramref name="wait"/> çağrılarak yeniden dener. Başarılıysa true; tüm denemeler kilit hatasıyla
    /// biterse false (kilit-DIŞI istisnalar YUTULMAZ — yayılır). </summary>
    public static bool TrySet(Action set, int attempts = DefaultAttempts, Action<int>? wait = null)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { set(); return true; }
            catch (ExternalException) // COMException dahil — pano kilidi sınıfı
            {
                if (i + 1 < attempts) wait?.Invoke(i);
            }
        }
        return false;
    }

    /// <summary>Üretim yolu: WPF <c>Clipboard.SetText</c>'i retry ile çağırır (kısa Thread.Sleep bekleme).
    /// UI thread'inde çağrılır; en kötü ~<c>DefaultAttempts×DefaultDelayMs</c> = ~100ms bloklar.</summary>
    public static bool SetText(string text) =>
        TrySet(() => System.Windows.Clipboard.SetText(text), DefaultAttempts,
            _ => System.Threading.Thread.Sleep(DefaultDelayMs));
}
