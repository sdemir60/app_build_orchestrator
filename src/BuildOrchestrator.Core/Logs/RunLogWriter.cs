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

    /// <summary>
    /// Projenin log dosyasını sıfırdan açar (rebuild = taze log). Çağıran obligasyonu: dönen <see cref="ProjectLogFile"/>
    /// yalnızca o projenin invoke'u TAMAMEN bitince Dispose edilmeli — bu <see cref="RunLogWriter"/> ise ancak TÜM
    /// worker'lar join olduktan sonra Dispose edilmeli (bkz. Task 9 dispatch; <see cref="ProjectLogFile.AppendLine"/>
    /// dokümanına bakınız). Önceki dosya Dispose edilmeden aynı projectId ile tekrar çağrılırsa, dosya hâlâ elde
    /// tutulduğu için share-mode çakışması IOException fırlatır — bilinçli fail-fast, kaza değil.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Bu <see cref="RunLogWriter"/> zaten Dispose edilmişse.</exception>
    public ProjectLogFile OpenProjectLog(string projectId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var file = new ProjectLogFile(ProjectLogPath(projectId));
        lock (_projectsGate) _projects[projectId] = file;
        return file;
    }

    /// <summary>
    /// Log metni + o ana kadar diske yazılmış satır sayısı — ATOMİK (yazıcı ile aynı kilit). [T28 dikişi]
    /// Proje hâlâ BU writer'da kayıtlıysa canlı <see cref="ProjectLogFile.Snapshot"/> kullanılır.
    /// Kayıtlı değilse diskten doğrudan okunur (satır sayısı '\n' sayılarak — bkz. <see cref="AppendLine"/>'ın
    /// tek-satır-tek-çağrı garantisi, bu yüzden iki dal aynı sonucu üretir). <see cref="ProjectLogFile.Dispose"/>
    /// bu sözlükten kayıt SİLMEZ; disk dalı yalnızca run bitip bu <see cref="RunLogWriter"/> Dispose edildikten
    /// sonra, AYNI run dizini üzerinde açılan TAZE bir <see cref="RunLogWriter"/> örneğinden erişilince devreye
    /// girer (kullanıcı run bittikten sonra bir proje kartına tıklayıp logunu ister).
    /// </summary>
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

    /// <exception cref="ObjectDisposedException">Bu <see cref="RunLogWriter"/> zaten Dispose edilmişse.</exception>
    public void AppendDecision(string line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_decisionGate)
            _decision.WriteLine(DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + SanitizeLine(line));
    }

    internal static int CountLines(string text)
    {
        int n = 0;
        foreach (char c in text) if (c == '\n') n++;
        return n;
    }

    /// <summary>Gömülü CR/LF'i tek boşlukla değiştirir: bir <c>AppendLine</c>/<c>AppendDecision</c> çağrısı == tam olarak bir fiziksel satır (tüketici modeli '\n' ile böler — bkz. <c>LogChunker</c>). Asla fırlatmaz; garip bir MSBuild satırı build'i öldürmemeli.
    /// <para>public: Supervisor (Task 9) canlı <c>projectLog</c> olayına diske YAZILAN satırın AYNISINI koymak için
    /// aynı dönüşümü uygular — kopyalanmış ikinci bir sanitizer, canlı akış ile disk logunu sessizce ayrıştırırdı
    /// (T28 dikişi satır numarası ⇄ metin eşleşmesine dayanır).</para></summary>
    public static string SanitizeLine(string text) =>
        text.IndexOfAny(['\r', '\n']) < 0 ? text : text.Replace('\r', ' ').Replace('\n', ' ');

    public void Dispose()
    {
        lock (_projectsGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var f in _projects.Values) f.Dispose();
            _projects.Clear();
        }
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

    /// <summary>
    /// Satırı diske yazar. ÇAĞIRAN OBLİGASYONU: bu dosyayı sahiplenen worker, ilgili projenin invoke'u
    /// TAMAMEN bitmeden bu nesneyi Dispose ETMEMELİDİR (<see cref="RunLogWriter.Dispose"/> da ancak tüm
    /// worker'lar join olduktan sonra çalışır — bkz. Task 9 dispatch). Metin içinde gömülü CR/LF varsa
    /// tek boşlukla değiştirilir: bir çağrı == tam olarak bir fiziksel satır (bkz. <see cref="RunLogWriter.SanitizeLine"/>).
    /// </summary>
    /// <returns>Yazılan satırın 1-tabanlı numarası (IPC `projectLog.lineNumber`).</returns>
    /// <exception cref="ObjectDisposedException">Dosya zaten Dispose edilmişse — satırı sessizce düşürmek yerine
    /// fail-fast eder ki çağıran "yazıldı" ile "düşürüldü" durumunu ayırt edebilsin.</exception>
    public int AppendLine(string text)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(RunLogWriter.SanitizeLine(text));
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
