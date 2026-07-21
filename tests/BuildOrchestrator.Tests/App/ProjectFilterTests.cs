using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [C2] <see cref="ProjectFilter"/>: statü chip'i + serbest metin sorgusunun eşleşme mantığı
/// (BuildApp.jsx:465-470). Sorgu yalnız proje ADINDA (yol/id'de DEĞİL) case-insensitive alt-dizedir ve
/// statü filtresiyle AND'lenir; "building" chip'i queued satırları da kapsar; "dep" chip'i statüden
/// BAĞIMSIZ dep-issue taşıyan her satırı seçer.
/// </summary>
public class ProjectFilterTests
{
    private static ProjectRowViewModel Row(string name, ProjectRowState state, bool dep = false)
    {
        var r = new ProjectRowViewModel($@"C:\p\{name}.csproj", name, state);
        if (dep) r.DepIssues = ["X"];
        return r;
    }

    [Fact]
    public void Building_filter_includes_queued_rows()
    {
        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Started), null, ProjectFilter.Building));
        Assert.True(ProjectFilter.Matches(Row("B", ProjectRowState.Pending), null, ProjectFilter.Building)); // queued
        Assert.False(ProjectFilter.Matches(Row("C", ProjectRowState.Succeeded), null, ProjectFilter.Building));
    }

    [Fact]
    public void Dep_filter_matches_any_row_with_a_dep_issue_regardless_of_status()
    {
        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Succeeded, dep: true), null, ProjectFilter.Dep));
        Assert.True(ProjectFilter.Matches(Row("B", ProjectRowState.Failed, dep: true), null, ProjectFilter.Dep)); // succeeded DEĞİL
        Assert.False(ProjectFilter.Matches(Row("C", ProjectRowState.Succeeded, dep: false), null, ProjectFilter.Dep));
    }

    [Fact]
    public void Query_matches_the_project_name_only_and_is_ANDed_with_the_status_filter()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "pay", ProjectFilter.Succeeded));   // ad + statü ikisi de tutar
        Assert.False(ProjectFilter.Matches(row, "pay", ProjectFilter.Failed));     // statü tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "nope", ProjectFilter.Succeeded)); // ad tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "csproj", null));                  // yalnız AD (id/yol değil) → "csproj" eşleşmez
    }

    [Fact]
    public void Query_is_a_case_insensitive_substring_not_a_fuzzy_match()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "YMEN", null)); // case-insensitive alt-dize
        Assert.False(ProjectFilter.Matches(row, "pymnts", null)); // fuzzy DEĞİL
    }

    [Fact]
    public void Label_names_each_chip_with_dep_reading_as_dependency_issues()
    {
        Assert.Equal("Building", ProjectFilter.Label(ProjectFilter.Building));
        Assert.Equal("Succeeded", ProjectFilter.Label(ProjectFilter.Succeeded));
        Assert.Equal("Failed", ProjectFilter.Label(ProjectFilter.Failed));
        Assert.Equal("Skipped", ProjectFilter.Label(ProjectFilter.Skipped));
        Assert.Equal("Dependency issues", ProjectFilter.Label(ProjectFilter.Dep));
    }
}
