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

    protected PopoverBase()
    {
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RefreshContent();
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
    {
        if (_vm is not null) UnsubscribeVm(_vm);
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) SubscribeVm(_vm);
        RefreshContent();
    }
}
