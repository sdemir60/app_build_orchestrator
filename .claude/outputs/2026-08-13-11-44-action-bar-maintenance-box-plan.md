# Action Bar Maintenance Box — Implementation Plan (Faz 1/4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Action bar'ı tasarım v1.7.0'daki hâline getirmek — Sync'in sağında üç ikonlu bakım kutusu (Clean ·
Optimize · Resolve cycles), mevcut döngü motorunun Resolve düğmesine bağlanması, Build menüsünün iki maddeye
inmesi ve `Continue`/`RetryFailed` koşu modlarının koddan kaldırılması.

**Architecture:** Bakım kutusu ayrı bir `MaintenanceBox` UserControl'üdür (ActionBar.xaml.cs zaten büyük;
kutunun enable/tooltip/spinner mantığı kendi dosyasında yaşar ve tek başına realize edilerek test edilir).
Kutu üç `Ds.IconButton` taşır; Clean ve Optimize'ın komutu YOKTUR ve kalıcı olarak disabled'dır (arka uç
sonraki bir işte gelecek), Resolve mevcut `RunViewModel.BuildCyclesCommand`'a bağlanır. Sync'in yanındaki
etiketli `Cycles` düğmesi kalkar — o iş artık kutunun üçüncü ikonudur.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit (+ `StaFact`).

**Spec:** `.claude/outputs/2026-08-05-01-26-design-v1.7.0/README.md` — §2.7-2 (bakım grubu), §2.7-11 (Build
split-button), §3.1 (fazlar), §3.7 (Resolve cycles), §8 (yapılmayacaklar), v1.7.0 sürüm notu.
Prototip referansı: `prototype/app/BuildApp.jsx:1929-1959` (bakım kutusu markup'ı), `:59-61` (ikon path'leri).

---

## Global Constraints

Bu bölüm her task'ın gereksinimlerine örtük olarak dahildir.

- **Dil:** kod, UI metinleri ve loglar İngilizce; kod yorumları ve `.claude/` kayıtları Türkçe.
- **Kopya YASAK:** aynı değer/metin iki yerde tanımlanmaz. UI metinleri `AccessibilityNames`/`StreamText`/
  `RibbonText` gibi tek sabit dosyalarında durur; testler literali oradan okur.
- **Hardcoded hex/px/ms YASAK:** renk `Brush.*`, ölçü `Size.*`/`Radius.*`, süre motion token'larından çözülür.
  Bileşenin kendi ölçüleri (prototipte literal olanlar) `private const` olarak kaynak satırıyla yazılır.
- **Kırmızı test kuralı:** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz.
- **Davranış değişince testi de değişir:** eski kuralı pinleyen test silinmez/gevşetilmez; YENİ kuralı
  pinleyecek şekilde yeniden yazılır, doc'una eski iddia + değişme gerekçesi yazılır.
- **Realize testi:** yeni XAML kökü/şablonu ekleyen her değişiklik bir realize testi ekler
  (`DsResources.NewHost()` + `DsResources.Realize(host, control)`).
