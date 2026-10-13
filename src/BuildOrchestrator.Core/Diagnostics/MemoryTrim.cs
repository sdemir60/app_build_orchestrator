namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>
/// Uygulamanın "işim bitti, boştayım" anındaki TEK bellek trimi: nesil 2 ve LOH sıkıştırılır, boşalan bölgeler işletim
/// sistemine geri verilir. <see cref="GCCollectionMode.Aggressive"/> yalnız <c>blocking: true</c> ile geçerlidir
/// (<c>blocking: false</c> her çağrıda <see cref="ArgumentException"/> fırlatır), yani çağrı BLOKLAYICIDIR: toplama süresince
/// bütün yönetilen thread'ler durur. Çağıran, bu duraklamanın kimseyi rahatsız etmeyeceği anı seçer (App: bitiş finalinden
/// sonra, uygulama boştayken; Supervisor: işlem sonunda, koşu slotu boşken). Toplama çağrısı başka hiçbir dosyada yazılmaz
/// (kaynak guard'ı: <c>MemoryTrimGuardTests</c>).
/// </summary>
public static class MemoryTrim
{
    public static void CollectAndRelease() =>
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
