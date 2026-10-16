using System.Globalization;
using System.Text;

namespace BuildOrchestrator.Core.Logs;

/// <summary>
/// [T5/D4] Bir run'ın tüm disk logu: <c>&lt;logsRoot&gt;\run-&lt;ts&gt;\</c> altında proje-başına log + decision.log.
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
    /// Log metni + o ana kadar diske yazılmış satır sayısı — ATOMİK (yazıcı ile aynı kilit). [T28 dikişi] İki dal:
    /// <list type="bullet">
    /// <item><b>Canlı dal:</b> proje BU writer'da kayıtlıysa (<see cref="OpenProjectLog"/>; <see cref="ProjectLogFile.Dispose"/> kaydı SİLMEZ)
    /// <see cref="ProjectLogFile.Snapshot"/> okunur — satır sayısı yazıcının sayacıdır.</item>
    /// <item><b>Disk dalı:</b> kayıtlı değilse — koşuda henüz açılmamış bir proje, aynı koşu klasörü üzerinde açılmış TAZE bir writer ya da
    /// [son temizlik · K-1] bu writer Dispose edildikten SONRA: Dispose kaydı temizler, kayıtlı olan proje de artık buradan okunur — dosya
    /// diskten okunur (<see cref="ReadProjectLogFromDisk"/>; satır sayısı '\n' sayılarak — <see cref="ProjectLogFile.AppendLine"/>'ın
    /// tek-satır-tek-çağrı garantisi sayesinde iki dal aynı sonucu üretir).</item>
    /// </list>
    /// <para>[idle-memory · dalga 3 · B1 fix 4] <b>Sözleşme:</b> <c>null</c> yalnız log YOKSA döner (disk dalı: dosya yok ya da okunurken gitti).
    /// Başka her okuma hatası çağırana YÜKSELİR — disk dalının okuma hatası (paylaşım ihlali, erişim reddi…) ve canlı dalda kayıtlı projenin dosyası
    /// gitmişse <see cref="FileNotFoundException"/> (koşu klasörü gittiyse <see cref="DirectoryNotFoundException"/>). Bunları "log yok"a çeviren TEK
    /// yer çağırandır — motorda <c>RunCoordinator.TryGetProjectLogSnapshot</c>.</para>
    /// </summary>
    /// <param name="projectId">Projenin kimliği.</param>
    /// <returns>Logun metni ve satır sayısı; log yoksa <c>null</c>.</returns>
    /// <exception cref="FileNotFoundException">Canlı dal: kayıtlı projenin dosyası diskten gitti.</exception>
    /// <exception cref="DirectoryNotFoundException">Canlı dal: koşu klasörü diskten gitti.</exception>
    /// <exception cref="IOException">Log okunamadı (ör. başka bir süreç dosyayı paylaşımsız tutuyor).</exception>
    /// <exception cref="UnauthorizedAccessException">Erişim reddedildi (ör. logun yerinde, adını taşıyan bir klasör).</exception>
    public (string Text, int ThroughLineNumber)? SnapshotProjectLog(string projectId)
    {
        ProjectLogFile? file;
        lock (_projectsGate) _projects.TryGetValue(projectId, out file);
        return file is not null ? file.Snapshot() : ReadProjectLogFromDisk(RunDirectory, projectId);
    }

    /// <summary>
    /// [T28] Writer'sız, PATH-tabanlı okuma: koşu bitip yazıcısı Dispose edildikten SONRA (motorun son koşusu ya da — karar 37 — isteğin adını verdiği
    /// önceki bir koşunun klasörü; kullanıcı bir proje kartına tıklayınca) ve canlı yazıcının henüz kaydetmediği bir proje için
    /// (<see cref="SnapshotProjectLog"/>'un disk dalı) çağrılır. Yeni bir <see cref="RunLogWriter"/> AÇMAZ — o,
    /// decision.log'u append-mode'da yeniden açardı; yalnız aynı adlandırma kuralıyla (<see cref="ProjectLogNaming"/>)
    /// dosyayı okur. Satır sayısı '\n' sayılarak elde edilir — <see cref="ProjectLogFile.AppendLine"/>'ın
    /// tek-satır-tek-çağrı garantisi sayesinde canlı sayaçla AYNI sonucu verir. <see cref="SnapshotProjectLog"/>'un
    /// disk-fallback dalı da AYNI metodu çağırır — iki dal aynı sonucu üretir, kopya sayım mantığı YOK.
    /// <para>[idle-memory · dalga 3 · B1 fix 3] <b>Yalnız YOK olan log <c>null</c>'dır:</b> dosya yok ya da dosya veya klasörü okunurken gitti
    /// (uyanan motorun açılıştaki saklama süpürmesi — beklenen yarış). Yokluğu okumanın kendisi söyler (<see cref="FileNotFoundException"/>,
    /// <see cref="DirectoryNotFoundException"/>); önceden <c>File.Exists</c> sorulmaz, "hiç yoktu" ile "okunurken gitti" tek daldır. Başka her okuma
    /// hatası (paylaşım ihlali, erişim reddi…) çağırana YÜKSELİR: "okunamadı" "yok" değildir — motorda onu "log yok"a çeviren ve iz bırakan TEK yer
    /// koordinatördür (<c>RunCoordinator.TryGetProjectLogSnapshot</c>).</para>
    /// </summary>
    /// <param name="runDirectory">Koşu klasörü.</param>
    /// <param name="projectId">Projenin kimliği.</param>
    /// <returns>Logun metni ve satır sayısı; log yoksa <c>null</c>.</returns>
    /// <exception cref="IOException">Log okunamadı (ör. başka bir süreç dosyayı paylaşımsız tutuyor) — yokluk hariç.</exception>
    /// <exception cref="UnauthorizedAccessException">Erişim reddedildi (ör. logun yerinde, adını taşıyan bir klasör).</exception>
    public static (string Text, int ThroughLineNumber)? ReadProjectLogFromDisk(string runDirectory, string projectId)
    {
        string path = Path.Combine(runDirectory, ProjectLogNaming.FileNameFor(projectId));
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; } // yok — ya da okunurken gitti
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

    /// <summary>Canlı anlık görüntü: önce yazıcıyı boşaltır, sonra dosyayı okur — metin ve yazıcının sayacı aynı kilit altında.</summary>
    /// <exception cref="FileNotFoundException">Dosya diskten gitti (yazıcısı kapandıktan sonra silindi).</exception>
    /// <exception cref="DirectoryNotFoundException">Koşu klasörü diskten gitti.</exception>
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
