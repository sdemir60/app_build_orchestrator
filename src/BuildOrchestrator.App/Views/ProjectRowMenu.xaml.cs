using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.11.0 §2.4-4 · §9-6] Proje satırının menüsü: <b>Build · Rebuild · Clean</b>. Hover'daki ⋯
/// düğmesi ve satıra <b>sağ tık</b> AYNI menüyü açar.
///
/// <para><b>İkon ailesi Build menüsüyle ORTAKTIR</b> — <c>play · rotate-cw · brush</c>, tek grid/stroke.
/// Eşleme <see cref="BuildMenu.IconKeyFor"/>'dan okunur; burada ikinci bir tablo YAZILMAZ (kopya YASAK).</para>
///
/// <para><b>Arka uç henüz yazılmadı</b> (§3.8 tek-proje koşusu motorda yok): maddeler tasarımdaki yerlerinde
/// ama PASİFTİR ve tooltip nedeni söyler — bakım kutusunun Clean/Optimize düğmeleriyle AYNI karar. Menünün
/// kendisi açılır, çünkü tasarımın akışı (sağ tık → menü) ancak böyle görünür.</para>
/// </summary>
public partial class ProjectRowMenu : UserControl
{
    // design-v1.11.0 BuildApp.jsx:608-613 satır ölçüleri — bileşenin KENDİ değerleri, token DEĞİL.
    private const double RowHeight = 26;
    private const double IconSlot = 13;
    private const double RowGap = 8;

    /// <summary>Menünün maddeleri — Build menüsüyle AYNI üçlü (aynı <c>Kind</c> anahtarları, aynı ikonlar).</summary>
    internal static readonly IReadOnlyList<(string Kind, string Label)> Items =
        [("build", "Build"), ("rebuild", "Rebuild"), ("clean", "Clean")];

    public ProjectRowMenu()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    private bool _built;

    /// <summary>Menünün başlığı — projenin KISA adı (ortak önek atılmış).</summary>
    internal string Title
    {
        get => PART_Title.Text;
        set => PART_Title.Text = value;
    }

    /// <summary>[test yüzeyi] Çizilmiş satırlar — pasiflik ve tooltip buradan okunur.</summary>
    internal IEnumerable<Border> Rows => PART_Rows.Children.Cast<Border>();

    /// <summary>[D6] Menü her açılışında 140ms pop-in (BuildApp.jsx:597 <c>bo-pop-in</c>).</summary>
    public void PlayPopIn() => PopIn.Play(PART_Rows);

    private void Build()
    {
        if (_built) return;
        _built = true;
        foreach (var (kind, label) in Items) PART_Rows.Children.Add(BuildRow(kind, label));
    }

    private Border BuildRow(string kind, string label)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSlot) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = IconVisual.Make(this, BuildMenu.IconKeyFor(kind), "Brush.TextDim", IconSlot);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(RowGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(FontSizeProperty, "FontSize.Sm");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var row = new Border
        {
            Height = RowHeight,
            Padding = new Thickness(7, 0, 7, 0),
            Child = grid,
            // Arka uç yok → pasif. Hover zemini de takılmaz: tıklanabilirmiş gibi görünmesi, basılıp hiçbir
            // şey olmamasından daha kötü olurdu (BuildMenu'nün Clean maddesiyle AYNI karar).
            IsEnabled = false,
            Opacity = BuildMenu.DisabledOpacity,
            Cursor = Cursors.Arrow,
            ToolTip = AccessibilityNames.RowActionsTooltip,
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        ToolTipService.SetShowOnDisabled(row, true); // pasif kontrolde WPF tooltip'i varsayılan olarak saklar
        return row;
    }
}
