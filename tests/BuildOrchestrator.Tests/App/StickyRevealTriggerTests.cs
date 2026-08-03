using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A12] Liste reveal'inin <b>TETİKLEYİCİSİ</b> — <c>StickyRevealTests</c>'in kapsamadığı yer.
///
/// <para><b>Neden bu dosya var (ölçülmüş kusur):</b> <c>StickyRevealTests</c>'in TÜM testleri
/// <see cref="StickyLayerList.PlayRevealStagger"/>'ı <b>DOĞRUDAN</b> çağırır ve yardımcısı
/// <c>SetGroups</c>'u realize'den ÖNCE yapar. Yani suite "reveal çağrılırsa doğru oynar" iddiasını kanıtlıyor,
/// "<b>reveal gerçekten çağrılır mı</b>" sorusunu HİÇ sormuyor. Üretimdeki sıra TERSİDİR: kabuk önce realize
/// edilir, gruplar SONRA akar (<c>MainWindow.RefreshProjectGroups</c> → <c>Shell.ProjectsList.SetGroups</c>).
/// O yolda tetikleyici <c>ItemContainerGenerator.StatusChanged</c> + <c>_revealPending</c> bayrağıdır.</para>
///
/// <para><b>Canlı uygulamada ölçülen:</b> ilk Sync'te 4 kart, 19 ms aralıkla alınan 721 karede
/// <b>0 ara-opaklık karesi</b> vererek belirdi — 300 ms'lik bir <c>bo-reveal</c> rampası ~15 ara kare üretirdi.
/// Kartlar tam opaklıkta "pat" diye geliyor: kademeli beliriş HİÇ oynamıyor.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyRevealTriggerTests
{
    private static IReadOnlyList<object> Rows(int n) =>
        [.. Enumerable.Range(0, n).Select(i => (object)new ProjectRowViewModel($"id{i}", $"P{i}", ProjectRowState.Pending))];

    /// <summary>ÜRETİM SIRASI: kabuk realize edilir (liste boş), gruplar SONRA verilir.
    /// <c>StickyRevealTests.Realize</c> bunun TERSİNİ yapar ve bu yüzden tetikleyiciyi hiç sınamaz.</summary>
    private static StickyLayerList RealizeEmptyThenFeed(
        MotionCoordinator? coordinator, out Window window)
    {
        var list = new StickyLayerList
        {
            AnimationsEnabledProvider = () => true,
            HeroCoordinator = coordinator,
        };
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, list);
        return list;
    }

    [StaFact]
    public void Feeding_groups_into_a_realized_list_actually_fires_the_reveal()
    {
        var coordinator = new MotionCoordinator();
        var list = RealizeEmptyThenFeed(coordinator, out var window);
        int before = list.RevealGeneration;

        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(4))]);
        // Tetikleyici asenkrondur (generator status + DispatcherPriority.Loaded) → koşula kadar pompala.
        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromSeconds(3));

        Assert.NotEqual(before, list.RevealGeneration); // reveal HİÇ tetiklendi mi?
        GC.KeepAlive(window);
    }

    /// <summary>Tetiklenmesi yetmez: reveal'in GERÇEKTEN satırlara uygulanmış olması gerekir.
    /// <c>HasPendingRevealRelease</c> tek assert'te ikisini birden kanıtlar — release ancak (a) hero alındıysa
    /// ve (b) en az BİR satır toplandıysa (<c>maxDelay >= 0</c>) zamanlanır.</summary>
    [StaFact]
    public void The_fired_reveal_collected_the_rows_and_took_the_hero()
    {
        var coordinator = new MotionCoordinator();
        var list = RealizeEmptyThenFeed(coordinator, out var window);
        int before = list.RevealGeneration;

        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(4))]);
        DispatcherPump.PumpUntil(() => list.HasPendingRevealRelease, TimeSpan.FromSeconds(3));

        Assert.NotEqual(before, list.RevealGeneration);
        Assert.True(list.HasPendingRevealRelease, "reveal ya hiç oynamadı ya da HİÇ satır toplayamadı");
        Assert.Equal(StickyLayerList.RevealHeroKey, coordinator.CurrentHeroKey);
        GC.KeepAlive(window);
    }

    /// <summary>Gözle görülen sonucun kendisi: kademeli beliriş sırasında satırlar bir süre ŞEFFAF olmalı.
    /// Reveal hiç oynamazsa satırlar daha ilk karede opacity 1'dedir → kullanıcı "pat diye geldi" der.</summary>
    [StaFact]
    public void Rows_start_transparent_so_the_stagger_is_actually_visible()
    {
        var list = RealizeEmptyThenFeed(new MotionCoordinator(), out var window);
        int before = list.RevealGeneration;

        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(4))]);
        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromSeconds(3));

        // Reveal AZ ÖNCE tetiklendi: son satırın gecikmesi (10ms/satır) + 300ms rampa henüz bitmemiş olmalı,
        // yani en az bir satır tam opaklığın ALTINDA olmalı. Hepsi 1.0 ise reveal hiç uygulanmadı.
        var rows = list.RevealRows;
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.Root.Opacity < 1.0);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/B3 · E5] reveal KAPSAMI

    /// <summary>[A13/B3 · E5] Bir satırın reveal OYNADIĞININ kalıcı, zamandan bağımsız kanıtı: <c>PlayReveal</c>
    /// açık motion'da <c>PART_Root.Opacity</c>'ye bir <see cref="System.Windows.Media.Animation.AnimationTimeline"/>
    /// bağlar (<c>FillBehavior.HoldEnd</c> — varsayılan) → <see cref="DependencyPropertyHelper"/> o özelliği
    /// SONSUZA DEK <c>IsAnimated</c> raporlar. Reveal'i HİÇ almamış satırda ise animasyon yoktur.
    /// <para>Opaklık DEĞERİNE bakmak yetmez: rampa bitince animasyonlu satır da 1.0'a oturur ve düşürülmüş
    /// satırdan ayırt edilemez — bu ölçüm zamanlama yarışına girmez.</para></summary>
    private static bool PlayedReveal(ProjectRow row) =>
        DependencyPropertyHelper.GetValueSource(row.Root, UIElement.OpacityProperty).IsAnimated;

    /// <summary>
    /// [A13/B3 · E5] <b>Reveal, ÜRETİM YOLUNDAN sürüldüğünde listedeki HER satıra ulaşır</b> — uçtan uca kapsam
    /// karakterizasyonu (gerçek <see cref="MainWindow"/> kablajı: yeni topoloji → <c>TopologyChanged</c> →
    /// <c>RefreshProjectGroups</c> → <c>SetGroups</c> → generator tetiği → <c>PlayRevealStagger</c>).
    ///
    /// <para><b>DÜRÜSTLÜK NOTU (fix round 1 — ölçüldü, iki bağımsız review lens'i de aynı sonuca vardı):</b> bu test
    /// <b>B3 düzeltmesi OLMADAN DA YEŞİLDİR</b>. Sebebi ölçülmüştür: tek üretim tetiği
    /// <c>DispatcherPriority.Loaded</c>(6) ertelemesidir ve <c>Render</c>(7) ondan ÖNCE koştuğu için satırlar
    /// <c>CollectRows</c> çağrılmadan realize olur — yani düşme penceresi üretimde bugün AÇILMAZ. Bu testin
    /// önceki sürümü doc'unda "<c>CollectRows</c> 0 satır döndürüyordu" diye <b>yanlış bir ölçüm iddiası</b>
    /// taşıyordu; iddia kaldırıldı (bkz. task-B3-report.md "Fix round 1 / E5").</para>
    ///
    /// <para><b>Öyleyse ne işe yarıyor:</b> uçtan uca kapsamın bugünkü gerçeğini pinler — tetik zinciri kopar,
    /// bir satır şablonu değişir ya da <c>SetGroups</c> yolu bozulursa bu test görür. Düzeltmenin KENDİSİNİ
    /// (<c>PlayRevealStagger</c>'ın layout'a dayanmayan kapsamı) pinleyen test AYRIDIR ve gerçek bir kırmızıya
    /// dayanır: <see cref="StickyRevealTests.A_reveal_driven_while_layout_is_dirty_still_reaches_every_row"/>.</para>
    ///
    /// <para><b>Vakum değil:</b> satır sayısı ayrıca assert edilir ve reveal'in gerçekten tetiklendiği
    /// (<c>RevealGeneration</c> arttı) ayrıca doğrulanır.</para>
    /// </summary>
    [StaFact]
    public void Every_row_of_a_freshly_synced_list_plays_the_reveal()
    {
        using var dir = new TempDir();
        // ÜRETİM FIXTURE'I: kabuk realize edilir, topoloji SONRA akar (MainWindow.RefreshProjectGroups → SetGroups).
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        // D8 seam'leri: headless'ta App.Motion null (reduced-motion) ve App.HeroMotion yok — reveal'in GÖRSEL
        // sonucunu ölçebilmek için ikisi de enjekte edilir (StickyRevealTests/GraphRenderTests ile AYNI desen).
        list.AnimationsEnabledProvider = () => true;
        list.HeroCoordinator = new MotionCoordinator();

        int before = list.RevealGeneration;
        // ÜRETİM YOLU: yeni bir Sync topolojiyi değiştirir → RunViewModel.TopologyChanged → RefreshProjectGroups
        // → SetGroups(reveal: true). Doğrudan PlayRevealStagger çağrısı YOK.
        var nodes = new[] { "Alpha", "Beta", "Gamma", "Delta" }
            .Select((n, i) => MainWindowHost.Node(n, i)).ToList();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));

        DispatcherPump.PumpUntil(() => list.RevealGeneration != before, TimeSpan.FromSeconds(3));
        Assert.NotEqual(before, list.RevealGeneration); // ön-koşul: reveal GERÇEKTEN tetiklendi
        // Rampanın başlaması için (10ms/satır gecikme) pompala; PumpUntil timeout'ta HATA VERMEZ → altta assert.
        DispatcherPump.PumpUntil(() => list.RevealRows.Count == nodes.Count && list.RevealRows.All(PlayedReveal),
            TimeSpan.FromSeconds(3));

        var rows = list.RevealRows;
        Assert.Equal(nodes.Count, rows.Count); // ön-koşul: liste GERÇEKTEN realize (vakum yasak)
        Assert.All(rows, r => Assert.True(PlayedReveal(r),
            $"'{((ProjectRowViewModel)r.DataContext).Name}' satırı reveal OYNAMADI — reveal kapsamı eksik kaldı."));
        GC.KeepAlive(window);
    }
}
