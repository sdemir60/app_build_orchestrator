using System;
using System.Threading.Tasks;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.15.0 → v1.19.0 §2.9] <c>Pull before build</c> switch'i: harici çalışma kopyaları her build'den ÖNCE
/// güncellensin mi. Settings → General'ın BUILD grubunda durur (v1.15.0'da External projects bölümünün başlık
/// satırındaydı); alt bara (per-run seçimler) konulmaz.
///
/// <para>Burada yalnız bu ayara özgü kural durur: notu yalnız tanımlı harici proje varken yazılır. Ayarın zinciri — varsayılan (açık),
/// taslak, ayar dosyası, Save, kabuğun tohumu ve kalıcılığı, General sayfasının switch'i — öteki General ayarlarıyla birlikte
/// <see cref="GeneralSettingChainTests"/>'tedir; değerin motora gidişi <see cref="ExternalProjectsWiringTests"/>'te.</para>
/// </summary>
public class PullBeforeBuildTests
{
    [Fact]
    public async Task A_workspace_without_external_projects_stays_quiet_even_when_the_switch_changes()
    {
        // Harici projesi olmayan kurulumda bayrak hiçbir şey yapmaz; orada not yazmak olmayan bir işi anlatırdı.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], pullExternalsBeforeBuild: false);

        Assert.False(run.UpdateExternals);                              // uygulanır
        Assert.DoesNotContain(GeneralSettingChains.Of(GeneralSetting.PullBeforeBuild).Label, run.GetRunDocumentText(),
            StringComparison.Ordinal);                                  // ama sessiz
    }
}
