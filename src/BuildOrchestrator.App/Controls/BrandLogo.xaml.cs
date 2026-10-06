using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>[About] Delta marka logosu — uygulamadaki TEK çizimi (title bar 15px, About hero'su 20px).
/// Tüketiciler yalnız <see cref="System.Windows.FrameworkElement.Height"/> verir; genişlik iç Viewbox'ın
/// Uniform ölçeğinden gelir.</summary>
public partial class BrandLogo : UserControl
{
    /// <summary>UIA rolü — gerekçe ve ölçüm <see cref="UserControlRolePeer"/>'de.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new UserControlRolePeer(this, AutomationControlType.Image);

    public BrandLogo() => InitializeComponent();
}
