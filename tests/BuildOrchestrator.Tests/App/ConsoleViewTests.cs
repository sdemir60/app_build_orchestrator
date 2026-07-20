using System.Windows;
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

    // ---------------------------------------------------------------- [3b I-2] chunk loader GERÇEK yolu

    [StaFact]
    public void Chunk_scroll_to_top_prepends_previous_slice_contiguously_and_compensates_offset()
    {
        // GERÇEK yol: PlayCascade render dilimini (son 200) kurar; arm (tepeden uzaklaş) → scroll-to-top →
        // ConsoleView.PrependPreviousChunk contiguous eski dilimi prepend eder + VerticalOffset'i telafi eder.
        var view = new ConsoleView();
        // Layout: TextView.DefaultLineHeight/VerticalOffset gerçek değer alsın (offset telafisi ölçülebilsin).
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        var all = Enumerable.Range(0, 250).Select(i => $"line{i}").ToArray();
        view.PlayCascade(all, buildInProgress: false); // instant → son 200 (line50..line249)
        Assert.StartsWith("line50\n", view.Document.Text);
        Assert.DoesNotContain("line49\n", view.Document.Text); // ilk 50 henüz chunk loader'da

        view.EvaluateChunkScroll(100.0); // arm: kullanıcı tepeden uzaklaştı (aşağı kaydırdı)
        view.EvaluateChunkScroll(0.0);   // scroll-to-top → önceki chunk prepend edilir

        // Dikiş: line0..line249 bitişik ve TAM — tekrar YOK, kayıp YOK.
        var expected = string.Concat(all.Select(l => l + "\n"));
        Assert.Equal(expected, view.Document.Text);

        // Offset prepend edilen 50 satırın piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen 50 satırın piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Re-arm + tekrar tepe: yüklenecek daha eski satır yok → idempotent (tekrar yükleme/dup YOK).
        view.EvaluateChunkScroll(100.0);
        view.EvaluateChunkScroll(0.0);
        Assert.Equal(expected, view.Document.Text);
    }

    // ---------------------------------------------------------------- [3b M-2] proje modu follow tail-trim

    [StaFact]
    public void Project_mode_following_document_stays_capped_at_the_render_slice()
    {
        // Alta-yapışık (follow) proje logu chatty bir build'de akarken belge render dilimini AŞMAZ.
        var view = new ConsoleView();
        view.PlayCascade(new[] { "seed" }, buildInProgress: true); // _projectMode=true, StickToBottom=true (varsayılan)

        for (int i = 0; i < 400; i++) view.AppendBatch($"live{i}\n");

        Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1,
            $"follow'da belge satır sayısı ({view.Document.LineCount}) render dilimini aşmamalı");
        Assert.Contains("live399", view.Document.Text);   // en yeni korunur
        Assert.DoesNotContain("live0\n", view.Document.Text); // en eski düştü
    }

    [StaFact]
    public void Project_mode_scrolled_up_document_is_not_trimmed()
    {
        // Kullanıcı yukarı kaydırıp chunk gezerken (StickToBottom=false) tail-trim YOK — prepend'le çakışmaz.
        var view = new ConsoleView();
        view.PlayCascade(new[] { "seed" }, buildInProgress: true);
        view.StickToBottom = false;

        for (int i = 0; i < 400; i++) view.AppendBatch($"live{i}\n");

        Assert.True(view.Document.LineCount > ConsoleView.RenderSliceLines,
            "scroll-up (browse) durumunda belge kırpılmamalı");
        Assert.Contains("live0\n", view.Document.Text); // eski satırlar korunur (chunk gezme bozulmaz)
    }

    // ---------------------------------------------------------------- [3b C-1] follow-trim + scroll-to-top: delik yok

    [StaFact]
    public void Project_mode_follow_trim_then_scroll_to_top_recovers_backlog_without_a_hole()
    {
        // [C-1 regression] Follow-trim, proje modunda belge tepesinden satır siler; bu, chunk loader'ın
        // _loadedFrom index'ini de ilerletmeli. Aksi halde sonraki scroll-to-top prepend'i STALE index'e karşı
        // YANLIŞ dilimi yükler → kırpılan satırlar KALICI kaybolur (delik) ve _loadedFrom onları "yüklü" sandığı
        // için geri getirilemez. Reviewer repro şekli: _loadedFrom>0 olan bir kaskat + çok sayıda canlı append
        // (follow aktif) + tepeye kaydırma. Layout: offset telafisi ölçülebilsin diye.
        var view = new ConsoleView();
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        // 300 satır kaskat → render dilimi son 200 (orig100..orig299), _loadedFrom=100 (backlog: orig0..orig99).
        var all = Enumerable.Range(0, 300).Select(i => $"orig{i}").ToArray();
        view.PlayCascade(all, buildInProgress: true); // instant (headless), _projectMode, StickToBottom=true (varsayılan)
        Assert.StartsWith("orig100\n", view.Document.Text);
        Assert.DoesNotContain("orig99\n", view.Document.Text); // ilk 100 chunk loader backlog'unda

        // Chatty canlı build: follow aktifken 250 satır append → tail-trim TÜM orijinal satırları belgeden atar.
        for (int i = 0; i < 250; i++) view.AppendBatch($"live{i}\n");
        string liveTail = view.Document.Text;              // belgede kalan salt-live kuyruk (orijinaller kırpıldı)
        Assert.Contains("live249\n", liveTail);            // en yeni korunur
        Assert.DoesNotContain("orig", liveTail);           // tüm orijinaller belgeden kırpıldı (backlog'a düştü)

        // Kullanıcı yukarı kaydırır: arm → scroll-to-top → önceki chunk prepend edilir.
        view.EvaluateChunkScroll(100.0); // arm (tepeden uzaklaş)
        view.EvaluateChunkScroll(0.0);   // scroll-to-top → önceki chunk

        // (a) DELİK YOK: prepend, mevcut live kuyruğun ÖNÜNE TAM olarak orig100..orig299'u (kırpılan backlog'un
        // sonu) dikmeli — kuyruk aynen korunur, araya kayıp/tekrar girmez. STALE index bug'ında _loadedFrom=100
        // kalır → from=100-200→0 hesaplanır, orig0..orig99 yüklenir ve orig100..orig299 KALICI kaybolur (delik).
        string expectedAfterFirst = string.Concat(Enumerable.Range(100, 200).Select(i => $"orig{i}\n")) + liveTail;
        Assert.Equal(expectedAfterFirst, view.Document.Text); // bug'da orig0.. yüklenir → eşitlik tutmaz (RED)

        // (b) VerticalOffset prepend edilen dilimin piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen dilimin piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Tekrar tepeye kaydır: kalan backlog (orig0..orig99) da geri gelir → HİÇBİR satır kalıcı kayıp değil,
        // belge orig0..orig299 + live kuyruğu olarak TAM ve bitişik (contiguous).
        view.EvaluateChunkScroll(100.0);
        view.EvaluateChunkScroll(0.0);
        string expectedAfterSecond = string.Concat(Enumerable.Range(0, 300).Select(i => $"orig{i}\n")) + liveTail;
        Assert.Equal(expectedAfterSecond, view.Document.Text);
    }
}
