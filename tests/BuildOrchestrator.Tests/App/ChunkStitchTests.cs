using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3b] ChunkStitch (SAF): chunk loader'ın scroll-telafi offset matematiği — prepend edilen içeriğin piksel
/// yüksekliği kadar VerticalOffset artışı → viewport sabit. Dikişin kendisi (contiguous, tekrar/kayıp yok)
/// gerçek yolu ConsoleView.PrependPreviousChunk üzerinden <see cref="ConsoleViewTests"/>'te; sınır dedup'ı ise
/// RunViewModel.OnProjectLogChunk üzerinden VM testlerinde doğrulanır.
/// </summary>
public class ChunkStitchTests
{
    [Fact]
    public void CompensatedOffset_adds_the_prepended_pixel_height_so_the_viewport_stays_stable()
    {
        // Kullanıcı offset 300px'de; tepeye 5 satır (5*18=90px) prepend edildi → viewport'un AYNI satırı
        // görmesi için offset 90px artmalı.
        Assert.Equal(390.0, ChunkStitch.CompensatedOffset(300.0, 90.0), 3);
        Assert.Equal(90.0, ChunkStitch.CompensatedOffset(0.0, 90.0), 3);   // tepedeyken de telafi
        Assert.Equal(300.0, ChunkStitch.CompensatedOffset(300.0, 0.0), 3); // boş prepend → değişmez
        Assert.Equal(300.0, ChunkStitch.CompensatedOffset(300.0, -50.0), 3); // negatif yükseklik yok sayılır
    }
}
