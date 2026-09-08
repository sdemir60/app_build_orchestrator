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
        new(AppIdentity.Version, "2026-08-27",
        [
            new(NoteKind.Added, "What's new — release notes now live in this window."),
            new(NoteKind.Added, "First run opens Settings: the repository root moved into it, next to the layers."),
            new(NoteKind.Added, "Settings can be exported to and imported from a file, or cleared in place."),
            new(NoteKind.Added, "Every operation opens with the same choreography: the scope lights up in a wave, then the rest fades out."),
            new(NoteKind.Added, "A run ends with the neon finale in the graph — only what was built lights up."),
            new(NoteKind.Added, "Row actions: build a single project, or right-click for Build · Rebuild · Clean."),
            new(NoteKind.Added, "The Build menu offers Clean — msbuild /t:Clean on every solution."),
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
