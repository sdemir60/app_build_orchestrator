using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Tanı raporu SAF: About sekmesinin kimlik satırları, Environment sekmesinin iki grubu ve "Copy diagnostics"in
/// panoya yazdığı metin AYNI modelden üretilir (ayrı listeler sessizce ayrışırdı). Etiket metinleri burada
/// tanımlanır — XAML onları tekrar yazmaz, satırları <c>ItemsControl</c>'lerle çizer.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> ESKİ MODEL tek düz listeydi (<c>App version</c>,
/// <c>Engine version</c>, <c>Engine PID</c> … <c>Worktree pool</c>) ve pano metni o listenin tamamıydı. v1.19.0
/// About'u üç gruba ayırdı: kimlik (Version · Engine · Copyright — About sekmesi), RUNTIME ve PATHS (Environment
/// sekmesi). Sürüm satırları Environment'tan ÇIKTI (tekrar yok); pano metni artık
/// <c>Build Orchestrator {sürüm}</c> başlığı + hizalı <c>Engine</c> + Runtime + Paths satırlarıdır.</para>
/// </summary>
public class DiagnosticsReportTests
{
    private static DiagnosticsInput Full() => new(
        Product: "Build Orchestrator",
        Version: "1.0.0+it5",
        Copyright: "© 2026 Delta Yazılım",
        EngineVersion: "1.0.0+engine",
        EnginePid: 4242,
        Runtime: ".NET 10.0.0",
        Os: "Microsoft Windows 10.0.26200",
        MsBuild: @"C:\VS\MSBuild.exe (v17.9.8)",
        RepositoryRoot: @"D:\repo",
        StateFile: @"C:\state\ui-state.json",
        LogsRoot: @"C:\state\logs",
        WorktreePool: @"C:\state\worktrees");

    private static IEnumerable<DiagnosticsLine> AllLines(DiagnosticsSnapshot s) =>
        s.Identity.Concat(s.Runtime).Concat(s.Paths);

    [Fact]
    public void The_identity_group_is_version_engine_and_copyright_in_that_order()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["Version", "Engine", "Copyright"], s.Identity.Select(l => l.Label));
        Assert.Equal(["1.0.0+it5", "1.0.0+engine", "© 2026 Delta Yazılım"], s.Identity.Select(l => l.Value));
    }

    [Fact]
    public void The_runtime_group_is_engine_pid_dotnet_runtime_and_os()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["Engine PID", ".NET runtime", "OS"], s.Runtime.Select(l => l.Label));
        Assert.Equal(["4242", ".NET 10.0.0", "Microsoft Windows 10.0.26200"], s.Runtime.Select(l => l.Value));
    }

    [Fact]
    public void The_paths_group_is_msbuild_repository_state_logs_and_worktree_pool()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["MSBuild", "Repository root", "State file", "Logs", "Worktree pool"], s.Paths.Select(l => l.Label));
        Assert.Equal(
            [@"C:\VS\MSBuild.exe (v17.9.8)", @"D:\repo", @"C:\state\ui-state.json", @"C:\state\logs", @"C:\state\worktrees"],
            s.Paths.Select(l => l.Value));
    }

    /// <summary>Tekrar YOK: bir etiket üç grubun toplamında bir kez geçer — eski <c>App version</c>/<c>Engine
    /// version</c> satırları Environment'a geri sızarsa burada yakalanır.</summary>
    [Fact]
    public void No_label_appears_twice_and_the_old_version_rows_are_gone()
    {
        var labels = AllLines(DiagnosticsReport.Compose(Full())).Select(l => l.Label).ToList();

        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("App version", labels);
        Assert.DoesNotContain("Engine version", labels);
    }

    [Fact]
    public void The_group_titles_are_runtime_and_paths()
    {
        Assert.Equal("Runtime", DiagnosticsReport.RuntimeTitle);
        Assert.Equal("Paths", DiagnosticsReport.PathsTitle);
    }

    /// <summary>Motor doğmamışken satır KAYBOLMAZ — kullanıcı "motor yok" bilgisini de görmeli.</summary>
    [Fact]
    public void A_missing_engine_reads_as_not_started_instead_of_disappearing()
    {
        var s = DiagnosticsReport.Compose(Full() with { EngineVersion = null, EnginePid = null });

        Assert.Equal(DiagnosticsReport.NotStarted, s.Engine.Value);
        Assert.Equal(DiagnosticsReport.Unknown, s.Runtime.Single(l => l.Label == "Engine PID").Value);
    }

    [Fact]
    public void An_empty_repository_root_reads_as_no_repository()
        => Assert.Contains(DiagnosticsReport.Compose(Full() with { RepositoryRoot = "" }).Paths,
            l => l.Value == DiagnosticsReport.NoRepository);

    /// <summary>[v1.19.0 §2.10] Pano metni: ilk satır <c>{ürün} {sürüm}</c>, ardından hizalı <c>Engine</c> +
    /// Runtime + Paths satırları — satır başına bir satır. Telif panoya GİTMEZ (destek talebinde bilgi taşımaz).</summary>
    [Fact]
    public void The_clipboard_text_is_a_title_line_then_engine_runtime_and_paths()
    {
        var s = DiagnosticsReport.Compose(Full());
        var rows = DiagnosticsReport.ToText(s).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Build Orchestrator 1.0.0+it5", rows[0]);
        var body = rows.Skip(1).ToList();
        var expected = new[] { s.Engine }.Concat(s.Runtime).Concat(s.Paths).ToList();
        Assert.Equal(expected.Count, body.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.StartsWith(expected[i].Label, body[i], StringComparison.Ordinal);
            Assert.EndsWith(expected[i].Value, body[i], StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Copyright", DiagnosticsReport.ToText(s), StringComparison.Ordinal);
    }

    /// <summary>Değerler AYNI kolonda başlar: bir destek talebine yapıştırıldığında okunabilir olmalı.</summary>
    [Fact]
    public void The_clipboard_text_aligns_the_values_in_one_column()
    {
        var s = DiagnosticsReport.Compose(Full());
        var rows = DiagnosticsReport.ToText(s).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(1);
        var lines = new[] { s.Engine }.Concat(s.Runtime).Concat(s.Paths);

        var columns = rows.Zip(lines, (row, line) => row.LastIndexOf(line.Value, StringComparison.Ordinal))
                          .Distinct()
                          .ToList();

        Assert.All(columns, c => Assert.True(c > 0, "değer satırın başında — etiket kaybolmuş"));
        Assert.Single(columns);
    }
}
