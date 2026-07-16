using System.Windows;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

public class MaximizeFixTests
{
    [Fact]
    public void Normal_state_has_zero_padding()
        => Assert.Equal(new Thickness(0), MaximizeFix.PaddingFor(WindowState.Normal, 8, 8, 4, 1.0));

    [Fact]
    public void Maximized_pads_by_frame_plus_padded_border_in_dip() // dotnet/wpf#3887
        => Assert.Equal(new Thickness(12, 12, 12, 12), MaximizeFix.PaddingFor(WindowState.Maximized, 8, 8, 4, 1.0));

    [Fact]
    public void Dpi_scale_converts_px_to_dip()
        => Assert.Equal(new Thickness(8, 8, 8, 8), MaximizeFix.PaddingFor(WindowState.Maximized, 8, 8, 4, 1.5));
}
