using System;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

// [T70] BuildDurationPersister: proje BAŞARIYLA derlendiğinde ölçülen süreyi BuildStateStore.Upsert ile
// BuildState.LastDurationMs'e yazan ince yardımcı — EtaCalculator'ın gelecek tick'lerinin girdisini besler.
// Gerçek BuildStateStore file-lock testleriyle AYNI collection'da değil (bu testler dosya kilidi kurmaz,
// yalnız Upsert/Load kullanır) — paralel çalışabilir.
public class BuildDurationPersisterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bo-durationpersist-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void persisting_a_success_writes_last_duration_ms_and_result_and_run_at()
    {
        var store = new BuildStateStore(_root);
        var runAt = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        BuildDurationPersister.PersistSucceeded(store, @"C:\repo\A\A.csproj", durationMs: 45_678, runAt: runAt);

        var map = new BuildStateStore(_root).Load(); // taze örnek — diskten okur
        var state = Assert.Single(map).Value;
        Assert.Equal(@"C:\repo\A\A.csproj", state.ProjectId);
        Assert.Equal(45_678, state.LastDurationMs);
        Assert.Equal(BuildResult.Succeeded, state.LastResult);
        Assert.Equal(runAt, state.LastRunAt);
    }

    [Fact]
    public void persisting_a_success_preserves_unrelated_existing_fields_on_the_same_project()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState(
            ProjectId: @"C:\repo\A\A.csproj",
            BuiltSignature: "sig-abc",
            BuiltCommit: "deadbeef",
            LastBranch: "main"));

        BuildDurationPersister.PersistSucceeded(store, @"C:\repo\A\A.csproj", durationMs: 12_000,
            runAt: new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));

        var state = store.Load()[@"C:\repo\A\A.csproj"];
        Assert.Equal("sig-abc", state.BuiltSignature); // dokunulmadı
        Assert.Equal("deadbeef", state.BuiltCommit);   // dokunulmadı
        Assert.Equal("main", state.LastBranch);         // dokunulmadı
        Assert.Equal(12_000, state.LastDurationMs);      // güncellendi
        Assert.Equal(BuildResult.Succeeded, state.LastResult);
    }

    [Fact]
    public void persisting_a_success_for_a_different_project_does_not_touch_other_entries()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState("P1", "sig1", LastDurationMs: 100));

        BuildDurationPersister.PersistSucceeded(store, "P2", durationMs: 200,
            runAt: new DateTimeOffset(2026, 7, 18, 14, 0, 0, TimeSpan.Zero));

        var map = store.Load();
        Assert.Equal(2, map.Count);
        Assert.Equal(100, map["P1"].LastDurationMs); // dokunulmadı
        Assert.Equal(200, map["P2"].LastDurationMs);
    }

    [Fact]
    public void persist_succeeded_throws_on_null_arguments()
    {
        var store = new BuildStateStore(_root);
        Assert.Throws<ArgumentNullException>(() => BuildDurationPersister.PersistSucceeded(null!, "P1", 1, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentNullException>(() => BuildDurationPersister.PersistSucceeded(store, null!, 1, DateTimeOffset.UtcNow));
    }
}
