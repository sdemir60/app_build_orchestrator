using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 3 · madde 15] Konsolun +∞ dibe isteği (<c>ScrollToEnd(</c>) <c>ConsoleView.xaml.cs</c>'te YALNIZ mod geçişinin pinindedir
/// (<c>PinAfterModeSwitch</c> — yeni belge sol kenardan okunur, yatay bilerek sıfırlanır). Takibin ve End'in isteği sonludur
/// (<c>RequestBottom</c>): +∞ istekte editör yalnız son boş satırı kurar, yatay içerik genişliği çöker ve yerleşim yatay okuma konumunu 0'a,
/// dikeyi geniş satırlarda dibin bir yatay çubuk boyu üstüne kenetler (B2 fix 2 · madde 3, ölçüldü). <c>ScrollToEnd</c>'i takibe geri koymak
/// ikisini birden sessizce bozardı; davranış testleri onu ancak geniş satırlı bir senaryoda görür — bu guard her yerde görür.
///
/// <para>Kardeş kaynak guard'ları gibi <see cref="SourceGuard"/> ile taranır; yorum satırları sayılmaz (kuralı ANLATAN doküman, kuralı İHLAL
/// eden kod değildir). Pinin gövdesi taramadan önce boşlukla örtülür — satır numaraları korunur.</para>
/// </summary>
public sealed class ConsoleBottomRequestGuardTests
{
    private const string FileName = "ConsoleView.xaml.cs";
    private const string PinSignature = "void PinAfterModeSwitch(";

    private static readonly Regex InfiniteBottomRequest = new(@"\bScrollToEnd\(", RegexOptions.Compiled);

    [Fact]
    public void Only_the_mode_switch_pin_asks_the_console_for_an_infinite_bottom()
    {
        // Vakum kapısı: tarama GERÇEKTEN o dosyayı görmeli.
        string relative = Assert.Single(SourceGuard.ScannedAppFiles(FileName));
        string text = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

        Assert.NotEmpty(SourceGuard.ScanText(relative, PinBodyOnly(text), InfiniteBottomRequest, skipCommentLines: true)); // pin hâlâ +∞ ister
        Assert.Empty(RequestsOutsideThePin(relative, text));
    }

    [Theory]
    [InlineData("    void AppendBatch()\n    {\n        EditorControl.ScrollToEnd();\n    }\n")]
    [InlineData("    void Other() => view.EditorControl.ScrollToEnd();\n")]
    public void The_rule_sees_an_infinite_bottom_request_outside_the_pin(string other)
    {
        string text = Pin + other;

        Assert.Single(RequestsOutsideThePin("Fake.cs", text));
    }

    [Theory]
    [InlineData("    /// <c>ScrollToEnd()</c> bir İSTEKTİR\n")]
    [InlineData("    void RequestBottom() => EditorControl.ScrollToVerticalOffset(PendingBottomOffset);\n")]
    public void The_rule_ignores_the_pin_comments_and_finite_requests(string other)
    {
        string text = Pin + other;

        Assert.Empty(RequestsOutsideThePin("Fake.cs", text));
    }

    /// <summary>Sahte bir pin — <c>ScrollToEnd(</c> gövdesinde, iç içe süslü parantezlerle.</summary>
    private const string Pin =
        "    private void PinAfterModeSwitch(bool toBottom)\n    {\n        if (!toBottom) { return; }\n"
        + "        for (int pass = 0; pass < 3; pass++)\n        {\n            EditorControl.ScrollToEnd();\n        }\n    }\n";

    /// <summary>Pinin dışındaki +∞ istekleri: pinin gövdesi (satır sonları korunarak) boşlukla örtülür, kalan metin taranır.</summary>
    private static IReadOnlyList<string> RequestsOutsideThePin(string relative, string text)
    {
        var (start, end) = PinBody(text);
        var masked = text.ToCharArray();
        for (int i = start; i < end; i++)
            if (masked[i] is not ('\n' or '\r')) masked[i] = ' ';
        return SourceGuard.ScanText(relative, new string(masked), InfiniteBottomRequest, skipCommentLines: true);
    }

    /// <summary>Yalnız pinin gövdesi — geri kalan metin boşlukla örtülür.</summary>
    private static string PinBodyOnly(string text)
    {
        var (start, end) = PinBody(text);
        var masked = text.ToCharArray();
        for (int i = 0; i < masked.Length; i++)
            if ((i < start || i >= end) && masked[i] is not ('\n' or '\r')) masked[i] = ' ';
        return new string(masked);
    }

    /// <summary>Pinin gövdesi [açılış süslü parantezi, kapanışın ötesi): imzadan sonraki ilk <c>{</c>'den eşi olan <c>}</c>'e dek. Pin yoksa
    /// fırlatır — kuralın konusu kaybolmuş demektir (yeniden adlandırma guard'ı da güncellemeli).</summary>
    private static (int Start, int End) PinBody(string text)
    {
        int signature = text.IndexOf(PinSignature, StringComparison.Ordinal);
        if (signature < 0) throw new InvalidOperationException($"'{PinSignature}' not found — the guard's subject moved");
        int open = text.IndexOf('{', signature);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return (open, i + 1);
        }
        throw new InvalidOperationException("the pin's body is not closed");
    }
}
