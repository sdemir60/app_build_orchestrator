using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// "Kullanıcı GERÇEKTEN kaydırdı" sinyalinin TEK kablajı — proje listesi, konsol ve event stream AYNI jestleri
/// dinler (kopya YASAK, CLAUDE.md). Sinyal geometriden ÇIKARILMAZ, ham girdiden gelir; tüketicileri konsol ve
/// stream'de <see cref="BottomAnchorBehavior.NotifyUserScroll"/>, listede frontier follow'un kullanıcı duraklatmasıdır.
///
/// <para><b>[B2 fix 3 · madde 2] Kural tek cümledir:</b> DİKEY kullanıcı jestleri (tekerlek, dikey kaydırma çubuğu, dikey kaydırma tuşları)
/// direksiyonu alır ve uçuştaki kaydırma animasyonunu durdurur; YANA jestler (←/→, Ctrl+←/→, yatay tekerlek, yatay kaydırma çubuğu) ikisini de
/// yapmaz — yatay okuma konumu dikey takipten ve atlamadan bağımsızdır. [DEĞİŞEN KURAL] Çubuk kanalı eskiden yönü ayırmıyordu: yatay çubuğu
/// sürüklemek direksiyonu alıyor (takip beş saniye duruyordu) ve dikey pill animasyonunu durduruyordu.</para>
///
/// <para><b>[son temizlik · madde 1] Yana jest yine de bir jesttir:</b> tüketiciye ayrı kanaldan bildirilir (<c>sideways</c>) — konsolda,
/// direksiyon ZATEN okuyucudaysa bekleme dönüşünü baştan kurar (<see cref="BottomAnchorBehavior.NotifySidewaysScroll"/>; koşul anchor'dadır).
/// Üç yana kanal buradadır: ←/→ tuşları (değiştiricileri ne olursa olsun), yatay çubuğun olayı ve yatay tekerlek — WPF onu dağıtmaz,
/// uygulamanın kancası kaydırdıktan sonra panelden yükseltir (<see cref="HorizontalWheelScroll.ScrolledEvent"/>). [DEĞİŞEN KURAL] Eskiden
/// yana jest bu kabloya hiç ulaşmıyordu (yatay çubuk süzülüyor, ←/→ ve yatay tekerlek hiç gelmiyordu): yukarıda okuyup uzun bir satırı
/// yana kaydıran okuyucu, son dikey jestinden beş saniye sonra dibe çekiliyordu.</para>
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

    /// <summary>[son temizlik · madde 1] Görünümü YANA kaydıran tuşlar (değiştiricileri ne olursa olsun) — yana jest (sınıf doc'u).</summary>
    private static bool IsSidewaysKey(Key key) => key is Key.Left or Key.Right;

    /// <summary>Çubuk olayı DİKEY bir çubuktan mı geldi — yatay çubuk yana bir jesttir (sınıf doc'u). Olay çubuğun kendisinden baloncuklanır.</summary>
    private static bool IsVerticalBar(object source) => source is ScrollBar { Orientation: Orientation.Vertical };

    /// <summary>Girdi kanallarını <paramref name="host"/>'un ağacına bağlar (tünelleyen tekerlek/klavye olayları ve baloncuklanan
    /// çubuk ve yatay tekerlek olayları kökte yakalanır — iç kontrolün tipini bilmek gerekmez). Her dikey jestte önce
    /// <paramref name="scrollTarget"/>'ın uçuştaki animasyonu durdurulur, sonra <paramref name="notify"/> çağrılır: argümanı jestin bir uçuşu
    /// durdurup durdurmadığıdır. Yana jest animasyona dokunmaz, yalnız <paramref name="sideways"/>'i çağırır (verilmezse hiçbir şey).</summary>
    public static void Wire(FrameworkElement host, UIElement scrollTarget, Action<bool> notify, Action? sideways = null)
    {
        void Gesture() => notify(ScrollAnimator.CancelForUser(scrollTarget));
        void Sideways() => sideways?.Invoke();
        host.PreviewMouseWheel += (_, _) => Gesture();
        host.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, e) =>
        {
            if (IsVerticalBar(e.OriginalSource)) Gesture();
            else Sideways();
        }));
        host.PreviewKeyDown += (_, e) =>
        {
            if (IsScrollKey(e.Key)) Gesture();
            else if (IsSidewaysKey(e.Key)) Sideways();
        };
        host.AddHandler(HorizontalWheelScroll.ScrolledEvent, new RoutedEventHandler((_, _) => Sideways()));
    }
}
