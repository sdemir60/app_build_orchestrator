using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using Xunit;
using static BuildOrchestrator.Tests.MsBuild.PostBuildCopy;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use · Task 5] <see cref="InUseCopyInvoker"/>: projenin post-build kopyası hedefte başka bir programın tuttuğu dosyaya
/// çarpınca o dosyayı kenara taşıyıp derlemeyi BİR kez daha koşturur (ayar açık) ya da yalnız raporlar (ayar kapalı). Sahte MSBuild
/// (<see cref="PostBuildCopyInvoker"/>) post-build olayını GERÇEKTEN çalıştırır: komut satırlarını yankılar, dosyaları
/// <c>cmd</c>'nin kopyası gibi zamanı koruyarak kopyalar, kilitli hedefte <c>cmd</c>'nin satırını yazar ve çıkış kodunu
/// <c>cmd</c>'nin toplu dosya kuralıyla — SON satırın sonucu — verir. Yüklü DLL'i silmeyi paylaşan bir tutamaç taklit eder.
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class InUseCopyInvokerTests
{
    private static readonly FileHolder Host = new(42, "Acme.Host.exe");

    private sealed class Workspace : IDisposable
    {
        private readonly TempDir _temp = new();
        public string Project { get; }
        public string Output { get; }
        public string Client { get; }
        public string Server { get; }
        public InUseStash Stash { get; }

        public Workspace()
        {
            Project = Path.Combine(_temp.Path, "src", "Acme.Types", "Acme.Types.csproj");
            Output = Path.Combine(_temp.Path, "src", "Acme.Types", "bin", "Release");
            Client = Path.Combine(_temp.Path, "Acme", "Client", "bin");
            Server = Path.Combine(_temp.Path, "Acme", "Server", "bin");
            foreach (string folder in new[] { Output, Client, Server }) Directory.CreateDirectory(folder);
            File.WriteAllText(Project, "<Project />");
            Stash = new InUseStash(Path.Combine(_temp.Path, "state", InUseStash.RecordFileName));
        }

        public string Pattern => Path.Combine(Output, "Acme.Types.*");

        /// <summary>Yeni derlemenin çıktısı (kaynak) ve paylaşılan klasördeki eski kopyası (hedef, daha eski).</summary>
        public void Built(params string[] names)
        {
            foreach (string name in names)
            {
                File.WriteAllText(Path.Combine(Output, name), "new build");
                foreach (string folder in new[] { Client, Server })
                {
                    string old = Path.Combine(folder, name);
                    File.WriteAllText(old, "old");
                    File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromHours(1));
                }
            }
        }

        public string Parked(string folder) => Path.Combine(folder, InUseStash.FolderName);

        public MsBuildInvokeRequest Request(MsBuildTarget target = MsBuildTarget.Build) =>
            new(Project, "Release", Path.GetDirectoryName(Project)!, NeedsRestore: false, Target: target);

        public void Dispose() => _temp.Dispose();
    }

    /// <summary>Post-build olayını gerçekten çalıştıran sahte MSBuild — her satır bir <c>copy /y "&lt;desen&gt;" "&lt;klasör&gt;\"</c>.</summary>
    private sealed class PostBuildCopyInvoker(string project, params (string Pattern, string Folder)[] lines) : IMsBuildInvoker
    {
        public int Calls;
        public int RestoreCalls;
        /// <summary>Denemeyi hiç koşturmadan dönen sonuç (ör. ikinci denemede iptal); <c>null</c> ⇒ olay çalışır.</summary>
        public Func<int, MsBuildInvokeResult?> Forced { get; init; } = _ => null;
        /// <summary>Olay ÇALIŞTIKTAN sonra sonucu biçimler (ör. kopya düştü ama koşu zaman aşımıyla bitti).</summary>
        public Func<MsBuildInvokeResult, MsBuildInvokeResult> Shape { get; init; } = r => r;
        public string[] ExtraLines { get; init; } = [];

        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref Calls);
            if (Forced(call) is { } forced) return Task.FromResult(forced);
            foreach (string extra in ExtraLines) onLine(extra);
            if (lines.Length == 0) return Task.FromResult(Shape(new MsBuildInvokeResult(0, 100, false, false)));
            return Task.FromResult(Shape(PostBuildCopy.Run(project, lines, onLine)));
        }

        public Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct)
        {
            Interlocked.Increment(ref RestoreCalls);
            return Task.FromResult(new MsBuildInvokeResult(0, 5, false, false));
        }
    }

    /// <summary>Rapor alan alan (kayıt eşitliği listeleri referansla karşılaştırır).</summary>
    private static void AssertReport(string folder, string[] files, FilesInUseOutcome outcome, FilesInUseReport report)
    {
        Assert.Equal(folder, report.Folder);
        Assert.Equal(files, report.FileNames);
        Assert.Equal([Host], report.Holders);
        Assert.Equal(outcome, report.Outcome);
    }

    private static (InUseCopyInvoker Invoker, ConcurrentQueue<FilesInUseReport> Reports) Wrap(
        IMsBuildInvoker inner, InUseStash stash, bool replace)
    {
        var reports = new ConcurrentQueue<FilesInUseReport>();
        var invoker = new InUseCopyInvoker(inner, stash, replace, (_, report) => reports.Enqueue(report), findHolders: _ => [Host]);
        return (invoker, reports);
    }

    [Fact]
    public async Task A_held_target_is_moved_aside_and_the_build_runs_once_more()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll", "Acme.Types.pdb");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);
        var lines = new List<string>();
        MsBuildInvokeResult result;

        using (HoldLikeALoadedDll(Path.Combine(w.Client, "Acme.Types.dll")))
            result = await invoker.InvokeAsync(w.Request(), lines.Add, CancellationToken.None);

        Assert.Equal(2, inner.Calls);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(200, result.DurationMs);
        Assert.Equal("new build", File.ReadAllText(Path.Combine(w.Client, "Acme.Types.dll")));
        string parked = Assert.Single(Directory.GetFiles(w.Parked(w.Client)));
        Assert.Matches(@"Acme\.Types\.dll~[0-9a-f]{16}\.old$", parked);
        Assert.Equal("old", File.ReadAllText(parked));
        AssertReport(w.Client, ["Acme.Types.dll"], FilesInUseOutcome.Replaced, Assert.Single(reports));
        Assert.Contains(lines, l => l.Contains("error MSB3073"));
        Assert.Equal(2, lines.Count(l => l == "PostBuildEvent:"));
    }

    [Fact]
    public async Task A_two_line_event_whose_first_copy_fails_exits_zero_and_is_still_replaced()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client), (w.Pattern, w.Server));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);
        MsBuildInvokeResult result;

        using (HoldLikeALoadedDll(Path.Combine(w.Client, "Acme.Types.dll")))
            result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(2, inner.Calls);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("new build", File.ReadAllText(Path.Combine(w.Client, "Acme.Types.dll")));
        Assert.Equal("new build", File.ReadAllText(Path.Combine(w.Server, "Acme.Types.dll")));
        Assert.False(Directory.Exists(w.Parked(w.Server)));
        Assert.Equal(FilesInUseOutcome.Replaced, Assert.Single(reports).Outcome);
    }

    [Fact]
    public async Task With_the_setting_off_the_held_file_is_named_and_left_alone()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: false);
        MsBuildInvokeResult result;

        using (HoldLikeALoadedDll(Path.Combine(w.Client, "Acme.Types.dll")))
            result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(100, result.DurationMs);
        Assert.Equal("old", File.ReadAllText(Path.Combine(w.Client, "Acme.Types.dll")));
        Assert.False(Directory.Exists(w.Parked(w.Client)));
        AssertReport(w.Client, ["Acme.Types.dll"], FilesInUseOutcome.Held, Assert.Single(reports));
    }

    [Fact]
    public async Task A_compile_error_is_left_alone()
    {
        using var w = new Workspace();
        var inner = new PostBuildCopyInvoker(w.Project)
        {
            Forced = _ => new MsBuildInvokeResult(1, 100, false, false),
            ExtraLines = ["Foo.cs(12,5): error CS0103: The name 'Bar' does not exist in the current context"],
        };
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);

        var result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task A_copy_that_landed_is_left_alone()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);

        var result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(reports);
        Assert.False(Directory.Exists(w.Parked(w.Client)));
    }

    [Fact]
    public async Task A_read_only_target_is_not_a_lock_and_is_left_alone()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        string target = Path.Combine(w.Client, "Acme.Types.dll");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
            var (invoker, reports) = Wrap(inner, w.Stash, replace: true);

            var result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

            Assert.Equal(1, inner.Calls);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(reports);
            Assert.Equal("old", File.ReadAllText(target));
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task A_lock_that_cannot_be_moved_is_named_and_the_build_is_not_repeated()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);
        string target = Path.Combine(w.Client, "Acme.Types.dll");
        MsBuildInvokeResult result;

        using (HoldWithoutDeleteSharing(target))
            result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal(FilesInUseOutcome.CannotMove, Assert.Single(reports).Outcome);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    public async Task When_the_second_build_fails_or_is_stopped_the_moved_file_goes_back(int exitCode, bool killed)
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client))
        {
            Forced = call => call == 2 ? new MsBuildInvokeResult(exitCode, 30, TimedOut: false, Killed: killed) : null,
        };
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);
        string target = Path.Combine(w.Client, "Acme.Types.dll");
        MsBuildInvokeResult result;

        using (HoldLikeALoadedDll(target))
            result = await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(2, inner.Calls);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(killed, result.Killed);
        Assert.Equal(130, result.DurationMs);
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Empty(Directory.Exists(w.Parked(w.Client)) ? Directory.GetFiles(w.Parked(w.Client)) : []);
        Assert.Equal(FilesInUseOutcome.FailedAgain, Assert.Single(reports).Outcome);
    }

    [Fact]
    public async Task Another_projects_file_that_shares_the_prefix_is_never_touched()
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        string sibling = Path.Combine(w.Client, "Acme.Types.Common.dll");
        File.WriteAllText(sibling, "another project's output");
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client));
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);

        using (HoldLikeALoadedDll(sibling))
            await invoker.InvokeAsync(w.Request(), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Empty(reports);
        Assert.Equal("another project's output", File.ReadAllText(sibling));
    }

    [Theory]
    [InlineData(true, false, MsBuildTarget.Build)]
    [InlineData(false, true, MsBuildTarget.Build)]
    [InlineData(false, false, MsBuildTarget.Clean)]
    public async Task A_timed_out_or_stopped_first_build_and_a_clean_are_left_alone(bool timedOut, bool killed, MsBuildTarget target)
    {
        using var w = new Workspace();
        w.Built("Acme.Types.dll");
        // Olay çalışır ve kopya kilide çarpar; yalnız sonuç zaman aşımı/iptal (ya da hedef Clean) der.
        var inner = new PostBuildCopyInvoker(w.Project, (w.Pattern, w.Client))
        {
            Shape = r => timedOut || killed ? r with { ExitCode = -1, TimedOut = timedOut, Killed = true } : r,
        };
        var (invoker, reports) = Wrap(inner, w.Stash, replace: true);

        using (HoldLikeALoadedDll(Path.Combine(w.Client, "Acme.Types.dll")))
            await invoker.InvokeAsync(w.Request(target), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Empty(reports);
        Assert.Equal("old", File.ReadAllText(Path.Combine(w.Client, "Acme.Types.dll")));
    }

    [Fact]
    public async Task A_restore_goes_straight_to_the_inner_invoker()
    {
        using var w = new Workspace();
        var inner = new PostBuildCopyInvoker(w.Project);
        var (invoker, _) = Wrap(inner, w.Stash, replace: true);

        await invoker.RestoreAsync(new MsBuildRestoreRequest(w.Project, Path.GetDirectoryName(w.Project)!), _ => { }, CancellationToken.None);

        Assert.Equal(1, inner.RestoreCalls);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public void The_note_for_each_outcome_names_the_files_the_folder_and_who_holds_them()
    {
        const string folder = @"C:\Acme\Client\bin";
        FilesInUseReport Report(FilesInUseOutcome outcome, params FileHolder[] holders) =>
            new(folder, ["Acme.Types.dll"], holders, outcome);

        Assert.Equal(
            @"warning: Acme.Types: Acme.Types.dll in C:\Acme\Client\bin is held by Acme.Host.exe (PID 42) — the copy kept the previous build. "
            + "Turn on Replace files in use in Settings to build while it runs",
            FilesInUseNotes.Line("Acme.Types", Report(FilesInUseOutcome.Held, Host)));
        Assert.Equal(
            @"Acme.Types: Acme.Types.dll, Acme.Types.pdb in C:\Acme\Client\bin were held by Acme.Host.exe (PID 42), devenv.exe (PID 7) — moved aside "
            + "and copied again; the running program keeps the previous build until it restarts",
            FilesInUseNotes.Line("Acme.Types", new(folder, ["Acme.Types.dll", "Acme.Types.pdb"], [Host, new(7, "devenv.exe")], FilesInUseOutcome.Replaced)));
        Assert.Equal(
            @"warning: Acme.Types: Acme.Types.dll in C:\Acme\Client\bin is held by another program and cannot be moved aside — close it and build again",
            FilesInUseNotes.Line("Acme.Types", Report(FilesInUseOutcome.CannotMove)));
        Assert.Equal(
            @"warning: Acme.Types: Acme.Types.dll in C:\Acme\Client\bin was held by Acme.Host.exe (PID 42) — moved aside, but the copy failed again",
            FilesInUseNotes.Line("Acme.Types", Report(FilesInUseOutcome.FailedAgain, Host)));
        Assert.Contains(FilesInUseNotes.SettingLabel, FilesInUseNotes.Line("Acme.Types", Report(FilesInUseOutcome.Held, Host)));
    }
}
