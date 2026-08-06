using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: "Sağ altta mono ipucu: <c>scroll = zoom · drag = pan</c>, seçiliyken
/// <c>click again to release</c>."
///
/// <para>İpucu dekoratif değil: pan KELEPÇESİZDİR (kamera aritmetiğinde gerekçesi yazılı) ve kullanıcının
/// grafı geri getirme yolu tam olarak bu satırın duyurduğu jesttir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphHintTests
{
    private static GraphView Built()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Discovered)], []);
        return view;
    }

    [StaFact]
    public void The_hint_switches_between_the_navigate_and_the_release_copy()
    {
        var view = Built();
        Assert.Equal(InteractionText.GraphHintNavigate, view.HintText);

        view.SelectedNode = "OSYS.Base";
        Assert.Equal(InteractionText.GraphHintRelease, view.HintText);

        view.SelectedNode = null;
        Assert.Equal(InteractionText.GraphHintNavigate, view.HintText);
    }

    /// <summary>Sync'ten önce gezinecek bir şey yoktur — ipucu da yoktur (boş durum kutusuyla birlikte).</summary>
    [StaFact]
    public void Before_sync_there_is_nothing_to_navigate_so_the_hint_stays_hidden()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        Assert.Equal(Visibility.Collapsed, view.HintVisibility);

        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Discovered)], []);
        Assert.Equal(Visibility.Visible, view.HintVisibility);
    }

    /// <summary>[REALIZE TESTİ] İpucu yeni bir XAML öğesidir: gerçek pencerede mono ailesini ve
    /// <c>Brush.TextFaint</c>'i çözerek realize olmalı ve sağ ALTA yaslanmalı.</summary>
    [StaFact]
    public void The_hint_realizes_in_a_real_window_pinned_to_the_bottom_right()
    {
        var host = DsResources.NewHost();
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Discovered)], []);
        view.UpdateLayout();

        var hint = DsResources.RealizedObjects(view.Ground).OfType<TextBlock>()
            .Single(t => t.Text == InteractionText.GraphHintNavigate);
        Assert.Same(AppFonts.Mono, hint.FontFamily);
        Assert.Same(view.FindResource("Brush.TextFaint"), hint.Foreground);
        Assert.Equal(HorizontalAlignment.Right, hint.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, hint.VerticalAlignment);
        Assert.True(hint.ActualWidth > 0, "ipucu realize olmadı (genişlik 0)");
        // Yerleşim ona 18px rezerv ayırır — düğümler bu banda değmemeli.
        Assert.True(hint.ActualHeight <= QuietGraphLayout.HintReservePx,
            $"ipucu {hint.ActualHeight:N1}px — yerleşimin ayırdığı {QuietGraphLayout.HintReservePx}px rezervi aşıyor");
        GC.KeepAlive(window);
    }
}
