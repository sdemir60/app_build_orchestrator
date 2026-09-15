using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 6/design v1.17.0 §9 "3 — Konsol ve event stream"] Konsol gövdesinin imleci standart OK'tur (el işareti
/// yalnız tıklanabilir öğelere aittir) ve imlecin altındaki satır tam genişlik, <c>Brush.Surface</c> zeminli bir
/// bantla işaretlenir. Prototip: <c>BuildApp.jsx</c> konsol gövdesi <c>cursor: default</c> (~satır 1080) +
/// <c>.bo-cline</c> (satır 62-63).
///
/// <para><b>Neden <see cref="ConsoleView.UpdateHoverBand"/> bir Y PARAMETRESİ alır, gerçek <c>MouseMove</c>'dan
/// okumaz:</b> WPF'in gerçek <c>MouseDevice</c> konumu (<c>MouseEventArgs.GetPosition</c>) işletim sistemi
/// imlecinin GERÇEK ekran konumunu sorar — headless'ta simüle edilemez (bkz. <c>ActionBarHoverTests</c>'in aynı
/// gözlemi). Desen <see cref="ConsoleView.EvaluateChunkScroll"/> ile AYNIDIR: üretim <c>MouseMove</c> kablosu
/// gerçek konumu ÇIKARIP bu metoda GEÇER, testler üretimin ÇAĞIRDIĞI metodun ta kendisini doğrudan sürer
/// (paralel bir kopya yol DEĞİL).</para>
///
/// <para><b>Cursor testleri neden gerçek bir olay yükseltir:</b> AvalonEdit'in <c>SelectionMouseHandler</c>'ı
/// IBeam'i statik <c>Cursor</c> özelliği üzerinden DEĞİL, yönlendirilmiş <c>QueryCursor</c> olayını
/// <c>TextArea</c> seviyesinde ele alarak dayatır (decompile ile doğrulandı — <c>TextArea</c>/<c>TextView</c>
/// hiçbir yerde kendi <c>Cursor</c>'ını atamaz). <c>RaiseEvent</c> GERÇEK WPF yönlendirme motorunu (kabarcıklanma
/// + kayıtlı sınıf handler'ları) sürer ve konum sorgusuna BAĞLI DEĞİLDİR — <see cref="ConsoleView.ForceArrowCursor"/>
/// konuma bakmaksızın SON SÖZÜ söyler, bu yüzden gerçek donanım imleç konumu testin sonucunu etkilemez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleHoverBandTests
{
    private static ConsoleView Realized(double width = 200, double height = 200)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    // ---------------------------------------------------------------- saf çekirdek: ConsoleHoverBand.LineAt

    [Fact]
    public void LineAt_returns_the_line_whose_range_contains_documentY()
    {
        var lines = new (double Top, double Height)[] { (0, 20), (20, 20), (40, 20) };

        Assert.Equal((20.0, 20.0), ConsoleHoverBand.LineAt(lines, 25));
    }

    [Fact]
    public void LineAt_top_boundary_is_inclusive_bottom_boundary_is_exclusive()
    {
        var lines = new (double Top, double Height)[] { (0, 20), (20, 20) };

        Assert.Equal((20.0, 20.0), ConsoleHoverBand.LineAt(lines, 20)); // tam sınırda: alttaki satır
        Assert.Equal((0.0, 20.0), ConsoleHoverBand.LineAt(lines, 19.999));
    }

    [Fact]
    public void LineAt_returns_null_outside_every_range()
    {
        var lines = new (double Top, double Height)[] { (10, 20) };

        Assert.Null(ConsoleHoverBand.LineAt(lines, 5));
        Assert.Null(ConsoleHoverBand.LineAt(lines, 30));
    }

    [Fact]
    public void LineAt_returns_null_for_an_empty_list()
    {
        Assert.Null(ConsoleHoverBand.LineAt([], 0));
    }

    // ---------------------------------------------------------------- imleç: standart ok

    [StaFact]
    public void Console_body_cursor_is_Arrow_on_the_editor_TextArea_and_TextView()
    {
        var view = Realized();

        Assert.Equal(Cursors.Arrow, view.Editor.Cursor);
        Assert.Equal(Cursors.Arrow, view.Editor.TextArea.Cursor);
        Assert.Equal(Cursors.Arrow, view.Editor.TextArea.TextView.Cursor);
    }

    /// <summary>AvalonEdit'in GERÇEK <c>SelectionMouseHandler</c>'ı devrede (metin yüklü, layout gerçek) — olay
    /// <c>TextView</c>'den kabarcıklanarak yükseltilir, tıpkı üretimde olduğu gibi. AvalonEdit'in kararı (IBeam
    /// ya da sürüklemede Arrow) ne olursa olsun son değer Arrow olmalıdır.</summary>
    [StaFact]
    public void QueryCursor_bubbling_from_the_real_TextView_resolves_to_Arrow_even_though_AvalonEdit_claims_it_first()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\nline2\n");
        view.UpdateLayout();

        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
        view.Editor.TextArea.TextView.RaiseEvent(args);

        Assert.Equal(Cursors.Arrow, args.Cursor);
        Assert.True(args.Handled);
    }

    /// <summary>Üretimin ÇAĞIRDIĞI metodun ta kendisi (<see cref="ConsoleView.ForceArrowCursor"/>) — AvalonEdit
    /// zaten IBeam'i "ele almış" (<c>Handled=true</c>) olsa bile SON SÖZ bize aittir (<c>handledEventsToo:true</c>
    /// kablosunun sözleşmesi).</summary>
    [StaFact]
    public void ForceArrowCursor_overrides_a_cursor_AvalonEdit_already_claimed_as_handled()
    {
        var view = Realized();
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.IBeam, Handled = true };

        view.ForceArrowCursor(view.Editor, args);

        Assert.Equal(Cursors.Arrow, args.Cursor);
        Assert.True(args.Handled);
    }

    // ---------------------------------------------------------------- satır hover bandı

    [StaFact]
    public void UpdateHoverBand_bands_the_real_visual_line_under_the_given_Y_with_full_width_and_Brush_Surface()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\nline2\nline3\n");
        view.UpdateLayout();

        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.VisualLinesValid);
        Assert.True(textView.VisualLines.Count >= 3);
        var target = textView.VisualLines[2]; // "line2"
        double mouseY = target.VisualTop - textView.ScrollOffset.Y + 1; // satırın 1px içi

        view.UpdateHoverBand(mouseY);

        var expected = textView.TransformToAncestor(view.TiltHost)
            .Transform(new Point(0, target.VisualTop - textView.ScrollOffset.Y));
        Assert.Equal(expected.Y, view.HoverBand.Margin.Top, precision: 2);
        Assert.Equal(target.Height, view.HoverBand.Height, precision: 2);
        Assert.Equal(HorizontalAlignment.Stretch, view.HoverBand.HorizontalAlignment);

        var fill = Assert.IsType<SolidColorBrush>(view.HoverBand.Fill);
        var surface = DsResources.TokenColor(view, "Brush.Surface");
        Assert.Equal(surface, fill.Color);
    }

    /// <summary>Bant <c>PART_TiltHost</c>'un TAM genişliğini kaplar — editörün kendi 12px iç dolgusunu (Padding)
    /// aşar (design v1.17.0 §9: "gövde padding'ini aşar: sol/sağ 12px dahil panel kenarından kenara").</summary>
    [StaFact]
    public void UpdateHoverBand_spans_the_full_panel_width_beyond_the_editor_padding()
    {
        var view = Realized(width: 240, height: 200);
        view.AppendBatch("line0\nline1\n");
        view.UpdateLayout();

        view.UpdateHoverBand(1);
        view.UpdateLayout();

        Assert.Equal(view.TiltHost.ActualWidth, view.HoverBand.ActualWidth, precision: 1);
        Assert.True(view.HoverBand.ActualWidth > 200); // editörün 12+12px iç dolgusunu belirgin biçimde aşıyor
    }

    [StaFact]
    public void UpdateHoverBand_outside_every_line_hides_the_band()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\n");
        view.UpdateLayout();

        view.UpdateHoverBand(1); // önce görünür yap
        view.UpdateHoverBand(100_000); // belgenin dışında bir Y

        var fill = Assert.IsType<SolidColorBrush>(view.HoverBand.Fill);
        Assert.Equal(0, fill.Color.A);
    }

    /// <summary>Fare panelden çıkınca bant kalkar (design v1.17.0 §9) — GERÇEK <c>MouseLeave</c> olayı, üretimin
    /// ctor'da kurduğu KABLONUN ta kendisini sürer (bir yardımcı metodu değil).</summary>
    [StaFact]
    public void Real_MouseLeave_on_the_TextView_hides_the_band()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\n");
        view.UpdateLayout();
        view.UpdateHoverBand(1);
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0);

        view.Editor.TextArea.TextView.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

        Assert.Equal(0, ((SolidColorBrush)view.HoverBand.Fill).Color.A);
    }

    /// <summary>Aktif prompt satırı overlay'i bant tarafından etkilenmez: overlay ayrı bir öğedir, bandın
    /// Margin/Height'ı ona hiç dokunmaz.</summary>
    [StaFact]
    public void UpdateHoverBand_never_touches_the_active_line_overlay()
    {
        var view = Realized();
        view.ShowReady();
        view.UpdateLayout();
        var marginBefore = view.ActiveLineOverlay.Margin;
        var visibilityBefore = view.ActiveLineOverlay.Visibility;

        view.UpdateHoverBand(1);

        Assert.Equal(marginBefore, view.ActiveLineOverlay.Margin);
        Assert.Equal(visibilityBefore, view.ActiveLineOverlay.Visibility);
    }

    // ---------------------------------------------------------------- seçim işlevi etkilenmez

    /// <summary>Renk kuralı, hiza, satır yüksekliği, METİN SEÇİLEBİLİRLİĞİ değişmez (design v1.17.0 §9): hover
    /// bandı ve zorunlu-Arrow kablosu eklendikten SONRA da gerçek bir seçim GERÇEKTEN kurulabilmeli.</summary>
    [StaFact]
    public void Text_selection_still_works_after_the_hover_and_cursor_wiring_run()
    {
        var view = Realized();
        view.AppendBatch("hello\nworld\n");
        view.UpdateLayout();

        view.UpdateHoverBand(1);
        view.Editor.TextArea.TextView.RaiseEvent(new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent });

        view.Editor.Select(0, 5);

        Assert.Equal(5, view.Editor.SelectionLength);
        Assert.Equal("hello", view.Editor.SelectedText);
    }
}
