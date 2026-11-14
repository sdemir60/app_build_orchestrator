using System.Globalization;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>[files-in-use] Kilitli kopya kararının sonucu.</summary>
public enum FilesInUseOutcome
{
    /// <summary>Ayar kapalı: dosya yerinde kaldı, kopya önceki derlemede kaldı.</summary>
    Held,
    /// <summary>Dosya kenara taşındı, yeniden deneme başarılı oldu ve kopya hedefe ulaştı.</summary>
    Replaced,
    /// <summary>Dosya taşınamadı (silmeyi paylaşmayan bir tutamaç, izin): yeniden deneme yapılmadı.</summary>
    CannotMove,
    /// <summary>Dosya taşındı ama yeniden deneme başarısız oldu ya da durduruldu; yuvası boş kalan dosya geri kondu.</summary>
    FailedAgain,
}

/// <summary>[files-in-use] Bir hedef klasördeki kilitli kopya dosyaları (yalnız adları), onları tutanlar ve kararın sonucu.</summary>
public sealed record FilesInUseReport(string Folder, IReadOnlyList<string> FileNames, IReadOnlyList<FileHolder> Holders,
    FilesInUseOutcome Outcome);

/// <summary>
/// [files-in-use] <see cref="IMsBuildInvoker"/>'ı saran decorator: projenin post-build kopyası (<c>copy</c>/<c>xcopy</c>) hedefte başka
/// bir programın tuttuğu dosyaya çarptıysa —
/// <list type="number">
/// <item>denemenin çıktısından kopya komutlarını okur (<see cref="CopyCommandCollector"/>),</item>
/// <item>komutun kopyaladığı ve hedefe ULAŞMAMIŞ dosyalardan yazmak için açılamayanları seçer (<see cref="CopyTargets.Landed"/>,
/// <see cref="FileLock.Probe"/> = <see cref="FileLockState.Locked"/>) — exit kodu ne olursa olsun: iki satırlı bir olayın ilk satırı
/// düşüp son satırı geçince MSBuild hata vermez,</item>
/// <item>tutanları bulur (<see cref="FileHolders"/>, yalnız not için),</item>
/// <item><paramref name="replace"/> kapalıysa raporlar (<see cref="FilesInUseOutcome.Held"/>) ve sonucu AYNEN döner; açıksa dosyaları
/// <see cref="InUseStash"/> ile kenara taşır ve AYNI isteği BİR kez daha koşturur — derleyici atlanır, yalnız kopya yeniden yapılır.</item>
/// </list>
/// Yeniden deneme başarılı olup taşınan her dosya hedefe ulaştıysa taşınanlar <c>.old</c> olur (<see cref="FilesInUseOutcome.Replaced"/>);
/// olmadıysa yuvası boş kalanlar geri konur (<see cref="FilesInUseOutcome.FailedAgain"/>). Bir dosya bile taşınamazsa yeniden deneme o
/// dosyada yine düşeceği için yapılmaz, taşınanlar geri konur (<see cref="FilesInUseOutcome.CannotMove"/>).
/// <para>Dokunulmayanlar: zaman aşımı ya da iptal ile biten deneme, <c>-t:Clean</c>, restore (doğrudan iç invoker'a gider), kilitsiz bir
/// sebeple ulaşmamış kopya (salt okunur hedef, izin) ve komutun kopyalamadığı her dosya. Her denemenin her satırı çağırana akar; süre iki
/// denemenin toplamıdır.</para>
/// </summary>
/// <param name="onReport">Hedef klasör başına bir rapor — çağıran (koordinatör) onu <see cref="FilesInUseNotes.Line"/> ile yazar.</param>
/// <param name="findHolders">[test dikişi] Tutanları bulan; varsayılan <see cref="FileHolders.Find"/> (<see cref="FileHolders.DefaultTimeout"/>).</param>
public sealed class InUseCopyInvoker(
    IMsBuildInvoker inner,
    InUseStash stash,
    bool replace,
    Action<MsBuildInvokeRequest, FilesInUseReport> onReport,
    Func<IReadOnlyList<string>, IReadOnlyList<FileHolder>>? findHolders = null) : IMsBuildInvoker
{
    private readonly IMsBuildInvoker _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly InUseStash _stash = stash ?? throw new ArgumentNullException(nameof(stash));
    private readonly Action<MsBuildInvokeRequest, FilesInUseReport> _onReport = onReport ?? throw new ArgumentNullException(nameof(onReport));
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<FileHolder>> _findHolders =
        findHolders ?? (paths => FileHolders.Find(paths, FileHolders.DefaultTimeout));

    public Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct) =>
        _inner.RestoreAsync(req, onLine, ct);

    public async Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(onLine);

        var collector = new CopyCommandCollector();
        var first = await _inner.InvokeAsync(req, line =>
        {
            onLine(line); // satır ÖNCE çağırana — tespit akışı hiç geciktirmez
            collector.Observe(line);
        }, ct);
        if (req.Target == MsBuildTarget.Clean || first.TimedOut || first.Killed || ct.IsCancellationRequested) return first;

        var commands = collector.Commands;
        if (commands.Count == 0) return first;
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(req.ProjectId)) ?? "";
        var locked = CopyTargets.Resolve(commands, workingDirectory)
            .Where(t => !CopyTargets.Landed(t) && FileLock.Probe(t.Destination) == FileLockState.Locked)
            .ToList();
        if (locked.Count == 0) return first;

        var holdersByFolder = locked.GroupBy(FolderOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => _findHolders([.. g.Select(t => t.Destination)]), StringComparer.OrdinalIgnoreCase);
        if (!replace)
        {
            Report(req, locked, holdersByFolder, FilesInUseOutcome.Held);
            return first;
        }

        var moved = new List<(CopyTarget Target, StashedFile File)>();
        var stuck = new List<CopyTarget>();
        foreach (var target in locked)
        {
            if (_stash.MoveAside(target.Destination) is { } file) moved.Add((target, file));
            else stuck.Add(target);
        }
        if (stuck.Count > 0)
        {
            foreach (var (_, file) in moved) _stash.Restore(file);
            Report(req, stuck, holdersByFolder, FilesInUseOutcome.CannotMove);
            return first;
        }

        var second = await _inner.InvokeAsync(req, onLine, ct);
        bool replaced = second is { ExitCode: 0, TimedOut: false, Killed: false } && moved.All(m => CopyTargets.Landed(m.Target));
        foreach (var (_, file) in moved)
        {
            if (replaced) _stash.Settle(file);
            else _stash.Restore(file);
        }
        Report(req, locked, holdersByFolder, replaced ? FilesInUseOutcome.Replaced : FilesInUseOutcome.FailedAgain);
        return second with { DurationMs = first.DurationMs + second.DurationMs };
    }

    private void Report(MsBuildInvokeRequest req, IEnumerable<CopyTarget> targets,
        IReadOnlyDictionary<string, IReadOnlyList<FileHolder>> holdersByFolder, FilesInUseOutcome outcome)
    {
        foreach (var group in targets.GroupBy(FolderOf, StringComparer.OrdinalIgnoreCase))
        {
            _onReport(req, new FilesInUseReport(group.Key, [.. group.Select(t => Path.GetFileName(t.Destination))],
                holdersByFolder.TryGetValue(group.Key, out var holders) ? holders : [], outcome));
        }
    }

    private static string FolderOf(CopyTarget target) => Path.GetDirectoryName(target.Destination) ?? target.Destination;
}

