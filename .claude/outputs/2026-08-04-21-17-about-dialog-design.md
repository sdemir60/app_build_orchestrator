# About / Info ekranı — tasarım (spec)

Tarih: 2026-08-04 · Branch: `feat/about-dialog`

Title bar'a bir info ikon butonu; tıklanınca Settings ile aynı kabuğu paylaşan modal bir **About** diyaloğu.
İçinde ürün kimliği, klavye kısayolları, ortam/tanı bilgisi ve üçüncü-taraf lisansları.

---

## 1. Kapsam

Girer:

- Title bar'da yeni info ikon butonu (`Icon.Info` dahil).
- `Views/AboutDialog.xaml` — 4 satırlı modal (hero / sekmeler / içerik / footer).
- Üç sekme: **Shortcuts** · **Environment** · **Third-party**.
- `F1` kısayolu + Esc katman zincirinin genişlemesi.
- "Kopya YASAK" değişmezinin zorunlu kıldığı dört tek-kaynak işi (§5).

Girmez:

- Settings'in içeriğine dokunmak.
- Global kısayolu (Alt+B) değiştirme yüzeyi — bugün olduğu gibi yalnız `ui-state.json`'dan okunur, About onu
  **gösterir**, değiştirmez.
- Güncelleme denetimi, lisans anahtarı, oturum/telemetri gibi kurumsal About'larda görülen ama bu üründe
  karşılığı olmayan yüzeyler.

## 2. Title bar butonu

**Yer:** gear'ın **sağında**, aynı `StackPanel` grubunda — `ayraç → gear → info → caption butonları`.
Gerekçe: grup kullanım sıklığı azalan sırada dizilir (layout seçici > settings > about) ve Windows/Office
geleneğinde Help/About uygulama komutlarının en sonundadır.

Kalıp, gear ile birebir aynı (`MainWindow.xaml`):

- `Style="{DynamicResource Ds.IconButton}"`, `Viewbox 16x16` + `Canvas 24x24`,
  `Path Style="{StaticResource TitleBarIcon}"`, `StrokeThickness="{DynamicResource Icon.Info.StrokeThickness}"`.
- `AutomationProperties.Name="About"`.
- `ToolTip` `controls:AppTooltip.Side="Bottom"`. **Metin elle yazılmaz** — `ShortcutCatalog.Get(ShortcutId.About).Description`
  (§5.1) okunur, yani tooltip ile Shortcuts sekmesindeki F1 satırı aynı cümleyi paylaşır:
  `About — version, shortcuts and diagnostics`. XAML'den erişim için `AboutTooltipText` gibi bir
  `x:Static` sarmalayıcı yeterlidir.

**Yeni ikon** — `Resources/Icons.xaml`:

```xml
<GeometryGroup x:Key="Icon.Info">
  <EllipseGeometry Center="12,12" RadiusX="10" RadiusY="10" />
  <PathGeometry Figures="M12 16v-4 M12 8h.01" />
</GeometryGroup>
<sys:Double x:Key="Icon.Info.StrokeThickness">1.7</sys:Double>
```

Kalınlık `Icon.Gear` ile aynı gruptan (1.7): iki buton komşu, optik ağırlıkları eşleşmeli.

