using System.Windows.Input;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.App;

/// <summary>
/// [design v1.23.0 §2.12 · plan U4 · motor · Task 11 · K6] <c>Restart to update</c>'in kabuktaki cevabı: kartı kapatır,
/// pencerenin en üst katmanındaki restart ekranını (<see cref="UpdateRestartScreen"/>) kurulu → teklif edilen sürümle
/// açar; ekranın çubuğu dolunca (<see cref="UpdateRestartScreen.BarFilled"/>) uygulamayı güvenli tam çıkış yoluna sokar
/// (<see cref="RequestFullExit"/> — × ve tepsi → Exit ile AYNI yol).
///
/// <para><b>Neden çıkış:</b> Windows çalışan programın dosyalarını değiştirmeye izin vermez — kurulumu pencere kapandıktan
/// sonra Update.exe penceresiz yapar ve yeni sürüm normal açılır. Ekran yalnız <c>Closing &lt;ürün&gt;…</c> der ve
/// pencere kapanana dek kalır. İsteğin kapısı (kilit + teklif) komutun kendisindedir
/// (<see cref="RunViewModel.RestartToUpdateRequested"/>); burada ikinci kez sorulmaz. Kilit uçuştaki işi zaten dışarıda
/// tutar; yine de iş varsa (ör. çubuk dolarken başlamış bir Sync) güvenli çıkış onu bekler
/// (<see cref="RequestFullExit"/>'in kendi kuralı).</para>
///
/// <para><b>Neden çubuk dolunca:</b> çıkış ekranla aynı turda istenseydi, iş yokken Shutdown dispatcher'da Normal
/// öncelikle koşar; ekranın görünür olmasının layout/render geçişi ise Render önceliğinde (daha düşük) bekler — pencere
/// ekran ilk kez çizilmeden kapanırdı. K6: ekran <c>Closing …</c> adımını gösterir, çubuk 800 ms'de dolar; çıkış o
/// zaman istenir.</para>
///
/// <para><b>Klavye:</b> ekran görünürken pencere klavyeyi ve global kısayolları yok sayar — prototipin
/// keydown'daki erken dönüşü. Karar tek yerdedir (<see cref="InputSuspended"/>); iki kapı onu okur: pencerenin tünelleyen
/// tuş olayı (<see cref="OnPreviewKeyDown"/> — handled bir olay pencerenin kısayol bağlamalarına ulaşmaz) ve global
/// kısayolun girişi (<see cref="OnGlobalHotkey"/>). Fare kendiliğinden yutulur: ekran dolu bir zeminle tüm pencereyi
/// örter.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Kullanıcı <c>Restart to update</c>'e bastı: kart kapanır, ekran <c>Closing…</c> ile açılır. Çıkış
    /// burada DEĞİL, çubuk dolunca istenir (<see cref="OnRestartScreenFilled"/>).</summary>
    private void OnRestartToUpdateRequested()
    {
        CloseUpdateCard(returnFocusToPill: true);
        if (_vm.AvailableUpdate is not { } offer) return;
        UpdateRestartOverlay.Play(AppIdentity.Version, offer.Version); // yalnız "Closing…" — kurulum pencere kapanınca (App.OnExit)
    }

    /// <summary>Restart ekranının çubuğu doldu: uygulama güvenli tam çıkış yoluna girer — × ile aynı yol.</summary>
    private void OnRestartScreenFilled() => RequestFullExit();

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