- **Test komutu:** `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Tek test için `--filter "FullyQualifiedName~<TestAdı>"`.
- **Uygulama açıkken build alınmaz** — çalışan Supervisor kendi binary'lerini kilitler.
- **Commit:** task başına bir commit, çalışma branch'i `design/v17-action-bar`.

### Bu iş için sabitlenmiş kararlar (kullanıcı onayı, 2026-08-13)

1. **Tur sayısı motorun kararıdır.** Tasarımın "pass 1/2" literali BAĞLAYICI DEĞİLDİR; gerçek tur sayısı
   `CycleRoundPolicy` (BaselineRounds=2, RoundCap=3) tarafından belirlenir. Tasarımdan alınan şey görsel
   dildir. (Bu faz kapsamı dışında — Faz 4.)
2. **Clean ve Optimize'ın arka ucu YOK.** Düğmeler görünür ve **disabled**; tooltip tasarımdaki metin +
   `— not available yet` eki.
3. **Continue ve Retry failed koddan kalkar.** Gerekçe: Stop'tan sonra Build zaten derlenmemişleri derler,
   hata sonrası Build zaten hatalıları alır (başarılılar `up to date` atlanır).
4. **Metinler tasarıma çekilir**, ancak kod tarafında sonradan geliştirilmiş DAHA İYİ metinler korunur.
5. **Tasarım çerçevesi birebir**; kodda sonradan eklenmiş faydalı bilgilendirmeler (tooltip sayıları gibi)
   korunur/uyarlanır.

---

## Faz haritası (4 plan)

Bu dosya **Faz 1**'dir. Sonraki fazlar kendi plan dosyalarını alacak; buraya yalnız kapsam sınırı için yazıldı.

| Faz | Kapsam | Tasarım kaynağı |
|---|---|---|
| **1 (bu plan)** | Action bar: bakım kutusu, Resolve bağlama, Build menüsü, `Continue`/`RetryFailed` temizliği | v1.4.0 (UI kabuğu) + v1.7.0 §2.7 |
| 2 | Konsol: saat/`▸` kolonu kalkar, daktilo kalkar, tilt-in geçiş, Geist Mono 300 | v1.6.0 §2.5 |
| 3 | Üç kanal: kart satırı (şerit/nokta/isim/SHA/uyarı slotu) + graf çekirdek renkleri | v1.7.0 §2.3, §2.4, §5 |
| 4 | Şerit + sayaç/filtre + Resolve koşu anlatısı (şerit/konsol/stream) | v1.5.x + v1.7.0 §2.2, §3.7 |

---

## Dosya yapısı

**Oluşturulacak:**

| Dosya | Sorumluluk |
|---|---|
| `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml` | Kutu kabuğu: 24px yükseklik, `surface-raised` zemin, 1px `border`, `Radius.Xs`, `ClipToBounds`; içinde 3 × 28×22 ikon buton + 2 × 1px×14 ayraç. |
| `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs` | İçerik kurulumu (ikon/spinner), tooltip'lerin tek yazıcısı, enable kuralları, `BuildCyclesCommand` kablajı. |
| `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs` | Kutunun yerleşimi, tooltip metinleri, disabled kuralları, Resolve kablajı ve spinner davranışı. |

**Değiştirilecek:**

| Dosya | Ne |
|---|---|
| `src/BuildOrchestrator.App/Resources/Icons.xaml` | `Icon.Eraser`, `Icon.Gauge`, `Icon.Unlink` eklenir. |
| `src/BuildOrchestrator.App/AccessibilityNames.cs` | Üç düğmenin UIA adı + tooltip metinleri; `CyclesButton*` yerine `ResolveCycles*`. |
| `src/BuildOrchestrator.App/Views/ActionBar.xaml` | `PART_Cycles` düğmesi çıkar, `MaintenanceBox` girer. |
| `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs` | Cycles kablajı/tooltip'i çıkar; kutu test yüzeyi olarak açılır. |
| `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs` | `retry` maddesi çıkar; Build açıklaması `Only stale projects` olur. |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` | `RetryFailedCommand` + `RunMode` eşlemeleri çıkar. |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` | `RunMode.Continue` dalı çıkar. |
| `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` | `RunMode`'dan `Continue` ve `RetryFailed` çıkar. |
| `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` | İki modun tüm dalları çıkar (resume snapshot, retryable kapısı, obj-root istisnası). |
| `ARCHITECTURE.md`, `README.md` | Action bar, koşu modları ve döngü yüzeyi anlatısı. |

---

## Task 1: Üç bakım ikonu sözlüğe eklenir

**Files:**
- Modify: `src/BuildOrchestrator.App/Resources/Icons.xaml`
- Modify: `tests/BuildOrchestrator.Tests/App/IconGeometryTests.cs:24-36` (`RequiredKeys` dizisi)

**Interfaces:**
- Consumes: yok (ilk task).
- Produces: `Icon.Eraser`, `Icon.Gauge`, `Icon.Unlink` kaynak anahtarları — Task 2 bunları
  `IconVisual.Make(this, "Icon.Eraser", …)` ile çözer. Üçü de viewBox `0 0 24 24`, KONTURLU (filled değil).

- [ ] **Step 1: Write the failing test**

`tests/BuildOrchestrator.Tests/App/IconGeometryTests.cs` içindeki `RequiredKeys` dizisine, `Icon.ChipRemove`
satırının altına ekle:

```csharp
        // [design v1.7.0 §2.7-2] Bakım kutusunun üç ikonu — prototype/app/BuildApp.jsx:59-61 (lucide
        // eraser/gauge/unlink). Anahtar eksikse düğme runtime'da boş Data ile çizilir, derlemede patlamaz.
        "Icon.Eraser", "Icon.Gauge", "Icon.Unlink",
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~IconGeometryTests"
```

Expected: FAIL — `All_required_icon_keys_are_defined` (veya eşdeğeri) `Icon.Eraser` anahtarının sözlükte
olmadığını söyler.

- [ ] **Step 3: Write minimal implementation**

`src/BuildOrchestrator.App/Resources/Icons.xaml` içinde `Icon.Sync` bloğunun altına ekle. Path data
prototipten BİREBİR kopyalanır (yeniden çizim/yuvarlama yok — dosyanın başındaki KAYNAK kuralı):

```xml
    <!-- design v1.7.0 §2.7-2 · BuildApp.jsx:59 eraser (Clean) -->
    <PathGeometry x:Key="Icon.Eraser"
                  Figures="m7 21-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21 M22 21H7 M5 11l9 9" />

    <!-- design v1.7.0 §2.7-2 · BuildApp.jsx:60 gauge (Optimize) -->
    <PathGeometry x:Key="Icon.Gauge"
                  Figures="M12 14l4-4 M3.34 19a10 10 0 1 1 17.32 0" />

    <!-- design v1.7.0 §2.7-2 · BuildApp.jsx:61 unlink (Resolve cycles) -->
    <PathGeometry x:Key="Icon.Unlink"
                  Figures="m18.84 12.25 1.72-1.71a4.5 4.5 0 0 0-6.36-6.37l-1.72 1.72 M5.17 11.75l-1.71 1.71a4.5 4.5 0 0 0 6.36 6.37l1.72-1.72 M8 2v3 M2 8h3 M16 19v3 M19 16h3" />
```

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~IconGeometryTests"
```

Expected: PASS (hem anahtar varlığı hem `Every_declared_icon_parses_to_a_non_empty_geometry`).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Resources/Icons.xaml tests/BuildOrchestrator.Tests/App/IconGeometryTests.cs
git commit -m "feat(app): bakim kutusunun uc ikonu ikon sozlugune eklendi"
```

---

## Task 2: Bakım kutusu kabuğu (üç ikon buton + iki ayraç)

**Files:**
- Create: `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml`
- Create: `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`
- Create: `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs`

**Interfaces:**
- Consumes: Task 1'in `Icon.Eraser`/`Icon.Gauge`/`Icon.Unlink` anahtarları.
- Produces:
  - `public partial class MaintenanceBox : UserControl` — DataContext bir `RunViewModel`.
  - Test yüzeyi: `internal Button CleanButton`, `internal Button OptimizeButton`, `internal Button ResolveButton`.
  - Task 4 `ResolveButton`'ın komut kablajını, Task 5 ActionBar yerleşimini kullanır.

**Ölçüler (prototipten literal — `BuildApp.jsx:1930-1933`):** kutu yüksekliği 24, buton 28×22, ayraç 1×14.

- [ ] **Step 1: Write the failing test**

`tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın bakım kutusu: chip ağırlığında TEK kutu (24px, surface-raised,
/// 1px border, radius-xs, overflow hidden) ve içinde üç 28×22 ikon buton — Clean · Optimize · Resolve cycles —
/// aralarında 1px×14 ayraçla. Etiket YOK: bar 1240px minimumda ancak böyle sığıyor (§2.7-2 gerekçesi),
/// anlam tooltip'te.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class MaintenanceBoxTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (MaintenanceBox box, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var box = new MaintenanceBox { DataContext = vm };
        return (box, DsResources.Realize(host, box));
    }

    [StaFact]
    public void The_box_is_a_raised_bordered_strip_that_clips_its_children()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var root = Assert.IsType<Border>(box.Content);
        Assert.Same(box.FindResource("Brush.SurfaceRaised"), root.Background);
        Assert.Same(box.FindResource("Brush.Border"), root.BorderBrush);
        Assert.Equal(new Thickness(1), root.BorderThickness);
        Assert.Equal(box.FindResource("Radius.Xs"), root.CornerRadius);
        Assert.Equal(24d, root.Height);
        Assert.True(root.ClipToBounds, "kutu overflow:hidden — köşe yarıçapı butonları kesmeli");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_box_orders_clean_then_optimize_then_resolve_with_hairline_separators_between_them()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var strip = Assert.IsType<StackPanel>(Assert.IsType<Border>(box.Content).Child);
        var children = strip.Children.Cast<UIElement>().ToList();
        Assert.Equal(5, children.Count);
        Assert.Same(box.CleanButton, children[0]);
        Assert.Same(box.OptimizeButton, children[2]);
        Assert.Same(box.ResolveButton, children[4]);

        foreach (int i in new[] { 1, 3 })
        {
            var separator = Assert.IsType<Border>(children[i]);
            Assert.Equal(1d, separator.Width);
            Assert.Equal(14d, separator.Height);
            Assert.Same(box.FindResource("Brush.Border"), separator.Background);
        }

        foreach (var button in new[] { box.CleanButton, box.OptimizeButton, box.ResolveButton })
        {
            Assert.Equal(28d, button.Width);
            Assert.Equal(22d, button.Height);
        }
        GC.KeepAlive(window);
    }

    GC.KeepAlive(null);
}
```

> Not: yukarıdaki son satır (`GC.KeepAlive(null);`) YAZILMAZ — sınıf gövdesinin sonunda hiçbir ifade yoktur.
> (Bu uyarı, kopyala-yapıştır sırasında sınıf gövdesine kaçan artık satırı önlemek içindir.)

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: FAIL — derleme hatası: `MaintenanceBox` tipi yok.

- [ ] **Step 3: Write minimal implementation**

`src/BuildOrchestrator.App/Views/MaintenanceBox.xaml`:

```xml
<UserControl x:Class="BuildOrchestrator.App.Views.MaintenanceBox"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!--
    [design v1.7.0 §2.7-2] Bakım grubu (BuildApp.jsx:1930-1959): Clean · Optimize · Resolve cycles.
    Chip ağırlığında TEK kutu — yükseklik 24, surface-raised zemin, 1px border, radius-xs, overflow hidden.
    İçerik (ikon/spinner/tooltip/enable) KOD-TARAFI kurulur (ActionBar deseni).
  -->
  <Border x:Name="PART_Root" Height="24" ClipToBounds="True"
          Background="{DynamicResource Brush.SurfaceRaised}"
          BorderBrush="{DynamicResource Brush.Border}" BorderThickness="1"
          CornerRadius="{DynamicResource Radius.Xs}" VerticalAlignment="Center">
    <StackPanel x:Name="PART_Strip" Orientation="Horizontal">
      <Button x:Name="PART_Clean" />
      <Border x:Name="PART_Sep1" Width="1" Height="14" Background="{DynamicResource Brush.Border}" />
      <Button x:Name="PART_Optimize" />
      <Border x:Name="PART_Sep2" Width="1" Height="14" Background="{DynamicResource Brush.Border}" />
      <Button x:Name="PART_Resolve" />
    </StackPanel>
  </Border>
