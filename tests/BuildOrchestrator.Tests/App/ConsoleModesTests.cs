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

    // ---------------------------------------------------------------- [D4/Solution B] reseed flicker: senkron doc-set

    [StaFact] // [D4 review §3] gerçek adıyla: SeedProjectDocument'ın SENKRON doküman-set'ini doğrular (tam
              // orchestration/guard dizisi ConsoleForwardWiringTests seam testlerinde ayrıca sürülür).
    public async Task SeedProjectDocument_swaps_the_body_synchronously_in_the_same_ui_turn_as_the_header()
    {
        // [D4 Step 1] Mod değişiminde konsol dokümanı TIKLAMA ANINDA (senkron, pump'a bağlı DEĞİL) kurulur —
        // başlık ile gövde AYNI UI turunda değişir. Kanıt: pump HİÇ tick atmayan bir batcher. Eski (pump-apply)
        // reseed yolunda apply yalnız pump sentinel'e uğrayınca çağrılırdı → gövde eski içeriği gösterirdi (RED);
        // Solution B'de doküman senkron kurulur → gövde eski içeriği ARTIK göstermez (GREEN).
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var batcher = new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)); // pump asla ilerlemez
        var vm = new RunViewModel(engine, batcher, () => "r1");
        var view = new ConsoleView();
        var header = new ConsoleHeader();

        // Önceki anlatı/run içeriği + gövdeye senkron kur.
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));
        view.ShowRunDocument(vm.GetRunDocumentText());
        const string previousRunText = "Sync complete — 7 changed projects";
        Assert.Contains(previousRunText, view.Document.Text);

        // Bir kart seçilmiş gibi: canlı log tamponlanır ve dikişle ActiveProjectId kurulur (kart-tıklaması yolu).
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 2, "Restored A.csproj"));
        vm.SelectProject(projectId); // [D4 review §2] üretimde seçim load'dan önce kurulur (proje modu koşulu)
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // Tıklama anı: başlık + gövde SENKRON proje-loguna geçer (pump beklenmeden).
        header.ShowProjectLog("A", ProjectRowState.Started, hasDepIssue: false, vm.GetActiveLineCount());
        vm.SeedProjectDocument(projectId, text =>
            view.PlayCascade(text.Length == 0 ? [] : text.TrimEnd('\n').Split('\n'), buildInProgress: true));

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.DoesNotContain(previousRunText, view.Document.Text, StringComparison.Ordinal);
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
