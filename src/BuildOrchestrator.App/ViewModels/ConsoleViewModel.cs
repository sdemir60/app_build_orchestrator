using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// One console line. <paramref name="IsHeader"/> marks status lines (start/finish/skip) that
/// are always shown regardless of the errors-only toggle; <paramref name="IsSuccess"/> tints
/// success headers green.
/// </summary>
public sealed record ConsoleLine(
    string ProjectId, string ProjectName, string Text, bool IsError,
    bool IsHeader = false, bool IsSuccess = false)
{
    /// <summary>Wall-clock time the line was produced (shown as a hh:mm:ss prefix).</summary>
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>Rendered text: time prefix, project name (for non-header lines), then the message.</summary>
    public string Display => IsHeader
        ? $"[{Time:HH:mm:ss}]  {Text}"
        : $"[{Time:HH:mm:ss}]  {ProjectName,-22}  {Text}";
}

/// <summary>
/// The right-hand console (Section 7):
/// <list type="bullet">
/// <item>No card selected — show only our concise summaries (build started / result / duration).</item>
/// <item>A card selected — show that project's full build-tool output (VS-style), success or fail.</item>
/// </list>
/// Full per-project output is kept in its own buffer so selecting any card (not just the last
/// one built) always shows its complete log.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxSummaryLines = 5000;
    private const int MaxLinesPerProject = 50000;

    private readonly List<ConsoleLine> _summaries = new();
    private readonly Dictionary<string, List<ConsoleLine>> _byProject = new();
    private readonly object _sync = new();

    /// <summary>Lines currently visible after filtering (bound to the UI).</summary>
    public ObservableCollection<ConsoleLine> Lines { get; } = new();

    /// <summary>When set, only this project's full output is shown.</summary>
    [ObservableProperty]
    private string? _focusedProjectId;

    partial void OnFocusedProjectIdChanged(string? value) => Rebuild();

    public void Append(ConsoleLine line)
    {
        lock (_sync)
        {
            // Keep the full output per project (never trimmed away by other projects' noise).
            if (!string.IsNullOrEmpty(line.ProjectId))
            {
                if (!_byProject.TryGetValue(line.ProjectId, out var list))
                    _byProject[line.ProjectId] = list = new List<ConsoleLine>();
                list.Add(line);
                if (list.Count > MaxLinesPerProject)
                    list.RemoveRange(0, list.Count - MaxLinesPerProject);
            }

            // Summaries (our headers) drive the default, no-card-selected view.
            if (line.IsHeader)
            {
                _summaries.Add(line);
                if (_summaries.Count > MaxSummaryLines)
                    _summaries.RemoveRange(0, _summaries.Count - MaxSummaryLines);
            }
        }

        if (Passes(line))
            Lines.Add(line);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _summaries.Clear();
            _byProject.Clear();
        }
        Lines.Clear();
    }

    private bool Passes(ConsoleLine line)
    {
        // A card is selected: show that project's full, detailed output (VS-style).
        if (FocusedProjectId != null)
            return line.ProjectId == FocusedProjectId;
        // Default view: only our concise summaries (building / built / result / duration).
        return line.IsHeader;
    }

    private void Rebuild()
    {
        Lines.Clear();
        List<ConsoleLine> snapshot;
        lock (_sync)
        {
            snapshot = FocusedProjectId != null
                ? (_byProject.TryGetValue(FocusedProjectId, out var list) ? list.ToList() : new List<ConsoleLine>())
                : _summaries.ToList();
        }
        foreach (var line in snapshot)
            Lines.Add(line);
    }
}
