using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T35/C1] ShellRoot kök yerleşimi GERÇEKTEN realize olabilmeli. REGRESYON: action bar satırı
/// (<c>RowDefinition.Height</c>, GridLength) bir <b>Double</b> token'ı (<c>Size.ActionBarHeight</c>)
/// <c>DynamicResource</c> ile alıyordu → üretimde Application.Resources'tan çözülünce GridLength'e set
/// EDİLEMEYİP <see cref="System.Windows.Markup.XamlParseException"/> atıyordu (uygulama HİÇ açılmıyordu).
/// Kullanıcı ilk gerçek launch'ta bildirdi; GÖZLE KONTROL yapılmadığı için otomatik testte yakalanmamıştı.
/// Bu test ShellRoot'u tam realize eder — throw = kırmızı.
/// </summary>
[Collection("Console UI (serial)")]
public class ShellRootTests
{
    [StaFact]
    public void The_shell_root_realizes_without_a_type_mismatch_on_the_action_bar_row()
    {
        var host = DsResources.NewHost();
        var shell = new ShellRoot();
        var window = DsResources.Realize(host, shell); // fix öncesi: Size.ActionBarHeight (Double) → RowDefinition.Height (GridLength) throw
        Assert.NotNull(window);
        GC.KeepAlive(window);
    }
}
