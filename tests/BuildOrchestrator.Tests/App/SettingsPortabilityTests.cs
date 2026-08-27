using System.Windows;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.10.0 §2.9] Settings'in <b>Export / Import / Clear</b> üçlüsü.
///
/// <para>Üçünün de ortak kuralı: <b>yalnız FORMU değiştirirler.</b> Save'e kadar ne canlı
/// <see cref="RunViewModel"/> ne UiState değişir ve ayrı bir onay dialogu YOKTUR — Clear'ın "onayı" iki
/// aşamalı düğmenin kendisidir (ilk tık uyarır, 2.4s sonra kendini iptal eder).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SettingsPortabilityTests
{
    private static void Click(ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    // ---------------------------------------------------------------- dosya biçimi (saf)

    /// <summary>[§2.9] Dosya <c>{ app, version, repositoryRoot, layers[{name, pattern}] }</c> taşır ve
    /// gidiş-dönüşte içerik korunur. Katman SIRASI dizinin kendi sırasıdır — ayrı bir <c>order</c> alanı
    /// yazılmaz (iki doğruluk kaynağı olurdu).</summary>
    [Fact]
    public void The_settings_file_round_trips_the_root_and_the_layers_in_order()
    {
        IReadOnlyList<LayerPattern> layers =
            [new LayerPattern(1, "^B", "Beta"), new LayerPattern(0, "^A", "Alpha")];

        string json = SettingsFile.From(@"D:\src\osys", layers).ToJson();
        var parsed = SettingsFile.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(@"D:\src\osys", parsed!.RepositoryRoot);
        Assert.Equal(["Alpha", "Beta"], parsed.Layers.Select(l => l.Name));  // Order'a göre sıralanmış
        Assert.Equal(["^A", "^B"], parsed.Layers.Select(l => l.Pattern));
        Assert.Equal(BuildOrchestrator.App.Services.AppIdentity.Product, parsed.App);
        Assert.Contains("\"pattern\"", json, StringComparison.Ordinal); // alan adı `pattern` (regex DEĞİL)
    }

    /// <summary>Geçersiz dosya bir HATA DEĞİL bir SONUÇTUR: kullanıcı yanlış dosyayı seçmiş olabilir.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    public void An_unreadable_file_parses_to_nothing_instead_of_throwing(string json) =>
        Assert.Null(SettingsFile.TryParse(json));

    /// <summary>[§2.9] Import'un geri bildirimi katman sayısını ve (varsa) kökü söyler.</summary>
    [Fact]
    public void The_import_feedback_counts_the_layers_and_mentions_the_root()
    {
        var withRoot = SettingsFile.From(@"D:\src\osys", [new LayerPattern(0, "^A", "Alpha")]);
        Assert.Equal("Imported — 1 layers · root set", withRoot.ImportedMessage());

        var withoutRoot = SettingsFile.From(null, []);
        Assert.Equal("Imported — 0 layers", withoutRoot.ImportedMessage());
    }

    // ---------------------------------------------------------------- taslak (saf)

    /// <summary>[§2.9] Import FORMA yükler: kök ve katmanlar taslakta değişir, canlı VM'e DOKUNULMAZ.
    /// <para>Kök dosyada yoksa mevcut kök KORUNUR — bir katman dosyası kökü sıfırlamamalıdır.</para></summary>
    [Fact]
    public void Importing_replaces_the_draft_layers_and_keeps_the_root_when_the_file_has_none()
    {
        var draft = new SettingsDraftViewModel([new LayerPattern(0, "^A", "Alpha")], @"D:\old");

        draft.LoadFrom(SettingsFile.From(@"D:\new", [new LayerPattern(0, "^X", "Xeno")]));
        Assert.Equal(@"D:\new", draft.RepositoryRoot);
        Assert.Equal(["Xeno"], draft.Layers.Select(l => l.Name));

        draft.LoadFrom(SettingsFile.From(null, [new LayerPattern(0, "^Y", "Yankee")]));
        Assert.Equal(@"D:\new", draft.RepositoryRoot);   // kök KORUNUR
        Assert.Equal(["Yankee"], draft.Layers.Select(l => l.Name));
    }

    /// <summary>[§2.9] Clear kökü ve TÜM katmanları boşaltır — ve Save'i bloklar (root zorunludur).</summary>
    [Fact]
    public void Clearing_empties_the_root_and_every_layer()
    {
        var draft = new SettingsDraftViewModel([new LayerPattern(0, "^A", "Alpha")], @"D:\src\osys");

        draft.ClearAll();

        Assert.Null(draft.RepositoryRoot);
        Assert.Empty(draft.Layers);
        Assert.False(draft.CanSave);
    }

    // ---------------------------------------------------------------- diyalog (görünüm + kablaj)

    [StaFact]
    public void Export_writes_the_picked_path_and_reports_it_in_the_footer()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;
        string? written = null;
        dialog.PickExportPath = () => @"D:\out\build-orchestrator-settings.json";
        dialog.WriteFile = (_, content) => written = content;

        Click(dialog.Export);

        Assert.NotNull(written);
        Assert.Equal("Alpha", SettingsFile.TryParse(written!)!.Layers.Single().Name);
        Assert.Equal("Exported build-orchestrator-settings.json", dialog.Feedback.Text);
        Assert.Empty(store.State.LayerPatterns);      // hiçbir şey UYGULANMADI
        Assert.Same(run.LayerPatterns, run.LayerPatterns);
    }

    [StaFact]
    public void Import_fills_the_form_and_applies_nothing()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.PickImportPath = () => @"D:\in\settings.json";
        dialog.ReadFile = _ => SettingsFile.From(@"D:\imported", [new LayerPattern(0, "^X", "Xeno")]).ToJson();

        Click(dialog.Import);

        Assert.Equal(@"D:\imported", dialog.Draft!.RepositoryRoot);
        Assert.Equal(["Xeno"], dialog.Draft!.Layers.Select(l => l.Name));
        Assert.Equal("Imported — 1 layers · root set", dialog.Feedback.Text);
        Assert.Equal(@"D:\repo", run.RootPath);       // canlı kök DOKUNULMADI
        Assert.Empty(store.State.LayerPatterns);      // diske yazılmadı
    }

    [StaFact]
    public void An_invalid_file_says_so_and_leaves_the_form_alone()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;
        dialog.PickImportPath = () => @"D:\in\whatever.json";
        dialog.ReadFile = _ => "definitely not json";

        Click(dialog.Import);

        Assert.Equal("Invalid settings file", dialog.Feedback.Text);
        Assert.Equal(["Alpha"], dialog.Draft!.Layers.Select(l => l.Name)); // form DOKUNULMADI
    }

    /// <summary>[§2.9] Clear İKİ AŞAMALIDIR: ilk tık yalnız uyarır (ve ikonu kırmızıya çevirir), ikinci tık
    /// boşaltır. Ayrı bir onay dialogu YOKTUR.</summary>
    [StaFact]
    public void Clear_asks_once_before_it_empties_the_form()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;

        Click(dialog.Clear);

        Assert.True(dialog.IsClearArmed);
        Assert.Equal("Click again to clear root and all layers", dialog.Feedback.Text);
        Assert.Equal(["Alpha"], dialog.Draft!.Layers.Select(l => l.Name)); // HENÜZ boşalmadı
        Assert.Same(dialog.FindResource("Brush.StatusFailText"), dialog.ClearIcon.Stroke);

        Click(dialog.Clear);

        Assert.False(dialog.IsClearArmed);
        Assert.Empty(dialog.Draft!.Layers);
        Assert.Null(dialog.Draft!.RepositoryRoot);
        Assert.Equal("Cleared — nothing is applied until you save", dialog.Feedback.Text);
    }

    /// <summary>Başka bir eyleme geçmek Clear'ın kurulu onayını DÜŞÜRÜR — kullanıcı fikrini değiştirmiştir.</summary>
    [StaFact]
    public void Another_action_disarms_a_pending_clear()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        Click(dialog.Clear);
        Assert.True(dialog.IsClearArmed);

        Click(dialog.SampleLayers);

        Assert.False(dialog.IsClearArmed);
        Assert.NotEmpty(dialog.Draft!.Layers); // ...ve örnekler yüklendi (Clear DEĞİL)
    }

    /// <summary>[design v1.10.0 §2.4] First run kısayolu: diyalog açılır ve dosya seçici HEMEN tetiklenir.</summary>
    [StaFact]
    public void Opening_for_import_triggers_the_file_picker_at_once()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        int picks = 0;
        dialog.PickImportPath = () => { picks++; return null; };

        dialog.OpenForImport(run, store, () => null);

        Assert.Equal(1, picks);
        Assert.Equal(Visibility.Visible, dialog.Visibility);
    }
}