</UserControl>
```

`src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın bakım kutusu: Clean · Optimize · Resolve cycles. Etiket YOK
/// (bar 1240px minimumda taşımıyor) — anlam tooltip'tedir. Üç düğme de 28×22 ikon butondur; kutunun
/// köşe yarıçapını kesmesi için kök Border ClipToBounds'tur.
/// </summary>
public partial class MaintenanceBox : UserControl
{
    // BuildApp.jsx:1932-1933 literal ölçüleri (token DEĞİL — kutunun kendi değerleri).
    private const double ButtonWidth = 28;
    private const double ButtonHeight = 22;
    private const double IconSize = 12;     // BuildApp.jsx:59-61 <svg width="12" height="12">

    public MaintenanceBox()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Button CleanButton => PART_Clean;
    internal Button OptimizeButton => PART_Optimize;
    internal Button ResolveButton => PART_Resolve;

    private bool _built;

    private void Build()
    {
        if (_built) return;
        _built = true;
        Shape(PART_Clean, "Icon.Eraser");
        Shape(PART_Optimize, "Icon.Gauge");
        Shape(PART_Resolve, "Icon.Unlink");
    }

    private void Shape(Button button, string iconKey)
    {
        if (TryFindResource("Ds.IconButton") is Style s) button.Style = s;
        button.Width = ButtonWidth;
        button.Height = ButtonHeight;
        // Kutu tek parça okunur: düğmelerin kendi köşesi/kenarı YOKTUR, çerçeveyi kutu taşır.
        button.BorderThickness = new Thickness(0);
        button.Content = IconVisual.Make(this, iconKey, "Brush.TextSecondary", IconSize);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: PASS (iki test).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Views/MaintenanceBox.xaml src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs
git commit -m "feat(app): bakim kutusu kabugu (clean/optimize/resolve ikon butonlari)"
```

---

## Task 3: Tooltip metinleri ve Clean/Optimize'ın kalıcı disabled hâli

**Files:**
- Modify: `src/BuildOrchestrator.App/AccessibilityNames.cs`
- Modify: `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`
- Modify: `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs`

**Interfaces:**
- Consumes: Task 2'nin `CleanButton`/`OptimizeButton`/`ResolveButton` yüzeyi.
- Produces: `AccessibilityNames` üzerinde
  - `public const string CleanButton = "Clean";`
  - `public const string OptimizeButton = "Optimize";`
  - `public const string ResolveCyclesButton = "Resolve cycles";`
  - `public const string CleanTooltip`, `OptimizeTooltip` (tasarım metni + `— not available yet`)
  - `public static string ResolveCyclesTooltip(int memberCount)` — Task 4 bunu çağırır.

- [ ] **Step 1: Write the failing test**

`MaintenanceBoxTests.cs` sınıfına ekle:

```csharp
    /// <summary>[karar 2026-08-13] Clean ve Optimize'ın ARKA UCU YOK — düğmeler görünür ama kalıcı olarak
    /// disabled ve tooltip bunu açıkça söyler. Basılıp hiçbir şey olmaması, yokluğu sessizce gizlemekten
    /// daha kötü olurdu; tasarımın tooltip metni korunur, sonuna durum eki gelir.</summary>
    [StaFact]
    public void Clean_and_optimize_are_disabled_and_say_so_in_their_tooltips()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.False(box.CleanButton.IsEnabled);
        Assert.False(box.OptimizeButton.IsEnabled);
        Assert.Equal("Clean — /t:Clean on every solution, then remove bin/, obj/, artifacts/ — not available yet",
                     box.CleanButton.ToolTip);
        Assert.Equal("Optimize — restore packages, prune the cache, rebuild the dependency index — not available yet",
                     box.OptimizeButton.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.7.0 §2.7-2] Resolve'un tooltip'i döngü VARKEN sayı taşır, yokken nedenini söyler.
    /// Sayı kaynağı VM'in üyelik sayacıdır (topoloji olayından gelir).</summary>
    [StaFact]
    public void Resolve_tooltip_explains_the_two_pass_run_when_cycles_exist_and_the_absence_otherwise()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Equal("Resolve cycles — no dependency cycles detected", box.ResolveButton.ToolTip);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1), Node(@"C:\p\c.csproj", "C", 2)],
            [], [[@"C:\p\a.csproj", @"C:\p\b.csproj", @"C:\p\c.csproj"]], []));

        Assert.Equal("Resolve cycles — build the 3 cycle projects in two passes: stale references first, "
                     + "then rebuild until they converge", box.ResolveButton.ToolTip);
        GC.KeepAlive(window);
    }
