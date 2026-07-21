using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3a] Konsol modları (design-v1 §2.5): başlık anlatı↔proje-log geçişi + "N lines" TAM tampon sayacı +
/// boş-durum metinleri (birebir/verbatim). Header kod-tarafı sürülür (küçük test edilebilir yüzey).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleModesTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // ---------------------------------------------------------------- başlık modları

    [StaFact]
    public void Header_narrative_mode_shows_caps_label_and_hides_back_and_project_bits()
    {
        var header = new ConsoleHeader();

        header.ShowNarrative(12);

        Assert.Equal(ConsoleHeader.HeaderMode.Narrative, header.Mode);
        Assert.Equal(Visibility.Visible, header.ConsoleLabel.Visibility);
        Assert.Equal(Visibility.Collapsed, header.BackButton.Visibility);
        Assert.Equal(Visibility.Collapsed, header.ProjectNameText.Visibility);
        Assert.Equal(Visibility.Collapsed, header.StatusNameText.Visibility);
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        Assert.Equal("12 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_project_log_mode_shows_back_name_status_and_dep_badge()
    {
        var header = new ConsoleHeader();

        header.ShowProjectLog("OSYS.Sales.Core", ProjectRowState.Failed, hasDepIssue: true, lineCount: 87);

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.Equal(Visibility.Collapsed, header.ConsoleLabel.Visibility);
        Assert.Equal(Visibility.Visible, header.BackButton.Visibility);
        Assert.Equal(Visibility.Visible, header.ProjectNameText.Visibility);
        Assert.Equal("OSYS.Sales.Core", header.ProjectNameText.Text);
        Assert.Equal("Failed", header.StatusNameText.Text);
        Assert.Equal(Visibility.Visible, header.DepIssueBadge.Visibility);
        Assert.Equal("87 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_project_log_without_dep_issue_hides_the_badge_and_switches_back_to_narrative()
    {
        var header = new ConsoleHeader();

        header.ShowProjectLog("OSYS.Base", ProjectRowState.Succeeded, hasDepIssue: false, lineCount: 5);
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        Assert.Equal("Succeeded", header.StatusNameText.Text);

        header.ShowNarrative(3); // geri dönüş moddu tekrar anlatıya çevirir
        Assert.Equal(ConsoleHeader.HeaderMode.Narrative, header.Mode);
        Assert.Equal(Visibility.Collapsed, header.BackButton.Visibility);
        Assert.Equal("3 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_back_button_raises_BackRequested()
    {
        var header = new ConsoleHeader();
        header.ShowProjectLog("OSYS.Base", ProjectRowState.Succeeded, hasDepIssue: false, lineCount: 0);
        bool raised = false;
        header.BackRequested += (_, _) => raised = true;

        header.BackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(raised);
    }

    [StaFact]
    public void SetLineCount_updates_only_the_counter_text()
    {
        var header = new ConsoleHeader();
        header.ShowNarrative(0);

        header.SetLineCount(1440);

        Assert.Equal("1440 lines", header.LinesText.Text);
    }

    // ---------------------------------------------------------------- boş-durum metinleri (verbatim §2.5)

    [Fact]
    public void Empty_state_texts_are_verbatim_design_v1()
    {
        Assert.Equal(
            "Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (a3f81c2)",
            ConsoleEmptyState.Skipped("a3f81c2"));
        Assert.Equal(
            "Queued — waiting for dependencies: Sales.Core, Security",
            ConsoleEmptyState.Queued(["Sales.Core", "Security"]));
        Assert.Equal(
            "No log yet — output streams here once the build starts.",
            ConsoleEmptyState.NoLog);
    }

    // ---------------------------------------------------------------- N lines = TAM tampon (Ek A #23)

    [Fact]
    public async Task GetActiveLineCount_reflects_full_run_buffer_length_line_for_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal(0, vm.GetActiveLineCount());

        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 2, "Restored a.csproj"));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 3, "Build succeeded"));

        // ActiveProjectId null (run modu) → aktif tampon = run dokümanı; sayaç satır satır artar.
        Assert.Equal(3, vm.GetActiveLineCount());
    }
}
