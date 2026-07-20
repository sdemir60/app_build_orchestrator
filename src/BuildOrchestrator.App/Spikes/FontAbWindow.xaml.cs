using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuildOrchestrator.App.Spikes;

/// <summary>
/// [T65/K9] Font rasterization A/B karar penceresi: design-v1'deki gerçek metin örnekleri
/// (11px caps panel başlığı, 13px proje satırı, 12px konsol satırları) 4 kombinasyonda yan yana —
/// TextFormattingMode Display/Ideal × TextRenderingMode ClearType/Grayscale. Aynı örneklerin
/// tarayıcı referansı: .claude/temp/2026-07-18-23-45-t65-font-ab-reference.html (aynı OTF dosyaları).
/// Karar sonrası kazanan kombinasyon MainWindow köküne sabitlenir; bu pencere --font-ab arg'ı olmadan açılmaz.
/// </summary>
public partial class FontAbWindow : Window
{
    // Gömülü fontlar — ConsoleView ile aynı pack URI kalıbı.
    private static readonly Uri FontsBase = new("pack://application:,,,/BuildOrchestrator.App;component/Fonts/");
    private static readonly FontFamily Sans = new(FontsBase, "./#Geist");
    private static readonly FontFamily Mono = new(FontsBase, "./#Geist Mono");
    private static readonly FontFamily MonoConsole = new(FontsBase, "./#Geist Mono Console");

    // design-v1 renk token'ları (tokens/colors.css).
    private static readonly Brush TextPrimary = Frozen("#ededee");
    private static readonly Brush TextSecondary = Frozen("#a9a9b0");
    private static readonly Brush TextDim = Frozen("#76767e");
    private static readonly Brush TextFaint = Frozen("#54545c");
    private static readonly Brush AmberText = Frozen("#f1ab2e");
    private static readonly Brush SuccessText = Frozen("#58cb80");
    private static readonly Brush FailText = Frozen("#ff706a");
    private static readonly Brush WarnText = Frozen("#f0853f"); // ConsoleLine warn = --status-cycle-text
    private static readonly Brush Surface = Frozen("#141417");
    private static readonly Brush ConsoleBg = Frozen("#060608");
    private static readonly Brush BorderSubtle = Frozen("#1c1c20");
    private static readonly Brush BorderStrong = Frozen("#2a2a30");

    public FontAbWindow()
    {
        InitializeComponent();
        AddQuadrant(0, 0, TextFormattingMode.Display, TextRenderingMode.ClearType);
        AddQuadrant(0, 2, TextFormattingMode.Display, TextRenderingMode.Grayscale);
        AddQuadrant(2, 0, TextFormattingMode.Ideal, TextRenderingMode.ClearType);
        AddQuadrant(2, 2, TextFormattingMode.Ideal, TextRenderingMode.Grayscale);
    }

