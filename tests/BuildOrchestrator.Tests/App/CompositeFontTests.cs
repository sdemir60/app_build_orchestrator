using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <c>Fonts/GeistMonoConsole.CompositeFont</c>'un GERÇEKTEN yüklendiğinin guard'ı.
///
/// <para>[DEĞİŞEN KURAL] Bu dosya eskiden <c>CompositeFontSpikeTests</c> idi ve tek testi
/// <c>Skip = "spike sonucu: 1.55 tutmuyor"</c> ile kapalıydı; It-0/T56 kaydı da "LineSpacing AvalonEdit'te
/// TUTMUYOR — ölçülen 15.96 DIP @13px" diyordu. O teşhis YANLIŞTI: composite dosyası kök elemanında
/// <c>presentation</c> namespace'iyle yazılmıştı, WPF onu <c>FileFormatException</c> ile TAMAMEN reddediyordu
/// ve ölçülen 15.96 Geist Mono'nun değil, sessizce devreye giren <b>Segoe UI</b> fallback'inin
/// yüksekliğiydi (Geist Mono metrikleri 13 × 1.3 = 16.9 verir). Namespace düzeltilince aile çözülüyor, bu
/// yüzden testler açıldı ve gerçek kuralı pinliyor.</para>
///
/// <para>Kusurun 4 ay yeşil süitte saklanabilmesinin nedeni, hiçbir testin ailenin ÇÖZÜLDÜĞÜNÜ ölçmemesiydi
/// (<c>ConsoleViewTests</c> yalnız <c>FontFamily.Source</c> string'ine bakar). Ölçüm testi bu yüzden burada:
/// kimlik değil, GLİF ölçüsü kontrol edilir — çözülemeyen aile sessizce sistem fontuna düşer ve hiçbir
/// string karşılaştırması bunu görmez.</para>
/// </summary>
public class CompositeFontTests
{
    private static readonly Uri FontsBase =
        new(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts") + Path.DirectorySeparatorChar);

    /// <summary>Uygulamanın <c>AppFonts.MonoConsole</c> ile kurduğu ailenin test karşılığı.</summary>
    private static FontFamily Console() => new(FontsBase, "./#Geist Mono Console");

    private static double WidthOf(string text, FontFamily family, FontWeight weight)
    {
        var formatted = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
            12.0, Brushes.Black, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    [StaTheory]
    [InlineData(300)] // konsol gövdesi (design v1.7.0 §2.5: mono 12px, ağırlık 300)
    [InlineData(400)]
    public void Console_family_resolves_to_a_monospace_face(int weight)
    {
        var face = FontWeight.FromOpenTypeWeight(weight);

        // Monospace kanıtı: en dar (i) ve en geniş (M) glifler AYNI genişlikte olmalı. Aile çözülmezse WPF
        // sessizce orantılı sistem fontuna (Segoe UI) düşer ve ikisi ayrışır — kusur tam olarak buydu.
        double narrow = WidthOf(new string('i', 10), Console(), face);
        double wide = WidthOf(new string('M', 10), Console(), face);

        Assert.Equal(wide, narrow, precision: 3);
    }

    [StaFact]
    public void Console_family_is_not_the_system_ui_fallback()
    {
        // Çözülmediğinde düşülen yüz Segoe UI'dır; ölçüler birebir tutuyordu. Aynı olmadıklarını doğrudan
        // pinle — monospace testi tek başına "başka bir mono çözüldü" durumunu ayırt etmez.
        var light = FontWeight.FromOpenTypeWeight(300);
        double console = WidthOf(new string('M', 10), Console(), light);
        double segoe = WidthOf(new string('M', 10), new FontFamily("Segoe UI"), light);

        Assert.NotEqual(segoe, console, precision: 3);
    }

    [StaFact]
    public void LineSpacing_155_reaches_WPF_text_stack()
    {
        // Composite'in LineSpacing'i gerçekten okunuyor mu: WPF'in kendi metin yığınında 13 × 1.55 = 20.15.
        // Aile çözülmediğinde bu değer fallback fontunun yüksekliğine düşerdi.
        var formatted = new FormattedText(
            "Mg", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Console(), FontStyles.Normal, FontWeight.FromOpenTypeWeight(300), FontStretches.Normal),
            13.0, Brushes.Black, 1.0);

        Assert.Equal(20.15, formatted.Height, precision: 2);
    }

    [StaFact]
    public void AvalonEdit_renders_the_monospace_face_at_155_line_spacing()
    {
        // [DEĞİŞEN KURAL] It-0/T56 "LineSpacing 1.55 AvalonEdit'te TUTMUYOR — ölçülen 15.96 DIP @13px" diye
        // kaydedilmiş ve testi Skip'e alınmıştı. İKİ ayrı hata vardı, ikisi de bu ölçümle düştü:
        //   1) composite dosyası yanlış namespace yüzünden hiç yüklenmiyordu (bkz. sınıf doc'u),
        //   2) eski spike editörü HWND'siz Measure/Arrange ile ölçülmüştü; görsel satırlar o ağaçta hiç
        //      oluşmaz ve DefaultLineHeight gerçek değere inmez — CLAUDE.md'nin "realize window.Content
        //      üzerinde yapılır" kuralının tam olarak uyardığı tuzak. 15.96 bu artefaktın sayısıydı.
        // Gerçek pencerede realize edilince AvalonEdit ailenin LineSpacing'ini ONURLANDIRIYOR: 13 × 1.55 = 20.15.
        // Görsel satırlar ancak GERÇEK bir pencerede oluşur (headless Measure/Arrange içeriğe inmez) —
        // repo'nun realize yardımcısı kullanılır.
        static ICSharpCode.AvalonEdit.Rendering.TextView Lay(string text)
        {
            var editor = new ICSharpCode.AvalonEdit.TextEditor
            {
                FontFamily = Console(),
                FontSize = 13.0,
                FontWeight = FontWeight.FromOpenTypeWeight(300),
            };
            editor.Document.Text = text;
            DsResources.Realize(DsResources.NewHost(), editor);
            editor.TextArea.TextView.EnsureVisualLines();
            return editor.TextArea.TextView;
        }

        // Asıl guard: editörün çizdiği iki satır eşit genişlikte — konsol mono yüzle çiziliyor.
        var narrow = Lay(new string('i', 10));
        var wide = Lay(new string('M', 10));
        Assert.Equal(
            wide.GetVisualLine(1)!.TextLines[0].Width,
            narrow.GetVisualLine(1)!.TextLines[0].Width,
            precision: 3);

        // design v1.7.0 §2.5 "satır 1.55": 13 × 1.55 = 20.15.
        Assert.Equal(20.15, narrow.DefaultLineHeight, precision: 2);
    }
}
