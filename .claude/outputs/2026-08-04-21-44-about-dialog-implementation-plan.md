# About / Info Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `.claude/outputs/2026-08-04-21-17-about-dialog-design.md` — read it first. Every section number
referenced below (`§3.2`, `§5.1`, …) points there.

**Goal:** Title bar'a bir info ikon butonu ve tıklanınca açılan, Settings ile aynı kabuğu paylaşan sekmeli bir
About modali (ürün kimliği · klavye kısayolları · ortam/tanı · üçüncü-taraf lisansları).

**Architecture:** Diyalog ince bir kabuktur; gösterdiği her şey saf, WPF'siz tiplerden gelir
(`ShortcutCatalog`, `DiagnosticsReport`, `ThirdPartyNotices`, `AppIdentity`). Bu tiplerin varlık sebebi
yalnız test edilebilirlik değil, **tek doğruluk kaynağı**: bugün iki yerde yazılı olan kısayol metinleri ve
inline duran marka logosu bu iş kapsamında tek yere toplanır.

**Tech Stack:** .NET 10 · WPF (`net10.0-windows`) · xUnit + `StaFact` · mevcut design system
(`Resources/Tokens.xaml`, `Icons.xaml`, `Controls.xaml`).

## Global Constraints

Her task'ın gereksinimlerine **örtük olarak dahildir**:

- **Kod, UI metinleri ve loglar İngilizce; kod yorumları Türkçe.**
- **Kopya YASAK / tek doğruluk kaynağı:** aynı değer, metin veya primitif iki yerde tanımlanmaz — ne kodda ne
  testlerde (ortak fixture tek yerde).
- **Kırmızı test kuralı:** hiçbir kod, kusuru/eksiği yakalayan test KIRMIZI verdiği gösterilmeden yazılmaz.
  Kırmızıyı gösteremiyorsan test yanlıştır — testi düzelt, kuralı esnetme.
- **Davranış değişince testi de değişir:** bilerek değişen bir kuralı pinleyen eski test sessizce silinmez ya
  da gevşetilmez; YENİ kuralı pinleyecek şekilde yeniden yazılır, doc'una eski iddia + değişme gerekçesi yazılır.
- **Realize testi:** yeni XAML kökü/şablonu ekleyen her değişiklik bir realize testi de ekler. `Window.Measure/Arrange`
  HWND'siz içeriğe inmez — realize `window.Content` üzerinde yapılır (`MainWindowHost.Realize` / `DsResources.Realize`).
- **Renk ve motion hardcode YASAK** — hepsi token'dan (`NoHardcodedColorTests`, `NoHardcodedMotionTests` guard'ları koşar).
- **D8:** testte gerçek zaman beklenmez (`NoSleepPollTests`); süre mantığı saf/enjekte-saatli tiplerde test edilir.
- Test komutu: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
- Build komutu: `dotnet build BuildOrchestrator.slnx` — **uygulama açıkken build alma** (çalışan Supervisor kendi
  binary'lerini kilitler).
- Branch: `feat/about-dialog` (açık). **Task başına bir commit.**

---

## File Structure

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs` | **yeni** | Kısayol jesti + açıklama tablosu (saf). Jestler `KeyboardShortcuts.WindowBindings` ve `HotkeyBinding.DefaultGesture`'dan TÜRETİLİR. |
| `src/BuildOrchestrator.App/Controls/BrandLogo.xaml(.cs)` | **yeni** | Delta logosu — tek yer. MainWindow ve About aynı kontrolü kullanır. |
| `src/BuildOrchestrator.App/Services/AppIdentity.cs` | **yeni** | Ürün adı / sürüm / telif / tagline (assembly attribute'larından). |
| `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs` | **yeni** | Tanı satırları (saf) + panoya gidecek düz metin. |
| `src/BuildOrchestrator.App/Services/ThirdPartyNotices.cs` | **yeni** | Üçüncü-taraf bileşen tablosu + runtime sürüm çözümü. |
| `src/BuildOrchestrator.App/Views/AboutDialog.xaml(.cs)` | **yeni** | Modal kabuk + üç sekme (ince view). |
| `src/BuildOrchestrator.App/Resources/Icons.xaml` | değişir | `Icon.Info` + `Icon.Info.StrokeThickness`. |
| `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs` | değişir | Rozetler katalogdan; iki literal silinir. |
| `src/BuildOrchestrator.App/Shell/KeyboardShortcuts.cs` | değişir | `WindowIntent.ShowAbout` + F1 satırı. |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` | değişir | `EngineVersion` / `EnginePid` saklanır. |
| `src/BuildOrchestrator.App/MainWindow.xaml(.cs)` | değişir | Info butonu · `BrandLogo` · `AboutOverlay` · F1 kablajı · Esc zinciri. |
| `Directory.Build.props` | değişir | `<Copyright>`. |
| `tests/…/App/AboutDialogHost.cs` | **yeni** | Realize + Open edilmiş About kuran TEK yer (SettingsDialogHost'un eşi). |
| `tests/…/App/FocusTrap.cs` | **yeni** | Modal odak-tuzağı iddiası — Settings ve About paylaşır. |
| `tests/…/App/SettingsDialogFocusTests.cs` | değişir | Odak-tuzağı gövdesi `FocusTrap`'e devredilir (davranış aynı). |
| `tests/…/App/ShortcutCatalogTests.cs` | **yeni** | Task 1 |
| `tests/…/App/BrandLogoTests.cs` | **yeni** | Task 2 |
| `tests/…/App/AppIdentityTests.cs` | **yeni** | Task 3 |
| `tests/…/App/DiagnosticsReportTests.cs` | **yeni** | Task 4 |
| `tests/…/App/ThirdPartyNoticesTests.cs` | **yeni** | Task 5 |
| `tests/…/App/AboutDialogTests.cs` | **yeni** | Task 6 |
| `tests/…/App/AboutWiringTests.cs` | **yeni** | Task 7 |
| `tests/…/App/KeyboardWiringTests.cs` | değişir | Bağlama sayısı 5 → 6 (yeniden yazılır, gerekçesiyle) |
| `ARCHITECTURE.md`, `README.md` | değişir | Task 8 |

**Task sırası bağımlılık zinciridir:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8. Task 6 (dialog) Task 1-5'in ürettiği
tiplere; Task 7 (MainWindow) Task 6'ya bağlıdır.

---

## Task 1: ShortcutCatalog — kısayol metinleri tek kaynağa

**Files:**
- Create: `src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs`
- Modify: `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs:79-87` (`ComposeItems`)
- Test: `tests/BuildOrchestrator.Tests/App/ShortcutCatalogTests.cs` (yeni)

**Interfaces:**
- Consumes: `KeyboardShortcuts.WindowBindings` (`IReadOnlyList<WindowBinding>`, `WindowBinding(Key, ModifierKeys, WindowIntent)`),
  `HotkeyBinding.DefaultGesture` (`const string = "Alt+B"`).
- Produces:
  - `enum ShortcutId { Build, Rebuild, FocusFilter, Escape, RestoreFromTray }` — Task 7 buna `About` ekler.
  - `readonly record struct ShortcutEntry(ShortcutId Id, IReadOnlyList<string> Gestures, string Description)`
  - `static class ShortcutCatalog` · `IReadOnlyList<ShortcutEntry> All` · `ShortcutEntry Get(ShortcutId id)`
    · `string Format(Key key, ModifierKeys modifiers)`

**Bağlam — neden bu task var:** `"F5"` ve `"Ctrl+F5"` metinleri bugün `BuildMenu.ComposeItems`'ta elle yazılı,
gerçek bağlamalar ise `KeyboardShortcuts.WindowBindings`'te. About'un kısayol tablosu üçüncü bir kopya olurdu.
Doğrulandı (`grep -F` ile, `src/BuildOrchestrator.App` altında): bu iki literal **yalnız** `Views/BuildMenu.xaml.cs:83-84`'te
geçiyor; `"Shift+F5"`, `"Ctrl+F"`, `"Esc"` hiçbir yerde yok.

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/ShortcutCatalogTests.cs`:

```csharp
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
    /// <see cref="ShortcutCatalog.Format"/> tarafından ÜRETİLİR; hiçbir üretim dosyası onu literal olarak
    /// yazmaz. (<c>"Alt+B"</c> listede YOK: onun tek kaynağı <see cref="HotkeyBinding.DefaultGesture"/>'dır
    /// ve katalog oradan okur. Yorum satırlarındaki tırnaksız <c>Ctrl+F5</c> anlatımı taramaya girmez —
    /// aranan şey TIRNAKLI literaldir.)
    /// </summary>
    [Fact]
    public void No_app_source_file_writes_a_key_gesture_as_a_literal()
    {
        string[] literals = ["\"F5\"", "\"Ctrl+F5\"", "\"Shift+F5\"", "\"Ctrl+F\"", "\"Esc\"", "\"F1\""];
        var offenders = new List<string>();
        foreach (string file in RepoPaths.AppSourceFiles("*.cs").Concat(RepoPaths.AppSourceFiles("*.xaml")))
        {
            string text = File.ReadAllText(file);
            foreach (string literal in literals)
                if (text.Contains(literal, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.AppSrcRoot, file)} → {literal}");
        }
        Assert.Empty(offenders);
    }
}
```

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ShortcutCatalogTests"`

Expected: derleme hatası — `ShortcutCatalog` / `ShortcutId` tipleri yok. (Bu geçerli bir kırmızıdır: tip
yoksa test derlenmez. `No_app_source_file_writes_a_key_gesture_as_a_literal`'ın gerçek kırmızısı Step 4'te
görünür — orada `ShortcutCatalog` derlenmiş ama `BuildMenu` hâlâ literal taşıyor olacak.)

- [ ] **Step 3: `ShortcutCatalog`'u yaz**

`src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs`:

```csharp
using System.Windows.Input;

namespace BuildOrchestrator.App.Shell;

/// <summary>Kullanıcıya GÖSTERİLEN bir kısayolun kimliği. <see cref="WindowIntent"/>'ten ayrıdır: niyet
/// "hangi tuş neyi tetikler", bu ise "hangi satır listelenir/rozetlenir".</summary>
public enum ShortcutId
{
    Build,
    Rebuild,
    FocusFilter,
    Escape,
    /// <summary>Global kısayol (tepsiden pencereyi getir) — <see cref="KeyboardShortcuts.WindowBindings"/>'te
    /// DEĞİLDİR, <see cref="HotkeyBinding"/> üzerinden kaydedilir.</summary>
    RestoreFromTray,
}

/// <summary>Bir kısayol satırı: jest metin(ler)i + tek cümlelik açıklama.</summary>
public readonly record struct ShortcutEntry(ShortcutId Id, IReadOnlyList<string> Gestures, string Description);

/// <summary>
/// Kullanıcıya gösterilen kısayol metinlerinin TEK kaynağı — About diyaloğunun tablosu, Build menüsünün
/// <c>Ds.Kbd</c> rozetleri ve ikon butonlarının tooltip'leri hep buradan okur.
///
/// <para><b>Jestler ELLE YAZILMAZ:</b> <see cref="Format"/> onları <see cref="KeyboardShortcuts.WindowBindings"/>
/// satırlarından türetir (global kısayol için <see cref="HotkeyBinding.DefaultGesture"/>). Böylece bir bağlama
/// değişince gösterilen metin de kendiliğinden değişir. Önceki hâlde <c>"F5"</c>/<c>"Ctrl+F5"</c>
/// <c>BuildMenu.ComposeItems</c>'ta bağımsız literallerdi — bağlama tablosuyla sessizce ayrışabilirlerdi
/// (ShortcutCatalogTests kaynak guard'ı bunu bir daha mümkün kılmaz).</para>
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

    /// <summary>Bir niyete bağlı TÜM jestler, tabloda göründükleri sırayla (ör. Rebuild → Ctrl+F5, Shift+F5).</summary>
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

    /// <summary>Tek kayıt. Eksik ya da ikiz bir kimlik burada fırlatır (sessiz yanlış satır üretmez).</summary>
    public static ShortcutEntry Get(ShortcutId id) => All.Single(e => e.Id == id);
}
```

- [ ] **Step 4: Kaynak guard'ının GERÇEK kırmızısını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ShortcutCatalogTests"`

Expected: `No_app_source_file_writes_a_key_gesture_as_a_literal` FAIL — çıktıda tam olarak iki ihlal:
`Views\BuildMenu.xaml.cs → "F5"` ve `Views\BuildMenu.xaml.cs → "Ctrl+F5"`.
Diğer testler PASS. (Başka bir dosya listeleniyorsa DUR ve neden diye bak — bu plan iki ihlal bekliyor.)

- [ ] **Step 5: `BuildMenu.ComposeItems`'ı katalogdan besle**

`src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs` — `using BuildOrchestrator.App.Shell;` ekle, sonra
`ComposeItems` gövdesindeki iki literali değiştir:

```csharp
        var items = new List<BuildMenuItem>();
        // [About] Rozet metni ARTIK literal DEĞİL: ShortcutCatalog jesti bağlama tablosundan türetir, böylece
        // bir bağlama değişirse rozet de değişir (kopya YASAK — ShortcutCatalogTests kaynak guard'ı pinler).
        items.Add(new("build", "Build",
            stopped ? "Start over — only changed projects" : "Only changed projects",
            ShortcutCatalog.Get(ShortcutId.Build).Gestures[0]));
        items.Add(new("rebuild", "Rebuild", Inv($"All {total} projects — cache ignored"),
            ShortcutCatalog.Get(ShortcutId.Rebuild).Gestures[0]));
```

`Gestures[0]`: Build için `"F5"`, Rebuild için `"Ctrl+F5"` (`WindowBindings`'te Control satırı Shift'ten
öncedir). Menüde tek rozet gösterilir — bu davranış değişmiyor.

- [ ] **Step 6: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ShortcutCatalogTests|FullyQualifiedName~ActionBarTests|FullyQualifiedName~KeyboardWiringTests"`

Expected: hepsi PASS. `ActionBarTests`'in mevcut rozet testleri (`The_F5_badge_stays_on_build_even_when_stopped`,
`Build_menu_desc_texts_are_verbatim_for_build_and_rebuild`) **değiştirilmeden** yeşil kalmalı — davranış aynı,
yalnız metnin geldiği yer değişti.

