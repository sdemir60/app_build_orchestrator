using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/FIX1] İkinci instance mevcut pencereyi öne getirme DENEMESİNİN sonucundan türeyen KARAR — balloon
/// gösterilecek mi + hangi çıkış kodu — WPF'ten ayrıştırılmış saf bir dikiş (<see cref="SecondInstanceGate.Decide"/>)
/// üzerinden test edilir. (Önceki review Minor'ı: <c>false→balloon</c> yolu YALNIZ compile-covered'dı; gerçek bir
/// ikinci process başlatmadan ya da gerçek tray'e dokunmadan kararın İKİ dalı da burada pinlenir.)
/// <para>[P3 · Task 4] <see cref="SecondInstanceGate.Decide"/> artık bir <see cref="IUiStateStore"/> alır —
/// yalnız öne getirme BAŞARISIZ olduğunda okunur (öne getirildiyse balon zaten yok, store'a hiç dokunulmaz;
/// <see cref="Activation_success_shuts_down_silently_without_a_balloon"/> bunu bir <see cref="ThrowingStore"/>
/// ile sınar).</para>
/// </summary>
public class SecondInstanceGateTests
{
    /// <summary>Öne getirme BAŞARILI dalında store'un HİÇ okunmadığını kanıtlamak için — dokunulursa test
    /// kırmızıya döner (assertion değil, bir istisna ile).</summary>
    private sealed class ThrowingStore : IUiStateStore
    {
        public UiState Load() => throw new InvalidOperationException("Store okunmamalıydı: öne getirme başarılı.");
        public void Save(UiState state) => throw new InvalidOperationException("Store'a yazılmamalıydı.");
    }

    [Fact] // öne getirilebildi → SESSİZ ve temiz kapan: balloon YOK, çıkış kodu 0, store OKUNMAZ
    public void Activation_success_shuts_down_silently_without_a_balloon()
    {
        var outcome = SecondInstanceGate.Decide(activated: true, new ThrowingStore());

        Assert.False(outcome.ShowBalloon);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact] // öne GETİRİLEMEDİ → SESSİZ KALMA: balloon iste (Show notifications varsayılan AÇIK) + AYRIŞAN kod
    public void Activation_failure_requests_a_balloon_and_a_distinct_exit_code()
    {
        var outcome = SecondInstanceGate.Decide(activated: false, new SettingsDialogHost.FakeStore());

        Assert.True(outcome.ShowBalloon);
        Assert.Equal(BuildOrchestrator.App.App.SecondInstanceActivationFailedExitCode, outcome.ExitCode);
        Assert.Equal(3, outcome.ExitCode); // named constant == 3 (machine-only ayrım korunur)
    }

    /// <summary>[P3 · Task 4] Show notifications kapalıyken öne getirme başarısız olsa bile balon YOK — ama
    /// ayrışan çıkış kodu (3) korunur: makine bu iki durumu (kapatıldı / öne getirilemedi) hâlâ ayırt edebilir,
    /// yalnız kullanıcı arayüzü sessizleşir.</summary>
    [Fact]
    public void With_notifications_off_a_failed_activation_exits_quietly_with_its_distinct_code()
    {
        var store = new SettingsDialogHost.FakeStore();
        store.Save(new UiState { ShowNotifications = false });

        var outcome = SecondInstanceGate.Decide(activated: false, store);

        Assert.False(outcome.ShowBalloon);
        Assert.Equal(BuildOrchestrator.App.App.SecondInstanceActivationFailedExitCode, outcome.ExitCode);
    }

    /// <summary>[P3 · Task 4] Ayar KALICI durumdan (ui-state.json) gelir — bu süreç açılırken hiçbir taslak/VM
    /// yaşamaz, o yüzden okunacak tek yer dosyanın kendisidir. Dosya hiç yoksa katalog varsayılanı (açık) geçerli
    /// olur.</summary>
    [Fact]
    public void The_second_instance_reads_the_setting_from_the_state_file()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");

        var whenMissing = SecondInstanceGate.Decide(activated: false, new JsonUiStateStore(path));
        Assert.True(whenMissing.ShowBalloon); // dosya yok → katalog varsayılanı (açık)

        new JsonUiStateStore(path).Save(new UiState { ShowNotifications = false });
        var whenOff = SecondInstanceGate.Decide(activated: false, new JsonUiStateStore(path));
        Assert.False(whenOff.ShowBalloon);
    }

    // ---------------------------------------------------------------- kaynak (kablo)

    /// <summary>
    /// [P3 · Task 4] Üretimdeki TEK çağıran: <c>App.OnStartup</c>, <see cref="SecondInstanceGate.Decide"/>'e
    /// kalıcı durumu <c>ui-state.json</c>'dan okuyan gerçek bir <c>JsonUiStateStore</c> verir. Pin bunu
    /// ÇALIŞTIRMAZ, kaynaktaki METNİNİ arar — <c>App</c> ikinci-instance dalı DI kurmadan döner, yani gerçek bir
    /// ikinci process başlatmadan sınanamaz (<see cref="TrayMenuTests.MainWindow_wires_the_tray_stop_item_to_the_run_view_models_stop_command"/>
    /// ile AYNI desen/gerekçe).
    /// </summary>
    [Fact]
    public void App_OnStartup_feeds_the_gate_from_the_persisted_ui_state()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        var wiring = new Regex(
            @"SecondInstanceGate\.Decide\(\s*_singleInstance\.ActivateExistingInstance\(TimeSpan\.FromSeconds\(3\)\),\s*new JsonUiStateStore\(JsonUiStateStore\.DefaultPath\)\)");

        Assert.Single(wiring.Matches(source));
    }
}
