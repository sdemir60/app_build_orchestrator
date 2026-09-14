using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/T4] Göstergeyi taşıyan penceresiz kabuk.
///
/// <para>Bu pencerenin tamamı bir dizi "olmama" kararıdır: kenarlığı yok, görev çubuğunda yok, odak çalmıyor,
/// Alt-Tab'da görünmüyor ve etrafındaki şeffaf alan tıklamayı ALTTAKİ pencereye geçiriyor. Yanlış giden her
/// biri kullanıcının masaüstünü ele geçiren bir kutu üretir — bu yüzden hepsi tek tek pinlenir.</para>
///
/// <para><b>Pencere hiçbir testte <c>Show()</c> EDİLMEZ.</b> Topmost bir overlay'i süit koşarken ekrana
/// çıkarmak testin yan etkisi olamaz (ve CI'da ekran yoktur). Kabuk kararları özellik olarak, yerleşim saf bir
/// yardımcı üzerinden, tıklama ise içerik köküne olay göndererek doğrulanır — <c>MainWindowRealizeTests</c>
/// ile aynı gerekçe.</para>
///
/// <para><b>[KALDIRILAN PİN] <c>The_overlay_forwards_the_counter_to_the_indicator</c>:</b> pencerenin
/// <c>UpdateCounter</c> fiilini göstergeye geçirdiğini pinliyordu. Gösterge artık sayaç taşımıyor
/// (kullanıcının görsel testi: overlay ölçüsünde okunmuyordu) ve view sözleşmesinde böyle bir fiil yok.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public sealed class TrayBuildOverlayWindowTests
{
    private static TrayBuildOverlayWindow New() => new(DsResources.NewScope());

    // ---------------------------------------------------------------- kabuk (K-1)

    [StaFact]
    public void Overlay_window_realizes_without_a_hwnd()
    {
        var overlay = New();

        overlay.ApplyTemplate();
        var content = (FrameworkElement)overlay.Content;
        content.Measure(new Size(TrayBuildOverlayWindow.OverlayWidth, TrayBuildOverlayWindow.OverlayHeight));
        content.Arrange(new Rect(0, 0, TrayBuildOverlayWindow.OverlayWidth, TrayBuildOverlayWindow.OverlayHeight));
        content.UpdateLayout();

        Assert.True(content.ActualWidth > 0);
        Assert.True(content.ActualHeight > 0);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(overlay));
    }

    [StaFact]
    public void Overlay_window_declares_the_non_intrusive_shell()
    {
        var overlay = New();

        Assert.Equal(WindowStyle.None, overlay.WindowStyle);      // kenarlık/başlık yok
        Assert.True(overlay.AllowsTransparency);                  // katmanlı pencere → alfa 0 pikseller tıklamayı geçirir
        Assert.False(overlay.ShowInTaskbar);                      // görev çubuğunda ikinci bir uygulama görünmez
        Assert.True(overlay.Topmost);                             // masaüstünün üstünde durur
        Assert.False(overlay.ShowActivated);                      // odak ÇALMAZ (kullanıcı yazarken araya girmez)
        Assert.False(overlay.Focusable);
        Assert.Equal(ResizeMode.NoResize, overlay.ResizeMode);
        Assert.Equal(SizeToContent.Manual, overlay.SizeToContent); // boyut K-3'ten gelir, içerikten değil
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)overlay.Background).Color);
        Assert.Equal(TrayBuildOverlayWindow.OverlayWidth, overlay.Width);
        Assert.Equal(TrayBuildOverlayWindow.OverlayHeight, overlay.Height);
    }

    // ---------------------------------------------------------------- tıkla-aç (K-2)

    [StaFact]
    public void Clicking_the_overlay_requests_a_restore()
    {
        var overlay = New();
        var content = (FrameworkElement)overlay.Content;
        int restores = 0;
        overlay.RestoreRequested += () => restores++;

        content.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });

        Assert.Equal(1, restores);
        Assert.Equal(Cursors.Hand, content.Cursor);   // çizili logo tıklanabilir GÖRÜNMELİ
    }

    /// <summary>
    /// <b>Negatif pin.</b> <c>WS_EX_TRANSPARENT</c> BİLEREK yoktur.
    ///
    /// <para>"Tıklama geçirgen pencere" denince akla ilk gelen bayrak odur ve buraya eklenmesi çok kolaydır —
    /// ama eklenirse pencere TAMAMEN tıklanamaz olur: logonun kendisine basmak da alttaki pencereye geçer ve
    /// "tıkla-aç" ölür. Geçirgenlik zaten <c>AllowsTransparency</c>'den gelir: katmanlı pencerede işletim
    /// sistemi piksel piksel alfaya bakar, alfası sıfır olan yerler tıklamayı kendiliğinden geçirir. Bu yüzden
    /// ne bu bayrak ne de tam-dikdörtgen görünmez bir hit alanı (<c>#01000000</c> zemin hilesi) yazılır.</para></summary>
    [Fact]
    public void The_overlay_never_adds_the_click_through_ex_style()
    {
        Assert.Equal(0, Win32.OverlayExStyle & Win32.WS_EX_TRANSPARENT);

        // …ve olması gereken ikisi GERÇEKTEN var (aksi halde yukarıdaki iddia boş bir bayrak kümesinde de geçerdi).
        Assert.Equal(Win32.WS_EX_TOOLWINDOW, Win32.OverlayExStyle & Win32.WS_EX_TOOLWINDOW);
        Assert.Equal(Win32.WS_EX_NOACTIVATE, Win32.OverlayExStyle & Win32.WS_EX_NOACTIVATE);
    }

    // ---------------------------------------------------------------- yerleşim (K-3)

    /// <summary>Çalışma alanının SAĞ ALT köşesi, iki ayrı kenar payıyla. Alan bilerek parametredir: görev
    /// çubuğu solda ya da üstte olabilir ve o zaman çalışma alanı (0,0)'dan başlamaz — köşe hesabı ekranın
    /// kendisinden değil, çalışma alanından türemeli.</summary>
    [Theory]
    // Beklenen köşe testin GÖVDESİNDE, aynı girdiden (workArea) hesaplanır — Place'in kendi formülü
    // (Right eksi OverlayWidth eksi RightMargin; Bottom eksi OverlayHeight eksi BottomMargin). Elle kopyalanmış
    // ondalık YOKTUR.
    // taskbar altta: 1920×1080 ekran, 40px şerit
    [InlineData(0, 0, 1920, 1040)]
    // taskbar solda (80px): alan x=80'den başlar — sağ kenar yine ekranın sağı
    [InlineData(80, 0, 1840, 1080)]
    // taskbar üstte: alan y=40'tan başlar, alt kenar ekranın altı
    [InlineData(0, 40, 1920, 1040)]
    public void Overlay_positions_into_the_bottom_right_of_a_given_work_area(double x, double y, double w, double h)
    {
        var workArea = new Rect(x, y, w, h);
        var (left, top) = TrayBuildOverlayWindow.Place(
            workArea, TrayBuildOverlayWindow.OverlayWidth, TrayBuildOverlayWindow.OverlayHeight,
            TrayBuildOverlayWindow.RightMargin, TrayBuildOverlayWindow.BottomMargin);

        Assert.Equal(workArea.Right - TrayBuildOverlayWindow.OverlayWidth - TrayBuildOverlayWindow.RightMargin,
            left, precision: 10);
        Assert.Equal(workArea.Bottom - TrayBuildOverlayWindow.OverlayHeight - TrayBuildOverlayWindow.BottomMargin,
            top, precision: 10);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL]</b> Sağ pay alttan AYRI ve daha dardır.
    ///
    /// <para><b>Eski iddia:</b> tek bir <c>EdgeMargin</c> (12) hem sağa hem alta uygulanırdı.</para>
    ///
    /// <para><b>Değişme gerekçesi:</b> kullanıcının görsel testi — duruş karesinde logo bandın SOLUNDA durur,
    /// sağında şevronun çıkış yolu için bırakılmış boşluk kalır; köşeden 12 birim içeride bu boşluk göze
    /// fazla geliyordu. Gösterge hafifçe sağa kaydırıldı, alt pay (görev çubuğuna mesafe) değişmedi. Sağa
    /// kaydırmanın sınırı ekran kenarıdır: bant şevronun en uç çıkış karesini (ve gölgesini) zaten içinde
    /// taşır, pay sıfıra inmediği sürece hiçbir parça ekranın dışına taşmaz.</para></summary>
    [Fact]
    public void The_right_margin_is_narrower_than_the_bottom_one_and_never_zero()
    {
        Assert.Equal(4.0, TrayBuildOverlayWindow.RightMargin);
        Assert.Equal(12.0, TrayBuildOverlayWindow.BottomMargin);
        Assert.True(TrayBuildOverlayWindow.RightMargin > 0);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL]</b> Pencere oranı artık 430:286 DEĞİL, tasarımcının BANDININ oranıdır (bant için
    /// bkz. <see cref="TrayBuildIndicator.StageWidth"/>'in doc'u — viewBox burada ikinci kez anlatılmaz).
    ///
    /// <para><b>Eski iddia:</b> pencere sahnenin TAMAMININ (430×286) oranını 144×96 ölçüsünde, kabaca
    /// (<c>precision: 1</c>) korurdu — Viewbox <c>Uniform</c> sapmayı boş kenar olarak gösterirdi.</para>
    ///
    /// <para><b>Değişme gerekçesi:</b> kullanıcının GÖRSEL TESTİ — 144×96'da şeritler ~7 piksele düşüyordu,
    /// okunmuyordu. Tasarımcının bandı çok daha büyük — 430:286'dan belirgin biçimde farklı bir orandır.
    /// İddia GERÇEKLENMİŞ pencerenin (<c>overlay.Width/Height</c>) oranı ile bandın oranı ÜZERİNEDİR — iki
    /// sabit tanımın (<c>StageWidth/StageHeight</c> ile <c>OverlayWidth/OverlayHeight</c>) birbirine eşitliği
    /// değil: ikisi derleyicinin katladığı AYNI ifade olsaydı bu hiçbir şeyi pinlemezdi.</para>
    ///
    /// <para><b>Neden yine de toleranslı (<c>precision: 10</c>):</b> <see cref="TrayBuildOverlayWindow.Scale"/>
    /// ileride başka bir değere değişirse (ör. 0.7) çarpım/bölüm sırası son bitte yuvarlanabilir — kavramsal
    /// olarak korunan bir oranı ULP gürültüsüyle kırmamak için 10 basamaklık pay bırakılır; gerçek bir oran
    /// hatasını (ör. genişlik/yükseklik yer değiştirmesi) yine yakalar.</para></summary>
    [StaFact]
    public void The_overlay_keeps_the_scene_aspect_ratio()
    {
        var overlay = New();

        double band = TrayBuildIndicator.StageWidth / TrayBuildIndicator.StageHeight;
        double window = overlay.Width / overlay.Height;
        Assert.Equal(band, window, precision: 10);
    }

    /// <summary>
    /// Overlay ölçüsünün TEK kaynağı: göstergenin bant ölçüsü (<see cref="TrayBuildIndicator.StageWidth"/>/
    /// <see cref="TrayBuildIndicator.StageHeight"/>) çarpı TEK bir ölçek (<see cref="TrayBuildOverlayWindow.Scale"/>).
    ///
    /// <para>Pencereyi büyütüp küçültmek istenirse dokunulacak TEK sayı <c>Scale</c>'dir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> <b>Eski iddialar:</b> <c>Scale == 2/3</c>, sonra <c>0.55</c>. <b>Değişme
    /// gerekçesi:</b> kullanıcının görsel testleri — 2/3 "bir tık fazla büyük" bulundu, 0.55 "güzel ama bir tık
    /// daha küçülsün" dendi; 0.5'e indi (şeritler hâlâ ilk ölçünün yaklaşık 1.5 katı, okunur kalır).</para>
    ///
    /// <para>İddia GERÇEKLENMİŞ pencerenin (<c>overlay.Width/Height</c>) üzerinedir, <c>OverlayWidth</c>/
    /// <c>OverlayHeight</c> sabitlerinin kendi tanımına karşı değil — ikisi derleyicinin katladığı AYNI ifade
    /// olurdu ve karşılaştırma hiçbir şey pinlemezdi. Kurucunun <c>Width = OverlayWidth</c> atamasını
    /// GERÇEKTEN çalıştırdığı da böylece doğrulanır. <c>precision: 10</c>: <c>Scale</c> ileride değişirse
    /// son bitteki yuvarlama farkı testi kırmasın diye.</para></summary>
    [StaFact]
    public void The_overlay_is_sized_from_the_stage_and_one_scale()
    {
        var overlay = New();

        Assert.Equal(TrayBuildIndicator.StageWidth * TrayBuildOverlayWindow.Scale, overlay.Width, precision: 10);
        Assert.Equal(TrayBuildIndicator.StageHeight * TrayBuildOverlayWindow.Scale, overlay.Height, precision: 10);
        Assert.Equal(0.5, TrayBuildOverlayWindow.Scale);
    }

    // ---------------------------------------------------------------- view sözleşmesi

    /// <summary>
    /// Pencere, controller'ın gördüğü <c>ITrayBuildIndicatorView</c>'dur ve fiilleri göstergeye geçirir.
    ///
    /// <para>Buradaki asıl iddia BİTİŞ TAAHHÜDÜDÜR: çıkış evresi beklerken pencere geri getirilirse
    /// (<c>HideNow</c>) gösterge susar ama "bitti" haberi düşmez — controller bildirimi yine gösterebilsin
    /// diye callback yerine getirilir. Saf controller testi bunu göremez (orada view sahtedir).</para></summary>
    [StaFact]
    public void The_overlay_hands_the_finish_request_to_the_indicator_and_never_drops_it()
    {
        var overlay = New();
        bool finished = false;

        overlay.BeginExit(() => finished = true);
        Assert.False(finished);          // istek YARIM kesmez

        overlay.HideNow();               // pencere geri geldi

        Assert.True(finished);
        Assert.False(overlay.IsVisible);
    }
}
