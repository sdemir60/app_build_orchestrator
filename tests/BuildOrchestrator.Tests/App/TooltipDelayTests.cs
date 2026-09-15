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

    /// <summary>[Task 9 · B] <see cref="AppTooltipDefaults.DelayMsFor"/> birim hatasını pinler:
    /// <c>TimeSpan.Milliseconds</c> yalnız 0-999 bileşenidir, TOPLAM ms değildir — eski kod
    /// <c>SystemParameters.MouseHoverTime.Milliseconds</c> okuyordu, yani hover süresi ≥1 sn olduğunda (ör.
    /// 1.2 sn) yanlış bir değer (200) dönüyordu. Doğrusu <c>TotalMilliseconds</c>'tır.</summary>
    [Fact]
    public void DelayMsFor_returns_the_total_milliseconds_not_the_sub_second_component()
    {
        Assert.Equal(1200, AppTooltipDefaults.DelayMsFor(TimeSpan.FromMilliseconds(1200)));
        Assert.Equal(400, AppTooltipDefaults.DelayMsFor(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(0, AppTooltipDefaults.DelayMsFor(TimeSpan.Zero));
    }
}