- [ ] **Step 7: Commit**

```bash
git add src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs tests/BuildOrchestrator.Tests/App/ShortcutCatalogTests.cs
git commit -m "feat(shortcuts): kisayol jestleri icin tek kaynak (ShortcutCatalog)"
```

---

## Task 2: BrandLogo — Delta logosu tek kontrole

**Files:**
- Create: `src/BuildOrchestrator.App/Controls/BrandLogo.xaml` + `BrandLogo.xaml.cs`
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml:146-168` (logo bloğu)
- Test: `tests/BuildOrchestrator.Tests/App/BrandLogoTests.cs` (yeni)
- Yeşil kalmalı: `tests/…/App/TitleBarContextTests.cs:141`, `tests/…/App/MainWindowRealizeTests.cs:194`

**Interfaces:**
- Produces: `BuildOrchestrator.App.Controls.BrandLogo : UserControl` — kökü `Viewbox Stretch="Uniform"`.
  Kullanım: `<controls:BrandLogo Height="15" />`. Yeni public üye YOK.

**Bağlam:** Logo Path'leri bugün `MainWindow.xaml`'de inline. About hero'suna kopyalamak "Kopya YASAK"
ihlali olurdu.

- [ ] **Step 1: Kırmızı testi yaz**

`tests/BuildOrchestrator.Tests/App/BrandLogoTests.cs`:

```csharp
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Marka logosu TEK yerde çizilir. Önceden Path verisi <c>MainWindow.xaml</c>'de inline duruyordu; About
/// hero'su onu ikinci kez yazacaktı (kopya YASAK, CLAUDE.md) — bu yüzden bir kontrole çıkarıldı.
/// </summary>
[Collection("Console UI (serial)")]
public class BrandLogoTests
{
    /// <summary>Logonun ayırt edici ilk figürü (amber accent parçası, design-v1 delta-logo-dark.svg).
    /// Bu dizgi kaynak ağacında BİR kez geçmelidir.</summary>
    private const string SignatureFigure = "M81.069,13.488";

    [Fact]
    public void The_logo_geometry_is_declared_in_exactly_one_source_file()
    {
        var carriers = RepoPaths.AppSourceFiles("*.xaml")
            .Concat(RepoPaths.AppSourceFiles("*.cs"))
            .Where(f => File.ReadAllText(f).Contains(SignatureFigure, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([Path.Combine("Controls", "BrandLogo.xaml")], carriers);
    }

    /// <summary>Kontrol GERÇEKTEN çiziyor: realize edildiğinde ağaçta boyanmış Path'ler var ve verilen
    /// yükseklikte oranını korur (Viewbox Uniform).</summary>
    [StaFact]
    public void The_logo_renders_its_paths_and_keeps_its_aspect_ratio()
    {
        var host = DsResources.NewHost();
        var logo = new BrandLogo { Height = 20 };
        var window = DsResources.Realize(host, logo);

        var paths = DsResources.Descendants(logo).OfType<System.Windows.Shapes.Path>().ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.NotNull(p.Fill)); // token fırçası çözüldü (hardcode YASAK guard'ı ayrıca koşar)

        Assert.IsType<Viewbox>(logo.Content);
        Assert.Equal(20.0, logo.ActualHeight, precision: 1);
        Assert.True(logo.ActualWidth > logo.ActualHeight, "Uniform ölçek oranı korumadı (logo geniştir)");
        GC.KeepAlive(window);
    }
}
```

- [ ] **Step 2: Testi koştur, KIRMIZI olduğunu gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BrandLogoTests"`

Expected: derleme hatası — `BrandLogo` tipi yok.

- [ ] **Step 3: `BrandLogo` kontrolünü oluştur**

**Bu bir TAŞIMA işlemidir, yeniden yazım değil.** `MainWindow.xaml`'de `<Viewbox x:Name="TitleBarLogo" …>`
açılış etiketinden onun kapanış `</Viewbox>`'una kadarki bloğu **kes** (satır ~148-168) ve aşağıdaki iskeletin
içine **olduğu gibi yapıştır**. Tek bir koordinatı, `RenderTransform`'u, `Canvas.Left`'i ya da
`DynamicResource` anahtarını **değiştirme veya yeniden yazma** — Path verisi design-v1
`delta-logo-dark.svg`'den birebir gelir ve testin `SignatureFigure` araması ile mevcut ölçü testleri buna
dayanır. Kesilen bloğun kök `Viewbox`'ından yalnız `x:Name="TitleBarLogo"` ve `VerticalAlignment` nitelikleri
düşürülür (bunlar artık kullanım yerinde verilir); `Stretch="Uniform"` **kalır**.

`src/BuildOrchestrator.App/Controls/BrandLogo.xaml`:

```xml
<UserControl x:Class="BuildOrchestrator.App.Controls.BrandLogo"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- [About] Delta logosunun TEK çizim yeri. Önceden MainWindow.xaml'de inline duruyordu; About hero'su
       ikinci bir kopya yazacaktı (kopya YASAK, CLAUDE.md). Path verisi design-v1 delta-logo-dark.svg'den
       BİREBİR gelir — yeniden çizim/yuvarlama YOK. Renkler token'dan (amber accent + text-primary). -->
  <Viewbox Stretch="Uniform">
    <!-- ↓ MainWindow.xaml'den KESİLEN <Canvas Width="132.574" Height="33"> … </Canvas> ağacı buraya,
         DEĞİŞTİRİLMEDEN. -->
  </Viewbox>
</UserControl>
```

Yapıştırdıktan sonra doğrula: `Canvas Width="132.574" Height="33"`, iç `Canvas Canvas.Left="0.5"`, ilk Path'in
`Fill="{DynamicResource Brush.Amber}"` ve `Figures`ının `M81.069,13.488` ile başlaması.

`src/BuildOrchestrator.App/Controls/BrandLogo.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>[About] Delta marka logosu — uygulamadaki TEK çizimi. Tüketiciler yalnız
/// <see cref="FrameworkElement.Height"/> verir; genişlik Viewbox'ın Uniform ölçeğinden gelir
/// (title bar 15px, About hero 20px).</summary>
public partial class BrandLogo : UserControl
{
    public BrandLogo() => InitializeComponent();
}
```

> **Taşıma kuralı:** `MainWindow.xaml`'deki `Canvas`/`Path` ağacını kopyala-yapıştır **olduğu gibi** al.
> Tek bir koordinatı, transform'u veya `DynamicResource` anahtarını değiştirme — testin `SignatureFigure`
> araması ve mevcut ölçü testleri buna dayanır.

- [ ] **Step 4: `MainWindow.xaml`'deki logoyu kontrolle değiştir**

`MainWindow.xaml:146-168` arasındaki yorum + `Viewbox x:Name="TitleBarLogo"` bloğunun tamamı şununla değişir:

```xml
            <!-- [T35 → About] Logo ARTIK Controls/BrandLogo.xaml'de (tek çizim yeri); burada yalnız yüksekliği
                 verilir. design-v1 README §1.1/§2.1: "Delta logosu (dark varyant, 15px yükseklik)". -->
            <controls:BrandLogo x:Name="TitleBarLogo" Height="15" VerticalAlignment="Center" />
```

`xmlns:controls` zaten tanımlı (`MainWindow.xaml:5`). `x:Name="TitleBarLogo"` **korunur** — iki mevcut test
alanı bu adla okuyor; alanın tipi `Viewbox`'tan `BrandLogo`'ya döner, ikisi de `FrameworkElement`'tir ve
testlerin kullandığı üyeler (`Height`, `ActualHeight`, `TranslatePoint`) değişmez.

- [ ] **Step 5: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BrandLogoTests|FullyQualifiedName~TitleBarContextTests|FullyQualifiedName~MainWindowRealizeTests"`

Expected: hepsi PASS. Özellikle `The_title_bar_logo_is_fifteen_pixels_tall` (Height=15 + ActualHeight≈15) ve
`MainWindowRealizeTests`'in logo-ortalama testi (`AlignmentSteps` ağaçtan hesaplandığı için bir seviye artışını
yutar) yeşil kalmalı. **Ortalama testi kırılırsa** `BrandLogo`'nun kökündeki `Viewbox`'a
`VerticalAlignment="Center"` ekle — kontrolün kendisine değil, iç Viewbox'a.

- [ ] **Step 6: Commit**

```bash
git add src/BuildOrchestrator.App/Controls/BrandLogo.xaml src/BuildOrchestrator.App/Controls/BrandLogo.xaml.cs src/BuildOrchestrator.App/MainWindow.xaml tests/BuildOrchestrator.Tests/App/BrandLogoTests.cs
git commit -m "refactor(app): Delta logosu tek kontrole (BrandLogo)"
```

---

## Task 3: Ürün kimliği — AppIdentity, Copyright, motor sürümü/PID

**Files:**
- Create: `src/BuildOrchestrator.App/Services/AppIdentity.cs`
- Modify: `Directory.Build.props:19-20` (`<Copyright>` ekle)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1206-1209` (`OnEngineReady`)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs:587` (çağrı)
- Test: `tests/BuildOrchestrator.Tests/App/AppIdentityTests.cs` (yeni)

**Interfaces:**
- Produces:
  - `static class AppIdentity` · `string Product` · `string Version` · `string Copyright` · `const string Tagline`
  - `RunViewModel.EngineVersion` (`string?`), `RunViewModel.EnginePid` (`int?`)
  - `RunViewModel.OnEngineReady(string engineVersion, int pid)` — **imza değişti** (eskisi tek parametreliydi)

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/AppIdentityTests.cs`:

```csharp
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Ürün kimliği TEK kaynaktan gelir: <c>Directory.Build.props</c> → assembly attribute'ları →
/// <see cref="AppIdentity"/>. UI hiçbir yerde ürün adını, sürümü ya da telif yılını yeniden yazmaz.
/// </summary>
[Collection("Console UI (serial)")]
public class AppIdentityTests
{
    private static readonly XNamespace None = "";
    private static XDocument Props() =>
        XDocument.Load(Path.Combine(RepoPaths.RepoRoot, "Directory.Build.props"));

    [Fact]
    public void Product_and_version_come_from_the_assembly_not_from_a_literal()
    {
        var assembly = typeof(AppIdentity).Assembly;
        Assert.Equal(assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product, AppIdentity.Product);
        Assert.Equal(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            AppIdentity.Version);
    }

    /// <summary>Telif TEK PARÇA olarak attribute'tan gelir — yıl ve şirket adı UI'da birleştirilmez
    /// ve <c>DateTime.Now.Year</c> KULLANILMAZ (telif yılı bir çalışma-zamanı değeri değildir).</summary>
    [Fact]
    public void Copyright_is_declared_once_in_directory_build_props()
    {
        string declared = Props().Descendants(None + "Copyright").Single().Value;
        Assert.False(string.IsNullOrWhiteSpace(declared));
        Assert.Equal(declared, AppIdentity.Copyright);
        Assert.Equal(declared,
            typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
    }

    [Fact]
    public void The_tagline_is_a_single_sentence()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppIdentity.Tagline));
        Assert.EndsWith(".", AppIdentity.Tagline, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ motor kimliği

    /// <summary>Motor sürümü + PID artık SAKLANIR. Önceden <c>OnEngineReady</c> sürümü yalnız konsol satırına
    /// yazıp atıyordu ve <c>EngineReadyEvent.Pid</c> hiç kullanılmıyordu; About ikisini de gösterir.</summary>
    [StaFact]
    public async Task Engine_ready_stores_the_version_and_pid_and_still_writes_the_boot_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1");

        Assert.Null(vm.EngineVersion);
        Assert.Null(vm.EnginePid);

        vm.OnEngineReady("1.0.0+it5", 4242);

        Assert.Equal("1.0.0+it5", vm.EngineVersion);
        Assert.Equal(4242, vm.EnginePid);
        // Konsolun boot satırı DEĞİŞMEDİ (davranış aynı, yalnız değer ayrıca saklanıyor).
        Assert.Contains("Engine ready — v1.0.0+it5", vm.GetActiveLines(), StringComparison.Ordinal);
    }
}
```

> **Not:** son assertion `RunViewModel`'in aktif konsol tamponunu okur. Projede bunun yüzeyi
> `GetActiveLineCount()`; **tam metin okuyan bir yüzey yoksa** assertion'ı `Assert.Equal(1, vm.GetActiveLineCount())`
> ile değiştir ve doc'una "boot satırı hâlâ yazılıyor" yaz — yeni bir test yüzeyi AÇMA.

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AppIdentityTests"`

