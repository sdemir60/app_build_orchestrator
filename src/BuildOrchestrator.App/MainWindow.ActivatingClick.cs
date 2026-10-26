using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19] <b>Pencereyi arka plandan öne getiren tık.</b> Windows tıkı pencereyi etkinleştirdikten SONRA teslim
/// eder; dönüşün Sync'i burada istenirse tıkın indiği Build'i ya da Debug|Release'i altından kapatır ve tık yutulur. Basışı
/// <see cref="MouseActivateWndProc"/> not eder, hemen ardından gelen <c>Activated</c> tüketir (<see cref="TakeActivatingClick"/>) ve
/// <see cref="OnActivated"/> karar verir. Kablaj ctor'da (<c>Activated</c>) ve <c>OnSourceInitialized</c>'dadır (hook).
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
    /// [kullanıcı kararı 2026-10-19] Pencere öne geldi. Tık kendi tazelemesini yapan bir komuta iniyorsa (<see cref="StartsOwnRefreshAt"/>)
    /// dönüşün Sync'i istenmez ve tık normal çalışır; başka her dönüş (klavye, görev çubuğu, tepsi, başka bir yere tık) bugünkü gibi
    /// Sync'ler. internal: test yüzeyi — gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir.
    /// </summary>
    /// <param name="clickAt">Etkinleşmeyi getiren basışın köke (<see cref="RootShell"/>) göre konumu; bir basışla gelmediyse <c>null</c>.</param>
    /// <param name="button">Basışın tuşu.</param>
    internal void OnActivated(Point? clickAt, MouseButton button = MouseButton.Left) =>
        _vm.OnWindowActivated(clickRefreshes: clickAt is { } at && StartsOwnRefreshAt(at, button));

    /// <summary>Köke göre bu noktaya inen basış kendi tazelemesini yapan bir komutu mu çalıştırır. Pencere yalnız tek isabet testini yapar
    /// (<see cref="ActivatingClick.HitAt"/>) ve isabeti alt çubuğa sorar (<see cref="Views.ActionBar.StartsOwnRefresh"/>); yalnız sol basış
    /// sayılır.</summary>
    internal bool StartsOwnRefreshAt(Point atRoot, MouseButton button) =>
        button == MouseButton.Left && ActivatingClick.HitAt(RootShell, atRoot) is { } hit && Shell.ActionBarControl.StartsOwnRefresh(hit);
}
