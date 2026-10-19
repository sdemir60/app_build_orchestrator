using System.Globalization;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [Exit] Exit butonunun, onay kartının ve kapanış ekranının metinlerinin TEK kaynağı — XAML, kabuk ve testler aynı sabiti okur
/// (<see cref="UpdateText"/>'in deseni). Tasarımın verdiği cümleler birebirdir: "Exit stops the build, then quits.", "4 projects in
/// flight stay unbuilt. Finished projects keep their results; the next Build picks up the rest.", "Nothing is running. Exit quits
/// right away.", "Stopping build…", "Ending 5 processes…". Tasarımın örneklemediği işler aynı kalıbı izler; sonuç satırları işin
/// yarıda kesilince gerçekte ne bıraktığını söyler (ARCHITECTURE §5.2, §8.7). Tüm metin İngilizce.
/// </summary>
public static class ExitText
{
    /// <summary>Butonun ekran okuyucu adı, kartın boştaki başlığı ve düğmesi.</summary>
    public const string ExitLabel = "Exit";

    /// <summary>Kartın kapatma düğmesi (secondary sm).</summary>
    public const string Cancel = "Cancel";

    /// <summary>Kesilen işte kartın birincil düğmesi (danger sm, power ikonlu).</summary>
    public const string StopAndExit = "Stop and exit";

    /// <summary>Git yazımında kartın birincil düğmesi — iş kesilmez, biter.</summary>
    public const string WaitAndExit = "Wait and exit";

    /// <summary>Kart açıkken iş biterse kartın cümlesi.</summary>
    public const string NothingRunning = "Nothing is running. Exit quits right away.";

    /// <summary>Kapanacak süreçler bloğunun caps başlığı (<c>ENDS ON EXIT</c> olarak çizilir).</summary>
    public const string EndsOnExit = "Ends on exit";

    /// <summary>Koşuda kesilen projelerin ardından gelen cümle — biten projeler kayıtlıdır (ARCHITECTURE §4.5, §8.7).</summary>
    private const string BuildKeepsResults = "Finished projects keep their results; the next Build picks up the rest.";

    /// <summary>Kapanış ekranının başlığı (13px/600). Ürün adı YAZILMAZ, kimlikten okunur (<see cref="AppIdentity.Product"/>).</summary>
    public static string ClosingHeading { get; } = "Closing " + AppIdentity.Product;

    /// <summary>İşin adı — kapanış ekranının bağlam satırı ve kartın başlığı bunu yazar.</summary>
    public static string WorkName(ExitWork work) => work switch
    {
        ExitWork.Build => "Build",
        ExitWork.Rebuild => "Rebuild",
        ExitWork.Resolve => "Resolve",
        ExitWork.Clean => "Clean",
        ExitWork.DeepClean => "Deep clean",
        ExitWork.Optimize => "Optimize",
        ExitWork.Sync => "Sync",
        ExitWork.BranchSwitch => "Branch switch",
        ExitWork.Pull => "Pull",
        ExitWork.None => ExitLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(work), work, null),
    };

    /// <summary>Cümle içindeki adı ("the build", "the deep clean").</summary>
    private static string InSentence(ExitWork work) => WorkName(work).ToLowerInvariant();

    /// <summary>Kartın caps durum etiketi (<c>BUILD IN PROGRESS</c> olarak çizilir); uçuşta iş yokken <c>EXIT</c>.</summary>
    public static string Heading(ExitWork work) => work == ExitWork.None ? ExitLabel : WorkName(work) + " in progress";

    /// <summary>Kartın tek cümlesi (13px/500).</summary>
    public static string Sentence(ExitWork work) =>
        work == ExitWork.None ? NothingRunning
        : work.Waits() ? $"Exit waits for the {InSentence(work)}, then quits."
        : $"Exit stops the {InSentence(work)}, then quits.";

    /// <summary>Kartın karar bloğundaki sonuç satırı (12px text-dim) — işin yarıda kalınca ne bıraktığı; uçuşta iş yokken satır yoktur
    /// (<c>null</c>). Koşuda <paramref name="projectsInFlight"/> o an derlenen proje sayısıdır; sıfırsa ilk cümle düşer.</summary>
    public static string? Outcome(ExitWork work, int projectsInFlight) => work switch
    {
        ExitWork.None => null,
        ExitWork.Build or ExitWork.Rebuild or ExitWork.Resolve => projectsInFlight switch
        {
            0 => BuildKeepsResults,
            1 => "1 project in flight stays unbuilt. " + BuildKeepsResults,
            _ => string.Create(CultureInfo.InvariantCulture,
                $"{projectsInFlight} projects in flight stay unbuilt. {BuildKeepsResults}"),
        },
        ExitWork.Clean => "Projects cleaned so far stay clean; the rest keep their outputs.",
        ExitWork.DeepClean => "Part of the workspace may already be reset; the next Build compiles what it needs.",
        ExitWork.Optimize => "Packages restored so far stay; the next Optimize does the rest.",
        ExitWork.Sync => "Nothing is lost; the next start syncs again.",
        ExitWork.BranchSwitch => "Cutting git off mid-write could leave the working tree half-switched.",
        ExitWork.Pull => "Cutting git off mid-write could leave the working tree half-updated.",
        _ => throw new ArgumentOutOfRangeException(nameof(work), work, null),
    };

    /// <summary>Kartın birincil düğmesi: uçuşta iş yokken <c>Exit</c>, git yazımında <see cref="WaitAndExit"/>, aksi hâlde
    /// <see cref="StopAndExit"/>.</summary>
    public static string Action(ExitWork work) =>
        work == ExitWork.None ? ExitLabel : work.Waits() ? WaitAndExit : StopAndExit;

    /// <summary>Kapanacak süreç satırının sağındaki sayı (mono 11px text-dim).</summary>
    public static string ProcessCount(int count) => string.Create(CultureInfo.InvariantCulture, $"×{count}");

    /// <summary>Kapanış ekranının mono bağlam satırı: işin adı, koşuda ilerlemesiyle (<c>Build · 14/38</c>).</summary>
    public static string ClosingContext(ExitWork work, string? progress) =>
        progress is null ? WorkName(work) : $"{WorkName(work)} · {progress}";

    /// <summary>Kapanış ekranının ilk adımı — sürerken yazıldığı için sonunda üç nokta (U+2026) taşır.</summary>
    public static string StoppingStep(ExitWork work) =>
        (work.Waits() ? "Finishing " : "Stopping ") + InSentence(work) + "…";

    /// <summary>Kapanış ekranının ikinci adımı: kartın listelediği süreçlerin sayısı; liste boşsa yalnız kapanış.</summary>
    public static string EndingStep(int processes) => processes switch
    {
        0 => "Quitting…",
        1 => "Ending 1 process…",
        _ => string.Create(CultureInfo.InvariantCulture, $"Ending {processes} processes…"),
    };
}
