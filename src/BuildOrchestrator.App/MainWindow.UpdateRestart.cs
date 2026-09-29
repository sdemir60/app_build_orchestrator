using System.Windows.Input;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.App;

/// <summary>
/// [design v1.23.0 §2.12 · plan U4] <c>Restart to update</c>'in kabuktaki cevabı: kartı kapatır ve pencerenin en üst
/// katmanındaki restart ekranını (<see cref="UpdateRestartScreen"/>) kurulu → teklif edilen sürümle oynatır.
///
/// <para><b>Güncelleme motoru henüz YOK.</b> Ekran tasarımın önizlemesidir: oynar, söner ve uygulama AYNEN önceki gibi
/// kalır — Sync yok, sıfırlama yok, seçim ve hap yerinde. İsteğin kapısı (kilit + teklif) komutun kendisindedir
/// (<see cref="RunViewModel.RestartToUpdateRequested"/>); burada ikinci kez sorulmaz.</para>
///
/// <para><b>Klavye:</b> ekran görünürken (sönüş dahil) pencere klavyeyi ve global kısayolları yok sayar — prototipin
/// keydown'daki erken dönüşü. Karar tek yerdedir (<see cref="InputSuspended"/>); iki kapı onu okur: pencerenin tünelleyen
/// tuş olayı (<see cref="OnPreviewKeyDown"/> — handled bir olay pencerenin kısayol bağlamalarına ulaşmaz) ve global
/// kısayolun girişi (<see cref="OnGlobalHotkey"/>). Fare kendiliğinden yutulur: ekran dolu bir zeminle tüm pencereyi
/// örter.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Kullanıcı <c>Restart to update</c>'e bastı: kart kapanır (odak hapa döner — ekran kalkınca kullanıcı
    /// kaldığı yerdedir) ve ekran oynar.</summary>
    private void OnRestartToUpdateRequested()
    {
        CloseUpdateCard(returnFocusToPill: true);
        if (_vm.AvailableUpdate is { } offer) UpdateRestartOverlay.Play(AppIdentity.Version, offer.Version);
    }

    /// <summary>Pencerenin klavyesi ve global kısayolları şu an askıda mı — restart ekranı görünürken.</summary>
    private bool InputSuspended => UpdateRestartOverlay.IsShowing;

    /// <summary>Restart ekranı görünürken her tuş burada, pencerenin en dışında tüketilir.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (InputSuspended)
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
