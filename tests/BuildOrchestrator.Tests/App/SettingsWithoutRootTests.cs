using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] <b>Ayarlar kök OLMADAN kaydedilebilir.</b> Repository root artık Save'in koşulu
/// değildir: boş kök "workspace yok" demektir ve uygulama ilk açılış görünümünde durur — liste panelinde kurulum
/// daveti (design v1.8.0 §2.4). Bu dosya kabuk seviyesindeki sonucu pinler: pencere üretim kablajıyla kurulur
/// (<see cref="MainWindowHost"/>), Save'in tek giriş noktası (<see cref="RunViewModel.ApplySettingsAsync"/>)
/// çağrılır ve EKRAN okunur.
///
/// <para><b>Ölçülen kusur (kullanıcı testi):</b> Settings'te Clear'dan sonra Save kapalıydı (<c>Repository root is
/// required</c>) — ayarsız duruma Settings'ten dönülemiyordu, bu yüzden kurulum daveti de hiç görülemiyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SettingsWithoutRootTests
{
    /// <summary>İlk açılışta kök girmeden katman kaydetmek geçerli bir Save'dir: davet kalır ve kartın <c>Layers</c>
    /// satırı kaydedilen sayıyı gösterir (<c>N defined</c>) — kök hâlâ <c>Not set</c>.</summary>
    [StaFact]
    public async Task Saving_layers_without_a_root_on_first_run_counts_them_on_the_invitation()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        Assert.Equal(("Not set", "Optional"), window.Shell.SetupChecklist); // ön-koşul: katman yok

        await vm.ApplySettingsAsync(
            [new LayerPattern(0, "^A", "Core"), new LayerPattern(1, "^B", "Api"), new LayerPattern(2, "^C", "Client")],
            null, []);

        Assert.Equal(("Not set", "3 defined"), window.Shell.SetupChecklist);
        GC.KeepAlive(window);
    }
}
