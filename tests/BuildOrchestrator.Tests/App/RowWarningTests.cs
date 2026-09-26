using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 6 review round 1] <see cref="RowWarning.WaitingForDependencyText"/> — proje sayfasının "bu koşu bir
/// bağımlılığı bekliyor" cümlesi. Saf, WPF'siz test edilir.
///
/// <para><b>[DEĞİŞEN KURAL — nereden geldi]</b> Bu davranış (virgülle birleştirme, ortak önekle kısaltma, boş
/// kök listesinde parantezsiz cümle) Task 6'dan önce <c>DecisionLabelTests.Multiple_roots_are_comma_joined_and_short_named</c>
/// ve <c>DecisionLabelTests.An_empty_root_list_does_not_print_empty_parentheses</c> tarafından pinleniyordu —
/// o zaman <c>WaitingForDependency</c> gerekçesi <c>DecisionLabel.For</c>'un kendi dalıydı (kökleri okuyup
/// tooltip'e yazıyordu). Task 6 o dalı <c>UpToDate</c> ile birleştirdi (design v1.20.0 §2.4: karar etiketi
/// artık hangi kökün beklendiğini söylemiyor, yalnız uyarı üçgeni ve proje sayfası söylüyor) ve cümlenin
/// üretimini <see cref="RowWarning"/>'e taşıdı — pinler burada, aynı iddiayla, yeni eve taşındı.</para>
///
/// <para><b>[Task 8]</b> <see cref="RowWarning.For"/>'un ÖNCELİK SIRASI (unconverged &gt; unsettled &gt; inCycle
/// &gt; dep-issue &gt; null) ve <see cref="RowWarning.CycleUnsettled"/> metninin İLK assert'i de bu sınıfa
/// eklendi — önceki sürüm yalnız <see cref="RowWarning.WaitingForDependencyText"/>'i kapsıyordu, öncelik
/// sırası mutasyona tamamen açıktı.</para>
/// </summary>
public class RowWarningTests
{
    /// <summary>Birden çok kök virgülle, ortak önek kısaltılarak (üçgenin kök-kısaltma diliyle AYNI otorite,
    /// <see cref="Graph.GraphNode.ShortLabel"/> — kopya YASAK), ardından sabit bekleme kuyruğu.</summary>
    [Fact]
    public void Multiple_roots_are_comma_joined_and_short_named()
        => Assert.Equal("Dependency issue: A, Zeta — rebuilds once that dependency is healthy again",
            RowWarning.WaitingForDependencyText(["OSYS.A", "OSYS.Zeta"], "OSYS."));

    /// <summary>Tek kök de aynı biçimi izler — virgül yok, kısaltma ve kuyruk aynı.</summary>
    [Fact]
    public void A_single_root_is_named_without_a_comma()
        => Assert.Equal("Dependency issue: Sales.Data — rebuilds once that dependency is healthy again",
            RowWarning.WaitingForDependencyText(["OSYS.Sales.Data"], "OSYS."));

    /// <summary>[Task 4 review — M3] Kökler bilinmiyorsa (savunmacı — <c>WillBuildEvaluator</c>'ın kuralı
    /// gereği pratikte olmaz) parantez BOŞ basılmaz; cümle köksüz de doğru okunur, büyük harfle başlar (kökle
    /// gelen kuyruğun küçük harfli hâliyle AYNI cümlenin başı büyütülmüş biçimi — tek sabitten türer).</summary>
    [Fact]
    public void A_null_root_list_does_not_print_empty_parentheses()
        => Assert.Equal("Rebuilds once that dependency is healthy again",
            RowWarning.WaitingForDependencyText(null, ""));

    /// <summary>Boş liste (null'dan AYRI bir olgu — motorun "hiç kök yok" cevabı) da aynı nötr cümleye düşer.</summary>
    [Fact]
    public void An_empty_root_list_does_not_print_empty_parentheses()
        => Assert.Equal("Rebuilds once that dependency is healthy again",
            RowWarning.WaitingForDependencyText([], ""));

    // ---------------------------------------------------------------- [Task 8] For — öncelik sırası [PİN]

    /// <summary>[Task 8] <see cref="RowWarning.For"/>'un ÖNCELİK SIRASI: unconverged &gt; unsettled &gt; inCycle
    /// &gt; dep-issue. Üç satırda da <c>depIssues</c> DOLU tutulur (aynı temsili kök "X") — üstteki bayrağın onu
    /// GERÇEKTEN ezdiğini kanıtlamak için; "zaten boştu" itirazı bu şekilde kapanır.
    /// <see cref="RowWarning.CycleUnsettled"/> burada İLK KEZ metin olarak assert ediliyor (önceden hiçbir test
    /// dosyasında string'in KENDİSİ sınanmıyordu, yalnız üretici bool bayrak taşınıyordu).</summary>
    [Theory]
    [InlineData(true, true, true, RowWarning.CycleUnconverged)]  // unconverged + unsettled + inCycle → EN KESİN kazanır
    [InlineData(true, true, false, RowWarning.CycleUnsettled)]   // unsettled + inCycle (unconverged yok) → unsettled kazanır
    [InlineData(true, false, false, RowWarning.InCycle)]         // yalnız sıradan üyelik (+ depIssues) → üyelik kazanır
    public void The_most_severe_cycle_reason_wins_even_when_a_dep_issue_is_also_present(
        bool inCycle, bool cycleUnsettled, bool cycleUnconverged, string expected)
        => Assert.Equal(expected, RowWarning.For(inCycle, cycleUnsettled, cycleUnconverged, ["X"], ""));

    /// <summary>Üç döngü bayrağı da yokken tek kök: önek + kısa ad, sayaç YOK
    /// (<see cref="RowWarning.DepIssuePrefix"/>'ten türetilir — kopya YASAK, literal yazılmaz).</summary>
    [Fact]
    public void For_reports_a_single_dep_issue_when_no_cycle_flag_is_set()
        => Assert.Equal(RowWarning.DepIssuePrefix + "A",
            RowWarning.For(false, false, false, ["A"], ""));

    /// <summary>Üç kök: ilk adın kısası + kalanların SAYISI ("+2"), ortak önek atılarak — mevcut
    /// <see cref="ProjectRowTests.Dep_tooltip_is_one_line_with_the_first_name_and_a_plus_count"/> ile AYNI
    /// girdi/çıktı: pure katmandaki bu iddia o realize testiyle ÇELİŞMEZ, aynı davranışı bir seviye aşağıda
    /// doğrular.</summary>
    [Fact]
    public void For_shortens_three_dep_issues_to_the_first_short_name_and_a_plus_count()
        => Assert.Equal(RowWarning.DepIssuePrefix + "Sales.Core +2",
            RowWarning.For(false, false, false,
                ["OSYS.Sales.Core", "OSYS.Billing.Core", "OSYS.Base"], "OSYS."));

    /// <summary>Hiçbir sinyal yok (üç bayrak false, depIssues null) → üçgen basılmaz.</summary>
    [Fact]
    public void For_returns_null_when_there_is_no_signal_at_all()
        => Assert.Null(RowWarning.For(false, false, false, null, ""));
}
