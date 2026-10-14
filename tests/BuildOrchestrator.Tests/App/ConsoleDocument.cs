using System.Windows;
using BuildOrchestrator.App.Console;
using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · group B re-review] Konsol belgesini satır listesi olarak okumanın TEK yeri (kopya YASAK): anlatı store'unun
/// "bir giriş = belgede bir satır" kuralını AvalonEdit'in kendisine karşı (<c>NarrativeStoreTests</c>) ve proje sayfasının dikişini
/// bir kâhin belgeye karşı (<c>ConsoleModesTests</c>) sınayan iki test aynı ifadeyi ayrı ayrı yazmıştı.
/// <para>[idle-memory · dalga 2 son inceleme · defter 3 · O6] Konsol görünümünü süren testlerin iki ortak jesti de buradadır: ölçülüp
/// yerleştirilmiş çıplak bir görünüm (<see cref="LaidOutView"/>) ve okuyucunun tepeye kaydırması (<see cref="ScrollUpToTheTop"/>) —
/// ikisi de test sınıflarında satır satır yeniden yazılıyordu.</para>
/// </summary>
internal static class ConsoleDocument
{
    /// <summary>Belgenin gösterdiği satırlar — son satır ('\n' sonrası boş satır) hariç; ayraçlar atılır.</summary>
    public static List<string> LinesOf(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. document.Lines.Take(document.LineCount - 1).Select(document.GetText)];
    }

    /// <summary>Ölçülüp yerleştirilmiş çıplak bir konsol görünümü: <c>TextView.DefaultLineHeight</c> ve <c>VerticalOffset</c> gerçek değer
    /// alır — prepend'in ofset telafisi ve dip pini ölçülebilir. Kaynak kapsamı ve animasyon yoktur: ikisini isteyen testler (geçiş, hover
    /// bandı) görünümü bir host'ta kurar.</summary>
    public static ConsoleView LaidOutView(double width = 800, double height = 600)
    {
        var view = new ConsoleView();
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    /// <summary>Okuyucu tepeye <paramref name="attempts"/> kez kaydırır: her deneme önce tepeden uzaklaşır (arm), sonra tepeye döner —
    /// chunk loader'ın üretimdeki jesti (<see cref="ConsoleView.EvaluateChunkScroll"/>). Yüklenecek dilim kalmayınca deneme bir şey
    /// yapmaz.</summary>
    public static void ScrollUpToTheTop(ConsoleView view, int attempts = 1)
    {
        ArgumentNullException.ThrowIfNull(view);
        for (int i = 0; i < attempts; i++)
        {
            view.EvaluateChunkScroll(100.0); // arm: kullanıcı tepeden uzaklaştı
            view.EvaluateChunkScroll(0.0);   // tepe: önceki dilim
        }
    }
}
