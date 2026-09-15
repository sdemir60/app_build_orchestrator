using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.17.0 §9 "3" — Task 7] Event stream'in her satırında hover artık var — eski kural (tıklanamaz
/// satırlarda "parıltıyı ezmesin" gerekçesiyle hover zemini hiç açılmıyordu) DEĞİŞTİ: tıklanamaz satır artık bir
/// adım daha sessiz (<c>Brush.Surface</c>) zemin alır, tıklanabilir satır (mevcut) <c>Brush.SurfaceHover</c>'a
/// açılır; imleç eşlemesi (tıklanamaz → Arrow, tıklanabilir → Hand) DEĞİŞMEDİ.
///
/// <para><b>Glow-once ile etkileşim — [review round 1 fix] DÜZELTİLMİŞ karar: hover BEKLER.</b> Prototipin CSS
/// <c>@keyframes bo-glow-once</c>'u (BuildApp.jsx:19) satırın <c>style.background</c>'ını (hover dahil) ezer ve
/// kendi 1.1s'lik yayını sonuna kadar oynatır; hover zemini ancak yay bittiğinde görünür olur (BuildApp.jsx:1109
/// + 1115). İLK turda burada "hover uçuştaki animasyonu ATLAMASIZ devralır" denmişti — bu YANLIŞTI: devralma
/// gerçek bir animasyon sürerken bile parıltının 1.1s'lik doğal süresini KISALTIYORDU, prototipte öyle bir
/// kısalma YOK. Doğru kural: parıltı sürerken (<c>EventStreamRow._glowRunning</c>) <c>ApplyBackground</c> hiçbir
/// şey yazmaz; parıltı doğal olarak bitince (<c>Completed</c>) BİR KEZ yeniden çağrılır ve O ANKİ hover/seçim
/// durumuna göre doğru zemine oturur — bkz.
/// <see cref="Hover_that_begins_mid_glow_waits_for_the_glow_to_finish_before_taking_the_ground"/> ve
/// <see cref="Hover_that_begins_before_the_glow_starts_still_waits_and_settles_on_the_hover_ground_once_it_ends"/>.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamHoverTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (EventStreamView view, Window window, System.Windows.Controls.Border host) Realize(
        RunViewModel vm, bool forceAnimations = false)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => forceAnimations, DataContext = vm };
        var window = DsResources.Realize(host, view);
        return (view, window, host);
    }

    private static void Enter(UIElement row) =>
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

    private static void Leave(UIElement row) =>
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

    // ================================================================ tıklanamaz satır

    [StaFact]
    public void Hovering_a_non_clickable_row_opens_a_quiet_surface_band_and_keeps_the_arrow_cursor()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 1200, "error CS0103"));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 1, 0, 0, 100)); // hatalı → Done UYGUN DEĞİL (parıltı yok)

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Last();

        // Non-vacuous: satır gerçekten Done ve gerçekten tıklanamaz, glow'la karışmıyor.
        Assert.Equal(StreamKind.Done, row.ViewModel!.Kind);
        Assert.False(row.ViewModel!.IsClickable);
        Assert.False(row.ViewModel!.GlowEligible);
        Assert.Equal(Cursors.Arrow, row.Cursor);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background)); // dinlenme: şeffaf

        Enter(row);
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.Surface"), PumpTimeout);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Surface"), DsResources.ColorOf(row.Background));
        Assert.Equal(Cursors.Arrow, row.Cursor); // imleç hover'dan etkilenmez

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ tıklanabilir satır

    [StaFact]
    public void Hovering_a_clickable_row_opens_surface_hover_and_switches_to_the_hand_cursor()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200)); // → "A built (1.2s)" ok satırı, tıklanabilir

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);

        Assert.True(row.ViewModel!.IsClickable);
        Assert.Equal(Cursors.Hand, row.Cursor);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));

        Enter(row);
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.SurfaceHover"), PumpTimeout);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceHover"), DsResources.ColorOf(row.Background));
        Assert.Equal(Cursors.Hand, row.Cursor);

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ seçili satır değişmez

    [StaFact]
    public void Hovering_a_selected_row_does_not_disturb_its_raised_surface()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200));

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);
        vm.SelectProject(id);
        view.UpdateLayout();
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.SurfaceRaised"), PumpTimeout);

        Enter(row);
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200)); // geçiş penceresinden fazlası — durağan olmalı

        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ glow-once ile etkileşim

    /// <summary>
    /// [review round 1 fix — DEĞİŞEN KURAL] Eski davranış (Task 7 ilk turu): parıltı sürerken hover gelirse zemin
    /// animasyonu KESİLİR, "ATLAMA olmadan" doğru hedefe devrederdi. Bu YANLIŞTI — prototipin CSS
    /// <c>@keyframes bo-glow-once</c>'u satırın <c>background</c>'ını (hover dahil) ezer, yay 1.1s tam oynar
    /// (BuildApp.jsx:19, 1109, 1115). Doğru kural: hover parıltı bitene dek zemine HİÇ dokunmaz — zemin parıltının
    /// KENDİ rengidir (yeşilden şeffafa), <c>Brush.Surface</c> DEĞİL — parıltı doğal olarak bitince BİR KEZ doğru
    /// hedefe oturur.
    /// </summary>
    [StaFact]
    public void Hover_that_begins_mid_glow_waits_for_the_glow_to_finish_before_taking_the_ground()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 0, 0, 2100)); // hatasız → Done UYGUN

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Last();
        DispatcherPump.PumpUntil(() => row.GlowPlayCount >= 1, PumpTimeout);

        // Non-vacuous: parıltı GERÇEKTEN sürüyor — yeşil, canlı bir saat; satır tıklanamaz (Brush.Surface hedefi
        // parıltının rengiyle KARIŞMAYACAK kadar farklı, aşağıdaki NotEqual'lar için gerekli ön koşul).
        Assert.True(((SolidColorBrush)row.Background).HasAnimatedProperties);
        Assert.NotEqual(Colors.Transparent, DsResources.ColorOf(row.Background));
        Assert.False(row.ViewModel!.IsClickable);
        var surfaceColor = DsResources.TokenColor(host, "Brush.Surface");
        Assert.NotEqual(surfaceColor, DsResources.ColorOf(row.Background));

        // Parıltı devam ederken (1.1s'nin başlarında) hover gelir.
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(150));
        Enter(row);

        // [KİLİT NOKTA] Hover gelmesinden hemen sonra bile (parıltının hâlâ ortasında) zemin parıltının KENDİ
        // yayında kalır: hover hedefine SIÇRAMAZ ve parıltının saati KESİLMEZ. Eski (round 1) kod burada anında
        // Brush.Surface'e geçip animasyonu sökerdi — bu assert o davranışa karşı KIRMIZI verir.
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(150));
        Assert.True(((SolidColorBrush)row.Background).HasAnimatedProperties, "hover parıltının saatini KESMEMELİ");
        Assert.NotEqual(surfaceColor, DsResources.ColorOf(row.Background)); // hâlâ parıltının kendi rengi/yayı

        // Parıltı doğal süresini (1.1s) tamamlayınca — ve ancak o zaman — hover zemini görünür olur.
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == surfaceColor, TimeSpan.FromSeconds(3));
        Assert.Equal(surfaceColor, DsResources.ColorOf(row.Background));
        Assert.Equal(1, row.GlowPlayCount); // parıltı YENİDEN oynamadı — yalnız doğal bitişte zemin oturdu

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        Assert.Equal(1, row.GlowPlayCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [review round 1 · finding 4] Fare parıltı BAŞLAMADAN (satır henüz <c>Loaded</c> olmadan, dolayısıyla
    /// <c>ApplyGlow</c> hiç çalışmadan) satırın üstüne girerse — hover niyeti kaybolmamalı: parıltı yine tam
    /// oynar (prototipin CSS keyframe'i her koşulda 0%'dan başlar, mevcut hover'ı da ezer) ama parıltı bitince
    /// satır hover zemininde belirir, çünkü fare hâlâ üstündedir.
    /// </summary>
    [StaFact]
    public void Hover_that_begins_before_the_glow_starts_still_waits_and_settles_on_the_hover_ground_once_it_ends()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 0, 0, 2100)); // hatasız → Done UYGUN

        // View'ı BİLEREK henüz bir pencereye/host'a takmadan kur: DataContext ataması satırları senkron üretir
        // (RebuildRows), ama hiçbiri PresentationSource'a bağlı değildir — Loaded (dolayısıyla ApplyGlow) HENÜZ
        // ateşlenmedi.
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        var row = view.Rows.Last();
        Assert.False(row.IsLoaded); // non-vacuous ön koşul: parıltı GERÇEKTEN henüz başlamadı
        Assert.Equal(0, row.GlowPlayCount);

        Enter(row); // Loaded'dan (dolayısıyla parıltıdan) ÖNCE hover başlar

        var window = DsResources.Realize(host, view); // burada Loaded ateşlenir → ApplyGlow parıltıyı BAŞTAN oynatır
        DispatcherPump.PumpUntil(() => row.GlowPlayCount >= 1, PumpTimeout);
        Assert.True(((SolidColorBrush)row.Background).HasAnimatedProperties); // parıltı GERÇEKTEN çalışıyor

        var surfaceColor = DsResources.TokenColor(host, "Brush.Surface");
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == surfaceColor, TimeSpan.FromSeconds(3));
        Assert.Equal(surfaceColor, DsResources.ColorOf(row.Background)); // fare hâlâ üstünde → hover zemini oturdu
        Assert.Equal(1, row.GlowPlayCount);
        GC.KeepAlive(window);
    }
}