Expected: derleme hatası — `AppIdentity` yok, `OnEngineReady` iki parametre almıyor, `EngineVersion`/`EnginePid` yok.

- [ ] **Step 3: `Directory.Build.props`'a telif ekle**

`Directory.Build.props`, `<Company>Delta</Company>` satırının hemen ardına:

```xml
    <!-- [About] Telif TEK PARÇA burada yazılır: About hero'su AssemblyCopyrightAttribute'tan okur, yıl ve
         şirket adını UI'da birleştirmez (DateTime.Now.Year bir telif yılı DEĞİLDİR). -->
    <Copyright>© 2026 Delta</Copyright>
```

- [ ] **Step 4: `AppIdentity`'yi yaz**

`src/BuildOrchestrator.App/Services/AppIdentity.cs`:

```csharp
using System.Reflection;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [About] Uygulamanın kendi kimliği — About hero'sunun ve tanı raporunun okuduğu TEK yer. Değerler
/// <c>Directory.Build.props</c>'tan assembly attribute'larına, oradan buraya akar; UI'da ürün adı, sürüm ya da
/// telif metni YENİDEN YAZILMAZ.
/// </summary>
public static class AppIdentity
{
    private static readonly Assembly Self = typeof(AppIdentity).Assembly;

    /// <summary>`Directory.Build.props` → `<Product>`.</summary>
    public static string Product { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? Self.GetName().Name ?? "";

    /// <summary>`Directory.Build.props` → `<InformationalVersion>` (teslim etiketi dahil, ör. `1.0.0+it5`).</summary>
    public static string Version { get; } =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Self.GetName().Version?.ToString(3) ?? "";

    /// <summary>`Directory.Build.props` → `<Copyright>`.</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>About hero'sundaki tek cümlelik ürün tanımı. Bunun bir assembly attribute karşılığı YOKTUR
    /// (`AssemblyDescription` MSBuild'de `<Description>` ile kurulur ve paket açıklamasıdır) — metnin tek
    /// yeri burasıdır.</summary>
    public const string Tagline = "Ordered, incremental builds for a multi-project .NET solution.";
}
```

- [ ] **Step 5: `RunViewModel`'de motor kimliğini sakla**

`src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` — `OnEngineReady`'yi (satır ~1206-1209) değiştir:

```csharp
    /// <summary>[About] Motor kimliği — About'un Environment sekmesi bunu gösterir. Motor doğmadan önce
    /// <c>null</c>'dır.</summary>
    public string? EngineVersion { get; private set; }

    /// <summary>[About] Motor process'inin PID'i (<c>EngineReadyEvent.Pid</c>). Önceden bu değer olaydan
    /// okunup ATILIYORDU.</summary>
    public int? EnginePid { get; private set; }

    /// <summary>[D1 review · C5] Motor hazır: konsolun boot satırında sürüm gösterilir (design-v1 §2.5 anlatı
    /// dili — "Build started — 14 projects, parallelism 4" ile aynı kalıp). Sürüm kimliği TEK kaynaktan gelir:
    /// <c>Directory.Build.props</c> → Supervisor assembly'sinin InformationalVersion'ı → <c>engineReady</c>.
    /// <para>[About] Değerler ayrıca SAKLANIR (About'un Environment sekmesi okur); boot satırı değişmedi.</para></summary>
    public void OnEngineReady(string engineVersion, int pid)
    {
        EngineVersion = engineVersion;
        EnginePid = pid;
        AppendRunLine($"Engine ready — v{engineVersion}");
    }
```

- [ ] **Step 6: Çağrı yerini güncelle**

`src/BuildOrchestrator.App/MainWindow.xaml.cs:587`:

```csharp
            _vm.OnEngineReady(ready.EngineVersion, ready.Pid);
```

- [ ] **Step 7: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AppIdentityTests|FullyQualifiedName~PublishLayoutTests"`

Expected: hepsi PASS. `PublishLayoutTests` `Directory.Build.props`'u okur — `<Copyright>` eklenmesi onu
kırmamalı.

- [ ] **Step 8: Commit**

```bash
git add Directory.Build.props src/BuildOrchestrator.App/Services/AppIdentity.cs src/BuildOrchestrator.App/ViewModels/RunViewModel.cs src/BuildOrchestrator.App/MainWindow.xaml.cs tests/BuildOrchestrator.Tests/App/AppIdentityTests.cs
git commit -m "feat(app): urun kimligi tek kaynaktan (AppIdentity + copyright + motor surumu/PID)"
```

---

## Task 4: DiagnosticsReport — tanı satırları ve pano metni

**Files:**
- Create: `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs`
- Test: `tests/BuildOrchestrator.Tests/App/DiagnosticsReportTests.cs` (yeni)

**Interfaces:**
- Produces:
  - `readonly record struct DiagnosticsLine(string Label, string Value)`
  - `sealed record DiagnosticsInput(string AppVersion, string? EngineVersion, int? EnginePid, string Runtime, string Os, string MsBuild, string RepositoryRoot, string StateFile, string LogsRoot, string WorktreePool)`
  - `static class DiagnosticsReport` · `IReadOnlyList<DiagnosticsLine> Compose(DiagnosticsInput)` ·
    `string ToText(IReadOnlyList<DiagnosticsLine>)` · `const string NotStarted/Unknown/NoRepository/Resolving`

**Bağlam:** Environment sekmesinin çizdiği satırlar ile "Copy diagnostics"in ürettiği metin AYNI modelden
gelmeli — iki ayrı liste sessizce ayrışır. Etiket metinleri burada tanımlanır, XAML'de tekrar yazılmaz
(sekme bir `ItemsControl`'dür).

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/DiagnosticsReportTests.cs`:

```csharp
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Tanı raporu SAF: Environment sekmesinin satırları ile "Copy diagnostics"in panoya yazdığı metin AYNI
/// modelden üretilir (iki ayrı liste sessizce ayrışırdı). Etiket metinleri burada tanımlanır — XAML onları
/// tekrar yazmaz, satırları bir ItemsControl olarak çizer.
/// </summary>
public class DiagnosticsReportTests
{
    private static DiagnosticsInput Full() => new(
        AppVersion: "1.0.0+it5",
        EngineVersion: "1.0.0+it5",
        EnginePid: 4242,
        Runtime: ".NET 10.0.0",
        Os: "Microsoft Windows 10.0.26200",
        MsBuild: @"C:\VS\MSBuild.exe (v17.9.8)",
        RepositoryRoot: @"D:\repo",
        StateFile: @"C:\state\ui-state.json",
        LogsRoot: @"C:\state\logs",
        WorktreePool: @"C:\state\worktrees");

    [Fact]
    public void Every_input_field_reaches_exactly_one_line()
    {
        var lines = DiagnosticsReport.Compose(Full());
        // Her satırın etiketi TEKİL ve değeri dolu.
        Assert.Equal(lines.Select(l => l.Label).Distinct(StringComparer.Ordinal).Count(), lines.Count);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Label)));
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Value)));

        // Girdideki her değer çıktıda GÖRÜNÜR — bir alanı satıra bağlamayı unutmak burada yakalanır.
        var values = lines.Select(l => l.Value).ToList();
        foreach (string expected in new[]
                 {
                     "1.0.0+it5", "4242", ".NET 10.0.0", "Microsoft Windows 10.0.26200",
                     @"C:\VS\MSBuild.exe (v17.9.8)", @"D:\repo",
                     @"C:\state\ui-state.json", @"C:\state\logs", @"C:\state\worktrees",
                 })
            Assert.Contains(values, v => v.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>Motor doğmamışken satır KAYBOLMAZ — kullanıcı "engine yok" bilgisini de görmeli.</summary>
    [Fact]
    public void A_missing_engine_reads_as_not_started_instead_of_disappearing()
    {
        var lines = DiagnosticsReport.Compose(Full() with { EngineVersion = null, EnginePid = null });
        int before = DiagnosticsReport.Compose(Full()).Count;

        Assert.Equal(before, lines.Count);
        Assert.Contains(lines, l => l.Value == DiagnosticsReport.NotStarted);
        Assert.Contains(lines, l => l.Value == DiagnosticsReport.Unknown);
    }

    [Fact]
    public void An_empty_repository_root_reads_as_no_repository()
    {
        var lines = DiagnosticsReport.Compose(Full() with { RepositoryRoot = "" });
        Assert.Contains(lines, l => l.Value == DiagnosticsReport.NoRepository);
    }

    [Fact]
    public void The_clipboard_text_carries_every_line_as_label_and_value()
    {
        var lines = DiagnosticsReport.Compose(Full());
        string text = DiagnosticsReport.ToText(lines);

        foreach (var line in lines)
        {
            Assert.Contains(line.Label, text, StringComparison.Ordinal);
            Assert.Contains(line.Value, text, StringComparison.Ordinal);
        }
        // Satır başına bir satır — pano metni yapıştırılabilir olmalı.
        Assert.Equal(lines.Count, text.Split('\n').Count(l => l.Trim().Length > 0));
    }

    /// <summary>Değerler AYNI kolonda başlar: bir destek talebine yapıştırıldığında okunabilir olsun.</summary>
    [Fact]
    public void The_clipboard_text_aligns_the_values_in_one_column()
    {
        var lines = DiagnosticsReport.Compose(Full());
        var columns = DiagnosticsReport.ToText(lines)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.IndexOf(lines.First(x => l.StartsWith(x.Label, StringComparison.Ordinal)).Value,
                                   StringComparison.Ordinal))
            .Distinct()
            .ToList();
        Assert.Single(columns);
    }
}
```

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~DiagnosticsReportTests"`

Expected: derleme hatası — `DiagnosticsReport` / `DiagnosticsInput` yok.

- [ ] **Step 3: `DiagnosticsReport`'u yaz**

`src/BuildOrchestrator.App/Services/DiagnosticsReport.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace BuildOrchestrator.App.Services;

/// <summary>Tanı tablosunun bir satırı: etiket + gösterilecek değer.</summary>
public readonly record struct DiagnosticsLine(string Label, string Value);

/// <summary>[About] Tanı raporunun ham girdisi — TOPLAMAYI çağıran yapar (Environment/RuntimeInformation/
/// MsBuildResolver), bu tip yalnız taşır. Böylece rapor mantığı WPF'siz ve process'siz test edilir.</summary>
public sealed record DiagnosticsInput(
    string AppVersion,
    string? EngineVersion,
    int? EnginePid,
    string Runtime,
    string Os,
    string MsBuild,
    string RepositoryRoot,
    string StateFile,
    string LogsRoot,
    string WorktreePool);

/// <summary>
/// [About] Environment sekmesinin çizdiği satırlar ve "Copy diagnostics"in panoya yazdığı metin — TEK
/// modelden. İkisi ayrı listelerden üretilseydi biri güncellenip diğeri unutulurdu.
/// <para>SAF: hiçbir şey okumaz/çalıştırmaz. Etiket metinleri BURADA tanımlanır; XAML onları tekrar yazmaz
/// (sekme bir <c>ItemsControl</c>'dür).</para>
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>Motor henüz doğmadı.</summary>
    public const string NotStarted = "not started";
    /// <summary>Değer bu koşulda YOK (ör. motor doğmadığı için PID).</summary>
    public const string Unknown = "—";
    /// <summary>Henüz bir repo kökü seçilmemiş.</summary>
    public const string NoRepository = "no repository";
    /// <summary>MSBuild çözümü sürüyor (vswhere child process'i).</summary>
    public const string Resolving = "resolving…";

    public static IReadOnlyList<DiagnosticsLine> Compose(DiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return
        [
            new("App version", input.AppVersion),
            new("Engine version", Or(input.EngineVersion, NotStarted)),
            new("Engine PID", input.EnginePid is { } pid
                ? pid.ToString(CultureInfo.InvariantCulture) : Unknown),
            new(".NET runtime", input.Runtime),
            new("OS", input.Os),
            new("MSBuild", input.MsBuild),
            new("Repository root", Or(input.RepositoryRoot, NoRepository)),
            new("State file", input.StateFile),
            new("Logs", input.LogsRoot),
            new("Worktree pool", input.WorktreePool),
        ];
    }

    /// <summary>Panoya gidecek düz metin — değerler tek kolonda hizalanır (destek talebine yapıştırılır).</summary>
    public static string ToText(IReadOnlyList<DiagnosticsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int width = lines.Count == 0 ? 0 : lines.Max(l => l.Label.Length);
        var text = new StringBuilder();
        foreach (var line in lines)
            text.Append(line.Label.PadRight(width)).Append("  ").AppendLine(line.Value);
        return text.ToString();
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
```

