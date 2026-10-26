using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19 · 2026-10-26] <b>Pencereyi arka plandan öne getiren tık.</b> Windows tıkı pencereyi etkinleştirdikten SONRA
/// teslim eder; dönüşün Sync'i (son Sync'ten beş saniye geçtiyse) o anda istenirse, istendiği ANDA kapattığı kontrolü tıkın altından kapatır
/// ve tık yutulur. Bu yüzden basış, işi bitince ekranın TAMAMINI tazeleyen bir kontrole iniyorsa dönüşün Sync'i istenmez ve basış normal
/// çalışır; başka her dönüş (klavye, görev çubuğu, tepsi, başka bir yere ya da sağ basış) bugünkü gibi Sync'ler. Hangi kontrollerin
/// sayıldığını SAHİPLERİ söyler — alt çubuk (<see cref="ActionBar.StartsOwnRefresh"/>), bakım kutusu (<see cref="MaintenanceBox.StartsOwnRefresh"/>)
/// ve title bar'ın pencereye ait iki düğmesi (<see cref="StartsOwnRefreshAt"/>); liste burada tekrarlanmaz. Basışı
/// <see cref="MouseActivateWndProc"/> not eder, hemen ardından gelen <c>Activated</c> tüketir (<see cref="TakeActivatingClick"/>) ve
/// <see cref="OnActivated"/> karar verir. Kablaj ctor'da (<c>Activated</c>) ve <c>OnSourceInitialized</c>'dadır (hook).
/// </summary>
public partial class MainWindow
{
    /// <summary>Pencereyi öne getiren basış istemci alanına bir SOL basıştı — <see cref="MouseActivateWndProc"/> kurar, hemen ardından gelen
    /// <c>Activated</c> tüketir (<see cref="TakeActivatingClick"/>).</summary>
    private bool _activatedByClick;

    /// <summary>[kullanıcı kararı 2026-10-19] <c>WM_MOUSEACTIVATE</c>: Windows bunu etkin olmayan pencereye fareyle basılınca, pencereyi
    /// etkinleştirmeden ÖNCE gönderir — sıra garanti, <c>Activated</c> ondan sonra gelir, tık en son. Basışın istemci alanına bir sol basış
    /// olup olmadığı not edilir (<see cref="ActivatingClick.IsClientLeftPress"/>); mesaj işlenmez, varsayılan cevap (etkinleştir ve tıkı
    /// teslim et) değişmez.</summary>
    private nint MouseActivateWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE) _activatedByClick = ActivatingClick.IsClientLeftPress(lParam);
        return 0;
    }

    /// <summary>Etkinleşmeyi getiren sol basışın köke (<see cref="RootShell"/>: title bar + gövde) göre konumu; etkinleşme bir sol basışla
    /// gelmediyse (klavye, görev çubuğu, tepsi, title bar zemini, sağ tuş) <c>null</c>. İşareti tüketir.</summary>
    private Point? TakeActivatingClick()
    {
        bool byClick = _activatedByClick;
        _activatedByClick = false;
        return byClick ? Mouse.GetPosition(RootShell) : null;
    }

    /// <summary>
    /// [kullanıcı kararı 2026-10-19 · 2026-10-26] Pencere öne geldi. Basış, işi bitince ekranın TAMAMINI tazeleyen AÇIK bir kontrole iniyorsa
    /// (<see cref="StartsOwnRefreshAt"/>) dönüşün Sync'i istenmez ve basış normal çalışır; başka her dönüş bugünkü gibi Sync'ler. Karar bir
    /// görünüm sorusudur: VM'e yalnız sonucu gider (<c>clickRefreshes</c>).
    /// internal: test yüzeyi — gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir.
    /// </summary>
    /// <param name="clickAt">Etkinleşmeyi getiren sol basışın köke (<see cref="RootShell"/>) göre konumu; bir sol basışla gelmediyse <c>null</c>.</param>
    internal void OnActivated(Point? clickAt) =>
        _vm.OnWindowActivated(clickRefreshes: clickAt is { } at && StartsOwnRefreshAt(at));

    /// <summary>
    /// Köke göre bu noktaya inen basış, işi bitince ekranın TAMAMINI tazeleyen bir kontrolü mü çalıştırır. Pencere yalnız tek isabet testini
    /// yapar (<see cref="ActivatingClick.HitAt"/>) ve isabetin sahibine sorar: alt çubuğa (<see cref="ActionBar.StartsOwnRefresh"/>; bakım
    /// kutusunu o <see cref="MaintenanceBox.StartsOwnRefresh"/>'e sorar). Title bar'ın iki düğmesi pencerenindir: Exit (uçuşta iş yoksa hemen
    /// kapatır; dönüşün sessiz Sync'i iş sayılır ve onay kartını açtırırdı) ve × (pencere kapanıyor ya da tepsiye iniyor, Sync anlamsız).
    /// <para>İkisi de yalnız uçuşta iş yokken sayılır (<see cref="ViewModels.RunViewModel.IsOperationInFlight"/> — koordinatörün tetiği
    /// beklettiği meşguliyet): iş sürerken Exit onay kartı açar, × pencereyi tepsiye indirir (<c>WindowCloseRule</c>) — hiçbiri tazeleyen bir iş
    /// başlatmaz. Sayılsalardı dönüşün Sync'i istenmez ve tetik de saklanmazdı; sayılmadıklarında tetik bugünkü gibi bekler ve iş bitince
    /// değerlendirilir.</para>
    /// <para>Proje satırı hiç sorulmaz (play, ⋯ ve sağ basış dahil): satırdan başlayan tek proje koşusu yalnız hedefi yeniden kararlaştırır
    /// (<c>ProjectRunScope</c>), öbür satırların bilgisi eski kalır. Satırda dönüş bugünkü gibi Sync'ler: play tıkın altından kapanır, ⋯'nin
    /// menüsü pasif maddeli açılır.</para>
    /// </summary>
    /// <param name="atRoot">Köke (<see cref="RootShell"/>) göre konum.</param>
    internal bool StartsOwnRefreshAt(Point atRoot) =>
        ActivatingClick.HitAt(RootShell, atRoot) is { } hit
        && (Shell.ActionBarControl.StartsOwnRefresh(hit)
            || (!_vm.IsOperationInFlight && (ExitButton.IsAncestorOf(hit) || CloseButton.IsAncestorOf(hit))));
}
