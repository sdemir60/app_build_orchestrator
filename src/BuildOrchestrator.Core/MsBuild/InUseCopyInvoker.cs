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
/// <item>denemenin çıktısından kopya komutlarını okur ve bir paylaşım/kilit ihlali görüp görmediğini not eder
/// (<see cref="CopyCommandCollector"/>) — ihlal yoksa hiçbir kopya bir kilide çarpmamıştır ve karar verilmez [son inceleme I2],</item>
/// <item>komutun kopyaladığı dosyalardan hedefte kaynaktan ESKİ kalanları [C1] ve yazmak için açılamayanları seçer
/// (<see cref="CopyTargets.Outdated"/>, <see cref="FileLock.Probe"/> = <see cref="FileLockState.Locked"/>) — exit kodu ne olursa olsun: iki
/// satırlı bir olayın ilk satırı düşüp son satırı geçince MSBuild hata vermez,</item>
/// <item>tutanları bulur (<see cref="FileHolders"/>, yalnız not için),</item>
/// <item><paramref name="replace"/> kapalıysa raporlar (<see cref="FilesInUseOutcome.Held"/>) ve sonucu AYNEN döner; açıksa taşıyıcının tek
/// karar kapısını tutarak [I3] dosyaları yeniden yoklar, hâlâ tutulanları <see cref="InUseStash"/> ile kenara taşır ve AYNI isteği BİR kez
/// daha koşturur — sıradan bir Build'de derleyici atlanır ve kopya yeniden yapılır; satırdan Rebuild'de istek yine Rebuild'dir.</item>
/// </list>
/// Yeniden deneme başarılı olup seçilen her dosya hedefe ulaştıysa taşınanlar <c>.old</c> olur (<see cref="FilesInUseOutcome.Replaced"/>);
/// olmadıysa yuvası boş kalanlar geri konur (<see cref="FilesInUseOutcome.FailedAgain"/>); deneme bir istisnayla biterse de geri konur ve
/// istisna yukarı çıkar [I5]. Bir dosya bile taşınamazsa yeniden deneme o dosyada yine düşeceği için yapılmaz, taşınanlar geri konur
/// (<see cref="FilesInUseOutcome.CannotMove"/>). Kapının altında taşınacak bir şey bulamayan proje (başka bir proje çözmüş) rapor yazmadan
/// yeniden dener.
/// <para>Dokunulmayanlar: zaman aşımı ya da iptal ile biten deneme, <c>-t:Clean</c>, restore (doğrudan iç invoker'a gider), kaynaktan yeni
/// ya da kilitsiz bir sebeple ulaşmamış hedef (salt okunur dosya, izin), göreli yollu komut (<see cref="CopyTargets.Resolve"/>) ve komutun
/// kopyalamadığı her dosya. Her denemenin her satırı çağırana akar; süre iki denemenin toplamıdır.</para>
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
        // [son inceleme I2] Kanıt: denemenin kendi çıktısında bir paylaşım/kilit ihlali. Yoksa hiçbir kopya bir kilide çarpmamıştır —
        // yankılanan ama çalışmayan bir satır, bilerek atlanan bir dosya karar tetiklemez.
        if (!collector.SawSharingViolation || collector.Commands is not { Count: > 0 } commands) return first;

        // Hedefe ulaşmamış kopyalar: kaynaktan ESKİ hedef [C1]. Kilitli olanlar şimdi tutulanlardır; kilitsiz ama eski bir hedef, kilidi
        // denemeden sonra (başka bir proje onu çözdü, program kapandı) kalkmış bir kopyadır — o da yeniden denemeyle oturur.
        var targets = CopyTargets.Resolve(commands);
        var outdated = targets.Where(CopyTargets.Outdated).ToList();
        var locked = outdated.Where(t => FileLock.Probe(t.Destination) == FileLockState.Locked).ToList();
        // Ayar kapalıyken not yalnız şimdi tutulan bir dosya içindir. Açıkken kilidin çözülmüş olması da yeniden denemenin sebebidir: deneme
        // ihlalle düştüyse (exit≠0) hedefler şimdi ulaşmış görünse bile — başka bir proje aynı dosyayı oturttu — bir kez daha denenir.
        if (replace ? outdated.Count == 0 && first.ExitCode == 0 : locked.Count == 0) return first;

        // Tutanlar yalnız not içindir; kapıdan ÖNCE sorulur ki kapı Restart Manager'ın süresini başka projelere bekletmesin.
        var holdersByFolder = locked.GroupBy(FolderOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => _findHolders([.. g.Select(t => t.Destination)]), StringComparer.OrdinalIgnoreCase);
        if (!replace)
        {
            Report(req, locked, holdersByFolder, FilesInUseOutcome.Held);
            return first;
        }

        try
        {
            await _stash.DecisionGate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return first; // hiçbir şey taşınmadı
        }
        try
        {
            // [son inceleme I3] Kapının altında YENİDEN oku: bekleyen sürede başka bir proje aynı dosyayı kenara alıp kendi kopyasını
            // oturtmuş olabilir — o zaman taşınacak bir şey kalmamıştır ve yeniden deneme kendi kopyasını yazar. Hiçbir hedef eski değilse
            // yeniden deneme yalnız ilk deneme düştüyse VE kopyalanan hiçbir dosya şimdi tutulmuyorsa (kilidi başkası çözdü) yapılır: hâlâ
            // tutulan, kaynaktan yeni bir dosya [C1] yeniden denemeyi aynı kilide çarptırırdı.
            outdated = [.. targets.Where(CopyTargets.Outdated)];
            var held = HeldAndOutdated(outdated);
            if (held.Count == 0 && outdated.Count == 0
                && (first.ExitCode == 0 || targets.Any(t => FileLock.Probe(t.Destination) == FileLockState.Locked)))
                return first;
            var moved = new List<StashedFile>();
            var stuck = new List<CopyTarget>();
            foreach (var target in held)
            {
                if (_stash.MoveAside(target.Destination) is { } file) moved.Add(file);
                else stuck.Add(target);
            }
            if (stuck.Count > 0)
            {
                foreach (var file in moved) _stash.Restore(file);
                Report(req, stuck, holdersByFolder, FilesInUseOutcome.CannotMove);
                return first;
            }

            MsBuildInvokeResult second;
            try
            {
                second = await _inner.InvokeAsync(req, onLine, ct);
            }
            catch
            {
                // [son inceleme I5] Yeniden deneme bir istisnayla bitti (ör. contention bekleyişinde hard Stop'un iptali): taşınan her dosya
                // yuvası boşsa yerine konur — yuva bir sonraki koşunun süpürmesine kadar boş kalmaz. İstisna aynen yukarı çıkar.
                foreach (var file in moved) _stash.Restore(file);
                throw;
            }
            bool replaced = second is { ExitCode: 0, TimedOut: false, Killed: false } && outdated.All(CopyTargets.Landed);
            foreach (var file in moved)
            {
                if (replaced) _stash.Settle(file);
                else _stash.Restore(file);
            }
            // Rapor yalnız bu projenin taşıdığı dosyalar içindir: taşınacak bir şey bulamayan proje (başkası çözmüş) sessizce yeniden dener.
            if (moved.Count > 0)
                Report(req, held, holdersByFolder, replaced ? FilesInUseOutcome.Replaced : FilesInUseOutcome.FailedAgain);
            return second with { DurationMs = first.DurationMs + second.DurationMs };
        }
        finally
        {
            _stash.DecisionGate.Release();
        }
    }

    /// <summary>Hâlâ kaynaktan eski [son inceleme C1] ve başka bir programın tuttuğu hedefler.</summary>
    private static List<CopyTarget> HeldAndOutdated(IEnumerable<CopyTarget> targets) =>
        [.. targets.Where(t => CopyTargets.Outdated(t) && FileLock.Probe(t.Destination) == FileLockState.Locked)];

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
/// [files-in-use] Kilitli kopya notunun TEK metin kaynağı: motor <c>decision.log</c>'a ve App'in konsoluna giden ayrıntılı satırı
/// (<see cref="Line"/>) ve olay akışının kısa satırını (<see cref="Brief"/>) buradan yazar; ayarın etiketi (<see cref="SettingLabel"/>)
/// Settings → General kataloğunun satırının da etiketidir (kopya YASAK). Ayrıntılı uyarı satırları konsolun amber kuralı için
/// <c>warning: </c> ile başlar.
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

    /// <summary>Tutan program bulunamadığında (Restart Manager cevap vermedi) notun söylediği ad — iki satırın ortak sözcüğü.</summary>
    private const string UnknownHolder = "another program";

    /// <summary>
    /// Notun olay akışındaki KISA hâli: proje, tutan program(lar)ın adı ve sonuç — <c>{project} in use by {holders} — {result}</c>. Akış bir
    /// zaman çizelgesidir ve satırı dar bir panelde okunur; klasör, dosya adları ve PID ayrıntıdır, <see cref="Line"/>'da (konsol ve
    /// <c>decision.log</c>) durur. İkisi aynı raporu anlatır, ayrıntı seviyeleri farklıdır. Uyarı öneki taşımaz (akışta satırın türü rengi
    /// söyler); aynı programın birden çok örneği tek ad yazar.
    /// </summary>
    public static string Brief(string projectName, FilesInUseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string holders = report.Holders.Count == 0
            ? UnknownHolder
            : string.Join(", ", report.Holders.Select(h => h.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        string result = report.Outcome switch
        {
            FilesInUseOutcome.Held => $"not replaced; turn on {SettingLabel}",
            FilesInUseOutcome.Replaced => "replaced",
            FilesInUseOutcome.CannotMove => "cannot be replaced; close it, then build again",
            FilesInUseOutcome.FailedAgain => "not replaced; the copy failed again",
            _ => throw new ArgumentOutOfRangeException(nameof(report), report.Outcome, null),
        };
        return $"{projectName} in use by {holders} — {result}";
    }

    public static string Line(string projectName, FilesInUseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string files = string.Join(", ", report.FileNames);
        bool one = report.FileNames.Count == 1;
        string holders = report.Holders.Count == 0
            ? UnknownHolder
            : string.Join(", ", report.Holders.Select(h => string.Format(CultureInfo.InvariantCulture, "{0} (PID {1})", h.Name, h.ProcessId)));
        string subject = $"{projectName}: {files} in {report.Folder}";
        return report.Outcome switch
        {
            // [son inceleme I4] "build this project again from its row": son satırı başarılı bir olayda proje başarılı kaydedilir ve
            // sıradan bir Build onu güncel sayıp atlar; satırdan derleme güncel olsa da derler.
            FilesInUseOutcome.Held => WarningPrefix + $"{subject} {(one ? "is" : "are")} held by {holders} — the copy kept the previous build. "
                + $"Turn on {SettingLabel} in Settings, then build this project again from its row",
            FilesInUseOutcome.Replaced => $"{subject} {(one ? "was" : "were")} held by {holders} — moved aside and copied again; "
                + "the running program keeps the previous build until it restarts",
            FilesInUseOutcome.CannotMove => WarningPrefix + $"{subject} {(one ? "is" : "are")} held by {holders} and cannot be moved aside "
                + "— close it, then build this project again from its row",
            FilesInUseOutcome.FailedAgain => WarningPrefix + $"{subject} {(one ? "was" : "were")} held by {holders} — moved aside, "
                + "but the copy failed again",
            _ => throw new ArgumentOutOfRangeException(nameof(report), report.Outcome, null),
        };
    }
}