- [ ] **Step 4: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~DiagnosticsReportTests"`

Expected: PASS (7 test).

- [ ] **Step 5: Commit**

```bash
git add src/BuildOrchestrator.App/Services/DiagnosticsReport.cs tests/BuildOrchestrator.Tests/App/DiagnosticsReportTests.cs
git commit -m "feat(about): tani raporu (DiagnosticsReport) — sekme ve pano tek modelden"
```

---

## Task 5: ThirdPartyNotices — üçüncü-taraf bileşenler

**Files:**
- Create: `src/BuildOrchestrator.App/Services/ThirdPartyNotices.cs`
- Test: `tests/BuildOrchestrator.Tests/App/ThirdPartyNoticesTests.cs` (yeni)

**Interfaces:**
- Produces:
  - `readonly record struct ThirdPartyComponent(string DisplayName, string? AssemblyName, string License, string Url)`
  - `static class ThirdPartyNotices` · `IReadOnlyList<ThirdPartyComponent> All` ·
    `string? ResolveVersion(ThirdPartyComponent)` · `const string FontLicenseNote`

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/ThirdPartyNoticesTests.cs`:

```csharp
using System.IO;
using System.Xml.Linq;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Üçüncü-taraf atıf tablosu. <b>Sürümler csproj'dan KOPYALANMAZ</b> — çalışma zamanında yüklenen
/// assembly'den okunur; yoksa csproj'daki sürümü UI'da ikinci kez yazmış olurduk (kopya YASAK).
/// </summary>
public class ThirdPartyNoticesTests
{
    private static readonly XNamespace None = "";

