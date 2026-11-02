using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [kullanıcı kararı 2026-11-02] Tepsideyken biten koşunun bildirim ikonunun köşesindeki SONUÇ ROZETİ: dolu bir dairenin rengi ve
/// içindeki işaret. Windows tepsi bildiriminin metnini renklendirmeye izin vermez — sonucu okumadan gösterebilen tek yüzey ikondur.
///
/// <para><b>Tek kaynak:</b> rozet şeridin terminal satırından okunur (<see cref="RibbonLine.Glyph"/>) — bildirimin başlığını ve
/// gövdesini veren AYNI satır, yani şerit ile bildirim ayrışamaz. Renk şeridin ilerleme çubuğunun dolgusudur
/// (<see cref="RibbonText.FillBrushKeyFor"/>: güçlü durum rengi), işaret satırın glyph'inin halka içi işaretidir
/// (<see cref="StatusGlyph.InnerIconKeyFor"/>). Glyph'siz bir satır — durdurulan koşu — nötrdür: atlanmanın grisi ve uygulamadaki
/// her Stop'un karesi.</para>
/// </summary>
/// <param name="FillBrushKey">Dairenin dolgusunun token anahtarı.</param>
/// <param name="MarkIconKey">İşaretin <c>Icons.xaml</c> anahtarı (boya semantiği sözlüktedir: konturlu ya da dolu).</param>
/// <param name="MarkViewBox">İşaret geometrisinin çizim kutusu (durum işaretleri 16, uygulama ikonları 24 birim).</param>
/// <param name="MarkInset">İşaretin rozetin 16 birimlik yüzünde kenarlardan içeri payı. Durum işaretleri halkanın içine çizilmiştir,
/// payları kendi kutularındadır (0); Stop karesi 24'lük kutusunun yarısından fazlasını tutar ve pay almazsa daireyi doldururdu —
/// göz kontrolü (%100, 32 px).</param>
internal readonly record struct BalloonBadge(string FillBrushKey, string MarkIconKey, double MarkViewBox, double MarkInset)
{
    /// <summary>Durdurulan (glyph'siz) koşunun nötr rozeti.</summary>
    internal static readonly BalloonBadge Stopped = new("Brush.StatusSkipped", "Icon.Stop", 24, 3);

    /// <summary>Satırın sonucu — başarı ✓ yeşil, hata ✗ kırmızı, gerisi nötr.</summary>
    internal static BalloonBadge For(RibbonLine line) => line.Glyph switch
    {
        "succeeded" => Mark("succeeded", VisualStatus.Succeeded),
        "failed" => Mark("failed", VisualStatus.Failed),
        _ => Stopped,
    };

    private static BalloonBadge Mark(string glyph, VisualStatus status) =>
        new(RibbonText.FillBrushKeyFor(glyph), StatusGlyph.InnerIconKeyFor(status)!, 16, 0);
}
