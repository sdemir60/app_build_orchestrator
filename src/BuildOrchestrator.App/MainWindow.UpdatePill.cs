using System.Windows;
using System.Windows.Automation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App;

/// <summary>
/// [design v1.23.0 §2.1 · §2.12] Title bar'daki <b>güncelleme hapının</b> ve kartının kablajı: görünürlük, sürüm
/// metni, UIA adı ve giriş animasyonu teklifin tek yerinden (<see cref="RunViewModel.AvailableUpdate"/>) sürülür; kart
/// mevcut popover altyapısıdır (hapın <c>IsChecked</c>'ı ↔ <c>UpdatePopup.IsOpen</c>).
///
/// <para>Güncelleme motoru henüz YOK: VM açılışta örnek teklifi taşır ve hap İLK karede görünür — giriş animasyonu
/// oynamaz. Giriş yalnız teklif sonradan gelirse (null → teklif) BİR KEZ oynar; bu, motorun yazılınca kullanacağı
/// dikiştir. Hap görünürken gelen yeni bir teklif yalnız metni günceller.</para>
///
/// <para><b>Kartın kapanış yolları:</b> <c>Later</c> ve kartın içindeki Esc (<see cref="Views.PopoverBase.CloseRequested"/>
/// → odak hapa döner), hapa ikinci basış (<see cref="PopoverToggle"/>), dışarı tık (<c>StaysOpen=False</c>), pencerenin
/// Esc zincirinin popover katmanı (<see cref="CloseAllPopovers"/>), bir dialogun açılışı (<see cref="ModalDialog.Opened"/>
/// — plan U2) ve Restart isteği.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>[design v1.23.0 §9] Kartın üst kenarı ile hapın alt kenarı arasındaki boşluk (<c>top: calc(100% + 9px)</c>)
    /// — tek yer; kart bunun kadar aşağıda, sol kenarı hapın sol kenarında durur (<see cref="PopoverPlacement"/>).</summary>
    internal const double UpdateCardGap = 9;

    /// <summary>Ctor'dan bir kez: kartı bağlar, ilk durumu animasyonsuz uygular, sonraki teklif değişimlerini dinler.</summary>
    private void SetupUpdatePill()
    {
        // Popup içerikleri (görsel ağaç dışı) DataContext'i güvenilir MİRAS ALMAZ → açıkça bağla (ActionBar deseni).
        UpdateCardView.DataContext = _vm;
        // Kart hapın altında, sol kenarı hapla hizalı — her makinede (Placement=Custom; Bottom el tercihine uyar).
        UpdatePopup.CustomPopupPlacementCallback = PopoverPlacement.BelowLeftEdgeCallback(UpdateCardGap);
        // Açık kartın hapına basmak onu KAPATIR (prototip: setUpdPop(v => !v)); kapı tek yerde.
        PopoverToggle.Bind(UpdatePill, UpdatePopup);
        UpdateCardView.CloseRequested += () => CloseUpdateCard(returnFocusToPill: true);
        foreach (var dialog in new ModalDialog[] { SettingsOverlay, AboutOverlay, NotesOverlay })
            dialog.Opened += (_, _) => CloseUpdateCard(returnFocusToPill: false);
        // Restart isteği kartı kapatır ve restart ekranını oynatır (MainWindow.UpdateRestart.cs).
        _vm.RestartToUpdateRequested += (_, _) => OnRestartToUpdateRequested();

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
            CloseUpdateCard(returnFocusToPill: false);
            UpdatePillSlot.Visibility = Visibility.Collapsed;
            return;
        }

        UpdatePillVersion.Text = offer.Version;
        AutomationProperties.SetName(UpdatePill, AccessibilityNames.UpdateTo(offer.Version));
        UpdatePillSlot.Visibility = Visibility.Visible;
        if (entrance && !wasShown) PopIn.PlayEntrance(UpdatePillSlot);
    }

    /// <summary>Güncelleme kartı açık mı — Esc zincirinin popover katmanının bir parçası (<see cref="AnyPopoverOpen"/>).</summary>
    private bool IsUpdateCardOpen => UpdatePill.IsChecked == true;

    /// <summary>Kartı kapatır (açık değilse hiçbir şey yapmaz). Kullanıcının kartta başlattığı kapanışta odak hapa döner
    /// (popover deseni: return-to-trigger); bir dialog açılırken odak dialoga aittir, dokunulmaz.</summary>
    private void CloseUpdateCard(bool returnFocusToPill)
    {
        if (!IsUpdateCardOpen) return;
        UpdatePill.IsChecked = false;
        if (returnFocusToPill) UpdatePill.Focus();
    }
}
