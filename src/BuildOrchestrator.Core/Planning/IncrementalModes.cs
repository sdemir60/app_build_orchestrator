namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// Defteri dinleyen (incremental) koşu modlarının TEK kaynağı: Build ve Cycles güncel olanı atlar ve kökünü bekleyeni
/// sırası gelince koşullu değerlendirir. Rebuild önbelleği yok sayar (her şeyi derler), Clean hiçbir şey derlemez.
/// <para>Kopya YASAK: koordinatörün güncel tohumu (<c>RunCoordinator</c>: güncel grup ve proje pre-skip'i) ile
/// <see cref="ConditionalRebuild"/>'in tekil ve grup mod kuralı buradan okur. Kural eskiden bu iki yerde ayrı ayrı
/// yazılıydı.</para>
/// </summary>
public static class IncrementalModes
{
    public static bool Includes(RunMode mode) => mode is RunMode.Build or RunMode.Cycles;
}
