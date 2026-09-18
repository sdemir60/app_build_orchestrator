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
}
