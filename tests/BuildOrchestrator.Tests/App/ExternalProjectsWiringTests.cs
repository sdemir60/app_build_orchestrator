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
/// Harici proje listesinin App'ten motora taşınması: hem Sync hem Build komutu listeyi TAŞIR; boş liste tel
/// üzerine <c>null</c> olarak çıkar (özellik kapalı — eski NDJSON şekli bayt-bayt aynı); ve Ayarlar'da Save'e
/// basıldığında liste Sync'ten ÖNCE atanır — ters sırada komut ESKİ listeyle giderdi. Konsol notları
/// <c>SettingsDialogTests</c>'te pinlidir.
/// </summary>
public class ExternalProjectsWiringTests
{
    private static readonly ExternalProject Mail = new(@"D:\ext\mail");
    private static readonly ExternalProject Ocr = new(@"D:\ext\ocr");

    private static IReadOnlyList<ExternalProject> Externals(params ExternalProject[] items) => items;

    private static RunViewModel NewVm(EngineHost engine) =>
        new(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    [Fact]
    public async Task The_sync_command_carries_the_external_list()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
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
        var run = NewVm(engine);
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);
        run.ExternalProjects = externals;

        await run.BuildCommand.ExecuteAsync(null);

        Assert.Same(externals, Assert.Single(sent.OfType<StartRunCommand>()).ExternalProjects);
    }

    [Fact]
    public async Task An_empty_list_is_sent_as_null_so_the_wire_shape_stays_as_it_was()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.SyncCommand.ExecuteAsync(null);
        await run.BuildCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).ExternalProjects);
        Assert.Null(Assert.Single(sent.OfType<StartRunCommand>()).ExternalProjects);
    }

    [Fact]
    public async Task The_run_command_asks_for_an_update_by_default()
    {
        // Varsayılan AÇIK: kullanıcı kapatmadıkça her Build harici çalışma kopyalarını günceller.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        run.ExternalProjects = Externals(Mail);

        await run.BuildCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(sent.OfType<StartRunCommand>()).UpdateExternals);
    }

    [Fact]
    public async Task Turning_the_update_off_is_carried_to_the_engine()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        run.ExternalProjects = Externals(Mail);
        run.UpdateExternals = false;

        await run.BuildCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(sent.OfType<StartRunCommand>()).UpdateExternals);
    }

    [Fact]
    public async Task Saving_settings_applies_the_list_before_the_single_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);

        await run.ApplySettingsAsync([], @"D:\repo", externals);

        // SIRA kanıtı: komut TAZE listeyi taşıdı.
        Assert.Same(externals, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).ExternalProjects);
        Assert.Same(externals, run.ExternalProjects);
    }

    [Fact]
    public async Task A_save_during_a_run_still_applies_the_list_but_defers_the_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = NewVm(engine);
        run.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0, null));
        Assert.True(run.IsMidRunLocked);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;
        var externals = Externals(Mail);

        await run.ApplySettingsAsync([], @"D:\repo", externals);

        Assert.Same(externals, run.ExternalProjects);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
    }
}
