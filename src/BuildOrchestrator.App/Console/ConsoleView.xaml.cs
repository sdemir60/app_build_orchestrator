using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/A13.2] AvalonEdit tabanlı, salt-okunur, batch-append canlı konsol control'ü. Bu iterasyon YALNIZ
/// batching + append iskeletini kurar: <see cref="AppendBatch"/>, TAM OLARAK
/// <c>Document.BeginUpdate()</c> → tek <c>Document.Insert(Document.TextLength, text)</c> →
/// <c>Document.EndUpdate()</c> sırasını izler; satır başına <c>Dispatcher.Invoke</c> YOK — çağıran
/// (Task 12) batch başına TEK <c>Dispatcher.InvokeAsync</c> ile buraya taşır. Colorizer, typewriter,
/// cascade pop-in, trim/tampon, "⌄ latest" pill → It-4 (YAGNI, burada YOK).
/// </summary>
public partial class ConsoleView : UserControl
{
    // Gömülü Geist Mono Console composite font (It-0 asset'i, Fonts\GeistMonoConsole.CompositeFont).
    // Aynı ";component/Fonts/" + "./#Aile Adı" kalıbı, spike testindeki (CompositeFontSpikeTests) dosya
    // yolu tabanlı kullanımın pack URI karşılığıdır — App'in kendi assembly'sinden gömülü kaynak okunur.
    private static readonly FontFamily ConsoleFontFamily = new(
        new Uri("pack://application:,,,/BuildOrchestrator.App;component/Fonts/"),
        "./#Geist Mono Console");

    public ConsoleView()
    {
        InitializeComponent();
        Editor.FontFamily = ConsoleFontFamily;
    }

    /// <summary>Task 12'nin run/proje görünümü arasında doküman değiştirebilmesi için dışa açılır.</summary>
    public TextDocument Document
    {
        get => Editor.Document;
        set => Editor.Document = value;
    }

    /// <summary>true iken her <see cref="AppendBatch"/> sonrası editör en alta kaydırılır. Varsayılan true —
    /// canlı build akışı doğal olarak alta yapışık izlenir (jump-back "⌄ latest" pill'i It-4).</summary>
    public bool StickToBottom { get; set; } = true;

    /// <summary>
    /// UI thread'inde çağrılır. TEK batch ekler — asla satır satır bölmez, asla <c>Dispatcher.Invoke</c>
    /// çağırmaz (o, çağıranın/Task 12'nin sorumluluğu). [A13.2 ZORUNLU sıra]
    /// </summary>
    public void AppendBatch(string text)
    {
        var document = Editor.Document;
        document.BeginUpdate();
        try
        {
            document.Insert(document.TextLength, text);
        }
        finally
        {
            document.EndUpdate();
        }
        if (StickToBottom)
            Editor.ScrollToEnd();
    }
}
