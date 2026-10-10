namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

/// <summary>
/// Bir koşunun SCC (dependency cycle) üyelerini DERLEYİP derlemediğinin TEK kaynağı. Build ve Rebuild de kirli
/// grupları Cycles'ın tur mekanizmasıyla derler (ARCHITECTURE §8.1); Clean hiçbir şey derlemez, planı zaten
/// döngüsüzdür (<c>CleanRunScope</c>). Satırdan tetiklenen tek-proje kapsamında grup yoktur: hedef düz düğüm
/// olarak tek başına derlenir (<see cref="ProjectRunScope"/>).
/// <para>Kopya YASAK: Sync'in önizlemesi (<c>SyncWorkspaceService</c>), Supervisor'ın planı
/// (<c>Program.ComputeIncremental</c>), koordinatörün grup kapısı (<c>RunCoordinator.PlanAndRunAsync</c>) ve
/// App'in tur muhasebesi (<c>RunViewModel.UpdateEta</c>) hep buradan okur — iki yer sessizce ayrışamaz.</para>
/// </summary>
public static class CycleCompilation
{
    public static bool CompilesCycles(RunMode mode) => mode is RunMode.Build or RunMode.Rebuild or RunMode.Cycles;

    /// <summary>
    /// Bir koşunun grup haritası: SCC derleyen bir modun TAM koşusunda plandaki döngü grupları; tek-proje kapsamında,
    /// döngü derlemeyen modda ya da döngüsüz planda <c>null</c> (scheduler o zaman InCycle düğümü pre-skip eder).
    /// <para>[ara inceleme I2] Koordinatörün grup kapısı ve Sync'in "bir sonraki düz Build" önizlemesi haritayı BURADAN
    /// alır (kopya YASAK): ikisi grup üyeliğini ayrı türetince kökünü bekleyen bir döngü üyesi Sync'te koşullu,
    /// koşunun kendi önizlemesinde koşulsuz okunuyordu.</para>
    /// </summary>
    public static CycleGroups? GroupsFor(BuildPlan plan, RunMode mode, bool scopedRun)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return !scopedRun && CompilesCycles(mode) && CycleGroups.From(plan) is { Count: > 0 } groups ? groups : null;
    }
}
