namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// Bir koşunun SCC (dependency cycle) üyelerini DERLEYİP derlemediğinin TEK kaynağı. Build ve Rebuild de kirli
/// grupları Cycles'ın tur mekanizmasıyla derler (ARCHITECTURE §8.1); Clean hiçbir şey derlemez, planı zaten
/// döngüsüzdür (<c>CleanRunScope</c>). Satırdan tetiklenen tek-proje kapsamı bu soruyu sormaz: hedef düz düğüm
/// olarak tek başına derlenir (<see cref="ProjectRunScope"/>), karar koordinatörde <c>ScopeProjectId</c> ile
/// ayrıca kapılıdır.
/// <para>Kopya YASAK: Sync'in önizlemesi (<c>SyncWorkspaceService</c>), Supervisor'ın planı
/// (<c>Program.ComputeIncremental</c>), koordinatörün grup kapısı (<c>RunCoordinator.PlanAndRunAsync</c>) ve
/// App'in tur muhasebesi (<c>RunViewModel.UpdateEta</c>) hep buradan okur — iki yer sessizce ayrışamaz.</para>
/// </summary>
public static class CycleCompilation
{
    public static bool CompilesCycles(RunMode mode) => mode is RunMode.Build or RunMode.Rebuild or RunMode.Cycles;
}
