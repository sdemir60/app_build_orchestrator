using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [C2] <see cref="ProjectFilter"/>: statü chip'leri + serbest metin sorgusunun eşleşme mantığı
/// (BuildApp.jsx:339-349). Sorgu yalnız proje ADINDA (yol/id'de DEĞİL) case-insensitive alt-dizedir ve
/// filtre kümesiyle AND'lenir. [design v1.20.0 §2.7] Statü chip'leri DURUM filtreleridir ve sayaçla aynı kovayı okur.
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

    /// <summary>Satıra önizleme kararı yazar (<see cref="ProjectRowViewModel.Standing"/>'in TEK girdisi).</summary>
    private static ProjectRowViewModel Decided(string name, ProjectRowState state, WillBuildReason? reason)
    {
        var r = Row(name, state);
        if (reason is { } why)
        {
            r.WillBuild = why is not (WillBuildReason.UpToDate or WillBuildReason.WaitingForDependency);
            r.WillBuildReason = why;
        }
        return r;
    }

    private static IReadOnlySet<string> Set(params string[] keys) => new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary><b>[DEĞİŞEN KURAL — design v1.20.0 §2.7]</b> Eski iddia (<c>Building_filter_includes_queued_rows</c>):
    /// "building chip'i queued satırları da kapsar" — Pending satır building filtresine giriyordu. Değişme
    /// gerekçesi: chip'ler artık gösterilen DURUMU sayar ve filtre sayaçla AYNI kovayı okur; building sayacı yalnız
    /// ŞU AN derleneni sayar (<see cref="ProjectRowViewModel.IsCompiling"/>), kuyruktaki satır amber saatle
    /// görünür ve "Building" değildir. Sırasını bekleyen döngü üyesi de Started'tadır ama Queued görünür — o da
    /// building filtresine girmez (sayaç onu da Queued'a taşır).</summary>
    [Fact]
    public void Building_matches_only_the_row_compiling_now()
    {
        var queued = Row("B", ProjectRowState.Pending);
        queued.IsRunActive = true;
        queued.InRunQueue = true;
        var waiting = Row("D", ProjectRowState.Started);
        waiting.InCycle = true;
        waiting.CycleWaiting = true;

        Assert.True(ProjectFilter.Matches(Row("A", ProjectRowState.Started), null, Set(ProjectFilter.Building)));
        Assert.False(ProjectFilter.Matches(queued, null, Set(ProjectFilter.Building)));  // kuyruk — artık DEĞİL
        Assert.False(ProjectFilter.Matches(Row("C", ProjectRowState.Succeeded), null, Set(ProjectFilter.Building)));
        Assert.False(ProjectFilter.Matches(waiting, null, Set(ProjectFilter.Building))); // Queued görünüyor
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

    /// <summary><b>[DEĞİŞEN KURAL — design v1.20.0 §2.7]</b> Eski iddia (<c>Several_chips_combine_with_or_not_and</c>):
    /// "✓ + ✗ = bu koşuda derlenenler" — ✓ koşunun başarılarını, ✗ hatalarını seçiyordu ve atlanan satır ikisinde
    /// de yoktu. Değişme gerekçesi: chip'ler artık DURUM filtreleridir. ✓ güncel çıktının hepsidir (bu koşuda
    /// atlanan güncel satır dahil), ✗ yalnız kırmızı görünendir (kanıtsız hata gri görünür → ○). Küme hâlâ
    /// VEYA ile birleşir.</summary>
    [Fact]
    public void Current_plus_failed_no_longer_means_what_this_run_built()
    {
        var both = Set(ProjectFilter.Current, ProjectFilter.Failed);

        Assert.True(ProjectFilter.Matches(Decided("A", ProjectRowState.Succeeded, WillBuildReason.NeverBuilt), null, both));
        Assert.True(ProjectFilter.Matches(Decided("B", ProjectRowState.Failed, WillBuildReason.LastFailed), null, both));
        // Bu koşunun DERLEMEDİĞİ güncel satır artık ✓'dadır.
        Assert.True(ProjectFilter.Matches(Decided("C", ProjectRowState.Skipped, WillBuildReason.UpToDate), null, both));
        Assert.True(ProjectFilter.Matches(Decided("D", ProjectRowState.Pending, WillBuildReason.UpToDate), null, both));
        // Bu koşunun kanıtsız hatası gri görünür → ✗ DEĞİL, ○.
        var greyFailure = Decided("E", ProjectRowState.Failed, WillBuildReason.NeverBuilt);
        Assert.False(ProjectFilter.Matches(greyFailure, null, both));
        Assert.True(ProjectFilter.Matches(greyFailure, null, Set(ProjectFilter.Stale)));
        Assert.False(ProjectFilter.Matches(Decided("F", ProjectRowState.Pending, WillBuildReason.SignatureChanged), null, both));
    }

    /// <summary>[design v1.20.0 §2.7] Filtre ile sayaç AYNI kovayı okur: bir chip'e basınca listede kalan satır
    /// sayısı o chip'in rozetidir. Kural iki yerde yazılsaydı sessizce ayrışırdı (kopya YASAK).</summary>
    [Fact]
    public void Each_state_filter_lists_exactly_the_rows_its_counter_counts()
    {
        var queued = Decided("Q", ProjectRowState.Pending, WillBuildReason.NeverBuilt);
        queued.IsRunActive = true;
        queued.InRunQueue = true;
        var marked = Decided("M", ProjectRowState.Pending, WillBuildReason.UpToDate);
        marked.Marked = true;
        var rows = new[]
        {
            Decided("A", ProjectRowState.Skipped, WillBuildReason.UpToDate),
            Decided("B", ProjectRowState.Succeeded, null),
            Decided("C", ProjectRowState.Failed, WillBuildReason.NeverBuilt),
            Decided("D", ProjectRowState.Failed, WillBuildReason.LastFailed),
            Decided("E", ProjectRowState.Pending, WillBuildReason.LastFailed),
            Decided("F", ProjectRowState.Pending, WillBuildReason.DepIssue),
            Decided("G", ProjectRowState.Started, WillBuildReason.UpToDate),
            Decided("U", ProjectRowState.Pending, null),
            queued, marked,
        };
        var c = RunCounters.From(rows);

        int Listed(string key) => rows.Count(r => ProjectFilter.Matches(r, null, Set(key)));
        Assert.Equal(c.Building, Listed(ProjectFilter.Building));
        Assert.Equal(c.Current, Listed(ProjectFilter.Current));
        Assert.Equal(c.Stale, Listed(ProjectFilter.Stale));
        Assert.Equal(c.Broken, Listed(ProjectFilter.Failed));
        Assert.Equal((1, 2, 2, 2), (c.Building, c.Current, c.Stale, c.Broken));
    }
    [Fact]
    public void Query_matches_the_project_name_only_and_is_ANDed_with_the_status_filter()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "pay", Set(ProjectFilter.Current)));   // ad + statü ikisi de tutar
        Assert.False(ProjectFilter.Matches(row, "pay", Set(ProjectFilter.Failed)));     // statü tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "nope", Set(ProjectFilter.Current))); // ad tutmaz → AND düşer
        Assert.False(ProjectFilter.Matches(row, "csproj", null));                       // yalnız AD (id/yol değil)
    }

    [Fact]
    public void Query_is_a_case_insensitive_substring_not_a_fuzzy_match()
    {
        var row = Row("Payments", ProjectRowState.Succeeded);

        Assert.True(ProjectFilter.Matches(row, "YMEN", null)); // case-insensitive alt-dize
        Assert.False(ProjectFilter.Matches(row, "pymnts", null)); // fuzzy DEĞİL
    }

    /// <summary><b>[DEĞİŞEN KURAL — design v1.20.0 §2.7]</b> Eski etiketler "Building" · "Succeeded" · "Failed" ·
    /// "Skipped" · "Warnings" idi. Değişme gerekçesi: chip'ler koşunun sonucunu değil çıktının DURUMUNU sayar;
    /// "Skipped" bir durum değildir (atlanan satır kendi durumunun rengini taşır) ve chip'i kalktı.</summary>
    [Fact]
    public void Label_names_each_chip_with_the_combined_one_reading_as_warnings()
    {
        Assert.Equal("Building", ProjectFilter.Label(ProjectFilter.Building));
        Assert.Equal("Up to date", ProjectFilter.Label(ProjectFilter.Current));
        Assert.Equal("To build", ProjectFilter.Label(ProjectFilter.Stale));
        Assert.Equal("Failed", ProjectFilter.Label(ProjectFilter.Failed));
        Assert.Equal("Warnings", ProjectFilter.Label(ProjectFilter.Warn));
        Assert.Equal(
            [ProjectFilter.Building, ProjectFilter.Current, ProjectFilter.Stale, ProjectFilter.Failed, ProjectFilter.Warn],
            ProjectFilter.Order);
    }

    /// <summary>[design v1.11.0 §2.7-4] Başlık chip'i seçili kümeyi <c>" + "</c> ile listeler; sıra BARDAKİ
    /// chip sırasıdır (küme sırası belirsizdir), küme boşsa chip hiç çizilmez.</summary>
    [Fact]
    public void The_header_chip_label_joins_the_set_in_bar_order()
    {
        Assert.Null(ProjectFilter.ChipLabel(ProjectFilter.None));
        Assert.Equal("Failed", ProjectFilter.ChipLabel(Set(ProjectFilter.Failed)));
        // Kümeye ters sırada eklense de etiket bar sırasını (building → current → stale → failed → warn) izler.
        Assert.Equal("Up to date + Failed", ProjectFilter.ChipLabel(Set(ProjectFilter.Failed, ProjectFilter.Current)));
        Assert.Equal("Building + To build + Warnings",
            ProjectFilter.ChipLabel(Set(ProjectFilter.Warn, ProjectFilter.Stale, ProjectFilter.Building)));
    }

    /// <summary>[design v1.11.0 §2.7-4] "Aktif çip KENDİ statü renginde yanar" — eşleme tek yerdedir.
    /// [design v1.20.0 §2.7] ○ (derlenecek) nötr gridir; ✓ ve ✗ çıktı durumunun yeşili/kırmızısı.</summary>
    [Fact]
    public void An_active_chip_lights_in_its_own_status_colour()
    {
        Assert.Equal("Brush.AmberText", ProjectFilter.ActiveBrushKey(ProjectFilter.Building));
        Assert.Equal("Brush.StatusSuccessText", ProjectFilter.ActiveBrushKey(ProjectFilter.Current));
        Assert.Equal("Brush.StatusSkippedText", ProjectFilter.ActiveBrushKey(ProjectFilter.Stale));
        Assert.Equal("Brush.StatusFailText", ProjectFilter.ActiveBrushKey(ProjectFilter.Failed));
        Assert.Equal("Brush.AmberText", ProjectFilter.ActiveBrushKey(ProjectFilter.Warn));
    }
}
