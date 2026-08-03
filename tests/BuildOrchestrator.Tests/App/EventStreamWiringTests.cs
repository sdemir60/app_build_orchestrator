using System.Windows;
using System.Windows.Input;
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

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

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

    /// <summary>Gerçek sol-tuş bırakışı. <c>UIElement.MouseLeftButtonUp</c> <b>Direct</b> yönlendirmelidir;
    /// kabarmayı <see cref="Mouse.MouseUpEvent"/> üzerindeki WPF sınıf handler'ı üretir — bu yüzden test
    /// gerçek girdi olayını yükseltir, satırın handler'ını doğrudan çağırmaz.</summary>
    private static void ReleaseLeft(UIElement target) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseUpEvent,
        });

    // ---------------------------------------------------------------- 1.2 satır tıklaması → SelectProject

    [StaFact]
    public void Clicking_a_stream_row_selects_the_project_it_reports()
    {
        var vm = NewVm();
        var (view, window) = RealizeEmpty(vm);

        // Olaylar realize'den SONRA akar → satır ARTIMLI (CollectionChanged/Insert) koldan doğar.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", ProjectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", ProjectId, 1200)); // tıklanabilir "A built (1.2s)" satırı

        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == ProjectId);
        Assert.Null(vm.SelectedProjectId); // ön-koşul: seçim yok

        ReleaseLeft(row);

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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", ProjectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", ProjectId, 1200));

        vm.SelectProject(ProjectId);
        var syncRow = view.Rows.First(r => r.ViewModel?.ProjectId is null);

        ReleaseLeft(syncRow);

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
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", "up to date"));

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
}
