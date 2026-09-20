using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [Faz 3 — spec 2026-09-18 §5.1] Bir projenin çıktı kanıtları: <paramref name="Evidence"/> kendi derleme çıktısı
/// (<see cref="EvaluatedProject.OutputFileFor"/>), <paramref name="FedCandidates"/> bağımlılarının HintPath ile
/// gösterdiği, aynı dosya adlı kopyalar — "beslenen çıktı" adayları. Hangilerinin gerçekten beslendiği derlemeden
/// sonra öğrenilir (<see cref="OutputEvidence.LearnFedOutputs"/>).
/// </summary>
public sealed record ProjectOutputs(string Evidence, IReadOnlyList<string> FedCandidates);

/// <summary>[§5.2] Çıktı kimin: kanıtsız (yol türetilemedi — bugünkü karar), aracın (defter) ya da başkasının (zaman).</summary>
public enum EvidenceMode { None, Ledger, Time }

/// <summary>
/// [§5.4] Zaman kipinin hükmü, değerlendirme sırasıyla: kanıt yok → kendi girdisi (dosya/klasör) yeni → yalnız
/// kendi HintPath hedefi yeni → beslenen kopya bozuk → taze.
/// </summary>
public enum TimeVerdict { Fresh, Missing, OwnNewer, DependencyNewer, FedBroken }

/// <summary>
/// Bir projenin kanıt kontrolü. <paramref name="EvidenceMissing"/> derleme kanıtının diskte olmadığını,
/// <paramref name="FedIntact"/> öğrenilmiş beslenen kopyaların sağlam olduğunu söyler (kanıt yoksa anlamsızdır ve
/// <c>true</c> raporlanır). <paramref name="Time"/> yalnız zaman kipinde dolar. <paramref name="EvidenceAt"/> kanıt
/// dosyasının zamanıdır (UTC), yoksa <c>null</c>.
/// </summary>
public sealed record OutputCheck(
    EvidenceMode Mode, bool EvidenceMissing, bool FedIntact, TimeVerdict? Time, DateTimeOffset? EvidenceAt);

