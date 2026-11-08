using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [worktree] <c>git worktree list --porcelain -z</c> çıktısının saf okuyucusu (<see cref="WorktreeList.HolderOf"/>): bir branch'i HEAD'inde
/// tutan BAŞKA çalışma kopyasının kökü. Git'siz sınanır — çıktının biçimi burada elle kurulur, gerçek git ile uçtan uca
/// <see cref="BranchSwitcherTests"/> sınar.
///
/// <para><b>Biçim:</b> <c>-z</c> ile alanlar NUL ile ayrılır ve hiçbir yol tırnaklanmaz (boşluklu, Türkçe karakterli yol olduğu gibi gelir);
/// bir kopyanın bloğu <c>worktree &lt;yol&gt;</c> ile başlar, boş alanla biter. Branch satırı <c>branch refs/heads/&lt;ad&gt;</c>'dır;
/// detached ve bare kopyada bu satır yoktur, <c>locked</c>/<c>prunable</c> satırları (nedenleriyle) araya girebilir.</para>
/// </summary>
public class WorktreeListTests
{
    /// <summary>Kopyaları porcelain -z biçiminde birleştirir: her alan NUL'la biter, her blok boş bir alanla.</summary>
    private static string Porcelain(params string[][] blocks) =>
        string.Concat(blocks.Select(fields => string.Concat(fields.Select(f => f + "\0")) + "\0"));

    [Fact]
    public void The_copy_that_holds_the_branch_is_named_with_windows_separators()
    {
        string output = Porcelain(
            ["worktree D:/src/acme", "HEAD 1111111111111111111111111111111111111111", "branch refs/heads/main"],
            ["worktree D:/src/acme-worktree", "HEAD 2222222222222222222222222222222222222222", "branch refs/heads/feature/x"]);

        Assert.Equal(@"D:\src\acme-worktree", WorktreeList.HolderOf(output, @"D:\src\acme", "feature/x"));
        Assert.Equal(@"D:\src\acme", WorktreeList.HolderOf(output, @"D:\src\acme-worktree", "main"));
    }

    /// <summary>Kendi kopyası sayılmaz — büyük/küçük harf ve sondaki ayraç farkı da kendisidir.</summary>
    [Fact]
    public void The_asking_copy_never_holds_its_own_branch()
    {
        string output = Porcelain(["worktree D:/src/acme", "HEAD 1111111111111111111111111111111111111111", "branch refs/heads/main"]);

        Assert.Null(WorktreeList.HolderOf(output, @"d:\SRC\acme\", "main"));
    }

    [Fact]
    public void Paths_with_spaces_and_non_ascii_letters_come_through_as_they_are()
    {
        string output = Porcelain(
            ["worktree D:/İş Kopyası/acme", "HEAD 1111111111111111111111111111111111111111", "branch refs/heads/main"],
            ["worktree D:/İş Kopyası/acme çalışma", "HEAD 2222222222222222222222222222222222222222", "branch refs/heads/düzeltme"]);

        Assert.Equal(@"D:\İş Kopyası\acme çalışma", WorktreeList.HolderOf(output, @"D:\İş Kopyası\acme", "düzeltme"));
    }

    /// <summary>Kilitli ya da budanabilir kopyanın ek satırları (nedenleriyle) branch satırını gizlemez.</summary>
    [Fact]
    public void Locked_and_prunable_lines_do_not_hide_the_branch()
    {
        string output = Porcelain(
            ["worktree D:/src/acme", "HEAD 1111111111111111111111111111111111111111", "branch refs/heads/main"],
            ["worktree D:/src/acme-worktree", "HEAD 2222222222222222222222222222222222222222", "branch refs/heads/side", "locked moved to a USB disk", "prunable gitdir file points to non-existent location"]);

        Assert.Equal(@"D:\src\acme-worktree", WorktreeList.HolderOf(output, @"D:\src\acme", "side"));
    }

    /// <summary>Detached ve bare kopya hiçbir branch'i tutmaz; branch adı ön ek olarak da eşleşmez.</summary>
    [Fact]
    public void Detached_bare_and_prefix_names_hold_nothing()
    {
        string output = Porcelain(
            ["worktree D:/src/acme", "bare"],
            ["worktree D:/src/acme-worktree", "HEAD 2222222222222222222222222222222222222222", "detached"],
            ["worktree D:/src/acme-other", "HEAD 3333333333333333333333333333333333333333", "branch refs/heads/feature/x-2"]);

        Assert.Null(WorktreeList.HolderOf(output, @"D:\src\acme-main", "feature/x"));
        Assert.Null(WorktreeList.HolderOf(output, @"D:\src\acme-main", "main"));
    }

    [Fact]
    public void Empty_output_holds_nothing()
    {
        Assert.Null(WorktreeList.HolderOf("", @"D:\src\acme", "main"));
    }
}
