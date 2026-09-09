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
/// <para><b>Üçü de tek proje koşusuna bağlıdır</b> (§3.8): madde seçilince <see cref="ItemInvoked"/> türüyle
/// ateşlenir, komutu satır (<see cref="ProjectRow"/>) kendi VM'inin kimliğiyle çalıştırır — menü hangi
/// projeye ait olduğunu bilmez, satır bilir. Bir koşu uçuştayken üçü de pasifleşir ve nedenini söyler
/// (<see cref="SetRunActionsEnabled"/>; prototip <c>busy</c>). <b>Clean</b>, Visual Studio'nun proje
/// Clean'idir: yalnız o projede <c>msbuild /t:Clean</c> (bakım kutusundaki DERİN Clean'in yerine GEÇMEZ).</para>
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
    private bool _runActionsEnabled = true;
    private readonly List<Border> _runRows = [];

    /// <summary>Bir madde seçildi — argüman maddenin <c>Kind</c>'ıdır (<c>build</c>/<c>rebuild</c>). Satır menüyü
    /// kapatır ve komutu kendi projesiyle çalıştırır (BuildMenu'nün <c>ItemInvoked</c> deseni).</summary>
    internal event Action<string>? ItemInvoked;

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

    /// <summary>[design §3.8] Build/Rebuild maddelerinin kapısı — satır menüyü açarken koşu kapısından
    /// (<c>BuildProjectCommand.CanExecute</c>) okuyup buraya yazar. Kapalıyken maddeler pasiftir, prototipin
    /// <c>busy</c> opaklığını alır ve tooltip nedeni söyler; Clean bundan bağımsız hep pasiftir.</summary>
    internal void SetRunActionsEnabled(bool enabled)
    {
        _runActionsEnabled = enabled;
        foreach (var row in _runRows) ApplyRunActionState(row);
    }

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
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        ToolTipService.SetShowOnDisabled(row, true); // pasif kontrolde WPF tooltip'i varsayılan olarak saklar

        HoverBackground.Attach(row);
        row.MouseLeftButtonUp += (_, _) => ItemInvoked?.Invoke(kind);
        _runRows.Add(row);
        ApplyRunActionState(row);
        return row;
    }

    private void ApplyRunActionState(Border row)
    {
        bool enabled = _runActionsEnabled;
        row.IsEnabled = enabled;
        row.Opacity = enabled ? 1.0 : BuildMenu.DisabledOpacity; // prototip: busy ? 0.45 : 1
        row.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        row.ToolTip = enabled ? null : AccessibilityNames.BuildBusyTooltip;
    }
}
