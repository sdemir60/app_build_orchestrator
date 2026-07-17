using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T69/K1] Sync'in İLK adımı: yalnızca remote-tracking ref güncellenir (`git fetch origin &lt;branch&gt;
/// --no-tags`); aktif branch ve working tree ASLA değişmez (checkout/pull/merge/switch yok). Fetch
/// başarısız olursa (offline/unreachable remote) hata yutulur, hedef SHA yerel HEAD'e düşer (K1 — throw
/// yok). Gerçek ephemeral temp git repo'lar üzerinde (D8 — mock yok, sleep-poll yok): bir "origin" repo +
/// ondan tam klon, origin ilerletilip klon üzerinde fetch tetiklenir.
/// </summary>
public class SyncFetchTests
{
    private static readonly ProcessRunner Runner = new();

    [Fact]
    public async Task FetchRefOnlyAsync_advances_remote_tracking_ref_but_leaves_HEAD_and_working_tree_unchanged()
    {
        using var origin = new GitTestRepo();
        origin.WriteFile("a.txt", "v1");
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        // origin ilerler — klon henüz haberdar değil
        origin.WriteFile("a.txt", "v2");
        string newOriginSha = origin.CommitAll("c2");

        var svc = new GitService(Runner, cloneRoot);
        string cloneHeadBefore = (await svc.GetHeadCommitAsync()).Value!;
        string cloneFileBefore = File.ReadAllText(Path.Combine(cloneRoot, "a.txt"));

        var fetchResult = await svc.FetchRefOnlyAsync(branch);

        Assert.False(fetchResult.Degraded);
        Assert.Equal(newOriginSha, fetchResult.TargetSha);

        string cloneHeadAfter = (await svc.GetHeadCommitAsync()).Value!;
        string cloneFileAfter = File.ReadAllText(Path.Combine(cloneRoot, "a.txt"));

        // ref-only kanıtı: remote ilerledi ama HEAD ve çalışma ağacı klonda AYNI kaldı
        Assert.Equal(cloneHeadBefore, cloneHeadAfter);
        Assert.Equal(cloneFileBefore, cloneFileAfter);
        Assert.NotEqual(newOriginSha, cloneHeadAfter); // hedef, yerel HEAD'den farklı — remote ilerlemiş
    }

    [Fact]
    public async Task FetchRefOnlyAsync_uses_ref_only_fetch_args_and_never_checkout_pull_merge_switch()
    {
        using var origin = new GitTestRepo();
        origin.WriteFile("a.txt", "v1");
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        origin.WriteFile("a.txt", "v2");
        origin.CommitAll("c2");

        var recorder = new RecordingProcessRunner();
        var svc = new GitService(recorder, cloneRoot);

        await svc.FetchRefOnlyAsync(branch);

        var fetchCall = Assert.Single(recorder.Calls, args => args.Count > 0 && args[0] == "fetch");
        Assert.Equal(["fetch", "origin", branch, "--no-tags"], fetchCall);

        foreach (var call in recorder.Calls)
        {
            Assert.DoesNotContain("checkout", call);
            Assert.DoesNotContain("pull", call);
            Assert.DoesNotContain("merge", call);
            Assert.DoesNotContain("switch", call);
        }
    }

    [Fact]
    public async Task GetRemoteTrackingShaAsync_returns_fetched_sha_and_differs_from_local_head()
    {
        using var origin = new GitTestRepo();
        origin.WriteFile("a.txt", "v1");
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        origin.WriteFile("a.txt", "v2");
        string newOriginSha = origin.CommitAll("c2");

        var svc = new GitService(Runner, cloneRoot);
        await svc.FetchRefOnlyAsync(branch);

        var trackingResult = await svc.GetRemoteTrackingShaAsync(branch);
        var localHead = await svc.GetHeadCommitAsync();

        Assert.True(trackingResult.Success);
        Assert.Equal(newOriginSha, trackingResult.Value);
        Assert.NotEqual(localHead.Value, trackingResult.Value);
    }

    [Fact]
    public async Task FetchRefOnlyAsync_degrades_gracefully_when_remote_unreachable_falls_back_to_local_head()
    {
        using var origin = new GitTestRepo();
        origin.WriteFile("a.txt", "v1");
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        // origin'i geçersiz/ulaşılamaz bir remote'a çevir — ağ olmadan, deterministik ve hızlı başarısızlık
        string bogusRemote = Path.Combine(Path.GetTempPath(), "gitsvc-nonexistent-remote-" + Guid.NewGuid().ToString("N"));
        GitTestRepo.RunGitAt(cloneRoot, "remote", "set-url", "origin", bogusRemote);

        var svc = new GitService(Runner, cloneRoot);
        string localHeadBefore = (await svc.GetHeadCommitAsync()).Value!;

        var fetchResult = await svc.FetchRefOnlyAsync(branch);

        Assert.True(fetchResult.Degraded);
        Assert.NotNull(fetchResult.Warning);
        Assert.Equal(localHeadBefore, fetchResult.TargetSha); // K1: hedef yerel HEAD'e düşer
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            Calls.Add(spec.Arguments);
            return await _inner.RunAsync(spec, ct);
        }
    }
}
