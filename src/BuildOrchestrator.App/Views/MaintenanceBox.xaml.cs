using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
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
/// <para><b>Üçünün de motoru vardır</b> ve üçü de gerçek komutlara bağlıdır; enable hâllerinin TEK yazıcısı o
/// komutların <c>CanExecute</c>'udur — kutu kendi enable hâlini YAZMAZ. Koşan işin düğmesi amber zemin +
/// spinner olur (<see cref="RefreshBusy"/>).</para>
/// </summary>
public partial class MaintenanceBox : UserControl
{
    // BuildApp.jsx:1932-1933 literal ölçüleri — kutunun KENDİ değerleri, tasarım token'ı değil.
    private const double ButtonWidth = 28;
    private const double ButtonHeight = 22;
    private const double IconSize = 12;     // BuildApp.jsx:59-61 <svg width="12" height="12">

    private RunViewModel? _vm;
    private bool _built;

    // Resolve'un ikon Path'i — rengi döngü varlığına göre değişen TEK öğe.
    private Path _resolveIcon = null!;

    // İşi koşarken ikonun YERİNE spinner konur; ikonlar burada saklanır ki iş bitince geri gelsinler.
    private Viewbox _cleanIcon = null!;
    private Viewbox _optimizeIcon = null!;
    private Viewbox _resolveIconBox = null!;

    public MaintenanceBox()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { Build(); Refresh(); RefreshBusy(); };
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Button CleanButton => PART_Clean;
    internal Button OptimizeButton => PART_Optimize;
    internal Button ResolveButton => PART_Resolve;
    /// <summary>[test yüzeyi] Resolve ikonunun o anki fırçası (döngü varken cycle turuncusu).</summary>
    internal Brush ResolveIconBrush => _resolveIcon.Stroke;

    // ---------------------------------------------------------------- lifecycle
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        Refresh();
        RefreshBusy(); // DataContext sonradan gelirse uçuştaki iş yine de boyanır
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tooltip sayıları topolojiden gelir. HasCycles her workspaceTopology'de AÇIKÇA yayılır (boole aynı
        // kalsa da sayılar değişmiş olabilir — bkz. RunViewModel.Workspace.OnWorkspaceTopology).
        if (e.PropertyName is nameof(RunViewModel.HasCycles)) Refresh();
        // Koşan işin düğmesi amber + spinner olur; ikisi de VM'de bildirimli DURUMLARDIR (komut değil).
        if (e.PropertyName is nameof(RunViewModel.CleanBusy) or nameof(RunViewModel.OptimizeBusy)
            or nameof(RunViewModel.IsResolvingCycles))
            RefreshBusy();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        _cleanIcon = Compose(PART_Clean, "Icon.Eraser", AccessibilityNames.CleanButton);
        _optimizeIcon = Compose(PART_Optimize, "Icon.Gauge", AccessibilityNames.OptimizeButton);
        _resolveIconBox = Compose(PART_Resolve, "Icon.Unlink", AccessibilityNames.ResolveCyclesButton);
        _resolveIcon = IconPathOf(_resolveIconBox);