    private static XDocument AppCsproj() => XDocument.Load(
        Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj"));

    [Fact]
    public void Every_component_has_a_display_name_a_license_and_a_url()
    {
        Assert.NotEmpty(ThirdPartyNotices.All);
        foreach (var component in ThirdPartyNotices.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(component.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(component.License));
            Assert.StartsWith("https://", component.Url, StringComparison.Ordinal);
        }
    }

    /// <summary>Assembly adı verilen her kaydın sürümü GERÇEKTEN çözülür — yanlış yazılmış bir assembly
    /// adı UI'da sessizce boş bir sürüm göstermek yerine burada kırar.</summary>
    [Fact]
    public void Each_managed_component_resolves_a_version_at_runtime()
    {
        foreach (var component in ThirdPartyNotices.All.Where(c => c.AssemblyName is not null))
            Assert.False(string.IsNullOrWhiteSpace(ThirdPartyNotices.ResolveVersion(component)),
                $"{component.DisplayName}: '{component.AssemblyName}' assembly'si yüklenemedi");
    }

    /// <summary>Font kaydının assembly'si YOKTUR (gömülü OTF) — sürüm alanı boş kalır, kırmaz.</summary>
    [Fact]
    public void The_font_component_has_no_assembly_and_no_version()
    {
        var font = ThirdPartyNotices.All.Single(c => c.AssemblyName is null);
        Assert.Null(ThirdPartyNotices.ResolveVersion(font));
        Assert.Contains("Open Font License", font.License, StringComparison.Ordinal);
    }

    /// <summary>csproj'un her <c>PackageReference</c>'ı tabloda görünür — bir paket eklenip atıfı unutulursa
    /// burada yakalanır (OSS uyumluluğu bir "unutulabilir" iş değildir).</summary>
    [Fact]
    public void Every_package_reference_is_attributed()
    {
        var referenced = AppCsproj().Descendants(None + "PackageReference")
            .Select(e => (string)e.Attribute("Include")!)
            .ToList();
        var attributed = ThirdPartyNotices.All
            .Select(c => c.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(referenced, package => Assert.Contains(package, attributed));
    }

    [Fact]
    public void The_font_note_points_at_the_licence_file_that_actually_ships()
    {
        Assert.Contains("GEIST-LICENSE.txt", ThirdPartyNotices.FontLicenseNote, StringComparison.Ordinal);
        // Dosya gerçekten çıktıya kopyalanıyor mu — PublishLayoutTests csproj tarafını ayrıca pinler.
        Assert.True(File.Exists(Path.Combine(RepoPaths.AppSrcRoot, "Assets", "GEIST-LICENSE.txt")));
    }
}
```

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ThirdPartyNoticesTests"`

Expected: derleme hatası — `ThirdPartyNotices` yok.

- [ ] **Step 3: `ThirdPartyNotices`'i yaz**

`src/BuildOrchestrator.App/Services/ThirdPartyNotices.cs`:

```csharp
using System.Reflection;

namespace BuildOrchestrator.App.Services;

/// <summary>Bir üçüncü-taraf bileşen. <paramref name="AssemblyName"/> <c>null</c> ise yönetilen bir
/// assembly değildir (gömülü font) — sürüm alanı boş kalır.</summary>
public readonly record struct ThirdPartyComponent(
    string DisplayName, string? AssemblyName, string License, string Url);

/// <summary>
/// [About] Üçüncü-taraf atıfları. <b>Sürüm burada YAZILMAZ</b> — çalışma zamanında yüklü assembly'den
/// okunur; csproj'daki <c>Version</c> değerini UI'da ikinci kez yazmak kopya olurdu.
/// <para><see cref="DisplayName"/> csproj'daki <c>PackageReference Include</c> ile AYNI yazılır: bir paket
/// eklenip atıfı unutulursa ThirdPartyNoticesTests yakalar.</para>
/// </summary>
public static class ThirdPartyNotices
{
    /// <summary>OFL "included in all copies" şartı dosya olarak karşılanır; bu not atfı GÖRÜNÜR kılar.</summary>
    public const string FontLicenseNote =
        "The full Geist license text ships as GEIST-LICENSE.txt next to the application.";

    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new("AvalonEdit", "ICSharpCode.AvalonEdit", "MIT", "https://github.com/icsharpcode/AvalonEdit"),
        new("CommunityToolkit.Mvvm", "CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("H.NotifyIcon.Wpf", "H.NotifyIcon.Wpf", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
        new("Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection", "MIT",
            "https://github.com/dotnet/runtime"),
        new("Geist · Geist Mono", null, "SIL Open Font License 1.1", "https://github.com/vercel/geist-font"),
    ];

    /// <summary>Yüklü assembly'nin sürümü — InformationalVersion tercih edilir, build metadata (<c>+sha</c>)
    /// kırpılır. Bulunamazsa <c>null</c> (satır sürümsüz çizilir; UI patlamaz).</summary>
    public static string? ResolveVersion(ThirdPartyComponent component)
    {
        if (component.AssemblyName is not { Length: > 0 } name) return null;
        try
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string? version = informational ?? assembly.GetName().Version?.ToString(3);
            if (version is null) return null;
            int plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? version : version[..plus];
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
        {
            return null; // atıf satırı sürümsüz çizilir — About bir paket yüzünden AÇILMAMAZLIK etmez
        }
    }
}
```

> `FileNotFoundException` için `using System.IO;` gerekir.

- [ ] **Step 4: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ThirdPartyNoticesTests"`

Expected: PASS (5 test). `Each_managed_component_resolves_a_version_at_runtime` FAIL ederse, ilgili paketin
GERÇEK assembly adını çıktı dizininden doğrula
(`Get-ChildItem src/BuildOrchestrator.App/bin/Debug/net10.0-windows/*.dll | Select-Object Name`) ve tabloyu düzelt.

- [ ] **Step 5: Commit**

```bash
git add src/BuildOrchestrator.App/Services/ThirdPartyNotices.cs tests/BuildOrchestrator.Tests/App/ThirdPartyNoticesTests.cs
git commit -m "feat(about): ucuncu-taraf atiflari (ThirdPartyNotices)"
```

---

## Task 6: AboutDialog — ikon + modal + üç sekme

**Files:**
- Modify: `src/BuildOrchestrator.App/Resources/Icons.xaml` (`Icon.Info` + kalınlık)
- Create: `src/BuildOrchestrator.App/Views/AboutDialog.xaml` + `AboutDialog.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/AboutDialogHost.cs` (yeni fixture),
  `tests/BuildOrchestrator.Tests/App/AboutDialogTests.cs` (yeni),
  `tests/BuildOrchestrator.Tests/App/FocusTrap.cs` (yeni — ortak odak-tuzağı iddiası)
- Modify (test): `tests/BuildOrchestrator.Tests/App/SettingsDialogFocusTests.cs:51-84` — gövde `FocusTrap`'e
  devredilir, yerel `IsDescendantOf` silinir (davranış aynı)

**Interfaces:**
- Consumes: `ShortcutCatalog` (T1) · `BrandLogo` (T2) · `AppIdentity`, `RunViewModel.EngineVersion/EnginePid` (T3)
  · `DiagnosticsReport`, `DiagnosticsInput` (T4) · `ThirdPartyNotices` (T5) ·
  `JsonUiStateStore.DefaultPath`, `RunLogPaths.DefaultLogsRoot`, `WorktreeManager.DefaultPoolRoot` (mevcut) ·
  `CopyLogFeedback`, `ClipboardRetry.SetText` (mevcut, `BuildOrchestrator.App.Console`).
- Produces:
  - `AboutDialog : UserControl` · `void Open(RunViewModel run, bool hotkeyRegistered, Func<Task<string>> resolveMsBuild)`
    · `void CloseDialog()` · `Func<string, bool> ClipboardWriter { get; set; }`
    · `internal Grid Scrim` (XAML `x:Name`) · `internal bool IsShowingCopied`

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/AboutDialogHost.cs`:

```csharp
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Realize edilmiş + açılmış bir <see cref="AboutDialog"/> kuran TEK yer (<see cref="SettingsDialogHost"/>
/// deseninin eşi — kopya YASAK, CLAUDE.md). Fixture repo SEÇİLMİŞ durumu kurar: diyalog üretimde de
/// kullanıcı bir kök seçtikten sonra ulaşılan bir yüzeydir.
/// <para><b>MSBuild çözümü ENJEKTE EDİLİR</b> — test hiçbir koşulda <c>vswhere</c> başlatmaz (D8).</para>
/// </summary>
internal static class AboutDialogHost
{
    public const string FakeMsBuild = @"C:\VS\MSBuild.exe (v17.9.8)";

    /// <param name="backgroundSibling">Verilirse diyalog, bu kontrolle AYNI kökün altında realize edilir —
    /// odak tuzağı testi "Tab arka plandaki bir kontrole kaçıyor mu" sorusunu ancak böyle sorabilir. Verilmezse
    /// diyalog tek başına realize olur.</param>
    public static (AboutDialog dialog, RunViewModel run, IDisposable scope) OpenRealized(
        Action<RunViewModel>? configure = null,
        bool hotkeyRegistered = true,
        Func<Task<string>>? resolveMsBuild = null,
        System.Windows.FrameworkElement? backgroundSibling = null)
    {
        var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        configure?.Invoke(run);

        var host = DsResources.NewHost();
        var dialog = new AboutDialog();

        System.Windows.FrameworkElement content = dialog;
        if (backgroundSibling is not null)
        {
            var root = new System.Windows.Controls.Grid();
            root.Children.Add(backgroundSibling);
            root.Children.Add(dialog);
            content = root;
        }
        var window = DsResources.Realize(host, content);

        dialog.Open(run, hotkeyRegistered, resolveMsBuild ?? (() => Task.FromResult(FakeMsBuild)));
        content.UpdateLayout(); // Collapsed→Visible sonrası GERÇEK arrange

        return (dialog, run, new Scope(engine, window));
    }

    private sealed class Scope(EngineHost engine, System.Windows.Window window) : IDisposable
    {
        public void Dispose()
        {
            // SettingsDialogHost.Scope ile aynı gerekçe: motor hiç başlatılmadığı için senkron tamamlanır.
            engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.KeepAlive(window);
        }
    }
}
```

`tests/BuildOrchestrator.Tests/App/AboutDialogTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About modali. Kabuk Settings ile AYNI (scrim + 620px Ds.Dialog + odak tuzağı + Esc); farkı sekmeli
/// gövdesidir. Headless süit XAML runtime çözümlemesini görmez — bu yüzden realize ZORUNLU (CLAUDE.md).
/// </summary>
[Collection("Console UI (serial)")]
public class AboutDialogTests
{
    // ---------------------------------------------------------------- kabuk

    [StaFact]
    public void The_dialog_realizes_and_is_six_hundred_twenty_pixels_wide()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            var shell = (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);
            Assert.Equal(620.0, shell.Width);
            Assert.Equal(620.0, shell.ActualWidth); // realize zorunlu — literal okumak yetmez
        }
    }

    /// <summary>Yapısal kanıt: scrim bir Cycle klavye-gezinme kapsayıcısı ve bir odak kapsamı
    /// (kardeşi <see cref="SettingsDialogFocusTests"/>). Odak tuzağı XAML dosyası BAŞINA kurulur — Settings'te
    /// düzeltilen kusur burada kendiliğinden düzelmiş sayılmaz.</summary>
    [StaFact]
    public void The_scrim_is_a_cyclic_keyboard_focus_scope()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog.Scrim));
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(dialog.Scrim));
            Assert.True(FocusManager.GetIsFocusScope(dialog.Scrim));
        }
    }

    /// <summary>Gerçek gezinme kanıtı: About açıkken arka plandaki odaklanabilir bir kontrole Tab ile
    /// ULAŞILAMAZ. İddia <see cref="FocusTrap.AssertCannotEscape"/> ile paylaşılır — Settings'in aynı iddiası
    /// da oradan beslenir (kopya YASAK, CLAUDE.md).</summary>
    [StaFact]
    public void Tab_navigation_cannot_escape_the_open_dialog()
    {
        var background = new Button { Content = "Background Build", Focusable = true, Width = 90, Height = 24 };
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(backgroundSibling: background);
        using (scope)
            FocusTrap.AssertCannotEscape(dialog.Scrim, background);
    }

    [StaFact]
    public void Close_dialog_hides_it()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.CloseDialog();
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        }
    }

    // ---------------------------------------------------------------- sekmeler

    private static IReadOnlyList<RadioButton> Tabs(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<RadioButton>()];

    [StaFact]
    public void It_has_three_tabs_and_the_first_one_is_selected()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var tabs = Tabs(dialog);
            Assert.Equal(3, tabs.Count);
            Assert.True(tabs[0].IsChecked);
            Assert.All(tabs.Skip(1), t => Assert.False(t.IsChecked));
        }
    }

    /// <summary>Sekme değişince diyalog BOYU DEĞİŞMEZ — footer'ın yeri her sekmede aynı kalır. Test SAYIYI
    /// değil DAVRANIŞI pinler: üç sekmenin ölçülen yüksekliği birbirine eşit olmalı.</summary>
    [StaFact]
    public void Switching_tabs_never_resizes_the_dialog()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var shell = (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);
            var heights = new List<double>();
            foreach (var tab in Tabs(dialog))
            {
                tab.IsChecked = true;
                dialog.UpdateLayout();
                heights.Add(shell.ActualHeight);
            }
            Assert.All(heights, h => Assert.True(h > 0, "diyalog hiç yerleşmedi"));
            Assert.Single(heights.Distinct());
        }
    }

    // ---------------------------------------------------------------- içerik

    [StaFact]
    public void The_hero_shows_the_product_identity_from_the_assembly()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            var texts = DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(AppIdentity.Product, texts);
            Assert.Contains(AppIdentity.Tagline, texts);
            Assert.Contains(texts, t => t.Contains(AppIdentity.Version, StringComparison.Ordinal)
                                     && t.Contains("9.9.9+test", StringComparison.Ordinal)
                                     && t.Contains(AppIdentity.Copyright, StringComparison.Ordinal));
            Assert.Single(DsResources.Descendants(dialog).OfType<BrandLogo>());
        }
    }

    [StaFact]
    public void The_shortcuts_tab_lists_every_catalog_entry_with_its_key_badges()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var texts = DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
            var badges = DsResources.Descendants(dialog).OfType<ContentControl>()
                .Select(c => c.Content as string).Where(c => c is not null).ToList();

            foreach (var entry in ShortcutCatalog.All)
            {
                Assert.Contains(entry.Description, texts);
                foreach (string gesture in entry.Gestures) Assert.Contains(gesture, badges);
            }
        }
    }

    /// <summary>Global kısayol kaydı çakışma yüzünden düştüğünde bu GÖRÜNÜR olur — README'nin "sessizce devre
    /// dışı" davranışını kullanıcının anlamasının başka bir yolu yok.</summary>
    [StaFact]
    public void An_unregistered_global_hotkey_is_marked_unavailable()
    {
        var (registered, _, scope1) = AboutDialogHost.OpenRealized(hotkeyRegistered: true);
        using (scope1)
            Assert.DoesNotContain(DsResources.Descendants(registered).OfType<TextBlock>()
                .Where(t => t.IsVisible).Select(t => t.Text), t => t.Contains("unavailable", StringComparison.Ordinal));

        var (disabled, _, scope2) = AboutDialogHost.OpenRealized(hotkeyRegistered: false);
        using (scope2)
            Assert.Contains(DsResources.Descendants(disabled).OfType<TextBlock>()
                .Where(t => t.IsVisible).Select(t => t.Text), t => t.Contains("unavailable", StringComparison.Ordinal));
    }

    [StaFact]
    public void The_environment_tab_draws_every_diagnostics_line()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(run => run.OnEngineReady("9.9.9+test", 777));
        using (scope)
        {
            Tabs(dialog)[1].IsChecked = true;
            dialog.UpdateLayout();

            var texts = DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
            foreach (var line in dialog.DiagnosticsLines)
            {
                Assert.Contains(line.Label, texts);
                Assert.Contains(line.Value, texts);
            }
            // Yollar YENİDEN YAZILMAZ — üretimin kendi static'lerinden gelir.
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == JsonUiStateStore.DefaultPath);
        }
    }

    /// <summary>MSBuild çözümü ASYNC'tir: sekme açılana kadar hiç tetiklenmez (About'u açmak bir child
    /// process başlatmamalı) ve sonuç gelene kadar satır "resolving…" der.</summary>
    [StaFact]
    public async Task Msbuild_is_resolved_lazily_when_the_environment_tab_is_first_opened()
    {
        var gate = new TaskCompletionSource<string>();
        int calls = 0;
        var (dialog, _, scope) = AboutDialogHost.OpenRealized(
            resolveMsBuild: () => { calls++; return gate.Task; });
        using (scope)
        {
            Assert.Equal(0, calls); // açılışta HİÇ çağrılmadı
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == DiagnosticsReport.Resolving);

            Tabs(dialog)[1].IsChecked = true;
            dialog.UpdateLayout();
            Assert.Equal(1, calls);

            gate.SetResult(AboutDialogHost.FakeMsBuild);
            await DispatcherPump.DrainAsync();
            Assert.Contains(dialog.DiagnosticsLines, l => l.Value == AboutDialogHost.FakeMsBuild);

            // İkinci kez açmak yeniden çözmez (sonuç cache'lenir).
            Tabs(dialog)[0].IsChecked = true;
            Tabs(dialog)[1].IsChecked = true;
            dialog.UpdateLayout();
            Assert.Equal(1, calls);
        }
    }

    [StaFact]
    public void The_third_party_tab_lists_every_component_with_its_licence()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            Tabs(dialog)[2].IsChecked = true;
            dialog.UpdateLayout();

            var texts = DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
            foreach (var component in ThirdPartyNotices.All)
            {
                Assert.Contains(component.DisplayName, texts);
                Assert.Contains(component.License, texts);
            }
            Assert.Contains(ThirdPartyNotices.FontLicenseNote, texts);
        }
    }

    // ---------------------------------------------------------------- copy diagnostics

    [StaFact]
    public void Copy_diagnostics_writes_the_report_text_and_shows_feedback()
    {
        string? written = null;
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.ClipboardWriter = text => { written = text; return true; };
            dialog.CopyDiagnostics();

            Assert.Equal(DiagnosticsReport.ToText(dialog.DiagnosticsLines), written);
            Assert.True(dialog.IsShowingCopied);
        }
    }

    /// <summary>Pano kilitliyse (kalıcı CLIPBRD_E_CANT_OPEN) UI çökmez ve "kopyalandı" YALANI söylemez.</summary>
    [StaFact]
    public void A_failed_clipboard_write_shows_no_copied_feedback()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.ClipboardWriter = _ => false;
            dialog.CopyDiagnostics();
            Assert.False(dialog.IsShowingCopied);
        }
    }
}
```

> `DispatcherPump.DrainAsync()` süitte mevcut (`tests/…/App/DispatcherPump.cs`). İmzası farklıysa o dosyadaki
> gerçek yardımcıyı kullan — **yeni bir pompa yazma** (kopya YASAK).

`AboutDialog`'un test yüzeyleri: `internal IReadOnlyList<DiagnosticsLine> DiagnosticsLines`,
`internal bool IsShowingCopied`, `public void CopyDiagnostics()`.

**Ortak odak-tuzağı iddiası.** `SettingsDialogFocusTests.Tab_navigation_cannot_escape_the_open_dialog_to_reach_a_background_control`
(satır 51-78) bu iddianın 20 satırlık gövdesini zaten taşıyor; About ikinci bir kopyasını yazacaktı. Gövde
**tek yere** çıkarılır — `tests/BuildOrchestrator.Tests/App/FocusTrap.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Modal odak tuzağı iddiası — İKİ diyalog (Settings, About) tarafından paylaşılır. Gövde önce
/// <c>SettingsDialogFocusTests</c>'in içindeydi; About ikinci bir kopyasını yazacaktı (kopya YASAK, CLAUDE.md).
/// </summary>
internal static class FocusTrap
{
    /// <summary>Diyalog alt-ağacından başlayarak tekrar tekrar "Sonraki" gezinme (Tab'ın WPF içindeki gerçek
    /// mekanizması — <see cref="UIElement.MoveFocus"/>) yapar: kontrol sayısından FAZLA turda ne odak arka plan
    /// kontrolüne kaçar ne de alt-ağacın dışına çıkar (Cycle sarar).</summary>
    public static void AssertCannotEscape(FrameworkElement dialogRoot, DependencyObject backgroundControl)
    {
        Assert.True(dialogRoot.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            "diyalog alt-ağacında odaklanabilir hiçbir kontrol bulunamadı");
        for (int i = 0; i < 25; i++) // kontrol sayısından kesinlikle fazla — Cycle sarmalıyor, kaçmıyor
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            Assert.NotNull(focused);
            Assert.NotSame(backgroundControl, focused);
            // Görsel+MANTIKSAL kip BİLEREK: odaklanan öğe bir Popup/ContentElement altındaysa görsel zincir
            // kopar, mantıksal zincir devam eder.
            Assert.True(DsResources.IsSelfOrDescendantOf(focused!, dialogRoot, includeLogical: true),
                "odak diyalog alt-ağacının DIŞINA çıktı");
            (focused as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
}
```

…ve `SettingsDialogFocusTests`'in o testi gövdesini bu yardımcıya devreder (yerel `IsDescendantOf` helper'ı
silinir; assertion'lar ve doc'u AYNEN korunur — davranış değişmiyor, yalnız gövde tek yere taşınıyor):

```csharp
        FocusTrap.AssertCannotEscape(dialog.Scrim, background);
        GC.KeepAlive(window);
```

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AboutDialogTests"`

Expected: derleme hatası — `AboutDialog` yok.

- [ ] **Step 3: `Icon.Info`'yu ekle**

`src/BuildOrchestrator.App/Resources/Icons.xaml` — `Icon.Gear` bloğunun (satır ~128-132) hemen ardına:

```xml
    <!--
      [About] lucide `info` — <circle cx="12" cy="12" r="10"/> + <path d="M12 16v-4"/> + <path d="M12 8h.01"/>

      TÜRETİLMİŞ: design-v1 BuildApp.jsx:44-72 ikon tablosunda `info` YOKTUR (Icon.CaptionRestore ile aynı
      statü — kaynaksız değer, gerekçesi burada). Geometri lucide'dan alınmıştır çünkü bu sözlükteki tüm
      24-viewBox ikonların ailesi lucide'dır; ikon dili böyle tutarlı kalır.
    -->
    <GeometryGroup x:Key="Icon.Info">
        <EllipseGeometry Center="12,12" RadiusX="10" RadiusY="10" />
        <PathGeometry Figures="M12 16v-4 M12 8h.01" />
    </GeometryGroup>
```

…ve `Icon.Gear.StrokeThickness` ile aynı gruba (satır ~269-271):

```xml
    <!-- strokeWidth="1.7" (BuildApp.jsx:59, :60 · Icon.Info TÜRETİLMİŞ: title bar'da Gear'ın KOMŞUSU,
         optik ağırlıkları eşleşmeli) -->
    <sys:Double x:Key="Icon.Info.StrokeThickness">1.7</sys:Double>
```

- [ ] **Step 4: `AboutDialog.xaml`'i yaz**

```xml
<UserControl x:Class="BuildOrchestrator.App.Views.AboutDialog"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:BuildOrchestrator.App.Controls"
             Visibility="Collapsed" Focusable="True">
  <!-- [About] İkinci modal. Kabuk SettingsDialog ile BİREBİR aynı (full-bleed Brush.Scrim + 620px Ds.Dialog +
       odak tuzağı + Esc/scrim ile kapanma) — yeni bir modal deseni GİRMEZ. Farkı sekmeli gövdesidir:
       dört içerik sınıfı (kimlik/kısayol/ortam/lisans) birbirine bakmaz, tek scroll'da taranmaları gerekirdi.
       Sekme anahtarı Ds.Segment'tir (ActionBar'ın Debug/Release'iyle AYNI bileşen). -->
  <Grid x:Name="Scrim" Background="{DynamicResource Brush.Scrim}" MouseLeftButtonDown="OnScrimClick"
        KeyboardNavigation.TabNavigation="Cycle"
        KeyboardNavigation.ControlTabNavigation="Cycle"
        FocusManager.IsFocusScope="True">
    <Border Width="620" HorizontalAlignment="Center" VerticalAlignment="Center"
            Style="{DynamicResource Ds.Dialog}" MouseLeftButtonDown="OnDialogClick">
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto" />  <!-- hero -->
          <RowDefinition Height="Auto" />  <!-- sekmeler -->
          <RowDefinition Height="Auto" />  <!-- içerik (SABİT yükseklik) -->
          <RowDefinition Height="Auto" />  <!-- footer -->
        </Grid.RowDefinitions>

        <!-- ============================ HERO ============================ -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="20,18,20,14">
          <controls:BrandLogo Height="20" VerticalAlignment="Top" Margin="0,2,14,0" />
          <StackPanel>
            <TextBlock x:Name="ProductText"
                       FontSize="{DynamicResource FontSize.Lg}"
                       FontWeight="{DynamicResource FontWeight.Emphasis}"
                       Foreground="{DynamicResource Brush.TextPrimary}" />
            <TextBlock x:Name="TaglineText" Margin="0,3,0,0" TextWrapping="Wrap" MaxWidth="480"
                       FontSize="{DynamicResource FontSize.Xs}"
                       Foreground="{DynamicResource Brush.TextDim}" />
            <TextBlock x:Name="IdentityText" Margin="0,6,0,0"
                       FontFamily="{x:Static controls:AppFonts.Mono}"
                       FontSize="{DynamicResource FontSize.2xs}"
                       Foreground="{DynamicResource Brush.TextFaint}" />
          </StackPanel>
        </StackPanel>

        <!-- ============================ SEKMELER ============================ -->
        <ItemsControl Grid.Row="1" Margin="20,0,20,12" Style="{DynamicResource Ds.Segment}">
          <RadioButton x:Name="ShortcutsTab" GroupName="about" IsChecked="True"
                       Style="{DynamicResource Ds.Segment.Item}" Content="Shortcuts" />
          <RadioButton x:Name="EnvironmentTab" GroupName="about" Checked="OnEnvironmentTabChecked"
                       Style="{DynamicResource Ds.Segment.Item}" Content="Environment" />
          <RadioButton x:Name="ThirdPartyTab" GroupName="about"
                       Style="{DynamicResource Ds.Segment.Item}" Content="Third-party" />
        </ItemsControl>

        <!-- ============================ İÇERİK ============================ -->
        <!-- SABİT yükseklik: sekme değişince diyaloğun boyu ve footer'ın yeri OYNAMAZ. Sayı, en uzun sekmeyi
             (Environment, 10 satır) sığdıracak şekilde seçildi; test sayıyı değil "üç sekmede de aynı
             yükseklik" DAVRANIŞINI pinler (AboutDialogTests). -->
        <Grid Grid.Row="2" Height="260" Margin="20,0,20,4">

          <!-- ---- Shortcuts ---- -->
          <ScrollViewer VerticalScrollBarVisibility="Auto"
                        Visibility="{Binding IsChecked, ElementName=ShortcutsTab,
                                     Converter={StaticResource BooleanToVisibilityConverter}}">
            <ItemsControl x:Name="ShortcutRows">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <DockPanel LastChildFill="False" Margin="0,0,0,8">
                    <ItemsControl DockPanel.Dock="Right" ItemsSource="{Binding Gestures}">
                      <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                      </ItemsControl.ItemsPanel>
                      <ItemsControl.ItemTemplate>
                        <DataTemplate>
                          <ContentControl Content="{Binding}" Margin="4,0,0,0"
                                          Style="{DynamicResource Ds.Kbd}" />
                        </DataTemplate>
                      </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock DockPanel.Dock="Right" Text="unavailable" VerticalAlignment="Center"
                               Margin="8,0,0,0"
                               FontSize="{DynamicResource FontSize.2xs}"
                               Foreground="{DynamicResource Brush.TextFaint}"
                               Visibility="{Binding Unavailable,
                                            Converter={StaticResource BooleanToVisibilityConverter}}" />
                    <TextBlock Text="{Binding Description}" VerticalAlignment="Center"
                               FontSize="{DynamicResource FontSize.Sm}"
                               Foreground="{DynamicResource Brush.TextSecondary}" />
                  </DockPanel>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>

          <!-- ---- Environment ---- -->
          <ScrollViewer VerticalScrollBarVisibility="Auto"
                        Visibility="{Binding IsChecked, ElementName=EnvironmentTab,
                                     Converter={StaticResource BooleanToVisibilityConverter}}">
            <ItemsControl x:Name="EnvironmentRows">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <DockPanel Margin="0,0,0,4">
                    <TextBlock DockPanel.Dock="Left" Width="130" Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="{DynamicResource FontSize.Xs}"
                               Foreground="{DynamicResource Brush.TextDim}" />
                    <TextBlock Text="{Binding Value}" ToolTip="{Binding Value}"
                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                               FontFamily="{x:Static controls:AppFonts.Mono}"
                               FontSize="{DynamicResource FontSize.Xs}"
                               Foreground="{DynamicResource Brush.TextSecondary}" />
                  </DockPanel>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>

          <!-- ---- Third-party ---- -->
          <ScrollViewer VerticalScrollBarVisibility="Auto"
                        Visibility="{Binding IsChecked, ElementName=ThirdPartyTab,
                                     Converter={StaticResource BooleanToVisibilityConverter}}">
            <StackPanel>
              <ItemsControl x:Name="ThirdPartyRows">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <DockPanel Margin="0,0,0,6">
                      <TextBlock DockPanel.Dock="Right" Text="{Binding License}" VerticalAlignment="Center"
                                 FontSize="{DynamicResource FontSize.2xs}"
                                 Foreground="{DynamicResource Brush.TextDim}" />
                      <TextBlock DockPanel.Dock="Left" Text="{Binding DisplayName}" VerticalAlignment="Center"
                                 FontSize="{DynamicResource FontSize.Xs}"
                                 Foreground="{DynamicResource Brush.TextSecondary}" />
                      <TextBlock Text="{Binding Version}" Margin="8,0,0,0" VerticalAlignment="Center"
                                 FontFamily="{x:Static controls:AppFonts.Mono}"
                                 FontSize="{DynamicResource FontSize.2xs}"
                                 Foreground="{DynamicResource Brush.TextFaint}" />
                    </DockPanel>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
              <TextBlock x:Name="FontLicenseNoteText" Margin="0,8,0,0" TextWrapping="Wrap"
                         FontSize="{DynamicResource FontSize.2xs}"
                         Foreground="{DynamicResource Brush.TextFaint}" />
            </StackPanel>
          </ScrollViewer>
        </Grid>

        <!-- ============================ FOOTER ============================ -->
        <Border Grid.Row="3" BorderBrush="{DynamicResource Brush.BorderSubtle}" BorderThickness="0,1,0,0">
          <DockPanel Margin="20,12">
            <Button x:Name="CopyButton" DockPanel.Dock="Left" Click="OnCopyDiagnostics"
                    Style="{DynamicResource Ds.Button.Ghost.Md}" />
            <Button DockPanel.Dock="Right" HorizontalAlignment="Right" Content="Close" Click="OnClose"
                    Style="{DynamicResource Ds.Button.Secondary.Md}" />
          </DockPanel>
        </Border>
      </Grid>
    </Border>
  </Grid>
</UserControl>
```

> **`BooleanToVisibilityConverter`:** WPF'in yerleşik `System.Windows.Controls.BooleanToVisibilityConverter`'ı
> bir `StaticResource` olarak tanımlı olmalı. Süitte zaten bir tanım varsa **onu kullan**; yoksa
> `Resources/Controls.xaml`'e tek bir kayıt ekle:
> `<BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />` (xmlns yerleşik). İkinci bir kopya AÇMA.

- [ ] **Step 5: `AboutDialog.xaml.cs`'i yaz**

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Logs;

namespace BuildOrchestrator.App.Views;

/// <summary>Shortcuts sekmesinin bir satırı (görünüm modeli).</summary>
internal readonly record struct ShortcutRow(string Description, IReadOnlyList<string> Gestures, bool Unavailable);

/// <summary>Third-party sekmesinin bir satırı — sürüm çalışma zamanında çözülür.</summary>
internal readonly record struct NoticeRow(string DisplayName, string Version, string License);

/// <summary>
/// [About] İkinci modal diyalog: ürün kimliği + klavye kısayolları + ortam/tanı + üçüncü-taraf lisansları.
/// Kabuk <see cref="SettingsDialog"/> ile AYNI (scrim, 620px Ds.Dialog, odak tuzağı, Esc/scrim ile kapanma).
///
/// <para><b>İnce view:</b> gösterilen her şey saf tiplerden gelir — <see cref="AppIdentity"/>,
/// <see cref="ShortcutCatalog"/>, <see cref="DiagnosticsReport"/>, <see cref="ThirdPartyNotices"/>. Burada
/// hiçbir metin ya da yol YENİDEN YAZILMAZ.</para>
///
/// <para><b>MSBuild LAZY çözülür:</b> <c>vswhere</c> bir child process başlatır — About'u AÇMAK bunu
/// tetiklememeli. Çözüm Environment sekmesi ilk kez seçildiğinde başlar, sonucu diyalog ömrü boyunca
/// cache'lenir.</para>
/// </summary>
public partial class AboutDialog : UserControl
{
    private readonly CopyLogFeedback _copyFeedback = new(); // [kopya YASAK] Konsolun copy geri-bildirimiyle AYNI saat
    private DispatcherTimer? _copyRevertTimer;
    private Stopwatch? _copyClock;

    private RunViewModel? _run;
    private Func<Task<string>>? _resolveMsBuild;
    private string _msBuild = DiagnosticsReport.Resolving;
    private bool _msBuildRequested;

    public AboutDialog()
    {
        InitializeComponent();
        ResetCopyLabel();
    }

    /// <summary>[3b deseni] Panoya yazma yolu — üretimde retry sarmalayıcı, testte enjekte edilir
    /// (gerçek panoya dokunmadan geri bildirim doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>[test yüzeyi] Environment sekmesinin O ANDA çizdiği satırlar — "Copy diagnostics" de
    /// AYNI listeyi metne çevirir.</summary>
    internal IReadOnlyList<DiagnosticsLine> DiagnosticsLines { get; private set; } = [];

    /// <summary>[test yüzeyi] Kopyalandı geri bildirimi görünür mü.</summary>
    internal bool IsShowingCopied => _copyFeedback.Copied;

    /// <summary>
    /// Diyaloğu açar. <paramref name="hotkeyRegistered"/> global kısayolun GERÇEKTEN kayıtlı olup olmadığıdır
    /// (çakışmada sessiz devre dışı — bkz. <see cref="HotkeyRegistration"/>); false ise satır "unavailable"
    /// işaretlenir. <paramref name="resolveMsBuild"/> vswhere seam'idir (testler process başlatmaz).
    /// </summary>
    public void Open(RunViewModel run, bool hotkeyRegistered, Func<Task<string>> resolveMsBuild)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(resolveMsBuild);
        _run = run;
        _resolveMsBuild = resolveMsBuild;
        _msBuild = DiagnosticsReport.Resolving;
        _msBuildRequested = false;

        ProductText.Text = AppIdentity.Product;
        TaglineText.Text = AppIdentity.Tagline;
        IdentityText.Text = string.Format(CultureInfo.InvariantCulture, "{0} · engine {1} · {2}",
            AppIdentity.Version,
            run.EngineVersion ?? DiagnosticsReport.NotStarted,
            AppIdentity.Copyright);

        ShortcutRows.ItemsSource = ShortcutCatalog.All
            .Select(e => new ShortcutRow(e.Description, e.Gestures,
                Unavailable: e.Id == ShortcutId.RestoreFromTray && !hotkeyRegistered))
            .ToList();

        ThirdPartyRows.ItemsSource = ThirdPartyNotices.All
            .Select(c => new NoticeRow(c.DisplayName, ThirdPartyNotices.ResolveVersion(c) ?? "", c.License))
            .ToList();
        FontLicenseNoteText.Text = ThirdPartyNotices.FontLicenseNote;

        RefreshDiagnostics();

        ShortcutsTab.IsChecked = true; // her açılışta ilk sekme
        ResetCopyVisual();
        Visibility = Visibility.Visible;
        Focus(); // Esc HER durumda yakalanabilsin
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Close() => Visibility = Visibility.Collapsed;

    /// <summary>Esc zincirinin dialog katmanı için dışarıdan kapatma (MainWindow güvenlik ağı — odak dialog
    /// dışındayken). Dialog odaklıyken Esc'i <see cref="OnKeyDown"/> yakalar.</summary>
    public void CloseDialog() => Close();

    // ---------------------------------------------------------------- tanı

    /// <summary>Satırları TEK yerden (DiagnosticsReport) yeniden kurar. Yol metinleri üretimin kendi
    /// static'lerinden gelir — burada YENİDEN YAZILMAZ.</summary>
    private void RefreshDiagnostics()
    {
        if (_run is not { } run) return;
        DiagnosticsLines = DiagnosticsReport.Compose(new DiagnosticsInput(
            AppVersion: AppIdentity.Version,
            EngineVersion: run.EngineVersion,
            EnginePid: run.EnginePid,
            Runtime: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            MsBuild: _msBuild,
            RepositoryRoot: run.RootPath,
            StateFile: JsonUiStateStore.DefaultPath,
            LogsRoot: RunLogPaths.DefaultLogsRoot,
            WorktreePool: WorktreeManager.DefaultPoolRoot));
        EnvironmentRows.ItemsSource = DiagnosticsLines;
    }

    // Environment sekmesi İLK kez seçildiğinde vswhere'i başlatır; sonuç cache'lenir.
    private async void OnEnvironmentTabChecked(object sender, RoutedEventArgs e)
    {
        if (_msBuildRequested || _resolveMsBuild is not { } resolve) return;
        _msBuildRequested = true;
        _msBuild = await resolve();
        RefreshDiagnostics();
    }

    // ---------------------------------------------------------------- copy diagnostics

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e) => CopyDiagnostics();

    /// <summary>Tanı raporunu panoya yazar. Başarıda buton etiketi <see cref="CopyLogFeedback.RevertMs"/>
    /// boyunca "Copied" olur — süre sabiti konsolun copy butonuyla PAYLAŞILIR (kopya YASAK).</summary>
    public void CopyDiagnostics()
    {
        if (!ClipboardWriter(DiagnosticsReport.ToText(DiagnosticsLines))) return; // kalıcı pano kilidi: sessiz

        _copyClock = Stopwatch.StartNew();
        _copyFeedback.MarkCopied(TimeSpan.Zero);
        CopyButton.Content = CopiedLabel;

        _copyRevertTimer?.Stop();
        _copyRevertTimer ??= CreateRevertTimer();
        _copyRevertTimer.Start();
    }

    private const string CopyLabel = "Copy diagnostics";
    private const string CopiedLabel = "Copied";

    private DispatcherTimer CreateRevertTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (_copyClock is not null && _copyFeedback.ShouldRevert(_copyClock.Elapsed)) ResetCopyVisual();
        };
        return timer;
    }

    private void ResetCopyVisual()
    {
        _copyRevertTimer?.Stop();
        _copyClock?.Stop();
        _copyClock = null;
        _copyFeedback.Revert();
        ResetCopyLabel();
    }

    private void ResetCopyLabel() => CopyButton.Content = CopyLabel;

    // ---------------------------------------------------------------- kapatma

    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();
    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
