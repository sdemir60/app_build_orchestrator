using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Harici proje listesinin App'ten motora taşınması: hem Sync hem Build komutu listeyi TAŞIR, ve Ayarlar'da
/// Save'e basıldığında liste Sync'ten ÖNCE atanır — ters sırada komut ESKİ listeyle giderdi.
/// </summary>
public class ExternalProjectsWiringTests
{
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");
    private static readonly ExternalProject Ocr = new("Ocr", @"D:\ext\ocr", @"D:\ext\ocr\Ocr.sln");

    private static IReadOnlyList<ExternalProject> Externals(params ExternalProject[] items) => items;

    [Fact]
    public async Task The_sync_command_carries_the_external_list()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail, Ocr);
        run.ExternalProjects = externals;

        await run.SyncCommand.ExecuteAsync(null);

        Assert.Same(externals, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).ExternalProjects);
    }

    [Fact]
    public async Task The_run_command_carries_the_external_list()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);
        run.ExternalProjects = externals;

        await run.BuildCommand.ExecuteAsync(null);

        Assert.Same(externals, Assert.Single(sent.OfType<StartRunCommand>()).ExternalProjects);
    }

    [Fact]
    public async Task Saving_settings_applies_the_list_before_the_single_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);

        await run.ApplySettingsAsync([], externals, @"D:\repo");

        // SIRA kanıtı: komut TAZE listeyi taşıdı.
        Assert.Same(externals, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).ExternalProjects);
        Assert.Same(externals, run.ExternalProjects);
    }

    [Fact]
    public async Task Saving_settings_notes_the_change_on_the_console()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], Externals(Mail, Ocr), @"D:\repo");
        Assert.Contains("External projects updated — 2 external projects", run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], [], @"D:\repo");
        Assert.Contains("External projects removed", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_with_no_externals_before_or_after_stays_quiet()
    {
        // Katman-only bir Save'de harici satırı gürültü olurdu.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], [], @"D:\repo");

        Assert.DoesNotContain("External projects", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_during_a_run_still_applies_the_list_but_defers_the_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0, null));
        Assert.True(run.IsMidRunLocked);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);

        await run.ApplySettingsAsync([], externals, @"D:\repo");

        Assert.Same(externals, run.ExternalProjects);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
    }
}
