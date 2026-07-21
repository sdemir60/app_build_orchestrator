using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T63 fix wave / I-2 · T64] Gömülü (It-0) yazı tipi ailelerinin TEK tanım yeri. design-v1 §1.2: UI = <b>Geist</b>,
/// makinenin ürettiği her şey (console, süre, SHA, <b>sayaç</b>, yol) = <b>Geist Mono</b>; mono asla dekoratif
/// kullanılmaz ve sistem <c>Consolas</c>'ı tasarımın parçası DEĞİLDİR.
///
/// <para>Pack URI'yi her tüketicide tekrar yazmak (kopya YASAK, CLAUDE.md) yerine buradan alınır:
/// <c>TrackedTextBlock</c> (caps etiketler, <see cref="Ui"/>), <c>GraphView</c> (etiket + panel sayacı),
/// <c>ConsoleHeader</c> (N lines), <c>StickyLayerList</c> (katman satır sayısı), <c>LatestPill</c>
/// (chevron <see cref="MonoConsole"/> + etiket <see cref="Mono"/>), <c>ConsoleView</c> (<see cref="MonoConsole"/>).
/// XAML tarafında <c>{x:Static controls:AppFonts.Mono}</c> ile tüketilir — <c>DynamicResource</c> DEĞİL, çünkü
/// bu bir tema token'ı değil sabit bir asset'tir ve headless test host'unda (merge edilmiş sözlük yok) da
/// çözülmesi gerekir. <c>FontAssetTests.Font_pack_uri_is_declared_in_exactly_one_place</c> bunu pinler; tek
/// bilinçli istisna <c>Spikes/FontAbWindow</c>'dur (T65 referans kabuğu, App'ten bağımsız kalır).</para>
/// </summary>
public static class AppFonts
{
    private static readonly Uri FontsBase =
        new("pack://application:,,,/BuildOrchestrator.App;component/Fonts/");

    /// <summary>Gömülü Geist (sans) — UI yüzü: caps panel/popover başlıkları, etiketler, gövde metni.</summary>
    public static FontFamily Ui { get; } = new(FontsBase, "./#Geist");

    /// <summary>Gömülü Geist Mono — makine çıktısı (sayaç/süre/SHA/yol/console chrome).</summary>
    public static FontFamily Mono { get; } = new(FontsBase, "./#Geist Mono");

    /// <summary>
    /// Gömülü <c>Geist Mono Console</c> <b>CompositeFont</b>'u (T56): AvalonEdit satır yüksekliğini sabitler
    /// ve <c>Fonts/GeistMonoConsole.CompositeFont</c>'taki FamilyMap ile Geist Mono'da bulunmayan sembolleri
    /// (⌄, ▸, ▲ …) <c>Segoe UI Symbol</c>'e düşürür. Sembol çizen her yüzey bunu kullanmalıdır — düz
    /// <see cref="Mono"/> o eşlemeyi baypas eder ve tofu (□) riski doğar.
    /// </summary>
    public static FontFamily MonoConsole { get; } = new(FontsBase, "./#Geist Mono Console");
}
