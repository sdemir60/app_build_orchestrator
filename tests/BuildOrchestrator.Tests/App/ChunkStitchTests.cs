using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3b] ChunkStitch (SAF): chunk loader dikişi (sequence-id ile tekrar/kayıp yok) + scroll-telafi offset'i
/// (prepend edilen içeriğin piksel yüksekliği kadar VerticalOffset artışı → viewport sabit).
/// </summary>
public class ChunkStitchTests
{
    private static StitchLine L(int id, string text) => new(id, text);

    [Fact]
    public void Merge_of_contiguous_chunks_keeps_ascending_order_no_gap_no_dup()
    {
        var older = new[] { L(1, "a"), L(2, "b") };
        var newer = new[] { L(3, "c"), L(4, "d") };

        var stitched = ChunkStitch.Merge(older, newer);

        Assert.Equal(new[] { 1, 2, 3, 4 }, stitched.Select(s => s.SequenceId));
        Assert.Equal(new[] { "a", "b", "c", "d" }, stitched.Select(s => s.Text));
    }

    [Fact]
    public void Merge_dedups_an_overlapping_boundary_sequence_id_no_duplicate_line()
    {
        // Disk chunk 1..3 ile canlı tampon 3..5 sınırda (id 3) çakışır — tek kopya kalmalı (tekrar yok).
        var diskChunk = new[] { L(1, "line1"), L(2, "line2"), L(3, "line3") };
        var liveTail = new[] { L(3, "line3"), L(4, "line4"), L(5, "line5") };

        var stitched = ChunkStitch.Merge(diskChunk, liveTail);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, stitched.Select(s => s.SequenceId));
        Assert.Single(stitched, s => s.SequenceId == 3); // id 3 yalnız BİR kez
    }

    [Fact]
    public void Merge_is_order_independent_prepend_then_current_equals_current_then_prepend()
    {
        var current = new[] { L(3, "c"), L(4, "d") };   // belgede yüklü (yüksek id)
        var prepended = new[] { L(1, "a"), L(2, "b") };  // tepeye eklenen eski chunk (düşük id)

        var a = ChunkStitch.Merge(current, prepended);
        var b = ChunkStitch.Merge(prepended, current);

        Assert.Equal(a.Select(s => s.SequenceId), b.Select(s => s.SequenceId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, a.Select(s => s.SequenceId)); // her iki yön de artan, kayıpsız
    }

    [Fact]
    public void Merge_handles_empty_parts()
    {
        Assert.Empty(ChunkStitch.Merge());
        Assert.Empty(ChunkStitch.Merge(Array.Empty<StitchLine>()));
        var only = ChunkStitch.Merge(Array.Empty<StitchLine>(), new[] { L(7, "x") });
        Assert.Equal(new[] { 7 }, only.Select(s => s.SequenceId));
    }

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
