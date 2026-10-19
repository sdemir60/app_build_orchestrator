using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19] <b>Arka plandaki pencereye bir tıkla dönmek.</b> Windows tıkı, pencereyi etkinleştirdikten SONRA
/// teslim eder; dönüş (son Sync'ten beş saniye geçtiyse) o anda sessiz bir Sync ister ve Sync istendiği ANDA koşu komutlarını ve
/// Debug|Release'i kapatır. Tık kendi tazelemesini yapan bir komuta iniyorsa — açık Build (diski baştan planlar: Sync'in taramasının ve
/// kararlarının aynısı) ya da Debug|Release'in seçili olmayan öğesi (kendi Sync'ini başlatır) — dönüşün Sync'i istenmez ve tık normal
/// çalışır. Başka her dönüş (klavye, görev çubuğu, tepsi, başka bir yere tık) bugünkü gibi Sync'ler.
///
/// <para>Ölçülen kusur (kullanıcı bildirimi, iki monitör): pencere öbür ekranda açıkken Build'e tıklamak pencereyi öne getiriyor, dönüşün
/// Sync'i Build'i tıkın altından kapatıyor ve tık hiçbir şey yapmıyordu; Debug|Release'te ilk tık yutuluyor, ikincisi geçiyordu.</para>
///
/// <para>Harness: gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir — testler pencerenin etkinleşme
/// girişini (<see cref="MainWindow.OnActivated"/>) tıkın çubuktaki konumuyla doğrudan sürer. Çubuk ekran dışı gerçek bir pencerede yüklenir
/// (<see cref="MainWindowHost.WithLoadedActionBar"/>: Build düğmesinin komutu <c>Loaded</c>'da bağlanır). Basış WPF'in yaptığı gibidir:
/// kapalı öğeye basılamaz (<see cref="CommandPress"/>). Gönderimler sahte ama canlı bir motora gider; saat enjekte edilir ve son Sync'ten
/// bir dakika sonrasına alınır. Kökte git dizini yoktur: dönüş HEAD'i okuyamaz ve sessiz Sync'ler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class WindowReturnClickTests
{
    /// <summary>Pencereye tıklamadan dönmenin ve tıklanan yerin üç biçimi — hiçbiri kendi tazelemesini yapmaz.</summary>
    public enum OtherReturn { Keyboard, ClickOnAChip, ClickOnTheSelectedConfiguration }

    /// <summary>Kullanıcı bir dakika başka pencerede çalıştı: tek projeli, Sync'lenmiş kabuk; gönderimler toplanır, çubuk yüklüdür.
    /// Gövde dönüşü kendisi yapar.</summary>
    private static void AfterAMinuteElsewhere(Action<MainWindow, RunViewModel, ActionBar, List<IpcCommand>> body)
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, () => now, ["A"]);
        MainWindowHost.AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        now += 60_000;
        MainWindowHost.WithLoadedActionBar(window, (_, bar) => body(window, vm, bar, sent));
        GC.KeepAlive(window);
    }

    /// <summary>Kusurun kendisi: pencereyi Build'in üstünden öne getiren tık dönüşün Sync'ini istemez; Build açık kalır ve tık onu
    /// başlatır.</summary>
    [StaFact]
    public void A_click_on_build_that_brings_the_window_back_requests_no_return_sync_and_builds() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            var build = (Button)bar.Split.PrimaryHalf!;
            Assert.True(build.IsEnabled);                                   // ön-koşul: pencere arkadayken Build açık

            window.OnActivated(ActionBarTests.CenterIn(bar, build));        // tık pencereyi Build'in üstünden öne getirdi

            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // dönüşün Sync'i istenmedi…
            Assert.True(build.IsEnabled);                                   // …Build tıkın altında açık kaldı
            CommandPress.Invoke(build);                                     // tık Build'e iner
            DispatcherPump.DrainToIdle();
            Assert.True(vm.IsStarting);                                     // koşu tıkla başladı (kilit tıkta iner)
            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // tazelemeyi koşunun kendi planı yapar
        });

    /// <summary>Asıl bulgu: pencereyi Debug|Release'in seçili olmayan öğesinin üstünden öne getiren tık geçişi yapar — ikinci tık
    /// gerekmez. Tek Sync gider, o da geçişin kendi Sync'idir (yeni configuration'la).</summary>
    [StaFact]
    public void A_click_on_the_configuration_not_selected_that_brings_the_window_back_switches_it() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            var release = ActionBarTests.ConfigurationItem(bar, "Release");
            Assert.Equal("Debug", vm.Configuration);                        // ön-koşul
            Assert.True(release.IsEnabled);

            window.OnActivated(ActionBarTests.CenterIn(bar, release));      // tık pencereyi Release'in üstünden öne getirdi

            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // dönüşün Sync'i istenmedi
            CommandPress.Select(release);                                   // tık seçili olmayan öğeye iner
            Assert.Equal("Release", vm.Configuration);
            Assert.Equal("Release", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Configuration);
        });

    /// <summary>Kural dar kalır: kendi tazelemesini yapmayan her dönüş — klavye/görev çubuğu/tepsi (tık yok), bir chip'e tık, zaten
    /// seçili configuration'a tık (geçiş no-op) — bugünkü gibi Sync'ler ve Build Sync istendiği ANDA kapanır.</summary>
    [StaTheory]
    [InlineData(OtherReturn.Keyboard)]
    [InlineData(OtherReturn.ClickOnAChip)]
    [InlineData(OtherReturn.ClickOnTheSelectedConfiguration)]
    public void Any_other_return_still_syncs_and_closes_build_at_once(OtherReturn how) =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var build = (Button)bar.Split.PrimaryHalf!;
            Assert.True(build.IsEnabled);                                   // ön-koşul

            window.OnActivated(how switch
            {
                OtherReturn.Keyboard => null,
                OtherReturn.ClickOnAChip => ActionBarTests.CenterIn(bar, bar.SigmaChip),
                _ => ActionBarTests.CenterIn(bar, ActionBarTests.ConfigurationItem(bar, "Debug")),
            });

            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // dönüş Sync'ledi
            Assert.False(build.IsEnabled);                                  // Build o anda kapandı
        });
}
