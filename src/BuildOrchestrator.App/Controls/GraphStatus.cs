namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T63] design-v1 DS <c>STATUS_META</c> / <c>DependencyGraphNode</c> statü kümesi — graf düğümünün renk/çerçeve
/// ailesini seçen TEK enum. (Çekirdeğin <c>ProjectRowState</c>'i ile birebir aynı değildir: graf, tasarımın
/// <c>discovered</c>/<c>cycle</c> gibi görsel durumlarını da taşır; eşleme çağıranın işidir.)
///
/// <para>[D1 fold — B3 review] Tip <c>Graph/GraphModels.cs</c>'ten buraya (nötr <c>Controls</c> namespace'ine)
/// taşındı: <see cref="StatusGlyph"/> ve <c>ProjectRow</c> gibi graf-dışı tüketiciler doğdukça enum'ın graf
/// namespace'inde durması yapaydı. Ad/değerler/dokümantasyon DEĞİŞMEDİ.</para>
/// </summary>
public enum GraphStatus
{
    Discovered,
    Queued,
    Building,
    Succeeded,
    Failed,
    Skipped,
    Cycle,
}
