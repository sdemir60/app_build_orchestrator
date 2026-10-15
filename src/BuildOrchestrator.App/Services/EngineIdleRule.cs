namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 / spec §6.4] Boştaki motor sürecinin durdurulması kararının SAF kuralı (WPF'siz; <c>MainWindow</c>'un tiki
/// uygular — <see cref="MemoryTrimRule"/> ile aynı yerleşim). Kuralın girdileri ve kararı sonraki görevle gelir; burada şimdilik kuralın
/// TEK sayısı durur: motor ne kadar işsiz kalınca uyur.
/// </summary>
public static class EngineIdleRule
{
    /// <summary>Son motor etkinliğinden bu kadar sonra ve o arada hiçbir iş yokken boştaki motor durdurulur (spec §6.4: beş dakika).
    /// Süre TEK yerdedir: onu kullanan kural/tik kodu kendi sayısını yazmaz, buradan okur. Kullanıcıya görünen iki metin (Settings
    /// satırının açıklaması ve "on" notu) süreyi sözcükle yazar — onların bu sabitle ayrışmaması testle yakalanır
    /// (<c>StopEngineWhenIdleSettingsTests</c>).</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
}