/// <summary>
/// [Faz 3/Task 3 — spec 2026-09-18 §5] Derleme çıktısının kanıtı ve iki kip. Saf: yalnız dosya sistemini okur
/// (zaman ve boyut), hiçbir şey yazmaz; kararı <c>WillBuildEvaluator</c> ve önizleme bu sonuçtan verir.
///
/// <para><b>Kip (§5.2).</b> Kanıt yolu biliniyor ∧ (kayıt yok ∨ <c>LastRunAt</c> yok ∨ kanıt dosyası var ve
/// zamanı <c>LastRunAt</c>'tan KESİN yeni) ⇒ zaman kipi; kanıt yolu biliniyorsa geri kalan her durumda defter
/// kipi; yol bilinmiyorsa kanıtsız. Aracın kendi çıktısı her zaman <c>LastRunAt</c>'tan eskidir (defter MSBuild
/// bittikten sonra yazılır) — bu yüzden çökme kurtarmasının yazdığı <c>LastRunAt=şimdi</c> yarım çıktıyı zaman
/// kipine sokmaz (§5.5).</para>
///
/// <para><b>Karşılaştırma.</b> "Yeni" kesin büyüktür: girdiye EŞİT kanıt tazedir. Okunamayan ya da olmayan girdi
/// ve HintPath hedefi yok sayılır. <b>Maliyet:</b> defter kipinde yalnız derleme kanıtı ve beslenen kopyalar stat
/// edilir; girdi zamanları yalnız zaman kontrolünde (<see cref="TimeCheck"/>) okunur.</para>
/// </summary>
public static class OutputEvidence
{
    /// <summary>[§5.1] Beslenen kopyanın derleme kanıtıyla "aynı an" sayıldığı pencere — kopya komutu zamanı
    /// korur ama dosya sistemleri arası çözünürlük farkı olabilir. Öğrenmenin TEK eşiği.</summary>
    public static readonly TimeSpan FedOutputWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Projenin kanıt yolları. Proje yoksa ya da kanıt yolu türetilemiyorsa (SDK-style, belirsiz OutputPath)
    /// <c>null</c> — kanıtsız. Adaylar: <paramref name="dependents"/>'ın (grafta bu projeye bağlı olanların)
    /// HintPath hedeflerinden dosya adı kanıtınkiyle aynı olanlar (büyük/küçük harf duyarsız), tekil ve sıralı.
    /// Belirsiz üreticide (aynı DLL adını iki proje üretiyor) graf kenarı düşer, dolayısıyla aday da yoktur (§7-44).
    /// </summary>
    public static ProjectOutputs? Locate(
        EvaluatedProject? project, string configuration, IEnumerable<EvaluatedProject> dependents)
    {
        ArgumentNullException.ThrowIfNull(dependents);
        string? evidence = project?.OutputFileFor(configuration);
        if (evidence is null) return null;

        string fileName = Path.GetFileName(evidence);
        var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependent in dependents)
            foreach (string target in dependent.HintPathTargets())
                if (string.Equals(Path.GetFileName(target), fileName, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(target);
        return new ProjectOutputs(evidence, [.. candidates]);
    }

    /// <summary>
    /// Kipi belirler ve kontrolü yapar. Zaman kipinde gövde <see cref="TimeCheck"/>'tir (tek gövde); defter
    /// kipinde yalnız kanıtın varlığı ve beslenen kopyaların sağlamlığı raporlanır — hüküm defterden (imza) gelir.
    /// </summary>
    public static OutputCheck Inspect(
        ProjectOutputs? outputs, BuildState? state, IReadOnlyList<string> inputFiles,
        IReadOnlyList<string> inputFolders, IReadOnlyList<string> hintTargets)
    {
        if (outputs is null) return NoEvidence;

        var evidence = StatFile(outputs.Evidence);
        bool timeMode = state?.LastRunAt is not { } lastRun
            || (evidence is { } e && e.At > lastRun.UtcDateTime);
        if (timeMode) return TimeCheck(outputs, state, inputFiles, inputFolders, hintTargets);

        return new OutputCheck(
            EvidenceMode.Ledger, evidence is null, evidence is not { } ev || FedIntact(state, ev), null, AtOf(evidence));
    }

    /// <summary>
    /// Zaman kipinin gövdesi (§5.4): kanıt yok → <see cref="TimeVerdict.Missing"/>; kendi girdi dosyası ya da
    /// taranan klasörü kanıttan kesin yeni → <see cref="TimeVerdict.OwnNewer"/>; var olan bir HintPath hedefi
    /// kesin yeni → <see cref="TimeVerdict.DependencyNewer"/>; öğrenilmiş beslenen kopya bozuk →
    /// <see cref="TimeVerdict.FedBroken"/>; aksi <see cref="TimeVerdict.Fresh"/>. Döngü grubu (§5.6) defter
    /// kipindeki üyeleri de buradan geçirir (<see cref="ApplyCycleGroups"/>). Kanıt yolu yoksa kanıtsız döner.
    /// </summary>
    public static OutputCheck TimeCheck(
        ProjectOutputs? outputs, BuildState? state, IReadOnlyList<string> inputFiles,
        IReadOnlyList<string> inputFolders, IReadOnlyList<string> hintTargets)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        ArgumentNullException.ThrowIfNull(inputFolders);
        ArgumentNullException.ThrowIfNull(hintTargets);
        if (outputs is null) return NoEvidence;

        if (StatFile(outputs.Evidence) is not { } evidence)
            return new OutputCheck(EvidenceMode.Time, true, true, TimeVerdict.Missing, null);

        bool fedIntact = FedIntact(state, evidence);
        TimeVerdict verdict =
            inputFiles.Any(f => FileTime(f) > evidence.At) || inputFolders.Any(d => FolderTime(d) > evidence.At)
                ? TimeVerdict.OwnNewer
            : hintTargets.Any(h => FileTime(h) > evidence.At) ? TimeVerdict.DependencyNewer
            : !fedIntact ? TimeVerdict.FedBroken
            : TimeVerdict.Fresh;
        return new OutputCheck(EvidenceMode.Time, false, fedIntact, verdict, AtOf(evidence));
    }

    /// <summary>
    /// [§5.6] Döngü grupları: grupta zaman kipinde bir üye varsa TÜM üyeler zaman kontrolünden
    /// (<paramref name="timeCheckOf"/> — <see cref="TimeCheck"/>) geçer. Hepsi tazeyse hepsi kendi (taze)
    /// kontrolünü alır; değilse kendi kontrolünü geçemeyen üye kendi hükmünü, geçen üye
    /// <see cref="TimeVerdict.DependencyNewer"/> alır — grup bayat kalır. Hiçbir üyesi zaman kipinde olmayan grup
    /// ve döngü dışı girişler olduğu gibi geçer.
    /// </summary>
    public static IReadOnlyDictionary<string, OutputCheck> ApplyCycleGroups(
        IReadOnlyDictionary<string, OutputCheck> checks, IReadOnlyList<IReadOnlyList<string>> cycles,
        Func<string, OutputCheck> timeCheckOf)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(timeCheckOf);

