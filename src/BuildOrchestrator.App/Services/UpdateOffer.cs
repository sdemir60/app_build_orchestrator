using System.Globalization;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [design v1.23.0 §2.12] İnip kuruluma hazır bekleyen bir güncelleme: gelen sürüm, paket boyutu ve kartta gösterilen
/// öne çıkan maddeler. Maddeler What's new'in veri tipidir (<see cref="ReleaseNote"/>) — ikinci bir kategori tablosu
/// yoktur; kart onları <see cref="ReleaseNotes.KindOrder"/> sırasıyla çizer.
///
/// <para><b>Güncelleme motoru henüz YAZILMADI.</b> Uygulamanın o anki teklifi tek yerde durur
/// (<c>RunViewModel.AvailableUpdate</c>); şimdilik orada <see cref="Sample"/> vardır ve hap bu yüzden her zaman
/// görünür (kullanıcı kararı 2026-09-29: güncelleme süreçlerinin yalnız tasarımı aktarılır). Motor yazıldığında
/// teklifi o yazar.</para>
/// </summary>
public sealed record UpdateOffer(string Version, string Size, IReadOnlyList<ReleaseNote> Highlights)
{
    /// <summary>[prototip <c>nextMinor</c>, BuildApp.jsx:1649] Minor bir artar, patch sıfırlanır; okunamayan ya da eksik
    /// parça 0 sayılır (<c>parseInt(n, 10) || 0</c>).</summary>
    public static string NextMinor(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        string[] parts = version.Split('.');
        int major = Part(parts, 0), minor = Part(parts, 1);
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor + 1}.0");
    }

    private static int Part(string[] parts, int index) =>
        index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : 0;

    /// <summary>
    /// PLACEHOLDER — the update engine is not written yet. Prototipin örnek kaydı (<c>UPDATE_FEED</c>,
    /// BuildApp.jsx:1650-1658): gelen sürüm kurulu sürümün (<see cref="AppIdentity.Version"/>) bir sonraki minor'ı,
    /// boyut ve üç madde örnek metindir. Sürüm elle yazılmaz; CHANGELOG ve <c>Directory.Build.props</c> bu kayıttan
    /// etkilenmez.
    /// </summary>
    public static UpdateOffer Sample { get; } = new(
        NextMinor(AppIdentity.Version),
        "18.4 MB",
        [
            new ReleaseNote(NoteKind.Performance,
                "Sync reads project files in parallel — about twice as fast on large solutions."),
            new ReleaseNote(NoteKind.Fixed, "Copy log keeps its line breaks when pasted into Teams or Outlook."),
            new ReleaseNote(NoteKind.Fixed, "A project renamed on disk is picked up by the next Sync."),
        ]);
}
