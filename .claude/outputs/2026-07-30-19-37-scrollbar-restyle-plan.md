# DS Scrollbar Restyle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows'un kaba açık-tema scrollbar'larını, design-v1 prototipinin `.bo-scroll` sözleşmesine (10px ray, şeffaf track, neutral-700 içerlek hap thumb) birebir uyan DS scrollbar'ıyla **uygulama genelinde** değiştirmek.

**Architecture:** Tek bir implicit `Style TargetType="ScrollBar"` Resources/Controls.xaml'a (uygulamanın TEK DS kontrol kütüphanesi) eklenir — App.xaml'a dokunulmaz, panel XAML'lerine dokunulmaz; ScrollBar bir `Control` türevi olduğu için implicit stil tüm şablon sınırlarını geçer (StickyLayerList ScrollViewer'ı, EventStream ScrollViewer'ı, AvalonEdit console editor'ünün iç ScrollViewer'ı, popover/dialog ListBox'ları). İki ek nokta: (1) ScrollViewer default şablonunun köşe karesi (`Corner`) `SystemColors.ControlBrushKey`'i DynamicResource ile okur → app kapsamında şeffaf override; (2) console'un AvalonEdit editor'ü bar'ları default `Visible` gösterir → `Auto`'ya çevrilir (prototip `overflowY:auto`).

**Tech Stack:** WPF (net10.0-windows), xUnit + StaFact (headless realize testleri, DsResources altyapısı), AvalonEdit (console).

---

## Global Constraints

