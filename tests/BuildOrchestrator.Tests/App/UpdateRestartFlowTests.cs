using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [motor · Task 11 · K6] <c>Restart to update</c> = kart kapanır, ekran <c>Closing &lt;ürün&gt;…</c> der, uygulama
/// güvenli tam çıkış yoluna girer (mevcut <c>RequestFullExit</c> — × ve tepsi → Exit ile AYNI yol: iş yoksa hemen
/// <c>ExitReady</c> → Shutdown). Kurulumu çıkışta Update.exe yapar (Task 12).
///
/// <para><b>Eski iddia</b> (<c>UpdateRestartScreenTests.When_the_screen_leaves_the_app_is_exactly_as_it_was</c>): ekran
/// oynayıp sönerdi ve uygulama AYNEN kalırdı — motora komut gitmez, seçim, faz, satırlar, hap ve teklif yerinde; motor
/// yokken tasarımın önizlemesi (plan U4). <b>Değişti</b> (K6, kullanıcı kararı 2026-09-30): Windows çalışan programın
/// dosyalarını değiştirmeye izin vermez — gerçek kurulum için pencere kapanmalı.</para>
///
/// <para>Kilit tarafı <c>UpdateRestartScreenTests.A_locked_restart_never_plays_the_screen</c>'den buraya taşındı: iddiası
/// (kilitli Restart ekranı açmaz, istek kapıdan geçmeden gelse bile) aynen korunur, kapanmama iddiası eklenir.</para>
///
/// <para>Harness: pencere GÖSTERİLMEZ (<see cref="MainWindowHost"/>); uygulamayı kapatan çağrı
/// (<c>MainWindow.ShutdownApplication</c>) sayaçlı bir sahteyle değiştirilir — uygulama kapanmaz. Ekranın zamanlayıcısı
/// sahtedir, gerçek bir saat kurulmaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UpdateRestartFlowTests
{
    /// <summary>Uçuşta iş yokken Restart: ekran <c>Closing &lt;ürün&gt;…</c> ile açılır ve uygulama hemen kapanır —
    /// bekleyiş açılmaz.</summary>
    [StaFact]
    public void Restart_to_update_shows_the_closing_screen_and_takes_the_safe_exit_path()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        window.UpdateRestartOverlay.Timer = new FakePollTimer();
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;
        vm.AvailableUpdate = UpdateOffers.Sample("9.9.0");

        vm.RestartToUpdateCommand.Execute(null);

        Assert.True(window.UpdateRestartOverlay.IsShowing);
        Assert.Equal(UpdateText.RestartStepLabel(UpdateRestartStep.Closing), window.UpdateRestartOverlay.PART_Step.Text);
        Assert.Equal(1, shutdowns); // iş yok → çıkış hemen
        Assert.False(vm.ExitPending);
        GC.KeepAlive(window);
    }

    /// <summary>Kilitli bir Restart ekranı AÇMAZ ve hiçbir şeyi kapatmaz — istek kapıdan geçmeden (doğrudan
    /// <c>Execute</c>) gelse bile: bir koşu sürerken kurulum onu yarıda keserdi.</summary>
    [StaFact]
    public void While_a_build_runs_the_restart_is_locked_and_nothing_closes()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        window.UpdateRestartOverlay.Timer = new FakePollTimer();
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;
        vm.AvailableUpdate = UpdateOffers.Sample();
        MainWindowHost.StartBuild(vm);
        Assert.NotNull(vm.UpdateRestartBlockedReason); // ön-koşul

        vm.RestartToUpdateCommand.Execute(null);

        Assert.False(window.UpdateRestartOverlay.IsShowing);
        Assert.Equal(0, shutdowns);
        Assert.False(vm.ExitPending);
        GC.KeepAlive(window);
    }
}
