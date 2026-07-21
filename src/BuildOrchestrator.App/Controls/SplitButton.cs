using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T60] DS'in "Build ▲" ikili butonu (BuildApp.jsx:1591-1598): TEK gövde gibi görünen iki yarım — solda
/// birincil eylem, sağda menüyü açan chevron. Prototipte iki ayrı primary <c>Button</c>'dır ve BİRLEŞİK
/// görünüm yalnız köşelerin düzleştirilmesiyle (<c>borderTopRightRadius: 0</c> / <c>…LeftRadius: 0</c>) ve
/// aralarındaki 1px <c>amber-dim</c> çizgiyle kurulur — bu kontrol de tam olarak bunu yapar (iki yarım aynı
/// <c>Ds.Button.Primary.Md</c> şablonunu paylaşır, yalnız <see cref="DsChrome.CornerRadiusProperty"/>
/// değerleri ayrışır).
///
/// <para>WPF'te hazır bir split button YOKTUR; ayrı bir kontrol olmasının nedeni budur. Menü içeriği bir
/// <see cref="Popup"/>'ta <c>Ds.Popover</c> kabuğuyla gösterilir.</para>
/// </summary>
[TemplatePart(Name = PrimaryPart, Type = typeof(ButtonBase))]
[TemplatePart(Name = MenuPart, Type = typeof(ToggleButton))]
public class SplitButton : Control
{
    private const string PrimaryPart = "PART_Primary";
    private const string MenuPart = "PART_Menu";

    static SplitButton()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SplitButton), new FrameworkPropertyMetadata(typeof(SplitButton)));

    /// <summary>Sol yarımın içeriği (BuildApp.jsx:1593 — ikon + "Build"/"Continue").</summary>
    public static readonly DependencyProperty PrimaryContentProperty = DependencyProperty.Register(
        nameof(PrimaryContent), typeof(object), typeof(SplitButton));

    public object? PrimaryContent
    {
        get => GetValue(PrimaryContentProperty);
        set => SetValue(PrimaryContentProperty, value);
    }

    /// <summary>Chevron'a basınca açılan menünün içeriği (BuildApp.jsx:1599-1611 <c>Popover</c>).</summary>
    public static readonly DependencyProperty MenuContentProperty = DependencyProperty.Register(
        nameof(MenuContent), typeof(object), typeof(SplitButton));

    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public static readonly DependencyProperty PrimaryCommandProperty = DependencyProperty.Register(
        nameof(PrimaryCommand), typeof(ICommand), typeof(SplitButton));

    public ICommand? PrimaryCommand
    {
        get => (ICommand?)GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    public static readonly DependencyProperty IsMenuOpenProperty = DependencyProperty.Register(
        nameof(IsMenuOpen), typeof(bool), typeof(SplitButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsMenuOpen
    {
        get => (bool)GetValue(IsMenuOpenProperty);
        set => SetValue(IsMenuOpenProperty, value);
    }

    /// <summary>Sol yarıma tıklandı (komut vermeyen çağıranlar için).</summary>
    public event RoutedEventHandler? PrimaryClick;

    public SplitButton() => Loaded += (_, _) => SplitCorners();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild(PrimaryPart) is ButtonBase primary)
            primary.Click += (_, e) => PrimaryClick?.Invoke(this, e);
        SplitCorners();
    }

    /// <summary>
    /// İki yarımın köşelerini GÖVDENİN yarıçapından türetir: sol yarım yalnız SOL köşeleri, sağ yarım yalnız
    /// SAĞ köşeleri yuvarlar (BuildApp.jsx:1594 / :1596). Yarıçap <see cref="DsChrome.CornerRadiusProperty"/>
    /// üzerinden gelen TOKEN değeridir (<c>Radius.Sm</c>) — köşe başına literal yazmak token'ı yeniden
    /// yazmak olurdu; XAML tek bir köşeyi bir CornerRadius kaynağından türetemez, bu yüzden hesap burada.
    ///
    /// <para><c>Loaded</c>'da BİR KEZ DAHA çalışır: <c>DsChrome.CornerRadius</c> bir <c>{DynamicResource}</c>
    /// setter'ıdır ve <see cref="OnApplyTemplate"/> anında henüz çözülmemiş olabilir.</para>
    /// </summary>
    private void SplitCorners()
    {
        var radius = DsChrome.GetCornerRadius(this);
        if (GetTemplateChild(PrimaryPart) is FrameworkElement primary)
            DsChrome.SetCornerRadius(primary, new CornerRadius(radius.TopLeft, 0, 0, radius.BottomLeft));
        if (GetTemplateChild(MenuPart) is FrameworkElement menu)
            DsChrome.SetCornerRadius(menu, new CornerRadius(0, radius.TopRight, radius.BottomRight, 0));
    }
}
