using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.19.0 §2.9/§2.10/§2.11 · prototip <c>DialogShell</c>] Üç modalın (Settings · About · What's new)
/// ORTAK kabuğu. Scrim, <c>Ds.Dialog</c> çerçevesi, köşelerde kırpılan içerik, <c>head · tabs · gövde · footer</c>
/// dikey dizilimi, host kelepçesi, giriş animasyonu, odak tuzağı ve kapanma davranışı (scrim tıklaması, Esc)
/// BURADA bir kez yaşar; dialoglar yalnız içeriklerini ve ölçülerini verir (kopya YASAK, CLAUDE.md — kaynak
/// guard'ı: <c>DialogShellTests.Dialog_files_do_not_carry_their_own_copy_of_the_shell</c>).
///
/// <para><b>Görünüm</b> <c>Controls.xaml</c>'deki <c>Ds.ModalDialog</c> şablonundadır; bu sınıf onu kurucuda
/// kaynak referansıyla bağlar (türetilmiş XAML kökleri örtük stil ALMAZ — örtük stil yalnız TAM tipe
/// uygulanır). Dialog XAML'i kökünde <c>controls:ModalDialog</c> olarak durur; <see cref="Head"/>,
/// <see cref="Tabs"/> ve <see cref="Footer"/> özellik elemanlarıyla, gövde ise doğrudan içerik olarak verilir.</para>
///
/// <para><b>Mantıksal çocuklar:</b> <see cref="Head"/>/<see cref="Tabs"/>/<see cref="Footer"/> düz nesne
/// DP'leridir ve şablondaki bir ContentPresenter'da çizilir — mantıksal ebeveynleri bu kontrol olarak
/// KAYDEDİLİR (<c>HeaderedContentControl</c> deseni). Aksi halde içlerindeki <c>ElementName</c> bağları ve
/// <c>x:Name</c> aramaları dialog XAML'inin isim kapsamı yerine şablonun kapsamına düşerdi.</para>
/// </summary>
[TemplatePart(Name = ScrimPart, Type = typeof(Grid))]
[TemplatePart(Name = FramePart, Type = typeof(Border))]
public class ModalDialog : UserControl
{
    private const string ScrimPart = "PART_Scrim";
    private const string FramePart = "PART_Frame";

    private Grid? _scrim;
    private Border? _frame;

    public static readonly DependencyProperty HeadProperty = DependencyProperty.Register(
        nameof(Head), typeof(object), typeof(ModalDialog), new PropertyMetadata(null, OnSlotChanged));

    public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(
        nameof(Tabs), typeof(object), typeof(ModalDialog), new PropertyMetadata(null, OnSlotChanged));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(ModalDialog), new PropertyMetadata(null, OnSlotChanged));

    /// <summary>Tasarım genişliği — host'a sığmazsa <see cref="DialogSize.Clamp"/> ile daralır.</summary>
    public static readonly DependencyProperty DialogWidthProperty = DependencyProperty.Register(
        nameof(DialogWidth), typeof(double), typeof(ModalDialog), new PropertyMetadata(double.NaN));

    /// <summary>Tasarım yüksekliği; <c>NaN</c> (varsayılan) = içerikten doğar. Her iki durumda da üst sınır
    /// <see cref="DialogSize.Clamp"/>'tir.</summary>
    public static readonly DependencyProperty DialogHeightProperty = DependencyProperty.Register(
        nameof(DialogHeight), typeof(double), typeof(ModalDialog), new PropertyMetadata(double.NaN));

    public ModalDialog()
    {
        // Üç dialogun ortak başlangıç durumu: kapalı ve kendi Esc'ini yakalayabilecek şekilde odaklanabilir.
        Visibility = Visibility.Collapsed;
        Focusable = true;
        SetResourceReference(StyleProperty, "Ds.ModalDialog");
        SizeChanged += (_, _) => ApplyHostClamp();
    }

    public object? Head { get => GetValue(HeadProperty); set => SetValue(HeadProperty, value); }
    public object? Tabs { get => GetValue(TabsProperty); set => SetValue(TabsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public double DialogWidth { get => (double)GetValue(DialogWidthProperty); set => SetValue(DialogWidthProperty, value); }
    public double DialogHeight { get => (double)GetValue(DialogHeightProperty); set => SetValue(DialogHeightProperty, value); }

    /// <summary>[test yüzeyi] Full-bleed scrim — odak kapsamı ve Cycle gezinme kabı. Şablon henüz uygulanmadıysa
    /// (dialog hiç açılmamış, Collapsed) burada uygulanır.</summary>
    internal Grid Scrim
    {
        get
        {
            ApplyTemplate();
            return _scrim ?? throw new InvalidOperationException("Ds.ModalDialog template is not applied.");
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_scrim is not null) _scrim.MouseLeftButtonDown -= OnScrimPressed;
        if (_frame is not null)
        {
            _frame.MouseLeftButtonDown -= OnFramePressed;
            _frame.SizeChanged -= OnFrameSizeChanged;
        }

        _scrim = GetTemplateChild(ScrimPart) as Grid;
        _frame = GetTemplateChild(FramePart) as Border;

        if (_scrim is not null) _scrim.MouseLeftButtonDown += OnScrimPressed;
        if (_frame is not null)
        {
            _frame.MouseLeftButtonDown += OnFramePressed;
            _frame.SizeChanged += OnFrameSizeChanged;
        }
        ApplyHostClamp();
    }

    /// <summary>Dialogu gösterir: görünür kılar, girişi oynatır (180ms fade + 6px — reduced-motion'da SNAP) ve
    /// odağı dialogun İÇİNE taşır. Türetilmiş dialog içeriğini kurduktan SONRA çağırır.
    ///
    /// <para><b>Yerleşim, odak taşınmadan önce tamamlanır (ölçüldü):</b> yeni görünür olmuş ama hiç
    /// yerleşmemiş bir alt ağaçta <c>MoveFocus(First)</c> aday bulamaz ve odak UserControl'ün kendisinde
    /// kalır (bkz. <c>DialogShellTests.Opening_any_dialog_moves_keyboard_focus_inside_it</c>).</para></summary>
    protected void ShowDialog()
    {
        Visibility = Visibility.Visible;
        ApplyTemplate();
        UpdateLayout();
        if (_frame is not null) PopIn.PlayDialog(_frame);
        Focus(); // Esc HER durumda yakalanabilsin (MoveFocus altta bir şey bulamazsa bile odak burada kalır)
        _scrim?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>Dialogu kapatır — Close/Cancel düğmeleri, scrim, Esc ve MainWindow'un Esc güvenlik ağı (odak
    /// dialog dışındayken) hep BU yoldan geçer.</summary>
    public void CloseDialog()
    {
        OnDialogClosing();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Kapanmadan hemen önce çağrılır (ör. Settings'in geri bildirim/Clear durumunu sıfırlaması).</summary>
    protected virtual void OnDialogClosing()
    {
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CloseDialog(); e.Handled = true; }
    }

    // Scrim tıklaması kapatır; dialogun kendi içine basış scrim'e ULAŞMAZ.
    private void OnScrimPressed(object sender, MouseButtonEventArgs e) => CloseDialog();
    private void OnFramePressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ---------------------------------------------------------------- host kelepçesi + köşe clip'i

    /// <summary>Prototipin <c>maxWidth/maxHeight: calc(100% - 48px)</c>'i: host bu kontrolün kendi alanıdır
    /// (scrim tüm pencereyi kaplar). Pencere yeniden boyutlandıkça <see cref="FrameworkElement.SizeChanged"/>
    /// ile yeniden uygulanır; henüz yerleşmemiş (0) bir host üst sınır KOYMAZ.</summary>
    private void ApplyHostClamp()
    {
        if (_frame is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        _frame.MaxWidth = DialogSize.Clamp(DialogWidth, ActualWidth);
        _frame.MaxHeight = DialogSize.Clamp(DialogHeight, ActualHeight);
    }

    /// <summary>İçerik çerçevenin yuvarlak köşesinde KIRPILIR (CSS <c>overflow: hidden</c>): düz
    /// <c>ClipToBounds</c> dikdörtgendir, Settings rayının/footer'ın zemini köşeden taşardı. Yarıçap çerçevenin
    /// İÇ yarıçapıdır (radius − border) ve token'dan çözülmüş <see cref="Border.CornerRadius"/>'tan türetilir.</summary>
    private void OnFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_frame?.Child is not FrameworkElement content) return;
        double radius = Math.Max(0, _frame.CornerRadius.TopLeft - _frame.BorderThickness.Left);
        var clip = new RectangleGeometry(new Rect(0, 0, content.ActualWidth, content.ActualHeight), radius, radius);
        clip.Freeze();
        content.Clip = clip;
    }

    // ---------------------------------------------------------------- mantıksal çocuklar

    private static void OnSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dialog = (ModalDialog)d;
        dialog.RemoveLogicalChild(e.OldValue);
        dialog.AddLogicalChild(e.NewValue);
    }

    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            var inner = base.LogicalChildren;
            while (inner?.MoveNext() == true) children.Add(inner.Current);
            foreach (object? slot in new[] { Head, Tabs, Footer })
                if (slot is not null) children.Add(slot);
            return children.GetEnumerator();
        }
    }
}
