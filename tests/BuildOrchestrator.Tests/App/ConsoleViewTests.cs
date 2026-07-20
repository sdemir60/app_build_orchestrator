using ICSharpCode.AvalonEdit;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/A13.2] ConsoleView: AvalonEdit tabanlı, salt-okunur, batch-append konsol control'ü. Bu iterasyonda
/// YALNIZ batching + append iskeleti test edilir — colorizer/typewriter/cascade/trim/pill It-4'tür (YAGNI).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
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
        // [T56/3a] Content artık editör+overlay Grid'i; editöre public Editor erişimcisinden ulaşılır.
        var editor = view.Editor;

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
        Assert.Equal("swapped content", view.Editor.Document.Text);
    }

    // ---------------------------------------------------------------- [3b] render dilimi (son 200 satır)

    [StaFact]
    public void AppendBatch_caps_the_document_at_the_render_slice_last_lines()
    {
        var view = new ConsoleView();
        for (int i = 0; i < 250; i++) view.AppendBatch($"line{i}\n");

        // Belge son ~200 satıra kırpıldı (baştakiler düştü) — hacim/performans (Ek A #16).
        Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1,
            $"belge satır sayısı ({view.Document.LineCount}) render dilimini aşmamalı");
        Assert.Contains("line249", view.Document.Text); // en yeni korunur
        Assert.DoesNotContain("line0\n", view.Document.Text); // en eski kırpıldı
    }

    // ---------------------------------------------------------------- [3b] kaskat (reduced-motion instant yolu)

    [StaFact]
    public void PlayCascade_shows_all_lines_when_reduced_motion_instant()
    {
        // Headless testte App.Motion null → animationsEnabled=false → kaskat INSTANT (tüm satırlar, fade yok).
        var view = new ConsoleView();

        view.PlayCascade(new[] { "a", "b", "c" }, buildInProgress: false);

        Assert.Equal("a\nb\nc\n", view.Document.Text);
    }

    // ---------------------------------------------------------------- [3b] chunk prepend (scroll-telafili)

    [StaFact]
    public void PrependChunk_inserts_older_lines_at_the_top_preserving_current_content()
    {
        var view = new ConsoleView();
        view.PlayCascade(new[] { "new1", "new2" }, buildInProgress: false); // instant → "new1\nnew2\n"

        view.PrependChunk("old1\nold2\n");

        // Eski chunk tepeye eklendi; mevcut içerik korundu; sınırda tekrar/kayıp yok (dikiş).
        Assert.Equal("old1\nold2\nnew1\nnew2\n", view.Document.Text);
    }
}
