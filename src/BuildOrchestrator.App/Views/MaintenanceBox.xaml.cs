using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın bakım kutusu: <b>Clean · Optimize · Resolve cycles</b>. Üçü de
/// derleme ÖNCESİ hazırlık işleridir ve tek kutuda birlikte okunurlar; Sync'in komşusudur, Build'in değil
/// (orası birincil aksiyonun yeridir ve bunlar onun varyantı değildir).
///
/// <para><b>Etiket YOK:</b> üç etiketli düğme barı 1240px minimumda taşırıyor ve Build split-button'ı
/// eziyordu — anlamı tooltip taşır (<see cref="AccessibilityNames"/>).</para>
///
/// <para><b>Clean/Optimize pasif (karar 2026-08-13):</b> arka uçları henüz yazılmadı. Düğmeler tasarımdaki
/// yerlerinde durur, kalıcı olarak disabled'dır ve tooltip nedeni söyler — basılıp hiçbir şey olmaması
/// yokluğu sessizce gizlemekten daha kötü olurdu.</para>
/// </summary>
public partial class MaintenanceBox : UserControl
{
    // BuildApp.jsx:1932-1933 literal ölçüleri — kutunun KENDİ değerleri, tasarım token'ı değil.
    private const double ButtonWidth = 28;
    private const double ButtonHeight = 22;
    private const double IconSize = 12;     // BuildApp.jsx:59-61 <svg width="12" height="12">

    private RunViewModel? _vm;
    private bool _built;

    public MaintenanceBox()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { Build(); Refresh(); };
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Button CleanButton => PART_Clean;
    internal Button OptimizeButton => PART_Optimize;
    internal Button ResolveButton => PART_Resolve;

    // ---------------------------------------------------------------- lifecycle
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tooltip sayıları topolojiden gelir. HasCycles her workspaceTopology'de AÇIKÇA yayılır (boole aynı
        // kalsa da sayılar değişmiş olabilir — bkz. RunViewModel.Workspace.OnWorkspaceTopology).
        if (e.PropertyName is nameof(RunViewModel.HasCycles)) Refresh();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        Shape(PART_Clean, "Icon.Eraser", AccessibilityNames.CleanButton);
        Shape(PART_Optimize, "Icon.Gauge", AccessibilityNames.OptimizeButton);
        Shape(PART_Resolve, "Icon.Unlink", AccessibilityNames.ResolveCyclesButton);

        // Arka uç yok → kalıcı disabled. Tooltip'leri SABİT olduğu için bir kez yazılır; Refresh yalnız
        // Resolve'unkini (sayılara bağlı) tazeler. Pasif kontrolde WPF tooltip'i varsayılan olarak
        // göstermez — açıkça açılır, yoksa metin var ama kullanıcı hiç göremez.
        foreach (var button in new[] { PART_Clean, PART_Optimize })
        {
            button.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(button, true);
        }
        PART_Clean.ToolTip = AccessibilityNames.CleanTooltip;
        PART_Optimize.ToolTip = AccessibilityNames.OptimizeTooltip;
    }

    private void Shape(Button button, string iconKey, string uiaName)
    {
        if (TryFindResource("Ds.IconButton") is Style s) button.Style = s;
        button.Width = ButtonWidth;
        button.Height = ButtonHeight;
        // Kutu tek parça okunur: düğmenin kendi kenarı yoktur, çerçeveyi kök Border taşır.
        button.BorderThickness = new Thickness(0);
        button.Content = IconVisual.Make(this, iconKey, "Brush.TextSecondary", IconSize);
        AutomationProperties.SetName(button, uiaName);
    }

    /// <summary>Resolve tooltip'inin TEK yazıcısı.</summary>
    private void Refresh()
    {
        if (!_built) return;
        PART_Resolve.ToolTip = AccessibilityNames.ResolveCyclesTooltip(
            _vm?.CycleGroupCount ?? 0, _vm?.CycleMemberCount ?? 0);
    }
}
