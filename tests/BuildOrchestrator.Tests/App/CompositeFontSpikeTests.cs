using System.IO;
using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

public class CompositeFontSpikeTests
{
    [StaFact(Skip = "spike sonucu: 1.55 tutmuyor — kayıt: it0-records")]
    public void CompositeFont_LineSpacing_155_holds_in_AvalonEdit() // It-0 kabul: sonuç KAYITLI (tutuyor/tutmuyor)
    {
        var baseUri = new Uri(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts") + Path.DirectorySeparatorChar);
        var family = new FontFamily(baseUri, "./#Geist Mono Console");
        var editor = new ICSharpCode.AvalonEdit.TextEditor { FontFamily = family, FontSize = 13.0 };
        editor.Document.Text = string.Join("\n", Enumerable.Repeat("10:24:31 ▸ building OSYS.UI.DMS", 10));
        editor.Measure(new Size(800, 600));
        editor.Arrange(new Rect(0, 0, 800, 600));
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        double h = view.DefaultLineHeight; // hedef: 13 × 1.55 = 20.15
        Assert.InRange(h, 19.65, 20.65);
    }
}
