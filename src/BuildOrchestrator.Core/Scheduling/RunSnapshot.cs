namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T55] Stop/Continue sınırını aşan run state'i: hangi projeler tamamlandı (<see cref="Completed"/> —
/// Continue'da yeniden derlenmez), hangileri hâlâ iş bekliyor (<see cref="Queued"/> — orijinal build-order
/// sırasında) ve o ana kadar geçen süre (<see cref="ElapsedMs"/>, bkz. <see cref="RunClock"/>).
///
/// <see cref="ReadySetScheduler.TakeSnapshot"/> ile alınır, <see cref="ReadySetScheduler(BuildPlan, RunSnapshot)"/>
/// resume ctor'una geçirilir. Saf DTO: I/O yok, disk'e serialize edilmez (YAGNI — bu task'ın kapsamı değil).
///
/// Completed ve Queued, AYNI BuildPlan.Nodes kümesinin (cycle-üyeleri dahil) bir PARTİSYONUDUR — her proje id
/// tam olarak birinde bulunur, ikisinde birden ya da hiçbirinde değil (bkz. TakeSnapshot dokümantasyonu).
/// </summary>
public sealed record RunSnapshot(
    IReadOnlyDictionary<string, BuildResult> Completed,
    IReadOnlyList<string> Queued,
    long ElapsedMs);
