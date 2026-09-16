using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.19.0 §2.11] What's new'in sticky sol kolonunun SAF kararı — CSS <c>position: sticky; top: 0</c>'ın
/// WPF karşılığı: <c>clamp(scrollTop − blockTop, 0, blockHeight − columnHeight)</c>. Kolon bloğun üstü görünür
/// oldukça yerinde durur, blok yukarı kaydıkça ona yapışır, ama bloğun ALTINI hiç aşmaz; blok kolondan kısaysa
/// hiç kaymaz.
/// </summary>
public class StickyColumnTests
{
    /// <summary>Bloğun üstü henüz viewport'un altındaysa (ya da tam hizadaysa) kolon kaymaz — alt sınır 0.</summary>
    [Fact]
    public void The_column_stays_put_while_the_block_top_is_visible()
    {
        Assert.Equal(0.0, StickyColumn.Offset(scrollTop: 100, blockTop: 300, blockHeight: 400, columnHeight: 50));
        Assert.Equal(0.0, StickyColumn.Offset(scrollTop: 300, blockTop: 300, blockHeight: 400, columnHeight: 50));
    }

    /// <summary>Blok yukarı kaydıkça kolon viewport'un üstüne yapışır: kayma = scrollTop − blockTop.</summary>
    [Fact]
    public void The_column_follows_the_scroll_once_the_block_top_has_passed()
    {
        Assert.Equal(120.0, StickyColumn.Offset(scrollTop: 420, blockTop: 300, blockHeight: 400, columnHeight: 50));
    }

    /// <summary>Üst sınır: kolonun altı bloğun altını aşamaz — kayma en çok blockHeight − columnHeight.</summary>
    [Fact]
    public void The_column_never_moves_past_the_bottom_of_its_block()
    {
        Assert.Equal(350.0, StickyColumn.Offset(scrollTop: 900, blockTop: 300, blockHeight: 400, columnHeight: 50));
    }

    /// <summary>Blok kolondan kısa (ya da eşit) ise kayacak yer yoktur — negatif üst sınır 0'a iner, istisna atılmaz.</summary>
    [Fact]
    public void A_block_shorter_than_its_column_never_shifts_it()
    {
        Assert.Equal(0.0, StickyColumn.Offset(scrollTop: 500, blockTop: 300, blockHeight: 40, columnHeight: 50));
        Assert.Equal(0.0, StickyColumn.Offset(scrollTop: 500, blockTop: 300, blockHeight: 50, columnHeight: 50));
    }
}
