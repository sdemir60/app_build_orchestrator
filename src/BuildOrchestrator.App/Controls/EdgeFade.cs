using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Kayan bir listenin üst kenarındaki SOLMA ŞERİDİ (gölge): kendi kutusunu <see cref="Fill"/> renginden (üst
/// kenarda tam opak) aynı rengin saydamına (alt kenarda) inen dikey bir rampayla boyar. <see cref="Fill"/>
/// listenin zemini olduğunda altından geçen içerik zemine karışarak solar; şerit boşken zeminden ayırt edilemez.
/// Tıklamayı ve tekerleği almaz — ikisi de alttaki listeye geçer (bkz. <see cref="Views.NotesDialog"/>).
///
/// <para><b>Neden içeriğe maske değil, üstüne şerit:</b> aynı rampa kayan içeriğin sunucusuna
/// <c>OpacityMask</c> olarak da konabilirdi; canlı pencerede ölçüldü — maske içeriği ayrı bir katmana çizer ve
/// katmanın kenar piksellerini 1px dışarı taşırır: sol kolondaki her sürüm numarasının, tarihin ve
/// <c>INSTALLED</c> çipinin solunda silik bir iz kalıyordu (ekran dışı çizimde görünmez). Bu şerit ara katman
/// açmaz, altındaki çizime dokunmaz.</para>
///
/// <para>Rampanın iki ucu AYNI RGB'dir, yalnız alfa değişir: saydam siyaha inen bir rampa (<c>Transparent</c>)
/// koyu zeminde ortada zeminden koyu bir bant bırakırdı.</para>
/// </summary>
public sealed class EdgeFade : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(EdgeFade),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public EdgeFade() => IsHitTestVisible = false;

    /// <summary>Şeridin rengi — altından geçen içeriğin zemini. Düz renk (<see cref="SolidColorBrush"/>) değilse
    /// şerit çizilmez.</summary>
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Fill is not SolidColorBrush { Color: var color } || RenderSize.Height <= 0) return;
        var ramp = new LinearGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B), new Point(0, 0), new Point(0, 1));
        ramp.Freeze();
        drawingContext.DrawRectangle(ramp, null, new Rect(RenderSize));
    }
}
