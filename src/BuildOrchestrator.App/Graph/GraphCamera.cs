using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>[T63] Kameranın uygulanacak hâli: ölçek + (piksele yuvarlanmış) öteleme.</summary>
public readonly record struct CameraTransform(double Scale, double Tx, double Ty);

/// <summary>
/// [quiet] design v1.3.0 §2.3 kamerasının SAF aritmetiği — prototype/app/BuildApp.jsx <c>GraphPanel</c>
/// portu (satır 322-356, 398-399).
///
/// <para><b>Kamera KENDİLİĞİNDEN hareket etmez.</b> Yalnız iki hedefi vardır: <see cref="Default"/>
/// (seçim yok — graf zaten panele tam sığar, çünkü yerleşimin kendisi panele göre hesaplanır) ve
/// <see cref="FocusAndFit"/> (seçim var — seçili düğüm + doğrudan komşuları panele sığdırılır). Koşu
/// sırasında kamera durur; kullanıcı nereye baktıysa orada kalır.</para>
///
/// <para><b>Öteleme kelepçesi YOKTUR ve olamaz.</b> Dünya tuvali panelin KENDİSİDİR; "sığan eksen
/// ortalanır" biçiminde bir kelepçe, ölçek 1'in altındaki her seçimde (geniş odak kümesi) ötelemeyi grafın
/// merkezine zorlar ve <see cref="FocusAndFit"/>'i tamamen ezerdi. Tasarımın kurtarma yolu kelepçe değil
/// jesttir: boş alana tıkla → varsayılan görünüm (§2.3).</para>
/// </summary>
public static class GraphCamera
{
    /// <summary>Seçim yokkenki ölçek — yerleşim zaten panele sığdığı için 1'dir (JSX:294).</summary>
    public const double DefaultScale = 1.0;

    /// <summary>Odakla-sığdır ölçek kelepçesi (§2.3: "zoom = min(W/bw, H/bh), 0.7–2.6 kelepçe").</summary>
    public const double SelectionMinScale = 0.7;
    public const double SelectionMaxScale = 2.6;
    /// <summary>Sığdırma payının sabit bileşeni (§2.3: "padding = 3×node + 48px").</summary>
    public const double SelectionPaddingPx = 48.0;
    /// <summary>Sığdırma payının düğüm boyutuna bağlı bileşeni.</summary>
    public const double SelectionPaddingNodeFactor = 3.0;

    /// <summary>Wheel bandı (§2.3: "Wheel = zoom 0.7–5.0").</summary>
    public const double ManualMinScale = 0.7;
    public const double ManualMaxScale = 5.0;
    /// <summary>Wheel kademesi, çarpansal (§2.3: "çarpan 1.14/adım").</summary>
    public const double WheelZoomStep = 1.14;

    /// <summary>Kamera hedefine kayış süresi (§2.3: "kamera 460ms ease-in-out kayar").</summary>
    public const double TransitionMs = 460.0;
    /// <summary>Wheel'in kendi kısa geçişi (§2.3: "160ms ease-out").</summary>
    public const double WheelTransitionMs = 160.0;

    /// <summary>Seçim yokkenki görünüm: ölçek 1, öteleme 0 (JSX:355 <c>{z:1, x:0, y:0}</c>).</summary>
    public static CameraTransform Default => new(DefaultScale, 0, 0);

    /// <summary>
    /// §2.3 "odakla & sığdır": odak kümesinin MERKEZLERİNİN sınır kutusu panele sığdırılır ve merkez
    /// ortalanır (JSX:352-354).
    /// </summary>
    /// <param name="panel">Panelin (kırpma alanının) ölçüsü.</param>
    /// <param name="centreBounds">Odak kümesindeki düğüm MERKEZLERİNİN sınır kutusu, içerik koordinatında.</param>
    /// <param name="nodeSize">Canlı düğüm kenarı — pay onun katıyla büyür, çünkü kutu merkezlerden kuruludur
    /// ve düğümlerin kendi genişliği payla karşılanır.</param>
    /// <param name="worldOffset">İçerik → dünya ötelemesi (<see cref="QuietGraphLayout.ContentInset"/>).</param>
    public static CameraTransform FocusAndFit(Size panel, Rect centreBounds, double nodeSize, Vector worldOffset)
    {
        double padding = nodeSize * SelectionPaddingNodeFactor + SelectionPaddingPx;
        double scale = Math.Clamp(
            Math.Min(
                panel.Width / (centreBounds.Width + padding),
                panel.Height / (centreBounds.Height + padding)),
            SelectionMinScale, SelectionMaxScale);

        double centreX = centreBounds.X + centreBounds.Width / 2 + worldOffset.X;
        double centreY = centreBounds.Y + centreBounds.Height / 2 + worldOffset.Y;
        return new CameraTransform(
            scale,
            RoundPixels(panel.Width / 2 - centreX * scale),
            RoundPixels(panel.Height / 2 - centreY * scale));
    }

    /// <summary>Manuel pan: <paramref name="delta"/> EKRAN pikselidir (öteleme de ekran uzayındadır,
    /// dolayısıyla ölçeğe bölünmez). Sonuç YUVARLANMAZ — sürükleme ara kareleridir ve her karede yuvarlamak
    /// eli takip eden grafı titretir.</summary>
    public static CameraTransform Pan(CameraTransform camera, Vector delta) =>
        camera with { Tx = camera.Tx + delta.X, Ty = camera.Ty + delta.Y };

    /// <summary>İmleç merkezli zoom: imlecin ALTINDAKİ dünya noktası sabit kalır — dünya noktası
    /// <c>w = (cursor − t) / s</c>, yeni öteleme <c>t' = cursor − w·s'</c>. Ölçek manuel banda kıstırılır;
    /// uçlar yuvarlanır (bu bir ANİMASYON hedefidir, ara kare değil).</summary>
    public static CameraTransform ZoomAt(CameraTransform camera, Point cursor, double factor)
    {
        double scale = Math.Clamp(camera.Scale * factor, ManualMinScale, ManualMaxScale);
        double worldX = (cursor.X - camera.Tx) / camera.Scale;
        double worldY = (cursor.Y - camera.Ty) / camera.Scale;
        return new CameraTransform(
            scale,
            RoundPixels(cursor.X - worldX * scale),
            RoundPixels(cursor.Y - worldY * scale));
    }

    /// <summary>JS <c>Math.round</c> paritesi: .5 HER ZAMAN yukarı (+∞ yönünde). .NET'in
    /// <c>Math.Round</c>'u banker's rounding yapar — prototiple sapmamak için kullanılmaz.
    ///
    /// <para>Yalnız ANİMASYON UÇLARINDA uygulanır, ara karelerde DEĞİL: geçiş sırasında yuvarlamak
    /// titretir (A13.2).</para></summary>
    public static double RoundPixels(double value) => Math.Floor(value + 0.5);
}
