using System.IO;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using H.NotifyIcon.Core;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/T5] VM ↔ gösterge kablajı: hangi sinyal neyi besliyor.
///
/// <para><b>Bu sınıfın asıl derdi TEK KAYNAKTIR.</b> Tepsideki bildirim ile ekrandaki şerit AYNI cümleyi
/// söylemek zorundadır ve sayaç da şeridin kullandığı sayı çiftinin ta kendisi olmalıdır. İkisi de sessizce
/// ayrışabilecek türden şeylerdir: metin ikinci kez derlenirse ya da sayaç ikinci kez hesaplanırsa arayüz
/// iki farklı gerçek anlatır ve kimse fark etmez.</para>
///
/// <para>Kablaj <c>MainWindow.OnSourceInitialized</c>'da DEĞİL ayrı bir bağlayıcıda yaşıyor çünkü o metot
/// gerçek bir tepsi ikonu kurar ve global kısayol kaydeder — süit onu bilerek hiç çalıştırmaz
/// (<c>MainWindowRealizeTests</c> sınıf özeti). Buradaki testler ne pencere ne HWND ister.</para>
/// </summary>
public sealed class TrayIndicatorBinderTests
{
    // ---------------------------------------------------------------- dikişler

    private sealed class SpyView : ITrayBuildIndicatorView
    {
        public readonly List<(int Done, int Total)> Counters = [];
        public Action? PendingExit;
        public int Shows;

        public void ShowLoop() => Shows++;
        public void ShowStatic() => Shows++;
        public void UpdateCounter(int done, int total) => Counters.Add((done, total));
        public void BeginExit(Action onFinished) => PendingExit = onFinished;
        public void HideNow() { }

        public void FinishExit()
        {
            var callback = PendingExit ?? throw new InvalidOperationException("BeginExit hiç çağrılmadı");
            PendingExit = null;
            callback();
        }
    }

    private sealed class SpyNotifier : ITrayRunNotifier
    {
        public int Count;
        public string? LastMessage;
        public bool? LastHealthy;

        public void ShowRunFinished(string message, bool healthy)
        {
            Count++;
            LastMessage = message;
            LastHealthy = healthy;
        }
    }

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new BuildOrchestrator.App.Services.EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
        { RootPath = @"D:\repo" };

    /// <summary>Bağlanmış bir üçlü: VM + controller + iki casus. Pencere TEPSİDE (gizli) kabul edilir —
    /// göstergenin var olma koşulunun yarısı budur.</summary>
    private static (RunViewModel Vm, SpyView View, SpyNotifier Notifier, TrayBuildIndicatorController Controller) Bound()
    {
        var vm = NewVm();
        var view = new SpyView();
        var notifier = new SpyNotifier();
        var controller = new TrayBuildIndicatorController(view, notifier);
        controller.SetMainWindowVisible(false);
        TrayIndicatorBinder.Attach(vm, controller);
        return (vm, view, notifier, controller);
    }

