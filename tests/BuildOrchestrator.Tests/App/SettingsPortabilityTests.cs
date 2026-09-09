using System.Windows;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
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

    // ---------------------------------------------------------------- [K5 · design v1.14.0 §9] EXTERNAL PROJECTS

    /// <summary>[K5] <c>externalProjects: [{ path, vcs }]</c> gidiş-dönüşte korunur; sıra dizinin KENDİ sırasıdır
    /// (Layer'ın deseniyle AYNI — ayrı bir <c>order</c> alanı yazılmaz).</summary>
    [Fact]
    public void The_settings_file_round_trips_the_external_projects_in_order()
    {
        IReadOnlyList<ExternalProjectRef> externals =
            [new ExternalProjectRef(@"C:\a", VcsKind.Git), new ExternalProjectRef(@"D:\shared\b.csproj", VcsKind.Tfvc)];

        string json = SettingsFile.From(@"D:\src\osys", [], externals).ToJson();
        var parsed = SettingsFile.TryParse(json);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.ExternalProjects);
        Assert.Equal([@"C:\a", @"D:\shared\b.csproj"], parsed.ExternalProjects!.Select(e => e.Path));
        Assert.Equal(["git", "tfvc"], parsed.ExternalProjects!.Select(e => e.Vcs));
        Assert.Contains("\"externalProjects\"", json, StringComparison.Ordinal);

        // [review fix — Bulgu 1] Alan SIRASI da brief'in pinlediği yerdir: "repositoryRoot ile layers ARASINA
        // externalProjects". System.Text.Json alanları BİLDİRİM sırasıyla yazar — bu yüzden sıra, dosyanın
        // gerçek şeklinin bir PARÇASIdır (yalnız anahtarın VAR OLMASI değil).
        int rootIndex = json.IndexOf("\"repositoryRoot\"", StringComparison.Ordinal);
        int externalIndex = json.IndexOf("\"externalProjects\"", StringComparison.Ordinal);
        int layersIndex = json.IndexOf("\"layers\"", StringComparison.Ordinal);
        Assert.True(rootIndex < externalIndex,
            $"externalProjects ({externalIndex}) repositoryRoot'tan ({rootIndex}) SONRA gelmeli");
        Assert.True(externalIndex < layersIndex,
            $"externalProjects ({externalIndex}) layers'tan ({layersIndex}) ÖNCE gelmeli");
    }

    /// <summary>[K5, §9] "externalProjects dizisi nesne YA DA düz string olabilir" — her eleman yalnız bir yol
    /// (string) olduğunda da (eksik <c>vcs</c>) dosya GEÇERLİ sayılır ve <c>vcs</c> "git"e düşer.</summary>
    [Fact]
    public void The_settings_file_accepts_a_plain_string_array_for_external_projects()
    {
        const string json = """
            { "externalProjects": ["C:\\a", "D:\\shared\\b.csproj"] }
            """;

        var parsed = SettingsFile.TryParse(json);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.ExternalProjects);
        Assert.Equal([@"C:\a", @"D:\shared\b.csproj"], parsed.ExternalProjects!.Select(e => e.Path));
        Assert.All(parsed.ExternalProjects!, e => Assert.Equal("git", e.Vcs)); // eksik vcs → git
    }

    /// <summary>[K5, §9] "eksik/bilinmeyen vcs → git" — yalnız tam olarak <c>"tfvc"</c> Tfvc'ye çözülür.</summary>
    [Fact]
    public void An_unknown_or_missing_vcs_value_normalizes_to_git()
    {
        const string json = """
            { "externalProjects": [
                { "path": "C:\\a" },
                { "path": "C:\\b", "vcs": "svn" },
                { "path": "C:\\c", "vcs": "TFVC" },
                { "path": "C:\\d", "vcs": "tfvc" }
            ] }
            """;

        var parsed = SettingsFile.TryParse(json);

        Assert.Equal(["git", "git", "git", "tfvc"], parsed!.ExternalProjects!.Select(e => e.Vcs));
    }

    /// <summary>[K5, §9] "boş path'ler düşer" — nesne biçimindeki boş/yalnız-boşluk path'ler VE düz-string
    /// biçimindeki boş elemanlar aynı kuralla elenir.</summary>
    [Fact]
    public void Blank_external_project_paths_are_dropped_on_import()
    {
        const string json = """
            { "externalProjects": [
                { "path": "C:\\a", "vcs": "git" },
                { "path": "   ", "vcs": "git" },
                "C:\\b",
                ""
            ] }
            """;

        var parsed = SettingsFile.TryParse(json);

        Assert.Equal([@"C:\a", @"C:\b"], parsed!.ExternalProjects!.Select(e => e.Path));
    }

    /// <summary>[K5, §9] Import geri bildirimi: <c>· N external</c> parçası YALNIZ dosya <c>externalProjects</c>
    /// anahtarını TAŞIYORSA eklenir — boş bir dizi DAHİL (anahtarın kendisi bir karardır); anahtar hiç yoksa
    /// (eski/yalnız-katman dosyası) mesaj eskisiyle BİREBİR aynı kalır.</summary>
    [Fact]
    public void The_import_feedback_mentions_external_projects_only_when_the_file_carries_the_key()
    {
        var withExternals = SettingsFile.From(@"D:\src\osys", [], [new ExternalProjectRef(@"C:\a", VcsKind.Git)]);
        Assert.Equal("Imported — 0 layers · 1 external · root set", withExternals.ImportedMessage());

        var withoutKey = SettingsFile.From(@"D:\src\osys", []); // externals parametresiz → anahtar YOK
        Assert.Equal("Imported — 0 layers · root set", withoutKey.ImportedMessage());

        var withEmptyKey = SettingsFile.From(@"D:\src\osys", [], []); // anahtar VAR ama dizi boş
        Assert.Equal("Imported — 0 layers · 0 external · root set", withEmptyKey.ImportedMessage());
    }

    /// <summary>[K5, §9] "Dosyada anahtar HİÇ yoksa mevcut harici liste KORUNUR" — kökün kendi kuralıyla AYNI
    /// ilke, ayrı bir davranış (bir katman-only dosya harici listeyi SIFIRLAMAMALIDIR).</summary>
    [Fact]
    public void Importing_a_layer_only_file_keeps_the_existing_external_projects()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\old", [new ExternalProjectRef(@"C:\kept", VcsKind.Git)]);

        draft.LoadFrom(SettingsFile.From(@"D:\new", [new LayerPattern(0, "^X", "Xeno")]));

        Assert.Equal([@"C:\kept"], draft.Externals.Select(e => e.Path));
    }

    /// <summary>[K5, §9] Anahtar VARSA (boş dizi DAHİL) taslak o listeYLE DEĞİŞTİRİLİR — "korunur" kuralı
    /// SADECE anahtar yokken geçerlidir, boş bir dizi de bir karardır.</summary>
    [Fact]
    public void Importing_a_file_with_an_empty_external_projects_array_clears_the_draft_list()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\old", [new ExternalProjectRef(@"C:\kept", VcsKind.Git)]);

        draft.LoadFrom(SettingsFile.From(@"D:\new", [], []));

        Assert.Empty(draft.Externals);
    }

    [Fact]
    public void Importing_replaces_the_draft_external_projects_when_the_file_carries_them()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\old");

        draft.LoadFrom(SettingsFile.From(@"D:\new", [], [new ExternalProjectRef(@"C:\new", VcsKind.Tfvc)]));

        var row = Assert.Single(draft.Externals);
        Assert.Equal(@"C:\new", row.Path);
        Assert.Equal(VcsKind.Tfvc, row.Vcs);
    }

    /// <summary>[K5, §9] "Load sample layers harici listeye DOKUNMAZ" — örnekler yalnız katmanları doldurur.</summary>
    [StaFact]
    public void Load_sample_layers_does_not_touch_the_external_projects()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.Draft!.AddExternal();
        dialog.Draft!.Externals[0].Path = @"C:\a";

        Click(dialog.SampleLayers);

        Assert.Equal([@"C:\a"], dialog.Draft!.Externals.Select(e => e.Path));
    }

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

    /// <summary>[§2.9 · K5] Clear kökü, TÜM katmanları VE TÜM harici projeleri boşaltır — ve Save'i bloklar
    /// (root zorunludur).</summary>
    [Fact]
    public void Clearing_empties_the_root_every_layer_and_every_external_project()
    {
        var draft = new SettingsDraftViewModel(
            [new LayerPattern(0, "^A", "Alpha")], @"D:\src\osys", [new ExternalProjectRef(@"C:\a", VcsKind.Git)]);

        draft.ClearAll();

        Assert.Null(draft.RepositoryRoot);
        Assert.Empty(draft.Layers);
        Assert.Empty(draft.Externals);
        Assert.False(draft.CanSave);
    }

    // ---------------------------------------------------------------- diyalog (görünüm + kablaj)

    /// <summary>[K5, §9] Export'un <c>externalProjects</c> alanı: yalnız boş olmayan path'ler, <c>vcs</c>
    /// normalize edilmiş — ve Export burada da yalnız FORMU okur (hiçbir şey uygulanmaz).</summary>
    [StaFact]
    public void Export_includes_the_external_projects_in_the_written_file()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        string? written = null;
        dialog.PickExportPath = () => @"D:\out\build-orchestrator-settings.json";
        dialog.WriteFile = (_, content) => written = content;

        dialog.Draft!.AddExternal();
        dialog.Draft!.Externals[0].Path = @"C:\a";
        dialog.Draft!.Externals[0].Vcs = VcsKind.Tfvc;
        dialog.Draft!.AddExternal(); // boş kalan ikinci kart — Export'un güvenlik ağı bunu düşürür

        Click(dialog.Export);

        Assert.NotNull(written);
        var parsed = SettingsFile.TryParse(written!)!;
        var ext = Assert.Single(parsed.ExternalProjects!);
        Assert.Equal(@"C:\a", ext.Path);
        Assert.Equal("tfvc", ext.Vcs);
        Assert.Empty(store.State.ExternalProjects); // hiçbir şey UYGULANMADI
        Assert.Empty(run.ExternalProjects);
    }

    /// <summary>[K5, §9] Import FORMA yükler: harici liste taslakta değişir, canlı VM'e DOKUNULMAZ — Layers'ın
    /// <see cref="Import_fills_the_form_and_applies_nothing"/> testiyle AYNI ilke.</summary>
    [StaFact]
    public void Import_fills_the_external_projects_into_the_form_and_applies_nothing()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.PickImportPath = () => @"D:\in\settings.json";
        dialog.ReadFile = _ => SettingsFile.From(@"D:\imported", [], [new ExternalProjectRef(@"C:\a", VcsKind.Git)]).ToJson();

        Click(dialog.Import);

        var row = Assert.Single(dialog.Draft!.Externals);
        Assert.Equal(@"C:\a", row.Path);
        Assert.Equal("Imported — 0 layers · 1 external · root set", dialog.Feedback.Text);
        Assert.Empty(run.ExternalProjects);           // canlı liste DOKUNULMADI
        Assert.Empty(store.State.ExternalProjects);   // diske yazılmadı
    }

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
        // [DEĞİŞEN KURAL — design v1.13.1] Eski metin gerçek dosya adını taşıyordu ("Exported
        // build-orchestrator-settings.json" — kullanıcının SEÇTİĞİ ad, sabit değil); yeni metin sabit ve dosya
        // adından bağımsız (bkz. SettingsFile.ExportedMessage).
        Assert.Equal("Exported — settings JSON", dialog.Feedback.Text);
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
    /// boşaltır. Ayrı bir onay dialogu YOKTUR.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.1]</b> Geri bildirim metinleri kısaldı: eski "Click again to
    /// clear root and all layers" → "Click again to clear", eski "Cleared — nothing is applied until you save"
    /// → "Cleared — save to apply". <b>Armed tooltip artık AYNI kısa metni taşıyor</b> — eskiden ikonun
    /// tooltip'i armed durumdan hiç ETKİLENMİYORDU (sabit "Clear settings" kalıyordu, iki tık arasında da);
    /// şimdi ilk tıkta footer'la AYNI cümleye döner ve ikinci tık/disarm'da TABANA geri döner.</para>
    /// <para>[K5] İkinci tık harici proje listesini de boşaltır (§9: "kök + katmanlar + harici projeler hepsi
    /// boşalır").</para></summary>
    [StaFact]
    public void Clear_asks_once_before_it_empties_the_form()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r =>
        {
            r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")];
            r.ExternalProjects = [new ExternalProjectRef(@"C:\a", VcsKind.Git)];
        });
        using var _scope = scope;
        object? baseTooltip = dialog.Clear.ToolTip; // armed/disarmed karşılaştırması için ÖNCEDEN oku

        Click(dialog.Clear);

        Assert.True(dialog.IsClearArmed);
        Assert.Equal("Click again to clear", dialog.Feedback.Text);
        Assert.Equal("Click again to clear", dialog.Clear.ToolTip); // armed tooltip = footer ile AYNI metin
        Assert.Equal(["Alpha"], dialog.Draft!.Layers.Select(l => l.Name)); // HENÜZ boşalmadı
        Assert.Equal([@"C:\a"], dialog.Draft!.Externals.Select(e => e.Path)); // harici liste de HENÜZ boşalmadı
        Assert.Same(dialog.FindResource("Brush.StatusFailText"), dialog.ClearIcon.Stroke);

        Click(dialog.Clear);

        Assert.False(dialog.IsClearArmed);
        Assert.Empty(dialog.Draft!.Layers);
        Assert.Empty(dialog.Draft!.Externals);
        Assert.Null(dialog.Draft!.RepositoryRoot);
        Assert.Equal("Cleared — save to apply", dialog.Feedback.Text);
        Assert.Equal(baseTooltip, dialog.Clear.ToolTip); // tooltip tabana DÖNDÜ
    }

    /// <summary>[§2.9] İkinci tık gelmezse armed durum KENDİNİ İPTAL EDER (aynı <c>FeedbackMs</c> penceresi,
    /// footer geri bildirimiyle PAYLAŞILAN süre) — ayrı bir onay dialogu olmadığı için bu, kullanıcının fikrini
    /// değiştirip hiçbir şey yapmamasının TEK güvenlik ağıdır. Daha önce yalnız İKİNCİ TIK yolu (yukarıdaki
    /// test) pinliydi; bekleme yolu hiç ölçülmemişti.</summary>
    [StaFact]
    public async Task Clear_disarms_itself_after_the_feedback_window_elapses_without_a_second_click()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            r => r.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;
        object? baseTooltip = dialog.Clear.ToolTip;

        Click(dialog.Clear);
        Assert.True(dialog.IsClearArmed);

        DispatcherPump.PumpUntil(() => !dialog.IsClearArmed,
            TimeSpan.FromMilliseconds(SettingsDialog.FeedbackMs) + TimeSpan.FromSeconds(1));

        Assert.False(dialog.IsClearArmed);
        Assert.Equal(["Alpha"], dialog.Draft!.Layers.Select(l => l.Name)); // HİÇBİR ŞEY silinmedi — yalnız uyarı düştü
        Assert.Equal("", dialog.Feedback.Text);          // geri bildirim de temizlendi
        Assert.Equal(baseTooltip, dialog.Clear.ToolTip);  // tooltip tabana DÖNDÜ
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
