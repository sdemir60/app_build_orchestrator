using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T63 fix wave / I-2] Gömülü (It-0) yazı tipi ailelerinin TEK tanım yeri. design-v1 §1.2: UI = <b>Geist</b>,
/// makinenin ürettiği her şey (console, süre, SHA, <b>sayaç</b>, yol) = <b>Geist Mono</b>; mono asla dekoratif
/// kullanılmaz ve sistem <c>Consolas</c>'ı tasarımın parçası DEĞİLDİR.
///
/// <para>Pack URI'yi her tüketicide tekrar yazmak (kopya YASAK, CLAUDE.md) yerine buradan alınır:
/// <c>GraphView</c> (etiket + panel sayacı), <c>ConsoleHeader</c> (N lines), <c>StickyLayerList</c> (katman satır
/// sayısı), <c>LatestPill</c> (chevron + etiket). XAML tarafında <c>{x:Static controls:AppFonts.Mono}</c> ile
/// tüketilir — <c>DynamicResource</c> DEĞİL, çünkü bu bir tema token'ı değil sabit bir asset'tir ve headless test
/// host'unda (merge edilmiş sözlük yok) da çözülmesi gerekir.</para>
///
/// <para>NOT: konsolun kendisi <c>Geist Mono Console</c> <b>CompositeFont</b>'unu kullanır (AvalonEdit satır
/// yüksekliği için, T56) — o ayrı bir ailedir ve <c>ConsoleView</c>'da kalır.</para>
/// </summary>
public static class AppFonts
{
    private static readonly Uri FontsBase =
        new("pack://application:,,,/BuildOrchestrator.App;component/Fonts/");

    /// <summary>Gömülü Geist Mono — makine çıktısı (sayaç/süre/SHA/yol/console chrome).</summary>
    public static FontFamily Mono { get; } = new(FontsBase, "./#Geist Mono");
}