    /// <summary>İki projelik bir topoloji + koşan bir run — sayaç ve bitiş metni için ortak zemin.</summary>
    private static void StartRun(RunViewModel vm)
    {
        var nodes = new List<ProjectNode>
        {
            MainWindowHost.Node("A", 0),
            MainWindowHost.Node("B", 1),
        };
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 2, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(MainWindowHost.IdOf("A"), "A", true),
            new BuildPreviewItem(MainWindowHost.IdOf("B"), "B", true),
        ]));
    }

    // ---------------------------------------------------------------- sayaç (K-6)

    /// <summary>
    /// Sayaç, şeridin "Building {fin}/{wb}" satırındaki ÇİFTİN kendisidir — ikinci bir hesap yok.
    ///
    /// <para>İddia bilerek şeridin METNİ üzerinden kurulur: göstergeye giden sayılar ile ekrandaki satırın
    /// yazdığı sayılar aynı olmalıdır. İki ayrı property okumayı karşılaştırmak, ikisinin de aynı yanlış
    /// kaynaktan gelmesi hâlinde bir şey kanıtlamazdı.</para></summary>
    [Fact]
    public void Counter_properties_track_the_ribbon_inputs()
    {
        var (vm, view, _, _) = Bound();
        StartRun(vm);

        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("A"), 10));

        var (done, total) = view.Counters[^1];
        Assert.Contains($"{done}/{total}", vm.RibbonLine.Text, StringComparison.Ordinal);
        Assert.Equal(vm.FinishedOfWillBuild, done);
        Assert.Equal(vm.WillBuildCount, total);
    }

    [Fact]
    public void The_counter_reaches_the_indicator_without_the_window_being_involved()
    {
        var (vm, view, _, _) = Bound();
        StartRun(vm);

        // Gösterge açık (pencere gizli + faz Running) → sayaç akmalı.
        Assert.NotEmpty(view.Counters);
        Assert.Equal((0, 2), view.Counters[^1]);
    }

    // ---------------------------------------------------------------- bitiş metni (K-5)

    /// <summary>
    /// Bildirim, şeridin O ANKİ satırını AYNEN taşır — yeni bir özet formatlayıcı YOKTUR.
    ///
    /// <para>Metnin faz anında değil BİLDİRİM anında okunması kritiktir: koşu biterken VM önce <c>Phase</c>'i,
    /// hemen ardından <c>Counters</c>'ı yayınlar. Faz anında yakalanan bir metin yarım bir cümle olurdu.</para></summary>
    [Fact]
    public void Terminal_transition_hands_the_current_ribbon_line_to_the_tray_controller()
    {
        var (vm, view, notifier, _) = Bound();
        StartRun(vm);
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("A"), 10));
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("B"), 12));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 0, 0, 1234, 0));
        view.FinishExit();

        Assert.Equal(1, notifier.Count);
        Assert.Equal(vm.RibbonLine.Text, notifier.LastMessage);
        Assert.StartsWith("Completed — ", notifier.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_run_reaches_the_notifier_as_unhealthy()
    {
        var (vm, view, notifier, _) = Bound();
        StartRun(vm);
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("A"), 10, "compile error"));
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("B"), 12));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 1, 0, 0, 1234, 0));
        view.FinishExit();

        Assert.False(notifier.LastHealthy);
        Assert.Equal(vm.RibbonLine.Text, notifier.LastMessage);
        Assert.Contains("failed", notifier.LastMessage, StringComparison.Ordinal);
    }

    /// <summary>Tepsi menüsünden Stop: drain bitince bildirim "Stopped — …" satırını taşır (kaç projenin
    /// derlenmeden kaldığı dahil).</summary>
    [Fact]
    public void A_stopped_run_reports_the_stopped_line()
    {
        var (vm, view, notifier, _) = Bound();
        StartRun(vm);
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("A"), 10));
        // B UÇUŞTA kalır: Stop anında derlenmekteydi ve sonucu hiç gelmedi. Bu satır önemlidir — koşu
        // serbest bırakılınca (IsRunning=false) o satır "derlenen"den "derlenmemiş"e geçer ve bu geçiş
        // fazdan SONRA yayınlanır.
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("B"), "B"));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 1, 900, 0));
        view.FinishExit();

        Assert.Equal(1, notifier.Count);
        Assert.Equal(vm.RibbonLine.Text, notifier.LastMessage);
        Assert.StartsWith("▸ Stopped — ", notifier.LastMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// [K-4] Motor koşu ORTASINDA ölürse özel bir dal YAZILMAZ: faz zaten terminale çekilir, bildirim de
    /// şeridin o anki (kalıcı kırmızı) satırını taşır.
    /// </summary>
    [Fact]
    public void An_engine_death_mid_run_reports_the_engine_died_line()
    {
        var (vm, view, notifier, _) = Bound();
        StartRun(vm);

        vm.OnEngineExited(exitCode: 1);
        view.FinishExit();

        Assert.Equal(1, notifier.Count);
        Assert.Equal(vm.RibbonLine.Text, notifier.LastMessage);
        Assert.False(notifier.LastHealthy);
    }

    // ---------------------------------------------------------------- görünürlük

    [Fact]
    public void A_run_that_starts_while_the_window_is_hidden_raises_the_overlay()
    {
        var (vm, view, _, _) = Bound();

        StartRun(vm);

        Assert.True(view.Shows > 0);
    }

    [Fact]
    public void Nothing_is_shown_while_the_window_is_visible()
    {
        var vm = NewVm();
        var view = new SpyView();
        var controller = new TrayBuildIndicatorController(view, new SpyNotifier());
        TrayIndicatorBinder.Attach(vm, controller);   // pencere GÖRÜNÜR (varsayılan)

        StartRun(vm);

        Assert.Equal(0, view.Shows);
    }

    // ---------------------------------------------------------------- tek kaynak guard'ı

    /// <summary>
    /// Şerit satırı kaynak ağacında TAM BİR yerde derlenir.
    ///
    /// <para>İkinci bir <c>RibbonText.Compose</c> çağrısı, tepsideki bildirimin ekrandaki şeritten sessizce
    /// ayrışabileceği anlamına gelir — kuralın tamamı budur. (Tanımın kendi dosyası hariç.)</para></summary>
    [Fact]
    public void The_ribbon_line_is_composed_in_exactly_one_place()
    {
        var callers = RepoPaths.AppSourceFiles("*.cs")
            .Where(f => File.ReadAllText(f).Contains("RibbonText.Compose(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Equal([Path.Combine("ViewModels", "RunViewModel.cs")], callers);
    }

    // ---------------------------------------------------------------- balloon ikonu

    /// <summary>
    /// Sonuç → balloon ikonu. Başarılı bir derlemeye uyarı/hata ikonu koymak yanlış sinyaldir.
    ///
    /// <para>Kural <see cref="AppTrayIcon"/>'un içinde SAF bir metot olarak durur çünkü sınıfın kendisi
    /// kurulamaz: ctor'u gerçek bir <c>TaskbarIcon</c> yaratır (headless süitte tepsi yoktur). Pin bu yüzden
    /// eşlemenin kendisine kurulur — plandaki "sarılamıyorsa çağıran koda kur" maddesinin karşılığı.</para></summary>
    [Theory]
    [InlineData(true, NotificationIcon.Info)]
    [InlineData(false, NotificationIcon.Error)]
    public void Tray_icon_run_finished_notification_uses_info_or_error(bool healthy, NotificationIcon expected)
        => Assert.Equal(expected, AppTrayIcon.RunFinishedIcon(healthy));
}
