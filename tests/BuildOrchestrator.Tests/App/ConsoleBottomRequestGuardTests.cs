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
/// <para>Kardeş kaynak guard'ları gibi <see cref="SourceGuard"/> ile taranır. Kaynak taramadan önce yorumsuz, literalsiz koda indirilir
/// (<see cref="SourceLiterals.CodeOnly"/> — <c>IoParallelismGuardTests</c>'in yolu; konumlar ve satır sonları korunur): kuralı ANLATAN
/// doküman kuralı İHLAL eden kod değildir, bir yorumdaki ya da literaldeki süslü parantez de pinin sınırını değiştirmez. Pinin gövdesi
/// boşlukla örtülür — satır numaraları korunur.</para>
/// <para>[son temizlik · madde 8 · DEĞİŞEN KURULUM] ESKİ: yalnız baştan sona yorum olan satırlar atlanıyor, pinin gövdesi ham metinde süslü
/// parantezler elle sayılarak bulunuyordu. GEREKÇE: pinin gövdesindeki bir yorumun ya da literalin <c>}</c>'i gövdeyi orada bitiriyor, pinin
/// kendi isteği "pinin dışında" sayılıyordu (<see cref="A_brace_in_a_comment_or_a_string_inside_the_pin_does_not_end_it"/>'in kırmızısı).</para>
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

        Assert.NotEmpty(RequestsInsideThePin(relative, text)); // pin hâlâ +∞ ister
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

    /// <summary>
    /// [son temizlik · madde 8] Pinin gövdesindeki bir yorumun ya da metin literalinin süslü parantezi pini bitirmez: tarama kaynağı önce
    /// yorumsuz, literalsiz koda indirir (<see cref="SourceLiterals.CodeOnly"/>), gövdenin sınırı yalnız kodun parantezlerinden bulunur.
    /// <para>KIRMIZI (değişiklikten önce): gövde ham metinde elle sayılıyordu — yorumdaki ya da literaldeki <c>}</c> gövdeyi orada bitiriyor,
    /// pinin kendi <c>ScrollToEnd</c>'i "pinin dışında" sayılıyordu.</para>
    /// </summary>
    [Theory]
    [InlineData("            // kapanış } burada değil\n")]
    [InlineData("            string note = \"}\";\n")]
    public void A_brace_in_a_comment_or_a_string_inside_the_pin_does_not_end_it(string line)
    {
        string text = "    private void PinAfterModeSwitch(bool toBottom)\n    {\n" + line + "        EditorControl.ScrollToEnd();\n    }\n";

        Assert.Empty(RequestsOutsideThePin("Fake.cs", text));
    }

    /// <summary>Sahte bir pin — <c>ScrollToEnd(</c> gövdesinde, iç içe süslü parantezlerle.</summary>
    private const string Pin =
        "    private void PinAfterModeSwitch(bool toBottom)\n    {\n        if (!toBottom) { return; }\n"
        + "        for (int pass = 0; pass < 3; pass++)\n        {\n            EditorControl.ScrollToEnd();\n        }\n    }\n";

    /// <summary>Pinin dışındaki +∞ istekleri: kod (<see cref="SourceLiterals.CodeOnly"/>) pinin gövdesi örtülerek taranır.</summary>
    private static IReadOnlyList<string> RequestsOutsideThePin(string relative, string text) => Requests(relative, text, insideThePin: false);

    /// <summary>Pinin içindeki +∞ istekleri: kodun yalnız pinin gövdesi taranır.</summary>
    private static IReadOnlyList<string> RequestsInsideThePin(string relative, string text) => Requests(relative, text, insideThePin: true);

    /// <summary>Kaynağı yorumsuz, literalsiz koda indirir, pinin gövdesinin içini ya da dışını (satır sonları korunarak) boşlukla örter ve
    /// kalan kodu tarar.</summary>
    private static IReadOnlyList<string> Requests(string relative, string text, bool insideThePin)
    {
        string code = SourceLiterals.CodeOnly(text);
        var (start, end) = PinBody(code);
        var masked = code.ToCharArray();
        for (int i = 0; i < masked.Length; i++)
            if ((i >= start && i < end) != insideThePin && masked[i] is not ('\n' or '\r')) masked[i] = ' ';
        return SourceGuard.ScanText(relative, new string(masked), InfiniteBottomRequest);
    }

    /// <summary>Pinin gövdesi [açılış süslü parantezi, kapanışın ötesi): imzadan sonraki ilk <c>{</c>'den eşi olan <c>}</c>'e dek — KODDA
    /// (yorum ve literaller boşlukla örtülmüştür, onların parantezleri sayılmaz). Pin yoksa fırlatır — kuralın konusu kaybolmuş demektir
    /// (yeniden adlandırma guard'ı da güncellemeli).</summary>
    private static (int Start, int End) PinBody(string code)
    {
        int signature = code.IndexOf(PinSignature, StringComparison.Ordinal);
        if (signature < 0) throw new InvalidOperationException($"'{PinSignature}' not found — the guard's subject moved");
        int open = code.IndexOf('{', signature);
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return (open, i + 1);
        }
        throw new InvalidOperationException("the pin's body is not closed");
    }
}
