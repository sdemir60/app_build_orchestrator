using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// "Kullanıcı GERÇEKTEN kaydırdı" sinyalinin TEK kablajı — proje listesi, konsol ve event stream AYNI jestleri
/// dinler (kopya YASAK, CLAUDE.md). Sinyal geometriden ÇIKARILMAZ, ham girdiden gelir; tüketicileri konsol ve
/// stream'de <see cref="BottomAnchorBehavior.NotifyUserScroll"/>, listede frontier follow'un kullanıcı duraklatmasıdır.
///
/// <para><b>Kaydırma çubuğu neden ayrı bir kanal:</b> tekerlek olayı yalnız tekerleği görür. Kullanıcı çubuğun
/// başlığını sürüklediğinde ya da oluğa tıkladığında hiç tekerlek olayı doğmaz — panel "kimse dokunmadı"
/// sanıp akan içerikle kullanıcıyı dibe çekerdi. <see cref="ScrollBar.ScrollEvent"/> YALNIZ kullanıcı
/// etkileşiminde ateşlenir; programatik <c>ScrollToVerticalOffset</c> onu yaymaz — aradığımız ayrım tam
/// budur.</para>
///
/// <para><b>[B2 fix 2 · madde 2] Jestin İLK işi uçuştaki kaydırma animasyonunu durdurmaktır</b>
/// (<see cref="ScrollAnimator.CancelForUser"/>) — "kullanıcının girdisi programatik hareketi durdurur" kuralının TEK tanımı, üç
/// panelde ve üç kanalda; tüketici jestin bir uçuşu durdurup durdurmadığını öğrenir (konsol ve stream'in atlama penceresi buna
/// bakar). [DEĞİŞEN KURAL] Eskiden iptal ayrı kablolardaydı: konsol ve stream'de yalnız tekerlek (<c>ScrollAnimator.EnableUserCancellation</c>)
/// ve konsolun tuş tablosu, listede bu sinyalin tüketicisi — çubuğu sürüklemek ve editörün kendi işlediği tuşlar (Ctrl+End, Shift'li
/// adımlar) <c>⌄ latest</c>'in animasyonunu durdurmuyor, sıradaki kareler görünümü jestin altından dibe sürmeye devam ediyordu; aynı
/// tekerlek çentiği de iki yoldan (kanca ve sinyal) geçiyordu.</para>
/// </summary>
internal static class UserScrollSignal
{
    /// <summary>Görünümü DİKEYDE kaydıran gezinme tuşları (değiştiricileri ne olursa olsun — editörün kendi işlediği Ctrl+Home/End ve
    /// Shift'li seçim adımları dahil) — AvalonEdit salt-okunurken de odak alır ve bunlarla kayar. ←/→ yataydır: ne dikey bir hareketi
    /// durdurur ne direksiyonu alır.</summary>
    private static bool IsScrollKey(Key key) =>
        key is Key.PageUp or Key.PageDown or Key.Up or Key.Down or Key.Home or Key.End;

    /// <summary>Üç girdi kanalını da <paramref name="host"/>'un ağacına bağlar (tünelleyen tekerlek/klavye olayları ve baloncuklanan
    /// çubuk olayı kökte yakalanır — iç kontrolün tipini bilmek gerekmez). Her jestte önce <paramref name="scrollTarget"/>'ın uçuştaki
    /// animasyonu durdurulur, sonra <paramref name="notify"/> çağrılır: argümanı jestin bir uçuşu durdurup durdurmadığıdır.</summary>
    public static void Wire(FrameworkElement host, UIElement scrollTarget, Action<bool> notify)
    {
        void Gesture() => notify(ScrollAnimator.CancelForUser(scrollTarget));
        host.PreviewMouseWheel += (_, _) => Gesture();
        host.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => Gesture()));
        host.PreviewKeyDown += (_, e) => { if (IsScrollKey(e.Key)) Gesture(); };
    }
}
