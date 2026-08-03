# Varsayılan katmanlar + Save'e ertelenmiş Settings senkronizasyonu — implementasyon planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Spec:** `.claude/outputs/2026-08-03-17-32-default-layers-and-deferred-settings-sync.md`
**Branch:** `feat/default-layers-and-deferred-settings-sync`

**Goal:** Settings diyaloğu OSYS katmanlarıyla hazır açılsın ve repo yolu dahil tüm ayarlar Save'e basılana
kadar uygulanmasın; Save tek bir Sync ile her şeyi senkronize etsin.

**Architecture:** Varsayılan katman listesi App/Shell'de tek bir statik sınıfta durur ve iki tüketicisi vardır
(taslak kurulumu + "Restore default layers"). Settings diyaloğunun taslak VM'i katmanların yanı sıra
seçilmiş-ama-uygulanmamış repo kökünü de taşır; Save `RunViewModel.ApplySettingsAsync` ile tek yoldan
uygular: katmanlar → (mid-run kapısı) → kök → tek Sync. Uygulama açılışı ve `MainWindow` Choose Folder yolu
değişmez.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`), xUnit (`Fact`/`StaFact`).

## Global Constraints

- **Kod, UI metinleri ve loglar İngilizce; kod yorumları Türkçe.** (CLAUDE.md)
- **Kopya YASAK / tek doğruluk kaynağı:** aynı değer, metin veya primitif iki yerde tanımlanmaz.
- **Kırmızı test kuralı:** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz.
- **Davranış değişince testi de değişir:** eski kuralı pinleyen test silinmez/gevşetilmez — YENİ kuralı
  pinleyecek şekilde yeniden yazılır ve doc'una eski iddia + değişme gerekçesi yazılır.
- **App'te regex çalıştırılmaz** — pattern string'leri veri olarak taşınır, eşleştirmeyi Core yapar.
- **A13.2 reset yasağı:** `ObservableCollection` üzerinde `Clear()` yok — sondan sil + ekle.
- Build/test komutları:
  - `dotnet build BuildOrchestrator.slnx`
  - `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
  - Uygulama açıkken build alma (çalışan Supervisor kendi binary'lerini kilitler).

## Dosya haritası

| Dosya | Sorumluluk | Task |
|---|---|---|
| `src/BuildOrchestrator.App/Shell/LayerDefaults.cs` | **yeni** — varsayılan katman listesi, tek doğruluk kaynağı | 1 |
| `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs` | `LayerEditorViewModel.cs`'ten taşınır; Settings taslağı (katmanlar + bekleyen repo kökü + commit) | 2,3,5 |
| `src/BuildOrchestrator.App/Views/SettingsDialog.xaml(.cs)` | ince view: buton etiketi, Change… taslağa yazar, Save commit eder | 3,5 |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs` | `ApplySettingsAsync` + ortak `ApplyRepositoryRoot` | 4 |
| `tests/BuildOrchestrator.Tests/App/LayerDefaultsTests.cs` | **yeni** — varsayılanların içeriği + Core eşleşmesi | 1 |
| `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs` | taslak/commit/repo testleri (saf + realize) | 3,5 |
| `tests/BuildOrchestrator.Tests/App/SettingsDialogHost.cs` | realize fixture — `pickFolder` seam'i parametreleşir | 5 |
| `tests/BuildOrchestrator.Tests/App/SettingsDialogFocusTests.cs`, `AntiSlopTests.cs` | rename'den etkilenen referanslar | 2 |
| `ARCHITECTURE.md`, `README.md` | anlatı güncellemesi | 6 |

---

### Task 1: `LayerDefaults` — varsayılan katman listesi

**Files:**
- Create: `src/BuildOrchestrator.App/Shell/LayerDefaults.cs`
- Test: `tests/BuildOrchestrator.Tests/App/LayerDefaultsTests.cs`

**Interfaces:**
- Consumes: `BuildOrchestrator.Contracts.Model.LayerPattern(int Order, string Regex, string Name)` (mevcut),
  `BuildOrchestrator.Core.Planning.LayerEngine.AssignLayers` (mevcut, yalnız testte).
- Produces:
  - `BuildOrchestrator.App.Shell.LayerDefaults.Layers` → `IReadOnlyList<(string Name, string Regex)>`
    (tek üye — `Order`, taslağın satır indeksinden `BuildPatterns()`'te türetilir, ikinci bir dönüştürücü
    API'ye gerek yoktur)

- [ ] **Step 1: Write the failing tests**

`tests/BuildOrchestrator.Tests/App/LayerDefaultsTests.cs`:

```csharp
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Settings'in VARSAYILAN katman tanımları (<see cref="LayerDefaults"/>). OSYS çözümünde proje adları
/// <c>OSYS.&lt;Katman&gt;.&lt;Proje…&gt;</c> biçimindedir; varsayılanlar bu dört sabit öneki katman sırasıyla
/// tanımlar. Liste TEK yerdedir — taslak kurulumu ve "Restore default layers" aynı kaynaktan okur.
/// </summary>
public class LayerDefaultsTests
{
    private static ProjectNode N(string name) =>
        new(Id: $@"D:\repo\{name}\{name}.csproj", Name: name, ProjectPath: $@"D:\repo\{name}\{name}.csproj",
            SolutionNames: [], Dependencies: [], BuildOrder: 0, LayerIndex: null, LayerName: null,
            InCycle: false, WillBuild: null);

    [Fact] // Ad, regex ve SIRA birebir pinlenir — bu liste kullanıcıya varsayılan olarak sunulan sözleşmedir.
    public void Default_layers_are_the_four_OSYS_prefixes_in_order()
    {
        Assert.Equal(
            ["OSYS.Types", "OSYS.Business", "OSYS.Orchestration", "OSYS.UI"],
            LayerDefaults.Layers.Select(l => l.Name));
        Assert.Equal(
            [@"^OSYS\.Types\.", @"^OSYS\.Business\.", @"^OSYS\.Orchestration\.", @"^OSYS\.UI\."],
            LayerDefaults.Layers.Select(l => l.Regex));
    }

    [Fact] // Varsayılanlar GERÇEK proje adlarına karşı Core'da çalışır: önek eşleşir, geri kalanı Other'a düşer.
    public void Default_layers_group_real_OSYS_project_names_and_drop_the_rest_into_Other()
    {
        // Order = liste indeksi. Üretimde bu eşleme SettingsDraftViewModel.BuildPatterns()'tedir (taslak satır
        // indeksinden) ve orada ayrıca test edilir; burada yalnız varsayılanların Core'da ne ürettiği ölçülür.
        var patterns = LayerDefaults.Layers.Select((l, i) => new LayerPattern(i, l.Regex, l.Name)).ToList();

        ProjectNode[] nodes =
        [
            N("OSYS.Types.Service.WorkOrder"),
            N("OSYS.Business.Service.WorkOrder"),
            N("OSYS.Orchestration.Service.WorkOrder"),
            N("OSYS.UI.Service.WorkOrder"),
            N("Contoso.Tools.Cli"),   // hiçbir önekle eşleşmez
            N("OSYS.Types"),          // çıplak önek: nokta YOK → bilinçli olarak eşleşmez
        ];

        var result = LayerEngine.AssignLayers(nodes, patterns);
        var byName = result.Nodes.ToDictionary(n => n.Name, n => n.LayerName);

        Assert.Equal("OSYS.Types", byName["OSYS.Types.Service.WorkOrder"]);
        Assert.Equal("OSYS.Business", byName["OSYS.Business.Service.WorkOrder"]);
        Assert.Equal("OSYS.Orchestration", byName["OSYS.Orchestration.Service.WorkOrder"]);
        Assert.Equal("OSYS.UI", byName["OSYS.UI.Service.WorkOrder"]);
        Assert.Equal(LayerEngine.OtherLayerName, byName["Contoso.Tools.Cli"]);
        Assert.Equal(LayerEngine.OtherLayerName, byName["OSYS.Types"]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~LayerDefaultsTests"
```

Beklenen: **derleme hatası** — `LayerDefaults` tipi yok (`CS0103`/`CS0246`). Bu geçerli bir kırmızıdır.

- [ ] **Step 3: Write the implementation**

`src/BuildOrchestrator.App/Shell/LayerDefaults.cs`:

```csharp
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// Settings'in VARSAYILAN katman tanımları — OSYS çözümünün sabit katman önekleri. İki tüketicisi vardır:
/// kayıtlı katman yokken Settings taslağının kurulumu ve "Restore default layers" butonu. Liste başka hiçbir
/// yerde tekrarlanmaz (tek doğruluk kaynağı).
///
/// <para>Proje adları <c>OSYS.&lt;Katman&gt;.&lt;Proje…&gt;</c> biçimindedir (ör.
/// <c>OSYS.Types.Service.WorkOrder</c>): önek sabittir, sonrası proje adıdır. Regex bu yapıya birebir uyar —
/// önek + nokta. Çıplak <c>OSYS.Types</c> adında bir proje BİLİNÇLİ olarak eşleşmez ve
/// <see cref="LayerEngine.OtherLayerName"/> katmanına düşer.</para>
///
/// <para>Eşleşme <see cref="ProjectNode.Name"/> (assembly kısa adı) üzerindedir ve
/// <see cref="LayerEngine.CompileUserPattern"/> pattern'leri <c>IgnoreCase</c> derler — <c>OSYS.UI</c> /
/// <c>OSYS.Ui</c> ayrımı sorun değildir.</para>
///
/// <para>Bu bir AÇILIŞ seed'i DEĞİLDİR: uygulama açılışında kalıcı duruma hiçbir şey yazılmaz, varsayılanlar
/// yalnız Settings taslağında görünür ve Save'e basılana dek ne motora ne diske gider.</para>
/// </summary>
public static class LayerDefaults
{
    /// <summary>Varsayılan katmanlar, KATMAN SIRASIYLA. Liste indeksi katman sırasıdır; Contracts'ın
    /// <see cref="LayerPattern.Order"/>'ı buradan DEĞİL, taslağın satır indeksinden türetilir
    /// (<c>SettingsDraftViewModel.BuildPatterns</c>) — sıra tek yerde yorumlanır.</summary>
    public static readonly IReadOnlyList<(string Name, string Regex)> Layers =
    [
        ("OSYS.Types", @"^OSYS\.Types\."),
        ("OSYS.Business", @"^OSYS\.Business\."),
        ("OSYS.Orchestration", @"^OSYS\.Orchestration\."),
        ("OSYS.UI", @"^OSYS\.UI\."),
    ];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~LayerDefaultsTests"
```

Beklenen: 2 test PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BuildOrchestrator.App/Shell/LayerDefaults.cs tests/BuildOrchestrator.Tests/App/LayerDefaultsTests.cs
git commit -m "feat(settings): varsayilan OSYS katman tanimlari tek kaynakta"
```

---

### Task 2: `LayerEditorViewModel` → `SettingsDraftViewModel` (saf rename)

Davranış değişmez; yalnız ad ve dosya taşınır. Gerekçe: Task 5 bu VM'e repo kökünü ekliyor, "layer editor"
adı yanıltıcı kalırdı. Rename'i ayrı tutmak sonraki task'ların diff'ini okunur bırakır.

**Bu task yeni test EKLEMEZ** — davranış değişmediği için kırmızı gösterilecek bir kusur yoktur; doğrulama
tam süitin yeşil kalmasıdır.

**Files:**
- Rename: `src/BuildOrchestrator.App/ViewModels/LayerEditorViewModel.cs` →
  `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs`
- Modify: `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` (satır 10 yorumu),
  `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs`
- Modify: `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs`,
  `tests/BuildOrchestrator.Tests/App/SettingsDialogFocusTests.cs:115`,
  `tests/BuildOrchestrator.Tests/App/AntiSlopTests.cs`
- Modify: `ARCHITECTURE.md:1623`

**Interfaces:**
- Produces: `BuildOrchestrator.App.ViewModels.SettingsDraftViewModel` — üyeleri değişmez
  (`Layers`, `CanSave`, `AddLayer()`, `RemoveLayer(row)`, `LoadSampleLayers()`, `BuildPatterns()`,
  `Commit(run, store)`). `LayerRowViewModel` aynı dosyada ve aynı adda kalır.

- [ ] **Step 1: Dosyayı taşı**

```bash
git mv src/BuildOrchestrator.App/ViewModels/LayerEditorViewModel.cs \
       src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs
```

- [ ] **Step 2: Sınıf adını ve tüm referansları güncelle**

`LayerEditorViewModel` → `SettingsDraftViewModel` (sınıf bildirimi, `<see cref=…>`/`<c>…</c>` doc atıfları
dahil) şu dosyalarda:

- `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs` — sınıf bildirimi ve XML doc'lar
- `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` satır 10 (yorum metni)
- `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs` — sınıf doc'u, `_editor` alanının tipi;
  alan adını da `_editor` → `_draft` yap (tüm kullanımlarıyla: `Open`, `OnAddLayer`, `OnRemoveLayer`,
  `OnLoadSampleLayers`, `OnSave`)
- `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs` — satır 16, 35, 43, 89, 118, 139
- `tests/BuildOrchestrator.Tests/App/SettingsDialogFocusTests.cs:115` — cast tipi
- `ARCHITECTURE.md:1623` — kod haritası satırı:
  `| Layer editor state | App/ViewModels/SettingsDraftViewModel.cs |`

`AntiSlopTests.cs`'te dosya adı bir **string** olarak taranır — güncellenmezse guard sessizce sıfır dosya
tarar (non-vacuity assert'i bunu yakalar ve kırmızı verir):

- satır 144: `Assert.NotEmpty(SourceGuard.ScannedAppFiles("SettingsDraftViewModel.cs"));`
- satır 147: `.Concat(SourceGuard.ScanApp("SettingsDraftViewModel.cs", LayerMatchCounter, skipCommentLines: true))`
- satır 132 ve 136'daki doc metinlerinde geçen `LayerEditorViewModel.cs` → `SettingsDraftViewModel.cs`
  (satır 136 `:33,:68` satır numaralarına atıfta bulunur — rename satır numaralarını değiştirmez, dokunma)

- [ ] **Step 3: Build ve tam süiti çalıştır**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Beklenen: derleme temiz, **tüm testler PASS** (davranış değişmedi).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(settings): LayerEditorViewModel -> SettingsDraftViewModel"
```

---

### Task 3: Taslak varsayılanla dolu gelir + "Restore default layers"

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs`
- Modify: `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` (footer butonu),
  `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs` (handler adı)
- Modify: `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs`
- Modify: `tests/BuildOrchestrator.Tests/App/AntiSlopTests.cs` (doc'ta `OnLoadSampleLayers` geçiyor)

**Interfaces:**
- Consumes: `LayerDefaults.Layers` (Task 1).
- Produces:
  - `SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial)` — `initial` null **veya boş** ise taslak
    `LayerDefaults.Layers` ile dolu kurulur
  - `SettingsDraftViewModel.RestoreDefaults()` — `LoadSampleLayers()`'in yerini alır
  - `SettingsDraftViewModel.SampleLayers` **kaldırılır**

- [ ] **Step 1: Write the failing tests**

`SettingsDialogTests.cs` içine üç yeni `[Fact]` ekle (mevcut `NeverTickingBatcher()`/`NewStore()`
yardımcılarını kullanır):

```csharp
    [Fact] // Kayıtlı katman YOKKEN taslak varsayılanlarla DOLU gelir — kullanıcı hiç uğraşmadan Save diyebilsin.
    public async Task A_fresh_draft_is_prefilled_with_the_default_layers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        Assert.Null(run.LayerPatterns); // kayıtlı katman yok

        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        Assert.Equal(
            ["OSYS.Types", "OSYS.Business", "OSYS.Orchestration", "OSYS.UI"],
            draft.Layers.Select(r => r.Name));
        Assert.Equal(@"^OSYS\.Types\.", draft.Layers[0].Regex);

        // Taslağın dolu gelmesi tek başına HİÇBİR ŞEY uygulamaz/kaydetmez — açılışta seed YOKtur.
        Assert.Null(run.LayerPatterns);
        Assert.Empty(store.State.LayerPatterns);
    }

    [Fact] // Kayıtlı katman VARSA taslak onların kopyasıdır — varsayılan kullanıcının tanımlarını ASLA ezmez.
    public async Task A_draft_built_from_saved_layers_never_shows_the_defaults()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        run.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")];

        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        var row = Assert.Single(draft.Layers);
        Assert.Equal("Alpha", row.Name);
        Assert.Equal("^A", row.Regex);
    }

    [Fact] // "Restore default layers": düzenlenmiş taslağı varsayılanlara döndürür, Save'siz KALICI DEĞİL.
    public async Task Restore_default_layers_replaces_the_draft_without_touching_the_live_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;
        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        draft.RestoreDefaults();

        Assert.Equal(4, draft.Layers.Count);
        Assert.Equal("OSYS.Types", draft.Layers[0].Name);
        Assert.Equal("OSYS.UI", draft.Layers[3].Name);
        Assert.Same(live, run.LayerPatterns);        // canlı pattern'lere DOKUNULMADI
        Assert.Empty(store.State.LayerPatterns);     // diske yazılmadı
    }
```

Realize (WPF) tarafında `SettingsDialogViewTests` içine, boş-durum kutusunun YENİ kuralını pinleyen bir
`[StaFact]` ekle (dosyanın başına `using System.Windows;` gerekir — `Visibility` oradan gelir):

```csharp
    /// <summary>Boş-durum kutusu ARTIK taze diyalogda görünmez: taslak varsayılanlarla dolu açılır. Kutu
    /// yalnız kullanıcı TÜM satırları silince ortaya çıkar.
    /// <para><b>Eski iddia (değişti):</b> <c>Settings_dialog_pins_the_layers_caption_description_and_empty_state_box_verbatim</c>
    /// kutuyu "katman yokken (taze LayerPatterns null) görünür" diye pinliyordu. Varsayılan taslak geldiğinden
    /// taze diyalogda 4 satır vardır; kuralın kendisi (satır yoksa kutu) korunur, tetikleyicisi değişti.</para></summary>
    [StaFact]
    public void Empty_state_box_appears_only_after_every_layer_row_is_deleted()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var box = DsResources.RealizedObjects(dialog).OfType<Grid>().Single(g => g.Name == "EmptyState");
        Assert.Equal(Visibility.Collapsed, box.Visibility); // taze diyalog: 4 varsayılan satır var

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        for (int i = draft.Layers.Count - 1; i >= 0; i--) draft.RemoveLayer(draft.Layers[i]);
        dialog.UpdateLayout();

        Assert.Equal(Visibility.Visible, box.Visibility);
    }
```

- [ ] **Step 2: Mevcut testleri YENİ kurala göre yeniden yaz**

Bunlar davranış değiştiği için kırılır; silme/gevşetme YOK — yeni kuralı pinlerler ve doc'larına eski iddia
+ gerekçe yazılır.

**(a)** `Save_is_blocked_only_by_an_empty_name_or_an_uncompilable_regex_never_by_an_empty_pattern` — taslak
artık 4 satırla açıldığı için `Assert.Single(editor.Layers)` kırılır. Testin konusu Save-validation'dır,
taslak dolgusu değil. Başına boşaltma ekle ve doc'a not düş:

```csharp
        var editor = new SettingsDraftViewModel(null);
        // [değişti] Taze taslak ARTIK 4 varsayılan satırla gelir (LayerDefaults). Bu testin konusu
        // Save-validation'dır — tek satırlık bir zeminde ölçülür, o yüzden varsayılanlar önce boşaltılır.
        for (int i = editor.Layers.Count - 1; i >= 0; i--) editor.RemoveLayer(editor.Layers[i]);
```

`editor` yerel adı olduğu gibi kalabilir (tip `SettingsDraftViewModel`).

**(b)** `Saving_layers_writes_the_exact_console_note_and_persists_the_patterns` — 6 örnek katmanı ve
`Layer 0 — Core` / `^OSYS\.(Base$|Common\.)` değerlerini pinliyordu. Varsayılanlara göre yeniden yaz:

```csharp
    /// <summary>Save: BİREBİR konsol notu + <see cref="RunViewModel.LayerPatterns"/> + UiState persist'i.
    /// <para><b>Eski iddia (değişti):</b> bu test "Load sample layers"in 6 örnek katmanını
    /// (<c>Layer 0 — Core</c> / <c>^OSYS\.(Base$|Common\.)</c>) pinliyordu. Örnek katmanlar kaldırıldı,
    /// yerlerini OSYS varsayılanları (<see cref="LayerDefaults"/>, 4 katman) aldı; pinlenen kural aynı —
    /// Save notu, pattern sırası ve persist şekli.</para></summary>
    [Fact]
    public async Task Saving_layers_writes_the_exact_console_note_and_persists_the_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();

        var editor = new SettingsDraftViewModel(null); // taze taslak = 4 varsayılan
        Assert.Equal(4, editor.Layers.Count);

        editor.Commit(run, store);

        // (a) BİREBİR konsol notu (BuildApp.jsx:1423).
        Assert.Contains("Layer definitions updated — 4 layers", run.GetRunDocumentText());

        // (b) RunViewModel.LayerPatterns set edildi (Order = 0..3, üstten alta).
        Assert.NotNull(run.LayerPatterns);
        Assert.Equal([0, 1, 2, 3], run.LayerPatterns!.Select(p => p.Order));
        Assert.Equal("OSYS.Types", run.LayerPatterns[0].Name);
        Assert.Equal(@"^OSYS\.Types\.", run.LayerPatterns[0].Regex);

        // (c) UiState'e persist edildi (aynı şekil).
        Assert.Equal(4, store.State.LayerPatterns.Count);
        Assert.Equal(run.LayerPatterns, store.State.LayerPatterns);

        // Emptied → farklı BİREBİR not + persist boşalır.
        var empty = new SettingsDraftViewModel(run.LayerPatterns);
        for (int i = empty.Layers.Count - 1; i >= 0; i--) empty.RemoveLayer(empty.Layers[i]);
        empty.Commit(run, store);
        Assert.Contains("Layers removed — single project list", run.GetRunDocumentText());
        Assert.Empty(store.State.LayerPatterns);
    }
```

**(c)** `Settings_dialog_footer_and_add_layer_button_labels_are_verbatim` — satır 235'i güncelle:

```csharp
        Assert.Contains(buttons, b => Equals(b.Content, "Restore default layers"));
```

ve doc'undaki `Load sample layers` atfını `Restore default layers` yap.

**(d)** `Settings_dialog_pins_the_layers_caption_description_and_empty_state_box_verbatim` — kutu METNİNİ
pinleyen `Assert.Contains("No layers yet — …", texts)` satırı KALIR (metin değişmedi), ama doc'undaki
"Katman yokken (taze `LayerPatterns` null) boş-durum kutusu görünür" cümlesi artık yanlıştır; görünürlük
iddiası Step 1'deki yeni `[StaFact]`'e taşındığı için doc'u şöyle düzelt: "kutu METNİ burada pinlenir;
GÖRÜNÜRLÜK kuralı `Empty_state_box_appears_only_after_every_layer_row_is_deleted`'tedir."

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~SettingsDialog"
```

Beklenen: derleme hatası (`RestoreDefaults` yok) ve/veya yeni testlerde FAIL — taze taslak boş geliyor.

- [ ] **Step 4: Write the implementation**

`SettingsDraftViewModel.cs`: `SampleLayers` alanını **sil**, ctor'u ve `LoadSampleLayers`'ı değiştir:

```csharp
    /// <summary>Taslak = kayıtlı pattern'lerin DERİN kopyası (Order'a göre; editör sırası = katman sırası).
    /// Kayıtlı katman YOKSA (null ya da boş) taslak <see cref="LayerDefaults"/> ile DOLU kurulur — araç
    /// paylaşıldığında kimse katmanları elle yazmasın. Bu YALNIZ taslaktır: Save'e basılmadıkça ne
    /// <see cref="RunViewModel.LayerPatterns"/> ne UiState değişir; uygulama açılışında seed YOKtur.</summary>
    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial)
    {
        Layers.CollectionChanged += OnLayersChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
        else
            AddDefaultRows();
    }

    /// <summary>"Restore default layers" — taslağı <see cref="LayerDefaults"/> ile değiştirir. A13.2 reset
    /// yasağı: <c>Clear()</c> yerine sondan sil + ekle (yalnız Remove/Add bildirimleri — Reset yok).</summary>
    public void RestoreDefaults()
    {
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        AddDefaultRows();
    }

    private void AddDefaultRows()
    {
        foreach (var (name, regex) in LayerDefaults.Layers) AddRow(new LayerRowViewModel(name, regex));
    }
```

`using BuildOrchestrator.App.Shell;` zaten dosyada var (`IUiStateStore` için).

`SettingsDialog.xaml` footer butonu (satır 200-201):

```xml
            <Button DockPanel.Dock="Left" Content="Restore default layers" Click="OnRestoreDefaults"
                    Style="{DynamicResource Ds.Button.Ghost.Md}" />
```

`SettingsDialog.xaml.cs`:

```csharp
    private void OnRestoreDefaults(object sender, RoutedEventArgs e) => _draft?.RestoreDefaults();
```

`AntiSlopTests.cs:130` doc'unda geçen `OnLoadSampleLayers` → `OnRestoreDefaults`.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Beklenen: tam süit PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(settings): taslak varsayilan katmanlarla acilir, Restore default layers butonu"
```

---

### Task 4: `RunViewModel.ApplySettingsAsync` — Save'in tek yolu

Bu task yalnız VM yüzeyini kurar; diyalog Task 5'te bağlanır. Mevcut `ChangeRepositoryAsync` davranışı
korunur ve kök-uygulama adımını yeni metotla PAYLAŞIR (kopya yasağı).

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs:184-200`
- Test: `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs`

**Interfaces:**
- Consumes: mevcut `ApplyLayerPatterns`, `ResetRowsToHollow`, `SyncAsync`, `RefreshRunSurface`,
  `_willBuildIds`, `IsMidRunLocked`, `RootPath`.
- Produces:
  - `RunViewModel.ApplySettingsAsync(IReadOnlyList<LayerPattern> patterns, string? repositoryRoot)` → `Task`
  - private `RunViewModel.ApplyRepositoryRoot(string? path)` → `bool` (kök gerçekten değiştiyse `true`)

- [ ] **Step 1: Write the failing tests**

`SettingsDialogTests.cs` içine dört yeni `[Fact]`:

```csharp
    [Fact] // Save = senkronize et: yalnız katmanlar değişse (kök AYNI) bile TEK Sync gider ve YENİ pattern'leri taşır.
    public async Task Applying_settings_sends_one_sync_that_carries_the_new_layer_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, @"^OSYS\.Types\.", "OSYS.Types")];
        await run.ApplySettingsAsync(patterns, @"D:\repo"); // kök DEĞİŞMEDİ — Sync yine gider

        var sync = Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Equal(@"D:\repo", sync.RootPath);
        Assert.Same(patterns, sync.LayerPatterns);   // SIRA kanıtı: katmanlar Sync'ten ÖNCE uygulandı
        Assert.Same(patterns, run.LayerPatterns);
        Assert.Contains("Layer definitions updated — 1 layers", run.GetRunDocumentText());
    }

    [Fact] // Save kökü de değiştirdiyse: kök yeni, satırlar hollow, Sync YENİ kökte ve TEK.
    public async Task Applying_settings_with_a_new_root_resets_rows_and_syncs_at_the_new_root()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\old" };
        run.OnEvent(new ProjectStartedEvent("r1", @"C:\old\a.csproj", "A"));
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State);

        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"D:\new\repo");

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    [Fact] // Kök HİÇ seçilmemişken Save: katmanlar kaydedilir ama gidecek bir kök yoktur → Sync GİTMEZ.
    public async Task Applying_settings_without_a_repository_root_keeps_the_layers_but_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, null);

        Assert.Same(patterns, run.LayerPatterns);
        Assert.Empty(sent);
    }

    [Fact] // Koşu UÇUŞTAyken Save: katmanlar kaydedilir, kök DEĞİŞMEZ, Sync GİTMEZ (koşan build'in kökü çekilmez).
    public async Task Applying_settings_mid_run_keeps_the_root_and_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", IsStarting = true };
        Assert.True(run.IsMidRunLocked);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\other\repo");

        Assert.Same(patterns, run.LayerPatterns);   // katmanlar YİNE uygulanır (sessizce kaybolmaz)
        Assert.Equal(@"D:\repo", run.RootPath);     // kök değişmedi
        Assert.Empty(sent);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~SettingsDialogTests"
