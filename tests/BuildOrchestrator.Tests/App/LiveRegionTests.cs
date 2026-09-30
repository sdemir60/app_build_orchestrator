using System.Text.RegularExpressions;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Canlı bölge duyurusunun (<c>LiveRegionChanged</c>) TEK yeri: öğenin UIA peer'ini bul (yoksa kur) ve olayı yükselt.
/// Uygulamanın canlı bölgeleri — şeridin faz metni, keşif sayacı, restart ekranının adım satırı — NE ZAMAN duyuracağına
/// kendisi karar verir, NASIL duyurulacağı ise tek yardımcıdadır (kopya YASAK, CLAUDE.md).
/// <para><b>Ölçülen kusur:</b> peer bul/kur + olay yükselt ikilisi üç yerde satır satır kopyalanmıştı
/// (<c>StickyRibbon.AnnouncePhaseIfChanged</c>, <c>ShellRoot.SetDiscoveryCount</c>,
/// <c>UpdateRestartScreen.Render</c>); iki yeni kopyanın yorumu deseni açıkça anıyordu.</para>
/// </summary>
public sealed class LiveRegionTests
{
    /// <summary>Olayın yükseltildiği biçim — <c>AutomationEvents.LiveRegionChanged</c> (tam ya da kısa ad).</summary>
    private static readonly Regex RaisesLiveRegionChanged = new(@"AutomationEvents\.LiveRegionChanged", RegexOptions.Compiled);

    /// <summary>Duyurunun meşru olduğu tek dosya (App köküne göre).</summary>
    private const string Helper = @"Controls\LiveRegion.cs";

    [Fact]
    public void Only_the_helper_raises_the_live_region_event()
    {
        var offenders = SourceGuard.ScanApp("*.cs", RaisesLiveRegionChanged, [Helper], skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "LiveRegionChanged yardımcının dışında yükseltiliyor:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Guard'ın kendi kanıtı: kural yardımcının yükseltme satırını gerçekten görüyor — boş bir tarama guard'ı
    /// sessizce yeşil bırakırdı.</summary>
    [Fact]
    public void The_rule_sees_the_helpers_own_raise()
    {
        var raises = SourceGuard.ScanApp("*.cs", RaisesLiveRegionChanged, allowedFiles: null, skipCommentLines: true);

        Assert.Contains(raises, raise => raise.StartsWith(Helper + ":", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Henüz peer'i olmayan bir öğe (realize edilmemiş, ekran okuyucu onu hiç sormamış) de duyurulabilir:
    /// yardımcı peer'i kurar — aksi halde ilk duyuru sessizce kaybolurdu. Olayın kendisi dinleyicisiz gözlemlenemez
    /// (WPF'te no-op); yüzeylerin "kaç kez duyurdu" sayaçları kendi testlerindedir.</summary>
    [StaFact]
    public void Announce_creates_the_peer_of_an_element_that_has_none()
    {
        var text = new TextBlock { Text = "29 found" };
        Assert.Null(UIElementAutomationPeer.FromElement(text)); // ön-koşul

        LiveRegion.Announce(text);

        Assert.NotNull(UIElementAutomationPeer.FromElement(text));
    }
}
