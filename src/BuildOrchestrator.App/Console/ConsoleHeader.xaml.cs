using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>[T56/3a] Konsol panel başlığının iki modu (design-v1 §2.5). Kod-tarafı sürülür (DP/binding şişkinliği
/// yerine küçük, test edilebilir yüzey): <see cref="ShowNarrative"/> / <see cref="ShowProjectLog"/> modu değiştirir,
/// <see cref="SetLineCount"/> sağdaki "N lines" sayacını günceller. Statü rengi token ANAHTARIndan
/// (<see cref="ConsoleStatus.BrushKey"/>) SetResourceReference ile canlı çözülür (hardcode YASAK).</summary>
public partial class ConsoleHeader : UserControl
{
    public enum HeaderMode { Narrative, ProjectLog }

    public ConsoleHeader()
    {
        InitializeComponent();
        ShowNarrative(0);
    }

    /// <summary>Test/okuma için mevcut mod.</summary>
    public HeaderMode Mode { get; private set; }

    /// <summary>Back ghost butonuna tıklandığında — MainWindow bunu <c>ShowRun</c>+reseed'e bağlar.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Anlatı modu: caps "CONSOLE" etiketi + N lines; proje başlığı öğeleri gizli.</summary>
    public void ShowNarrative(int lineCount)
    {
        Mode = HeaderMode.Narrative;
        ConsoleLabel.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        ProjectNameText.Visibility = Visibility.Collapsed;
        StatusGlyphText.Visibility = Visibility.Collapsed;
        StatusNameText.Visibility = Visibility.Collapsed;
        DepIssueBadge.Visibility = Visibility.Collapsed;
        SetLineCount(lineCount);
    }

    /// <summary>Proje-log modu: ← Back + proje adı (mono) + statü glyph/adı + (varsa) ▲ dependency issue + N lines.</summary>
    public void ShowProjectLog(string projectName, ProjectRowState state, bool hasDepIssue, int lineCount)
    {
        Mode = HeaderMode.ProjectLog;
        ConsoleLabel.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;

        ProjectNameText.Text = projectName;
        ProjectNameText.Visibility = Visibility.Visible;

        StatusGlyphText.Text = ConsoleStatus.Glyph(state);
        StatusGlyphText.SetResourceReference(ForegroundProperty, ConsoleStatus.BrushKey(state));
        StatusGlyphText.Visibility = Visibility.Visible;

        StatusNameText.Text = ConsoleStatus.Name(state);
        StatusNameText.SetResourceReference(ForegroundProperty, ConsoleStatus.BrushKey(state));
        StatusNameText.Visibility = Visibility.Visible;

        DepIssueBadge.Visibility = hasDepIssue ? Visibility.Visible : Visibility.Collapsed;
        SetLineCount(lineCount);
    }

    /// <summary>Sağdaki mono "N lines" sayacı — TAM tampon uzunluğu (render dilimi DEĞİL, Ek A #23).</summary>
    public void SetLineCount(int lineCount) => LinesText.Text = $"{lineCount} lines";

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
