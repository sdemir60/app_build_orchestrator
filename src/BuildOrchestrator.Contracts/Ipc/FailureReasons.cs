namespace BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// <see cref="ProjectFailedEvent.Reason"/>'ın "kullanıcı durdurdu" değeri — TEK doğruluk kaynağı
/// (<see cref="SkipReasons"/>'ın başarısızlık yanı). Contracts'ta yaşar çünkü Supervisor (<c>RunCoordinator</c>) YAZAR,
/// App (<c>RunViewModel.NoteTerminated</c>) OKUR: aynı literal iki projede tanımlanırsa biri değişip diğeri
/// unutulduğunda "Stop now" sonrası sayılan sonlandırılmış proje sayısı sessizce sıfırda kalırdı (kopya YASAK,
/// CLAUDE.md). Reason'ın öbür değerleri serbest metindir ("exit N", "timeout", "invoke error: …", "group start failed: …") ve burada yoktur.
/// <para>Değer WIRE'dadır (NDJSON satırı): değiştirmek, eski bir Supervisor ile yeni bir App'i ayrıştırır.</para>
/// </summary>
public static class FailureReasons
{
    /// <summary>Kullanıcı durdurdu: hard stop'ta uçuştaki her projenin, kill edilen bir child'ın ve iptalle yarıda kalan
    /// bir projenin nedeni. Bir derleme hatası değil, kullanıcının kararıdır.</summary>
    public const string Stopped = "stopped";
}