/// <summary>
/// [files-in-use] Kilitli kopya notunun TEK metin kaynağı: motor <c>decision.log</c>'a ve App'in konsoluna giden satırı buradan yazar;
/// ayarın etiketi (<see cref="SettingLabel"/>) Settings → General kataloğunun satırının da etiketidir (kopya YASAK). Uyarı satırları
/// konsolun amber kuralı için <c>warning: </c> ile başlar.
/// </summary>
public static class FilesInUseNotes
{
    /// <summary>Settings → General → BUILD satırının etiketi.</summary>
    public const string SettingLabel = "Replace files in use";

    private const string WarningPrefix = "warning: ";

    /// <summary>Koşu başı süpürmesinin <c>decision.log</c> satırı (<see cref="InUseStash.Sweep"/>).</summary>
    public static string SweepLine(StashSweep sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        return string.Format(CultureInfo.InvariantCulture,
            "files in use: {0} removed, {1} put back, {2} still held", sweep.Removed, sweep.Restored, sweep.Kept);
    }

    public static string Line(string projectName, FilesInUseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string files = string.Join(", ", report.FileNames);
        bool one = report.FileNames.Count == 1;
        string holders = report.Holders.Count == 0
            ? "another program"
            : string.Join(", ", report.Holders.Select(h => string.Format(CultureInfo.InvariantCulture, "{0} (PID {1})", h.Name, h.ProcessId)));
        string subject = $"{projectName}: {files} in {report.Folder}";
        return report.Outcome switch
        {
            FilesInUseOutcome.Held => WarningPrefix + $"{subject} {(one ? "is" : "are")} held by {holders} — the copy kept the previous build. "
                + $"Turn on {SettingLabel} in Settings to build while it runs",
            FilesInUseOutcome.Replaced => $"{subject} {(one ? "was" : "were")} held by {holders} — moved aside and copied again; "
                + "the running program keeps the previous build until it restarts",
            FilesInUseOutcome.CannotMove => WarningPrefix + $"{subject} {(one ? "is" : "are")} held by {holders} and cannot be moved aside "
                + "— close it and build again",
            FilesInUseOutcome.FailedAgain => WarningPrefix + $"{subject} {(one ? "was" : "were")} held by {holders} — moved aside, "
                + "but the copy failed again",
            _ => throw new ArgumentOutOfRangeException(nameof(report), report.Outcome, null),
        };
    }
}