> **Otorite notu (dosyaya yorum olarak yazılacak):** design-v1 `BuildApp.jsx:44-72` ikon tablosunda `info`
> **yoktur**. Bu geometri, `Icon.CaptionRestore` ile aynı statüdedir — **türetilmiş**, kaynaktan birebir
> kopyalanmış değil. Türetme lucide `info` ikonundan yapılır (diğer tüm 24-viewBox ikonların ailesi lucide'dır),
> böylece ikon dili tutarlı kalır.

## 3. Diyalog iskeleti — `Views/AboutDialog.xaml`

Kabuk Settings ile **birebir** aynı, bu yüzden yeni bir modal deseni girmiyor:

- Kök `Grid x:Name="Scrim"`, `Background="{DynamicResource Brush.Scrim}"`, `MouseLeftButtonDown` → kapat.
- Odak tuzağı: `KeyboardNavigation.TabNavigation="Cycle"` + `ControlTabNavigation="Cycle"` +
  `FocusManager.IsFocusScope="True"`.
- İçte `Border Width="620" Style="{DynamicResource Ds.Dialog}"`, `MouseLeftButtonDown` → `e.Handled = true`.
- `Open()` görünür kılar + `Scrim.MoveFocus(First)`; `Close()` `Visibility=Collapsed`.
- `OnKeyDown` Esc → kapat + handled.
- MainWindow'da Settings'in yanında, `Grid.Row="0" Grid.RowSpan="2"`, `WindowChrome.IsHitTestVisibleInChrome="True"`.

Settings'ten **farkı**: gövde tek bir uzun scroll değil, sekmeli. Dört satır:

```
┌─ Ds.Dialog 620 ────────────────────────────────────────┐
│  [logo 20]  Build Orchestrator                         │  0: hero (Auto)
│             Ordered, incremental builds for a          │
│             multi-project .NET solution.               │
│             1.0.0+it5 · engine 1.0.0+it5 · © 2026 Delta│
│                                                        │
│  ┌─────────┐┌───────────┐┌───────────┐                 │  1: Ds.Segment (Auto)
│  │Shortcuts││Environment││Third-party│                 │
│  └─────────┘└───────────┘└───────────┘                 │
├────────────────────────────────────────────────────────┤
│  Build — or Stop while a run is in flight       [F5]   │  2: içerik
│  Rebuild                          [Ctrl+F5] [Shift+F5] │     (SABİT yükseklik
│  Focus the project filter                   [Ctrl+F]   │      ScrollViewer)
│  …                                                     │
├────────────────────────────────────────────────────────┤
│  [Copy diagnostics]                           [Close]  │  3: footer (Auto)
└────────────────────────────────────────────────────────┘
```

**Sabit içerik yüksekliği zorunludur:** sekme değişince diyaloğun boyu değişmemeli. İçerik satırı
`RowDefinition Height="Auto"` **değil**, `ScrollViewer` üzerinde açık bir `Height` taşır; değeri en uzun
sekmeyi (Environment, 10 satır) sığdıracak şekilde seçilir, kısa sekmeler alt boşlukla kalır, taşan sekme
kendi içinde kaydırır. Sayı bir literaldir (Settings'in `MaxHeight="640"`'ı gibi — bu kod tabanında diyalog
ölçüleri token'lı değil); **test sayıyı değil davranışı pinler**: "üç sekmede de diyalog `ActualHeight`'ı
aynı". Zıplayan bir modal profesyonel durmaz ve footer'ın yeri her sekmede aynı kalmalıdır.

**Sekme anahtarı** `Ds.Segment` + üç `Ds.Segment.Item` (`RadioButton`, `GroupName="about"`). Bu bileşen tasarım
sisteminde zaten var ve `ActionBar.xaml:50-52`'de Debug/Release için kullanılıyor — **yeni bir kalıp girmiyor**,
mevcut bileşen yeniden kullanılıyor. Sekme gövdeleri üç `Grid`; seçili olan `Visible`, diğerleri `Collapsed`
(`DataTrigger`/`IsChecked` ile, kod-tarafı görünürlük yönetimi yok).

### 3.1 Hero

| Satır | Kaynak | Stil |
|---|---|---|
| Logo | `controls:BrandLogo` (§5.2), `Height="20"` | — |
| `Build Orchestrator` | `AssemblyProductAttribute` (`Directory.Build.props` `<Product>`) | `FontSize.Lg` + `FontWeight.Emphasis` + `Brush.TextPrimary` |
| Tek cümlelik tanım | Sabit metin (bu değerin başka kaynağı yok) | `FontSize.Xs` + `Brush.TextDim`, `TextWrapping="Wrap"` |
| `{app} · engine {engine} · {copyright}` | `AssemblyInformationalVersion` · `RunViewModel.EngineVersion` · `AssemblyCopyrightAttribute` | mono, `FontSize.2xs`, `Brush.TextFaint` |

Motor henüz doğmamışsa engine parçası `engine not started` olur (boş string ya da `v` öneki asılı kalmaz).

Telif satırı **tek parça** olarak `AssemblyCopyrightAttribute`'tan gelir (`© 2026 Delta`) — yıl ve şirket adı
UI'da ayrı ayrı birleştirilmez, `DateTime.Now.Year` kullanılmaz (telif yılı bir çalışma-zamanı değeri değildir).
Bu attribute bugün yok; `Directory.Build.props`'a eklenir (§5.6) ve tek kaynak orası olur.

Ürün tanımı metni: `Ordered, incremental builds for a multi-project .NET solution.`

### 3.2 Shortcuts sekmesi

Satır düzeni: açıklama solda (`FontSize.Sm`, `Brush.TextSecondary`), `Ds.Kbd` rozetleri sağda
(`StackPanel Orientation="Horizontal"`, birden çok jest yan yana). Kaynak: `ShortcutCatalog` (§5.1).

| Jest(ler) | Açıklama |
|---|---|
| `F5` | Build — or Stop while a run is in flight |
| `Ctrl+F5` `Shift+F5` | Rebuild — all projects, cache ignored |
| `Ctrl+F` | Focus the project filter |
| `F1` | About — version, shortcuts and diagnostics |
| `Esc` | Close the topmost open layer: dialog → popover/menu → selection |
| `Alt+B` | Global — bring the window back from the tray |

`Alt+B` satırı, kayıt çakışma yüzünden düşmüşse (`HotkeyRegistration.IsRegistered == false`) soluk çizilir
(`Opacity` düşürülmez — `Brush.TextFaint` kullanılır) ve satır sonuna `unavailable` notu düşer. README'nin
"sessizce devre dışı" davranışı böylece ilk kez **görünür** olur; bugün kullanıcının bunu anlamasının yolu yok.

Bunun için `MainWindow`'un tuttuğu `HotkeyRegistration`'ın `IsRegistered`'ı `Open()`'a parametre olarak geçer
(About, `MainWindow`'un alanına erişmez).

### 3.3 Environment sekmesi

Etiket/değer satırları: etiket solda sabit 130px (`FontSize.Xs`, `Brush.TextDim`), değer mono
(`FontSize.Xs`, `Brush.TextSecondary`, `TextTrimming="CharacterEllipsis"`, tooltip'te tam metin).

| Etiket | Kaynak |
|---|---|
| App version | `AssemblyInformationalVersion` |
| Engine version | `RunViewModel.EngineVersion` (yoksa `not started`) |
| Engine PID | `RunViewModel.EnginePid` (yoksa `—`) |
| .NET runtime | `RuntimeInformation.FrameworkDescription` |
| OS | `RuntimeInformation.OSDescription` |
| MSBuild | `MsBuildResolver.ResolveAsync()` → `{MsBuildExePath} (v{Version})` |
| Repository root | `RunViewModel.RootPath` (boşsa `no repository`) |
| State file | `JsonUiStateStore.DefaultPath` |
| Logs | `RunLogPaths.DefaultLogsRoot` |
| Worktree pool | `WorktreeManager.DefaultPoolRoot` |

Yol metinleri **yeniden yazılmaz** — üç `Default*` static'inden okunur (bugün `"BuildOrchestrator"` klasör adı
o üç dosyada ayrı ayrı duruyor; bu spec o mevcut durumu **değiştirmez**, sadece yeni bir dördüncü kopya
üretmez).

**MSBuild çözümü async'tir** ve `vswhere` child process'i başlatır. Bu yüzden:

- Environment sekmesi **ilk kez** açıldığında tetiklenir (diyalog açılışında değil — About'u açmak bir process
  başlatmamalı).
- Bir diyalog ömrü boyunca **bir kez** çözülür, sonucu cache'lenir.
- Üç durum: `resolving…` → yol + sürüm → `MsBuildResolveException.Message` (hata metni olduğu gibi gösterilir,
  `Brush.TextFaint`).
- `MsBuildResolver(new ProcessRunner())` — App'te `ProcessRunner` zaten kullanılıyor (`App.xaml.cs:103`).

### 3.4 Third-party sekmesi

Satır: bileşen adı (`FontSize.Xs`, `Brush.TextSecondary`) + sürüm (mono, `Brush.TextFaint`) + lisans adı sağda
(`FontSize.2xs`, `Brush.TextDim`). Kaynak: `ThirdPartyNotices` (§5.4).

| Bileşen | Lisans |
|---|---|
| AvalonEdit | MIT |
| CommunityToolkit.Mvvm | MIT |
| H.NotifyIcon.Wpf | MIT |
| Microsoft.Extensions.DependencyInjection | MIT |
| Geist · Geist Mono (embedded fonts) | SIL Open Font License 1.1 |

Sekmenin altında tek satır not: `The full Geist license text ships as GEIST-LICENSE.txt next to the application.`
(OFL'in "included in all copies" şartı bugün dosya olarak karşılanıyor; bu satır atıfı **görünür** kılar.)

**Sürümler csproj'dan kopyalanmaz** — runtime'da yüklü assembly'nin `AssemblyInformationalVersionAttribute`'undan
(yoksa `AssemblyName.Version`'dan) okunur. Fontun sürümü yok, o satırda sürüm alanı boş kalır.

### 3.5 Footer

Settings'in footer'ıyla aynı iskelet: `Border` üstte `Brush.BorderSubtle` 1px, `DockPanel Margin="20,12"`.

- Sol: `Copy diagnostics` — `Ds.Button.Ghost.Md`. `DiagnosticsReport` metnini panoya kopyalar.
- Sağ: `Close` — `Ds.Button.Secondary.Md`. About bir şey commit etmez; Primary bir mutasyon ima ederdi.

Kopyalama geri bildirimi: butonun metni 1.5 sn `Copied` olur, sonra geri döner. (`Ds.Button` üzerinde
mevcut bir "kopyalandı" deseni yok; en ucuz ve tutarlı geri bildirim budur. Toast/snackbar yüzeyi eklenmez.)

## 4. Klavye ve katman

- `WindowIntent` enum'una `ShowAbout` eklenir.
- `KeyboardShortcuts.WindowBindings`'e `new(Key.F1, ModifierKeys.None, WindowIntent.ShowAbout)`.
- **Bir modal açıkken F1 no-op'tur.** Gerekçe: Settings açıkken F1'in Settings'i kapatıp About'u açması,
  kaydedilmemiş bir taslağı sessizce çöpe atardı. Kural: `if (AnyDialogOpen) return;`
- İki modal **karşılıklı dışlayıcıdır**: `SettingsOverlay.Open` ve `AboutOverlay.Open` çağrılmadan önce
  diğerinin kapalı olduğu garanti edilir (yukarıdaki no-op kuralı zaten sağlıyor; gear/info butonları modal
  açıkken scrim'in altında kaldığı için tıklanamaz — scrim `RowSpan=2` + `IsHitTestVisibleInChrome="True"`).
- Esc zinciri: `MainWindow.OnEscapePressed`'deki
  `bool dialogOpen = SettingsOverlay.Visibility == Visibility.Visible;`
  → `SettingsOverlay` **veya** `AboutOverlay` görünür. `EscAction.CloseDialog` hangisi açıksa onu kapatır.
  `KeyboardShortcuts.ResolveEsc`'in **imzası değişmez** (saf karar zaten "bir dialog açık mı" soruyor) —
  değişen yalnız `dialogOpen`'ın nasıl hesaplandığı.

## 5. Tek doğruluk kaynağı işleri

CLAUDE.md değişmezi: *"aynı değer, metin veya primitif iki yerde tanımlanmaz"*. About ekranı dört yerde bu
kuralı tetikliyor; dördü de bu işin parçası.

### 5.1 `Shell/ShortcutCatalog.cs` — kısayol metinleri

Bugün `"F5"` ve `"Ctrl+F5"` metinleri `BuildMenu.ComposeItems` içinde **elle yazılı**
(`BuildMenu.xaml.cs:83-84`), gerçek bağlamalar ise `KeyboardShortcuts.WindowBindings`'te. About tablosu
üçüncü bir kopya olurdu.

Yeni saf tip:

```csharp
public readonly record struct ShortcutEntry(
    ShortcutId Id, IReadOnlyList<string> Gestures, string Description);

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutEntry> All { get; }
    public static ShortcutEntry Get(ShortcutId id);
    /// Bir WindowBinding'i insan-okur jeste çevirir: "Ctrl+F5", "F5", "Esc".
    public static string Format(Key key, ModifierKeys modifiers);
}
```

- `Gestures` **`WindowBindings`'ten türetilir** (`Format` ile), elle yazılmaz. Alt+B tek istisna:
  `HotkeyBinding.DefaultGesture` sabitinden gelir (o da tek kaynak).
- `Description` metinleri burada tanımlanır — başka hiçbir yerde.
- `BuildMenu.ComposeItems` rozetleri `ShortcutCatalog.Get(ShortcutId.Build).Gestures[0]` gibi okur;
  iki literal **silinir**.
- Kaynak-tarama guard'ı: `"Ctrl+F5"` metni `ShortcutCatalog.cs` dışında hiçbir `src/**/*.cs` ya da
  `src/**/*.xaml` dosyasında geçmez. (Projede benzer guard deseni var: `IconGeometryTests`'in font-adı
  taraması, `PublishLayoutTests`.)

`ShortcutId`: `Build`, `Rebuild`, `FocusFilter`, `About`, `Escape`, `RestoreFromTray`.

### 5.2 `Controls/BrandLogo.xaml` — Delta logosu

Logo Path'leri bugün `MainWindow.xaml:148-168`'de inline. About hero'suna kopyalamak yasak.

- Yeni `UserControl`: kökü `Viewbox Stretch="Uniform"` + içinde mevcut `Canvas`/`Path` ağacı **birebir**
  (transform'lar ve `Brush.Amber`/`Brush.TextPrimary` referansları değişmeden taşınır).
- `MainWindow.xaml` → `<controls:BrandLogo x:Name="TitleBarLogo" Height="15" VerticalAlignment="Center" />`.
- About hero → aynı kontrol, `Height="20"`.

**Kırmızı çizgi:** iki mevcut test yeşil kalmalı — `MainWindowRealizeTests` (logo iç kutuda dikey ortalı,
`AlignmentSteps` ağaçtan hesaplandığı için bir seviye artışını yutar) ve
`TitleBarContextTests.The_title_bar_logo_is_fifteen_pixels_tall` (`window.TitleBarLogo.Height == 15` +
`ActualHeight ≈ 15`). `x:Name="TitleBarLogo"` korunur, tip `Viewbox`'tan `BrandLogo`'ya döner — ikisi de
`FrameworkElement`, testlerin kullandığı üyeler değişmez.

### 5.3 `Services/DiagnosticsReport.cs` — tanı metni

Environment sekmesinin gösterdiği satırlar ile "Copy diagnostics"in ürettiği metin **aynı modelden** gelmeli;
iki ayrı liste sessizce ayrışır.

```csharp
public readonly record struct DiagnosticsLine(string Label, string Value);

public static class DiagnosticsReport
{
    /// Saf: hiçbir şey okumaz/çalıştırmaz, verilen değerleri satırlara dizer.
    public static IReadOnlyList<DiagnosticsLine> Compose(DiagnosticsInput input);
    /// Saf: satırları panoya gidecek düz metne çevirir (hizalı "Label: Value").
    public static string ToText(IReadOnlyList<DiagnosticsLine> lines);
}
```

`DiagnosticsInput` bir `record` — app sürümü, engine sürümü/PID, runtime, OS, MSBuild sonucu, repo kökü, üç
yol. Toplayıcı (`Environment.*`, `RuntimeInformation.*`, `MsBuildResolver`) view tarafındadır; `Compose`/`ToText`
WPF'siz test edilir.

### 5.4 `Services/ThirdPartyNotices.cs` — bileşen tablosu

```csharp
public readonly record struct ThirdPartyComponent(
    string DisplayName, string? AssemblyName, string License, string Url);

public static class ThirdPartyNotices
{
    public static IReadOnlyList<ThirdPartyComponent> All { get; }
    /// Yüklü assembly'den sürüm; bulunamazsa null (satır sürümsüz çizilir).
    public static string? ResolveVersion(ThirdPartyComponent component);
}
```

`AssemblyName == null` → font satırı (sürüm alanı boş).

### 5.5 `RunViewModel` — motor kimliği

Bugün `OnEngineReady(string engineVersion)` değeri yalnız konsol satırına yazıp atıyor
(`RunViewModel.cs:1209`). About'un okuyabilmesi için değer **saklanır**:

- `public string? EngineVersion { get; private set; }`
- `public int? EnginePid { get; private set; }`
- `OnEngineReady` imzası `(string engineVersion, int pid)` olur; `MainWindow.xaml.cs:587` çağrısı
  `ready.EngineVersion` yanında `ready.Pid`'i de geçer (`EngineReadyEvent(Pid, EngineVersion)` ikisini de
  taşıyor, PID bugün atılıyor).
- Konsol boot satırı **değişmez** (`Engine ready — v{version}`) — davranış aynı kalır.

### 5.6 `Directory.Build.props` — telif

`<Copyright>© 2026 Delta</Copyright>` eklenir. Hero'daki telif satırı `AssemblyCopyrightAttribute`'tan okunur;
UI'da yıl ya da şirket adı **yeniden yazılmaz**.

## 6. Kod haritası

| Dosya | Durum | Ne |
|---|---|---|
| `Resources/Icons.xaml` | değişir | `Icon.Info` + `Icon.Info.StrokeThickness` |
| `MainWindow.xaml` | değişir | info butonu; logo → `BrandLogo`; `AboutOverlay` |
| `MainWindow.xaml.cs` | değişir | `OnAbout`, `ShowAbout` intent kablajı, `dialogOpen` genişlemesi, `OnEngineReady(v, pid)` |
| `Shell/KeyboardShortcuts.cs` | değişir | `WindowIntent.ShowAbout` + F1 satırı |
| `Shell/ShortcutCatalog.cs` | **yeni** | §5.1 |
| `Controls/BrandLogo.xaml(.cs)` | **yeni** | §5.2 |
| `Views/AboutDialog.xaml(.cs)` | **yeni** | §3 |
| `Views/BuildMenu.xaml.cs` | değişir | rozetler katalogdan; iki literal silinir |
| `ViewModels/RunViewModel.cs` | değişir | §5.5 |
| `Services/DiagnosticsReport.cs` | **yeni** | §5.3 |
| `Services/ThirdPartyNotices.cs` | **yeni** | §5.4 |
| `Directory.Build.props` | değişir | `<Copyright>` |
| `AccessibilityNames.cs` | değişir (muhtemel) | About içindeki isimsiz kalan kontroller için |

## 7. Test planı

Kural: **hiçbir fix/özellik, kusuru veya eksiği yakalayan test KIRMIZI gösterilmeden yazılmaz.**

### Saf (WPF'siz)

| Test | Ne pinler |
|---|---|
| `ShortcutCatalog` her `WindowBindings` satırını kapsar | Yeni bir bağlama eklenip katalog güncellenmezse kırar |
| `ShortcutCatalog`'ta yetim kayıt yok | Katalogda karşılığı olmayan jest metni kalmaz |
| `Format(Key.F5, Control)` → `"Ctrl+F5"`, `Format(Key.Escape, None)` → `"Esc"` | Jest metni üretimi |
| Alt+B kaydı `HotkeyBinding.DefaultGesture`'dan gelir | Global kısayol metni ikinci kez yazılmaz |
| `WindowBindings` F1 → `ShowAbout` | Yanlış tuş/modifier/niyet |
| `ResolveEsc(dialogOpen: true, …)` → `CloseDialog` | Mevcut davranış korunur (regresyon ağı) |
| `BuildMenu.ComposeItems` rozetleri katalogdan | Literal geri sızarsa kırar |
| `DiagnosticsReport.Compose` satır sırası + eksik değerlerin karşılığı (`not started`, `—`, `no repository`) | Tanı metni sözleşmesi |
| `DiagnosticsReport.ToText` her satırı içerir, hizalıdır | Panoya giden metin |
| `ThirdPartyNotices.All` boş değil; `AssemblyName` dolu olan her kayıt bir sürüm çözer | Paket adı yanlış yazılırsa kırar |
| `RunViewModel.OnEngineReady` sürüm + PID'i saklar, konsol satırı değişmez | §5.5 |

### Kaynak guard'ları

| Test | Ne pinler |
|---|---|
| `"Ctrl+F5"` literali yalnız `ShortcutCatalog.cs`'te | §5.1 kopya yasağı |
| `Icon.Info`'nun `StrokeThickness` kardeşi var | `IconGeometryTests` zaten her ikon için bunu pinliyor — yeni ikon otomatik kapsanır, ayrıca açık assert eklenir |
| Delta logo path verisi yalnız `BrandLogo.xaml`'de | §5.2 kopya yasağı |

### WPF / STA (realize)

| Test | Ne pinler |
|---|---|
| **`AboutDialog` realize** — `window.Content` üzerinde ölçülür (CLAUDE.md kuralı: yeni XAML kökü ⇒ realize testi) | XAML runtime'da çözülüyor |
| Sekme değişiminde diyalog yüksekliği **değişmez** | §3'ün sabit içerik yüksekliği |
| Odak tuzağı: About açıkken Tab arka plandaki kontrollere kaçmaz | Settings'teki `Fix1` regresyonunun About'ta tekrarlanmaması |
| Esc About'u kapatır; Settings kapalıyken/açıkken doğru katman | §4 |
| Info butonu ağaçta ve gear'ın **sağında** (aynı `StackPanel`, index gear+1) | §2 yerleşimi |
| `MainWindowRealizeTests` + `TitleBarContextTests` logo testleri **yeşil** | §5.2'nin kırmızı çizgisi |
| Alt+B satırı `isRegistered: false` ile `unavailable` gösterir | §3.2 |

Bitişte **tam süit yeşil** (`--filter "Category!=Acceptance"`), token/motion/D8 guard'ları dahil.

## 8. Doküman etkisi

- **ARCHITECTURE.md** — §13/§14 (UI + design system): title bar'ın ikinci ikon butonu, ikinci modal ve
  About'un sekme yapısı. §22 kod haritasına yeni dosyalar. Esc zincirinin iki diyalogu kapsaması.
  `Icon.Info`'nun türetilmiş olduğu (otoritede yok) kaydı.
- **README.md** — §Keyboard shortcuts tablosuna `F1` satırı; About ekranının ne gösterdiğine dair bir-iki
  cümle (destek akışı: "Copy diagnostics").
- **CLAUDE.md** — değişiklik yok.

## 9. Bilinçli kararlar

| Karar | Gerekçe |
|---|---|
| Sekmeli, tek uzun scroll değil | Dört içerik sınıfı (kimlik / kısayol / ortam / lisans) birbirine bakmaz; tek scroll'da kullanıcı aradığını taramak zorunda kalır. `Ds.Segment` zaten DS'te var. |
| 620px (Settings ile aynı) | İki modalin farklı genişlikte olması sebepsiz. En uzun içerik (yol satırları) 130px etiket + trimming ile 620'ye sığıyor. |
| Sabit içerik yüksekliği | Sekme değişince footer'ın yerinden oynaması ucuz durur. |
| `Close` = Secondary, Primary değil | About commit etmez; Primary bir mutasyon ima eder. |
| Modal açıkken F1 no-op | Kaydedilmemiş Settings taslağını sessizce atmamak için. |
| MSBuild çözümü sekme açılışında, lazy | About'u açmak bir child process başlatmamalı. |
| Üçüncü-taraf sürümleri runtime'dan | csproj'daki `Version` değerini UI'da tekrar yazmak kopya olurdu. |
| Güncelleme denetimi / lisans anahtarı yok | Üründe karşılığı yok; About'a olmayan bir yüzey konmaz. |
