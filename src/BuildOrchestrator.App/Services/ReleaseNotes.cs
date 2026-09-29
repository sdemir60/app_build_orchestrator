using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.App.Services;

/// <summary>[design v1.9.0 §2.10] Bir sürüm notu maddesinin türü — <b>blok başlığı</b> olarak çizilir
/// (Keep a Changelog mantığı): satır başına ikon/sigil YOKTUR. <c>CHANGELOG.md</c>'deki <c>### </c> başlıkları
/// bu adların kendisidir.</summary>
public enum NoteKind
{
    /// <summary>Yeni özellik — amber.</summary>
    Added,
    /// <summary>Davranış değişikliği — nötr.</summary>
    Changed,
    /// <summary>Düzeltme — yeşil.</summary>
    Fixed,
    /// <summary>Performans — turuncu.</summary>
    Performance,
    /// <summary>Kaldırılan — kırmızı.</summary>
    Removed,
}

/// <summary>[design v1.9.0 §2.10] Tek bir madde.</summary>
public readonly record struct ReleaseNote(NoteKind Kind, string Text);

/// <summary>[design v1.9.0 §2.10] Tek bir sürüm: numara, tarih ve maddeleri.</summary>
public sealed record ReleaseEntry(string Version, string Date, IReadOnlyList<ReleaseNote> Notes);

/// <summary>
/// [design v1.13.0/v1.19.0 §2.10/§2.11 "What's new"] What's new diyaloğunun (<c>Views/NotesDialog</c>) veri ve
/// kural yüzeyi. Açılışta pop-up YOKTUR; kullanıcıyı title bar'daki sparkle butonunun görülmemiş-sürüm noktası
/// çağırır.
///
/// <para><b>Notların TEK kaynağı repo kökündeki <c>CHANGELOG.md</c>'dir.</b> Dosya App'e gömülü kaynak olarak
/// girer (App csproj, <see cref="ChangelogResourceName"/>) ve ilk erişimde <see cref="Parse"/> ile okunur; burada
/// elle yazılmış bir liste YOKTUR. Dosya yalnız bir sürüm çıkarılırken yazılır (CLAUDE.md "Sürüm çıkarma");
/// biçimini ve en üst sürümün çalışan sürümle eşleştiğini <c>ChangelogTests</c>/<c>WhatsNewTests</c> pinler.</para>
///
/// <para>En yeni sürüm ÜSTTEDİR. Kategoriler sabit sırayla çizilir (<see cref="KindOrder"/>) ve boş kategori
/// başlığı hiç görünmez.</para>
///
/// <para><b>Sürüm numarası burada TEKRARLANMAZ:</b> "şu an hangi sürümdeyiz" sorusunun tek cevabı
/// <see cref="AppIdentity.Version"/>'dır (<c>Directory.Build.props</c>'tan akar) ve <c>INSTALLED</c> çipi
/// onunla eşleşen girdiye konur.</para>
/// </summary>
public static class ReleaseNotes
{
    /// <summary>[§2.10] Kategorilerin ÇİZİM sırası — sabittir ve içeriğe göre değişmez.</summary>
    public static readonly IReadOnlyList<NoteKind> KindOrder =
        [NoteKind.Added, NoteKind.Changed, NoteKind.Fixed, NoteKind.Performance, NoteKind.Removed];

    /// <summary>[§2.10] Kategori başlığının 6px karesinin rengi.</summary>
    public static string SwatchBrushKey(NoteKind kind) => kind switch
    {
        NoteKind.Added => "Brush.Amber",
        NoteKind.Fixed => "Brush.StatusSuccess",
        NoteKind.Performance => "Brush.StatusCycle",
        NoteKind.Removed => "Brush.StatusFail",
        _ => "Brush.TextDim",
    };

    /// <summary>[§2.10] Kategori başlığının caps etiketi.</summary>
    public static string Label(NoteKind kind) => kind switch
    {
        NoteKind.Added => "ADDED",
        NoteKind.Changed => "CHANGED",
        NoteKind.Fixed => "FIXED",
        NoteKind.Performance => "PERFORMANCE",
        NoteKind.Removed => "REMOVED",
        _ => kind.ToString().ToUpperInvariant(),
    };

    /// <summary>[§2.10] "Son 3 sürüm açık, gerisi katlı."</summary>
    public const int OpenByDefault = 3;

    /// <summary>[design v1.13.1/v1.19.0 §2.10/§2.11] <c>What's new in {sürüm}</c> — cümlenin TEK yeri. İki tüketicisi
    /// vardır: görülmemiş sürüm varken sparkle butonunun tooltip'i (MainWindow) ve About sekmesinin What's new
    /// butonu (WhatsNewTests kaynak guard'ı pinler).</summary>
    public static string WhatsNewInLabel(string version) =>
        string.Format(CultureInfo.InvariantCulture, "What's new in {0}", version);

    /// <summary>Katlı kısmın ghost butonunun etiketi.</summary>
    public static string EarlierVersionsLabel(int count) =>
        string.Format(CultureInfo.InvariantCulture, "Earlier versions ({0})", count);

