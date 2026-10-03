using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.19.0 §2.9] Settings → <b>General</b> sayfası: katalogdan (<see cref="GeneralSettingsCatalog"/>) doğan
/// gruplar ve tek <c>ToggleRow</c> şablonu; <c>Pull before build</c>'in buraya taşınması ve External projects sayfasının
/// altındaki "nereye gitti" satırı.
///
/// <para>Altı anahtarın hepsi gerçektir: <c>Pull before build</c> (<see cref="PullBeforeBuildTests"/>), <c>Stash and
/// switch branches</c> (<see cref="StashOnBranchSwitchTests"/>), <c>Start with Windows</c> ve <c>Start minimized to
/// tray</c> (<see cref="StartWithWindowsTests"/>), <c>Close to tray</c> ve <c>Show notifications</c>
/// (<see cref="ShellSwitchesTests"/>; davranışları <see cref="CloseToTrayTests"/> ve tepsi balonu testleri).</para>
/// <para><b>[DEĞİŞEN KURAL — P3 2026-09-28 · P4 2026-09-29, kullanıcı kararları]</b> ESKİ (kullanıcı kararı 1): dört
/// kabuk anahtarı (<c>Start with Windows</c>, <c>Start minimized to tray</c>, <c>Close to tray</c>, <c>Show
/// notifications</c>) YALNIZ taslakta yaşardı — kaydedilmez, dosyaya yazılmaz, her açılışta varsayılana dönerdi. Artık
/// dördü de kalıcıdır ve davranışa bağlıdır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SettingsGeneralPageTests
{
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    /// <summary>General sayfasındaki ToggleRow kökleri, yukarıdan aşağıya.</summary>
    private static List<Border> Rows(SettingsDialog dialog)
    {
        var page = dialog.Page(SettingsSection.General);
        var style = dialog.FindResource("Ds.Settings.ToggleRow");
        return [.. DsResources.Descendants(page).OfType<Border>().Where(b => ReferenceEquals(b.Style, style))
            .OrderBy(b => b.TranslatePoint(new Point(0, 0), page).Y)];
    }

    private static CheckBox SwitchOf(Border row) => DsResources.Descendants(row).OfType<CheckBox>().Single();

    private static CheckBox SwitchNamed(SettingsDialog dialog, string name) =>
        Rows(dialog).Select(SwitchOf).Single(s => AutomationProperties.GetName(s) == name);

    private static Border RowNamed(SettingsDialog dialog, string label) =>
        Rows(dialog).Single(r => DsResources.Descendants(r).OfType<TextBlock>().Any(t => t.Text == label));

    // ---------------------------------------------------------------- katalog

    /// <summary>Gruplar ve satırlar BİREBİR (git-only uyarlanmış pull açıklaması dahil); yeni ayar = kataloğa bir satır.
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §6.3]</b> Eski iddia (<c>The_catalog_carries_the_three_groups_and_their_rows_verbatim</c>):
    /// katalog üç gruptu (STARTUP, BUILD, NOTIFICATIONS). Branch chip'i checkout edince kirli ağaç kararı bir ayar
    /// oldu: BUILD'in ardına dördüncü grup BRANCHES ve tek satırı <c>Stash and switch branches</c> eklendi.</para></summary>
    [Fact]
    public void The_catalog_carries_the_groups_and_their_rows_verbatim()
    {
        var actual = GeneralSettingsCatalog.Groups
            .Select(g => (g.Title, Rows: g.Rows.Select(r => (r.Label, r.Description)).ToList())).ToList();

        Assert.Equal(["STARTUP", "BUILD", "BRANCHES", "NOTIFICATIONS"], actual.Select(g => g.Title));
        Assert.Equal(
        [
            ("Start with Windows", "Launch when you sign in to Windows."),
            ("Start minimized to tray", "No window on start — the tray icon brings it back."),
            ("Close to tray", "Closing the window leaves the engine running in the tray."),
        ], actual[0].Rows);
        Assert.Equal(
            [("Pull before build", "Update every external working copy first — a fast-forward-only git pull, one per copy.")],
            actual[1].Rows);
        Assert.Equal(
            [("Stash and switch branches",
                "When the working tree has uncommitted changes, stash them (including untracked files) and switch. "
                + "Off: switching stops and asks you to commit or stash first.")],
            actual[2].Rows);
        Assert.Equal(
            [("Show notifications", "Tray notifications: a build's result, the first close to the tray, a Build shortcut that could not start, "
                + "and a second launch that could not bring the window forward.")],
            actual[3].Rows);
    }

    /// <summary>Taslak satırları katalogdan doğar (sıra ve metin kopyalanmaz); dört yeni anahtarın varsayılanı
    /// <c>off/off/on/on</c>, pull canlı değerden gelir. Yalnız <c>Start minimized to tray</c> bağımlıdır.</summary>
    [Fact]
    public void The_draft_builds_its_rows_from_the_catalog_with_the_defaults()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", pullExternalsBeforeBuild: false);

        Assert.Equal(GeneralSettingsCatalog.Groups.Select(g => g.Title), draft.GeneralGroups.Select(g => g.Title));
        Assert.Equal(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows),
            draft.GeneralGroups.SelectMany(g => g.Rows).Select(r => r.Definition));

        Assert.False(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.False(draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
        Assert.True(draft.GeneralRow(GeneralSetting.CloseToTray).IsOn);
        Assert.True(draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn);
        Assert.False(draft.GeneralRow(GeneralSetting.PullBeforeBuild).IsOn); // canlı değer

        Assert.Equal([GeneralSetting.StartMinimizedToTray],
            GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows).Where(r => r.DependsOn is not null).Select(r => r.Setting));
    }

    /// <summary>Pull satırı taslağın GERÇEK bayrağıdır: iki yüz aynı değeri okur ve yazar.</summary>
    [Fact]
    public void The_pull_row_and_the_draft_flag_are_one_value()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        var notified = new List<string?>();
        draft.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        draft.GeneralRow(GeneralSetting.PullBeforeBuild).IsOn = false;
        Assert.False(draft.PullExternalsBeforeBuild);
        Assert.Contains(nameof(SettingsDraftViewModel.PullExternalsBeforeBuild), notified);

        draft.PullExternalsBeforeBuild = true;
        Assert.True(draft.GeneralRow(GeneralSetting.PullBeforeBuild).IsOn);
    }

    /// <summary>Bağımlı satır üst anahtar kapalıyken etkin DEĞİLDİR; açılınca etkinleşir.</summary>
    [Fact]
    public void The_dependent_row_follows_its_parent()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        var dependent = draft.GeneralRow(GeneralSetting.StartMinimizedToTray);

        Assert.False(dependent.IsEnabled);
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        Assert.True(dependent.IsEnabled);
        Assert.All(draft.GeneralGroups.SelectMany(g => g.Rows).Where(r => r != dependent), r => Assert.True(r.IsEnabled));
    }

    /// <summary>Clear dört yeni anahtarı da (pull gibi) VARSAYILANINA döndürür — prototip parity, yalnız taslak.</summary>
    [Fact]
    public void Clear_returns_every_general_switch_to_its_default()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", pullExternalsBeforeBuild: false);
        foreach (var row in draft.GeneralGroups.SelectMany(g => g.Rows)) row.IsOn = !row.Definition.Default;

        draft.ClearAll();

        Assert.All(draft.GeneralGroups.SelectMany(g => g.Rows), r => Assert.Equal(r.Definition.Default, r.IsOn));
        Assert.True(draft.PullExternalsBeforeBuild);
    }

    /// <summary>Dört kabuk anahtarı ayar dosyasında taşınır: dördü çevrilip dışa aktarılan dosya yeni bir taslağa
    /// yüklenince dördü de çevrik gelir — tablonun (<see cref="ShellSwitches.All"/>) her satırı Export/Import'a bağlıdır.
    /// <para><b>[DEĞİŞEN KURAL — P3 2026-09-28 · P4 2026-09-29, kullanıcı kararları]</b> ESKİ İDDİA
    /// (<c>The_four_new_switches_are_not_written_to_the_settings_file</c>): dört anahtarın hiçbiri ayar dosyasına
    /// girmezdi — çevrilseler de export JSON'u varsayılanla aynıydı. Artık dördü de kalıcıdır; tek tek round-trip
    /// testleri <see cref="ShellSwitchesTests"/> ve <see cref="StartWithWindowsTests"/>'tedir.</para></summary>
    [Fact]
    public void The_four_shell_switches_travel_in_the_settings_file()
    {
        var flipped = new SettingsDraftViewModel(null, @"D:\repo");
        foreach (var s in ShellSwitches.All) flipped.GeneralRow(s.Setting).IsOn = !flipped.GeneralRow(s.Setting).IsOn;

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(flipped.ToFile().ToJson())!);

        Assert.All(ShellSwitches.All, s =>
            Assert.Equal(!GeneralSettingsCatalog.Definition(s.Setting).Default, loaded.GeneralRow(s.Setting).IsOn));
    }

    // ---------------------------------------------------------------- realize

    /// <summary>Dört grup: caps başlık 11px/500 <c>text-dim</c>, başlık → satırlar 5px, gruplar arası 22px; satırların
    /// sırası ve metinleri katalogla aynı; UIA adı etiket (pull: mevcut ad).
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §6.3]</b> Eski iddia (<c>The_general_page_realizes_three_groups_with_their_rows</c>):
    /// sayfa üç grup, beş satır realize ederdi. BRANCHES grubu (<c>Stash and switch branches</c>) eklendi — yeni satır
    /// aynı <c>ToggleRow</c> şablonuyla realize olur ve ölçüler onu da kapsar.</para></summary>
    [StaFact]
    public void The_general_page_realizes_its_groups_with_their_rows()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        var page = dialog.Page(SettingsSection.General);

        var headingStyle = dialog.FindResource("Ds.Settings.GroupHeading");
        var headings = DsResources.Descendants(page).OfType<TextBlock>().Where(t => ReferenceEquals(t.Style, headingStyle))
            .OrderBy(t => t.TranslatePoint(new Point(0, 0), page).Y).ToList();
        Assert.Equal(["STARTUP", "BUILD", "BRANCHES", "NOTIFICATIONS"], headings.Select(h => h.Text));
        Assert.All(headings, h =>
        {
            Assert.Equal(11.0, h.FontSize);
            Assert.Equal(FontWeights.Medium, h.FontWeight);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(h.Foreground));
        });

        var rows = Rows(dialog);
        Assert.Equal(["Start with Windows", "Start minimized to tray", "Close to tray", "Pull before build",
                "Stash and switch branches", "Show notifications"],
            rows.Select(r => DsResources.Descendants(r).OfType<TextBlock>().First().Text));
        Assert.Equal(["Start with Windows", "Start minimized to tray", "Close to tray",
                AccessibilityNames.PullExternalsBeforeBuild, "Stash and switch branches", "Show notifications"],
            rows.Select(r => AutomationProperties.GetName(SwitchOf(r))));

        // Başlık → ilk satır 5px; bir grubun son satırı → sonraki başlık 22px.
        double Top(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), page).Y;
        double Bottom(FrameworkElement e) => Top(e) + e.ActualHeight;
        Assert.Equal(5.0, Top(rows[0]) - Bottom(headings[0]), precision: 1);
        Assert.Equal(5.0, Top(rows[3]) - Bottom(headings[1]), precision: 1);
        Assert.Equal(22.0, Top(headings[1]) - Bottom(rows[2]), precision: 1);
        Assert.Equal(22.0, Top(headings[2]) - Bottom(rows[3]), precision: 1);
        Assert.Equal(5.0, Top(rows[4]) - Bottom(headings[2]), precision: 1);
        Assert.Equal(22.0, Top(headings[3]) - Bottom(rows[4]), precision: 1);
    }

    /// <summary>ToggleRow: padding <c>13 0</c>, satırlar arası 1px <c>border-subtle</c> (grubun ilk satırında yok);
    /// solda ad 13px/500 <c>text-primary</c> + 3px altında açıklama 12px <c>text-dim</c> snug, en çok 430; sağda
    /// <c>Ds.Switch</c> 20px arayla, üstten 1px.</summary>
    [StaFact]
    public void A_toggle_row_has_the_design_measures_and_no_hairline_above_the_first_row_of_a_group()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        var rows = Rows(dialog);

        var firstOfGroup = new[] { true, false, false, true, true, true };
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(new Thickness(0, 13, 0, 13), rows[i].Padding);
            Assert.Equal(new Thickness(0, firstOfGroup[i] ? 0 : 1, 0, 0), rows[i].BorderThickness);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(rows[i].BorderBrush));
        }

        var row = rows[1];
        var texts = DsResources.Descendants(row).OfType<TextBlock>().ToList();
        var label = texts.Single(t => t.Text == "Start minimized to tray");
        var description = texts.Single(t => t.Text == "No window on start — the tray icon brings it back.");
        Assert.Equal(13.0, label.FontSize);
        Assert.Equal(FontWeights.Medium, label.FontWeight);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextPrimary"), DsResources.ColorOf(label.Foreground));
        Assert.Equal(12.0, description.FontSize);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(description.Foreground));
        Assert.Equal(16.2, description.LineHeight, precision: 6);
        Assert.Equal(430.0, description.MaxWidth);
        Assert.Equal(3.0, description.TranslatePoint(new Point(0, 0), label).Y - label.ActualHeight, precision: 1);

        var toggle = SwitchOf(row);
        Assert.Same(dialog.FindResource("Ds.Switch"), toggle.Style);
        Assert.Equal(1.0, toggle.TranslatePoint(new Point(0, 0), label).Y, precision: 1); // üstten hizalı + 1
        var textColumn = (FrameworkElement)VisualTreeHelper.GetParent(label);
        double gap = toggle.TranslatePoint(new Point(0, 0), textColumn).X - textColumn.ActualWidth;
        Assert.Equal(20.0, gap, precision: 1);
        // Switch satırın sağ kenarına yaslanır.
        Assert.Equal(row.ActualWidth, toggle.TranslatePoint(new Point(toggle.ActualWidth, 0), row).X, precision: 1);
    }

    /// <summary>Switch'lerin açılış değerleri: <c>off/off/on/on</c> + pull ve stash canlı değerden.</summary>
    [StaFact]
    public void The_switches_open_on_their_defaults()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r => r.UpdateExternals = false);
        using var _scope = scope;

        Assert.Equal([false, false, true, false, false, true], Rows(dialog).Select(r => SwitchOf(r).IsChecked == true));
    }

    /// <summary>Start with Windows kapalıyken <c>Start minimized to tray</c> satırı %45 opak ve etkileşimsiz; açınca
    /// etkin. Satırın yüksekliği iki durumda aynıdır (layout sabit).</summary>
    [StaFact]
    public void Start_minimized_is_dimmed_and_inert_until_start_with_windows_is_on()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var parent = SwitchNamed(dialog, "Start with Windows");
        var row = RowNamed(dialog, "Start minimized to tray");
        var toggle = SwitchOf(row);
        var textColumn = (FrameworkElement)VisualTreeHelper.GetParent(
            DsResources.Descendants(row).OfType<TextBlock>().Single(t => t.Text == "Start minimized to tray"));
        double height = row.ActualHeight;

        Assert.False(toggle.IsEnabled);
        Assert.Equal(0.45, toggle.Opacity, precision: 6);
        Assert.Equal(0.45, textColumn.Opacity, precision: 6);
        Assert.Equal(1.0, row.Opacity);   // satırın kendisi değil, iki yüzü söner (switch iki kez sönmez)

        parent.IsChecked = true;          // kullanıcının tıklamasının bağladığı yol
        dialog.UpdateLayout();

        Assert.True(dialog.Draft!.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(toggle.IsEnabled);
        Assert.Equal(1.0, toggle.Opacity);
        Assert.Equal(1.0, textColumn.Opacity);
        Assert.Equal(height, row.ActualHeight);
    }

    /// <summary>Dört kabuk anahtarı Save'de UiState'e yazılır ve diyalog yeniden açılınca KAYITLI değerle gelir — dördü
    /// çevrilip kaydedilince yeniden açılış çevrik değerleri gösterir (pull ve stash canlı değerlerinde kalır).
    /// <para><b>[DEĞİŞEN KURAL — P3 2026-09-28 · P4 2026-09-29, kullanıcı kararları]</b> ESKİ İDDİA
    /// (<c>The_four_new_switches_are_not_saved_and_reopen_on_their_defaults</c>): dört anahtarın hiçbiri Save'de
    /// UiState'e girmezdi ve yeniden açılışta varsayılana dönerdi. Artık dördü de kalıcıdır.</para></summary>
    [StaFact]
    public void The_four_shell_switches_are_saved_and_reopen_on_the_saved_value()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        foreach (var name in new[] { "Start with Windows", "Start minimized to tray", "Close to tray", "Show notifications" })
            SwitchNamed(dialog, name).IsChecked = !SwitchNamed(dialog, name).IsChecked;

        Click(dialog.Save); // commit'in persist yarısı ilk await'ten ÖNCE, senkron yazılır
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);

        dialog.Open(run, store, () => null);
        dialog.UpdateLayout();
        Assert.Equal([true, true, false, true, false, false], Rows(dialog).Select(r => SwitchOf(r).IsChecked == true));
    }

    /// <summary>[P3] Close to tray ve Show notifications Save'de UiState'e yazılır ve yeniden açılışta KAYITLI değeri
    /// gösterir (switch'e tıklamanın bağladığı gerçek yol).</summary>
    [StaFact]
    public void Close_to_tray_and_show_notifications_are_saved_and_reopen_on_the_saved_value()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        foreach (var name in new[] { "Close to tray", "Show notifications" })
            SwitchNamed(dialog, name).IsChecked = false;

        Click(dialog.Save);
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        Assert.False(store.State.CloseToTray);
        Assert.False(store.State.ShowNotifications);

        dialog.Open(run, store, () => null);
        dialog.UpdateLayout();

        Assert.False(SwitchNamed(dialog, "Close to tray").IsChecked);
        Assert.False(SwitchNamed(dialog, "Show notifications").IsChecked);
    }

    /// <summary>[Task 1] Diyalog kayıtlı KAPALI değerle açılır — Close to tray ve Show notifications switch'leri
    /// kapalı realize olur (<see cref="ShellSwitchesTests.The_draft_opens_on_the_saved_values"/>'ın realize eşi).</summary>
    [StaFact]
    public void The_general_page_opens_on_the_saved_switch_values()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(saved: new UiState { CloseToTray = false, ShowNotifications = false });
        using var _scope = scope;

        Assert.False(SwitchNamed(dialog, "Close to tray").IsChecked);
        Assert.False(SwitchNamed(dialog, "Show notifications").IsChecked);
    }

    /// <summary>[P4] İki başlangıç anahtarı Save'de UiState'e yazılır ve diyalog yeniden açılınca kayıtlı değerle
    /// gelir (switch'e tıklamanın bağladığı gerçek yol).</summary>
    [StaFact]
    public void The_startup_switches_are_saved_and_reopen_on_the_saved_value()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        SwitchNamed(dialog, "Start with Windows").IsChecked = true;
        SwitchNamed(dialog, "Start minimized to tray").IsChecked = true;

        Click(dialog.Save);
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        Assert.True(store.State.Autostart);
        Assert.True(store.State.StartMinimizedToTray);

        dialog.Open(run, store, () => null);
        dialog.UpdateLayout();
        Assert.Equal([true, true, true, true, false, true], Rows(dialog).Select(r => SwitchOf(r).IsChecked == true));
    }

    /// <summary>[P4] Uçtan uca: switch'e tıklayıp Save → diyaloğun servisi Windows'un başlangıç kaydını ANINDA yazar.</summary>
    [StaFact]
    public void Saving_start_with_windows_on_writes_the_windows_startup_entry()
    {
        var registry = new FakeAutostartRegistry();
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(autostart: registry.Service());
        using var _scope = scope;
        SwitchNamed(dialog, "Start with Windows").IsChecked = true;

        Click(dialog.Save);

        Assert.Equal(FakeAutostartRegistry.Command, registry.CommandFor(AutostartService.DefaultValueName));
    }

    /// <summary>[P4] Görev Yöneticisi'nde kapatılmış kayıt: Start with Windows satırı kapalı açılır, açıklaması
    /// Windows'un kararını ve anahtarın ne yapacağını söyler; bağımlı satır (üst kapalı) söner.</summary>
    [StaFact]
    public void An_entry_turned_off_in_task_manager_shows_off_with_the_task_manager_note()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        registry.DisableInStartupApps(AutostartService.DefaultValueName);
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            saved: new UiState { Autostart = true }, autostart: registry.Service());
        using var _scope = scope;

        var row = RowNamed(dialog, "Start with Windows");
        Assert.False(SwitchOf(row).IsChecked);
        Assert.Contains(DsResources.Descendants(row).OfType<TextBlock>(),
            t => t.Text == "Turned off in Task Manager's Startup apps — switch it on to start with Windows again.");
        Assert.False(SwitchOf(RowNamed(dialog, "Start minimized to tray")).IsEnabled);
    }

    // ---------------------------------------------------------------- External projects

    /// <summary>External projects sayfasında switch YOK (eski başlık satırı, caps etiketi ve tooltip kalktı); altta
    /// hairline'lı tek satır: metin taslağın pull değerini CANLI söyler, ghost sm <c>Pull before build</c> düğmesi
    /// rayı General'a alır.</summary>
    [StaFact]
    public void The_external_page_has_no_switch_and_ends_with_a_line_pointing_to_general()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(r => r.ExternalProjects = [new ExternalProject(@"C:\a")]);
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.External);
        dialog.UpdateLayout();
        var page = dialog.Page(SettingsSection.External);

        Assert.Empty(DsResources.RealizedObjects(page).OfType<CheckBox>());
        Assert.DoesNotContain(DsResources.RealizedObjects(page).OfType<TextBlock>(), t => t.Text == "PULL BEFORE BUILD");

        var note = dialog.PullBeforeBuildNote;
        Assert.True(DsResources.IsSelfOrDescendantOf(note, page));
        Assert.Equal(new Thickness(0, 20, 0, 0), note.Margin);
        Assert.Equal(new Thickness(0, 14, 0, 0), note.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), note.BorderThickness);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(note.BorderBrush));
        var add = DsResources.RealizedObjects(page).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Add external project");
        Assert.True(note.TranslatePoint(new Point(0, 0), add).Y > add.ActualHeight, "satır kartlardan ve Add'den SONRA gelir");

        var summary = dialog.PullBeforeBuildSummary;
        Assert.Equal(12.0, summary.FontSize);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(summary.Foreground));
        Assert.Equal("Card order sets the order the working copies are updated. Updating them before a build is on.",
            new TextRange(summary.ContentStart, summary.ContentEnd).Text);
        dialog.Draft!.PullExternalsBeforeBuild = false;
        dialog.UpdateLayout();
        Assert.Equal("Card order sets the order the working copies are updated. Updating them before a build is off.",
            new TextRange(summary.ContentStart, summary.ContentEnd).Text);

        var link = dialog.PullBeforeBuildLink;
        Assert.Equal("Pull before build", link.Content);
        Assert.Same(dialog.FindResource("Ds.Button.Ghost.Sm"), link.Style);
        Assert.Equal(-6.0, link.Margin.Left);
        Click(link);
        dialog.UpdateLayout();
        Assert.True(dialog.RailItem(SettingsSection.General).IsChecked);
        Assert.Equal(Visibility.Visible, dialog.Page(SettingsSection.General).Visibility);
    }
}