```

Beklenen: derleme hatası — `ApplySettingsAsync` yok.

- [ ] **Step 3: Write the implementation**

`RunViewModel.ActionBar.cs`'te `ChangeRepositoryAsync`'ın yerine (satır 184-200):

```csharp
    /// <summary>[Settings] Save'in TEK giriş noktası: katman pattern'lerini uygular, gerekirse repo kökünü
    /// değiştirir ve TEK bir Sync gönderir.
    ///
    /// <para><b>Sıra ZORUNLUdur:</b> katmanlar Sync'ten ÖNCE uygulanır — <see cref="SyncWorkspaceCommand"/>
    /// <see cref="LayerPatterns"/>'i TAŞIR, ters sırada komut ESKİ pattern'lerle giderdi.</para>
    ///
    /// <para><b>Sync KOŞULSUZdur:</b> "repo mu katman mı değişti" ayrımı YAPILMAZ — Save'e basmak
    /// "senkronize et" demektir ve Sync salt-okurdur, tekrarı zararsızdır. İki kapı vardır: (a) koşu
    /// uçuştaysa (<see cref="IsMidRunLocked"/>) katmanlar yine uygulanır ama kök DEĞİŞMEZ ve Sync GİTMEZ —
    /// koşan bir build'in kökünü altından çekmek doğru değildir (<see cref="ChangeRepositoryAsync"/> de
    /// mid-run'da no-op'tur); (b) kök hiç seçilmemişse gidecek bir kök yoktur.</para></summary>
    public async Task ApplySettingsAsync(IReadOnlyList<LayerPattern> patterns, string? repositoryRoot)
    {
        ApplyLayerPatterns(patterns);
        if (IsMidRunLocked) return;
        ApplyRepositoryRoot(repositoryRoot);
        if (RootPath.Length == 0) return;
        await SyncAsync();
    }

    /// <summary>[D7 · K10] Kabuğun "Choose Folder" yolu: yeni bir repo kökü seçilince kökü değiştirir, proje
    /// durumlarını sıfırlar (yeni repo = yeni taban) ve HEMEN Sync başlatır — burada bir Save yoktur. Settings
    /// diyaloğu bu yolu KULLANMAZ; orada seçim Save'e ertelenir (<see cref="ApplySettingsAsync"/>). Klasör
    /// seçici çağıranın enjekte ettiği bir seam'dir — bu metot yalnız sonucu (yol) alır.</summary>
    public async Task ChangeRepositoryAsync(string path)
    {
        if (IsMidRunLocked) return;
        if (!ApplyRepositoryRoot(path)) return;
        await SyncAsync();
    }

    /// <summary>[Settings · K10] Repo kökünü UYGULAR: kök değişir (<see cref="OnRootPathChanged"/> Empty→Boot
    /// geçişini sürer), satırlar hollow'a sıfırlanır, willBuild kümesi temizlenir ve run yüzeyi tazelenir.
    /// Sync GÖNDERMEZ — o kararı çağıran verir (Choose Folder hemen, Settings Save'de tek Sync içinde). İki
    /// yolun ortak adımı burada TEK yerdedir (kopya yasağı).
    /// <para>Boş yol ya da AYNI kökün yeniden seçilmesi (Windows yolları case-insensitive) NO-OP'tur ve
    /// <c>false</c> döner — aksi halde her satır boşuna hollow'a sıfırlanır ve gereksiz bir Sync gönderilirdi.</para></summary>
    private bool ApplyRepositoryRoot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (string.Equals(path, RootPath, StringComparison.OrdinalIgnoreCase)) return false;
        RootPath = path;
        ResetRowsToHollow();
        _willBuildIds.Clear();
        RefreshRunSurface();
        return true;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Beklenen: tam süit PASS — mevcut `Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root`
