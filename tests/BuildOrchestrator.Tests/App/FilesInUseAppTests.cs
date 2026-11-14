using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [files-in-use · Task 7] App tarafı: Settings → General → BUILD'deki "Replace files in use" her koşunun komutuyla motora gider
/// (<see cref="StartRunCommand.ReplaceFilesInUse"/>) ve motorun proje notu (<see cref="ProjectNoteEvent"/> — kilitli kopya kararı) kullanıcıya
/// iki yerde görünür: konsolda AYNEN bir kez (<c>warning: </c> öneki satırı amber boyar), olay akışında öneksiz, uyarıysa Warn, değilse Info
/// türünde. Ayarın etiket/varsayılan/taslak/kayıt/dosya/not zinciri ortak tablodadır (<see cref="GeneralSettingChains"/>).
/// </summary>
public sealed class FilesInUseAppTests
{
    private const string ProjectA = @"C:\p\a.csproj";

    private const string HeldNote =
        @"warning: A: A.dll in C:\Acme\Client\bin is held by Acme.Host.exe (PID 42) — the copy kept the previous build. "
        + "Turn on Replace files in use in Settings to build while it runs";

    private const string ReplacedNote =
        @"A: A.dll in C:\Acme\Client\bin was held by Acme.Host.exe (PID 42) — moved aside and copied again; "
        + "the running program keeps the previous build until it restarts";

    [Fact]
    public void The_catalog_label_is_the_engine_note_label()
    {
        Assert.Equal(FilesInUseNotes.SettingLabel, GeneralSettingsCatalog.Definition(GeneralSetting.ReplaceFilesInUse).Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_sends_the_setting_with_every_run(bool replace)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        Assert.False(vm.ReplaceFilesInUse); // kataloğun varsayılanı
        vm.ReplaceFilesInUse = replace;
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal(replace, sent!.ReplaceFilesInUse);
    }

    [Fact]
    public async Task A_project_note_reaches_the_console_as_it_is_and_the_stream_without_its_prefix()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([new ProjectNode(ProjectA, "A", ProjectA, [], [], 0, null, null, false, true)], [], [], []));
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(ProjectA, "A", true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", ProjectA, "A"));
        int streamBefore = vm.StreamEvents.Count;

        vm.OnEvent(new ProjectNoteEvent("r1", ProjectA, HeldNote));
        vm.OnEvent(new ProjectNoteEvent("r1", ProjectA, ReplacedNote));

        var lines = vm.GetRunDocumentText().Split('\n');
        Assert.Equal(1, lines.Count(l => l == HeldNote));
        Assert.Equal(1, lines.Count(l => l == ReplacedNote));
        Assert.Equal(
            [(StreamKind.Warn, HeldNote["warning: ".Length..]), (StreamKind.Info, ReplacedNote)],
            vm.StreamEvents.Skip(streamBefore).Select(s => (s.Kind, s.Text)));
    }
}
