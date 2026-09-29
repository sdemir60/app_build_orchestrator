using System.Windows.Input;

namespace BuildOrchestrator.App.Shell;

/// <summary>[About] Kullanıcıya GÖSTERİLEN bir kısayolun kimliği. <see cref="WindowIntent"/>'ten ayrıdır:
/// niyet "hangi tuş neyi tetikler", bu ise "hangi satır listelenir / hangi rozet basılır".</summary>
public enum ShortcutId
{
    Build,
    Rebuild,
    /// <summary>[kullanıcı kararı 2026-09-29] F7 — Build menüsünün Clean'i (her projede <c>-t:Clean</c>).</summary>
    Clean,
    FocusFilter,
    About,
    Escape,
    /// <summary>[kullanıcı kararı 2026-09-29] Global: pencereyi getir / gizle — <see cref="KeyboardShortcuts.WindowBindings"/>'te
    /// DEĞİLDİR, <see cref="GlobalHotkeys"/> üzerinden RegisterHotKey ile kaydedilir.</summary>
    ShowHideWindow,
    /// <summary>[kullanıcı kararı 2026-09-29] Global: pencere gelmeden Build.</summary>
    BuildInBackground,
}

/// <summary>[design v1.19.0 §2.10] About'un Shortcuts sekmesindeki caps grubu. Sıra ve başlık metni
/// <see cref="ShortcutCatalog.GroupOrder"/>/<see cref="ShortcutCatalog.GroupTitle"/>'dadır.</summary>
public enum ShortcutGroup
{
    /// <summary>Koşu komutları — Build, Rebuild, Clean.</summary>
    Build,
    /// <summary>Uygulama geneli — filtre, About, katman kapatma.</summary>
    Application,
    /// <summary>[kullanıcı kararı 2026-09-29] Pencere öndeyken de tepsideyken de çalışan iki kısayol.</summary>
    Global,
}

/// <summary>[About] Bir kısayol satırı: jest metin(ler)i + tek cümlelik açıklama + ait olduğu grup. Global bir
/// kısayolsa <paramref name="Global"/> hangi eyleme ait olduğunu söyler — About kaydı düşen satırı buradan işaretler.</summary>
public readonly record struct ShortcutEntry(
    ShortcutId Id, IReadOnlyList<string> Gestures, string Description, ShortcutGroup Group,
    GlobalHotkeyAction? Global = null);