```

Ve sınıfın başına `Node` yardımcısını ekle (ActionBarTests'teki ile aynı imza — orada `private static`
olduğu için paylaşılamaz; kopya değil, ayrı fixture'ın kendi kurulumudur):

```csharp
    private static ProjectNode Node(string id, string name, int buildOrder) =>
        new(id, name, id, ["Osys"], [], buildOrder, null, null, false, null);
```

`using BuildOrchestrator.Contracts.Ipc;` ve `using BuildOrchestrator.Contracts.Model;` eklenir.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: FAIL — `ToolTip` null (henüz yazılmıyor) ve `IsEnabled` true.

- [ ] **Step 3: Write minimal implementation**

`AccessibilityNames.cs` — mevcut `CyclesButton`/`CyclesButtonTooltip` bloğunun YERİNE (Task 5 eski düğmeyi
kaldıracak; sabitler burada tek seferde yeni adlarına taşınır):

```csharp
    /// <summary>[design v1.7.0 §2.7-2] Bakım kutusunun üç düğmesinin UIA adı. İkon-yalnız düğmelerin görsel
    /// içeriği ekran okuyucuya bir şey söylemez; ad tooltip'ten AYRIDIR ve durumdan bağımsız SABİTTİR.</summary>
    public const string CleanButton = "Clean";
    public const string OptimizeButton = "Optimize";
    public const string ResolveCyclesButton = "Resolve cycles";

    /// <summary>[karar 2026-08-13] Clean/Optimize'ın arka ucu henüz yok — tasarım metni (§2.7-2) korunur,
    /// sonuna durum eki gelir. Ek metni TEK yerde durur: iki tooltip de aynı sabitten türer.</summary>
    private const string NotAvailableSuffix = " — not available yet";

    public const string CleanTooltip =
        CleanButton + " — /t:Clean on every solution, then remove bin/, obj/, artifacts/" + NotAvailableSuffix;

    public const string OptimizeTooltip =
        OptimizeButton + " — restore packages, prune the cache, rebuild the dependency index" + NotAvailableSuffix;

    /// <summary>[design v1.7.0 §2.7-2] Resolve'un tooltip'i: döngü varsa üye sayısıyla ne yapacağını anlatır,
    /// yoksa neden pasif olduğunu söyler. UIA adı (<see cref="ResolveCyclesButton"/>) her iki durumda AYNI kalır —
    /// ekran okuyucu kontrolün İŞLEVİNİ duyar, durumunu değil.</summary>
    public static string ResolveCyclesTooltip(int memberCount) =>
        memberCount > 0
            ? string.Format(CultureInfo.InvariantCulture,
                "{0} — build the {1} cycle projects in two passes: stale references first, then rebuild until they converge",
                ResolveCyclesButton, memberCount)
            : ResolveCyclesButton + " — no dependency cycles detected";
```

`MaintenanceBox.xaml.cs` — `Build()` ve `Shape()` güncellenir, VM aboneliği eklenir:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

// … sınıf gövdesinde:

    private RunViewModel? _vm;

    public MaintenanceBox()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { Build(); Refresh(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tooltip sayısı topolojiden gelir (HasCycles her workspaceTopology'de AÇIKÇA yayılır — boole aynı
        // kalsa da sayı değişmiş olabilir; RunViewModel.Workspace.OnWorkspaceTopology).
        if (e.PropertyName is nameof(RunViewModel.HasCycles) or nameof(RunViewModel.HasWorkspace)) Refresh();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        Shape(PART_Clean, "Icon.Eraser", AccessibilityNames.CleanButton);
        Shape(PART_Optimize, "Icon.Gauge", AccessibilityNames.OptimizeButton);
        Shape(PART_Resolve, "Icon.Unlink", AccessibilityNames.ResolveCyclesButton);

        // [karar 2026-08-13] Arka uç yok → kalıcı disabled. Tooltip'leri sabit oldukları için BİR KEZ yazılır;
        // Refresh yalnız Resolve'unkini (sayıya bağlı) tazeler.
        PART_Clean.IsEnabled = false;
        PART_Optimize.IsEnabled = false;
        PART_Clean.ToolTip = AccessibilityNames.CleanTooltip;
        PART_Optimize.ToolTip = AccessibilityNames.OptimizeTooltip;
    }

    private void Shape(Button button, string iconKey, string uiaName)
    {
        if (TryFindResource("Ds.IconButton") is Style s) button.Style = s;
        button.Width = ButtonWidth;
        button.Height = ButtonHeight;
        button.BorderThickness = new Thickness(0);
        button.Content = IconVisual.Make(this, iconKey, "Brush.TextSecondary", IconSize);
        AutomationProperties.SetName(button, uiaName);
    }

    /// <summary>Resolve'un tooltip'inin TEK yazıcısı.</summary>
    private void Refresh()
    {
        if (!_built) return;
        PART_Resolve.ToolTip = AccessibilityNames.ResolveCyclesTooltip(_vm?.CycleMemberCount ?? 0);
    }
```