        // Resolve'un işi MEVCUT döngü koşusudur (yüzey yer değiştirdi, iş değişmedi). Komut binding ile
        // bağlanır: DataContext sonradan gelse de düğme doğru komuta bakar.
        PART_Resolve.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(RunViewModel.BuildCyclesCommand)));

        // [clean] Clean'in motoru var: düğme komuta BAĞLANIR, enable'ı komutun CanExecute'undan gelir —
        // kutu kendi enable hâlini YAZMAZ (Resolve ile aynı desen, iki yazıcı olmaz).
        PART_Clean.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(RunViewModel.CleanCommand)));

        // [optimize] Optimize'ın da motoru var: aynı desen, ikinci bir enable yazıcısı yok.
        PART_Optimize.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(RunViewModel.OptimizeCommand)));

        // Tooltip'ler SABİT olduğu için bir kez yazılır; Refresh yalnız Resolve'unkini (sayılara bağlı) tazeler.
        // Pasif kontrolde WPF tooltip'i varsayılan olarak göstermez — açıkça açılır, yoksa metin var ama
        // kullanıcı hiç göremez. İki bakım düğmesi de mid-run/mid-sync pasiftir ve nedeni ancak tooltip'ten okunur.
        foreach (var button in new[] { PART_Clean, PART_Optimize }) ToolTipService.SetShowOnDisabled(button, true);
        PART_Clean.ToolTip = AccessibilityNames.CleanTooltip;
        PART_Optimize.ToolTip = AccessibilityNames.OptimizeTooltip;
    }

    /// <summary>Düğmeyi biçimlendirir ve ikon görselini döndürür — iş koşarken yerine spinner konacağı için
    /// çağıran onu SAKLAR (<see cref="SetBusy"/>).</summary>
    private Viewbox Compose(Button button, string iconKey, string uiaName)
    {
        if (TryFindResource("Ds.IconButton") is Style s) button.Style = s;
        button.Width = ButtonWidth;
        button.Height = ButtonHeight;
        // Kutu tek parça okunur: düğmenin kendi kenarı yoktur, çerçeveyi kök Border taşır.
        button.BorderThickness = new Thickness(0);
        var icon = IconVisual.Make(this, iconKey, "Brush.TextSecondary", IconSize);
        button.Content = icon;
        AutomationProperties.SetName(button, uiaName);
        return icon;
    }

    /// <summary>İkon görselinin boyanabilir <see cref="Path"/>'i (<see cref="ResolveIconBrush"/> test yüzeyi).</summary>
    private static Path IconPathOf(Viewbox icon) => (Path)((Canvas)icon.Child).Children[0];

    /// <summary>
    /// [design — BuildApp.jsx:2619-2622/2639-2641] Koşan işin düğmesi DS'in <c>active</c> hâline geçer:
    /// amber-soft zemin ve ikonun yerinde dönen spinner. Kutunun ÜÇ düğmesi de kendi işini gösterir — koşan
    /// iş nerede başladıysa orada görünür.
    /// </summary>
    private void RefreshBusy()
    {
        if (!_built) return;
        SetBusy(PART_Clean, _cleanIcon, _vm?.CleanBusy == true);
        SetBusy(PART_Optimize, _optimizeIcon, _vm?.OptimizeBusy == true);
        SetBusy(PART_Resolve, _resolveIconBox, _vm?.IsResolvingCycles == true);
    }

    /// <summary>
    /// Tek düğmenin meşgul boyaması. <b>Komut kapısına DOKUNULMAZ</b> — düğme uçuşta zaten pasiftir (ikinci
    /// bir Clean anlamsız) ve enable'ın tek yazıcısı komut kalır; değişen yalnız görünümdür.
    ///
    /// <para>Üç şey birlikte gider: (a) zemin, <c>Ds.IconButton.Toggle</c>'ın <c>IsChecked</c> tetikleyicisiyle
    /// AYNI token çiftinden (amber-soft) — yerel değer stilin hover tetikleyicisini de bastırır; (b) içerik,
    /// ikonla aynı kutuda bir <see cref="BuildingSpinner"/> (kendi varsayılan stili onu zaten amber boyar ve
    /// azaltılmış harekette döndürmez); (c) opaklık, çünkü <c>Ds.Button.Base</c> pasif düğmeyi 0.45'e söndürür
    /// ve koşan iş sönük görünmemelidir (prototipte koşan düğme disabled kümesinin DIŞINDADIR).</para>
    ///
    /// <para>İşi bitince yerel değerler TEMİZLENİR (<c>ClearValue</c>), böylece stilin kendi varsayılanları ve
    /// hover tetikleyicisi yeniden söz sahibi olur. Çağrı idempotenttir: aynı durum ikinci kez yazılmaz, aksi
    /// halde her bildirimde yeni bir spinner kurulur ve animasyon baştan başlardı.</para>
    /// </summary>
    private static void SetBusy(Button button, Viewbox icon, bool busy)
    {
        if (busy == button.Content is BuildingSpinner) return;
        if (busy)
        {
            button.Content = new BuildingSpinner { Size = IconSize };
            button.SetResourceReference(DsTransition.AnimatedBackgroundProperty, "Brush.AmberSoft");
            button.Opacity = 1;
        }
        else
        {
            button.Content = icon;
            button.ClearValue(DsTransition.AnimatedBackgroundProperty);
            button.ClearValue(OpacityProperty);
        }
    }

    /// <summary>Resolve'un tooltip'inin TEK yazıcısı.
    /// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §2.7-2/§3.7]</b> İkon döngü varken TURUNCUYA dönerdi
    /// (<c>Brush.StatusCycleText</c>) — renk "listedeki ve graftaki yapısal işaretin aynısı" diye
    /// gerekçelendirilmişti. v1.11.0 turuncuyu UI'dan tamamen çıkardı: o yapısal işaret artık yok (satırdaki
    /// tek amber üçgen kaldı), dolayısıyla ikonun rengi de NÖTRDÜR. Döngünün varlığını düğmenin ENABLE
    /// durumu ve tooltip'i söyler.</para></summary>
    private void Refresh()
    {
        if (!_built) return;
        int groups = _vm?.CycleGroupCount ?? 0;
        int members = _vm?.CycleMemberCount ?? 0;
        PART_Resolve.ToolTip = AccessibilityNames.ResolveCyclesTooltip(groups, members);
    }
}