        var result = new Dictionary<string, OutputCheck>(checks, StringComparer.OrdinalIgnoreCase);
        foreach (var cycle in cycles)
        {
            bool anyTime = cycle.Any(id => checks.TryGetValue(id, out var c) && c.Mode == EvidenceMode.Time);
            if (!anyTime) continue;

            var own = cycle.ToDictionary(id => id, timeCheckOf, StringComparer.OrdinalIgnoreCase);
            bool allFresh = own.Values.All(c => c.Time == TimeVerdict.Fresh);
            foreach (var (id, check) in own)
                result[id] = allFresh || check.Time != TimeVerdict.Fresh
                    ? check
                    : check with { Mode = EvidenceMode.Time, Time = TimeVerdict.DependencyNewer };
        }
        return result;
    }

    /// <summary>
    /// [§5.1] Başarılı derlemeden sonra: adaylardan var olan, boyutu derleme kanıtına eşit ve zamanı ondan en çok
    /// <see cref="FedOutputWindow"/> farklı olanlar "beslenen" sayılır. Kanıt yoksa (ya da yol yoksa) <c>null</c>;
    /// kanıt var ama hiçbiri uymuyorsa boş liste. Repo'ya check-in edilmiş bir kopya hiç beslenmez, öğrenilmez.
    /// </summary>
    public static IReadOnlyList<string>? LearnFedOutputs(ProjectOutputs? outputs)
    {
        if (outputs is null || StatFile(outputs.Evidence) is not { } evidence) return null;
        return
        [
            .. outputs.FedCandidates.Where(candidate =>
                StatFile(candidate) is { } fed
                && fed.Length == evidence.Length
                && (fed.At - evidence.At).Duration() <= FedOutputWindow),
        ];
    }

    /// <summary>
    /// "Kendi dosyaları değişti mi" (<c>modified</c> ↔ <c>affected</c>) — Sync ve koşu önizlemesinin TEK cevabı:
    /// zaman kipinde kanıttan (<see cref="TimeVerdict.OwnNewer"/>), diğer kiplerde defterin cevabı.
    /// </summary>
    public static bool? OwnFilesChanged(OutputCheck? check, bool? ledgerAnswer) =>
        check?.Mode == EvidenceMode.Time ? check.Time == TimeVerdict.OwnNewer : ledgerAnswer;

    /// <summary>
    /// Önizleme satırının <c>OwnFilesChanged</c>'ı — Sync ve koşu önizlemesinin çağırdığı TEK bileşim: defterin
    /// cevabı kayıttaki içerik özeti ile bugünkünün karşılaştırmasıdır (<see cref="BuildStateStore.OwnFilesChanged"/>),
    /// zaman kipinde kanıt onu ezer (<see cref="OwnFilesChanged(OutputCheck?, bool?)"/>).
    /// </summary>
    public static bool? OwnFilesChanged(
        OutputCheck? check, IReadOnlyDictionary<string, BuildState>? state, string projectId, string? currentContent) =>
        OwnFilesChanged(check, BuildStateStore.OwnFilesChanged(state, projectId, currentContent));

    /// <summary>
    /// Bu araç dışında derlenmiş çıktının kanıt zamanı — yalnız son gerekçe
    /// <see cref="WillBuildReason.BuiltOutside"/> iken (zaman kipinde ve taze). Gerekçe de okunur çünkü taze
    /// bir kontrol tek başına yetmez: kirli bir upstream'in arkasındaki düğüm (<c>IncrementalPlanner</c>,
    /// §5.4 son cümle) kontrolü taze olsa da <c>OutputStale</c> ile derlenir ve böyle bir zamanı yoktur.
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Bu değeri satırın "built outside this tool
    /// 5m ago" yaşı okurdu — ve o yaş YANILTICIydı, kalktı. Önizleme alanını
    /// (<c>BuildPreviewItem.OutputBuiltAt</c>) beslemeye devam eder; bugün okuyucusu YOK.</para>
    /// </summary>
    public static DateTimeOffset? OutputBuiltAt(OutputCheck? check, WillBuildReason? reason) =>
        reason == WillBuildReason.BuiltOutside && check is { Mode: EvidenceMode.Time, Time: TimeVerdict.Fresh }
            ? check.EvidenceAt : null;

    private static readonly OutputCheck NoEvidence = new(EvidenceMode.None, false, true, null, null);

    private readonly record struct FileStat(long Length, DateTime At);

    /// <summary>
    /// Öğrenilmiş her beslenen kopya var ∧ boyutu kanıta eşit ∧ zamanı kanıttan eski DEĞİL (yeni ve aynı boyutlu
    /// kopya sağlamdır). Liste yoksa sağlam.
    /// </summary>
    private static bool FedIntact(BuildState? state, FileStat evidence) =>
        (state?.FedOutputs ?? []).All(path =>
            StatFile(path) is { } fed && fed.Length == evidence.Length && fed.At >= evidence.At);

    /// <summary>Dosyanın boyutu ve UTC zamanı; yoksa ya da okunamıyorsa <c>null</c>.</summary>
    private static FileStat? StatFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStat(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>Girdi dosyasının UTC zamanı; yoksa ya da okunamıyorsa <c>null</c> (yok sayılır).</summary>
    private static DateTime? FileTime(string path) => StatFile(path)?.At;

    /// <summary>Taranan klasörün UTC zamanı (silme/yeniden adlandırma onu ilerletir); yoksa <c>null</c>.</summary>
    private static DateTime? FolderTime(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>Okunamayan yolun istisnaları (erişim, geçersiz yol, desteklenmeyen biçim) — dosya ve klasör
    /// okumasının TEK filtresi: böyle bir yol yok sayılır ya da "kanıt yok"tur.</summary>
    private static bool IsUnreadable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static DateTimeOffset? AtOf(FileStat? stat) =>
        stat is { } s ? new DateTimeOffset(DateTime.SpecifyKind(s.At, DateTimeKind.Utc)) : null;
}
