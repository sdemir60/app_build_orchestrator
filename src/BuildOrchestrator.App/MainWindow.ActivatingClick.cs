using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19 · 2026-10-26] <b>Pencereyi arka plandan öne getiren tık.</b> Windows tıkı pencereyi etkinleştirdikten SONRA
/// teslim eder; dönüşün Sync'i (son Sync'ten beş saniye geçtiyse) o anda istenirse, istendiği ANDA kapattığı kontrolü — koşu, bakım ve pull
/// komutları, Sync düğmesi, Debug|Release — tıkın altından kapatır ve tık yutulur. Basışı <see cref="MouseActivateWndProc"/> not eder, hemen
/// ardından gelen <c>Activated</c> tüketir (<see cref="TakeActivatingClick"/>) ve <see cref="OnActivated"/> karar verir. Kablaj ctor'da
/// (<c>Activated</c>) ve <c>OnSourceInitialized</c>'dadır (hook).
/// </summary>
public partial class MainWindow
{
    /// <summary>Pencereyi öne getiren basışın tuşu — <see cref="MouseActivateWndProc"/> kurar, hemen ardından gelen <c>Activated</c>
    /// tüketir (<see cref="TakeActivatingClick"/>); etkinleşme istemci alanına bir basışla gelmediyse <c>null</c>.</summary>
    private MouseButton? _activatingButton;

    /// <summary>[kullanıcı kararı 2026-10-19] <c>WM_MOUSEACTIVATE</c>: Windows bunu etkin olmayan pencereye fareyle basılınca,
    /// pencereyi etkinleştirmeden ÖNCE gönderir — sıra garanti, <c>Activated</c> ondan sonra gelir, tık en son. Basışın tuşu not edilir
    /// (<see cref="ActivatingClick.ButtonOf"/>); mesaj işlenmez, varsayılan cevap (etkinleştir ve tıkı teslim et) değişmez.</summary>
    private nint MouseActivateWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE) _activatingButton = ActivatingClick.ButtonOf(lParam);
        return 0;
    }

    /// <summary>Etkinleşmeyi getiren basışın köke (<see cref="RootShell"/>: title bar + gövde) göre konumu ve tuşu; etkinleşme bir basışla
    /// gelmediyse (klavye, görev çubuğu, tepsi, title bar zemini) konum <c>null</c>. İşareti tüketir.</summary>
    private (Point? At, MouseButton Button) TakeActivatingClick()
    {
        var button = _activatingButton;
        _activatingButton = null;
        return button is { } pressed ? (Mouse.GetPosition(RootShell), pressed) : (null, MouseButton.Left);
    }

    /// <summary>
    /// [kullanıcı kararı 2026-10-19 · 2026-10-26] Pencere öne geldi. Basış kendi tazelemesini yapan AÇIK bir kontrole iniyorsa
    /// (<see cref="StartsOwnRefreshAt"/>) dönüşün Sync'i istenmez ve basış normal çalışır; başka her dönüş (klavye, görev çubuğu, tepsi,
    /// başka bir yere basış) bugünkü gibi Sync'ler. Karar bir görünüm sorusudur: VM'e yalnız sonucu gider (<c>clickRefreshes</c>).
    /// internal: test yüzeyi — gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir.
    /// </summary>
    /// <param name="clickAt">Etkinleşmeyi getiren basışın köke (<see cref="RootShell"/>) göre konumu; bir basışla gelmediyse <c>null</c>.</param>
    /// <param name="button">Basışın tuşu.</param>
    internal void OnActivated(Point? clickAt, MouseButton button = MouseButton.Left) =>
        _vm.OnWindowActivated(clickRefreshes: clickAt is { } at && StartsOwnRefreshAt(at, button));

    /// <summary>
    /// Köke göre bu noktaya inen basış kendi tazelemesini yapan bir kontrolü mü çalıştırır. Pencere yalnız tek isabet testini yapar
    /// (<see cref="ActivatingClick.HitAt"/>), isabetin sahibini bulur ve ona sorar — hangi kontrollerin sayıldığını sahibi bilir:
    /// <list type="bullet">
    /// <item>isabet bir proje satırının içindeyse karar satırındır (<see cref="ProjectRow.StartsOwnRefresh"/>: play, ⋯ ve satırın
    /// neresine inerse insin SAĞ basış — yalnız satırın koşu kapısı açıkken);</item>
    /// <item>değilse yalnız sol basış sayılır: alt çubuk (<see cref="ActionBar.StartsOwnRefresh"/>: Build ve chevron'u, Sync, <c>N behind</c>,
    /// bakım kutusu, seçili olmayan Debug|Release) ve title bar'ın pencereye ait iki düğmesi — Exit (uçuşta iş yoksa hemen kapatır; dönüşün
    /// sessiz Sync'i iş sayılır ve onay kartını açtırırdı) ve × (pencere kapanıyor ya da tepsiye iniyor, Sync anlamsız); ikisi de yalnız
    /// uçuşta iş yokken (<see cref="ViewModels.RunViewModel.IsOperationInFlight"/> — koordinatörün tetiği beklettiği meşguliyet).</item>
    /// </list>
    /// Bir kontrol yalnız işi başlayabilecekken sayılır: iş sürerken ⋯ ve sağ basış yalnız pasif maddeli bir menü açar; Exit ve × ise onay kartı
    /// açar ya da pencereyi tepsiye indirir (<c>WindowCloseRule</c>) — hiçbiri tazeleyen bir iş başlatmaz. Sayılsalardı dönüşün Sync'i istenmez
    /// ve tetik de saklanmazdı; sayılmadıklarında tetik bugünkü gibi bekler ve iş bitince değerlendirilir. Branch chip'i bilerek yoktur
    /// (kullanıcı kararı 2026-10-26): tık branch'i doğrudan değiştirmez, kullanıcı seçmeyebilir. Sağ basış yalnız satırda sayılır; başka her
    /// yerde dönüş bugünkü gibi Sync'ler.
    /// </summary>
    /// <param name="atRoot">Köke (<see cref="RootShell"/>) göre konum.</param>
    /// <param name="button">Basışın tuşu.</param>
    internal bool StartsOwnRefreshAt(Point atRoot, MouseButton button)
    {
        if (ActivatingClick.HitAt(RootShell, atRoot) is not { } hit) return false;
        for (DependencyObject? node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ProjectRow row) return row.StartsOwnRefresh(hit, button);
        return button == MouseButton.Left
            && (Shell.ActionBarControl.StartsOwnRefresh(hit)
                || (!_vm.IsOperationInFlight && (ExitButton.IsAncestorOf(hit) || CloseButton.IsAncestorOf(hit))));
    }
}
