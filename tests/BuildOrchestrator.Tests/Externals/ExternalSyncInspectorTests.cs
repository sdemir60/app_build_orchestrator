using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D5] Sync harici projelere yalnız BAKAR: hiçbir VCS mutasyonu yapmaz, ağa çıkmaz ve bir harici yüzünden
/// ölmez. Git haricilerde yerel HEAD gerçek bir önizleme verir; TFVC hariciler Sync'te hollow kalır (tf.exe
/// hiç çalıştırılmaz) — gerçek karar koşu planlamasında verilir.
/// </summary>
public class ExternalSyncInspectorTests
{
    private const string Configuration = "Debug";

    private static ExternalSyncInspector Inspector() => new(new ProcessRunner());

    private static ExternalProject ProjectAt(string directory, string name = "Mail")
        => new(name, directory, Path.Combine(directory, name + ".sln"));

    private static IReadOnlyDictionary<string, BuildState> Built(string targetPath, string signature) =>
        new Dictionary<string, BuildState>
        {
            [targetPath] = new(targetPath, signature, LastResult: BuildResult.Succeeded),
        };

    [Fact]
    public async Task A_git_external_that_was_built_at_this_revision_is_up_to_date()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "one");
        string head = repo.CommitAll("first");
        var project = ProjectAt(repo.RootPath);
        string signature = ExternalSignature.Compute(Configuration, VcsKind.Git, head);

        var inspection = (await Inspector().InspectAsync([project], Configuration, Built(project.TargetPath, signature))).Single();

        Assert.Equal(VcsKind.Git, inspection.Vcs);
        Assert.Equal(head, inspection.Revision);
        Assert.False(inspection.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, inspection.Reason);
        Assert.False(inspection.Dirty);
        Assert.Null(inspection.Warning);
    }

    [Fact]
    public async Task A_git_external_built_at_another_revision_will_build()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "one");
        repo.CommitAll("first");
        var project = ProjectAt(repo.RootPath);

        var inspection = (await Inspector().InspectAsync([project], Configuration,
            Built(project.TargetPath, ExternalSignature.Compute(Configuration, VcsKind.Git, "0000")))).Single();

        Assert.True(inspection.WillBuild);
        Assert.Equal(WillBuildReason.SignatureChanged, inspection.Reason);
    }

    [Fact]
    public async Task A_never_built_git_external_will_build()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "one");
        repo.CommitAll("first");

        var inspection = (await Inspector().InspectAsync([ProjectAt(repo.RootPath)], Configuration, null)).Single();

        Assert.True(inspection.WillBuild);
        Assert.Equal(WillBuildReason.NeverBuilt, inspection.Reason);
    }

    [Fact]
    public async Task Local_changes_are_flagged_without_changing_the_preview()
    {
        // Kir Sync'i bloklamaz ve önizlemeyi bozmaz — yalnız bir uyarı taşır; koşuyu durduran kapı Build'dedir.
        using var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "one");
        string head = repo.CommitAll("first");
        var project = ProjectAt(repo.RootPath);
        string signature = ExternalSignature.Compute(Configuration, VcsKind.Git, head);
        File.WriteAllText(Path.Combine(repo.RootPath, "a.cs"), "edited");

        var inspection = (await Inspector().InspectAsync([project], Configuration, Built(project.TargetPath, signature))).Single();

        Assert.True(inspection.Dirty);
        Assert.False(inspection.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, inspection.Reason);
    }

    [Fact]
    public async Task A_tfvc_external_stays_hollow_and_no_tf_process_is_started()
    {
        using var temp = new TempDir();
        string workspace = Path.Combine(temp.Path, "tfs");
        Directory.CreateDirectory(Path.Combine(workspace, "$tf"));
        var runner = new RecordingProcessRunner();

        var inspection = (await new ExternalSyncInspector(runner).InspectAsync([ProjectAt(workspace)], Configuration, null)).Single();

        Assert.Equal(VcsKind.Tfvc, inspection.Vcs);
        Assert.Null(inspection.WillBuild);
        Assert.Null(inspection.Reason);
        Assert.Null(inspection.Revision);
        // Sync hızlı ve çevrimdışı-toleranslı kalmalı: tf.exe burada ÇALIŞTIRILMAZ.
        Assert.Empty(runner.Started);
    }

    [Fact]
    public async Task A_missing_folder_is_reported_as_unknown()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-external-4b7d");

        var inspection = (await Inspector().InspectAsync([ProjectAt(missing)], Configuration, null)).Single();

        Assert.Equal(VcsKind.Unknown, inspection.Vcs);
        Assert.Null(inspection.WillBuild);
        Assert.Contains("folder not found", inspection.Warning);
    }

    [Fact]
    public async Task A_folder_without_version_control_is_reported_as_hollow()
    {
        using var temp = new TempDir();

        var inspection = (await Inspector().InspectAsync([ProjectAt(temp.Path)], Configuration, null)).Single();

        Assert.Equal(VcsKind.Unknown, inspection.Vcs);
        Assert.Null(inspection.WillBuild);
        Assert.Contains("no version control", inspection.Warning);
    }

    [Fact]
    public async Task One_broken_external_does_not_stop_the_others()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.cs", "one");
        repo.CommitAll("first");
        var broken = ProjectAt(Path.Combine(Path.GetTempPath(), "no-such-external-91cc"), "Ocr");
        var healthy = ProjectAt(repo.RootPath);

        var inspections = await Inspector().InspectAsync([broken, healthy], Configuration, null);

        Assert.Equal(2, inspections.Count);
        Assert.Equal(["Ocr", "Mail"], inspections.Select(i => i.Project.Name)); // liste sırası korunur
        Assert.True(inspections[1].WillBuild);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Started { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, System.Threading.CancellationToken ct = default)
        {
            Started.Add(spec.FileName);
            return Task.FromResult(new ProcessResult(0, "", "", System.TimeSpan.Zero, TimedOut: false));
        }
    }
}