> **Disabled kontrolde tooltip:** WPF varsayılanı `IsEnabled=false` olan kontrolde tooltip GÖSTERMEZ.
> `Ds.IconButton` bunu zaten çözmüyorsa iki düğmeye `ToolTipService.SetShowOnDisabled(button, true)` eklenir
> (bu satır `Build()` içinde, tooltip atamasının yanına yazılır). Test `ToolTip` DEĞERİNİ okuduğu için
> yeşil kalır; eksikse kullanıcı tooltip'i hiç göremez — bu yüzden atlanmaz.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: PASS (dört test).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/AccessibilityNames.cs src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs
git commit -m "feat(app): bakim kutusu tooltip metinleri; clean/optimize pasif"
```

---

## Task 4: Resolve düğmesi mevcut döngü motoruna bağlanır

**Files:**
- Modify: `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`
- Modify: `tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs`

**Interfaces:**
- Consumes: `RunViewModel.BuildCyclesCommand` (mevcut; `CanExecute` topoloji + döngü varlığı + mid-run +
  motor sağlığı kapılarını zaten içerir), `RunViewModel.HasWorkspace`, `RunViewModel.CycleMemberCount`.
- Produces: Resolve düğmesinin komut kablajı ve renk kuralı — sonraki fazlar (koşu anlatısı) buna dokunmaz.

**Renk kuralı (§2.7-2):** döngü varken ikon `Brush.StatusCycleText`, yokken düğmenin kendi (miras) rengi.

- [ ] **Step 1: Write the failing test**

```csharp
    /// <summary>[design v1.7.0 §2.7-2 · §3.7] Resolve düğmesi mevcut döngü koşusunu (BuildCyclesCommand)
    /// tetikler — düğme yer değiştirdi, iş değişmedi. Repo kapısı düğmede, geri kalan her koşul komutun
    /// CanExecute'unda: iki yerden yazılan bir enable hâli olmaz (ActionBar deseni).</summary>
    [StaFact]
    public void Resolve_is_wired_to_the_existing_cycle_run_command()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(vm.BuildCyclesCommand, box.ResolveButton.Command);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.7.0 §2.7-2] Döngü varken ikon cycle turuncusuna döner — düğme, listede ve grafta
    /// turuncuyla işaretlenmiş projelerin ta kendisini derler; bağ görsel olarak kurulur. Döngü yokken
    /// düğme nötr kalır (turuncu, var olmayan bir sorunu ima etmemeli).</summary>
    [StaFact]
    public void The_resolve_icon_turns_cycle_orange_only_while_a_cycle_exists()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(box.FindResource("Brush.TextSecondary"), box.ResolveIconBrush);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)],
            [], [[@"C:\p\a.csproj", @"C:\p\b.csproj"]], []));

        Assert.Same(box.FindResource("Brush.StatusCycleText"), box.ResolveIconBrush);
        GC.KeepAlive(window);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: FAIL — derleme hatası (`ResolveIconBrush` yok) ve `Command` null.

- [ ] **Step 3: Write minimal implementation**

`MaintenanceBox.xaml.cs`:

```csharp
    // Resolve'un ikon Path'i — rengi döngü varlığına göre değişen TEK öğe (test yüzeyi de buradan okur).
    private System.Windows.Shapes.Path _resolveIcon = null!;

    /// <summary>[test yüzeyi] Resolve ikonunun o anki fırçası.</summary>
    internal Brush ResolveIconBrush => _resolveIcon.Stroke;
```

`Shape()` çağrılarında Resolve'unki ayrılır (ikon Path'i saklanır) ve `Build()` sonunda komut bağlanır:

```csharp
        // Resolve: komut VM'den; repo kapısı burada, geri kalan koşullar CanExecute'ta.
        PART_Resolve.SetBinding(Button.CommandProperty,
            new System.Windows.Data.Binding(nameof(RunViewModel.BuildCyclesCommand)));
```

`Refresh()` ikon rengini de yazar:

```csharp
    private void Refresh()
    {
        if (!_built) return;
        int members = _vm?.CycleMemberCount ?? 0;
        PART_Resolve.ToolTip = AccessibilityNames.ResolveCyclesTooltip(members);
        PART_Resolve.IsEnabled = _vm?.HasWorkspace ?? false;
        _resolveIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
            members > 0 ? "Brush.StatusCycleText" : "Brush.TextSecondary");
    }
```

> `IconVisual.Make` bir `Viewbox` döndürür; içindeki `Path`'e ulaşmak için `Shape()` bunu döndürecek şekilde
> genişletilir (`private Viewbox Shape(...)` → `((Canvas)viewbox.Child).Children[0] as Path`). Mevcut
> `ActionBar.DepIcon()` aynı deseni elle kurar; burada `IconVisual` üzerinden okunur — kopya çıkmaz.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MaintenanceBoxTests"
```

Expected: PASS (altı test).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs tests/BuildOrchestrator.Tests/App/MaintenanceBoxTests.cs
git commit -m "feat(app): resolve dugmesi mevcut dongu kosusuna baglandi"
```

---

## Task 5: Kutu action bar'a girer, etiketli Cycles düğmesi kalkar

**Files:**
- Modify: `src/BuildOrchestrator.App/Views/ActionBar.xaml:74-85`
- Modify: `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs` (`CyclesButton`, `RefreshCyclesTooltip`,
  `BuildButtons` ve `RefreshEnabled` içindeki Cycles satırları)