/// <summary>
/// [About] Kullanıcıya gösterilen kısayol metinlerinin TEK kaynağı — About diyaloğunun tablosu, Build
/// menüsünün <c>Ds.Kbd</c> rozetleri ve ikon butonlarının tooltip'leri hep buradan okur.
///
/// <para><b>Jestler ELLE YAZILMAZ:</b> <see cref="Format"/> onları <see cref="KeyboardShortcuts.WindowBindings"/>
/// satırlarından türetir (global kısayollar için <see cref="GlobalHotkeys"/>). Böylece bir bağlama
/// değişince gösterilen metin de kendiliğinden değişir. Önceki hâlde <c>"F5"</c>/<c>"Ctrl+F5"</c>
/// <c>BuildMenu.ComposeItems</c>'ta bağımsız literallerdi — bağlama tablosuyla sessizce ayrışabilirlerdi
/// (<c>ShortcutCatalogTests</c> kaynak guard'ı bunu bir daha mümkün kılmaz).</para>
///
/// <para><b>Açıklamalar</b> burada tanımlanır ve başka hiçbir yerde tekrarlanmaz.</para>
/// </summary>
public static class ShortcutCatalog
{
    /// <summary>Bir tuş + modifier bileşimini klavyede yazdığı gibi okur. Modifier sırası SABİTTİR
    /// (Ctrl → Shift → Alt), böylece aynı jest her yerde aynı görünür. <see cref="Key.Escape"/> "Esc" olarak
    /// kısaltılır (klavye tuşunun üzerindeki yazı budur); diğer tuşlar WPF adıyla yazılır (F5, F1, F…).</summary>
    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        parts.Add(key == Key.Escape ? "Esc" : key.ToString());
        return string.Join('+', parts);
    }

    /// <summary>Bir niyete bağlı TÜM jestler, tablodaki sırayla.</summary>
    private static string[] GesturesFor(WindowIntent intent) =>
        [.. KeyboardShortcuts.WindowBindings.Where(b => b.Intent == intent).Select(b => Format(b.Key, b.Modifiers))];

    /// <summary>Gösterim sırası: en sık kullanılandan en seyreğe (About tablosu bu sırayı olduğu gibi çizer).
    /// [kullanıcı kararı 2026-09-29] What's new'in kısayolu (Ctrl+F1) kalktığı için katalogda satırı yoktur;
    /// sparkle butonunun cümlesi <c>ReleaseNotes.WhatsNewTooltip</c>'tedir.</summary>
    public static IReadOnlyList<ShortcutEntry> All { get; } =
    [
        // [kullanıcı kararı 2026-09-29] Jestler GlobalHotkeys tablosundan okunur (varsayılanlar) — başka yerde yazılmaz.
        new(ShortcutId.ShowHideWindow, [GlobalHotkeys.Get(GlobalHotkeyAction.ShowHide).DefaultGesture],
            "Show or hide the window", ShortcutGroup.Global, GlobalHotkeyAction.ShowHide),
        new(ShortcutId.BuildInBackground, [GlobalHotkeys.Get(GlobalHotkeyAction.Build).DefaultGesture],
            "Build without bringing the window up", ShortcutGroup.Global, GlobalHotkeyAction.Build),
        new(ShortcutId.Build, GesturesFor(WindowIntent.Build),
            "Build — only stale projects", ShortcutGroup.Build),
        new(ShortcutId.Rebuild, GesturesFor(WindowIntent.Rebuild),
            "Rebuild — all projects, cache ignored", ShortcutGroup.Build),
        new(ShortcutId.Clean, GesturesFor(WindowIntent.Clean),
            "Clean — remove build outputs", ShortcutGroup.Build),
        new(ShortcutId.FocusFilter, GesturesFor(WindowIntent.FocusFilter),
            "Focus the project filter", ShortcutGroup.Application),
        // Bu cümle AYNI ZAMANDA title bar'daki info butonunun tooltip'idir (MainWindow.xaml) — iki yerde
        // yazılmaz.
        new(ShortcutId.About, GesturesFor(WindowIntent.ShowAbout),
            "About — version, shortcuts and diagnostics", ShortcutGroup.Application),
        new(ShortcutId.Escape, GesturesFor(WindowIntent.Escape),
            "Close the topmost layer: dialog → popover/menu → selection; otherwise stop the running build",
            ShortcutGroup.Application),
    ];

    /// <summary>[design v1.19.0 §2.10] Grupların gösterim sırası. [kullanıcı kararı 2026-09-29] GLOBAL ilk sıradadır —
    /// kullanıcının "en önemlileri" dediği iki kısayol.</summary>
    public static IReadOnlyList<ShortcutGroup> GroupOrder { get; } =
        [ShortcutGroup.Global, ShortcutGroup.Build, ShortcutGroup.Application];

    /// <summary>[design v1.19.0 §2.10] Grubun başlığı — caps olarak çizilir (<c>TrackedTextBlock</c> büyütür).</summary>
    public static string GroupTitle(ShortcutGroup group) => group switch
    {
        ShortcutGroup.Global => "Global",
        ShortcutGroup.Build => "Build",
        ShortcutGroup.Application => "Application",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>Tek kayıt. Eksik ya da ikiz bir kimlik burada fırlatır (sessizce yanlış satır üretmez).</summary>
    public static ShortcutEntry Get(ShortcutId id) => All.Single(e => e.Id == id);
}