```

> `CopyLogFeedback.Revert()` / `ShouldRevert(TimeSpan)` üyelerinin gerçek adlarını
> `src/BuildOrchestrator.App/Console/CopyLogFeedback.cs`'ten doğrula ve birebir kullan.

- [ ] **Step 6: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AboutDialogTests|FullyQualifiedName~SettingsDialogFocusTests|FullyQualifiedName~IconGeometryTests|FullyQualifiedName~DsControlTemplateTests"`

Expected: hepsi PASS. `IconGeometryTests` her ikonun `StrokeThickness` kardeşini pinler — `Icon.Info` kalınlığı
unutulmuşsa orada kırar. `SettingsDialogFocusTests` gövdesi `FocusTrap`'e taşındıktan sonra da **aynı** sonucu
vermeli (davranış değişmedi, yalnız iddia tek yere toplandı).

`Switching_tabs_never_resizes_the_dialog` FAIL ederse: içerik `Grid`'inin `Height="260"`'ı gerçekten sabit mi,
sekme gövdeleri `Visibility` ile mi değişiyor (`Collapsed` olan yer kaplamamalı ama `Grid` yüksekliği zaten
sabit) — düzeltme sayıyı büyütmek değil, satırın `Auto` kalmadığından emin olmaktır.

- [ ] **Step 7: Commit**

