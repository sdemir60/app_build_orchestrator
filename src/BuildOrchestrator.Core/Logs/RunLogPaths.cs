using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BuildOrchestrator.Core.Logs;

/// <summary>[D4] Per-run disk log konumları. Bellek ring buffer YOKTUR — tek kaynak disktir.</summary>
public static class RunLogPaths
{
    private const string RunDirPrefix = "run-";
    private const string RunDirStampFormat = "yyyyMMdd-HHmmss-fff";

    /// <summary>Dizin listelemede koşu klasörlerini ön-süzen arama deseni (<c>run-*</c>). Ön ekten TÜRER — kalıp tek yerde
    /// durur; asıl karar yine <see cref="TryParseRunDirName"/>'dedir, bu yalnız listeyi daraltır.</summary>
    internal const string RunDirSearchPattern = RunDirPrefix + "*";

    /// <summary>[idle-memory · dalga 2 · spec §6.3] Koşu klasöründeki anlatı dosyasının adı — TEK tanım. Dosyayı motor değil App
    /// yazar: konsolun gösterdiği koşu anlatısı (<c>NarrativeStore</c>'un spool'u), <c>runStarted</c> klasörü bildirdiği andan
    /// bölümün sonuna dek. Klasörün öteki dosyaları gibi saklama kuralıyla budanır.</summary>
    public const string NarrativeFileName = "narrative.log";

    public static string DefaultLogsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "logs");

    public static string RunDirName(DateTimeOffset ts) =>
        RunDirPrefix + ts.ToString(RunDirStampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// [PERF Faz C/C2] <see cref="RunDirName"/>'in tersi: bir klasör ADININ (yol değil) koşu klasörü kalıbına TAM uyup
    /// uymadığını söyler, uyuyorsa koşunun başladığı anı verir. Kalıp tek yerde durur (yukarıdaki sabitler) — saklama
    /// budaması neyin koşu klasörü olduğunu buradan öğrenir ve başka hiçbir klasöre dokunmaz.
    /// Damga, yazıcıya verilen anın DUVAR SAATİDİR; <c>RunCoordinator</c> onu yerel saatle (<c>DateTimeOffset.Now</c>)
    /// verdiği için yerel saat dilimi olarak yorumlanır.
    /// </summary>
    public static bool TryParseRunDirName(string name, out DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        startedAt = default;
        if (!name.StartsWith(RunDirPrefix, StringComparison.Ordinal)) return false;
        if (!DateTime.TryParseExact(name.AsSpan(RunDirPrefix.Length), RunDirStampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var wall)) return false;
        try
        {
            startedAt = new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false; // DateTime uçlarındaki (0001/9999) bir damga yerel ofsetle DateTimeOffset aralığından taşar
        }
    }

    /// <summary>
    /// [idle-memory · dalga 3 · karar 37] <paramref name="path"/> bu log kökünün koşu klasörlerinden biri mi: tam nitelikli (göreli bir yol
    /// sürecin çalışma klasörüne düşerdi), normalleştirildiğinde kökün DOĞRUDAN altında (yazıcı koşu klasörünü orada açar —
    /// <see cref="RunLogWriter"/>) ve adı koşu klasörü kalıbına tam uyuyor (<see cref="TryParseRunDirName"/>). Karşılaştırma Windows yolu gibi
    /// harf duyarsızdır; sondaki ayırıcı, ileri eğik çizgi ve <c>.</c>/<c>..</c> bölümleri normalleştirilir — kökten dışarı çıkan bir yol
    /// böylece dışarıda kalır; aygıt (<c>\\?\</c>, <c>\\.\</c>) ve UNC biçimleri kökle aynı biçimde değilse eşleşmez. Saf ve SÖZCÜKSELDİR: diske
    /// bakmaz (klasörün var olup olmadığını okuma sorar), bağlantı ya da junction çözmez. Okunamayan bir yol (ör. gömülü null; sistemin
    /// normalleştiremeyeceği uzunluk — IO hatası ailesi) fırlatmaz, reddedilir.
    /// <para>[B1 fix 1 · m-2] Kabul edilen klasör NORMALLEŞTİRİLMİŞ hâliyle döner (<paramref name="runDirectory"/>) — okuyan, isteğin ham metnini
    /// değil doğrulanan yolu kullanır.</para>
    /// <para>Tek kullanıcısı kendi koşusu olmayan motorun proje log okumasıdır (<c>RunCoordinator.TryGetProjectLogSnapshot</c>): istek App'in
    /// gördüğü son koşunun klasörünü taşır ve motor onu yalnız bu kural doğruysa okur.</para>
    /// </summary>
    /// <param name="logsRoot">Motorun log kökü (koşu klasörlerini açtığı yer).</param>
    /// <param name="path">Sınanan klasör — isteğin taşıdığı yol; <c>null</c> ya da boş reddedilir.</param>
    /// <param name="runDirectory">Kabul edildiyse normalleştirilmiş klasör (sondaki ayırıcısız); aksi hâlde <c>null</c>.</param>
    public static bool TryGetRunDirectory(string logsRoot, string? path, [NotNullWhen(true)] out string? runDirectory)
    {
        ArgumentNullException.ThrowIfNull(logsRoot);
        runDirectory = null;
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(logsRoot));
            if (!string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase)
                || !TryParseRunDirName(Path.GetFileName(folder), out _)) return false;
            runDirectory = folder;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false; // okunamayan yol (ör. gömülü null, sistemin normalleştiremeyeceği uzunluk) bir koşu klasörü değildir
        }
    }
}
