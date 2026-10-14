namespace BuildOrchestrator.Tests;

/// <summary>
/// [bellek trimi · idle-memory final review o2] "Testler gerçek agresif GC'yi koşturmaz" politikasının TEK tanımı (kopya YASAK).
/// Üretim toplaması (<c>MemoryTrim.CollectAndRelease</c>) bloklayan, sıkıştıran bir GC'dir ve bütün test sürecini durdurur; her
/// koordinatör testi koşu ya da Sync bitişinde, her pencere testi işlem bitişinde onu ödese süit yavaşlar ve paralel koşan zamanlama
/// testlerini bozar. İki dikiş aynı boş delegeyi alır: Supervisor'ın <c>RunCoordinator</c> trim dikişi
/// (<c>SupervisorHostHarness.NewIdleCoordinator</c> ve <c>RunCoordinatorTests.Harness</c> varsayılanı) ve App'in
/// <c>MainWindow.MemoryCollector</c>'ı (<c>MainWindowHost</c>). Toplamayı SAYAN ya da sıralayan testler kendi sahtesini verir;
/// üretim varsayılanlarını yalnız kaynak pini (<c>RunCoordinatorMemoryTests</c>) ve kapılı ölçüm (<c>NarrativeBudgetTests</c>) görür.
/// </summary>
internal static class NoRealGc
{
    /// <summary>Hiçbir şey yapmayan toplayıcı — toplama KARARI ve zamanlaması aynen koşar, yalnız GC çalıştırılmaz.</summary>
    public static readonly Action Collection = static () => { };
}
