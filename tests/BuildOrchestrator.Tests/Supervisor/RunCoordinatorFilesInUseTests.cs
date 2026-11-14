using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.MsBuild;
using static BuildOrchestrator.Tests.MsBuild.PostBuildCopy;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [files-in-use · Task 6] Kilitli kopya kararının koşuya bağlanması: koordinatör iç invoker'ı <see cref="InUseCopyInvoker"/> ile sarar
/// (ayar <see cref="StartRunCommand.ReplaceFilesInUse"/>'tan), raporu <see cref="FilesInUseNotes.Line"/> ile hem <c>decision.log</c>'a
/// hem <see cref="ProjectNoteEvent"/> olarak App'e yazar ve koşu başında <see cref="InUseStash.Sweep"/> çalıştırır. Sahte MSBuild
/// post-build kopyasını gerçekten yapar (<see cref="MsBuild.PostBuildCopy.Run"/>); paylaşılan kopya yüklü bir DLL gibi tutulur.
/// Tutanı gerçek Restart Manager bulur — bu test süreci.
/// </summary>
public sealed class RunCoordinatorFilesInUseTests
{
    private sealed class Shared : IDisposable
    {
        private readonly TempDir _temp = new();
        public string Output { get; }
        public string Folder { get; }
        public InUseStash Stash { get; }
        public string Copy => Path.Combine(Folder, "Acme.Types.dll");
        public string Pattern => Path.Combine(Output, "Acme.Types.*");

        public Shared()
        {
            Output = Path.Combine(_temp.Path, "src", "bin", "Release");
            Folder = Path.Combine(_temp.Path, "Acme", "Client", "bin");
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Output, "Acme.Types.dll"), "new build");
            File.WriteAllText(Copy, "old");
            File.SetLastWriteTimeUtc(Copy, DateTime.UtcNow - TimeSpan.FromHours(1));
            Stash = new InUseStash(Path.Combine(_temp.Path, "state", InUseStash.RecordFileName));
        }

        /// <summary>Her derlemesi projenin post-build kopyasını gerçekten yapan sahte MSBuild.</summary>
        public FakeInvoker Invoker() => new((req, onLine, _) => Task.FromResult(Run(req.ProjectId, [(Pattern, Folder)], onLine)));

        public void Dispose() => _temp.Dispose();
    }

    private static void AssertDecided(Harness h, string line) =>
        Assert.Single(h.DecisionLog.Split('\n'), l => l.TrimEnd('\r') is { Length: > 13 } text && text[13..] == line);

    [Fact]
    public async Task With_the_setting_on_a_held_copy_target_is_replaced_and_the_project_succeeds()
    {
        using var s = new Shared();
        using var h = new Harness(PlanOf(Node("A")), s.Invoker(), inUseStash: s.Stash);

        using (HoldLikeALoadedDll(s.Copy))
        {
            await h.Sut.StartAsync(Start() with { ReplaceFilesInUse = true }, default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
        }

        Assert.Equal("new build", File.ReadAllText(s.Copy));
        var events = h.Events;
        var note = Assert.Single(events.OfType<ProjectNoteEvent>());
        Assert.Equal(Id("A"), note.ProjectId);
        Assert.StartsWith($"A: Acme.Types.dll in {s.Folder} was held by ", note.Text);
        Assert.Contains($"(PID {Environment.ProcessId})", note.Text);
        Assert.EndsWith("until it restarts", note.Text);
        var succeeded = Assert.Single(events.OfType<ProjectSucceededEvent>());
        Assert.True(events.ToList().IndexOf(note) < events.ToList().IndexOf(succeeded), "the note comes before the project's result");
        AssertDecided(h, note.Text);
    }

    [Fact]
    public async Task With_the_setting_off_the_project_fails_as_before_and_the_note_names_the_setting()
    {
        using var s = new Shared();
        using var h = new Harness(PlanOf(Node("A")), s.Invoker(), inUseStash: s.Stash);

        using (HoldLikeALoadedDll(s.Copy))
        {
            await h.Sut.StartAsync(Start(), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
        }

        Assert.Single(h.Events.OfType<ProjectFailedEvent>());
        Assert.Equal("old", File.ReadAllText(s.Copy));
        Assert.False(Directory.Exists(Path.Combine(s.Folder, InUseStash.FolderName)));
        var note = Assert.Single(h.Events.OfType<ProjectNoteEvent>());
        Assert.StartsWith($"warning: A: Acme.Types.dll in {s.Folder} is held by ", note.Text);
        Assert.Contains(FilesInUseNotes.SettingLabel, note.Text);
        AssertDecided(h, note.Text);
    }

    [Fact]
    public async Task Without_a_stash_the_run_is_what_it_was()
    {
        using var s = new Shared();
        using var h = new Harness(PlanOf(Node("A")), s.Invoker());

        using (HoldLikeALoadedDll(s.Copy))
        {
            await h.Sut.StartAsync(Start() with { ReplaceFilesInUse = true }, default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
        }

        Assert.Single(h.Events.OfType<ProjectFailedEvent>());
        Assert.Empty(h.Events.OfType<ProjectNoteEvent>());
        Assert.Equal("old", File.ReadAllText(s.Copy));
    }

    [Fact]
    public async Task The_run_start_sweep_puts_back_a_file_a_dead_engine_left_pending()
    {
        using var s = new Shared();
        // Ölen bir motor kopyayı kenara çekti ve sonucu yazamadı: yuva boş, dosya .pending.
        Assert.NotNull(s.Stash.MoveAside(s.Copy));
        Assert.False(File.Exists(s.Copy));
        using var h = new Harness(PlanOf(Node("B")), AllSucceed(), inUseStash: s.Stash);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal("old", File.ReadAllText(s.Copy));
        Assert.False(Directory.Exists(Path.Combine(s.Folder, InUseStash.FolderName)));
        AssertDecided(h, FilesInUseNotes.SweepLine(new StashSweep(Removed: 0, Restored: 1, Kept: 0)));
    }
}