- Modify: `src/BuildOrchestrator.App/AccessibilityNames.cs` (eski `CyclesButton*` sabitleri silinir)
- Modify: `tests/BuildOrchestrator.Tests/App/ActionBarTests.cs`
  (`The_left_group_orders_sync_then_cycles_then_a_separator_then_the_six_counter_chips_in_design_order`
  ve `CyclesButton`'a değen diğer testler)

**Interfaces:**
- Consumes: Task 2-4'ün `MaintenanceBox` kontrolü.
- Produces: `ActionBar.MaintenanceBoxControl` test yüzeyi; `ActionBar.CyclesButton` ARTIK YOKTUR.

- [ ] **Step 1: Write the failing test**

`ActionBarTests.cs` — mevcut sıralama testi YENİ kurala göre yeniden yazılır (silinmez):

```csharp
    /// <summary>[A13/T3c · c5] design-v1 BuildApp.jsx:1546-1614 sırası: Sync · ayraç · 6 sayaç chip'i …
    /// <para><b>[DEĞİŞEN KURAL — design v1.7.0 §2.7-2]</b> Sol grup artık Sync'ten sonra <b>bakım kutusunu</b>
    /// taşır (ayraçtan ÖNCE). Eski iddia: "Sync'ten hemen sonra etiketli Cycles düğmesi gelir" —
    /// o düğme kaldırıldı; döngü koşusu kutunun ÜÇÜNCÜ ikonudur (unlink). Gerekçe: Clean/Optimize/Resolve
    /// üçü de derleme öncesi bakım işleridir ve tasarım bunları tek kutuda toplar; ayrıca üç etiketli düğme
    /// barı 1240px minimumda taşırıyordu (§2.7-2).</para></summary>
    [StaFact]
    public void The_left_group_orders_sync_then_the_maintenance_box_then_a_separator_then_the_six_counter_chips()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var leftGroup = Assert.IsType<StackPanel>(bar.SyncButton.Parent);
        var leftChildren = leftGroup.Children.Cast<UIElement>().ToList();
        Assert.Equal(4, leftChildren.Count);
        Assert.Same(bar.SyncButton, leftChildren[0]);
        Assert.Same(bar.MaintenanceBoxControl, leftChildren[1]);
        var leftSeparator = Assert.IsType<Border>(leftChildren[2]);
        Assert.Same(bar.FindResource("Brush.BorderSubtle"), leftSeparator.Background);
        Assert.Same(bar.CounterChipStrip, leftChildren[3]);
        GC.KeepAlive(window);
    }
```

> `CounterChipStrip` yoksa mevcut testte hangi ifade kullanılıyorsa o korunur — bu satır yalnız 4. çocuğun
> chip şeridi olduğunu söyler.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ActionBarTests"
```

Expected: FAIL — derleme hatası: `MaintenanceBoxControl` yok.

- [ ] **Step 3: Write minimal implementation**

`ActionBar.xaml` — sol gruptaki `PART_Cycles` düğmesi ve yorumu SİLİNİR, yerine:

```xml
        <!-- [design v1.7.0 §2.7-2] Bakım kutusu: Clean · Optimize · Resolve cycles. Sync'in KOMŞUSU —
             üçü de derleme ÖNCESİ hazırlık işleridir ve birlikte okunurlar; ayracın öbür yanı sayaçlarındır. -->
        <views:MaintenanceBox x:Name="PART_Maintenance" Margin="8,0,0,0" VerticalAlignment="Center" />
```

`ActionBar.xaml.cs`:
- `internal Button CyclesButton => PART_Cycles;` → `internal MaintenanceBox MaintenanceBoxControl => PART_Maintenance;`
- `RefreshCyclesTooltip()` metodu ve `RefreshAll()`/`OnVmPropertyChanged` içindeki çağrıları SİLİNİR
  (tooltip'in tek yazıcısı artık `MaintenanceBox.Refresh`).
- `BuildButtons()` içindeki `PART_Cycles.Content = …` ve `AutomationProperties.SetName(PART_Cycles, …)` SİLİNİR.
- `RefreshEnabled()` içindeki `PART_Cycles.IsEnabled = hasWs;` SİLİNİR.
- `OnVmPropertyChanged` içindeki `case nameof(RunViewModel.HasCycles):` dalı SİLİNİR.

`AccessibilityNames.cs` — eski `CyclesButton` sabiti ve `CyclesButtonTooltip(int, int)` metodu SİLİNİR
(yerlerini Task 3'ün sabitleri aldı).

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ActionBarTests|FullyQualifiedName~MaintenanceBoxTests|FullyQualifiedName~AccessibilityTests"
```

Expected: PASS. `CyclesButton`'a değen başka testler kırmızıya düşerse (ör. `AccessibilityTests`) aynı
kurala göre yeniden yazılır: UIA adı artık `Resolve cycles` ve düğme kutunun içindedir.

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Views/ActionBar.xaml src/BuildOrchestrator.App/Views/ActionBar.xaml.cs src/BuildOrchestrator.App/AccessibilityNames.cs tests/BuildOrchestrator.Tests/App/
git commit -m "refactor(app): etiketli Cycles dugmesi kalkti, bakim kutusu action bara girdi"
```

---

## Task 6: Build menüsü iki maddeye iner

**Files:**
- Modify: `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs:12-27` (doc), `:80-94` (`ComposeItems`),
  `:158-176` (`Invoke`, `IconKey`)
- Modify: `tests/BuildOrchestrator.Tests/App/ActionBarTests.cs`
  (`Build_menu_never_offers_continue_and_shows_retry_only_when_something_failed`)

**Interfaces:**
- Consumes: `RunViewModel.BuildCommand`, `RunViewModel.RebuildCommand`.
- Produces: `BuildMenu.ComposeItems(bool stopped, int total)` — **imza değişir**, `failed` parametresi düşer.
  Task 7 `RetryFailedCommand`'ı silerken bu metodun artık ona referans vermediğine güvenir.

**Metinler (§2.7-11):** `Build — Only stale projects — F5` · `Rebuild — All {total} projects — cache ignored — Ctrl+F5`.

- [ ] **Step 1: Write the failing test**

Mevcut testi YENİ kurala göre yeniden yaz:

```csharp
    /// <summary>[D6/T40] Build menüsünün maddeleri.
    /// <para><b>[DEĞİŞEN KURAL — design v1.7.0 §2.7-11]</b> Eski iddia: "menü Continue sunmaz, Retry failed'ı
    /// yalnız hata varken sunar". Retry failed de KALDIRILDI: Build zaten stale set'i derler (hatalılar bir
    /// sonraki Build'de hâlâ kirlidir, başarılılar `up to date` atlanır) — ayrı bir yüzey aynı işi ikinci kez
    /// sunuyordu. Menü artık her fazda İKİ maddedir ve açıklamalar tasarım metnidir.</para></summary>
    [StaFact]
    public void Build_menu_offers_exactly_build_and_rebuild_in_every_phase()
    {
        var vm = NewVm();
        var (menu, window) = RealizeMenu(vm);
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.Equal(["build", "rebuild"], menu.Items.Select(i => i.Kind));
        Assert.Equal("Only stale projects", menu.Items[0].Desc);
        Assert.Equal("F5", menu.Items[0].Kbd);
        Assert.Equal("Ctrl+F5", menu.Items[1].Kbd);

        // Hata da olsa, Stop'tan sonra da menü aynı iki maddedir.
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 1, 0, 100));
        Assert.Equal(["build", "rebuild"], menu.Items.Select(i => i.Kind));
        GC.KeepAlive(window);
    }
```

> `ProjectFailedEvent`'in gerçek imzası farklıysa mevcut testlerdeki kullanımı birebir alınır — amaç yalnız
> `Counters.Failed > 0` yapmaktır.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~Build_menu"
```

Expected: FAIL — menü üç madde döndürüyor (`retry` var) ve `Desc` `"Only changed projects"`.

- [ ] **Step 3: Write minimal implementation**

`BuildMenu.xaml.cs`:

```csharp
    /// <summary>[T40] VM durumundan menü modelini kurar.
    /// <para>[B4] <c>continue</c> maddesi kaldırılmıştı; [design v1.7.0 §2.7-11] <c>retry</c> de kaldırıldı —
    /// Build zaten stale set'i (değişen + hatalı + hiç derlenmemiş + hatalıların bağımlıları) derler.
    /// Menü her fazda iki maddedir; <paramref name="stopped"/> yalnız Build'in açıklamasını ayırmak için durur.</para></summary>
    internal static IReadOnlyList<BuildMenuItem> ComposeItems(bool stopped, int total)
    {
        return
        [
            new("build", "Build",
                stopped ? "Start over — only stale projects" : "Only stale projects",
                ShortcutCatalog.Get(ShortcutId.Build).Gestures[0]),
            new("rebuild", "Rebuild", Inv($"All {total} projects — cache ignored"),
                ShortcutCatalog.Get(ShortcutId.Rebuild).Gestures[0]),
        ];
    }
```

