using System.Globalization;
using System.Text;

namespace BuildOrchestrator.Core.Logs;

/// <summary>
/// [T5/D4] Bir run'ın tüm disk logu: `<logsRoot>\run-<ts>\` altında proje-başına log + decision.log.
/// Proje logunu proje-başına TEK worker yazar (scheduler garantisi); decision.log çok worker'dan yazılır.
/// </summary>
public sealed class RunLogWriter : IDisposable
{
    private readonly Dictionary<string, ProjectLogFile> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly StreamWriter _decision;
    private readonly Lock _decisionGate = new();
    private readonly Lock _projectsGate = new();
    private bool _disposed;

    public string RunDirectory { get; }

    public RunLogWriter(string logsRoot, DateTimeOffset startedAt)
    {
        RunDirectory = Path.Combine(logsRoot, RunLogPaths.RunDirName(startedAt));
        Directory.CreateDirectory(RunDirectory);
        _decision = new StreamWriter(Path.Combine(RunDirectory, "decision.log"), append: true) { AutoFlush = true };
    }

    public string ProjectLogPath(string projectId) => Path.Combine(RunDirectory, ProjectLogNaming.FileNameFor(projectId));

    /// <summary>Projenin log dosyasını sıfırdan açar (rebuild = taze log). Çağıran Dispose etmelidir.</summary>
    public ProjectLogFile OpenProjectLog(string projectId)
    {
        var file = new ProjectLogFile(ProjectLogPath(projectId));
        lock (_projectsGate) _projects[projectId] = file;
        return file;
    }

    /// <summary>Log metni + o ana kadar diske yazılmış satır sayısı — ATOMİK (yazıcı ile aynı kilit). [T28 dikişi]</summary>
    public (string Text, int ThroughLineNumber)? SnapshotProjectLog(string projectId)
    {
        ProjectLogFile? file;
        lock (_projectsGate) _projects.TryGetValue(projectId, out file);
        if (file is not null) return file.Snapshot();
        string path = ProjectLogPath(projectId);
        if (!File.Exists(path)) return null;
        string text = File.ReadAllText(path);
        return (text, CountLines(text));
    }

    public void AppendDecision(string line)
    {
        lock (_decisionGate)
            _decision.WriteLine(DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line);
    }

    internal static int CountLines(string text)
    {
        int n = 0;
        foreach (char c in text) if (c == '\n') n++;
        return n;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_projectsGate) { foreach (var f in _projects.Values) f.Dispose(); _projects.Clear(); }
        lock (_decisionGate) _decision.Dispose();
    }
}

/// <summary>Tek projenin disk logu. Tek yazıcı + snapshot okuyucuları aynı kilidi paylaşır.</summary>
public sealed class ProjectLogFile : IDisposable
{
    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();
    private int _lineNumber;
    private bool _disposed;

    internal ProjectLogFile(string path)
    {
        _path = path;
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    /// <returns>Yazılan satırın 1-tabanlı numarası (IPC `projectLog.lineNumber`).</returns>
    public int AppendLine(string text)
    {
        lock (_gate)
        {
            if (_disposed) return _lineNumber;
            _writer.WriteLine(text);
            return ++_lineNumber;
        }
    }

    internal (string Text, int ThroughLineNumber) Snapshot()
    {
        lock (_gate)
        {
            if (!_disposed) _writer.Flush();
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            return (reader.ReadToEnd(), _lineNumber);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
