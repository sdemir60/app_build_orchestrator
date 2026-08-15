using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0] Tooltip'ler <b>GECİKMESİZ</b> açılır ve fare ayrılana kadar açık kalır.
///
/// <para><b>Neden bu dosya var:</b> iki ayar da <c>ToolTip</c>'in KENDİ stiline yazılmıştı
/// (<c>Controls.xaml</c>). WPF bu iki attached property'yi tooltip'ten değil, tooltip'e SAHİP olan öğeden
/// okur — yani hiçbir etkileri yoktu ve her tooltip WPF'in varsayılan ~1 saniyelik gecikmesiyle açılıyordu.
/// Sahada "tooltip hiç çıkmıyor" diye görüldü: fare bir saniye beklemeden geçtiğinde gerçekten hiç
/// açılmıyorlar.</para>
///
/// <para>Ayar bu yüzden TEK yerde, sahibin tipinde (<see cref="FrameworkElement"/>) metadata ile ezilir —
/// her tooltip sahibine tek tek yazmak (yüzlerce yer) kopya olurdu.</para>
/// </summary>
public class TooltipDelayTests
{
    [StaFact]
    public void A_tooltip_owner_opens_with_no_delay_and_stays_until_the_pointer_leaves()
    {
        AppTooltipDefaults.Apply(); // üretimde App başlatılırken çağrılır

        var owner = new Button { ToolTip = "In a dependency cycle" };

        Assert.Equal(0, ToolTipService.GetInitialShowDelay(owner));
        Assert.Equal(int.MaxValue, ToolTipService.GetShowDuration(owner));
    }

    /// <summary>Devre dışı bırakılmış öğe de tooltip gösterir — bakım kutusunun Clean/Optimize butonları
    /// tam olarak bunun için tooltip taşır ("not available yet").</summary>
    [StaFact]
    public void A_disabled_owner_still_shows_its_tooltip()
    {
        AppTooltipDefaults.Apply();

        var owner = new Button { ToolTip = "Clean — not available yet", IsEnabled = false };

        Assert.True(ToolTipService.GetShowOnDisabled(owner));
    }
}
