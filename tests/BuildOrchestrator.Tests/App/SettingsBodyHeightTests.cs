using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.14.0 §2.9 · ruling task-D6] <see cref="SettingsBodyHeight"/>'ın SAF (WPF'siz) hesabı: Settings
/// gövdesinin üst yükseklik sınırı <c>min(pencere yüksekliği × 0.56, 460)</c>'tır. Alt taban (300px) bu
/// sınıfın işi DEĞİLDİR (bkz. <see cref="SettingsBodyHeight"/> sınıf özeti) — burada yalnız üst sınırın
/// formülü pinlenir; gerçek pencereye kablajın kanıtı <c>SettingsDialogFocusTests</c>'tedir (realize testleri).
/// </summary>
public class SettingsBodyHeightTests
{
    [Fact] // Büyük pencere: oran (56%) 460'ı aşar → tavan (460) kazanır.
    public void A_tall_window_caps_at_460()
    {
        Assert.Equal(460.0, SettingsBodyHeight.MaxHeightFor(1200));
    }

    /// <summary>[review fix-1] Taban SABİTİNİN kendisi — tavanın <c>A_tall_window_caps_at_460</c>'ta SERT
    /// literal (460.0) ile pinlendiği AYNI desen. <c>A_small_window_yields_a_result_below_the_floor_by_design</c>
    /// yalnız "sonuç &lt; MinFloor" diyordu — bu, MinFloor'un KENDİSİ 300'ün dışına kayarsa (250'ye, 350'ye)
    /// SESSİZCE geçen totolojik bir iddiaydı. Burada değer DOĞRUDAN 300'e karşı ölçülür.</summary>
    [Fact]
    public void The_floor_is_300_pixels()
    {
        Assert.Equal(300.0, SettingsBodyHeight.MinFloor);
    }

    [Fact] // Orta pencere: oran tavanın ALTINDA kalır → oranın kendisi kazanır (56%'nin gerçekten UYGULANDIĞININ kanıtı).
    public void A_mid_size_window_follows_56_percent_of_its_height()
    {
        Assert.Equal(392.0, SettingsBodyHeight.MaxHeightFor(700), precision: 6); // 700 × 0.56 = 392
    }

    [Fact] // Küçük pencere: oran tabanın (300) ALTINA iner — bu METODUN işi DEĞİL, taban ScrollViewer.MinHeight'ta
           // AYRI uygulanır (bkz. SettingsBodyHeight sınıf özeti). Metot burada BİLEREK 300'ün altını döner.
    public void A_small_window_yields_a_result_below_the_floor_by_design()
    {
        double result = SettingsBodyHeight.MaxHeightFor(400); // 400 × 0.56 = 224
        Assert.Equal(224.0, result, precision: 6);
        Assert.True(result < SettingsBodyHeight.MinFloor, "taban burada uygulanmamalı — WPF MinHeight'ın işi");
    }

    [Fact] // Diz noktası: oran tam tavana denk geldiğinde (460 / 0.56) sonuç hâlâ 460 — sınırda taşma/eksik yok.
    public void The_knee_point_lands_exactly_on_the_cap()
    {
        double windowHeight = SettingsBodyHeight.MaxCap / SettingsBodyHeight.WindowHeightFraction;
        Assert.Equal(460.0, SettingsBodyHeight.MaxHeightFor(windowHeight), precision: 6);
    }

    [Fact] // Sıfır/negatif pencere yüksekliği (henüz yerleşmemiş pencere) çökmez — küçük/sıfır bir sonuç döner,
           // tabanı UYGULAMAK yine çağıranın MinHeight'ının işidir.
    public void A_zero_window_height_does_not_throw_and_yields_a_non_positive_result()
    {
        Assert.Equal(0.0, SettingsBodyHeight.MaxHeightFor(0));
    }
}
