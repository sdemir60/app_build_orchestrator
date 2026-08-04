using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>[D6/T40] Build menüsünün TEK maddesi (saf model — koşullu kurulum + F5 rozetinin yeri view'siz de
/// doğrulanabilir). <paramref name="Kbd"/> <c>null</c> ⇒ rozet YOK.</summary>
public readonly record struct BuildMenuItem(string Kind, string Title, string Desc, string? Kbd);

/// <summary>
/// [D6/T40] Build split-button menüsü (BuildApp.jsx:1599-1612). DataContext bir <see cref="RunViewModel"/>'dir.
/// Maddeler VM durumuna göre KOŞULLU kurulur:
/// <list type="bullet">
///   <item><b>Continue</b> yalnız <c>stopped</c> — "{n} queued projects resume" — F5.</item>
///   <item><b>Build</b> her zaman — "Only changed projects" (stopped'ta "Start over — only changed projects",
///     Kbd KALDIRILIR) — F5 (yalnız NOT-stopped).</item>
///   <item><b>Rebuild</b> her zaman — "All {total} projects — cache ignored" — Ctrl+F5.</item>
///   <item><b>Retry failed</b> yalnız <c>failed&gt;0</c> — "{n} failed + dependents" — Kbd YOK.</item>
/// </list>
/// <b>Kbd rozetleri DISPLAY-ONLY (v7 K6):</b> gerçek global tuş yakalama E5'in işidir — burada jest bağlanmaz.
/// </summary>
public partial class BuildMenu : UserControl
{
    // BuildApp.jsx:1078 satır ölçüleri (token DEĞİL — bileşenin kendi değerleri, kaynak satırıyla yazılır).
    private const double RowGap = 10;       // gap: 10
    private const double IconSlot = 14;     // icon span width 14
    private const double TitleIconSize = 14;

    private RunViewModel? _vm;

    /// <summary>Bir madde seçildiğinde (komut çalıştıktan sonra) — ActionBar menüyü kapatır.</summary>
    public event Action? ItemInvoked;

    public BuildMenu()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RefreshRows();
    }

    /// <summary>[test yüzeyi] O anki (VM durumundan türetilmiş) menü modeli — koşullu maddeler + F5 rozetinin yeri.</summary>
    internal IReadOnlyList<BuildMenuItem> Items { get; private set; } = [];

    /// <summary>[D6] Menü her açılışında 140ms pop-in (BuildApp.jsx:33) — ActionBar, IsMenuOpen true olunca çağırır.</summary>
    public void PlayPopIn() => PopIn.Play(PART_Rows);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as RunViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        RefreshRows();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Menü içeriğini etkileyen sinyaller: faz (stopped), sayaçlar (total/failed), willBuild yüzeyi (remaining).
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Phase):
            case nameof(RunViewModel.Counters):
            case nameof(RunViewModel.WillBuildCount):
            case nameof(RunViewModel.FinishedOfWillBuild):
                RefreshRows();
                break;
        }
    }

    /// <summary>[T40] VM durumundan menü modelini kurar — koşullu maddeler + F5 rozetinin yeri.
    /// <para>[B4] <c>continue</c> maddesi KALDIRILDI: yarıda kalan bir run'ı sürdürme yüzeyi yok, Stop'tan sonra
    /// Build baştan koşar. Dolayısıyla F5 rozeti de her fazda Build'de KALIR (eskiden stopped'ta Continue'ya
    /// taşınıyordu). <paramref name="stopped"/> yalnız Build'in açıklamasını "Start over" önekiyle ayırmak için
    /// durur — o ayrım hâlâ doğru bilgi verir.</para></summary>
    internal static IReadOnlyList<BuildMenuItem> ComposeItems(bool stopped, int total, int failed)
    {
        var items = new List<BuildMenuItem>();
        items.Add(new("build", "Build",
            stopped ? "Start over — only changed projects" : "Only changed projects", "F5"));
        items.Add(new("rebuild", "Rebuild", Inv($"All {total} projects — cache ignored"), "Ctrl+F5"));
        if (failed > 0)
            items.Add(new("retry", "Retry failed", Inv($"{failed} failed + dependents"), null));
        return items;
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private void RefreshRows()
    {
        bool stopped = _vm?.Phase == AppPhase.Stopped; // BuildApp.jsx:1386
        int total = _vm?.Counters.Total ?? 0;
        int failed = _vm?.Counters.Failed ?? 0;

        Items = ComposeItems(stopped, total, failed);

        PART_Rows.Children.Clear(); // minik non-virtualized menü (StickyRibbon chip deseni)
        foreach (var item in Items) PART_Rows.Children.Add(BuildRow(item));
    }

    private Border BuildRow(BuildMenuItem item)
    {
        var grid = new Grid { Margin = new Thickness(0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSlot) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = IconVisual.Make(this, IconKey(item.Kind), "Brush.TextSecondary", TitleIconSize);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(RowGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = item.Title };
        title.SetResourceReference(FontSizeProperty, "FontSize.Sm");
        title.SetResourceReference(FontWeightProperty, "FontWeight.Emphasis");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        var desc = new TextBlock { Text = item.Desc, Margin = new Thickness(0, 1, 0, 0) };
        desc.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        text.Children.Add(title);
        text.Children.Add(desc);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (item.Kbd is { } kbd)
        {
            var badge = new ContentControl { Content = kbd, VerticalAlignment = VerticalAlignment.Center };
            if (TryFindResource("Ds.Kbd") is Style s) badge.Style = s;
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        // BuildApp.jsx:1078 padding '7px 8px', radius-sm, hover surface-raised.
        var row = new Border
        {
            Padding = new Thickness(8, 7, 8, 7),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        HoverBackground.Attach(row);
        string kind = item.Kind;
        row.MouseLeftButtonUp += (_, _) => Invoke(kind);
        return row;
    }

    private void Invoke(string kind)
    {
        var command = kind switch
        {
            "build" => _vm?.BuildCommand,
            "rebuild" => _vm?.RebuildCommand,
            "retry" => _vm?.RetryFailedCommand,
            _ => null,
        };
        ItemInvoked?.Invoke(); // menüyü kapat (BuildApp.jsx her maddede setBuildMenu(false))
        if (command is not null && command.CanExecute(null)) command.Execute(null);
    }

    private static string IconKey(string kind) => kind switch
    {
        "rebuild" => "Icon.Rot",   // BuildApp.jsx:1606 <I.rot/>
        "retry" => "Icon.Redo",    // BuildApp.jsx:1609 <I.redo/>
        _ => "Icon.Play",          // build <I.play/>
    };
}
