using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 fix-1 · S2] Bir <see cref="DsSplitter"/>'ı gerçek bir 3-kolonlu <see cref="Grid"/> + token'lı kaynak
/// kapsamında realize etmenin TEK yeri. Aynı 12 satırlık kurulum <c>E5FoldTests</c> (klavye resize persist) ve
/// <c>SplitterDragTests</c> (sürükleme rengi) içinde birebir kopyalanmıştı — kopya YASAK (CLAUDE.md).
///
/// <para>Topoloji kasıtlı: <c>*</c> · <c>Auto</c> · <c>*</c> — iki star-kolon genişliği paylaşır (ayraç ~7px),
/// yani <c>ColumnDefinitions[0].ActualWidth</c> resize'ı GERÇEKTEN yansıtır (persist yolunun okuduğu değer).</para>
/// </summary>
internal static class SplitterHost
{
    /// <summary>Realize edilmiş ayraç + onu taşıyan grid + token kapsamı (host) + canlı tutulması gereken pencere.</summary>
    internal sealed record Scaffold(DsSplitter Splitter, Grid Grid, Border Host, Window Window);

    public static Scaffold ThreeColumnGrid(
        SplitterLine orientation = SplitterLine.Vertical, double width = 400, double height = 200)
    {
        var grid = new Grid { Width = width, Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var splitter = new DsSplitter { LineOrientation = orientation };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, grid);
        return new Scaffold(splitter, grid, host, window);
    }
}
