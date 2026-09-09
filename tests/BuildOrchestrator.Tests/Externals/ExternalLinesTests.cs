using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici projelerin kullanıcıya görünen satırları TEK kaynaktadır: aynı metin hem Sync transkriptinde hem
/// koşu planlamasında görünür. Metinler burada verbatim pinlenir — başka bir yerde yeniden yazılırsa iki
/// yüzey sessizce ayrışır.
/// </summary>
public class ExternalLinesTests
{
    [Fact]
    public void Updating_line_names_the_project()
        => Assert.Equal("Updating external 'Mail'", PlanProgressLines.UpdatingExternal("Mail"));

    [Fact]
    public void Up_to_date_line_names_the_project()
        => Assert.Equal("External 'Mail' is up to date", PlanProgressLines.ExternalUpToDate("Mail"));

    [Fact]
    public void A_degraded_update_says_the_local_version_is_being_built()
        => Assert.Equal(
            "warning: external 'Mail' could not be updated — building the local version (no route to host)",
            PlanProgressLines.ExternalUpdateDegraded("Mail", "no route to host"));

    [Fact]
    public void The_dirty_warning_says_what_the_user_has_to_do()
        => Assert.Equal(
            "warning: external 'Mail' has uncommitted changes — Build will refuse to run until they are committed or shelved",
            PlanProgressLines.ExternalDirtyWarning("Mail"));

    [Fact]
    public void An_unresolvable_path_is_reported_with_the_resolver_sentence()
        => Assert.Equal(
            "warning: external 'Mail': the path was not found — state unknown",
            PlanProgressLines.ExternalUnresolved("Mail", "the path was not found"));

    [Fact]
    public void An_unreadable_working_copy_is_reported_as_unknown_state()
        => Assert.Equal(
            "warning: external 'Mail': state unknown (not a git repository)",
            PlanProgressLines.ExternalStateUnknown("Mail", "not a git repository"));

    [Theory]
    [InlineData(VcsKind.Git, "git")]
    [InlineData(VcsKind.Tfvc, "tfvc")]
    public void A_path_without_a_working_copy_of_the_selected_kind_says_it_is_built_as_is(VcsKind vcs, string label)
        => Assert.Equal(
            $"warning: external 'Mail': no {label} working copy found above its path — building as-is",
            PlanProgressLines.ExternalNoWorkingCopy("Mail", vcs));
}