```bash
git add src/BuildOrchestrator.App/Resources/Icons.xaml src/BuildOrchestrator.App/Views/AboutDialog.xaml src/BuildOrchestrator.App/Views/AboutDialog.xaml.cs tests/BuildOrchestrator.Tests/App/AboutDialogHost.cs tests/BuildOrchestrator.Tests/App/AboutDialogTests.cs tests/BuildOrchestrator.Tests/App/FocusTrap.cs tests/BuildOrchestrator.Tests/App/SettingsDialogFocusTests.cs
git commit -m "feat(about): sekmeli About modali + Icon.Info"
```

---

## Task 7: MainWindow entegrasyonu — info butonu, F1, Esc zinciri

**Files:**
- Modify: `src/BuildOrchestrator.App/Shell/KeyboardShortcuts.cs:31-41` (`WindowIntent`), `:64-71` (`WindowBindings`)
- Modify: `src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs` (`ShortcutId.About` + katalog satırı)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml:129-141` (buton), `:194-197` (overlay)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs:294-303` (intent tablosu), `:322-331` (Esc), `:614` civarı
- Modify: `src/BuildOrchestrator.App/AccessibilityNames.cs` (About butonunun adı)
- Test: `tests/BuildOrchestrator.Tests/App/AboutWiringTests.cs` (yeni)
- **Yeniden yazılır:** `tests/BuildOrchestrator.Tests/App/KeyboardWiringTests.cs:44-58`

**Interfaces:**
- Consumes: `AboutDialog.Open/CloseDialog` (T6), `ShortcutCatalog.Get` (T1)
- Produces: `MainWindow.AboutOverlay` (XAML `x:Name`), `MainWindow.InfoButton` (XAML `x:Name`),
  `WindowIntent.ShowAbout`, `ShortcutId.About`, `AccessibilityNames.About`

- [ ] **Step 1: Kırmızı testleri yaz**

`tests/BuildOrchestrator.Tests/App/AboutWiringTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// About'un kabuğa bağlanması: title bar butonu, F1, Esc katman zinciri ve modal dışlama.
/// </summary>
[Collection("Console UI (serial)")]
public class AboutWiringTests
{
    // ---------------------------------------------------------------- title bar butonu

    /// <summary>Buton gear'ın SAĞINDA, aynı grupta durur (kullanım sıklığı azalan sıra; Windows/Office'te
    /// Help/About uygulama komutlarının en sonundadır).</summary>
    [StaFact]
    public void The_info_button_sits_immediately_to_the_right_of_the_gear()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        // İki buton AYNI mantıksal grupta (title bar'ın layout+gear StackPanel'i) ve info gear'dan HEMEN sonra.
        var group = (Panel)LogicalTreeHelper.GetParent(window.GearButton);
        int gear = group.Children.IndexOf(window.GearButton);
        int info = group.Children.IndexOf(window.InfoButton);

        Assert.True(gear >= 0, "gear butonu beklenen grupta değil");
        Assert.True(info >= 0, "info butonu gear ile AYNI grupta değil");
        Assert.Equal(gear + 1, info);
        GC.KeepAlive(window);
    }

    /// <summary>Butonun tooltip'i ve UIA adı metni ELLE yazmaz: tooltip katalogdan, ad
    /// <see cref="AccessibilityNames"/>'ten gelir.</summary>
    [StaFact]
    public void The_info_button_reads_its_tooltip_from_the_shortcut_catalog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        var tooltip = (ToolTip)window.InfoButton.ToolTip;
        Assert.Equal(ShortcutCatalog.Get(ShortcutId.About).Description, tooltip.Content);
        Assert.Equal(AccessibilityNames.About,
            AutomationProperties.GetName(window.InfoButton));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_the_info_button_opens_the_about_dialog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        window.InfoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- F1

    [StaFact]
    public void F1_is_bound_to_the_show_about_intent()
    {
        var binding = KeyboardShortcuts.WindowBindings.Single(b => b.Key == Key.F1);
        Assert.Equal(ModifierKeys.None, binding.Modifiers);
        Assert.Equal(WindowIntent.ShowAbout, binding.Intent);
    }

    /// <summary>F1 gerçekten bir <see cref="KeyBinding"/>'e dönüşmüş mü — tablo doğru ama kablaj eksik
    /// olabilirdi.</summary>
    [StaFact]
    public void The_window_installs_a_key_binding_for_every_row_in_the_table()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);

        foreach (var row in KeyboardShortcuts.WindowBindings)
            Assert.Contains(window.InputBindings.OfType<KeyBinding>(),
                k => k.Key == row.Key && k.Modifiers == row.Modifiers);
        GC.KeepAlive(window);
    }

    /// <summary>Bir modal AÇIKKEN F1 NO-OP'tur: Settings'in kaydedilmemiş taslağını sessizce çöpe atmamalı.</summary>
    [StaFact]
    public void F1_does_nothing_while_another_dialog_is_open()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        window.GearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility);

        Invoke(window, Key.F1, ModifierKeys.None);

        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility); // About AÇILMADI
        Assert.Equal(Visibility.Visible, window.SettingsOverlay.Visibility); // Settings taslağı DURUYOR
        GC.KeepAlive(window);
    }

    [StaFact]
    public void F1_opens_the_about_dialog_when_nothing_else_is_open()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.None);

        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Esc zinciri

    /// <summary>Esc zinciri artık İKİ diyalogu da kapsar. Eskiden <c>dialogOpen</c> yalnız
    /// <c>SettingsOverlay</c>'e bakıyordu — About açıkken Esc alt katmana (popover/seçim) SIZARDI.</summary>
    [StaFact]
    public void Escape_closes_the_about_dialog_too()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Invoke(window, Key.F1, ModifierKeys.None);
        Assert.Equal(Visibility.Visible, window.AboutOverlay.Visibility);

        Invoke(window, Key.Escape, ModifierKeys.None);
        Assert.Equal(Visibility.Collapsed, window.AboutOverlay.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Pencere-seviyesi bir tuş bağlamasını, üretimdeki yolun AYNISIYLA (InputBinding'in komutu)
    /// tetikler — WPF olay yönlendirmesi HWND'siz güvenilir değildir.</summary>
    private static void Invoke(BuildOrchestrator.App.MainWindow window, Key key, ModifierKeys modifiers)
    {
        var binding = window.InputBindings.OfType<KeyBinding>()
            .Single(k => k.Key == key && k.Modifiers == modifiers);
        if (binding.Command.CanExecute(null)) binding.Command.Execute(null);
    }
}
```

> `window.GearButton` / `InfoButton` / `SettingsOverlay` / `AboutOverlay` XAML `x:Name` alanlarıdır;
> `MainWindowHost` aynı assembly'den okuyabilmek için testlerin `InternalsVisibleTo`'su zaten kurulu
> (`BuildOrchestrator.App.csproj:10-12`). Alanlar `internal` üretilir — erişilemiyorsa
> `x:FieldModifier="Internal"` ekle.

`KeyboardWiringTests.cs:44-58` **yeniden yazılır** (silinmez, gevşetilmez):

```csharp
    // ------------------------------------------------------------------ tuş+modifier → niyet (SetupKeyboardShortcuts kablajı)

    /// <summary>
    /// [About] ESKİ İDDİA: "tabloda TAM 5 satır var". Bu sayı bir bütçe değil, o günkü kısayol kümesinin
    /// negatif-pin'iydi (yanlışlıkla eklenen bir bağlamayı yakalamak için). About ekranı F1'i ekledi, yani
    /// KURAL BİLEREK DEĞİŞTİ: satır sayısı 6'dır ve tablo artık <see cref="WindowIntent.ShowAbout"/>'u da
    /// taşır. Negatif-pin'in NİYETİ korunuyor — sayı, tabloda ADI GEÇEN niyetlerden türetilmiyor, açıkça
    /// yazılıyor ki fazladan/kayıp bir bağlama yine kırsın.
    /// </summary>
    [Fact]
    public void The_window_binding_table_maps_each_key_gesture_to_the_correct_intent()
    {
        // Single: tam olarak BİR satır eşleşmezse (yanlış/eksik modifier veya tuş) fırlatır → yanlış kablaj kırar.
        WindowIntent Intent(Key key, ModifierKeys mods) =>
            KeyboardShortcuts.WindowBindings.Single(b => b.Key == key && b.Modifiers == mods).Intent;

        Assert.Equal(WindowIntent.Rebuild, Intent(Key.F5, ModifierKeys.Control));     // Ctrl+F5  → Rebuild
        Assert.Equal(WindowIntent.Rebuild, Intent(Key.F5, ModifierKeys.Shift));       // Shift+F5 → Rebuild
        Assert.Equal(WindowIntent.F5StateBranch, Intent(Key.F5, ModifierKeys.None));  // çıplak F5 → duruma-dallı
        Assert.Equal(WindowIntent.FocusFilter, Intent(Key.F, ModifierKeys.Control));  // Ctrl+F   → filtre odağı
        Assert.Equal(WindowIntent.ShowAbout, Intent(Key.F1, ModifierKeys.None));      // F1       → About
        Assert.Equal(WindowIntent.Escape, Intent(Key.Escape, ModifierKeys.None));     // Esc      → katman zinciri

        // Negatif-pin: tabloda TAM 6 satır — fazladan/kayıp bir bağlama (ör. yanlışlıkla eklenen Ctrl+P) kırar.
        Assert.Equal(6, KeyboardShortcuts.WindowBindings.Count);
    }
```

