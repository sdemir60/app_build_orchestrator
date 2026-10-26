using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-26] Pencereyi arka plandan öne getiren basışın iki saf sorusu: nasıl bir basıştı
/// (<see cref="ActivatingClick.IsClientLeftPress"/>: <c>WM_MOUSEACTIVATE</c>'in <c>lParam</c>'ı — düşük söz isabet kodu, yüksek söz basışın
/// fare mesajı) ve neye indi (<see cref="ActivatingClick.HitAt"/>). Yalnız istemci alanına SOL basış bir basıştır. Title bar zemini
/// (<see cref="Win32.HTCAPTION"/>), orta tuş ve sağ tuş değildir: dönüş bugünkü gibi Sync'ler.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public sealed class ActivatingClickTests
{
    /// <summary>Orta tuş basıldı — üretimin tanımadığı örnek, yalnız burada gerekir.</summary>
    private const int WM_MBUTTONDOWN = 0x0207;

    /// <summary>Sağ tuş basıldı — üretim artık tanımaz (sağ basış hiçbir yerde sayılmaz), yalnız burada gerekir.</summary>
    internal const int WM_RBUTTONDOWN = 0x0204;

    /// <summary><c>WM_MOUSEACTIVATE</c>'in <c>lParam</c>'ı: yüksek söz fare mesajı, düşük söz isabet kodu.</summary>
    internal static nint LParam(int hitTest, int mouseMessage) => (nint)((mouseMessage << 16) | hitTest);

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: istemci alanına SAĞ basış da bir tuş verir (satırın menüsünü açar
    /// ve satırda dönüşün Sync'ini atlardı). Değişme gerekçesi: satırın menüsünün maddeleri tek proje koşusudur ve o yalnız hedefi yeniden
    /// kararlaştırır, ekranın tamamını tazelemez; sağ basış hiçbir yerde sayılmaz — dönüş bugünkü gibi Sync'ler.
    /// </summary>
    [Theory]
    [InlineData(Win32.HTCLIENT, Win32.WM_LBUTTONDOWN, true)]
    [InlineData(Win32.HTCLIENT, WM_RBUTTONDOWN, false)]
    [InlineData(Win32.HTCAPTION, Win32.WM_LBUTTONDOWN, false)]
    [InlineData(Win32.HTCLIENT, WM_MBUTTONDOWN, false)]
    public void Only_a_left_press_on_the_client_area_is_an_activating_press(int hitTest, int mouseMessage, bool expected) =>
        Assert.Equal(expected, ActivatingClick.IsClientLeftPress(LParam(hitTest, mouseMessage)));

    /// <summary>
    /// [son inceleme M1] <c>InputHitTest</c> bir <c>TextBlock</c>'un içindeki <c>Run</c> gibi bir <see cref="ContentElement"/> verebilir —
    /// <c>Visual</c> değildir. <see cref="ActivatingClick.HitAt"/> onu mantıksal ebeveyninin görseline (etiketin kendisine) çevirir; çevirmeseydi
    /// sahipler (<c>IsAncestorOf</c>) etiketin içindeki isabeti göremezdi. Dal bir etikete <c>Run</c> girdiği gün devreye girer, bu test
    /// onu bugünden pinler.
    /// </summary>
    [StaFact]
    public void A_hit_on_a_run_inside_a_label_resolves_to_the_label()
    {
        var label = new TextBlock { FontSize = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        label.Inlines.Add(new Run("Build"));
        var window = DsResources.Realize(DsResources.NewHost(), label);
        var middle = new Point(label.ActualWidth / 2, label.ActualHeight / 2);

        Assert.IsAssignableFrom<ContentElement>(label.InputHitTest(middle));   // ön-koşul: ham isabet Run'dır, Visual değil
        Assert.Same(label, ActivatingClick.HitAt(label, middle));
        GC.KeepAlive(window);
    }
}
