using ICSharpCode.AvalonEdit;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/A13.2] ConsoleView: AvalonEdit tabanlı, salt-okunur, batch-append konsol control'ü. Bu iterasyonda
/// YALNIZ batching + append iskeleti test edilir — colorizer/typewriter/cascade/trim/pill It-4'tür (YAGNI).
/// </summary>
public class ConsoleViewTests
{
    [StaFact]
    public void AppendBatch_two_batches_append_in_order_with_correct_line_count()
    {
        var view = new ConsoleView();

        view.AppendBatch("line1\nline2\n");
        view.AppendBatch("line3\n");

        Assert.Equal("line1\nline2\nline3\n", view.Document.Text);
        // AvalonEdit: sondaki '\n' bir sonraki (boş) satırı başlatır -> 4 satır (line1/line2/line3/boş).
        Assert.Equal(4, view.Document.LineCount);
    }

    [StaFact]
    public void AppendBatch_never_replaces_prior_content_single_insert_only()
    {
        var view = new ConsoleView();
        view.AppendBatch("a\n");
        view.AppendBatch("b\n");
        view.AppendBatch("c\n");

        Assert.Equal("a\nb\nc\n", view.Document.Text);
    }

    [StaFact]
    public void Editor_is_read_only_no_wrap_and_uses_embedded_console_font()
    {
        var view = new ConsoleView();
        var editor = Assert.IsType<TextEditor>(view.Content);

        Assert.True(editor.IsReadOnly);
        Assert.False(editor.WordWrap);
        // FontFamily.Source, pack URI baseUri + "./#Aile Adı" ctor'unda tam olarak ikinci argümanı döner.
        Assert.Equal("./#Geist Mono Console", editor.FontFamily.Source);
    }

    [StaFact]
    public void StickToBottom_defaults_to_true()
    {
        var view = new ConsoleView();
        Assert.True(view.StickToBottom);
    }

    [StaFact]
    public void Document_can_be_swapped_for_a_different_TextDocument()
    {
        var view = new ConsoleView();
        var swapped = new ICSharpCode.AvalonEdit.Document.TextDocument("swapped content");

        view.Document = swapped;

        Assert.Same(swapped, view.Document);
        Assert.Equal("swapped content", ((TextEditor)view.Content).Document.Text);
    }
}
