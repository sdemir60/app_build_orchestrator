using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.Views;

/// <summary>Bir <see cref="BranchPopover"/> satırının modeli: branch'in kendisi + o anki seçili olup olmadığı.
/// Seçim satırın DIŞINDA hesaplanır (<see cref="RunViewModel.Branch"/> ile karşılaştırma popover'ındır) —
/// satır yalnız kendisine verileni çizer, ata ağaçtan hiçbir şey ÇEKMEZ (<c>ProjectRow</c> deseni).</summary>
public sealed record BranchRowItem(BranchRef Branch, bool IsSelected);

/// <summary>
/// Branch popover'ının TEK satırı (BuildApp.jsx:859-867). Görseli KOD-tarafı kurulur — <see cref="IconVisual"/>
/// ve <see cref="HoverBackground"/> tek doğruluk kaynaklarıdır, bir DataTemplate onların markup'ını
/// kopyalamak zorunda kalırdı (kopya YASAK, CLAUDE.md).
///
/// <para><b>Neden ayrı bir kontrol:</b> satırlar eskiden popover'ın kendi kodunda üretilip bir
/// <c>StackPanel.Children</c>'a ekleniyordu; bu, listeyi sanallaştırılamaz kılıyordu — 475 branch'lik gerçek
/// bir repoda popover'ı açmak (ve arama kutusuna her harf) tüm satırları baştan kuruyor, UI thread'ini
/// yüzlerce ms bloke ediyordu. Satır bir <see cref="ContentControl"/> olunca sanallaştırılmış bir
/// <c>ItemsControl</c>'ün item container'ı olabilir: yalnız GÖRÜNÜR satırlar realize olur ve container'lar
/// geri dönüştürülür (<c>VirtualizationMode.Recycling</c> → <see cref="FrameworkElement.DataContextChanged"/>
/// ile yeniden çizilir).</para>
/// </summary>
public sealed class BranchRow : ContentControl
{
    /// <summary>BuildApp.jsx:859 satır yüksekliği. <b>Sanallaştırmanın kaydırma aritmetiği bunu sabit varsayar</b>
    /// (<c>ScrollUnit=Item</c>) — satır yüksekliği değişirse burası tek kaynaktır.</summary>
    internal const double RowHeight = 28;
    private const double RowGap = 8;
    private const double IconSlot = 12;

    public BranchRow()
    {
        Height = RowHeight;
        DataContextChanged += (_, _) => Rebuild();
    }

    private void Rebuild() => Content = DataContext is BranchRowItem item ? BuildVisual(item) : null;

    private FrameworkElement BuildVisual(BranchRowItem item)
    {
        var branch = item.Branch;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // ikon: seçilide amber ✓, değilse branch ikonu (BuildApp.jsx:863).
        var icon = IconVisual.Make(this, item.IsSelected ? "Icon.Check" : "Icon.Branch",
            item.IsSelected ? "Brush.AmberText" : "Brush.TextDim", IconSlot);
        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);

        var name = new TextBlock
        {
            Text = branch.Name,
            Margin = new Thickness(RowGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        name.SetResourceReference(TextBlock.ForegroundProperty,
            item.IsSelected ? "Brush.TextPrimary" : "Brush.TextSecondary");
        panel.Children.Add(name);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(panel, 0);
        grid.Children.Add(panel);

        // aktif branch → "active" rozeti; diğerleri → mono 7-hane SHA (BuildApp.jsx:865-867).
        FrameworkElement trailing = branch.IsActive ? ActiveBadge() : ShaText(branch.Sha);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(trailing);

        var row = new Border
        {
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        HoverBackground.Attach(row);
        return row;
    }

    private Border ActiveBadge()
    {
        var text = new TextBlock { Text = "active", Margin = new Thickness(5, 1, 5, 1) };
        text.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AmberText");
        var badge = new Border { BorderThickness = new Thickness(1), Child = text };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.AmberSoft");
        badge.SetResourceReference(Border.BorderBrushProperty, "Brush.AmberBorder");
        badge.SetResourceReference(Border.CornerRadiusProperty, "Radius.Xs");
        return badge;
    }

    private TextBlock ShaText(string sha)
    {
        var text = new TextBlock { Text = RunViewModel.Short7(sha), FontFamily = AppFonts.Mono };
        text.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        return text;
    }
}
