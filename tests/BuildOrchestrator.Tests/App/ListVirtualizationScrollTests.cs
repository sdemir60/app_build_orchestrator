using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Sanallaştırılmış proje listesinin KAYDIRMA davranışı.
///
/// <para><b>Ölçülen kusur:</b> listeyi aşağı-yukarı birkaç kez kaydırınca satırlar kayboluyor, sonunda liste
/// tamamen boşalıyordu; ayrıca son satırın altında kaydırılabilen boşluk kalıyordu. Kök neden container
/// GERİ DÖNÜŞÜMÜ: <c>IItemContainerGenerator.GenerateNext</c>, havuzdan gelen bir container'ı
/// <c>isNewlyRealized = false</c> ile döndürür — oysa o container geri dönüştürülürken
/// <c>InternalChildren</c>'dan ÇIKARILMIŞTIR. Panel yalnız "yeni realize" olanları ekliyordu, dolayısıyla
/// geri dönüştürülmüş her satır bir daha ağaca girmiyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ListVirtualizationScrollTests(ITestOutputHelper output)
{
    private const int RowCount = 60;

    /// <summary>Üretimdeki gibi KATMAN BAŞLIKLI liste (24px başlık + 36px satır karışık) — panelin kümülatif
    /// tablosu bu karışımda sınanmalı.</summary>
    private static (Border host, StickyLayerList list, Window window) NewList()
    {
        var host = DsResources.NewHost();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var groups = new List<StickyLayerList.LayerGroup>();
        for (int layer = 0; layer < 6; layer++)
        {
            var rows = new List<object>();
            for (int i = 0; i < RowCount / 6; i++)
            {
                int index = layer * (RowCount / 6) + i;
                rows.Add(new ProjectRowViewModel($@"C:\p\proj{index}.csproj", $"Proj{index}", ProjectRowState.Pending));
            }
            groups.Add(new StickyLayerList.LayerGroup($"Layer{layer}", rows));
        }
        list.SetGroups(groups);
        var window = DsResources.Realize(host, list); // 400×200
        return (host, list, window);
    }

    /// <summary>Bir öğe indeksinin container'ı GERÇEKTEN ağaçta mı. Geri dönüştürülmüş bir container
    /// generator'da hâlâ eşleşebilir ama görsel ebeveyni YOKTUR — kullanıcının gördüğü boşluk tam olarak budur.</summary>
    private static bool IsInTree(StickyLayerList list, int itemIndex) =>
        list.RowFlow.ItemContainerGenerator.ContainerFromIndex(itemIndex) is DependencyObject container
        && System.Windows.Media.VisualTreeHelper.GetParent(container) is not null;

    private static void ScrollTo(StickyLayerList list, double offset)
    {
        list.Scroll.ScrollToVerticalOffset(offset);
        list.UpdateLayout();
    }

    /// <summary>Aşağı-yukarı tekrarlı kaydırma satır KAYBETTİRMEZ: her durakta görünür pencere dolu olmalı.</summary>
    [StaFact]
    public void Scrolling_up_and_down_repeatedly_never_empties_the_list()
    {
        var (_, list, window) = NewList();
        double max = list.Scroll.ExtentHeight - list.Scroll.ViewportHeight;

        var stops = new List<double>();
        for (int round = 0; round < 4; round++) { stops.Add(max); stops.Add(0); stops.Add(max / 2); }

        foreach (double offset in stops)
        {
            ScrollTo(list, offset);
            int realized = list.RevealRows.Count;
            output.WriteLine($"[scroll] offset {offset,7:N1} → realize {realized} satır");
            Assert.True(realized > 0,
                $"offset {offset:N1}'de liste BOŞALDI — geri dönüştürülmüş container'lar ağaca geri konmuyor.");
        }
        GC.KeepAlive(window);
    }

    /// <summary>Görünür pencere HER durakta gerçekten kaplanmalı — yalnız "sıfırdan fazla satır" yetmez,
    /// viewport'un tamamı satırla dolu olmalıdır (aksi halde kullanıcı boşluk görür).</summary>
    [StaFact]
    public void Every_scroll_position_keeps_the_visible_window_covered_by_rows()
    {
        var (_, list, window) = NewList();
        double max = list.Scroll.ExtentHeight - list.Scroll.ViewportHeight;
        int expected = (int)Math.Floor(list.Scroll.ViewportHeight / LayoutMetrics.DefaultRowHeight);

        foreach (double offset in new[] { 0, max / 3, max, max / 2, 0, max, max / 4, max })
        {
            ScrollTo(list, offset);
            var missing = MissingFromTree(list);
            output.WriteLine($"[kapsama] offset {offset,7:N1} → ağaçta OLMAYAN görünür öğe: {missing.Count}");
            Assert.True(missing.Count == 0,
                $"offset {offset:N1}'de görünür pencerenin şu öğeleri ağaçta değil (boşluk görünür): " +
                string.Join(", ", missing.Take(10)));
        }
        _ = expected;
        GC.KeepAlive(window);
    }

    /// <summary>Görünür pencereye düşen ama görsel ağaçta OLMAYAN öğe indeksleri.</summary>
    private static List<int> MissingFromTree(StickyLayerList list)
    {
        double top = list.Scroll.VerticalOffset;
        double bottom = top + list.Scroll.ViewportHeight;
        var missing = new List<int>();
        double y = 0;
        for (int i = 0; i < list.RowFlow.Items.Count; i++)
        {
            double height = list.RowFlow.Items[i] is StickyLayerList.HeaderEntry
                ? LayoutMetrics.DefaultHeaderHeight : LayoutMetrics.DefaultRowHeight;
            if (y + height > top && y < bottom && !IsInTree(list, i)) missing.Add(i);
            y += height;
        }
        return missing;
    }

    /// <summary>Kaydırma menzili içeriği AŞMAMALI: son satırın altında kaydırılabilen boşluk olmamalı.</summary>
    [StaFact]
    public void The_scrollable_extent_matches_the_content_and_the_last_row_ends_at_the_bottom()
    {
        var (_, list, window) = NewList();
        double max = list.Scroll.ExtentHeight - list.Scroll.ViewportHeight;
        ScrollTo(list, max);

        Assert.Equal(list.Metrics!.TotalHeight, list.Scroll.ExtentHeight, precision: 3);

        // En alttayken SON satır ekranın dibinde bitmeli (içerik koordinatında).
        double lastRowBottom = list.Metrics.OffsetOfRow(RowCount - 1) + LayoutMetrics.DefaultRowHeight;
        double viewportBottom = list.Scroll.VerticalOffset + list.Scroll.ViewportHeight;
        output.WriteLine($"[dip] son satır sonu {lastRowBottom:N1} · viewport dibi {viewportBottom:N1}");
        Assert.Equal(lastRowBottom, viewportBottom, precision: 3);
        GC.KeepAlive(window);
    }

    /// <summary>Görünür pencereye (kısmen de olsa) düşen, ağaca GERÇEKTEN yerleşmiş satır sayısı.</summary>
    private static int VisibleRowCount(StickyLayerList list)
    {
        double top = list.Scroll.VerticalOffset;
        double bottom = top + list.Scroll.ViewportHeight;
        int count = 0;
        foreach (var row in list.RevealRows)
        {
            if (row.DataContext is not ProjectRowViewModel vm) continue;
            int index = int.Parse(vm.Name["Proj".Length..], System.Globalization.CultureInfo.InvariantCulture);
            double rowTop = list.Metrics!.OffsetOfRow(index);
            if (rowTop + LayoutMetrics.DefaultRowHeight > top && rowTop < bottom) count++;
        }
        return count;
    }
}
