using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [W2/It-5] <see cref="BranchPopover"/> ve <see cref="WorktreePopover"/>'ın ORTAK iskeleti — TEK yer. İki popover
/// aşağıdaki dört parçayı gövde olarak birebir aynı yazıyordu:
/// <list type="number">
///   <item><see cref="IsOpen"/> DP'si (iki-yönlü; ActionBar <c>Popup.IsOpen</c>'a bağlar) + açılış davranışı:
///     içeriği tazele → 140ms <see cref="PopIn"/> → odağı İÇERİ taşı.</item>
///   <item>Esc → <see cref="CloseRequested"/> (popover AYRI bir HWND olduğundan pencere-seviyesi Esc zinciri
///     buraya ULAŞMAZ; odak açılışta içeri taşındığı için Esc'i popover'ın KENDİSİ yakalamalıdır).</item>
///   <item>DataContext (VM) takası: eski VM'in aboneliklerini çöz, yenisininkileri kur, içeriği tazele.</item>
///   <item><c>Loaded</c>'da ilk tazeleme.</item>
/// </list>
///
/// <para><b>Popover'a ÖZEL davranışlar türevde kalır</b> (Branch: kapanışta sorgu sıfırlama · Worktree: hover'da
/// çöp kutusu, forced/disabled switch, üçüncü kolon). Odak hedefi ve tazeleme türevin sorumluluğudur
/// (<see cref="InitialFocusTarget"/>, <see cref="RefreshContent"/>).</para>
///
/// <para><b>Kapsam dışı:</b> <see cref="BuildMenu"/> — onun <see cref="IsOpen"/> DP'si ve Esc'i YOKtur, pop-in'ini
/// dışarıdan <see cref="ActionBar"/> tetikler.</para>
/// </summary>
public abstract class PopoverBase : UserControl
{
    private RunViewModel? _vm;

    /// <summary>
    /// [W2 fix-1 · latent bug kapatıldı] Kablaj <b>ctor'da DEĞİL</b> burada kurulur. Taban ctor'u türevin
    /// <c>InitializeComponent()</c>'inden ÖNCE koşar; orada <c>DataContextChanged</c>'e abone olmak, XAML kökü
    /// <c>DataContext</c>'i kendi attribute'uyla atadığı anda <see cref="RefreshContent"/>'i türevin
    /// <c>PART_*</c> alanları HENÜZ null iken çağırırdı (NullReferenceException). Bugünkü iki popover kökü
    /// DataContext atamadığı için patlamıyordu — yani bir zaman bombasıydı.
    ///
    /// <para><c>OnInitialized</c>, <c>InitializeComponent()</c>'in içindeki <c>EndInit</c> ile tetiklenir; yani
    /// buraya gelindiğinde adlandırılmış öğelerin TAMAMI kurulmuştur. Ağaca kod tarafından eklenen (BeginInit
    /// görmemiş) bir öğe için WPF bunu parent'a bağlanırken tetikler — bu yüzden <c>DataContext</c> kablajdan
    /// ÖNCE atanmış olabilir; o durumu da kaçırmamak için VM aşağıda ayrıca <b>seed</b> edilir.</para>
    /// </summary>
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RefreshContent();
        // Kablajdan ÖNCE atanmış bir DataContext varsa (XAML kök attribute'u / object initializer) değişim olayı
        // kaçırılmış olur — burada yakala ki abonelik ve ilk tazeleme yine de koşsun.
        if (DataContext is RunViewModel seeded) SwapVm(seeded);
    }

    /// <summary>[E5/T46] Popover içinde Esc — ActionBar popover'ı kapatır + odağı tetikleyici chip'e döndürür.</summary>
    public event Action? CloseRequested;

    /// <summary>[D6] Popover açık mı — ActionBar, <c>Popup.IsOpen</c> ile bağlar. true olunca içerik tazelenir,
    /// 140ms pop-in oynar ve odak içeri taşınır; false olunca <see cref="OnClosed"/> koşar.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(PopoverBase),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    /// <summary>Bağlı <see cref="RunViewModel"/> (DataContext); henüz atanmadıysa null.</summary>
    protected RunViewModel? Vm => _vm;

    /// <summary>Popover gövdesini VM'den yeniden kurar — açılışta, <c>Loaded</c>'da ve VM takasında çağrılır.</summary>
    protected abstract void RefreshContent();

    /// <summary>Açılışta odağın taşınacağı İLK etkileşimli öğe (Branch: arama kutusu · Worktree: switch).</summary>
    protected abstract UIElement InitialFocusTarget { get; }

    /// <summary>VM takasında yeni VM'in olaylarına abone ol.</summary>
    protected abstract void SubscribeVm(RunViewModel vm);

    /// <summary>VM takasında eski VM'in olaylarından çık.</summary>
    protected abstract void UnsubscribeVm(RunViewModel vm);

    /// <summary>Popover kapanınca (varsayılan: hiçbir şey). Branch bunu sorguyu sıfırlamak için ezer.</summary>
    protected virtual void OnClosed()
    {
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var popover = (PopoverBase)d;
        if (e.NewValue is not true) { popover.OnClosed(); return; }

        popover.RefreshContent();
        PopIn.Play(popover);
        // [E5/T47] Açılınca odak İÇERİ (ilk etkileşimli öğe). Popup içeriği bu an henüz realize olmamış olabilir
        // → layout tamamlanınca odakla (Input önceliği).
        popover.Dispatcher.BeginInvoke(
            DispatcherPriority.Input, new Action(() => popover.InitialFocusTarget.Focus()));
    }

    /// <summary>[E5/T46] Esc → popover'ı kapat (arama kutusundan/switch'ten bubble eder). Prototip
    /// BuildApp.jsx:1313 gibi popover katmanı: Esc içerikteki aramayı TEMİZLEMEZ, popover'ı KAPATIR.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => SwapVm(e.NewValue as RunViewModel);

    /// <summary>VM takasının TEK yolu: eskisinden çık, yenisine gir, içeriği tazele. Hem
    /// <c>DataContextChanged</c> hem <see cref="OnInitialized"/>'daki seed buradan geçer (kopya YASAK).</summary>
    private void SwapVm(RunViewModel? vm)
    {
        if (_vm is not null) UnsubscribeVm(_vm);
        _vm = vm;
        if (_vm is not null) SubscribeVm(_vm);
        RefreshContent();
    }
}
