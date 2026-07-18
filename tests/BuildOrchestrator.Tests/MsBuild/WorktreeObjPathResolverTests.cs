using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [I2-K2/It-3 Task 10] Worktree obj-izolasyonu: proje **Id** (tam yol) anahtarlı, deterministik, çakışmasız
/// bir <c>BaseIntermediateOutputPath</c> üretir. Saf fonksiyon — dosya I/O yok, git yok.
/// </summary>
public class WorktreeObjPathResolverTests
{
    private const string WorktreeRoot = @"c:\worktrees\feature-x-1";

    [Fact]
    public void Different_project_ids_resolve_to_different_paths() // SPIKE bayat-obj vakası: paylaşılan obj YASAK
    {
        string a = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");
        string b = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\B\B.csproj");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Same_project_id_resolves_to_the_identical_path_every_time() // deterministik — random/GUID yok
    {
        string first = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");
        string second = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Same_project_id_with_different_casing_resolves_identically() // Windows dosya sistemi case-insensitive
    {
        string lower = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");
        string upper = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"C:\R\A\A.CSPROJ");

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void Resolved_path_is_rooted_under_the_given_worktree_root_in_a_dedicated_obj_folder()
    {
        string path = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");

        Assert.StartsWith(WorktreeRoot, path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\_obj\", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolved_path_final_segment_is_a_safe_filesystem_segment() // PathSanitizer disiplini: geçersiz karakter yok
    {
        string path = WorktreeObjPathResolver.Resolve(WorktreeRoot, @"c:\r\A\A.csproj");
        string finalSegment = path.TrimEnd('\\', '/');
        finalSegment = finalSegment[(finalSegment.LastIndexOf('\\') + 1)..];

        Assert.True(PathSanitizer.IsSafeSegment(finalSegment));
    }

    [Fact]
    public void Two_worktree_roots_keep_the_same_project_isolated_per_root() // farklı worktree'ler birbirini kirletmez
    {
        string underRootOne = WorktreeObjPathResolver.Resolve(@"c:\worktrees\main-1", @"c:\r\A\A.csproj");
        string underRootTwo = WorktreeObjPathResolver.Resolve(@"c:\worktrees\main-2", @"c:\r\A\A.csproj");

        Assert.NotEqual(underRootOne, underRootTwo);
        Assert.StartsWith(@"c:\worktrees\main-1", underRootOne, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(@"c:\worktrees\main-2", underRootTwo, StringComparison.OrdinalIgnoreCase);
    }
}