- **Ham renk literali YALNIZ `Resources/Tokens.xaml`'a yazılır** — `NoHardcodedColorTests` tüm `*.xaml` + `*.cs`'i tarar. İsimli renklerden yalnız `Transparent` serbesttir.
- **Controls.xaml kaynak biçimi** (dosya başlığı, satır 55-58): token/ikon anahtarları **HER ZAMAN `{DynamicResource}`**; `{StaticResource}` yalnız `BasedOn`'da ve **yalnız aynı dosyada daha ÖNCE tanımlı** anahtarlar için (sözlük testlerde XamlReader ile TEK BAŞINA yüklenir).
- **Controls.xaml ölçü istisnası** (satır 14-15): kaynakta da token olmayan, bileşenin KENDİ ölçüleri (scrollbar'ın 10/3/2 değerleri gibi) kaynak satır numarası yorumuyla doğrudan yazılır.
- **A13.2:** animasyon hedefi asla paylaşılan token fırçası değildir; 120ms durum geçişleri `controls:DsTransition.Animated*` ile ilan edilir (şablonda Storyboard YASAK — Step 1 kararı).
- **Amber yalnız STATÜ taşır** — scrollbar nötr gri kalır, hover dahil hiçbir durumda amber kullanılmaz.
- **Test kalıbı:** WPF testleri `[StaFact]`/`[StaTheory]` + `[Collection("Console UI (serial)")]`; kontroller `DsResources.NewHost()` + `DsResources.Realize()` ile GERÇEKTEN kurulur; pencere `GC.KeepAlive(window)` ile canlı tutulur.
- **Komutlar:** `dotnet build BuildOrchestrator.slnx` · `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj`
- **Git:** `main`'den çalışma branch'i aç, task başına commit, sonda `main`'e merge + push, merge doğrulanınca branch'i local+remote sil, dizini `main`'de bırak. Commit mesajları ASCII Türkçe, şu satırla biter: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

### Eşzamanlılık notu (plan yazıldığı andaki durum)

Plan, `a13-visual-debt-automation` dalgası uçarken (commit `256c490` + uncommitted değişiklikler) yazıldı. **Bu planın dokunduğu 5 dosyanın HİÇBİRİ a13'ün değiştirdiği dosya kümesinde değil:** `Resources/Tokens.xaml`, `Resources/Controls.xaml`, `Console/ConsoleView.xaml`, `tests/.../DesignTokenScaleTests.cs` (+ yeni test dosyası). Çakışma riski düşük; yine de tüm ekleme noktaları satır numarasıyla değil **benzersiz anchor string** ile verildi. Uygulama anında anchor bulunamazsa önce dosyanın güncel halini oku, aynı semantik noktayı bul. Branch: güncel `main`'den `scrollbar-restyle` aç (a13 merge olmuş olsun ya da olmasın; kullanıcı açıkça a13 üstüne isterse oradan aç).

---

## Tasarım Otoritesi ve Kararlar

Kaynak: `.claude/outputs/2026-07-15-19-00-design-v1/prototype/app/BuildApp.jsx` satır 35-38 (**tasarımın scrollbar sözleşmesi**):

```css
.bo-scroll { scrollbar-width: thin; scrollbar-color: var(--neutral-700) transparent; }
.bo-scroll::-webkit-scrollbar { width: 10px; height: 10px; }
.bo-scroll::-webkit-scrollbar-track { background: transparent; }
.bo-scroll::-webkit-scrollbar-thumb { background: var(--neutral-700); border: 3px solid transparent; background-clip: padding-box; border-radius: 5px; }
```

Prototipte `.bo-scroll` tüketicileri: proje listesi (`:476`), console kutusu (`:615`, `overflowY:'auto'`), event stream (`:704`), branch popover listesi (`:839`) → **uygulama geneli implicit stil doğru kapsamdır.** design-v1 `README.md:14` scrollbar'ı açıkça "en yakın native karşılıkla çözülür" istisnasına devreder.

| Karar | Değer | Kaynak / gerekçe |
|---|---|---|
| Ray kalınlığı | 10px (dikey Width, yatay Height) | BuildApp.jsx:36 |
| Track | Şeffaf; boş alan tıklanınca sayfa atlar (şeffaf RepeatButton'lar) | BuildApp.jsx:37 + native davranış parity |
| Thumb | `Border`: `Margin=3` (CSS'in 3px şeffaf kenarı) + `CornerRadius=2` (dış 5 − kenar 3, `background-clip:padding-box` eşlemesi) + zemin `Brush.Neutral700` → 4px'lik hap | BuildApp.jsx:38 |
| Ok butonları | YOK — webkit scrollbar'ında buton çizilmez; wheel/klavye ScrollViewer'da, davranış kaybı yok | BuildApp.jsx:36-38 |
| **Hover/drag** (türetilmiş TEK karar) | Thumb `Brush.Neutral600` (rampada bir adım açık). Prototip tek renklidir; README:14 "en yakın native karşılık" native etkileşim geri bildirimini meşrulaştırır — `Brush.SurfaceHover`'ın "bir üst adım" desenine paralel. Amber DEĞİL. Geçiş DS kuralıyla: `DsTransition.AnimatedBackground` | README:14 + colors.css:6 |
| Köşe karesi (Corner) | `SystemColors.ControlBrushKey` app kapsamında `Transparent` override. `DsSplitter` ETKİLENMEZ (ctor'da explicit `Background=Transparent`, DsSplitter.cs:50); uygulamada başka native ControlBrush tüketicisi yok | Aero2 ScrollViewer şablonu |
| Console bar görünürlüğü | AvalonEdit `TextEditor`'a `Vertical/HorizontalScrollBarVisibility="Auto"` (default'u Visible) | BuildApp.jsx:616 `overflowY:'auto'` |
| Devre dışı bar | Track `Collapsed` (boş koyu hap kalıntısı görünmesin) | restraint |
| Graf paneli | ScrollViewer YOK — dokunulmaz; ileride eklenirse implicit stil kendiliğinden uygulanır | mevcut kod |
| Yeni token'lar | `Brush.Neutral700 #2a2a30`, `Brush.Neutral600 #3a3a42` — colors.css:6'dan birebir; `Brush.Neutral200` emsali zaten var | colors.css:6 |

**Dokunulmayanlar:** `FollowScrollController`/`ScrollAnimator`/`BottomAnchorBehavior` (offset aritmetiği, bar görselinden bağımsız), StickyLayerList overlay genişliği (`ViewportWidth` binding'i dar bar'a kendiliğinden uyar), `Ds.Input` TextBox içi ScrollViewer (bar'ları `Hidden`), App.xaml merge zinciri.

---

## Dosya Haritası

| Dosya | İş |
|---|---|
| Modify: `src/BuildOrchestrator.App/Resources/Tokens.xaml` | Nötr rampa bölümüne `Brush.Neutral600` + `Brush.Neutral700` |
| Modify: `src/BuildOrchestrator.App/Resources/Controls.xaml` | Yeni `SCROLLBAR` bölümü: Corner override + PageButton/Thumb stilleri + 2 şablon + implicit ScrollBar stili |
| Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml` | TextEditor'a 2 attribute (`Auto` görünürlükler) |
| Modify: `tests/BuildOrchestrator.Tests/App/DesignTokenScaleTests.cs` | Mevcut nötr-rampa testine 2 assert |
| Create: `tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs` | 7 test (Task 2'de 5 + Task 3'te 2) |

---

### Task 0: Branch aç

- [ ] **Step 0.1:**
```bash
git checkout main && git pull && git checkout -b scrollbar-restyle
```
Suite'in başlangıçta yeşil olduğunu doğrula: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj` → Expected: PASS.

---

### Task 1: Nötr rampa token'ları (`Brush.Neutral600/700`)

**Files:**
- Modify: `tests/BuildOrchestrator.Tests/App/DesignTokenScaleTests.cs` (metot `Neutral_ramp_endpoints_match_colors_css`)
- Modify: `src/BuildOrchestrator.App/Resources/Tokens.xaml`

**Interfaces:**
- Produces: `Brush.Neutral700` (#2a2a30) ve `Brush.Neutral600` (#3a3a42) resource anahtarları — Task 2'nin Thumb stili bunları `DynamicResource` ile tüketir.

- [ ] **Step 1.1: Failing test — mevcut nötr-rampa testine 2 assert ekle**

`DesignTokenScaleTests.cs` içinde şu bloğu bul (anchor):

```csharp
        Assert.Equal(Hex("#cdcdd2"), ((SolidColorBrush)t["Brush.Neutral200"]).Color);
```

Hemen ALTINA ekle:

```csharp
        // [SCROLLBAR] colors.css:6 — `.bo-scroll` thumb rampası (BuildApp.jsx:35-38).
        Assert.Equal(Hex("#2a2a30"), ((SolidColorBrush)t["Brush.Neutral700"]).Color);
        Assert.Equal(Hex("#3a3a42"), ((SolidColorBrush)t["Brush.Neutral600"]).Color);
```

- [ ] **Step 1.2: Testin kırmızı olduğunu gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~DesignTokenScaleTests"
```
Expected: FAIL — `KeyNotFoundException` / anahtar `Brush.Neutral700` yok.

- [ ] **Step 1.3: Token'ları ekle**

`Tokens.xaml` içinde şu satırı bul (anchor):

```xml
    <SolidColorBrush x:Key="Brush.Neutral200" Color="#cdcdd2" />
```

Hemen ALTINA ekle:

```xml
    <!-- [SCROLLBAR] colors.css:6 neutral-600/700 — `.bo-scroll` thumb'ının rampası (BuildApp.jsx:35-38):
         thumb neutral-700; hover/drag rampada BİR adım açık (neutral-600). DEĞERLER colors.css'ten birebir;
         türetilen yalnız hover KULLANIMIDIR (README:14 "en yakın native karşılık" — native scrollbar'ın
         etkileşim geri bildirimi; Brush.SurfaceHover'ın "bir üst adım" desenine paralel). -->
    <SolidColorBrush x:Key="Brush.Neutral600" Color="#3a3a42" />
    <SolidColorBrush x:Key="Brush.Neutral700" Color="#2a2a30" />
```

- [ ] **Step 1.4: Yeşili gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~DesignTokenScaleTests"
```
Expected: PASS (NoHardcodedColorTests etkilenmez — Tokens.xaml izinli dosyadır).

- [ ] **Step 1.5: Commit**

```bash
git add src/BuildOrchestrator.App/Resources/Tokens.xaml tests/BuildOrchestrator.Tests/App/DesignTokenScaleTests.cs
git commit -m "feat(scrollbar): neutral-600/700 rampa token'lari (colors.css:6 birebir)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: DS ScrollBar — Controls.xaml SCROLLBAR bölümü

**Files:**
- Create: `tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs`
- Modify: `src/BuildOrchestrator.App/Resources/Controls.xaml`

**Interfaces:**
- Consumes: `Brush.Neutral700`, `Brush.Neutral600` (Task 1), `DsResources.NewHost/Realize/TokenColor/ColorOf/Descendants` (mevcut test altyapısı), `controls:DsTransition.AnimatedBackground` (mevcut, `Controls/DsTransition.cs`).
- Produces: implicit `Style TargetType="ScrollBar"` + anahtarlar `Ds.ScrollBar.PageButton`, `Ds.ScrollBar.Thumb`, `Ds.ScrollBar.Vertical.Template`, `Ds.ScrollBar.Horizontal.Template` ve `SystemColors.ControlBrushKey` override'ı (Task 3'ün corner testi bunu doğrular).

- [ ] **Step 2.1: Failing testler — yeni dosya**

`tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs` oluştur, İÇERİĞİN TAMAMI:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [SCROLLBAR] Resources/Controls.xaml SCROLLBAR bölümü — design-v1 `.bo-scroll` sözleşmesini
/// (BuildApp.jsx:35-38) pinler: 10px ray, şeffaf track, ok butonu YOK, thumb = Brush.Neutral700 +
/// 3px içerlek + CornerRadius 2 hap. Implicit stil olduğu için ŞABLON SINIRLARINI geçmesi ayrıca
/// kanıtlanır (ScrollViewer ve AvalonEdit üzerinden) — bir Style'ın varlığını okumak, gerçek
/// ScrollViewer'ların onu giydiğini kanıtlamaz. Kontroller GERÇEKTEN kurulur (DsControlTemplateTests deseni).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ScrollBarStyleTests
{
    private static ScrollBar NewVerticalBar() => new()
    {
        Orientation = Orientation.Vertical,
        Height = 150,
        Minimum = 0,
        Maximum = 100,
        ViewportSize = 20,
        Value = 10,
    };

    [StaFact]
    public void Vertical_rail_is_10px_and_the_thumb_is_a_neutral700_pill_inset_by_3px()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // BuildApp.jsx:36 — ::-webkit-scrollbar { width: 10px }.
        Assert.Equal(10.0, bar.ActualWidth);

        // BuildApp.jsx:38 — thumb: neutral-700 zemin, 3px şeffaf kenar (Margin), dış 5 − kenar 3 = 2 radius.
        var thumb = DsResources.Descendants(bar).OfType<Thumb>().Single();
        var pill = DsResources.Descendants(thumb).OfType<Border>().Single();
        Assert.Equal(new Thickness(3), pill.Margin);
        Assert.Equal(new CornerRadius(2), pill.CornerRadius);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), DsResources.ColorOf(pill.Background));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Horizontal_rail_is_10px()
    {
        var host = DsResources.NewHost();
        var bar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Width = 200,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 20,
        };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(10.0, bar.ActualHeight); // BuildApp.jsx:36 — height: 10px
        Assert.Single(DsResources.Descendants(bar).OfType<Thumb>());
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_rail_has_no_arrow_buttons_only_two_transparent_page_areas()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // webkit scrollbar'ında buton çizilmez (BuildApp.jsx:36-38 yalnız track+thumb tanımlar): ok glyph'i
        // (Path) HİÇ yok; ray'ın boş alanı = 2 şeffaf sayfa-atlama RepeatButton'ı (davranış korunur).
        Assert.Empty(DsResources.Descendants(bar).OfType<Path>());
        var pageAreas = DsResources.Descendants(bar).OfType<RepeatButton>().ToList();
        Assert.Equal(2, pageAreas.Count);
        Assert.All(pageAreas, b => Assert.Equal(Colors.Transparent, DsResources.ColorOf(
            DsResources.Descendants(b).OfType<Border>().Single().Background)));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_scrollviewer_gets_the_ds_bar_through_its_default_template()
    {
        // Implicit stilin ŞABLON SINIRINI geçtiğinin kanıtı: ScrollBar'ı biz değil, ScrollViewer'ın
        // default şablonu kurar (üretimdeki StickyLayerList/EventStream yolu budur).
        var host = DsResources.NewHost();
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = 1000 },
        };
        var window = DsResources.Realize(host, viewer);

        var bar = DsResources.Descendants(viewer).OfType<ScrollBar>()
            .Single(b => b.Orientation == Orientation.Vertical);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(10.0, bar.ActualWidth);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_disabled_bar_collapses_its_track()
    {
        // Kaydıracak şey yokken (IsEnabled=false) boş koyu hap kalıntısı görünmez — restraint.
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        var track = DsResources.Descendants(bar).OfType<Track>().Single();
        Assert.Equal(Visibility.Visible, track.Visibility);

        bar.IsEnabled = false;
        bar.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, track.Visibility);
        GC.KeepAlive(window);
    }
}
```

- [ ] **Step 2.2: Kırmızıyı gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ScrollBarStyleTests"
```
Expected: 5 test FAIL — ilk assert'lerde `Expected: 10  Actual: 17` (sistem teması genişliği) ve/veya default şablonda `Path` ok glyph'leri bulunması.

- [ ] **Step 2.3: SCROLLBAR bölümünü Controls.xaml'a ekle**

`Controls.xaml` içinde şu anchor satırını bul:

```xml
  <!-- ========================= ÖZEL KONTROLLERİN VARSAYILAN ŞABLONLARI ========================= -->
```

Bu satırın hemen ÜSTÜNE, aşağıdaki bloğun TAMAMINI ekle:

```xml
  <!-- ====================================== SCROLLBAR ====================================== -->
  <!--
    BuildApp.jsx:35-38 `.bo-scroll` — tasarımın scrollbar sözleşmesi: 10px ray, track TRANSPARENT, thumb
    neutral-700 + 3px şeffaf kenar (background-clip: padding-box) + dış radius 5. WPF eşlemesi: ray 10px →
    ScrollBar Width/Height=10; thumb'ın GÖRÜNEN hap'ı = Border Margin=3 (3px kenar) + CornerRadius=2
    (dış 5 − kenar 3 = padding-box yarıçapı) + Brush.Neutral700 zemin → 4px'lik hap.
    Tüketiciler (prototip): proje listesi :476 · console :615 · event stream :704 · branch popover :839 —
    yani UYGULAMA GENELİ implicit stil doğru kapsamdır; README:14 scrollbar'ı "en yakın native karşılık"
    istisnasına açıkça devreder.
    Ok butonları YOK: webkit scrollbar'ında buton çizilmez (:36-38 yalnız track+thumb tanımlar) — ray'ın
    boş alanı sayfa-atlama davranışını korur (şeffaf RepeatButton'lar). Hover/drag: prototip tek renklidir;
    native etkileşim geri bildirimi README:14 gereği korunur — rampada BİR adım açık (Brush.Neutral600),
    Brush.SurfaceHover'ın "bir üst adım" desenine paralel; geçiş A13.2 gereği DsTransition ile. Amber DEĞİL:
    renk yalnız STATÜ taşır.
  -->

  <!-- ScrollViewer default şablonunun köşe karesi (Corner): SystemColors.ControlBrushKey'i DynamicResource
       ile okur (açık-tema grisi #f0f0f0) — iki bar'ın kesiştiği köşede koyu kabuğun ortasında parlardı.
       App kapsamında şeffaflaştırılır. DsSplitter ETKİLENMEZ (ctor'da explicit Background=Transparent,
       DsSplitter.cs:50); uygulamada başka native ControlBrush tüketicisi yoktur. -->
  <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="Transparent" />

  <!-- Ray'ın boş alanı: görünmez ama hit-test alır (sayfa atlama). Background=Transparent (null DEĞİL) bu
       yüzden zorunludur. -->
  <Style x:Key="Ds.ScrollBar.PageButton" TargetType="RepeatButton">
    <Setter Property="OverridesDefaultStyle" Value="True" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Background="Transparent" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Thumb: hover/drag geçişi A13.2 kuralıyla (DsTransition.AnimatedBackground → şablon TemplateBinding
       ile okur; ListBoxItem'daki desenin aynısı). -->
  <Style x:Key="Ds.ScrollBar.Thumb" TargetType="Thumb">
    <Setter Property="OverridesDefaultStyle" Value="True" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="controls:DsTransition.AnimatedBackground" Value="{DynamicResource Brush.Neutral700}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <!-- BuildApp.jsx:38 — Margin 3 = 3px şeffaf kenar; CornerRadius 2 = dış 5 − kenar 3. -->
          <Border Background="{TemplateBinding Background}" Margin="3" CornerRadius="2"
                  SnapsToDevicePixels="True" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="controls:DsTransition.AnimatedBackground" Value="{DynamicResource Brush.Neutral600}" />
      </Trigger>
      <Trigger Property="IsDragging" Value="True">
        <Setter Property="controls:DsTransition.AnimatedBackground" Value="{DynamicResource Brush.Neutral600}" />
      </Trigger>
    </Style.Triggers>
  </Style>

  <ControlTemplate x:Key="Ds.ScrollBar.Vertical.Template" TargetType="ScrollBar">
    <Grid Background="Transparent">
      <Track x:Name="PART_Track" IsDirectionReversed="True">
        <Track.DecreaseRepeatButton>
          <RepeatButton Style="{StaticResource Ds.ScrollBar.PageButton}" Command="ScrollBar.PageUpCommand" />
        </Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton>
          <RepeatButton Style="{StaticResource Ds.ScrollBar.PageButton}" Command="ScrollBar.PageDownCommand" />
        </Track.IncreaseRepeatButton>
        <Track.Thumb>
          <Thumb Style="{StaticResource Ds.ScrollBar.Thumb}" />
        </Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <!-- Kaydıracak şey yokken boş koyu hap kalıntısı görünmesin (restraint). -->
      <Trigger Property="IsEnabled" Value="False">
        <Setter TargetName="PART_Track" Property="Visibility" Value="Collapsed" />
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <ControlTemplate x:Key="Ds.ScrollBar.Horizontal.Template" TargetType="ScrollBar">
    <Grid Background="Transparent">
      <Track x:Name="PART_Track">
        <Track.DecreaseRepeatButton>
          <RepeatButton Style="{StaticResource Ds.ScrollBar.PageButton}" Command="ScrollBar.PageLeftCommand" />
        </Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton>
          <RepeatButton Style="{StaticResource Ds.ScrollBar.PageButton}" Command="ScrollBar.PageRightCommand" />
        </Track.IncreaseRepeatButton>
        <Track.Thumb>
          <Thumb Style="{StaticResource Ds.ScrollBar.Thumb}" />
        </Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property="IsEnabled" Value="False">
        <Setter TargetName="PART_Track" Property="Visibility" Value="Collapsed" />
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <!-- Implicit: ScrollBar bir Control türevidir → bu stil ŞABLON SINIRLARINI geçer ve uygulamadaki HER
       scrollbar'a (StickyLayerList/EventStream ScrollViewer'ları, AvalonEdit console editor'ünün iç
       ScrollViewer'ı, popover/dialog ListBox'ları) kendiliğinden uygulanır — panel XAML'lerine dokunulmaz.
       Explicit stil verilmiş bir bar olursa implicit'i tamamen ezer. -->
  <Style TargetType="ScrollBar">
    <Setter Property="OverridesDefaultStyle" Value="True" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="Width" Value="10" />
    <Setter Property="MinWidth" Value="10" />
    <Setter Property="Template" Value="{StaticResource Ds.ScrollBar.Vertical.Template}" />
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto" />
        <Setter Property="MinWidth" Value="0" />
        <Setter Property="Height" Value="10" />
        <Setter Property="MinHeight" Value="10" />
        <Setter Property="Template" Value="{StaticResource Ds.ScrollBar.Horizontal.Template}" />
      </Trigger>
    </Style.Triggers>
  </Style>

```

- [ ] **Step 2.4: Yeşili gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ScrollBarStyleTests"
```
Expected: 5 PASS. Ek güvence: `--filter "FullyQualifiedName~DsControlTemplateTests|FullyQualifiedName~AntiSlopTests|FullyQualifiedName~NoHardcodedColorTests"` → PASS (yeni XAML'de ham renk yok; gradient/emoji yok; nötr griler amber-hue kuralına takılmaz — aynı değerler `Brush.Border/BorderStrong` olarak zaten sözlükte).

- [ ] **Step 2.5: Commit**

```bash
git add src/BuildOrchestrator.App/Resources/Controls.xaml tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs
git commit -m "feat(scrollbar): DS scrollbar - 10px ray, seffaf track, neutral-700 hap thumb (bo-scroll birebir)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Console (AvalonEdit) — Auto bar görünürlüğü + şeffaf köşe kanıtı

**Files:**
- Modify: `tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs` (2 test eklenir)
- Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml`

**Interfaces:**
- Consumes: Task 2'nin implicit stili + `SystemColors.ControlBrushKey` override'ı; `RepoPaths.AppSrcRoot` (mevcut, `tests/BuildOrchestrator.Tests/RepoPaths.cs` — `BuildOrchestrator.Tests` namespace'i, App alt-namespace'inden using'siz çözülür).

- [ ] **Step 3.1: Failing testler — ScrollBarStyleTests sınıfının sonuna (son testin kapanış `}`'inden sonra, sınıf kapanışından önce) 2 test ekle**

```csharp
    [StaFact]
    public void The_console_editor_realizes_ds_bars_and_a_transparent_corner()
    {
        // Console'un GERÇEK yolu: AvalonEdit TextEditor → iç ScrollViewer → ScrollBar'lar. Implicit stilin
        // AvalonEdit şablonunun içine de ulaştığı ve iki bar'ın kesiştiği köşe karesinin (Corner) şeffaf
        // olduğu burada, üretimdekiyle aynı kontrol üzerinden kanıtlanır.
        var host = DsResources.NewHost();
        var editor = new ICSharpCode.AvalonEdit.TextEditor
        {
            Width = 260,
            Height = 100,
            WordWrap = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, Enumerable.Repeat(new string('x', 400), 60)),
        };
        var window = DsResources.Realize(host, editor);

        var bars = DsResources.Descendants(editor).OfType<ScrollBar>().ToList();
        Assert.Equal(10.0, bars.Single(b => b.Orientation == Orientation.Vertical).ActualWidth);
        Assert.Equal(10.0, bars.Single(b => b.Orientation == Orientation.Horizontal).ActualHeight);

        // Default ScrollViewer şablonundaki "Corner" Rectangle'ı ControlBrushKey'i DynamicResource ile okur —
        // Controls.xaml'deki override onu şeffaflaştırır (açık-tema grisi koyu konsolda parlamaz).
        var corner = DsResources.Descendants(editor).OfType<Rectangle>().Single(r => r.Name == "Corner");
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(corner.Fill));
        GC.KeepAlive(window);
    }

    [Fact]
    public void Console_view_declares_auto_visibility_for_both_bars()
    {
        // BuildApp.jsx:616 konsol kutusu overflow AUTO'dur — bar yalnız gerektiğinde görünür. AvalonEdit'in
        // default'u Visible olduğundan ConsoleView bunu AÇIKÇA Auto'ya çevirmek zorundadır (kaynak pinlenir;
        // ConsoleView pack URI'siz headless kurulamadığı için realize DEĞİL kaynak taraması kullanılır —
        // NoHardcodedColorTests ile aynı yaklaşım).
        string xaml = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Console", "ConsoleView.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml);
    }
```

- [ ] **Step 3.2: Kırmızıyı gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ScrollBarStyleTests"
```
Expected: 2 yeni test FAIL — corner testinde `Expected: #00FFFFFF (Transparent)  Actual: #FFF0F0F0` benzeri renk farkı (override henüz Task 2'de eklendiyse bu assert geçebilir; o durumda yalnız `Console_view_declares...` FAIL olur — `Contains` bulunamaz).

- [ ] **Step 3.3: ConsoleView.xaml'a 2 attribute ekle**

`ConsoleView.xaml` içinde şu anchor bloğunu bul:

```xml
    <avalonEdit:TextEditor x:Name="EditorControl"
                            IsReadOnly="True"
                            WordWrap="False"
                            ShowLineNumbers="False"
```

Şununla değiştir (yalnız 2 satır eklenir, diğer attribute'lara dokunulmaz):

```xml
    <avalonEdit:TextEditor x:Name="EditorControl"
                            IsReadOnly="True"
                            WordWrap="False"
                            ShowLineNumbers="False"
                            VerticalScrollBarVisibility="Auto"
                            HorizontalScrollBarVisibility="Auto"
```

(İstenirse editörün üstündeki mevcut yorum bloğuna tek satır eklenebilir: `<!-- [SCROLLBAR] BuildApp.jsx:616: konsol kutusu overflow AUTO — AvalonEdit default'u Visible olduğundan açıkça çevrilir. -->`)

- [ ] **Step 3.4: Yeşili gör**

```bash
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ScrollBarStyleTests"
```
Expected: 7 PASS.

- [ ] **Step 3.5: Commit**

```bash
git add src/BuildOrchestrator.App/Console/ConsoleView.xaml tests/BuildOrchestrator.Tests/App/ScrollBarStyleTests.cs
git commit -m "feat(scrollbar): konsol Auto bar gorunurlugu + seffaf kose (ControlBrushKey override kaniti)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Tam doğrulama + merge

- [ ] **Step 4.1: Tam suite + build**

```bash
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj
```
Expected: build OK, suite tamamen PASS. Özellikle konsol/scroll davranış testleri (`FollowScrollControllerTests`, BottomAnchor/LatestPill testleri) yeşil kalmalı — bar görseli değişti, offset aritmetiği değişmedi.

- [ ] **Step 4.2: Görsel smoke (manuel, isteğe bağlı ama önerilir)**

```bash
dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```
Kontrol listesi: (a) sol-alt proje listesi + sağ-alt event stream: ince koyu 10px bar, açık-gri Windows bar'ı YOK; (b) console: bar'lar yalnız içerik taşınca; uzun satırda yatay bar; iki bar kesişiminde AÇIK GRİ KÖŞE KARESİ YOK; (c) thumb hover'da bir tık açılıyor, sürüklerken açık kalıyor; (d) track boşluğuna tıklayınca sayfa atlıyor; (e) graf paneli değişmemiş; (f) branch popover listesi açıldığında aynı ince bar.

- [ ] **Step 4.3: Merge + temizlik (CLAUDE.md git kuralı)**

```bash
git checkout main && git pull
git merge scrollbar-restyle
git push
# merge'ün main'e gerçekten geçtiğini doğrula (git log), SONRA:
git branch -d scrollbar-restyle
git push origin --delete scrollbar-restyle   # remote'a push'landıysa
```
Dizin `main` üzerinde bırakılır.

---

## Riskler ve Fallback'ler

1. **Corner anahtarı varsayımı:** Aero2 ScrollViewer şablonunda Corner `SystemColors.ControlBrushKey` okur — Task 3'ün AvalonEdit testi bunu GERÇEK şablon üzerinden doğrular. Test kırmızı kalırsa (farklı tema anahtarı), fallback: `Single(r => r.Name == "Corner")` ile bulunan Rectangle'ın gerçekte hangi DynamicResource'u okuduğunu debugger/`Fill` üzerinden tespit edip override'ı o anahtara taşı; son çare per-view scoped override (`ConsoleView.Resources`).
2. **`DsTransition.AnimatedBackground` Thumb'da çalışmazsa** (ör. attached property yalnız belirli tiplere bakıyorsa — beklenmez, ListBoxItem deseni birebir): fallback, thumb şablon trigger'larının `Pill` Border'ının Background'ını doğrudan `DynamicResource Brush.Neutral600` setter'ıyla değiştirmesi (anlık geçiş; paylaşılan fırça ANİME EDİLMEDİĞİ için A13.2 ihlali değildir). Test assert'leri değişmez.
3. **Track'in minimum thumb boyu:** WPF `Track`, thumb uzunluğunu viewport oranından hesaplar ve sistem metriğiyle (~17px) tabanlar — çok uzun içerikte thumb kaybolmaz; ekstra MinHeight gerekmez. Smoke'ta çok uzun console log'uyla gözle doğrula.
4. **a13 çakışması:** Anchor'lar bulunamazsa dosyanın güncel halini oku; SCROLLBAR bölümü her durumda "ÖZEL KONTROLLERİN VARSAYILAN ŞABLONLARI" başlığından önce, TOOLTIP bölümünden sonra durur; Tokens ekleri nötr rampa bloğunda kalır.

## Kapsam Dışı (bilinçli)

- **Overlay/auto-hide scrollbar yok:** prototip klasik (yer kaplayan) ince ray kullanır; ScrollViewer şablonuna dokunulmaz (TextBox `PART_ContentHost` gibi iç ScrollViewer'ları kırma riski sıfırlanır).
- **Graf paneline scroll eklenmez** (bugün ScrollViewer'ı yok; eklenirse stil kendiliğinden uygulanır).
- **GridSplitter/`DsSplitter` görseli** bu planın konusu değildir (ControlBrushKey override'ından etkilenmediği doğrulandı: `DsSplitter.cs:50` explicit `Background=Transparent`).
- Scrollbar'a motion/parlama eklenmez (restraint; tek hero motion kuralı).
