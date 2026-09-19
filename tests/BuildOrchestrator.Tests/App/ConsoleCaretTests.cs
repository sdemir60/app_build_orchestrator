using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 6/design v1.17.0 §9 "3 — Konsol ve event stream"] Konsolun TEK canlı imleci
/// <see cref="CursorHop"/>'lu prompt caret'idir (event stream'deki gibi). AvalonEdit'in kendi ince
/// caret'i, kullanıcı konsola tıklayıp <c>TextArea</c> odağı aldığında normalde yanıp söner — iki imleç
/// birden görünür, ikinci canlı imleç sözleşmeyi bozar (design v1.17.0 §9). AvalonEdit caret'i bu yüzden
/// hiçbir durumda GÖRÜNMEZ (fırçası şeffaf) ama caret mantığı (konum takibi, klavyeyle seçim) canlı kalır
/// — <c>Focusable</c> kapatılmaz, yalnız GÖRÜNÜRLÜK bastırılır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleCaretTests
{
    [StaFact]
    public void AvalonEdit_caret_stays_invisible_when_the_console_takes_keyboard_focus()
    {
        var view = new ConsoleView();
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.AppendBatch("line1\n");
        view.Measure(new Size(400, 200));
        view.Arrange(new Rect(0, 0, 400, 200));
        view.UpdateLayout();

        view.Editor.Focus();
        Keyboard.Focus(view.Editor.TextArea);

        // Tek canlı imleç CursorHop'lu prompt caret'idir — AvalonEdit'in kendi caret'i odak alınca da
        // görünmez kalmalı (fırçası şeffaf).
        var caretBrush = view.Editor.TextArea.Caret.CaretBrush as SolidColorBrush;
        Assert.NotNull(caretBrush);
        Assert.Equal(Colors.Transparent, caretBrush!.Color);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Console_stays_focusable_and_selectable_despite_the_hidden_caret()
    {
        // Görünmezlik SEÇİMİ/odağı bozmamalı — CLAUDE.md/brief: metin seçimi ve Ctrl+C çalışmaya devam eder.
        var view = new ConsoleView();
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.AppendBatch("line1\nline2\n");
        view.UpdateLayout();

        Assert.True(view.Editor.Focusable);
        view.Editor.SelectAll();
        Assert.Equal(view.Document.Text, view.Editor.SelectedText);
        GC.KeepAlive(window);
    }
}
