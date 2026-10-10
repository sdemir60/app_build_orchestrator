using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf G2 · ÖLÇÜLDÜ] Görünür bir koşuda UI thread'inin en uzun kesintisiz dilimleri (98–226 ms) liste panelinin
/// yerleşim işiydi — satır İNŞASI değil (dilim başına 8–14 ms), geri dönüştürülen container'ların YENİ veriye
/// bağlanması: yeniden bağlanan her satır metin biçimlemeyi (ölçüm + çizim), UIA peer tazelemesini ve binding
/// yazımını öder (~2,5 ms); takip kaydırması pencereyi tek karede 30–60 satır kaydırdığında bu tek dilimde
/// toplanıyor ve graf animasyonları o dilim boyunca duruyordu. Topoloji/filtre tazelemesi de <c>ItemsSource</c>
/// reset'iyle pencerenin tamamını yeniden kuruyordu.
///
/// <para><b>Kural:</b> bir satır bir kez kurulunca kontrolü ömrü boyunca o satırda kalır — pencere kayınca
/// bırakılmaz, geri dönüştürülmez. İlk yerleşim yalnız pencereyi kurar (bütçe ölçekten bağımsız kalır,
/// bkz. <see cref="ListRealizationPerfTests"/>); geri kalan satırlar dispatcher boşken küçük dilimlerle kurulur.
/// Topoloji/filtre tazelemesi listeyi YERİNDE uzlaştırır (çıkan silinir, giren eklenir, yer değiştiren taşınır):
/// dokunulmayan satırın kontrolü ve ölçümü aynen kalır, reset yoktur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ListRowsStayRealizedTests
{
    private const int Layers = 6;
    private const int RowsPerLayer = 10;
    private const int RowCount = Layers * RowsPerLayer;

    private static List<ProjectRowViewModel> NewRows() =>
        [.. Enumerable.Range(0, RowCount).Select(i =>
            new ProjectRowViewModel($@"C:\p\proj{i}.csproj", $"Proj{i}", ProjectRowState.Pending))];

    /// <summary>Üretimdeki gibi KATMAN BAŞLIKLI liste (24px başlık + 36px satır karışık).</summary>
    private static List<StickyLayerList.LayerGroup> GroupsOf(IReadOnlyList<ProjectRowViewModel> rows, bool reversedLayers = false)
    {
        var groups = new List<StickyLayerList.LayerGroup>();
        for (int layer = 0; layer < Layers; layer++)
            groups.Add(new StickyLayerList.LayerGroup($"Layer{layer}",
                rows.Skip(layer * RowsPerLayer).Take(RowsPerLayer).Cast<object>().ToList()));
        if (reversedLayers) groups.Reverse();
        return groups;
    }

    private static List<ProjectRowViewModel> RowsInOrder(IEnumerable<StickyLayerList.LayerGroup> groups) =>
        [.. groups.SelectMany(g => g.Rows).Cast<ProjectRowViewModel>()];

    private static (StickyLayerList list, Window window, List<ProjectRowViewModel> rows) NewList()
    {
        var rows = NewRows();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        list.SetGroups(GroupsOf(rows));
        var window = DsResources.Realize(DsResources.NewHost(), list); // 400×200: pencere ≈ 11 girdi
        return (list, window, rows);
    }

    /// <summary>Realize olmuş satır kontrolleri, modeline göre — kimlik (AYNI nesne mi) buradan karşılaştırılır.</summary>
    private static Dictionary<ProjectRowViewModel, ProjectRow> RowsByModel(StickyLayerList list) =>
        list.RevealRows.ToDictionary(r => (ProjectRowViewModel)r.DataContext, r => r);

    private static void ScrollTo(StickyLayerList list, double offset)
    {
        list.Scroll.ScrollToVerticalOffset(offset);
        list.UpdateLayout();
    }

    private static void PumpUntilEveryRowIsRealized(StickyLayerList list)
    {
        DispatcherPump.PumpUntil(() => list.RevealRows.Count == RowCount, TimeSpan.FromSeconds(5));
        Assert.Equal(RowCount, list.RevealRows.Count); // ön-koşul: boşta dolum HER satıra ulaştı (vakum yasak)
    }

    private static void AssertSameControls(
        IReadOnlyDictionary<ProjectRowViewModel, ProjectRow> before, IReadOnlyDictionary<ProjectRowViewModel, ProjectRow> after)
    {
        foreach (var (vm, row) in before)
        {
            Assert.True(after.TryGetValue(vm, out var now), $"{vm.Name} satırının kontrolü kayboldu.");
            Assert.True(ReferenceEquals(row, now), $"{vm.Name} satırı YENİ bir kontrole bağlandı — kontrol satırda kalmalıydı.");
        }
    }

    /// <summary>Her satır kontrolü ağaçtadır ve akış koordinatında kendi <see cref="LayoutMetrics"/> üst kenarında
    /// durur — taşıma/silme sonrası panelin çocuk sırası ile generator sırası ayrışmış olsaydı burada görünürdü.</summary>
    private static void AssertRowsSitAtTheirOffsets(StickyLayerList list, IList<ProjectRowViewModel> orderedRows)
    {
        foreach (var (vm, row) in RowsByModel(list))
        {
            Assert.NotNull(VisualTreeHelper.GetParent(row));
            double expected = list.Metrics!.OffsetOfRow(orderedRows.IndexOf(vm));
            double actual = row.TranslatePoint(new Point(0, 0), list.RowFlow).Y;
            Assert.True(Math.Abs(expected - actual) < 0.001, $"{vm.Name} satırı {actual:N1}'de, {expected:N1}'de olmalıydı.");
        }
    }

    [StaFact]
    public void Rows_keep_their_controls_while_the_window_scrolls_away_and_back()
    {
        var (list, window, _) = NewList();
        var before = RowsByModel(list);
        double max = list.Scroll.ExtentHeight - list.Scroll.ViewportHeight;

        ScrollTo(list, max);
        ScrollTo(list, 0);

        AssertSameControls(before, RowsByModel(list));
        GC.KeepAlive(window);
    }

    /// <summary>Boşta dolum küçük dilimlerle ilerler: bir dilim en çok <see cref="FixedHeightVirtualizingPanel.IdleSliceRows"/>
    /// girdi kurar ve sonraki dilimi kuyruklar — animasyon kareleri arasına sığsın diye (gerekçe panel doc'unda).</summary>
    [StaFact]
    public void An_idle_slice_realizes_at_most_the_slice_size_and_the_next_slice_continues()
    {
        var (list, window, _) = NewList();
        PumpUntilEveryRowIsRealized(list);
        // Yepyeni satırlar: hepsi kurulmamış girer; senkron tur yalnız pencereyi kurar, gerisi dilim dilim gelir.
        list.SetGroups(GroupsOf(NewRows()), reveal: false);
        list.UpdateLayout();
        int afterWindow = list.RevealRows.Count;
        Assert.InRange(afterWindow, 1, RowCount - 2 * FixedHeightVirtualizingPanel.IdleSliceRows);

        DispatcherPump.DrainToIdle(); // tam bir dilim (+ onun istediği yerleşim turu)
        int afterOneSlice = list.RevealRows.Count;
        Assert.InRange(afterOneSlice - afterWindow, 1, FixedHeightVirtualizingPanel.IdleSliceRows);

        DispatcherPump.DrainToIdle(); // dilim kendini yeniden kuyrukladı
        Assert.InRange(list.RevealRows.Count - afterOneSlice, 1, FixedHeightVirtualizingPanel.IdleSliceRows);
        GC.KeepAlive(window);
    }

    /// <summary>Kurulu ama uzaktaki satırlar ÇİZİLMEZ (Hidden): yerinde ve ölçülü dururlar, bant kaydırmayla üstlerine
    /// gelince görünür olurlar. Gerekçe ve ölçüm <see cref="FixedHeightVirtualizingPanel.RenderBandScreens"/>'de.</summary>
    [StaFact]
    public void Rows_far_from_the_viewport_stay_unrendered_until_the_window_approaches()
    {
        var (list, window, rows) = NewList();
        PumpUntilEveryRowIsRealized(list);
        var byModel = RowsByModel(list);
        var first = byModel[rows[0]];
        var last = byModel[rows[RowCount - 1]];

        Assert.True(first.IsVisible, "pencerenin içindeki satır görünür olmalı");
        Assert.False(last.IsVisible, "bir ekrandan uzaktaki satır çizilmemeli (Hidden)");
        Assert.Equal(RowCount, list.RevealRows.Count);             // yine de kuruludur
        AssertRowsSitAtTheirOffsets(list, rows);                   // ve yerindedir

        ScrollTo(list, list.Scroll.ExtentHeight - list.Scroll.ViewportHeight);

        Assert.True(last.IsVisible, "bant son satırın üstüne geldi: görünür");
        Assert.False(first.IsVisible, "ilk satır artık bir ekrandan uzakta: Hidden");
        Assert.Same(first, RowsByModel(list)[rows[0]]);           // kontrol değişmedi, yalnız çizimi durdu
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Idle_realizes_every_row_and_scrolling_afterwards_builds_nothing_new()
    {
        var (list, window, _) = NewList();
        PumpUntilEveryRowIsRealized(list);
        var filled = RowsByModel(list);
        double max = list.Scroll.ExtentHeight - list.Scroll.ViewportHeight;

        foreach (double offset in new[] { max, max / 2, 0, max / 3 }) ScrollTo(list, offset);

        var after = RowsByModel(list);
        Assert.Equal(RowCount, after.Count);
        AssertSameControls(filled, after);
        GC.KeepAlive(window);
    }

    /// <summary>Katman sırası değişince yalnız YERİ DEĞİŞEN satırlar yeniden kurulur (WPF generator'ı taşınan bir
    /// container'ı güvenle yeni indeksine bağlayamıyor — gerekçe <see cref="FixedHeightVirtualizingPanel"/> doc'unda);
    /// dokunulmayan katmanların satırları kontrollerini korur ve reset HİÇ yoktur.</summary>
    [StaFact]
    public void A_reordered_topology_rebuilds_only_the_rows_that_moved()
    {
        var (list, window, rows) = NewList();
        PumpUntilEveryRowIsRealized(list);
        var before = RowsByModel(list);
        int resets = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, e) =>
        { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };

        // Katman 2 katman 1'in önüne geçer; diğer dört katman yerinde. Uzlaştırma katman 2'nin satırlarını taşır.
        var groups = GroupsOf(rows);
        (groups[1], groups[2]) = (groups[2], groups[1]);
        var untouched = groups.Where((_, i) => i != 1).SelectMany(g => g.Rows).Cast<ProjectRowViewModel>().ToHashSet();
        list.SetGroups(groups);
        list.UpdateLayout();
        PumpUntilEveryRowIsRealized(list); // taşınan satırlar yeniden kurulur (pencere dışındakiler boşta)

        Assert.Equal(0, resets);
        AssertSameControls(before.Where(kv => untouched.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), RowsByModel(list));
        // Akış yeni sırayı birebir yansıtır: başlıklar ve satırlar, Metrics ile AYNI kaynaktan.
        var expected = groups.SelectMany(g => new[] { g.Name }.Concat(g.Rows.Select(r => ((ProjectRowViewModel)r).Name))).ToList();
        var actual = list.RowFlow.Items.Cast<object>()
            .Select(e => e is StickyLayerList.HeaderEntry h ? h.Name : ((ProjectRowViewModel)e).Name).ToList();
        Assert.Equal(expected, actual);
        AssertRowsSitAtTheirOffsets(list, RowsInOrder(groups));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Removing_rows_keeps_every_other_row_control_in_place()
    {
        var (list, window, rows) = NewList();
        PumpUntilEveryRowIsRealized(list);
        var before = RowsByModel(list);
        var gone = new HashSet<ProjectRowViewModel> { rows[3], rows[17], rows[RowCount - 1] };
        var narrowed = GroupsOf(rows)
            .Select(g => new StickyLayerList.LayerGroup(g.Name, g.Rows.Where(r => !gone.Contains((ProjectRowViewModel)r)).ToList()))
            .ToList();

        list.SetGroups(narrowed, reveal: false); // filtre tazelemesi yolu
        list.UpdateLayout();

        var after = RowsByModel(list);
        foreach (var vm in gone) Assert.False(after.ContainsKey(vm), $"{vm.Name} listeden çıkmalıydı.");
        AssertSameControls(before.Where(kv => !gone.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), after);
        AssertRowsSitAtTheirOffsets(list, RowsInOrder(narrowed));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Feeding_the_same_groups_again_touches_nothing_but_still_plays_the_reveal()
    {
        var (list, window, rows) = NewList();
        list.AnimationsEnabledProvider = () => true; // reveal kuşağı ancak motion açıkken ilerler
        list.HeroCoordinator = new MotionCoordinator();
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        int before = list.RevealGeneration;
        Assert.True(before > 0, "ilk reveal hiç oynamadı — taban çizgisi yok (vakum)");
        int touches = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => touches++;

        list.SetGroups(GroupsOf(rows)); // AYNI yapı, reveal istenir (Sync düğmesi: ekran baştan)
        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromSeconds(3));

        Assert.Equal(0, touches);                       // koleksiyona DOKUNULMADI
        Assert.NotEqual(before, list.RevealGeneration); // ...ama reveal yine oynadı
        GC.KeepAlive(window);
    }
}
