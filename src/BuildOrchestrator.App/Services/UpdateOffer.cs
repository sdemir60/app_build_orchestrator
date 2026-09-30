using System.Globalization;
using BuildOrchestrator.App.Services.Updates;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [design v1.23.0 §2.12 · K5] İnip kuruluma hazır bekleyen bir güncelleme: gelen sürüm, paket boyutu, kartın öne
/// çıkanları ve kartın göstermediği madde sayısı. Maddeler What's new'in veri tipidir (<see cref="ReleaseNote"/>); kart
/// onları <see cref="ReleaseNotes.KindOrder"/> sırasıyla çizer. Teklif yalnız gerçek bir feed kaydından üretilir
/// (<see cref="From"/>); feed notu yayın script'inin CHANGELOG'dan kestiği bölümdür ve aynı parser okur.
/// </summary>
public sealed record UpdateOffer(string Version, string Size, IReadOnlyList<ReleaseNote> Highlights, int MoreCount)
{
    /// <summary>Kartın gösterdiği en çok madde (K5) — 8 maddelik bir sürüm kartı ~580px'e çıkarıyordu.</summary>
    public const int MaxHighlights = 5;

    private const double BytesPerMegabyte = 1024 * 1024;

    public static UpdateOffer From(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var (shown, more) = SelectHighlights(ParseNotes(candidate.NotesMarkdown));
        return new(candidate.Version, FormatSize(candidate.DownloadBytes), shown, more);
    }

    /// <summary>"18.4 MB" — Explorer'ın MB'ı (2^20), tek ondalık, İngilizce nokta.</summary>
    internal static string FormatSize(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPerMegabyte:0.0} MB");

    /// <summary>KindOrder sırasıyla ilk <see cref="MaxHighlights"/> madde; kalan sayısı.</summary>
    internal static (IReadOnlyList<ReleaseNote> Shown, int More) SelectHighlights(IReadOnlyList<ReleaseNote> notes)
    {
        var ordered = ReleaseNotes.KindOrder.SelectMany(kind => notes.Where(n => n.Kind == kind)).ToList();
        return (ordered.Take(MaxHighlights).ToList(), Math.Max(0, ordered.Count - MaxHighlights));
    }

    /// <summary>Feed notu bu uygulamanın CHANGELOG bölümü değilse (elle yüklenmiş paket) kart düşmez: boş öne çıkanlar.</summary>
    private static IReadOnlyList<ReleaseNote> ParseNotes(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        try { return ReleaseNotes.Parse(markdown).FirstOrDefault()?.Notes ?? []; }
        catch (FormatException) { return []; }
    }
}