    private void AddQuadrant(int row, int col, TextFormattingMode fmt, TextRenderingMode render)
    {
        var panel = new StackPanel();

        // Kombinasyon etiketi — kendisi de test ayarlarıyla render edilir.
        panel.Children.Add(new TextBlock
        {
            Text = $"TextFormattingMode={fmt} · TextRenderingMode={render}",
            FontFamily = Mono,
            FontSize = 12,
            Foreground = AmberText,
            Margin = new Thickness(0, 0, 0, 10),
        });

        // ---- Örnek 1: 11px caps panel başlığı (PanelHead: 500 / +0.07em / uppercase / text-faint, 28px şerit) ----
        var panelHead = new Border
        {
            Height = 28,
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = Row(
                CapsLabel("PROJECTS", 11, TextFaint),
                HSpace(16),
                CapsLabel("EVENT STREAM", 11, TextFaint),
                HSpace(16),
                CapsLabel("DEPENDENCY GRAPH", 11, TextFaint)),
        };
        panel.Children.Add(panelHead);

        // ---- Örnek 2: 13px proje satırları (ProjectRow: ad 13/500, sln 12/faint, süre mono 12 tabular) ----
        panel.Children.Add(ProjectRow("OSYS.Sales.Core", "Osys.Sales.sln", "4.2s", dim: false));
        panel.Children.Add(ProjectRow("OSYS.Domain.Parts", "Osys.Parts.sln", "—", dim: true));

        // 13px SemiBold ayrışma örneği + 12px mono tabular metrik satırı.
        panel.Children.Add(new TextBlock
        {
            Text = "Build Orchestrator — OSYS workspace",
            FontFamily = Sans,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 10, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "elapsed 00:41 · eta ~2:10 · 118/191 succeeded",
            FontFamily = Mono,
            FontSize = 12,
            Foreground = TextSecondary,
            Margin = new Thickness(0, 0, 0, 10),
        });

        // ---- Örnek 3: konsol satırları (mono 12, satır kutusu 12×1.55=18.6 DIP, console-bg) ----
        var console = new StackPanel();
        console.Children.Add(ConsoleLine("msbuild OSYS.Sales.Core.csproj /t:Build /p:Configuration=Debug /m /nr:false", TextPrimary, cmd: true));
        console.Children.Add(ConsoleLine("ResolveReferences: 3 project references resolved", TextFaint));
        console.Children.Add(ConsoleLine("Restore: packages up to date (0.4s)", TextFaint));
        console.Children.Add(ConsoleLine("CoreCompile: 128 files → obj/Debug/net8.0/OSYS.Sales.Core.dll", TextSecondary));
        console.Children.Add(ConsoleLine("warning CS8618: non-nullable field '_ctx' may be null when exiting constructor", WarnText));
        console.Children.Add(ConsoleLine("Analyzer: Delta.CodeRules 2 rules, 0 violations", TextFaint));
        console.Children.Add(ConsoleLine("SalesContract.cs(129,17): error CS0246: the type or namespace name 'ICampaignService' could not be found", FailText));
        console.Children.Add(ConsoleLine("Build succeeded — 0 errors, 1 warnings (4.2s)", SuccessText));
        panel.Children.Add(new Border
        {
            Background = ConsoleBg,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = console,
        });

        var quadrant = new Border
        {
            BorderBrush = BorderStrong,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = panel,
        };
        TextOptions.SetTextFormattingMode(quadrant, fmt);
        TextOptions.SetTextRenderingMode(quadrant, render);
        Grid.SetRow(quadrant, row);
        Grid.SetColumn(quadrant, col);
        Root.Children.Add(quadrant);
    }

    private static FrameworkElement ProjectRow(string name, string solution, string duration, bool dim)
    {
        var row = new DockPanel { Height = 36, LastChildFill = true };
        var dur = new TextBlock
        {
            Text = duration,
            FontFamily = Mono,
            FontSize = 12,
            Foreground = TextDim,
            MinWidth = 52,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dur, Dock.Right);
        row.Children.Add(dur);

        var text = Row(
            new TextBlock
            {
                Text = name,
                FontFamily = Sans,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = dim ? TextDim : TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
            },
            HSpace(8),
            new TextBlock
            {
                Text = solution,
                FontFamily = Sans,
                FontSize = 12,
                Foreground = TextFaint,
                VerticalAlignment = VerticalAlignment.Center,
            });
        row.Children.Add(text);

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = row,
        };
    }

    private static FrameworkElement ConsoleLine(string text, Brush color, bool cmd = false)
    {
        // CSS satır kutusu karşılığı: line-height 1.55 @12px = 18.6 DIP sabit kutu + dikey ortalama
        // (tek satırlık TextBlock'ta WPF LineHeight kullanılmaz — feasibility §3.1).
        var line = new TextBlock
        {
            FontFamily = MonoConsole,
            FontSize = 12,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (cmd)
        {
            line.Inlines.Add(new System.Windows.Documents.Run("▸ ") { Foreground = AmberText });
        }
        line.Inlines.Add(new System.Windows.Documents.Run(text));
        return new Grid { Height = 18.6, Children = { line } };
    }

    /// <summary>0.07em tracking yaklaşığı: karakter başına ayrı TextBlock + sağ boşluk. Üretim çözümü
    /// TrackedTextBlock'tur (T57); burada amaç yalnız rasterization karşılaştırması. Kesirli aralığın
    /// tam piksele yuvarlanıp bozulmaması için bu alt ağaçta layout rounding kapalı.</summary>
    private static FrameworkElement CapsLabel(string text, double size, Brush brush)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            UseLayoutRounding = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var ch in text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = ch.ToString(),
                FontFamily = Sans,
                FontSize = size,
                FontWeight = FontWeights.Medium,
                Foreground = brush,
                Margin = new Thickness(0, 0, size * 0.07, 0),
            });
        }
        return panel;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static FrameworkElement HSpace(double width) => new Border { Width = width };

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
