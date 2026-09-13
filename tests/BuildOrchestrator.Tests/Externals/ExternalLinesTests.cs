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

    /// <summary>
    /// <b>Eski iddia:</b> satır seçilen kaynağın adını taşıyordu (<c>no git/tfvc working copy…</c>) ve iki
    /// <c>InlineData</c> ile pinlenmişti. TFVC kolu kaldırıldı: aranan tek işaret <c>.git</c>, dolayısıyla
    /// söylenecek tek sözcük de "git".
    /// </summary>
    [Fact]
    public void A_path_without_a_git_working_copy_says_it_is_built_as_is()
        => Assert.Equal(
            "warning: external 'Mail': no git working copy found above its path — building as-is",
            PlanProgressLines.ExternalNoWorkingCopy("Mail"));
}
