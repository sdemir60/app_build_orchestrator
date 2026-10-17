using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.25.0 §2.1] Title bar'ın tam ortasındaki <b>kaynak şeridi</b>: CPU · RAM · paralellik + mod, üç hücre, hepsi yalnız okur. Görünüm
/// <c>ResourceStrip.xaml</c>'dedir (ölçüler, renkler, hayalet metinler, aşağı açılan tooltip'ler); değerler <c>RunViewModel</c>'in <c>Strip*</c>
/// özelliklerinden buradan yazılır (örnekleme ve metin üretimi VM tarafında, örnekleyicinin kablajı <c>MainWindow.ResourceStrip.cs</c>'te). İş
/// mantığı YOKTUR: ne zamanlayıcı, ne fare davranışı, ne animasyon — yalnız "VM'in metnini hücreye yaz". Hücrelerin üstündeki sürükleme/çift
/// tık/sistem menüsü, hücrelerin <c>IsHitTestVisibleInChrome</c> altında olmasının bedelidir ve pencerede iletilir; kökün zemini yoktur, yani
/// hücre arası boşluklar ve hücrelerin üstü/altı (üst boyutlandırma bandı dahil) yerel title bar'da kalır.
///
/// <para><b>Neden XAML bağı değil, kod-tarafı yazım (perf Faz A):</b> pencere tepsideyken kimsenin görmediği ekran işi yapılmaz — bir metnin
/// değişmesi ölçümü geçersizler ve koşu sürerken pay (derlenen proje sayısı) saniyede onlarca kez değişir. Bir <c>{Binding}</c> bu kapıyı
/// tanımaz. Şerit diğer görünümler (şerit, proje satırları, olay akışı) gibi VM'in <c>PropertyChanged</c>'ına abone olur; yüzey gizliyken yalnız
/// "modelin gerisindeyim" bayrağını kaldırır ve hiçbir hücreye yazmaz, görünür olunca (kalıtsal <see cref="HiddenSurface"/> sinyali) TEK geçişte
/// modele yetişir. Bu aynı zamanda tooltip'lerin içeriğini de çözer: bir <see cref="ToolTip"/> ağacın parçası değildir ve DataContext miras ALMAZ
/// (ölçüldü: düz <c>{Binding}</c> tooltip açılana dek boş kalır; <c>x:Reference</c> de kökü kendi içinden gösteremez — ProvideValue döngüsü), bu
/// yüzden içerik doğrudan atanır.</para>
///
/// <para>UIA: bir <see cref="UserControl"/>'ün varsayılan peer'ı <c>Custom</c>'dır ve her yerleşimde baştan sayılır (<see cref="UserControlRolePeer"/>);
/// şerit gerçek rolünü bildirir — adlı bir <c>Group</c> (<see cref="AccessibilityNames.ResourceUsage"/>). Hücrelerin etiketi yoktur: ekran okuyucu
/// değerleri kendi metinlerinden duyar.</para>
/// </summary>
public partial class ResourceStrip : UserControl
{
    private RunViewModel? _vm;

    /// <summary>Yüzey gizliyken şeridin izlediği bir VM özelliği değişti: hücreler modelin gerisinde. Görünür olunca <see cref="Refresh"/> düşürür.</summary>
    private bool _staleWhileHidden;

    protected override AutomationPeer OnCreateAutomationPeer() => new UserControlRolePeer(this, AutomationControlType.Group);

    public ResourceStrip()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
    }

    /// <summary>Şeridin izlediği sekiz VM özelliği: üç hücrenin beş değeri ve üç tooltip. Başka her bildirim (koşu sırasında yüzlercesi) burada elenir.</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RunViewModel.StripCpuText) or nameof(RunViewModel.StripMemoryText)
            or nameof(RunViewModel.StripBuildingText) or nameof(RunViewModel.StripAllowedText) or nameof(RunViewModel.StripModeText)
            or nameof(RunViewModel.StripCpuTooltip) or nameof(RunViewModel.StripMemoryTooltip) or nameof(RunViewModel.StripParallelTooltip)))
            return;

        if (HiddenSurface.GetIsHidden(this)) { _staleWhileHidden = true; return; }
        Refresh();
    }

    /// <summary>Kalıtsal <see cref="HiddenSurface.IsHiddenProperty"/> değişimi torunlara buradan gelir: yüzey görünür olunca ve bayat kalındıysa
    /// şerit modele TEK geçişte yetişir.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (HiddenSurface.BecameVisible(e) && _staleWhileHidden) Refresh();
    }

    /// <summary>VM'in o anki metinlerini hücrelere ve tooltip'lere yazar (aynı değeri yazmak ölçümü geçersizlemez). VM yoksa (DataContext
    /// kalktı) hücrelere dokunulmaz.</summary>
    private void Refresh()
    {
        _staleWhileHidden = false;
        if (_vm is not { } vm) return;

        CpuValue.Text = vm.StripCpuText;
        MemoryValue.Text = vm.StripMemoryText;
        BuildingValue.Text = vm.StripBuildingText;
        AllowedValue.Text = vm.StripAllowedText;
        ModeValue.Text = vm.StripModeText;
        ((ToolTip)CpuCell.ToolTip).Content = vm.StripCpuTooltip;
        ((ToolTip)MemoryCell.ToolTip).Content = vm.StripMemoryTooltip;
        ((ToolTip)ParallelCell.ToolTip).Content = vm.StripParallelTooltip;
    }
}