    /// <summary><c>CHANGELOG.md</c>'nin App assembly'sindeki gömülü kaynak adı — App csproj'daki
    /// <c>LogicalName</c> ile aynıdır; repo kökündeki dosyanın adı da budur.</summary>
    public const string ChangelogResourceName = "CHANGELOG.md";

    /// <summary>Şu an çalışan sürümün notları (varsa) — <c>INSTALLED</c> çipi buna konur.</summary>
    public static ReleaseEntry? Current =>
        All.FirstOrDefault(e => string.Equals(e.Version, AppIdentity.Version, StringComparison.Ordinal));

    /// <summary>
    /// Sürümler, <b>en yeni üstte</b> — gömülü <c>CHANGELOG.md</c>'den, ilk erişimde bir kez okunur.
    ///
    /// <para>Okuma TEMBELDİR: sınıfın diğer üyeleri (kategori rengi, <see cref="WhatsNewInLabel"/>) açılışta
    /// title bar tooltip'i için okunur ve dosyaya dokunmaz. Sürüm anahtarı <see cref="AppIdentity.Version"/> ile
    /// birebir eşleşir; eşleşmezse hiçbir girdi <c>INSTALLED</c> çipi almaz (yanlış bir sürümü "kurulu"
    /// göstermektense hiçbirini göstermemek doğrudur).</para>
    /// </summary>
    public static IReadOnlyList<ReleaseEntry> All => Loaded.Value;

    private static readonly Lazy<IReadOnlyList<ReleaseEntry>> Loaded = new(() => Parse(ReadChangelog()));

    /// <summary>Gömülü <c>CHANGELOG.md</c>'nin metni. Kaynak yoksa bu bir build hatasıdır (csproj kablosu
    /// kopmuş) — sessizce boş liste dönülmez.</summary>
    internal static string ReadChangelog()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ChangelogResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{ChangelogResourceName}' is missing from the application assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // "## [1.7.0] - 2026-09-29" — Keep a Changelog sürüm başlığı; sürüm yalın major.minor.patch.
    private static readonly Regex VersionHeading =
        new(@"^## \[(?<version>\d+\.\d+\.\d+)\] - (?<date>\d{4}-\d{2}-\d{2})$", RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>CHANGELOG.md</c> metnini sürümlere çevirir. Biçim Keep a Changelog'un bu uygulamanın kullandığı
    /// alt kümesidir:
    /// <list type="bullet">
    /// <item><c>## [x.y.z] - yyyy-MM-dd</c> bir sürüm açar; ilk sürümden önceki her şey (başlık, giriş metni)
    /// veri değildir.</item>
    /// <item><c>### Added</c> … <c>### Removed</c> kategori açar — yalnız <see cref="NoteKind"/> adları.</item>
    /// <item><c>- metin</c> bir maddedir; hemen altındaki girintili satırlar aynı maddenin devamıdır ve tek
    /// boşlukla birleşir.</item>
    /// </list>
    /// Bunun dışındaki her satır, kategorisiz madde ve maddesi olmayan sürüm <see cref="FormatException"/> ile
    /// SATIR NUMARASIYLA reddedilir: ekranda görünmeyecek ya da yanlış görünecek bir satır sessizce atlanmaz.
    /// </summary>
    public static IReadOnlyList<ReleaseEntry> Parse(string markdown)
    {
        var entries = new List<ReleaseEntry>();
        string? version = null, date = null;
        int versionLine = 0;
        var notes = new List<ReleaseNote>();
        NoteKind? kind = null;
        bool continuable = false; // son satır bir madde (ya da devamı) mı — girintili satır ancak ona eklenir

        void CloseVersion()
        {
            if (version is null) return;
            if (notes.Count == 0) throw Malformed(versionLine, $"version {version} has no notes");
            entries.Add(new ReleaseEntry(version, date!, [.. notes]));
            notes.Clear();
        }

        string[] lines = markdown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = lines[i].TrimEnd('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continuable = false;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseVersion();
                var match = VersionHeading.Match(line);
                if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw Malformed(lineNo, "a version heading reads '## [major.minor.patch] - yyyy-MM-dd'");
                version = match.Groups["version"].Value;
                date = match.Groups["date"].Value;
                versionLine = lineNo;
                kind = null;
                continuable = false;
                continue;
            }

            if (version is null) continue; // başlık ve giriş metni

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                string name = line[4..].Trim();
                kind = Enum.GetValues<NoteKind>().Cast<NoteKind?>().FirstOrDefault(k => k.ToString() == name)
                    ?? throw Malformed(lineNo, $"unknown category '{name}' (use {string.Join(", ", KindOrder)})");
                continuable = false;
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (kind is null) throw Malformed(lineNo, "a note must sit under a category heading");
                notes.Add(new ReleaseNote(kind.Value, line[2..].Trim()));
                continuable = true;
                continue;
            }

            if (continuable && char.IsWhiteSpace(line[0]))
            {
                var last = notes[^1];
                notes[^1] = last with { Text = last.Text + " " + line.Trim() };
                continue;
            }

            throw Malformed(lineNo, "unexpected text; only headings and '- ' notes are allowed inside a version");
        }

        CloseVersion();
        return entries;
    }

    private static FormatException Malformed(int lineNo, string reason) =>
        new($"{ChangelogResourceName} line {lineNo}: {reason}.");
}
