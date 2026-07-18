using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [Flaky-test hardening] <see cref="BuildStateStoreTests"/> içindeki bazı testler GERÇEK OS
/// file-sharing violation'ları üretir (hedefi uyumsuz share mode ile açık tutup <c>BuildStateStore.Upsert</c>'in
/// sınırlı rename-retry bütçesinin — 20×5ms ≈ 100ms — kurtarmasını ya da tükenmesini bekler). Full suite'in
/// paralel yükü altında (xUnit test collection'ları paralel koşar) diğer testlerden gelen CPU contention bu
/// 100ms bütçeyi ara sıra tüketip testi flaky şekilde fail ettirebiliyordu (izolasyonda / rerun'da hep geçiyor —
/// timing/starvation flake'i, correctness bug değil). Çözüm: bu sınıfı DisableParallelization=true bir
/// collection'a koyup diğer collection'larla eşzamanlı koşmasını engellemek — CPU contention'ı ortadan kaldırır.
/// Bkz. BuildStateStoreTests.cs — production kodda (retry bütçesi dahil) HİÇBİR değişiklik yapılmadı.
/// </summary>
[CollectionDefinition("BuildStateStore file-lock (serial)", DisableParallelization = true)]
public class BuildStateStoreSerialCollection
{
}
