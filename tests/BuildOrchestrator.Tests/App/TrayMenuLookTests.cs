using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray menu look] Tepsinin sağ tık menüsü uygulamanın diliyle çizilir: proje satırının menüsüyle (Build · Rebuild · Clean) AYNI
/// popover kabuğu, AYNI satırlar (26 px, 13 px ikon, 8 px boşluk, 7 px iç pay, 154 px genişlik), AYNI vurgu (zemin bir ton
/// <c>surface-raised</c>) ve AYNI pasif opaklık. Eskiden WPF'in hazır menüsüydü — açık gri zemin, sistem yazısı, mavi vurgu, ikon yok —
/// ve koyu uygulamanın yanında yabancı duruyordu.
///
/// <para>Ölçüler satır menüsünün kaynağından elle türetilmiş sabitlerdir (design-v1.11.0 BuildApp.jsx:599-614), üretim kodundan
/// okunmaz.</para>
///
/// <para><b>Realize kök olarak yapılır:</b> menü <see cref="AppTrayIcon.CreateMenu"/>'den gelir, uygulamanın kaynak zinciri menünün KENDİ
/// sözlüğüne birleştirilir, şablon uygulanır ve menü ölçülüp yerleştirilir. Bir host'a konamaz: WPF bir <see cref="ContextMenu"/>'nün
/// yalnız kendi popup'ında bir üst öğesi olmasına izin verir (<c>ElementMustBeInPopup</c>), o popup da ancak menü gerçekten açılınca
/// (pencereyle) kurulur. Kök realize şablonu ve kaynak çözümlemesini yine çalışma anında sınar — headless süit XAML'i başka türlü
/// görmez.</para>
/// </summary>
[Collection("Console UI (serial)")]
public sealed class TrayMenuLookTests
{
    /// <summary>Durdurulacak bir koşu varmış gibi: Stop maddesi etkin.</summary>
    private static readonly ICommand LiveStop = new RelayCommand(() => { }, () => true);

    private static ContextMenu Realize(ICommand stop)
    {
        var menu = AppTrayIcon.CreateMenu(stop, () => { });
        menu.Resources.MergedDictionaries.Add(DsResources.NewScope());
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        return menu;
    }

    private static MenuItem StopItem(ContextMenu menu) => menu.Items.Cast<MenuItem>().Single(i => i.Command is not null);

    private static MenuItem ExitItem(ContextMenu menu) => menu.Items.Cast<MenuItem>().Single(i => i.Command is null);

    private static Color? ColorOrNull(Brush? brush) => (brush as SolidColorBrush)?.Color;

    /// <summary>Maddenin başlığını çizen metin bloğu.</summary>
    private static TextBlock Label(MenuItem item) =>
        DsResources.Descendants(item).OfType<TextBlock>().Single(t => DsResources.DisplayedText(t) == (string)item.Header);

    /// <summary>Maddenin ikonunu çizen yol (ikon yoksa <c>null</c>).</summary>
    private static Path? IconPath(MenuItem item) =>
        item.Icon is DependencyObject icon ? DsResources.Descendants(icon).OfType<Path>().SingleOrDefault() : null;

