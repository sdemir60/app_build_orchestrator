using System.IO;
using BuildOrchestrator.Core.Logs;

namespace BuildOrchestrator.Tests.Logs;

public class LogChunkerTests
{
    [Fact]
    public void Empty_text_yields_single_last_chunk()
    {
        var c = LogChunker.Chunk("").Single();
        Assert.Equal((0, "", true), (c.Sequence, c.Text, c.IsLast));
    }

    [Fact]
    public void Large_text_splits_on_line_boundaries_and_reassembles()
    {
        string line = new string('x', 100) + "\n";
        string text = string.Concat(Enumerable.Repeat(line, 2000)); // ~200KB → ≥3 chunk
        var chunks = LogChunker.Chunk(text).ToList();
        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= LogChunker.MaxChunkChars));
        Assert.All(chunks.SkipLast(1), c => Assert.EndsWith("\n", c.Text)); // satır sınırında bölme
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Sequence));
        Assert.Equal(text, string.Concat(chunks.Select(c => c.Text)));
        Assert.True(chunks[^1].IsLast); Assert.All(chunks.SkipLast(1), c => Assert.False(c.IsLast));
    }
}

public class ProjectLogNamingTests
{
    [Fact]
    public void Deterministic_and_path_safe()
    {
        string a = ProjectLogNaming.FileNameFor(@"D:\Projects\Delta\OSYS\Src\P\P.csproj");
        Assert.Equal(a, ProjectLogNaming.FileNameFor(@"d:\projects\delta\osys\src\p\p.csproj")); // case-insensitive Id
        Assert.EndsWith(".log", a);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => a.Contains(c));
    }
}
