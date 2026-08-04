using System.IO;
using System.Windows.Input;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kısayol jestlerinin TEK doğruluk kaynağı. Metinler elle yazılmaz: <see cref="ShortcutCatalog.Format"/>
/// bunları <see cref="KeyboardShortcuts.WindowBindings"/>'ten (global kısayol için
/// <see cref="HotkeyBinding.DefaultGesture"/>'dan) türetir. Önceden "F5"/"Ctrl+F5"
/// <c>BuildMenu.ComposeItems</c>'ta ELLE yazılıydı ve bağlama tablosuyla sessizce ayrışabilirdi.
/// </summary>
public class ShortcutCatalogTests
{
    [Fact]
    public void Every_window_binding_is_covered_by_a_catalog_entry()
    {
        var catalogued = ShortcutCatalog.All.SelectMany(e => e.Gestures).ToHashSet(StringComparer.Ordinal);
        foreach (var binding in KeyboardShortcuts.WindowBindings)
            Assert.Contains(ShortcutCatalog.Format(binding.Key, binding.Modifiers), catalogued);
    }

    /// <summary>Ters yön: katalogda, hiçbir bağlamanın (ya da global kısayolun) üretmediği bir jest kalamaz —
    /// bir bağlama kaldırılınca katalog satırı yetim kalır ve burada yakalanır.</summary>
    [Fact]
    public void The_catalog_has_no_orphan_gesture()
    {
        var bound = KeyboardShortcuts.WindowBindings
            .Select(b => ShortcutCatalog.Format(b.Key, b.Modifiers))
            .Append(HotkeyBinding.DefaultGesture) // global kısayol WindowBindings'te DEĞİLDİR
            .ToHashSet(StringComparer.Ordinal);
        foreach (string gesture in ShortcutCatalog.All.SelectMany(e => e.Gestures))
            Assert.Contains(gesture, bound);
    }

    [Theory]
    [InlineData(Key.F5, ModifierKeys.None, "F5")]
    [InlineData(Key.F5, ModifierKeys.Control, "Ctrl+F5")]
    [InlineData(Key.F5, ModifierKeys.Shift, "Shift+F5")]
    [InlineData(Key.F, ModifierKeys.Control, "Ctrl+F")]
    [InlineData(Key.Escape, ModifierKeys.None, "Esc")]
    [InlineData(Key.F1, ModifierKeys.None, "F1")]
    public void Gesture_text_reads_the_way_a_keyboard_is_labelled(Key key, ModifierKeys modifiers, string expected)
        => Assert.Equal(expected, ShortcutCatalog.Format(key, modifiers));

    /// <summary>Modifier SIRASI sabittir (Ctrl → Shift → Alt): aynı jest her yerde aynı okunmalı.</summary>
    [Fact]
    public void Modifiers_are_written_in_a_fixed_order()
        => Assert.Equal("Ctrl+Shift+Alt+F5",
            ShortcutCatalog.Format(Key.F5, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt));

    [Fact]
    public void The_global_hotkey_row_reads_its_gesture_from_the_hotkey_default()
        => Assert.Equal([HotkeyBinding.DefaultGesture], ShortcutCatalog.Get(ShortcutId.RestoreFromTray).Gestures);

    [Fact]
    public void Every_entry_has_a_description_and_at_least_one_gesture()
    {
        Assert.NotEmpty(ShortcutCatalog.All);
        foreach (var entry in ShortcutCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"{entry.Id} açıklamasız");
            Assert.NotEmpty(entry.Gestures);
        }
    }

    [Fact]
    public void Get_returns_exactly_one_entry_per_id()
    {
        foreach (var id in Enum.GetValues<ShortcutId>())
            Assert.Equal(id, ShortcutCatalog.Get(id).Id); // Single(): eksik ya da ikiz kayıt fırlatır
    }

    [Fact]
    public void The_build_menu_reads_its_key_badges_from_the_catalog()
    {
        var items = BuildMenu.ComposeItems(stopped: false, total: 3, failed: 0);
        Assert.Equal(ShortcutCatalog.Get(ShortcutId.Build).Gestures[0], items.Single(i => i.Kind == "build").Kbd);
        Assert.Equal(ShortcutCatalog.Get(ShortcutId.Rebuild).Gestures[0], items.Single(i => i.Kind == "rebuild").Kbd);
    }

    /// <summary>
    /// KAYNAK GUARD'ı — asıl kopya yasağını bu test zorlar. Jest metni yalnız
    /// <see cref="ShortcutCatalog.Format"/> tarafından ÜRETİLİR; başka hiçbir üretim dosyası onu literal
    /// olarak yazmaz.
    ///
    /// <para><b>Tek kaynağın kendisi kapsam DIŞIDIR</b> (ölçüldü: aksi halde test kendi çözümünü ihlal sayardı).
    /// <c>Format</c>'ın yapı taşları — <c>"Ctrl"</c>, <c>"Shift"</c>, <c>"Alt"</c> ve <c>Esc</c> kısaltması —
    /// kaçınılmaz olarak o dosyada literaldir; guard'ın derdi o metinlerin BAŞKA bir dosyada ikinci kez
    /// belirmesidir.</para>
    ///
    /// <para><c>"Alt+B"</c> listede YOK: onun tek kaynağı <see cref="HotkeyBinding.DefaultGesture"/>'dır ve
    /// katalog oradan okur. Yorum satırlarındaki TIRNAKSIZ <c>Ctrl+F5</c> anlatımı taramaya girmez — aranan
    /// şey tırnaklı literaldir.</para>
    /// </summary>
    [Fact]
    public void No_app_source_file_outside_the_catalog_writes_a_key_gesture_as_a_literal()
    {
        string[] literals = ["\"F5\"", "\"Ctrl+F5\"", "\"Shift+F5\"", "\"Ctrl+F\"", "\"Esc\"", "\"F1\""];
        string singleSource = Path.Combine("Shell", "ShortcutCatalog.cs");

        var offenders = new List<string>();
        foreach (string file in RepoPaths.AppSourceFiles("*.cs").Concat(RepoPaths.AppSourceFiles("*.xaml")))
        {
            string relative = Path.GetRelativePath(RepoPaths.AppSrcRoot, file);
            if (relative == singleSource) continue;

            string text = File.ReadAllText(file);
            foreach (string literal in literals)
                if (text.Contains(literal, StringComparison.Ordinal))
                    offenders.Add($"{relative} → {literal}");
        }
        Assert.Empty(offenders);
    }

    /// <summary>Muafiyet BOŞA DÜŞMESİN: yukarıdaki guard'ın kapsam dışı bıraktığı dosya gerçekten VAR.
    /// Katalog taşınır/yeniden adlandırılırsa guard sessizce "hiçbir şeyi muaf tutmayan" bir teste dönüşürdü —
    /// bu iyi yönde bir sapma değil, muafiyetin bayatladığının işaretidir.</summary>
    [Fact]
    public void The_single_source_the_guard_exempts_really_exists()
        => Assert.True(File.Exists(Path.Combine(RepoPaths.AppSrcRoot, "Shell", "ShortcutCatalog.cs")));
}