`RefreshRows()` içinden `int failed = …` satırı ve `ComposeItems(stopped, total, failed)` çağrısındaki üçüncü
argüman kaldırılır; `OnVmPropertyChanged`'de `Counters` dalı KALIR (`total` hâlâ oradan gelir).
`Invoke()` içindeki `"retry" => _vm?.RetryFailedCommand,` satırı ve `IconKey()` içindeki `"retry" =>
"Icon.Redo",` satırı silinir.

> `Icon.Redo` başka bir tüketicisi kalmazsa `Icons.xaml`'de KALIR (sözlük tasarımın I.* tablosunun tam
> karşılığıdır; `IconGeometryTests.RequiredKeys` onu pinler).

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ActionBarTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs tests/BuildOrchestrator.Tests/App/ActionBarTests.cs
git commit -m "feat(app): build menusu iki maddeye indi (retry failed kaldirildi)"
```

---

## Task 7: `RunMode.RetryFailed` koddan kaldırılır

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:582-583`, `:640` (`RetryFailedCommand`)
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs:40` (+ `:43-48` doc)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:257`, `:701`, `:870`
- Modify: `tests/BuildOrchestrator.Tests/Scheduling/RetryFailedTests.cs` (dosya bütünüyle yeniden yazılır)
- Modify: `tests/BuildOrchestrator.Tests/App/RunViewModelStateTests.cs:483`,
  `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs:76,89`,
  `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorTests.cs:397,715-738`

**Interfaces:**
- Consumes: Task 6'nın menüsü (artık `RetryFailedCommand`'a referans vermiyor).
- Produces: `RunMode` yalnız `{ Rebuild, Build, Continue, Cycles }` — Task 8 `Continue`'yu da düşürür.

**Kural (yerine geçen davranış):** hata sonrası **Build**, hatalı projeleri ve bağımlılarını yeniden derler —
çünkü başarısız proje imzasını persist ETMEZ, bir sonraki incremental kararda hâlâ kirlidir.

- [ ] **Step 1: Write the failing test**

`tests/BuildOrchestrator.Tests/Scheduling/RetryFailedTests.cs` dosyasını `BuildAfterFailureTests.cs` adıyla
yeniden yaz. İlk test — eski `retry_set_is_failed_projects_plus_their_transitive_dependents…` testinin
YENİ kural karşılığı:

```csharp
/// <summary>
/// [design v1.7.0 §2.7-11 · §3.1] Hata sonrası davranış.
/// <para><b>DEĞİŞEN KURAL:</b> eski iddia "ayrı bir Retry failed modu, hatalıları + tüm transitif
/// bağımlılarını yeniden derler" idi (<c>RetryFailedTests</c>). O mod kaldırıldı: hatalı proje imzasını
/// persist etmediği için bir sonraki <b>Build</b>'de hâlâ kirlidir ve bağımlıları da cascade ile gelir —
/// yani Build zaten aynı kümeyi derliyordu. İki yüzey aynı işi yapıyordu; biri kaldı.</para>
/// </summary>
public class BuildAfterFailureTests
{
    [Fact]
    public void A_build_after_a_failed_run_rebuilds_the_failed_project_and_its_transitive_dependents()
    {
        // Kurulum: A ← B ← C zinciri; A önceki koşuda FAILED, B ve C succeeded.
        // Beklenen: Build'in willBuild kümesi = { A, B, C } (A kirli, cascade B ve C'yi getirir).
        // Somut kurulum mevcut RetryFailedTests'teki plan/fixture kurucularından BİREBİR alınır —
        // yalnız çağrılan yüzey RunMode.RetryFailed yerine RunMode.Build olur.
    }
}
```

> **Uygulayıcıya not:** yukarıdaki gövde, eski dosyadaki dört testin fixture kurulumundan (aynı `BuildPlan`
> kurucusu, aynı düğümler) türetilir; her eski test için YENİ kuralın karşılığı bir test yazılır. Eski
> dosyadaki `Retry_requeues_the_whole_scc_and_its_downstream_when_any_member_is_affected` testinin
> karşılığı, **döngü üyeleri standart Build'de zaten pre-skip edildiği için** (`SkipReasons.InDependencyCycle`)
> "SCC üyesi Build'de yeniden kuyruğa GİRMEZ" iddiasına dönüşür.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BuildAfterFailureTests"
```

Expected: FAIL (testler henüz gövdesiz/kırmızı).

- [ ] **Step 3: Write minimal implementation**

1. `IpcMessages.cs`: `public enum RunMode { Rebuild, Build, Continue, Cycles }`; doc'taki RetryFailed cümlesi
   ve `DependentMode` doc'undaki "RetryFailed her zaman…" cümlesi silinir.
2. `RunViewModel.cs`: `RunMode.RetryFailed => "retry",` eşlemesi ve `RetryFailedCommand` (+`BeginRunAsync(
   RunMode.RetryFailed, …)` gövdesi) silinir.
3. `RunCoordinator.cs`: `IsRetryableForLocked` kapısı (`:257`) ve tanımı silinir; `:701` ve `:870`
   koşullarındaki `or RunMode.RetryFailed` / `RunMode.RetryFailed` terimleri düşer (o satırlar yalnız
   `Continue` için kalır — Task 8 onları tümüyle kaldıracak).
4. Etkilenen testler yeni kurala göre düzeltilir (yukarıdaki dosya listesi).

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Expected: tam süit yeşil.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "refactor: RunMode.RetryFailed kaldirildi, hata sonrasi Build kapsiyor"
```

---