- [ ] **Step 2: Testleri koştur, KIRMIZI olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AboutWiringTests|FullyQualifiedName~KeyboardWiringTests"`

Expected: derleme hatası — `WindowIntent.ShowAbout`, `ShortcutId.About`, `AccessibilityNames.About`,
`window.InfoButton`, `window.AboutOverlay` yok.

- [ ] **Step 3: Niyet + bağlama + katalog satırını ekle**

`src/BuildOrchestrator.App/Shell/KeyboardShortcuts.cs` — `WindowIntent` enum'una:

```csharp
    /// <summary>F1 → About diyaloğu (sürüm, kısayollar, tanı). Bir modal AÇIKKEN NO-OP'tur — kaydedilmemiş
    /// bir Settings taslağı sessizce atılmamalı.</summary>
    ShowAbout,
```

`WindowBindings` tablosuna (Ctrl+F satırının ardına, Esc'ten önce):

```csharp
        new(Key.F1, ModifierKeys.None, WindowIntent.ShowAbout),        // F1       → About (Windows Help geleneği)
```

`src/BuildOrchestrator.App/Shell/ShortcutCatalog.cs` — `ShortcutId`'ye `About` ekle (FocusFilter'dan sonra) ve
`All` listesine FocusFilter satırının ardına:

```csharp
        new(ShortcutId.About, GesturesFor(WindowIntent.ShowAbout),
            "About — version, shortcuts and diagnostics"),
```

- [ ] **Step 4: `AccessibilityNames`'e adı ekle**

`src/BuildOrchestrator.App/AccessibilityNames.cs` — Settings/başlık bandı bölümüne:

```csharp
    // ---- [About] Title bar ----
    /// <summary>Title bar'daki ikon-yalnız info butonu. Tooltip'ten AYRIDIR: tooltip kısayolu da anlatan
    /// katalog cümlesidir (<c>ShortcutCatalog.Get(ShortcutId.About).Description</c>), UIA adı ise kontrolün
    /// işlevini KISA tarif eder.</summary>
    public const string About = "About";
```

- [ ] **Step 5: Title bar butonunu ve overlay'i ekle**

`src/BuildOrchestrator.App/MainWindow.xaml` — `GearButton`'ın (satır ~141 `</Button>`) hemen ardına, aynı
`StackPanel` içinde:

```xml
            <!-- [About] Gear'ın SAĞINDA, aynı grupta: grup kullanım sıklığı azalan sırada dizilir (layout >
                 settings > about) ve Windows/Office geleneğinde Help/About uygulama komutlarının en sonundadır.
                 Tooltip metni ELLE yazılmaz — ShortcutCatalog'dan gelir (kopya YASAK); kod-tarafı kurulur. -->
            <Button x:Name="InfoButton" Style="{DynamicResource Ds.IconButton}" Click="OnAbout"
                    AutomationProperties.Name="{x:Static local:AccessibilityNames.About}">
              <Viewbox Width="16" Height="16">
                <Canvas Width="24" Height="24">
                  <Path Style="{StaticResource TitleBarIcon}" Data="{DynamicResource Icon.Info}"
                        StrokeThickness="{DynamicResource Icon.Info.StrokeThickness}" />
                </Canvas>
              </Viewbox>
            </Button>
```

…ve `SettingsOverlay`'in (satır ~196-197) ardına:

```xml
      <!-- [About] İkinci modal. Settings ile aynı katman kuralları: scrim tüm pencereyi (başlık bandı dahil)
           kaplar, caption bölgesinde de tıklama alsın diye IsHitTestVisibleInChrome=True. -->
      <views:AboutDialog x:Name="AboutOverlay" Grid.Row="0" Grid.RowSpan="2"
                         WindowChrome.IsHitTestVisibleInChrome="True" />
```

- [ ] **Step 6: `MainWindow.xaml.cs` kablajı**

Intent tablosuna (`SetupKeyboardShortcuts`, satır ~294-300):

```csharp
            [WindowIntent.ShowAbout] = new RelayCommand(OnAboutRequested),
```

Esc zincirini (satır ~322-331) genişlet:

```csharp
    /// <summary>[About] Bir modal AÇIK MI — Esc zinciri ve F1 kapısı bu tek karardan beslenir (iki ayrı
    /// yerde ayrı ayrı sorulsaydı biri güncellenip diğeri unutulurdu).</summary>
    private bool AnyDialogOpen =>
        SettingsOverlay.Visibility == Visibility.Visible || AboutOverlay.Visibility == Visibility.Visible;

    private void OnEscapePressed()
    {
        switch (KeyboardShortcuts.ResolveEsc(AnyDialogOpen, Shell.AnyPopoverOpen, _vm.SelectedProjectId is not null))
        {
            // Hangi modal açıksa o kapanır (ikisi aynı anda AÇILAMAZ — OnAboutRequested/OnSettings kapısı).
            case EscAction.CloseDialog:
                if (SettingsOverlay.Visibility == Visibility.Visible) SettingsOverlay.CloseDialog();
                else AboutOverlay.CloseDialog();
                break;
            case EscAction.ClosePopovers: Shell.CloseAllPopovers(); break;
            case EscAction.ClearSelection: _vm.SelectProject(null); break;
        }
    }
```

About'u açan yol (gear'ın `OnSettings`'inin yanına, satır ~614 civarı):

```csharp
    /// <summary>[About] Info butonu → About modali.</summary>
    private void OnAbout(object sender, RoutedEventArgs e) => OnAboutRequested();

    /// <summary>[About] About'u açar — bir modal ZATEN AÇIKSA no-op. Gerekçe: F1 pencere-seviyesi bir
    /// InputBinding'dir ve Settings'in odak tuzağına RAĞMEN ateşler; kapı olmasaydı F1, kaydedilmemiş bir
    /// Settings taslağını sessizce çöpe atardı. (Fare yolu zaten scrim'in altında kalır.)</summary>
    private void OnAboutRequested()
    {
        if (AnyDialogOpen) return;
        AboutOverlay.Open(_vm, _hotkey?.IsRegistered ?? false, ResolveMsBuildAsync);
    }

    /// <summary>[About] MSBuild yolu — About'un Environment sekmesi bunu LAZY çağırır (vswhere bir child
    /// process başlatır; About'u açmak onu tetiklememeli). Çözülemezse hata mesajı olduğu gibi gösterilir.</summary>
    private static async Task<string> ResolveMsBuildAsync()
    {
        try
        {
            var location = await new MsBuildResolver(new ProcessRunner()).ResolveAsync();
            return $"{location.MsBuildExePath} (v{location.Version})";
        }
        catch (MsBuildResolveException ex)
        {
            return ex.Message;
        }
    }
```

`using BuildOrchestrator.Core.MsBuild;` ve `using BuildOrchestrator.Core.Processes;` ekle.

Ayrıca `OnSettings`'e simetrik kapı:

```csharp
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (AnyDialogOpen) return; // [About] iki modal aynı anda açılamaz
        SettingsOverlay.Open(_vm, _uiState, PickFolder);
    }
```

- [ ] **Step 7: Testleri koştur, YEŞİL olduklarını gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~AboutWiringTests|FullyQualifiedName~KeyboardWiringTests|FullyQualifiedName~KeyboardShortcutTests|FullyQualifiedName~ShortcutCatalogTests|FullyQualifiedName~AboutDialogTests"`

Expected: hepsi PASS. `ShortcutCatalogTests`'in kapsama testi artık F1'i de kapsıyor olmalı (About satırı
eklendiği için).

- [ ] **Step 8: TAM SÜİT**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`

Expected: tamamı PASS — token/motion/D8/erişilebilirlik guard'ları dahil. `AccessibilityTests` ya da
`TooltipTests` yeni butonu kapsıyorsa onların da yeşil olması gerekir.

- [ ] **Step 9: Commit**

```bash
git add src/BuildOrchestrator.App tests/BuildOrchestrator.Tests
git commit -m "feat(about): title bar info butonu + F1 + Esc zinciri"
```

---

## Task 8: Dokümanlar

**Files:**
- Modify: `ARCHITECTURE.md` (§13/§14 UI + design system, §22 kod haritası)
- Modify: `README.md` (§Keyboard shortcuts + About'un ne gösterdiği)

**Kural:** doküman projeyi ANLATIR — "şu oturumda şunu ekledik" YAZILMAZ; değişen davranış ilgili bölümde
**yerinde yeniden yazılır**. Bayatlayacak sayı (test sayısı, sha) yazma. Her iddia kodda doğrulanır.

- [ ] **Step 1: `README.md` — kısayol tablosuna F1**

`README.md:161-175` bölümü. Tabloya (Ctrl+F satırından sonra) `F1` satırı eklenir ve tablonun altındaki
paragrafta Settings'in kapsamını anlatan cümle güncellenir (artık ikinci bir modal var):

```markdown
| `F1` | About — version, shortcuts and diagnostics |
```

- [ ] **Step 2: `README.md` — About ekranını anlat**

Kısayol tablosunun ardına kısa bir alt bölüm: title bar'daki info butonu; üç sekme (Shortcuts / Environment /
Third-party); *Copy diagnostics*'in bir destek talebine yapıştırılacak metni panoya yazdığı; global kısayol
kaydı düşmüşse bunun About'ta `unavailable` olarak göründüğü.

- [ ] **Step 3: `ARCHITECTURE.md` — UI ve design system**

§13/§14'te yerinde güncelle:
- Title bar artık iki ikon butonu taşır (gear + info), ayraçtan sonra, kullanım sıklığı azalan sırada.
- İki modal vardır ve **aynı anda yalnız biri açılabilir**; Esc zinciri ikisini de kapsar; F1 modal açıkken no-op.
- `Ds.Segment` diyalog içinde sekme anahtarı olarak da kullanılır; About'un içerik alanı **sabit yüksekliktedir**
  (sekme değişince footer oynamaz).
- `Icon.Info` **türetilmiştir** — design-v1 ikon tablosunda yoktur (`Icon.CaptionRestore` ile aynı statü).
- Marka logosu `Controls/BrandLogo.xaml`'dedir; title bar ve About aynı kontrolü kullanır.

- [ ] **Step 4: `ARCHITECTURE.md` §22 — kod haritası**

Yeni dosyaları ekle: `Shell/ShortcutCatalog.cs`, `Controls/BrandLogo.xaml`, `Services/AppIdentity.cs`,
`Services/DiagnosticsReport.cs`, `Services/ThirdPartyNotices.cs`, `Views/AboutDialog.xaml`. Kısayol metinlerinin
tek kaynağının artık `ShortcutCatalog` olduğunu ilgili satırda belirt.

- [ ] **Step 5: Tam süit + commit**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
git add README.md ARCHITECTURE.md
git commit -m "docs: About ekrani + F1 kisayolu"
```

- [ ] **Step 6: `main`'e merge, branch temizliği**

```bash
git checkout main
git merge --no-ff feat/about-dialog
git push origin main
```

Merge'ün geçtiğini **doğruladıktan sonra**:

```bash
git branch -d feat/about-dialog
git push origin --delete feat/about-dialog   # remote'a hiç push edilmediyse atlanır
```

---

## Uygulama sırasında karşılaşılabilecek riskler

| Risk | Belirti | Yapılacak |
|---|---|---|
| `BrandLogo` çıkarımı logo ölçü testlerini kaydırır | `MainWindowRealizeTests` ortalama testi FAIL | Task 2 Step 5'teki not: `VerticalAlignment="Center"` iç `Viewbox`'a. **Testi gevşetme.** |
| `BooleanToVisibilityConverter` kaynağı yok | About XAML parse hatası | Süitte mevcut bir kayıt ara; yoksa `Controls.xaml`'e TEK kayıt ekle. |
| `Assembly.Load` bir paketi bulamaz | `ThirdPartyNoticesTests` FAIL | Çıktı dizinindeki gerçek DLL adını oku, tabloyu düzelt. Testi gevşetme. |
| `DispatcherPump` imzası farklı | `AboutDialogTests` derlenmez | Mevcut yardımcıyı kullan; yeni pompa YAZMA. |
| Sabit içerik yüksekliği (260) bir sekmeyi kırpar | Environment'ta kaydırma çubuğu | Sayıyı büyütmek serbest (bütçe değil, yerleşim); test davranışı pinler, sayıyı değil. |
| `RunViewModel`'de aktif konsol metnini okuyan yüzey yok | `AppIdentityTests` derlenmez | Task 3 Step 1'deki not: `GetActiveLineCount()` ile pinle, yeni test yüzeyi AÇMA. |
