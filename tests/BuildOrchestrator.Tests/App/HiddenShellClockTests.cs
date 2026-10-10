using System.Windows.Media.Animation;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Pencere, koşu GÖRÜNÜRKEN bitip sonra tepsiye inince hiçbir animasyon saati dönmeye devam etmemelidir (ARCHITECTURE §14.5:
/// "An infinite animation must stop being visible before it stops running").
///
/// <para><b>Neden bu dosya var (ÖLÇÜLDÜ):</b> koşu sürerken gizlenen pencere tepside boşta UI thread'ini 13 Mdöngü/s'de bırakır;
/// koşu pencere açıkken bitip 3 s sonra gizlenirse aynı boşta 118 Mdöngü/s ölçüldü ve çekirdek izi UI thread'in ~66 Hz'lik bir
/// kare döngüsünde uyandığını, <c>dotnet-trace</c> atfı da her karede <c>TimeManager.Tick</c> koştuğunu gösterdi
/// (<c>.claude/outputs/2026-10-06-00-14-desktop-measurements-final.md</c> §2). Sahip-başına gizlilik testleri
/// (<c>HiddenDecorativeClockTests</c>, <c>HiddenCursorClockTests</c>) her sonsuz saati tek tek pinler; burası ise kabuğun
/// BÜTÜNÜNE, ölçümdeki sırayla bakar: gerçek <c>MainWindow</c> gösterilen bir pencerede, hareket açık, koşu görünürken
/// biter, final oynarken gizlenir — ve sonlu geçişlerin de bitmesine yetecek bir süre sonra zamanlama ağacında dönen
/// saat aranır. Sahibi bulan araç <see cref="ActiveClockInspector"/>'dır; test kırmızıyken mesajı suçluyu adıyla söyler.</para>
///
/// <para><b>Gizleme üretimdeki gibi üç parçadır:</b> ekran dışı host'un <c>Hide()</c>'ı torunların <c>IsVisibleChanged</c>'ini
/// ateşler, gizli-yüzey sinyali host'a yazılır (üretimde pencerenin kendi <c>IsVisibleChanged</c>'i yazar) ve
/// <c>MainWindow.SetSurfaceHidden</c> kabuğun kapılarını (final kesilir, koreografi düşer) çalıştırır —
/// <c>HiddenSurfaceMeasurementTests</c> ile aynı üçlü.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HiddenShellClockTests
{
    /// <summary>Yerleşim / koşu başlangıcı / building hâli otursun diye bir nefes.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>Ölçümdeki sıra: koşu bitti, final sürerken (3 s sonra) gizlendi — burada final başlasın diye 1 s.</summary>
    private static readonly TimeSpan FinaleInFlight = TimeSpan.FromSeconds(1);

    /// <summary>Gizlendikten sonra her SONLU geçişin (finalin kesilişinde kurulan ≤ 1 s'lik opaklık ve kamera glide'ları)
    /// bitmesine yetecek pay: bundan sonra hâlâ dönen saat ya sonsuzdur ya da durmadan yeniden kuruluyordur.</summary>
    private static readonly TimeSpan FiniteTransitionsBudget = TimeSpan.FromSeconds(3);

    /// <summary>Gövde KENDİ STA thread'inde koşar (<see cref="StaThread.RunAsync(Action, string?)"/>): zamanlama ağacı dispatcher'ındır ve
    /// <c>[StaFact]</c> thread'leri kiralayıp paylaşır — paylaşılan bir dispatcher'da başka testlerin bıraktığı saatler bu testin
    /// "hiç saat yok" iddiasına karışırdı. Taze thread = taze dispatcher = yalnız bu testin saatleri.</summary>
    [Fact]
    public Task Hiding_the_shell_after_a_run_that_ended_visibly_leaves_no_animation_clock_active() =>
        StaThread.RunAsync(HideShellAfterVisibleRunEnd, name: "hidden-shell-clock-sta");

    private static void HideShellAfterVisibleRunEnd()
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(12);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var host = MainWindowHost.HostOffscreen(window);
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            DispatcherPump.PumpFor(Settle);

            MainWindowHost.PreviewBuild(vm, names);
            MainWindowHost.StartBuild(vm, names);
            DispatcherPump.PumpFor(Settle);
            foreach (var name in names)
            {
                MainWindowHost.StartProject(vm, name);
                MainWindowHost.LogLine(vm, name, 1, $"building {name}");
            }
            window.AppendConsoleBatch(string.Concat(names.Select(n => $"building {n}\n")), window.ConsoleReseedGen);
            DispatcherPump.PumpFor(Settle); // building satırları nefes alır, graf beads'i döner, şerit chip'leri spinner taşır
            foreach (var name in names) MainWindowHost.SucceedProject(vm, name);
            MainWindowHost.CompleteRun(vm, names.Length);
            Assert.True(window.Shell.GraphHost.IsEndFinalePlaying, "ön-koşul: görünür pencerede biten koşu finali oynatır");
            DispatcherPump.PumpFor(FinaleInFlight);
            Assert.NotEmpty(ActiveClockInspector.ActiveRootClocks(dispatcher)); // ön-koşul: görünürken dönen saatler var ve araç onları görüyor

            host.Hide();                            // üretimde Hide(): torunların IsVisibleChanged'i
            HiddenSurface.SetIsHidden(host, true);  // ...pencerenin yazdığı gizli-yüzey sinyali
            window.SetSurfaceHidden(true);          // ...ve kabuğun kendi kapıları (final kesilir, koreografi düşer)
            DispatcherPump.PumpFor(FiniteTransitionsBudget);

            var stillActive = ActiveClockInspector.ActiveRootClocks(dispatcher);
            Assert.True(stillActive.Count == 0,
                "gizli pencerede hâlâ dönen saat(ler):" + Environment.NewLine + ActiveClockInspector.Describe(stillActive));
            var nextTick = ActiveClockInspector.NextTickNeeded(dispatcher);
            Assert.True(nextTick < TimeSpan.Zero,
                $"zamanlama ağacı hâlâ tik istiyor ({nextTick}); kök saatler:" + Environment.NewLine +
                ActiveClockInspector.Describe(ActiveClockInspector.RootClocks(dispatcher)));
        }
        finally
        {
            host.Close();
        }
    }
}
