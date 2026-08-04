using System.Windows.Input;

namespace BuildOrchestrator.App.Shell;

/// <summary>[About] Kullanıcıya GÖSTERİLEN bir kısayolun kimliği. <see cref="WindowIntent"/>'ten ayrıdır:
/// niyet "hangi tuş neyi tetikler", bu ise "hangi satır listelenir / hangi rozet basılır".</summary>
public enum ShortcutId
{
    Build,
    Rebuild,
    FocusFilter,
    Escape,
    /// <summary>Global kısayol (tepsiden pencereyi getir) — <see cref="KeyboardShortcuts.WindowBindings"/>'te
    /// DEĞİLDİR, <see cref="HotkeyBinding"/> üzerinden RegisterHotKey ile kaydedilir.</summary>
    RestoreFromTray,
}

/// <summary>[About] Bir kısayol satırı: jest metin(ler)i + tek cümlelik açıklama.</summary>
public readonly record struct ShortcutEntry(ShortcutId Id, IReadOnlyList<string> Gestures, string Description);

/// <summary>
/// [About] Kullanıcıya gösterilen kısayol metinlerinin TEK kaynağı — About diyaloğunun tablosu, Build
/// menüsünün <c>Ds.Kbd</c> rozetleri ve ikon butonlarının tooltip'leri hep buradan okur.
///
/// <para><b>Jestler ELLE YAZILMAZ:</b> <see cref="Format"/> onları <see cref="KeyboardShortcuts.WindowBindings"/>
/// satırlarından türetir (global kısayol için <see cref="HotkeyBinding.DefaultGesture"/>). Böylece bir bağlama
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

    /// <summary>Bir niyete bağlı TÜM jestler, tablodaki sırayla (ör. Rebuild → Ctrl+F5, Shift+F5).</summary>
    private static string[] GesturesFor(WindowIntent intent) =>
        [.. KeyboardShortcuts.WindowBindings.Where(b => b.Intent == intent).Select(b => Format(b.Key, b.Modifiers))];

    /// <summary>Gösterim sırası: en sık kullanılandan en seyreğe (About tablosu bu sırayı olduğu gibi çizer).</summary>
    public static IReadOnlyList<ShortcutEntry> All { get; } =
    [
        new(ShortcutId.Build, GesturesFor(WindowIntent.F5StateBranch),
            "Build — or Stop while a run is in flight"),
        new(ShortcutId.Rebuild, GesturesFor(WindowIntent.Rebuild),
            "Rebuild — all projects, cache ignored"),
        new(ShortcutId.FocusFilter, GesturesFor(WindowIntent.FocusFilter),
            "Focus the project filter"),
        new(ShortcutId.Escape, GesturesFor(WindowIntent.Escape),
            "Close the topmost open layer: dialog → popover/menu → selection"),
        new(ShortcutId.RestoreFromTray, [HotkeyBinding.DefaultGesture],
            "Global — bring the window back from the tray"),
    ];

    /// <summary>Tek kayıt. Eksik ya da ikiz bir kimlik burada fırlatır (sessizce yanlış satır üretmez).</summary>
    public static ShortcutEntry Get(ShortcutId id) => All.Single(e => e.Id == id);
}
