using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E4/T48 · E3 fold] StickyLayerList liste-frontier reveal hero'sunun CANLI wiring'i — GraphView reveal hero
/// deseninin (GraphRenderTests) liste karşılığı. DD9: graf reveal + liste reveal AYNI hero (<c>"sync-reveal"</c>,
/// re-entrant → birlikte oynar). Hero reveal PENCERESİ boyunca tutulur ve generation-guarded bir
/// <see cref="System.Windows.Threading.DispatcherTimer"/> ile bırakılır (<c>Completed</c>-after-<c>BeginAnimation</c>
/// ÖLÜ yolu DEĞİL — E3 fix). Reduced-motion / başka hero sürerken satırlar ANİ yerleşir, hero tutulmaz.
///
/// <para>Container üretimi WPF'e bağlı olduğundan liste ekran dışı realize edilir (DsResources.Realize) ve
/// ProjectRow çocukları realize olana dek pompalanır; sonra <see cref="StickyLayerList.PlayRevealStagger"/> DOĞRUDAN
/// çağrılır (GraphView.SetGraph'ın senkron reveal tetiklemesiyle AYNI determinizm).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyRevealTests
{
    private static IReadOnlyList<object> Rows(int n) =>
        [.. Enumerable.Range(0, n).Select(i => (object)new ProjectRowViewModel($"id{i}", $"P{i}", ProjectRowState.Pending))];

    private static StickyLayerList Realize(bool animationsEnabled, MotionCoordinator? coordinator, out Window window, int rowCount = 3)
    {
        var list = new StickyLayerList
        {
            AnimationsEnabledProvider = () => animationsEnabled,
            HeroCoordinator = coordinator,
        };
        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(rowCount))]);
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, list);
        // Container üretimi + ProjectRow çocuklarının realize olmasını bekle (deterministik reveal için).
        DispatcherPump.PumpUntil(() => list.RevealRows.Count == rowCount, TimeSpan.FromSeconds(2));
        return list;
    }

    [StaFact]
    public void The_list_reveal_uses_the_exact_same_hero_key_as_the_graph_reveal()
    {
        // DD9 "graf + liste frontier AYNI hero" — derleme-zamanı sabiti; ikisi ASLA sürüklenemez.
        Assert.Equal(GraphView.RevealHeroKey, StickyLayerList.RevealHeroKey);
    }

    [StaFact]
    public void The_list_reveal_stagger_takes_the_sync_reveal_hero_while_it_plays()
    {
        var coordinator = new MotionCoordinator();
        var list = Realize(true, coordinator, out var window);

        list.PlayRevealStagger();

        Assert.Equal(StickyLayerList.RevealHeroKey, coordinator.CurrentHeroKey);
        Assert.True(list.HasPendingRevealRelease);
        // Senkron: PlayRevealStagger ile assert arasında pump YOK → clock tick etmedi, satırlar hâlâ 0 (gecikme başı).
        Assert.All(list.RevealRows, r => Assert.Equal(0.0, r.Root.Opacity));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_list_reveal_releases_the_sync_reveal_hero_when_it_completes()
    {
        var coordinator = new MotionCoordinator();
        var list = Realize(true, coordinator, out var window);

        list.PlayRevealStagger();
        Assert.True(coordinator.IsHeroActive);
        // CANLI bir release ZAMANLANDI — ölü Completed-after-BeginAnimation yolu DEĞİL (E3 fix'in özü).
        Assert.True(list.HasPendingRevealRelease);

        // Reveal tamamlandığında release tetiklenir (gerçek tick beklemeden, mevcut kuşak damgasıyla) → hero BIRAKILIR.
        list.ReleaseRevealHeroIfCurrent(list.RevealGeneration);

        Assert.False(coordinator.IsHeroActive);
        Assert.False(list.HasPendingRevealRelease);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_stale_list_reveal_completion_does_not_release_the_current_reveal_hero()
    {
        var coordinator = new MotionCoordinator();
        var list = Realize(true, coordinator, out var window);

        list.PlayRevealStagger();                 // reveal kuşağı #1
        int gen1 = list.RevealGeneration;
        list.PlayRevealStagger();                 // hızlı ikinci reveal → #1'i bırakır, #2'yi yeniden alır
        Assert.NotEqual(gen1, list.RevealGeneration);
        Assert.Equal(StickyLayerList.RevealHeroKey, coordinator.CurrentHeroKey);

        // #1'in gecikmiş (stale) release'i ateşlense bile #2'nin TAZE hero'suna dokunmaz (gen1 != mevcut kuşak).
        list.ReleaseRevealHeroIfCurrent(gen1);

        Assert.True(coordinator.IsHeroActive);
        Assert.Equal(StickyLayerList.RevealHeroKey, coordinator.CurrentHeroKey);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_list_reveal_yields_to_an_already_running_hero_and_snaps_the_rows()
    {
        var coordinator = new MotionCoordinator();
        Assert.True(coordinator.TryBeginHero("frontier")); // DD9: başka bir hero zaten oynuyor
        var list = Realize(true, coordinator, out var window);

        list.PlayRevealStagger();

        // Reveal reddedildi → satırlar ANİ yerleşir (Opacity 1); aktif hero DEĞİŞMEDİ; release beklemez.
        Assert.All(list.RevealRows, r => Assert.Equal(1.0, r.Root.Opacity));
        Assert.False(list.HasPendingRevealRelease);
        Assert.Equal("frontier", coordinator.CurrentHeroKey);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_graph_and_list_reveal_share_one_sync_reveal_hero_re_entrantly()
    {
        // DD9: graf reveal hero'yu alır; liste reveal AYNI key'le re-entrant girer (depth 2) → birlikte oynar.
        // Biri bitince hero SÜRER (öteki hâlâ açık), ikisi de bitince serbest kalır.
        var coordinator = new MotionCoordinator();
        Assert.True(coordinator.TryBeginHero(StickyLayerList.RevealHeroKey)); // graf reveal (simüle)
        var list = Realize(true, coordinator, out var window);

        list.PlayRevealStagger(); // liste reveal — AYNI key, re-entrant kabul edilir
        Assert.Equal(StickyLayerList.RevealHeroKey, coordinator.CurrentHeroKey);

        list.ReleaseRevealHeroIfCurrent(list.RevealGeneration); // liste reveal bitti
        Assert.True(coordinator.IsHeroActive);                  // graf hâlâ açık (depth 1) → hero sürüyor

        coordinator.EndHero(StickyLayerList.RevealHeroKey);     // graf reveal de bitti
        Assert.False(coordinator.IsHeroActive);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/B3 · E5 — fix round 1] <b>Layout KİRLİYKEN sürülen bir reveal de HER satıra ulaşmalıdır.</b>
    ///
    /// <para><b>Neden bu test var (ölçüm):</b> <c>SetGroups</c> <c>ItemsSource</c>'u atadığı anda
    /// <c>ItemContainerGenerator</c> container'ları SENKRON üretir, ama <c>ContentPresenter</c>'lar henüz measure
    /// EDİLMEMİŞTİR — yani o an <c>CollectRows</c> <b>SIFIR</b> satır görür (aşağıdaki ön-koşul bunu her koşumda
    /// yeniden ölçer). Üretimde bu pencere bugün açılmaz, çünkü tetik
    /// <c>DispatcherPriority.Loaded</c>(6) &lt; <c>Render</c>(7) ertelemesindedir ve otomatik layout turu önce
    /// koşar. Ama <b>o öncelik varsayımı hiçbir yerde pinli değildi</b>: tetik <c>Background</c>'a kaysa ya da
    /// senkron çağrılsa satırlar SESSİZCE düşerdi ve süit hiçbir şey söylemezdi.</para>
    ///
    /// <para><b>Ne pinliyor:</b> <see cref="StickyLayerList.PlayRevealStagger"/>'ın kapsamı o varsayıma BAĞLI
    /// DEĞİL. Test, tetiğin ertelemesini bilerek atlayıp reveal'i tam o kirli pencerede sürer — bu, bu dosyanın
    /// zaten kullandığı desendir (sınıf özeti: reveal DOĞRUDAN çağrılır, deterministik).</para>
    ///
    /// <para><b>Ayırt edici (ÖLÇÜLDÜ):</b> <c>StickyLayerList.xaml.cs</c> <c>e2e50aa</c> (B3 ÖNCESİ) sürümüne
    /// döndürüldüğünde bu test KIRMIZI olur — 4 satırın 4'ü de reveal oynamaz. Ayrıntı: task-B3-report.md.</para>
    /// </summary>
    [StaFact]
    public void A_reveal_driven_while_layout_is_dirty_still_reaches_every_row()
    {
        const int rowCount = 4;
        var coordinator = new MotionCoordinator();
        // Üretim sırası: kabuk ÖNCE realize (liste boş), veri SONRA — StickyRevealTriggerTests ile aynı kurulum.
        var list = new StickyLayerList { AnimationsEnabledProvider = () => true, HeroCoordinator = coordinator };
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);

        // Üretim API'si. Bundan SONRA pompalama YOK → layout kirli kalır (tam olarak ölçülen pencere).
        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(rowCount))]);
        Assert.Empty(list.RevealRows); // ÖN-KOŞUL: pencere gerçekten açık (henüz hiçbir satır realize değil)

        list.PlayRevealStagger();

        list.UpdateLayout(); // iki taraf AYNI satır kümesini görsün (düzeltme yokken satırlar burada realize olur)
        var rows = list.RevealRows;
        Assert.Equal(rowCount, rows.Count); // vakum yasak: satırlar GERÇEKTEN var
        // Gözlem, bu dosyanın mevcut deseni (bkz. The_list_reveal_stagger_takes_the_sync_reveal_hero_while_it_plays):
        // PlayRevealStagger ile assert arasında pump YOK → clock hiç tick etmedi, reveal ALMIŞ satır gecikmenin
        // başında 0.0'da DURUR. Reveal ALMAMIŞ satır XAML varsayılanı olan 1.0'da kalır — ikisi ayrışır.
        Assert.All(rows, r => Assert.True(r.Root.Opacity == 0.0,
            $"satır reveal OYNAMADI (Opacity={r.Root.Opacity}) — kirli layout'ta CollectRows onu sessizce düşürdü."));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_list_reveal_hero_is_released_when_the_control_unloads()
    {
        var coordinator = new MotionCoordinator();
        var list = Realize(true, coordinator, out var window);
        list.PlayRevealStagger();
        Assert.True(coordinator.IsHeroActive);

        list.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); // unload reveal ortasında → hero bırakılır (leak yok)

        Assert.False(coordinator.IsHeroActive);
        Assert.False(list.HasPendingRevealRelease);
        GC.KeepAlive(window);
    }
}