ve `Repicking_the_current_repository_root_is_a_no_op` testleri de yeşil kalır (davranış korundu).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(settings): ApplySettingsAsync - katmanlar, kok ve tek Sync tek yolda"
```

---

### Task 5: Repo yolu Save'e ertelenir (diyalog bağlanır)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs`
- Modify: `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs`
- Modify: `tests/BuildOrchestrator.Tests/App/SettingsDialogHost.cs` (pickFolder seam'i parametreleşir)
- Modify: `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs`

**Interfaces:**
- Consumes: `RunViewModel.ApplySettingsAsync` (Task 4).
- Produces:
  - `SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot = null)`
  - `SettingsDraftViewModel.RepositoryRoot` → `string?` (observable)
  - `SettingsDraftViewModel.CommitAsync(RunViewModel run, IUiStateStore store)` → `Task`
    (senkron `Commit` **kaldırılır**)
  - `SettingsDialogHost.OpenRealized(Action<RunViewModel>? configure = null, Func<string?>? pickFolder = null)`

- [ ] **Step 1: Write the failing tests**

`SettingsDialogTests.cs` içine üç yeni test:

```csharp
    [Fact] // "Change…" TEK BAŞINA hiçbir şey uygulamaz: kök değişmez, satırlar sıfırlanmaz, komut GİTMEZ.
    public async Task Picking_a_folder_only_updates_the_draft()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new ProjectStartedEvent("r1", @"D:\repo\a.csproj", "A"));
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath);
        draft.RepositoryRoot = @"D:\new\repo"; // "Change…" yalnız BUNU yapar

        Assert.Equal(@"D:\repo", run.RootPath);
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State); // hollow reset YOK
        Assert.Empty(sent);                                                       // Sync YOK
    }

    [Fact] // Save: bekleyen kök UYGULANIR, satırlar hollow, TEK Sync yeni kökte — ve katmanlar da persist edilir.
    public async Task Saving_applies_the_pending_repository_root_and_syncs_once()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new ProjectStartedEvent("r1", @"D:\repo\a.csproj", "A"));
        var store = NewStore();
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath) { RepositoryRoot = @"D:\new\repo" };

        await draft.CommitAsync(run, store);

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(4, store.State.LayerPatterns.Count); // varsayılan taslak da aynı Save'de persist edildi
    }
```

Üçüncü test bir `[StaFact]`'tir — **`SettingsDialogTests`'e DEĞİL, `SettingsDialogViewTests` sınıfına**
eklenir (o sınıfın doc'u der ki: saf `[Fact]` sınıfında WPF YOKTUR, realize kalemler
`SettingsDialogViewTests`'tedir). Gereken using'ler: `System.Windows` (`RoutedEventArgs`) ve
`System.Windows.Controls.Primitives` (`ButtonBase.ClickEvent`).

```csharp
    /// <summary>Diyalogda "Change…": yalnız yol ETİKETİ güncellenir; canlı kök ve motor DOKUNULMAZ.</summary>
    [StaFact]
    public void Change_button_updates_only_the_dialog_label_until_save()
    {
        var (dialog, run, _, scope) = SettingsDialogHost.OpenRealized(pickFolder: () => @"D:\picked\repo");
        using var _scope = scope;
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var change = DsResources.RealizedObjects(dialog).OfType<Button>().Single(b => Equals(b.Content, "Change…"));
        change.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout();

        var label = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Single(t => t.Name == "RepoPathText");
        Assert.Equal(@"D:\picked\repo", label.Text);   // etiket YENİ yolu gösterir
        Assert.Equal(@"D:\repo", run.RootPath);        // canlı kök ESKİ (fixture kökü)
        Assert.Empty(sent);                            // Sync YOK
    }
```

`Cancel_discards_the_draft` testini repo tarafını da kapsayacak şekilde genişlet (doc'una not düşerek):

```csharp
        // [genişletildi] Cancel artık repo seçimini de atar: taslak kökü değişse bile canlı kök DOKUNULMAZ.
        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath) { RepositoryRoot = @"D:\new\repo" };
        ...
        Assert.Equal(@"D:\repo", run.RootPath); // Commit çağrılmadı → kök eski
```

(Testin başında `run.RootPath = @"D:\repo"` set edilmelidir.)

Diğer mevcut testlerde `editor.Commit(run, store)` çağrıları `await editor.CommitAsync(run, store)` olur —
`Cancel_discards_the_draft` ve `Saving_layers_writes_the_exact_console_note_and_persists_the_patterns`.
Bu ikisi zaten `async Task` testlerdir. `Saving_layers…` testinde `run.RootPath` boş kaldığı için Sync
gönderilmez (Task 4 kapısı) — testin konusu değişmez.

`Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root` testinin **doc'una** ekle:

```csharp
    /// <para><b>Kapsam değişti:</b> bu test artık YALNIZ kabuğun "Choose Folder" yolunu pinler. Settings
    /// diyaloğunun "Change…" düğmesi bu yola girmez — orada seçim Save'e ertelenir
    /// (<c>Picking_a_folder_only_updates_the_draft</c> / <c>Saving_applies_the_pending_repository_root_and_syncs_once</c>).</para>
```

- [ ] **Step 2: `SettingsDialogHost`'a pickFolder seam'ini ekle**

```csharp
    public static (SettingsDialog dialog, RunViewModel run, FakeStore store, IDisposable scope) OpenRealized(
        Action<RunViewModel>? configure = null, Func<string?>? pickFolder = null)
    {
        ...
        dialog.Open(run, store, pickFolder ?? (() => null));
```

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~SettingsDialog"
```

Beklenen: derleme hatası — `RepositoryRoot`/`CommitAsync` ve iki-argümanlı ctor yok.

- [ ] **Step 4: Write the implementation**

`SettingsDraftViewModel.cs` — ctor'a kök parametresi, observable alan ve `Commit` → `CommitAsync`:

```csharp
    /// <summary>Seçilmiş ama HENÜZ UYGULANMAMIŞ repo kökü. "Change…" yalnız burayı yazar; kök değişimi,
    /// satır reset'i ve Sync Save'e ertelenir — Cancel/Esc taslağı atar ve hiçbir iz kalmaz. Diyalog
    /// açılırken canlı <see cref="RunViewModel.RootPath"/> ile başlar.</summary>
    [ObservableProperty] private string? _repositoryRoot;

    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot = null)
    {
        _repositoryRoot = repositoryRoot;
        Layers.CollectionChanged += OnLayersChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
        else
            AddDefaultRows();
    }

    /// <summary>Kaydet (commit): taslağı <see cref="UiState.LayerPatterns"/>'a persist eder ve TEK yoldan
    /// uygular — <see cref="RunViewModel.ApplySettingsAsync"/> katmanları, bekleyen repo kökünü ve TEK Sync'i
    /// birlikte sürer. Cancel bu metodu ÇAĞIRMAZ → taslak (kopya) atılır, canlı duruma dokunulmaz.</summary>
    public async Task CommitAsync(RunViewModel run, IUiStateStore store)
    {
        var patterns = BuildPatterns();
        var state = store.Load();
        state.LayerPatterns = patterns.ToList();
        store.Save(state);
        await run.ApplySettingsAsync(patterns, RepositoryRoot);
    }
```

Eski senkron `Commit` metodunu **sil**.

`SettingsDialog.xaml.cs`:

```csharp
    public void Open(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        _run = run;
        _store = store;
        _pickFolder = pickFolder;
        _draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath);
        DataContext = _draft;
        UpdateRepoLabel();
        Visibility = Visibility.Visible;
        Focus();
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void UpdateRepoLabel() =>
        RepoPathText.Text = _draft?.RepositoryRoot is { Length: > 0 } root ? root : "no repository";

    // ---- Repository (K10) ----

    // "Change…" YALNIZ taslağa yazar: kök değişimi, satır reset'i ve Sync Save'e ertelenir (Cancel her şeyi atar).
    private void OnChangeRepository(object sender, RoutedEventArgs e)
    {
        if (_pickFolder?.Invoke() is not { Length: > 0 } path || _draft is null) return;
        _draft.RepositoryRoot = path;
        UpdateRepoLabel();
    }

    // ---- Save / Cancel ----

    // Diyalog Save'e basıldığı anda kapanır; commit (persist + katmanlar + kök + tek Sync) arkasından sürer.
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _run is null || _store is null || !_draft.CanSave) return;
        var (draft, run, store) = (_draft, _run, _store);
        Close();
        await draft.CommitAsync(run, store);
    }
