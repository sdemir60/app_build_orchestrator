using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>Çalışma alanının SAĞ ALT köşesi, kenar payıyla. Alan bilerek parametredir: görev çubuğu solda
    /// ya da üstte olabilir ve o zaman çalışma alanı (0,0)'dan başlamaz — köşe hesabı ekranın kendisinden
    /// değil, çalışma alanından türemeli.</summary>
    [Theory]
    // taskbar altta: 1920×1080 ekran, 40px şerit
    [InlineData(0, 0, 1920, 1040, 1764, 932)]
    // taskbar solda (80px): alan x=80'den başlar — sağ kenar yine ekranın sağı
    [InlineData(80, 0, 1840, 1080, 1764, 972)]
    // taskbar üstte: alan y=40'tan başlar, alt kenar ekranın altı
    [InlineData(0, 40, 1920, 1040, 1764, 972)]
    public void Overlay_positions_into_the_bottom_right_of_a_given_work_area(
        double x, double y, double w, double h, double expectedLeft, double expectedTop)
    {
        var (left, top) = TrayBuildOverlayWindow.Place(
            new Rect(x, y, w, h),
            TrayBuildOverlayWindow.OverlayWidth, TrayBuildOverlayWindow.OverlayHeight,
            TrayBuildOverlayWindow.EdgeMargin);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Fact]
    public void The_overlay_keeps_the_scene_aspect_ratio()
    {
        // Sahne 430×286; pencere oranı ondan belirgin biçimde sapmamalı (Viewbox Uniform ölçekler ve sapma
        // ne kadar büyükse o kadar boş kenar bırakır).
        double scene = 430.0 / 286.0;
        double window = TrayBuildOverlayWindow.OverlayWidth / TrayBuildOverlayWindow.OverlayHeight;
        Assert.Equal(scene, window, precision: 1);
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

    [StaFact]
    public void The_overlay_forwards_the_counter_to_the_indicator()
    {
        var overlay = New();

        overlay.UpdateCounter(139, 248);

        Assert.Equal("139/248", overlay.Indicator.Counter.Text);
    }
}
