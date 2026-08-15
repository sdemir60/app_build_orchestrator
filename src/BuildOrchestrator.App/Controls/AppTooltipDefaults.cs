using System.Windows;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.7.0] Tooltip davranışının uygulama geneli varsayılanları: <b>gecikmesiz açılır</b>, fare
/// ayrılana kadar açık kalır ve devre dışı bırakılmış öğelerde de görünür.
///
/// <para><b>Neden burada ve neden metadata ile:</b> bu üç ayar <see cref="ToolTipService"/>'in ATTACHED
/// property'leridir ve WPF onları tooltip'in kendisinden değil, tooltip'e SAHİP olan öğeden okur. Değerler
/// <c>Controls.xaml</c>'deki <c>ToolTip</c> stiline yazılmıştı — hiçbir etkileri yoktu ve her tooltip WPF'in
/// varsayılan ~1 saniyelik gecikmesiyle açılıyordu; sahada bu "tooltip hiç çıkmıyor" olarak görüldü, çünkü
/// fare bir saniye beklemeden geçtiğinde gerçekten açılmıyorlar.</para>
///
/// <para>Alternatif her tooltip sahibine tek tek yazmaktı (yüzlerce yer, kopya YASAK). Metadata ezmesi
/// varsayılanı TEK yerde değiştirir; bir yüzey isterse kendi değerini yine de yazabilir.</para>
/// </summary>
public static class AppTooltipDefaults
{
    private static bool _applied;

    /// <summary>Uygulama başlarken bir kez çağrılır. Metadata ezmesi process başına TEK kez yapılabilir,
    /// bu yüzden idempotenttir (testler de çağırır).</summary>
    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        // Gecikmesiz: §2.3 "GECİKMESİZ" ve §2.4'ün hover tooltip'leri bir saniye beklemez.
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(0));
        // Fare ayrılana kadar açık: int.MaxValue = "sonsuz" (WPF varsayılanı 5 sn'de kapatırdı).
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(int.MaxValue));
        // Devre dışı öğede de görünür: bakım kutusunun Clean/Optimize butonları tam olarak bunun için
        // tooltip taşır ("not available yet") ve devre dışıdırlar.
        ToolTipService.ShowOnDisabledProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
    }
}
