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
/// The right-hand console (Section 7): ring buffer, errors-only/full toggle, and optional
/// per-project scoping.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxLines = 5000;

    private readonly List<ConsoleLine> _all = new();
    private readonly object _sync = new();

    /// <summary>Lines currently visible after filtering (bound to the UI).</summary>
    public ObservableCollection<ConsoleLine> Lines { get; } = new();

    /// <summary>False = errors only (default), true = full log.</summary>
    [ObservableProperty]
    private bool _showFullLog;

    /// <summary>When set, only this project's output is shown.</summary>
    [ObservableProperty]
    private string? _focusedProjectId;

    partial void OnShowFullLogChanged(bool value) => Rebuild();
    partial void OnFocusedProjectIdChanged(string? value) => Rebuild();

    public void Append(ConsoleLine line)
    {
        lock (_sync)
        {
            _all.Add(line);
            if (_all.Count > MaxLines)
                _all.RemoveRange(0, _all.Count - MaxLines);
        }

        if (Passes(line))
        {
            Lines.Add(line);
            if (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
        }
    }

    public void Clear()
    {
        lock (_sync)
            _all.Clear();
        Lines.Clear();
    }

    private bool Passes(ConsoleLine line)
    {
        // A card is selected: show that project's full, detailed output (VS-style).
        if (FocusedProjectId != null)
            return line.ProjectId == FocusedProjectId;
        // "Tümü" mode: only the concise summaries (building / built / result), unless the user
        // explicitly turned on the full log.
        if (ShowFullLog)
            return true;
        return line.IsHeader || line.IsError;
    }

    private void Rebuild()
    {
        Lines.Clear();
        List<ConsoleLine> snapshot;
        lock (_sync)
            snapshot = _all.ToList();
        foreach (var line in snapshot)
            if (Passes(line))
                Lines.Add(line);
    }
}
