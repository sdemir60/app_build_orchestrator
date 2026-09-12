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

    /// <summary>
    /// [design v1.11.0 §2.4-4 · §9-13] <b>"Native" tooltip gecikmesi.</b> v1.11.0, proje satırında DS
    /// tooltip'ini yalnız uyarı üçgenine bıraktı: statü glyph'i ve hover ikon butonları (play · ⋯ · Explorer ·
    /// VS) <i>"tooltip taşımaz — anlamları belli; erişilebilirlik için native <c>title</c> + <c>aria-label</c>
    /// durur"</i>.
    ///
    /// <para>WPF'te HTML'in <c>title</c> özniteliğinin birebir karşılığı YOKTUR; en yakını, işletim
    /// sisteminin fare-üzerinde-bekleme süresiyle açılan sıradan bir <see cref="ToolTip"/>'tir. Bu yüzden o
    /// butonlar tooltip'lerini KORUR (aksi halde ikon-yalnız kontrolün ne yaptığını öğrenmenin hiçbir yolu
    /// kalmazdı) ama uygulama genelindeki GECİKMESİZ davranıştan çıkarılırlar: fare satır boyunca gezerken
    /// arka arkaya balon açılmaz, yalnız bir yerde bilerek beklenirse görünür.</para>
    /// </summary>
    public static int NativeDelayMs => System.Windows.SystemParameters.MouseHoverTime.Milliseconds;

    /// <summary>Bir öğeyi uygulama genelindeki gecikmesiz kipten çıkarır — bkz. <see cref="NativeDelayMs"/>.</summary>
    public static void UseNativeDelay(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ToolTipService.SetInitialShowDelay(element, NativeDelayMs);
    }

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
        // Devre dışı öğede de görünür: bakım kutusunun düğmeleri mid-run/mid-sync pasiftir ve NEDENİ ancak
        // tooltip'ten okunur; Build menüsünün Clean Solution maddesi de arka ucu yazılana dek böyle durur.
        ToolTipService.ShowOnDisabledProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
    }
}
