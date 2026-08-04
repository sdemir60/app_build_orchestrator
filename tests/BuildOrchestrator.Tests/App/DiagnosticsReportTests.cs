using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Tanı raporu SAF: Environment sekmesinin çizdiği satırlar ile "Copy diagnostics"in panoya yazdığı metin
/// AYNI modelden üretilir (iki ayrı liste sessizce ayrışırdı). Etiket metinleri burada tanımlanır — XAML
/// onları tekrar yazmaz, satırları bir <c>ItemsControl</c> olarak çizer.
/// </summary>
public class DiagnosticsReportTests
{
    private static DiagnosticsInput Full() => new(
        AppVersion: "1.0.0+it5",
        EngineVersion: "1.0.0+it5",
        EnginePid: 4242,
        Runtime: ".NET 10.0.0",
        Os: "Microsoft Windows 10.0.26200",
        MsBuild: @"C:\VS\MSBuild.exe (v17.9.8)",
        RepositoryRoot: @"D:\repo",
        StateFile: @"C:\state\ui-state.json",
        LogsRoot: @"C:\state\logs",
        WorktreePool: @"C:\state\worktrees");

    [Fact]
    public void Every_input_field_reaches_exactly_one_line()
    {
        var lines = DiagnosticsReport.Compose(Full());

        // Etiketler TEKİL, değerler dolu.
        Assert.Equal(lines.Select(l => l.Label).Distinct(StringComparer.Ordinal).Count(), lines.Count);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Label)));
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Value)));

        // Girdideki her değer çıktıda GÖRÜNÜR — bir alanı satıra bağlamayı unutmak burada yakalanır.
        var values = lines.Select(l => l.Value).ToList();
        foreach (string expected in new[]
                 {
                     "1.0.0+it5", "4242", ".NET 10.0.0", "Microsoft Windows 10.0.26200",
                     @"C:\VS\MSBuild.exe (v17.9.8)", @"D:\repo",
                     @"C:\state\ui-state.json", @"C:\state\logs", @"C:\state\worktrees",
                 })
            Assert.Contains(values, v => v.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>Motor doğmamışken satır KAYBOLMAZ — kullanıcı "motor yok" bilgisini de görmeli.</summary>
    [Fact]
    public void A_missing_engine_reads_as_not_started_instead_of_disappearing()
    {
        var lines = DiagnosticsReport.Compose(Full() with { EngineVersion = null, EnginePid = null });

        Assert.Equal(DiagnosticsReport.Compose(Full()).Count, lines.Count);
        Assert.Contains(lines, l => l.Value == DiagnosticsReport.NotStarted);
        Assert.Contains(lines, l => l.Value == DiagnosticsReport.Unknown);
    }

    [Fact]
    public void An_empty_repository_root_reads_as_no_repository()
        => Assert.Contains(DiagnosticsReport.Compose(Full() with { RepositoryRoot = "" }),
            l => l.Value == DiagnosticsReport.NoRepository);

    [Fact]
    public void The_clipboard_text_carries_every_line_as_label_and_value()
    {
        var lines = DiagnosticsReport.Compose(Full());
        string text = DiagnosticsReport.ToText(lines);

        foreach (var line in lines)
        {
            Assert.Contains(line.Label, text, StringComparison.Ordinal);
            Assert.Contains(line.Value, text, StringComparison.Ordinal);
        }
        // Satır başına bir satır — pano metni yapıştırılabilir olmalı.
        Assert.Equal(lines.Count, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>Değerler AYNI kolonda başlar: bir destek talebine yapıştırıldığında okunabilir olmalı.</summary>
    [Fact]
    public void The_clipboard_text_aligns_the_values_in_one_column()
    {
        var lines = DiagnosticsReport.Compose(Full());
        var rows = DiagnosticsReport.ToText(lines).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var columns = rows.Zip(lines, (row, line) => row.IndexOf(line.Value, StringComparison.Ordinal))
                          .Distinct()
                          .ToList();

        Assert.All(columns, c => Assert.True(c > 0, "değer satırın başında — etiket kaybolmuş"));
        Assert.Single(columns);
    }

    [Fact]
    public void An_empty_line_set_produces_empty_text_instead_of_throwing()
        => Assert.Equal("", DiagnosticsReport.ToText([]));
}