## Task 8: `RunMode.Continue` koddan kaldırılır

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs:40,46-48`
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:582`, `:712` (kontrat notu)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs:109`
- Modify: `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (`Continue(...)` üreteci)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:255`, `:701`, `:721`, `:870`
  (+ `IsResumableForLocked` ve resume snapshot dalı)
- Modify: `tests/BuildOrchestrator.Tests/Scheduling/ContinueRunTests.cs` (yeniden yazılır),
  `tests/BuildOrchestrator.Tests/App/EventStreamTests.cs:215`,
  `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs:208,438,1297`,
  `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs:58`,
  `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorTests.cs` (Continue geçen tüm testler)

**Interfaces:**
- Consumes: Task 7'nin sadeleşmiş `RunMode`'u.
- Produces: `public enum RunMode { Rebuild, Build, Cycles }` — bundan sonrasında koşu modu üçe iner.

**Kural (yerine geçen davranış):** Stop'tan sonra **Build**, öldürülen ve hiç başlamamış projeleri derler;
tamamlananlar `up to date` ile atlanır. Elapsed yeni koşuda **sıfırlanır** (§3.1 — `ElapsedMsAtStart`
taşınmaz).

- [ ] **Step 1: Write the failing test**

`ContinueRunTests.cs` → `BuildAfterStopTests.cs`:

```csharp
/// <summary>
/// [design v1.7.0 §3.1] Stop sonrası davranış.
/// <para><b>DEĞİŞEN KURAL:</b> eski iddia "Continue modu tamamlanmış projeleri yeniden dispatch etmez ve
/// kalan build-order'ı sürdürür" idi (<c>ContinueRunTests</c>). Ayrı bir sürdürme modu kaldırıldı: Stop'tan
/// sonra <b>Build</b> aynı sonucu verir — tamamlananlar imzalarını persist ettiği için `up to date` atlanır,
/// öldürülen ve hiç başlamamış olanlar kirli kalır ve derlenir. Fark yalnız elapsed'tedir: yeni koşu
/// sıfırdan sayar (§3.1) ve bu bilinçlidir — yeni bir koşudur.</para>
/// </summary>
public class BuildAfterStopTests
{
    [Fact]
    public void A_build_after_stop_skips_completed_projects_and_dispatches_the_remaining_build_order()
    {
        // Kurulum: eski ContinueRunTests'in plan/state fixture'ı birebir; yalnız çağrılan mod Build.
        // Beklenen: tamamlananlar SkipReasons.UpToDate ile atlanır; kalanlar build-order sırasında dispatch edilir.
    }
}
```

> Eski dosyadaki iki testin (`continue_does_not_redispatch_completed_projects`,
> `continue_dispatch_order_matches_remaining_build_order`) her biri için yukarıdaki desende bir karşılık
> yazılır; fixture kurulumu eski dosyadan birebir taşınır.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BuildAfterStopTests"
```

Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

1. `IpcMessages.cs`: `public enum RunMode { Rebuild, Build, Cycles }`; `StartRunCommand`/`RunStartedEvent`
   doc'larındaki Continue cümleleri silinir.
2. `RunViewModel.cs`: `RunMode.Continue => "continue",` eşlemesi ve `:712`'deki "kontratta KALIR" notu silinir.
3. `RunViewModel.Stream.cs:109`: `RunMode.Continue => StreamText.Continue(…)` dalı silinir;
   `StreamText.Continue(int, int)` üreteci ve varsa onu pinleyen test kaldırılır
   (`EventStreamTests.Continue_line_uses_remaining_will_build_count` → YENİ kural: Stop sonrası Build,
   `StreamText`'in normal Build satırını basar; test o iddiaya çevrilir).
4. `RunCoordinator.cs`: `IsResumableForLocked` kapısı ve tanımı, `:721`'deki `effectiveSnapshot` resume dalı
   (artık her zaman taze snapshot) ve `:701`/`:870`'teki mod koşulları kaldırılır.
5. Etkilenen tüm testler yeni kurala göre düzeltilir.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Expected: tam süit yeşil (token/motion/D8 guard'ları dahil).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "refactor: RunMode.Continue kaldirildi, Stop sonrasi Build surduruyor"
```

---

## Task 9: Dokümanlar güncellenir

**Files:**
- Modify: `ARCHITECTURE.md` (action bar / koşu modları / döngü yüzeyi bölümleri)
- Modify: `README.md` (kullanım + kısayol bölümleri)

**Interfaces:**
- Consumes: Task 1-8'in tamamı.
- Produces: yok (son task).

**Kural:** anlatı üslubu korunur — "şu oturumda şunu ekledik" YAZILMAZ; değişen davranış ilgili bölümde
YERİNDE yeniden yazılır. Bayatlayacak rakam (test sayısı, sha) gömülmez.

- [ ] **Step 1: Doğrulanacak iddiaları çıkar**

```powershell
Select-String -Path ARCHITECTURE.md,README.md -Pattern "Continue|Retry|Cycles|cycle" | Select-Object LineNumber,Path,Line
```

Çıkan her satır için: kod artık ne diyor? Yanlışsa yerinde düzeltilir, doğruysa DOKUNULMAZ.

- [ ] **Step 2: ARCHITECTURE.md'yi güncelle**

En az şu üç yer: (a) action bar bileşen listesi — Sync · bakım kutusu (Clean/Optimize pasif, Resolve cycles) ·
sayaç chip'leri · … · Build split-button; (b) koşu modları — `RunMode` üç değerlidir, Stop sonrası devam ve
hata sonrası retry Build'in doğal sonucudur; (c) §14.3'teki döngü işareti anlatısı — cycle turuncudur, kırmızı
badge yoktur (v1.5.0 sürüm notunun işaret ettiği düzeltme).

- [ ] **Step 3: README.md'yi güncelle**

Kullanım bölümünde Build/Rebuild ve döngü çözme akışı; kısayol tablosunda Retry/Continue kalıntısı varsa
kaldırılır.

- [ ] **Step 4: Tam süit + doküman guard'ları**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Expected: yeşil (kaynak guard'ları doküman metnini de tarar).

- [ ] **Step 5: Commit**

```powershell
git add ARCHITECTURE.md README.md
git commit -m "docs: action bar bakim kutusu ve uc degerli RunMode dokumanlara islendi"
```

---

## Faz 1 kapanışı

- [ ] `dotnet build BuildOrchestrator.slnx` temiz.
- [ ] `dotnet test … --filter "Category!=Acceptance"` tam yeşil.
- [ ] Uygulama elle açılıp bakım kutusu görülür: Clean/Optimize sönük ve tooltip'leri okunur, Resolve
      döngü yokken pasif, döngü varken turuncu ve çalışıyor.
- [ ] `main`'e merge + push; merge doğrulandıktan sonra çalışma branch'i local ve remote'tan silinir.

## Self-review notları

- **Spec kapsamı:** §2.7-2 (kutu, ölçüler, tooltip'ler, ikonlar, renk) → Task 1-4; §2.7-11 (menü iki madde) →
  Task 6; §3.1 + v1.7.0 "Kaldırılanlar" (Continue/Retry) → Task 7-8; §8 (onay dialogu/toast/etiket YOK) →
  Task 2'de kutuda etiket yok, hiçbir task toast eklemiyor. Bu fazın dışında bırakılanlar faz haritasındadır.
- **Tip tutarlılığı:** `ComposeItems` imzası Task 6'da değişiyor ve Task 7'den ÖNCE geliyor — sırayla
  uygulandığında `RetryFailedCommand`'a referans kalmaz.
- **Açık nokta:** `Ds.IconButton`'ın disabled tooltip davranışı (Task 3'teki not) uygulama sırasında
  doğrulanır; gerekiyorsa `ToolTipService.ShowOnDisabled` eklenir.
