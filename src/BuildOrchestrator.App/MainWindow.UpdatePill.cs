using System.Windows;
using System.Windows.Automation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App;

/// <summary>
/// [design v1.23.0 §2.1 · §2.12] Title bar'daki <b>güncelleme hapının</b> kablajı: görünürlük, sürüm metni, UIA adı ve
/// giriş animasyonu teklifin tek yerinden (<see cref="RunViewModel.AvailableUpdate"/>) sürülür.
///
/// <para>Güncelleme motoru henüz YOK: VM açılışta örnek teklifi taşır ve hap İLK karede görünür — giriş animasyonu
/// oynamaz. Giriş yalnız teklif sonradan gelirse (null → teklif) BİR KEZ oynar; bu, motorun yazılınca kullanacağı
/// dikiştir. Hap görünürken gelen yeni bir teklif yalnız metni günceller.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Ctor'dan bir kez: ilk durumu animasyonsuz uygular, sonraki teklif değişimlerini dinler.</summary>
    private void SetupUpdatePill()
    {
        ApplyUpdateOffer(entrance: false);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.AvailableUpdate)) ApplyUpdateOffer(entrance: true);
        };
    }

    /// <summary>Teklifi hapa yazar. Teklif yoksa hap (ve açıksa kartı) kalkar; <paramref name="entrance"/> ve hap o ana
    /// dek gizliyse giriş oynar (reduced-motion'da <see cref="PopIn"/> son duruma atlar).</summary>
    private void ApplyUpdateOffer(bool entrance)
    {
        var offer = _vm.AvailableUpdate;
        bool wasShown = UpdatePillSlot.Visibility == Visibility.Visible;
        if (offer is null)
        {
            UpdatePill.IsChecked = false;
            UpdatePillSlot.Visibility = Visibility.Collapsed;
            return;
        }

        UpdatePillVersion.Text = offer.Version;
        AutomationProperties.SetName(UpdatePill, AccessibilityNames.UpdateTo(offer.Version));
        UpdatePillSlot.Visibility = Visibility.Visible;
        if (entrance && !wasShown) PopIn.PlayEntrance(UpdatePillSlot);
    }
}
