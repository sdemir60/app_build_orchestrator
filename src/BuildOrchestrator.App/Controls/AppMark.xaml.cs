using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>[design-v1.2.1 §6] Build Orchestrator'ın ÜRÜN markası — uygulamadaki tek çizimi (title bar 19px,
/// About başlığı 30px). Firma logosu için <see cref="BrandLogo"/> ayrıdır. Tüketiciler yalnız
/// <see cref="System.Windows.FrameworkElement.Height"/> verir; genişlik iç Viewbox'ın Uniform ölçeğinden
/// gelir, işaretin 186×128 oranı bozulmaz.</summary>
public partial class AppMark : UserControl
{
    public AppMark() => InitializeComponent();
}
