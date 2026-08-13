using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın bakım kutusu: <b>Clean · Optimize · Resolve cycles</b>. Üçü de
/// derleme ÖNCESİ hazırlık işleridir ve tek kutuda birlikte okunurlar; Sync'in komşusudur, Build'in değil
/// (orası birincil aksiyonun yeridir ve bunlar onun varyantı değildir).
///
/// <para><b>Etiket YOK:</b> üç etiketli düğme barı 1240px minimumda taşırıyor ve Build split-button'ı
/// eziyordu — anlamı tooltip taşır.</para>
/// </summary>
public partial class MaintenanceBox : UserControl
{
    // BuildApp.jsx:1932-1933 literal ölçüleri — kutunun KENDİ değerleri, tasarım token'ı değil.
    private const double ButtonWidth = 28;
    private const double ButtonHeight = 22;
    private const double IconSize = 12;     // BuildApp.jsx:59-61 <svg width="12" height="12">

    private bool _built;

    public MaintenanceBox()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Button CleanButton => PART_Clean;
    internal Button OptimizeButton => PART_Optimize;
    internal Button ResolveButton => PART_Resolve;

    private void Build()
    {
        if (_built) return;
        _built = true;
        Shape(PART_Clean, "Icon.Eraser");
        Shape(PART_Optimize, "Icon.Gauge");
        Shape(PART_Resolve, "Icon.Unlink");
    }

    private void Shape(Button button, string iconKey)
    {
        if (TryFindResource("Ds.IconButton") is Style s) button.Style = s;
        button.Width = ButtonWidth;
        button.Height = ButtonHeight;
        // Kutu tek parça okunur: düğmenin kendi kenarı yoktur, çerçeveyi kök Border taşır.
        button.BorderThickness = new Thickness(0);
        button.Content = IconVisual.Make(this, iconKey, "Brush.TextSecondary", IconSize);
    }
}
