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
    public void A_degraded_update_says_the_local_version_is_being_built()
        => Assert.Equal(
            "warning: external 'Mail' could not be updated — building the local version (no route to host)",
            PlanProgressLines.ExternalUpdateDegraded("Mail", "no route to host"));

    [Fact]
    public void A_path_that_contributes_no_projects_is_reported_with_the_resolver_sentence()
        => Assert.Equal(
            "warning: external 'Mail': the path was not found — no projects from it will be built",
            PlanProgressLines.ExternalNotScanned("Mail", "the path was not found"));

    [Theory]
    [InlineData(VcsKind.Git, "git")]
    [InlineData(VcsKind.Tfvc, "tfvc")]
    public void A_path_without_a_working_copy_of_the_selected_kind_says_it_is_built_as_is(VcsKind vcs, string label)
        => Assert.Equal(
            $"warning: external 'Mail': no {label} working copy found above its path — building as-is",
            PlanProgressLines.ExternalNoWorkingCopy("Mail", vcs));
}
