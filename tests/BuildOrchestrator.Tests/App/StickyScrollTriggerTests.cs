using System.Windows;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · madde 1.4] Yapışık katman başlıklarının <b>TETİKLEYİCİSİ</b>: gerçek <c>ScrollViewer.ScrollChanged</c>.
///
/// <para><b>Ölçülmüş boşluk:</b> <c>StickyOverlayTests.cs:53</c> ve kardeşleri overlay'i
/// <c>list.UpdateOverlay(288)</c> ile <b>DOĞRUDAN</b> besliyor. Yani "verilen offset için doğru başlıklar
/// seçiliyor mu" pinliydi; "gerçek kaydırma overlay'i besliyor mu" değil —
/// <c>StickyLayerList.xaml.cs:56</c>'daki <c>Scroll.ScrollChanged</c> aboneliği silinse suite yeşil kalırdı.</para>
///
/// <para>Bu test listeyi GERÇEKTEN realize eder, <c>Scroll</c>'u GERÇEKTEN kaydırır ve overlay'in yapışık
/// başlıklarını okur — <c>UpdateOverlay</c>'e hiç dokunmaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyScrollTriggerTests
{
    private sealed record Proj(string Name);

    // StickyOverlayTests ile AYNI topoloji: 3 katman (3+5+6 satır) → 3×24 + 14×36 = 576px.
    private static IReadOnlyList<StickyLayerList.LayerGroup> SampleGroups() =>
    [
        new("L0", [new Proj("a"), new Proj("b"), new Proj("c")]),
        new("L1", [new Proj("d"), new Proj("e"), new Proj("f"), new Proj("g"), new Proj("h")]),
        new("L2", [new Proj("i"), new Proj("j"), new Proj("k"), new Proj("l"), new Proj("m"), new Proj("n")]),
    ];

    /// <summary>ÜRETİM SIRASI (A12 dersi): kabuk önce realize edilir, gruplar SONRA akar.</summary>
    private static StickyLayerList RealizeThenFeed(out Window window)
    {
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, list);
        list.SetGroups(SampleGroups());
        list.UpdateLayout();
        return list;
    }

    private static IReadOnlyList<StuckHeader> Overlay(StickyLayerList list) =>
        (IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource;

    [StaFact]
    public void Really_scrolling_the_list_drives_the_sticky_overlay_through_ScrollChanged()
    {
        var list = RealizeThenFeed(out var window);

        // Ön-koşul: içerik GERÇEKTEN kaydırılabilir olmalı — aksi halde test vacuous olurdu.
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight >= 288, TimeSpan.FromSeconds(3));
        Assert.True(list.Scroll.ScrollableHeight >= 288,
            $"liste kaydırılamıyor (ScrollableHeight={list.Scroll.ScrollableHeight}) — senaryo kurulamadı");

        // Tepede yalnız ilk başlık yapışıktır.
        Assert.Equal(new[] { "L0" }, Overlay(list).Select(h => h.Name).ToArray());

        // GERÇEK kaydırma — UpdateOverlay ÇAĞRILMAZ; yalnız ScrollViewer sürülür.
        list.Scroll.ScrollToVerticalOffset(288);
        DispatcherPump.PumpUntil(() => Overlay(list).Count == 3, TimeSpan.FromSeconds(3));

        var stuck = Overlay(list);
        Assert.Equal(new[] { "L0", "L1", "L2" }, stuck.Select(h => h.Name).ToArray());
        Assert.Equal(new[] { 0.0, 24.0, 48.0 }, stuck.Select(h => h.PinnedY).ToArray()); // i×24 yığını
        GC.KeepAlive(window);
    }

    /// <summary>Geri kaydırınca yığın da GERÇEKTEN çözülür — tek yönlü bir kablo (yalnız "aşağı") burada kırılır.</summary>
    [StaFact]
    public void Scrolling_back_to_the_top_unstacks_the_overlay_again()
    {
        var list = RealizeThenFeed(out var window);
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight >= 288, TimeSpan.FromSeconds(3));

        list.Scroll.ScrollToVerticalOffset(288);
        DispatcherPump.PumpUntil(() => Overlay(list).Count == 3, TimeSpan.FromSeconds(3));
        Assert.Equal(3, Overlay(list).Count);

        list.Scroll.ScrollToVerticalOffset(0);
        DispatcherPump.PumpUntil(() => Overlay(list).Count == 1, TimeSpan.FromSeconds(3));

        Assert.Equal(new[] { "L0" }, Overlay(list).Select(h => h.Name).ToArray());
        GC.KeepAlive(window);
    }
}
