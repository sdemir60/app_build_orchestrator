using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · maddeler 1.2 + 1.3] Event stream panelinin KENDİ kablajı — <b>tetikleyicinin kendisi</b>.
///
/// <para><b>Ölçülmüş boşluklar:</b>
/// <list type="bullet">
///   <item><b>1.2</b> — <c>EventStreamView.xaml.cs:489 OnClicked</c> (satır tıklaması → <c>SelectProject</c>)
///   hiçbir testte raise EDİLMİYORDU; mevcut testler <c>vm.SelectProject</c>'i DOĞRUDAN çağırıyor
///   (<c>RunViewModelTests.cs:40</c>, <c>EventStreamTests.cs:89</c>) — yani satırın kablosu koparılsa suite yeşil kalırdı.</item>
///   <item><b>1.3</b> — <c>LatestPillTests</c> kendi <c>Wiring</c> sınıfında ConsoleView kablajının bir
///   <b>KOPYASINI</b> kuruyor (<c>:48-50</c>). O kopya yeşilken stream panelinin KENDİ
///   <c>OnBottomAnchorChanged</c>'i (<c>:301-307</c>) bozulabilirdi.</item>
/// </list></para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamWiringTests
{
    private const string ProjectId = @"C:\p\a.csproj";

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    /// <summary>
    /// [fix-1 · I-B] ÜRETİM SIRASI: kabuk BOŞ VM ile realize edilir, olaylar SONRA akar.
    ///
    /// <para><b>Neden kritik:</b> ters sırada (önce <c>OnEvent</c>, sonra realize) satırlar
    /// <c>OnDataContextChanged → RebuildRows</c> toplu kolundan doğar. Üretimde canlı bir koşuda satırlar
    /// <c>StreamEvents.CollectionChanged → PART_Rows.Children.Insert</c> ARTIMLI kolundan doğar
    /// (<c>EventStreamView.xaml.cs:159-166</c>). Ters sıra o kolu hiç sınamaz — brief'in adıyla yasakladığı
    /// körlük budur. Emsal: <c>StickyRevealTriggerTests</c> / <c>StickyScrollTriggerTests</c>.</para></summary>
    private static (EventStreamView view, Window window) RealizeEmpty(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(host, view);
        Assert.Empty(view.Rows); // ön-koşul: kabuk BOŞ realize edildi (satırlar artımlı koldan doğacak)
        return (view, window);
    }

    // ---------------------------------------------------------------- 1.2 satır tıklaması → SelectProject

    [StaFact]
    public void Clicking_a_stream_row_selects_the_project_it_reports()
    {
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);

        // Olaylar realize'den SONRA akar → satır ARTIMLI (CollectionChanged/Insert) koldan doğar.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", ProjectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", ProjectId, 1200)); // tıklanabilir "A built (1.2s)" satırı

        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == ProjectId);
        Assert.Null(vm.SelectedProjectId); // ön-koşul: seçim yok

        MouseInput.ReleaseLeft(row);

        Assert.Equal(ProjectId, vm.SelectedProjectId);
        GC.KeepAlive(window);
    }

    /// <summary>Proje taşımayan satır (run başlığı gibi) tıklanınca seçim DEĞİŞMEZ — handler'ın
    /// <c>_vm?.ProjectId is { } id</c> kapısı. Kablo körlemesine "her satır seçer" olsaydı burası kırmızı olurdu.</summary>
    [StaFact]
    public void Clicking_a_row_that_carries_no_project_leaves_the_selection_untouched()
    {
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);

        vm.OnEvent(new SyncCompletedEvent("main", "abc", false, 1, 0, ToBuildCount: 1, UpToDateCount: 0)); // sync satırı: ProjectId YOK
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", ProjectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", ProjectId, 1200));

        vm.SelectProject(ProjectId);
        var syncRow = view.Rows.First(r => r.ViewModel?.ProjectId is null);

        MouseInput.ReleaseLeft(syncRow);

        Assert.Equal(ProjectId, vm.SelectedProjectId); // değişmedi
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 1.3 panelin KENDİ latest-pill kablajı

    /// <summary>
    /// Panelin KENDİ <c>PART_Scroll.ScrollChanged → _bottomAnchor → OnBottomAnchorChanged → PART_Pill.Visibility</c>
    /// zinciri: kullanıcı GERÇEKTEN yukarı kaydırınca pill belirir, dibe GERÇEKTEN dönünce kaybolur. Hiçbir adımda
    /// <c>_bottomAnchor</c>'a elle dokunulmaz (kopya kablaj DEĞİL).
    /// </summary>
    [StaFact]
    public void Scrolling_the_stream_away_from_the_bottom_reveals_the_latest_pill_and_returning_hides_it()
    {
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);

        // Olaylar realize'den SONRA akar → satırlar artımlı koldan doğar ve içerik CANLI büyür (üretimdeki gibi).
        for (int i = 0; i < 60; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", SkipReasons.UpToDate));

        // İçerik viewport'u GERÇEKTEN aşmalı — aksi halde "dipten uzaklık" hep 0 kalır ve test vacuous olurdu.
        DispatcherPump.PumpUntil(
            () => view.Scroll.ScrollableHeight > BottomAnchorDecision.DefaultThresholdPx, TimeSpan.FromSeconds(2));
        Assert.True(view.Scroll.ScrollableHeight > BottomAnchorDecision.DefaultThresholdPx,
            $"stream içeriği kaydırılabilir değil (ScrollableHeight={view.Scroll.ScrollableHeight}) — senaryo kurulamadı");

        // Kullanıcı tepeye kaydırır (gerçek ScrollViewer hareketi → gerçek ScrollChanged).
        view.Scroll.ScrollToVerticalOffset(0);
        DispatcherPump.PumpUntil(() => view.Pill.Visibility == Visibility.Visible, TimeSpan.FromSeconds(2));
        Assert.Equal(Visibility.Visible, view.Pill.Visibility);

        // ...ve gerçekten dibe dönünce kaybolur.
        view.Scroll.ScrollToVerticalOffset(view.Scroll.ScrollableHeight);
        DispatcherPump.PumpUntil(() => view.Pill.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(2));
        Assert.Equal(Visibility.Collapsed, view.Pill.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · dalga 2 · kapanış] Akışı izleyen stream'e satır eklenince görünüm YENİ dibe oturur. Satırı ekleyen kolun eşzamanlı
    /// yakalaması o an ScrollViewer'ın eklemeyi henüz görmemiş geometrisinden dibi hedefler (eski dip); yeni dibe iniş, yerleşimin ardından
    /// gelen <c>PART_Scroll.ScrollChanged</c>'ten (içerik büyümesi) gelir — bu pin o yolu korur.
    /// <para>PİN — yeşil doğar.</para>
    /// </summary>
    [StaFact]
    public void A_following_stream_lands_on_the_new_bottom_after_rows_are_appended()
    {
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);
        for (int i = 0; i < 60; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", SkipReasons.UpToDate));
        DispatcherPump.DrainToIdle();
        Assert.True(view.Scroll.ScrollableHeight > BottomAnchorDecision.DefaultThresholdPx, "test kurgusu: stream kaydırılabilir olmalıydı");
        Assert.True(view.FollowsBottom); // ön-koşul: akışı izliyor

        for (int i = 0; i < 5; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\late{i}.csproj", SkipReasons.UpToDate));
        DispatcherPump.DrainToIdle();

        Assert.Equal(view.Scroll.ScrollableHeight, view.Scroll.VerticalOffset, precision: 1);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [B2 fix 2 · madde 2] kullanıcı jesti pill animasyonunu durdurur

    /// <summary>
    /// [B2 fix 2 · madde 2] Stream'de de okuyucunun kaydırma jesti <c>⌄ latest</c>'in uçuştaki animasyonunu durdurur — tekerlek de kaydırma
    /// çubuğu da: kural konsol ve listeyle aynı tek kablodadır (<c>UserScrollSignal</c>).
    /// <para>KIRMIZI (madde 2'den önce, çubuk): stream'de animasyonu yalnız tekerlek durduruyordu (<c>ScrollAnimator.EnableUserCancellation</c>);
    /// çubuğu sürüklemek animasyona dokunmuyordu. Tekerlek satırı PİN: eski kancanın yerine geçen kablonun tekerleği de taşıdığını korur.</para>
    /// </summary>
    [StaTheory]
    [InlineData("wheel")]
    [InlineData("scrollbar")]
    public void A_scroll_gesture_during_the_streams_latest_pill_animation_stops_it(string gesture)
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);
        for (int i = 0; i < 60; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", SkipReasons.UpToDate));
        DispatcherPump.DrainToIdle();
        UserScrollGesture.DragVerticalBarOf(view.Scroll); // okuyucu çubuğu tuttu...
        view.Scroll.ScrollToVerticalOffset(0); // ...ve tepeye çıktı
        DispatcherPump.DrainToIdle();
        Assert.Equal(Visibility.Visible, view.Pill.Visibility); // ön-koşul: dipten uzakta

        CommandPress.Click(view.Pill.PillButton); // ⌄ latest
        DispatcherPump.DrainToIdle();
        Assert.True(ScrollAnimation.IsAnimating(view.Scroll), "test kurgusu: pill bir kaydırma animasyonu başlatmalıydı");

        if (gesture == "wheel") MouseInput.Wheel(view.Scroll, System.Windows.Input.Mouse.MouseWheelDeltaForOneLine);
        else UserScrollGesture.DragVerticalBarOf(view.Scroll);
        DispatcherPump.DrainToIdle();

        Assert.False(ScrollAnimation.IsAnimating(view.Scroll));
        GC.KeepAlive(window);
    }
}