```

`OnChangeRepository` artık `async` DEĞİLDİR (await kalmadı) — `async` anahtar sözcüğünü kaldır.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Beklenen: tam süit PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(settings): repo yolu Save'e ertelenir, Change... yalnizca taslagi yazar"
```

---

### Task 6: Dokümanları güncelle

Anlatı üslubu korunur — "eskiden böyleydi" YAZILMAZ, ilgili bölüm yerinde yeniden yazılır.

**Files:**
- Modify: `ARCHITECTURE.md` (§13.3 Settings paragrafı ~952-955; §22 kod haritası ~1623, 1639)
- Modify: `README.md:126`

- [ ] **Step 1: Değişen iddiaları bul**

```powershell
Select-String -Path ARCHITECTURE.md,README.md -Pattern "Settings|sample layers|Change|LAYERS"
```

- [ ] **Step 2: `ARCHITECTURE.md` §13.3 paragrafını yeniden yaz**

Mevcut paragraf (satır 952-955) şunu söylüyor: dialog 620 px, LAYERS editörü + REPOSITORY satırı, 36 px
kartlar, grip ile sürükle, geçersiz regex Save'i kilitler. Bunların hepsi **hâlâ doğru** — dokunma. Sonuna
yeni davranışı ekle (İngilizce):

