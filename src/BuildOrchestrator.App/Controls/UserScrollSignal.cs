using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// "Kullanıcı GERÇEKTEN kaydırdı" sinyalinin TEK kablajı — konsol ve event stream AYNI jestleri dinler
/// (kopya YASAK, CLAUDE.md). Sinyal geometriden ÇIKARILMAZ, ham girdiden gelir; tüketicisi
/// <see cref="BottomAnchorBehavior.NotifyUserScroll"/>'dur.
///
/// <para><b>Kaydırma çubuğu neden ayrı bir kanal:</b> tekerlek olayı yalnız tekerleği görür. Kullanıcı çubuğun
/// başlığını sürüklediğinde ya da oluğa tıkladığında hiç tekerlek olayı doğmaz — panel "kimse dokunmadı"
/// sanıp akan içerikle kullanıcıyı dibe çekerdi. <see cref="ScrollBar.ScrollEvent"/> YALNIZ kullanıcı
/// etkileşiminde ateşlenir; programatik <c>ScrollToVerticalOffset</c> onu yaymaz — aradığımız ayrım tam
/// budur.</para>
/// </summary>
internal static class UserScrollSignal
{
    /// <summary>Görünümü kaydıran gezinme tuşları — AvalonEdit salt-okunurken de odak alır ve bunlarla kayar.</summary>
    private static bool IsScrollKey(Key key) =>
        key is Key.PageUp or Key.PageDown or Key.Up or Key.Down or Key.Home or Key.End;

    /// <summary>Üç girdi kanalını da <paramref name="host"/>'un ağacına bağlar (tünelleyen tekerlek/klavye
    /// olayları ve baloncuklanan çubuk olayı kökte yakalanır — iç kontrolün tipini bilmek gerekmez).</summary>
    public static void Wire(FrameworkElement host, Action notify)
    {
        host.PreviewMouseWheel += (_, _) => notify();
        host.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => notify()));
        host.PreviewKeyDown += (_, e) => { if (IsScrollKey(e.Key)) notify(); };
    }
}
