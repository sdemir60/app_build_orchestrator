using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>[Final review I-2] Konsol başlığı seçili SATIRI okur (<c>ConsoleHeader.ShowProjectLog(row, …)</c>) —
/// başlık testlerinin gerçek bir <see cref="ProjectRowViewModel"/> kurmasının TEK yeri (kopya YASAK). Varsayılan
/// satır koşu dışıdır; Started bir satır <see cref="ProjectRowViewModel.IsCompiling"/> true'dur (Building).</summary>
internal static class ConsoleHeaderRow
{
    public static ProjectRowViewModel For(string name, ProjectRowState state, bool inCycle = false,
        IReadOnlyList<string>? depIssues = null, string namePrefix = "") =>
        new(name, name, state) { InCycle = inCycle, DepIssues = depIssues, NamePrefix = namePrefix };
}
