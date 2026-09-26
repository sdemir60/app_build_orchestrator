namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Core.Planning;
using Xunit;

public class CycleRoundPolicyTests
{
    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    // Tur 1 yeşil geçse bile DURULMAZ: A, tur 1'de B'nin ESKİ dll'ine karşı derlenmiş olabilir.
    // Yakınsama ölçütü İKİ ARDIŞIK yeşil turdur (spec §5).
    [Fact]
    public void first_green_round_alone_does_not_converge()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set(), null));
    }

    [Fact]
    public void two_consecutive_green_rounds_converge()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(2, Set(), Set()));
    }

    // Aynı KÜME iki turdur patlıyorsa ilerleme yok. (Sayı değil küme — {A,C}→{B,D} salınımdır.)
    [Fact]
    public void identical_failure_set_two_rounds_is_no_progress()
    {
        Assert.Equal(CycleRoundDecision.NoProgress, CycleRoundPolicy.Decide(2, Set("a"), Set("a")));
    }

    [Fact]
    public void same_count_different_members_is_not_no_progress()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a", "c"), Set("b", "d")));
    }

    [Fact]
    public void shrinking_failure_set_continues()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a"), Set("a", "b")));
    }

    // Tavan: tur 1 patladı, tur 2 düzeldi ama tur 3'e kadar iki ardışık yeşil görülemedi.
    [Fact]
    public void cap_stops_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.CapReached, CycleRoundPolicy.Decide(3, Set(), Set("a")));
    }

    // Converged, cap'ten ÖNCE değerlendirilir: 3. turda iki ardışık yeşil varsa yakınsamıştır.
    [Fact]
    public void converged_wins_over_cap_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(3, Set(), Set()));
    }

    // ---------------------------------------------------------------- API kısa devresi (staleNow)
    // staleNow = son derlemesinin OKUDUĞU grup-içi çıktı yüzeyi tur sonunda DEĞİŞMİŞ üyeler. Kaynak turlar
    // arasında değişmediği için yüzeyler tek yönlü oturur; staleNow boşsa bir tur daha HİÇBİR üyenin
    // sonucunu değiştiremez — kanıt "iki ardışık yeşil tur"la aynı iddiadır, sadece daha erken elde edilir.

    [Fact] // Herkes yeşil VE kimse bayat bağlanmamış: tek turda KANITLI yakınsama — ikinci tur satın alınmaz.
    public void all_green_with_no_stale_member_converges_at_round_one()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(1, Set(), null, staleNow: Set()));
    }

    [Fact] // Yeşil ama bir üyenin okuduğu yüzey değişti: o üye eski API'ye bağlı olabilir — bir tur daha.
    public void all_green_with_a_stale_member_continues()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set(), null, staleNow: Set("b")));
    }

    [Fact] // Patlayan üyenin girdisi DEĞİŞMEDİYSE aynı derleme aynı hatayı verir: tur eklemek çözmez — umutsuz.
    public void a_failed_member_that_is_not_stale_is_no_progress_at_round_one()
    {
        Assert.Equal(CycleRoundDecision.NoProgress, CycleRoundPolicy.Decide(1, Set("a"), null, staleNow: Set()));
    }

    [Fact] // Patlayan üyenin girdisi değişti: bir sonraki tur onu düzeltebilir — devam.
    public void a_failed_member_that_is_stale_continues()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set("a"), null, staleNow: Set("a")));
    }

    [Fact] // Grup HEP BİRLİKTE persist eder: tek bir kanıtlı-umutsuz üye tüm grubun kaderidir — b'nin
           // düzelebilecek olması sonucu değiştirmez, koşu erken keser.
    public void one_hopeless_failure_is_no_progress_even_when_another_member_is_stale()
    {
        Assert.Equal(CycleRoundDecision.NoProgress, CycleRoundPolicy.Decide(1, Set("a"), null, staleNow: Set("b")));
    }

    [Fact] // Yüzey bilgisi tavanı esnetmez: hâlâ hareket varken bütçe biterse karar yine CapReached'tir.
    public void stale_info_still_respects_the_cap()
    {
        Assert.Equal(CycleRoundDecision.CapReached, CycleRoundPolicy.Decide(3, Set("a"), Set("b"), staleNow: Set("a")));
    }

    [Fact] // Kanıtlı yakınsama tavandan da önce gelir (converged_wins_over_cap ile aynı öncelik sırası).
    public void surface_converged_wins_over_cap_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(3, Set(), Set("a"), staleNow: Set()));
    }

    [Fact] // staleNow YOKKEN (null) eski kurallar birebir geçerli — mevcut çağıranlar davranış değiştirmez.
    public void a_null_stale_set_keeps_the_legacy_rules()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set(), null, staleNow: null));
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set("a"), null, staleNow: null));
    }
}
