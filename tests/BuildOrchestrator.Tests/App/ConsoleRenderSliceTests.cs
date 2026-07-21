using System.Text;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>[T56/3b] ConsoleRenderSlice (SAF): metni son N satıra kırpar (render dilimi — Ek A #16/#23).
/// "N lines" sayacı bundan ETKİLENMEZ (o TAM mantıksal sayaç, VM'de) — bu yalnız RENDER dilimidir.</summary>
public class ConsoleRenderSliceTests
{
    [Fact]
    public void LastLines_keeps_only_the_final_maxLines_lines()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.Append("line").Append(i).Append('\n');

        string sliced = ConsoleRenderSlice.LastLines(sb.ToString(), 200);

        int lineCount = sliced.Count(c => c == '\n');
        Assert.Equal(200, lineCount);
        Assert.StartsWith("line300\n", sliced);        // 500-200 = ilk tutulan satır
        Assert.EndsWith("line499\n", sliced);
        Assert.DoesNotContain("line299\n", sliced);
    }

    [Fact]
    public void LastLines_returns_text_unchanged_when_under_the_limit()
    {
        const string text = "a\nb\nc\n";
        Assert.Equal(text, ConsoleRenderSlice.LastLines(text, 200));
        Assert.Equal(text, ConsoleRenderSlice.LastLines(text, 3)); // tam sınırda da tümü
    }

    [Fact]
    public void LastLines_handles_last_two_of_four()
    {
        Assert.Equal("c\nd\n", ConsoleRenderSlice.LastLines("a\nb\nc\nd\n", 2));
    }

    [Fact]
    public void LastLines_handles_text_without_trailing_newline()
    {
        Assert.Equal("b\nc", ConsoleRenderSlice.LastLines("a\nb\nc", 2));
    }

    [Fact]
    public void LastLines_handles_empty_and_nonpositive()
    {
        Assert.Equal("", ConsoleRenderSlice.LastLines("", 200));
        Assert.Equal("a\nb\n", ConsoleRenderSlice.LastLines("a\nb\n", 0));
    }
}
