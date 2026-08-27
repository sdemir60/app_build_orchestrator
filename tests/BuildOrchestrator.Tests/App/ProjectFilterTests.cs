using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [C2] <see cref="ProjectFilter"/>: statü chip'leri + serbest metin sorgusunun eşleşme mantığı
/// (BuildApp.jsx:339-349). Sorgu yalnız proje ADINDA (yol/id'de DEĞİL) case-insensitive alt-dizedir ve
/// filtre kümesiyle AND'lenir; "building" chip'i queued satırları da kapsar.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §2.7-4/§3.5]</b> Filtre eskiden TEK bir chip'ti
/// (<c>string?</c>) ve ayrı bir <c>dep</c> chip'i vardı ("Dependency issues"). Artık bir KÜME'dir — chip'ler
/// bağımsız açılıp kapanır ve <b>VEYA</b> ile birleşir — ve <c>dep</c>/<c>cycle</c> chip'leri tek bir
/// <see cref="ProjectFilter.Warn"/> chip'inde ("Warnings") birleşti.</para>
/// </summary>
public class ProjectFilterTests
{
    private static ProjectRowViewModel Row(string name, ProjectRowState state, bool dep = false, bool cycle = false)
    {
        var r = new ProjectRowViewModel($@"C:\p\{name}.csproj", name, state);
        if (dep) r.DepIssues = ["X"];
        if (cycle) r.InCycle = true;
        return r;
    }

    private static IReadOnlySet<string> Set(params string[] keys) => new HashSet<string>(keys, StringComparer.Ordinal);

    [Fact]
    public void Building_filter_includes_queued_rows()
    {
        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Started), null, Set(ProjectFilter.Building)));
        Assert.True(ProjectFilter.Matches(Row("B", ProjectRowState.Pending), null, Set(ProjectFilter.Building))); // queued
        Assert.False(ProjectFilter.Matches(Row("C", ProjectRowState.Succeeded), null, Set(ProjectFilter.Building)));
    }

    /// <summary>[design v1.11.0 §2.7-4] <c>warn</c> = döngü üyeliği <b>∪</b> dependency issue; ikisi de
    /// statüden BAĞIMSIZDIR.</summary>
    [Fact]
    public void Warn_filter_matches_a_cycle_member_or_a_dep_issue_regardless_of_status()
    {
        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Succeeded, dep: true), null, Set(ProjectFilter.Warn)));
        Assert.True(ProjectFilter.Matches(Row("B", ProjectRowState.Failed, dep: true), null, Set(ProjectFilter.Warn)));
        Assert.True(ProjectFilter.Matches(Row("C", ProjectRowState.Skipped, cycle: true), null, Set(ProjectFilter.Warn)));
        Assert.False(ProjectFilter.Matches(Row("D", ProjectRowState.Succeeded), null, Set(ProjectFilter.Warn)));
    }

    /// <summary>[design v1.11.0 §2.7-4] Küme çok elemanlıysa VEYA: ✓ + ✗ = "bu koşuda derlenenler".</summary>
    [Fact]
    public void Several_chips_combine_with_or_not_and()
    {
        var both = Set(ProjectFilter.Succeeded, ProjectFilter.Failed);

        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Succeeded), null, both));
        Assert.True(ProjectFilter.Matches(Row("B", ProjectRowState.Failed), null, both));
        Assert.False(ProjectFilter.Matches(Row("C", ProjectRowState.Skipped), null, both));
    }

    [Fact]
    public void Query_matches_the_project_name_only_and_is_ANDed_with_the_status_filter()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "pay", Set(ProjectFilter.Succeeded)));   // ad + statü ikisi de tutar
        Assert.False(ProjectFilter.Matches(row, "pay", Set(ProjectFilter.Failed)));     // statü tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "nope", Set(ProjectFilter.Succeeded))); // ad tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "csproj", null));                       // yalnız AD (id/yol değil)
    }

    [Fact]
    public void Query_is_a_case_insensitive_substring_not_a_fuzzy_match()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "YMEN", null)); // case-insensitive alt-dize
        Assert.False(ProjectFilter.Matches(row, "pymnts", null)); // fuzzy DEĞİL
    }

    [Fact]
    public void Label_names_each_chip_with_the_combined_one_reading_as_warnings()
    {
        Assert.Equal("Building", ProjectFilter.Label(ProjectFilter.Building));
        Assert.Equal("Succeeded", ProjectFilter.Label(ProjectFilter.Succeeded));
        Assert.Equal("Failed", ProjectFilter.Label(ProjectFilter.Failed));
        Assert.Equal("Skipped", ProjectFilter.Label(ProjectFilter.Skipped));
        Assert.Equal("Warnings", ProjectFilter.Label(ProjectFilter.Warn));
    }

    /// <summary>[design v1.11.0 §2.7-4] Başlık chip'i seçili kümeyi <c>" + "</c> ile listeler; sıra BARDAKİ
    /// chip sırasıdır (küme sırası belirsizdir), küme boşsa chip hiç çizilmez.</summary>
    [Fact]
    public void The_header_chip_label_joins_the_set_in_bar_order()
    {
        Assert.Null(ProjectFilter.ChipLabel(ProjectFilter.None));
        Assert.Equal("Failed", ProjectFilter.ChipLabel(Set(ProjectFilter.Failed)));
        // Kümeye ters sırada eklense de etiket bar sırasını (building → succeeded → failed → skipped → warn) izler.
        Assert.Equal("Succeeded + Failed", ProjectFilter.ChipLabel(Set(ProjectFilter.Failed, ProjectFilter.Succeeded)));
        Assert.Equal("Building + Skipped + Warnings",
            ProjectFilter.ChipLabel(Set(ProjectFilter.Warn, ProjectFilter.Skipped, ProjectFilter.Building)));
    }

    /// <summary>[design v1.11.0 §2.7-4] "Aktif çip KENDİ statü renginde yanar" — eşleme tek yerdedir.</summary>
    [Fact]
    public void An_active_chip_lights_in_its_own_status_colour()
    {
        Assert.Equal("Brush.AmberText", ProjectFilter.ActiveBrushKey(ProjectFilter.Building));
        Assert.Equal("Brush.StatusSuccessText", ProjectFilter.ActiveBrushKey(ProjectFilter.Succeeded));
        Assert.Equal("Brush.StatusFailText", ProjectFilter.ActiveBrushKey(ProjectFilter.Failed));
        Assert.Equal("Brush.StatusSkippedText", ProjectFilter.ActiveBrushKey(ProjectFilter.Skipped));
        Assert.Equal("Brush.AmberText", ProjectFilter.ActiveBrushKey(ProjectFilter.Warn));
    }
}
