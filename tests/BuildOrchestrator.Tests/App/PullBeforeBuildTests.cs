using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.15.0 §2.9] EXTERNAL PROJECTS bölümünün başlık satırındaki <c>Pull before build</c> switch'i:
/// harici çalışma kopyaları her build'den ÖNCE güncellensin mi. Bölüme ait bir KURAL olduğu için kartların
/// yanında değil başlıkta durur; alt bara (per-run seçimler) konulmaz.
///
/// <para>Dialog kuralı korunur: <b>Save'e kadar hiçbir şey uygulanmaz</b>. Varsayılan AÇIK — bayrak öncesi
/// kaydedilmiş bir kurulum bugünkü davranışı sürdürür.</para>
/// </summary>
public class PullBeforeBuildTests
{
    private static readonly ExternalProject Mail = new(@"C:\src\shared\Delta.Common", VcsKind.Git);

    private static SettingsDialogHost.FakeStore NewStore() => new();

    // ---------------------------------------------------------------- taslak

    [Fact]
    public void The_switch_starts_on()
        => Assert.True(new SettingsDraftViewModel(null, @"D:\repo").PullExternalsBeforeBuild);

    [Fact]
    public void The_draft_starts_from_the_live_value_so_cancel_can_discard_it()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", [Mail], pullExternalsBeforeBuild: false);

        Assert.False(draft.PullExternalsBeforeBuild);
    }

    [Fact]
    public void Clear_returns_the_switch_to_its_default_rather_than_turning_it_off()
    {
        // §2.9: Clear kökü ve iki listeyi boşaltır; switch VARSAYILANINA döner (kapanmaz).
        var draft = new SettingsDraftViewModel(null, @"D:\repo", [Mail], pullExternalsBeforeBuild: false);

        draft.ClearAll();

        Assert.True(draft.PullExternalsBeforeBuild);
    }

    // ---------------------------------------------------------------- dosya biçimi

    [Fact]
    public void Export_writes_the_flag()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", [Mail], pullExternalsBeforeBuild: false);

        Assert.False(draft.ToFile().PullExternalBeforeBuild);
        Assert.Contains("\"pullExternalBeforeBuild\": false", draft.ToFile().ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_loads_the_flag_into_the_form()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");

        draft.LoadFrom(SettingsFile.From(@"D:\repo", [], [Mail], pullExternalBeforeBuild: false));

        Assert.False(draft.PullExternalsBeforeBuild);
    }

    [Fact]
    public void A_file_without_the_flag_leaves_the_form_untouched()
    {
        // Eski/yalnız-katman dosyası taşımadığı bir ayarı sıfırlamamalı — harici liste kuralının aynısı.
        var draft = new SettingsDraftViewModel(null, @"D:\repo", [Mail], pullExternalsBeforeBuild: false);

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.False(draft.PullExternalsBeforeBuild);
    }

    // ---------------------------------------------------------------- Save

    [Fact]
    public async Task Save_persists_the_flag_and_applies_it_in_the_same_commit()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = NewStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo", [Mail]) { PullExternalsBeforeBuild = false };

        await draft.CommitAsync(run, store);

        Assert.False(run.UpdateExternals);
        Assert.False(store.State.UpdateExternals);
    }

    [Fact]
    public async Task Turning_it_off_notes_it_on_the_console()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [Mail], pullExternalsBeforeBuild: false);
        Assert.Contains("Pull before build off — external working copies are used as they are",
            run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [Mail], pullExternalsBeforeBuild: true);
        Assert.Contains("Pull before build on — external working copies update first",
            run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unchanged_switch_stays_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [Mail], pullExternalsBeforeBuild: true);

        Assert.DoesNotContain("Pull before build", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workspace_without_external_projects_stays_quiet_even_when_the_switch_changes()
    {
        // Harici projesi olmayan kurulumda bayrak hiçbir şey yapmaz; orada not yazmak olmayan bir işi anlatırdı.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], pullExternalsBeforeBuild: false);

        Assert.False(run.UpdateExternals);                              // uygulanır
        Assert.DoesNotContain("Pull before build", run.GetRunDocumentText(), StringComparison.Ordinal); // ama sessiz
    }

    // ---------------------------------------------------------------- görünüm

    [StaFact]
    [Trait("Category", "Wpf")]
    public void The_section_header_carries_the_caps_label_the_switch_and_its_tooltip()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var texts = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("EXTERNAL PROJECTS", texts);
        Assert.Contains("PULL BEFORE BUILD", texts);

        var toggle = DsResources.RealizedObjects(dialog).OfType<CheckBox>()
            .Single(c => c.Name == "PullExternalsSwitch");
        Assert.Equal(dialog.FindResource("Ds.Switch"), toggle.Style);
        Assert.True(toggle.IsChecked);                                  // varsayılan AÇIK
        Assert.Equal(AccessibilityNames.PullExternalsBeforeBuild,
            System.Windows.Automation.AutomationProperties.GetName(toggle));
    }

    [StaFact]
    [Trait("Category", "Wpf")]
    public void The_switch_reflects_the_live_value_when_the_dialog_opens()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run => run.UpdateExternals = false);
        using var _scope = scope;

        var toggle = DsResources.RealizedObjects(dialog).OfType<CheckBox>()
            .Single(c => c.Name == "PullExternalsSwitch");
        Assert.False(toggle.IsChecked);
    }
}
