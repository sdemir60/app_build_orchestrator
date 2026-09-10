namespace BuildOrchestrator.App.Services;

/// <summary>[design v1.9.0 §2.10] Bir sürüm notu maddesinin türü — <b>blok başlığı</b> olarak çizilir
/// (Keep a Changelog mantığı): satır başına ikon/sigil YOKTUR.</summary>
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
/// [design v1.9.0 §2.10 "What's new"] <b>Sürüm notlarının TEK kaynağı.</b> About penceresinin dördüncü
/// sekmesi buradan beslenir — ayrı bir pencere ya da açılış pop-up'ı YOKTUR (§8: "sürüm notları için açılış
/// pop-up'ı yok").
///
/// <para>Veri statik ve küçüktür; en yeni sürüm ÜSTTEDİR. Kategoriler sabit sırayla çizilir
/// (<see cref="KindOrder"/>) ve boş kategori başlığı hiç görünmez.</para>
///
/// <para><b>Sürüm numarası burada TEKRARLANMAZ:</b> "şu an hangi sürümdeyiz" sorusunun tek cevabı
/// <see cref="AppIdentity.Version"/>'dır (<c>Directory.Build.props</c>'tan akar) ve <c>CURRENT</c> etiketi
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

    /// <summary>Katlı kısmın ghost butonunun etiketi.</summary>
    public static string EarlierVersionsLabel(int count) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "Earlier versions ({0})", count);

    /// <summary>Şu an çalışan sürümün notları (varsa) — <c>CURRENT</c> etiketi buna konur.</summary>
    public static ReleaseEntry? Current =>
        All.FirstOrDefault(e => string.Equals(e.Version, AppIdentity.Version, StringComparison.Ordinal));

    /// <summary>
    /// Sürümler, <b>en yeni üstte</b>.
    ///
    /// <para>İçerik uygulamanın KENDİ sürüm geçmişidir — tasarım paketinin sürüm geçmişi (handoff README §9)
    /// DEĞİL. Sürüm anahtarı <see cref="AppIdentity.Version"/> ile birebir eşleşir; eşleşmezse hiçbir girdi
    /// <c>CURRENT</c> etiketi almaz (yanlış bir sürümü "güncel" göstermektense hiçbirini göstermemek doğrudur).</para>
    /// </summary>
    public static IReadOnlyList<ReleaseEntry> All { get; } =
    [
        new(AppIdentity.Version, "2026-09-09",
        [
            new(NoteKind.Added, "What's new has its own window — the sparkle button in the title bar, or Ctrl+F1."),
            new(NoteKind.Added, "First run opens Settings: the repository root moved into it, next to the layers."),
            new(NoteKind.Added, "Settings can be exported to and imported from a file, or cleared in place."),
            new(NoteKind.Added, "Every operation opens with the same choreography: the scope lights up in a wave, then the rest fades out."),
            new(NoteKind.Added, "A run ends with the neon finale in the graph — only what was built lights up."),
            new(NoteKind.Added, "Row actions: build a single project, or right-click for Build · Rebuild · Clean."),
            new(NoteKind.Added, "The Build menu offers Clean — msbuild /t:Clean on every solution."),
            new(NoteKind.Added, "Projects caught in a dependency cycle show an amber cube inside a grey node — the graph now says “not built, in a cycle” while you inspect a finished run."),
            new(NoteKind.Added, "Settings has an External projects section: projects outside the repository root, each with its source (Git or TFVC), kept in build order."),
            new(NoteKind.Added, "External projects are scanned into the same graph as the repository's own and build first, in their own External group at the top of the list."),
            new(NoteKind.Added, "Pull before build, in the External projects header: every build refreshes each external working copy first — turn it off to build them exactly as they are on disk."),
            new(NoteKind.Added, "Each row says what will happen to it and why: modified, affected, never built, failed · retry, or up to date with the age of its last successful build."),
            new(NoteKind.Added, "A behind chip next to the branch shows how far the repository has fallen behind its remote; clicking it fast-forwards — never a merge commit, never a rebase, never on a dirty tree."),
            new(NoteKind.Added, "The console reports where an external working copy landed after an update, and the Sync line says how many commits you are behind."),
            new(NoteKind.Changed, "Whether a project changed is now read from the source files on disk — a committed .xaml or .resx change, or a file version control never saw, is no longer missed. Version control takes no part in the decision, so it also works offline and for folders under no version control at all."),
            new(NoteKind.Changed, "Rows no longer show a commit pair: the right half was a remote commit you had not pulled, and the left half described the repository rather than the project. The revision stayed where it is evidence — the project log's last successful build."),
            new(NoteKind.Performance, "Source hashes are cached by size and modification time, so a normal Sync only stats the input set; the first run after this upgrade reads them once, in parallel, and says so on the console."),
            new(NoteKind.Changed, "The console cursor changes colour on every blink, stepping through the console’s own line palette — command, info, success, warning, error, dim."),
            new(NoteKind.Changed, "The initial state after Sync draws a plain strip and a four-arc ring instead of dashed lines, at full opacity — same sizes, no jagged edges; graph nodes keep their dashed border."),
            new(NoteKind.Changed, "Sync clears the console and the event stream like every other operation, replays the reveal and brings the project list back to the top."),
            new(NoteKind.Changed, "The opening choreography holds its last frame until the run actually starts — no flash back to full brightness in between."),
            new(NoteKind.Changed, "When a run ends the graph lets go of the selected project's focus and plays the finale in full view; the selection itself stays."),
            new(NoteKind.Changed, "Dialogs are sized by how they grow: Settings 760px with a scrolling body, About 660px, What's new 620px with a fixed 400px body."),
            new(NoteKind.Changed, "Long paths in About's Environment tab scroll sideways under the mouse wheel instead of being cut short."),
            new(NoteKind.Changed, "Colour tells one story: the stripe, the dot, the glyph and the graph node all carry the same status."),
            new(NoteKind.Changed, "Sync colours nothing — every project waits in the dashed start mode until an operation begins."),
            new(NoteKind.Changed, "Status chips filter together: pick several and the list shows their union."),
            new(NoteKind.Changed, "The ribbon carries a persistent operation pill — what you last asked for stays readable."),
            new(NoteKind.Changed, "The title bar carries the brand alone; the workspace name moved next to the branch chip."),
            new(NoteKind.Removed, "The orange cycle colour — a dependency cycle now shows as one amber warning triangle."),
            new(NoteKind.Removed, "The separate will-build dot — the commit pair already says what is stale."),
        ]),
    ];
}
