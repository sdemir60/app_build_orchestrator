using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

public sealed record ConsoleEntry(string ProjectId, string Text, bool IsError);

/// <summary>
/// Console output model (Section 7): a bounded ring buffer with two live filters — errors-only
/// (default) and an optional single-project focus. The visible collection is what the UI binds to;
/// auto-follow scrolling is handled by a view behavior. All methods must be called on the UI thread.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private readonly LinkedList<ConsoleEntry> _buffer = new();
    private readonly int _maxLines;

    public ConsoleViewModel(int maxLines = 20000)
    {
        _maxLines = Math.Max(1000, maxLines);
    }

    /// <summary>The currently visible (filtered) lines bound by the console view.</summary>
    public ObservableCollection<ConsoleEntry> Visible { get; } = new();

    [ObservableProperty]
    private bool _errorsOnly = true; // Section 7 default

    /// <summary>When set, only this project's output is shown; null shows all.</summary>
    [ObservableProperty]
    private string? _focusedProjectId;

    partial void OnErrorsOnlyChanged(bool value) => Rebuild();
    partial void OnFocusedProjectIdChanged(string? value) => Rebuild();

    public void Append(ConsoleEntry entry)
    {
        _buffer.AddLast(entry);
        if (_buffer.Count > _maxLines)
        {
            _buffer.RemoveFirst();
            if (Visible.Count > 0 && Passes(Visible[0]))
            {
                // Keep the visible collection roughly bounded too.
                if (Visible.Count >= _maxLines)
                {
                    Visible.RemoveAt(0);
                }
            }
        }

        if (Passes(entry))
        {
            Visible.Add(entry);
            if (Visible.Count > _maxLines)
            {
                Visible.RemoveAt(0);
            }
        }
    }

    public void Clear()
    {
        _buffer.Clear();
        Visible.Clear();
    }

    private bool Passes(ConsoleEntry e)
    {
        if (ErrorsOnly && !e.IsError)
        {
            return false;
        }
        if (FocusedProjectId is not null && !string.Equals(e.ProjectId, FocusedProjectId, StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private void Rebuild()
    {
        Visible.Clear();
        foreach (var e in _buffer)
        {
            if (Passes(e))
            {
                Visible.Add(e);
            }
        }
    }

    /// <summary>Toggle focus: clicking a failed card focuses it; clicking again clears (Section 7).</summary>
    public void ToggleFocus(string projectId)
        => FocusedProjectId = string.Equals(FocusedProjectId, projectId, StringComparison.Ordinal) ? null : projectId;
}