    /// <summary>Kabuk popover'ın kabuğudur — surface-overlay, 1 px border-strong, radius-lg, 154 px — ama GÖLGESİZDİR. Popover'ın overlay
    /// gölgesi menünün penceresine sığmaz: WPF popup penceresini menünün kendi boyuna kurar, gölge pencere sınırında kırpılır ve yalnız
    /// alt köşelerde, yuvarlak kenarın dışında koyu bir leke olarak kalır (canlı ekran görüntüsünde ölçüldü: koyu masaüstünde köşe
    /// <c>(17,18,18)</c>, hemen yanındaki masaüstü <c>(25,26,27)</c>; açık bir pencere ya da açık temalı görev çubuğu üstünde gri
    /// köşeler olur). Uygulama içindeki popover'lar hep koyu zeminde durduğu için orada görünmez; tepsi menüsü ise herhangi bir zeminde
    /// açılır. Gölgeye pay bırakmak menünün köşesini imleçten uzaklaştırırdı.</summary>
    [StaFact]
    public void The_tray_menu_is_drawn_on_the_popover_shell()
    {
        var menu = Realize(LiveStop);

        var shell = DsResources.Descendants(menu).OfType<Border>().FirstOrDefault();
        Assert.NotNull(shell);

        Assert.Equal(DsResources.TokenColor(menu, "Brush.SurfaceOverlay"), ColorOrNull(shell.Background));
        Assert.Equal(DsResources.TokenColor(menu, "Brush.BorderStrong"), ColorOrNull(shell.BorderBrush));
        Assert.Equal(new Thickness(1), shell.BorderThickness);
        Assert.Equal((CornerRadius)menu.FindResource("Radius.Lg"), shell.CornerRadius);
        Assert.Null(shell.Effect);
        Assert.Equal(154, menu.ActualWidth);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(menu));
    }

    /// <summary>Satır: 26 px; başlık 13 px'lik <c>FontSize.Sm</c>, <c>text-secondary</c>; ikon kutusu 13 px; başlık 7 px iç pay + 13 px
    /// ikon yuvası + 8 px boşluk sonra, yani maddenin sol kenarından 28 px içeride başlar.</summary>
    [StaFact]
    public void Tray_menu_rows_have_the_row_menus_measures()
    {
        var menu = Realize(LiveStop);

        foreach (var item in menu.Items.Cast<MenuItem>())
        {
            Assert.Equal(26, item.ActualHeight);
            var label = Label(item);
            Assert.Equal((double)menu.FindResource("FontSize.Sm"), label.FontSize);
            Assert.Equal(DsResources.TokenColor(menu, "Brush.TextSecondary"), ColorOrNull(label.Foreground));
            Assert.Equal(28, DsResources.BoundsIn(label, item).X, precision: 1);
            var icon = Assert.IsAssignableFrom<FrameworkElement>(item.Icon);
            Assert.Equal(13, icon.ActualWidth);
            Assert.Equal(13, icon.ActualHeight);
        }
    }

    /// <summary>Stop, uygulamadaki her Stop gibi kırmızı dolu kareyi taşır (action bar'ın Stop düğmesi ve satırın Stop ikonu:
    /// <c>Icon.Stop</c>, <c>status-fail-text</c>); Exit, title bar'daki Exit düğmesinin power işaretini menü satırı ikonlarının
    /// <c>text-dim</c> rengiyle taşır.</summary>
    [StaFact]
    public void Stop_shows_the_red_stop_square_and_exit_the_power_mark()
    {
        var menu = Realize(LiveStop);

        var stop = IconPath(StopItem(menu));
        Assert.NotNull(stop);
        Assert.Same(menu.FindResource("Icon.Stop"), stop.Data);
        Assert.Equal(DsResources.TokenColor(menu, "Brush.StatusFailText"), ColorOrNull(stop.Fill));

        var exit = IconPath(ExitItem(menu));
        Assert.NotNull(exit);
        Assert.Same(menu.FindResource("Icon.Power"), exit.Data);
        Assert.Equal(DsResources.TokenColor(menu, "Brush.TextDim"), ColorOrNull(exit.Stroke));
    }

    /// <summary>Vurgulanan madde — fareyle üstüne gelinen ya da klavyeyle seçilen; WPF ikisini de <see cref="MenuItem.IsHighlighted"/>
    /// ile bildirir — zeminini satır menüsünün hover'ı gibi bir ton <c>surface-raised</c>'e taşır (DS'in 120 ms'lik geçişiyle); vurgu
    /// kalkınca zemin saydama döner.</summary>
    [StaFact]
    public void A_highlighted_item_steps_onto_surface_raised()
    {
        var menu = Realize(LiveStop);
        var exit = ExitItem(menu);
        var highlighted = typeof(MenuItem).GetProperty(nameof(MenuItem.IsHighlighted))!;

        Assert.Equal(Colors.Transparent, ColorOrNull(DsTransition.GetAnimatedBackground(exit)));

        highlighted.SetValue(exit, true);
        Assert.Equal(DsResources.TokenColor(menu, "Brush.SurfaceRaised"), ColorOrNull(DsTransition.GetAnimatedBackground(exit)));

        highlighted.SetValue(exit, false);
        Assert.Equal(Colors.Transparent, ColorOrNull(DsTransition.GetAnimatedBackground(exit)));
    }

    /// <summary>Durdurulacak bir şey yokken Stop, satır menüsünün meşgul maddeleri gibi 0,45 opaklığa söner; Exit tam opak kalır.</summary>
    [StaFact]
    public void Stop_with_nothing_to_stop_fades_like_the_row_menus_busy_items()
    {
        var menu = Realize(AppTrayIcon.NoRunToStop);

        var stop = StopItem(menu);
        Assert.False(stop.IsEnabled);
        Assert.Equal(0.45, stop.Opacity);
        Assert.Equal(1.0, ExitItem(menu).Opacity);
    }
}
