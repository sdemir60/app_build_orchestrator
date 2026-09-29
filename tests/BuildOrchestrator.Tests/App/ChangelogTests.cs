using System.Globalization;
using System.IO;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sürüm notları] Repo kökündeki <c>CHANGELOG.md</c>'nin guard'ı. What's new'in TEK veri kaynağı bu dosyadır:
/// App onu gömülü kaynak olarak okur (<see cref="ReleaseNotes.All"/>). Okuyucunun biçim kuralları
/// <c>WhatsNewTests</c>'tedir; burada gerçek dosyanın kendisi denetlenir: exe'ye gömülen metin diskteki dosyadır,
/// sürümler yeniden eskiye sıralıdır, kategoriler ekrandaki çizim sırasıyla yazılır ve maddeler düz metindir.
///
/// <para>En üst sürümün çalışan sürümle (<c>Directory.Build.props</c> → <c>Version</c>) eşleştiğini
/// <c>WhatsNewTests.The_running_version_has_an_entry_so_it_can_be_marked_current</c> pinler — sürüm numarası
/// notu yazılmadan artırılamaz.</para>
/// </summary>
public class ChangelogTests
{
    private static string ChangelogPath => Path.Combine(RepoPaths.RepoRoot, ReleaseNotes.ChangelogResourceName);

    private static IReadOnlyList<ReleaseEntry> DiskEntries() => ReleaseNotes.Parse(File.ReadAllText(ChangelogPath));

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Exe'nin okuduğu metin repo kökündeki dosyanın kendisidir — kopyası ya da bayat bir sürümü değil.
    /// Satır sonları git ayarına göre değişebildiği için karşılaştırma satır sonundan bağımsızdır.</summary>
    [Fact]
    public void The_app_embeds_the_changelog_from_the_repository_root()
    {
        Assert.True(File.Exists(ChangelogPath), $"{ChangelogPath} is missing.");
        Assert.Equal(NormalizeNewlines(File.ReadAllText(ChangelogPath)), NormalizeNewlines(ReleaseNotes.ReadChangelog()));
    }

    /// <summary>Sürümler yeniden eskiye sıralıdır ve her numara bir kez geçer; tarih gerçek bir takvim günüdür ve
    /// daha yeni bir sürümün tarihi eskisinden önce olamaz.</summary>
    [Fact]
    public void Versions_run_newest_first_with_real_dates()
    {
        var entries = DiskEntries();
        Assert.NotEmpty(entries);

        var versions = entries.Select(e => Version.Parse(e.Version)).ToList();
        for (int i = 1; i < versions.Count; i++)
            Assert.True(versions[i - 1] > versions[i], $"{versions[i - 1]} must be newer than {versions[i]}.");

        var dates = entries.Select(e => DateOnly.ParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        for (int i = 1; i < dates.Count; i++)
            Assert.True(dates[i - 1] >= dates[i], $"{entries[i - 1].Version} is dated before {entries[i].Version}.");
    }

    /// <summary>Her sürümde kategoriler ekrandaki çizim sırasıyla (<see cref="ReleaseNotes.KindOrder"/>) ve her biri
    /// bir kez yazılır — dosyayı okuyan da ekrandaki sırayı görür.</summary>
    [Fact]
    public void Categories_appear_once_each_in_drawing_order()
    {
        var kindOrder = ReleaseNotes.KindOrder.ToList();
        foreach (var entry in DiskEntries())
        {
            var runs = new List<NoteKind>();
            foreach (var note in entry.Notes)
                if (runs.Count == 0 || runs[^1] != note.Kind) runs.Add(note.Kind);

            var order = runs.Select(k => kindOrder.IndexOf(k)).ToList();
            Assert.True(order.SequenceEqual(order.Order()) && order.Distinct().Count() == order.Count,
                $"{entry.Version}: categories must follow {string.Join(", ", ReleaseNotes.KindOrder)}, once each.");
        }
    }

    /// <summary>What's new maddeyi düz metin olarak çizer: markdown vurgusu, kod ya da bağlantı işareti ekranda
    /// olduğu gibi görünürdü.</summary>
    [Fact]
    public void Notes_are_plain_text()
    {
        char[] markup = ['`', '*', '[', ']', '<', '>'];
        var offenders = DiskEntries()
            .SelectMany(e => e.Notes.Select(n => (e.Version, n.Text)))
            .Where(x => x.Text.IndexOfAny(markup) >= 0)
            .Select(x => $"{x.Version}: {x.Text}")
            .ToList();
        Assert.Empty(offenders);
    }
}