- Kayıtlı katman yokken editör OSYS varsayılanlarıyla (`OSYS.Types` / `OSYS.Business` /
  `OSYS.Orchestration` / `OSYS.UI`, önek regex'leri) açılır; *Restore default layers* bunları geri getirir.
  Bu bir açılış seed'i değildir — varsayılanlar yalnız taslakta yaşar, *Save*'e basılana dek ne diske ne
  motora gider.
- *Change…* yalnız taslağa yazar. *Save* tek yoldur: katmanlar uygulanır, sonra bekleyen kök (satırlar
  hollow'a sıfırlanır), sonra **tek** Sync — bu sırayla, çünkü Sync komutu katman pattern'lerini taşır.
  Sync koşulsuzdur; iki istisnası koşu uçuştayken ve kök hiç seçilmemişken'dir. Cancel/Esc/scrim taslağı
  atar.
- Kabuğun *Choose Folder* daveti bu yoldan geçmez: orada Save yoktur, seçim anında uygulanır.

- [ ] **Step 3: §22 kod haritası satırlarını güncelle**

- `| Layer editor state | App/ViewModels/SettingsDraftViewModel.cs |` (Task 2'de yapıldıysa doğrula) —
  etiketi de `| Settings draft state (layers + pending root) |` yap.
- Yeni satır ekle: `| Default layer definitions | App/Shell/LayerDefaults.cs |`

- [ ] **Step 4: `README.md:126`'yı düzelt**

Mevcut: *"Pick a repository — Settings → repository "Change…". Changing it resets project states and starts
a Sync."* Artık yanlış. Yeni hâli (İngilizce, kısa): Change… seçimi kaydeder; *Save* uygular — proje
durumları sıfırlanır ve tek bir Sync başlar. Kabuğun *Choose Folder* düğmesi anında uygular.

- [ ] **Step 5: Tam süiti son bir kez çalıştır**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

Beklenen: tam süit PASS (doküman guard'ları dahil).

- [ ] **Step 6: Commit**

```bash
git add ARCHITECTURE.md README.md
git commit -m "docs: varsayilan katmanlar ve Save'e ertelenmis settings senkronizasyonu"
```

---

## Bitiş

- [ ] Tam süit yeşil: `dotnet test … --filter "Category!=Acceptance"`
- [ ] `main`'e merge + push, merge doğrulandıktan sonra branch local + remote silinir (CLAUDE.md Git kuralı)
