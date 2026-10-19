using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.Diagnostics;
using static BuildOrchestrator.Tests.ByteUnits;

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
///
/// <para><b>[DEĞİŞEN KURAL — kaynak şeridi, karar K7]</b> ESKİ İDDİA (idle-memory spec §6.6): App'in üç bellek satırı
/// (<c>App private</c> · <c>GC committed</c> · <c>Managed heap</c>) Environment sekmesinin RUNTIME grubundaydı ve pano metni onları
/// Runtime satırlarının arasında taşırdı. Title bar'a kaynak şeridi geldi ve belleği gösterir; About'taki satırlar yalnız pencerenin
/// açıldığı andaki bir görüntüydü. Satırlar ekrandan ÇIKTI, kopyalanan tanı metninde (destek talebine yapıştırılan) KALDI: modelde
/// <c>CopyOnly</c> grubudur — hiçbir sekmede çizilmez, yalnız <see cref="DiagnosticsReport.ToText"/>'e girer, metindeki yeri
/// değişmedi. Görünür/yalnız-kopya ayrımı TEK yerdedir: modelin grupları.</para>
/// </summary>
public class DiagnosticsReportTests
{
    private static DiagnosticsInput Full() => new(
        Product: "Build Orchestrator",
        Version: "1.0.0+it5",
        Copyright: "© 2026 Delta Yazılım",
        EngineVersion: "1.0.0+engine",
        EnginePid: 4242,
        EngineState: EngineState.Ready,
        Runtime: ".NET 10.0.0",
        Os: "Microsoft Windows 10.0.26200",
        Memory: new MemoryReading(PrivateBytes: 309 * Mb, CommittedBytes: 250 * Mb, HeapBytes: 8 * Mb),
        MsBuild: @"C:\VS\MSBuild.exe (v17.9.8)",
        RepositoryRoot: @"D:\repo",
        StateFile: @"C:\state\ui-state.json",
        LogsRoot: @"C:\state\logs");

    private static IEnumerable<DiagnosticsLine> AllLines(DiagnosticsSnapshot s) =>
        s.Identity.Concat(s.Runtime).Concat(s.CopyOnly).Concat(s.Paths);

    /// <summary>[dalga 3 · B1 fix 1 · m-3] Motor PID satırının değeri — satır üretimin etiket sabitiyle bulunur (etiketin literal pini yalnız
    /// RUNTIME grubunun etiket sırası testindedir).</summary>
    private static string PidValue(DiagnosticsSnapshot s) => s.Runtime.Single(l => l.Label == DiagnosticsReport.EnginePidLabel).Value;

    [Fact]
    public void The_identity_group_is_version_engine_and_copyright_in_that_order()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["Version", "Engine", "Copyright"], s.Identity.Select(l => l.Label));
        Assert.Equal(["1.0.0+it5", "1.0.0+engine", "© 2026 Delta Yazılım"], s.Identity.Select(l => l.Value));
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kaynak şeridi, karar K7]</b> RUNTIME grubu: Engine PID · .NET runtime · OS — Environment sekmesinin çizdiği satırlar.
    /// <para><b>Eski iddia</b> (<c>The_runtime_group_is_engine_pid_dotnet_runtime_os_and_the_app_memory</c>, idle-memory spec §6.6): grup altı
    /// satırdı — bu üçü ve ardından App private · GC committed · Managed heap (MB; boşta bellek ölçüm raporu, 2026-10-13: App'in koşu
    /// sonrası 798 MB özel belleği ancak dış araçlarla görülmüştü). <b>Değişme gerekçesi:</b> title bar'a kaynak şeridi geldi ve belleği
    /// gösterir; About'taki satırlar yalnız pencerenin açıldığı andaki bir görüntüydü. Üç satır ekrandan çıktı, sayılar kopyalanan metinde
    /// kalır ve modelde <c>CopyOnly</c> grubundadır (<see cref="The_copy_only_group_is_the_app_memory_in_megabytes"/>).</para>
    /// </summary>
    [Fact]
    public void The_runtime_group_is_engine_pid_dotnet_runtime_and_os_without_the_app_memory()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["Engine PID", ".NET runtime", "OS"], s.Runtime.Select(l => l.Label));
        Assert.Equal(["4242", ".NET 10.0.0", "Microsoft Windows 10.0.26200"], s.Runtime.Select(l => l.Value));
    }

    /// <summary>
    /// [kaynak şeridi, karar K7] Yalnız-kopya grup: App private · GC committed · Managed heap, MB — hiçbir sekmede çizilmez, yalnız kopyalanan
    /// tanı metnine girer (hata bildirimi için). Satırlar motorun bellek satırının (<c>memory: private=… committed=… heap=…</c>) App'teki eşidir:
    /// aynı okuma, aynı MB kuralı (<see cref="MemoryLine"/>); okuma ve değerler değişmedi, yalnız satırların nerede durduğu değişti. MODELDEKİ
    /// etiketlerin harfi harfine pini BURADADIR; kopyalanan metindeki aynı satırların sırası ve hizası ayrı bir testte pinlidir
    /// (<see cref="The_clipboard_text_keeps_the_memory_rows_between_the_runtime_rows_and_the_paths"/>); öteki testler satırları bu gruptan okur.
    /// </summary>
    [Fact]
    public void The_copy_only_group_is_the_app_memory_in_megabytes()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["App private", "GC committed", "Managed heap"], s.CopyOnly.Select(l => l.Label));
        Assert.Equal(["309 MB", "250 MB", "8 MB"], s.CopyOnly.Select(l => l.Value));
    }

    /// <summary>[idle-memory · spec §6.6] Bellek satırlarının sayısı motorun bellek satırıyla AYNI kuraldan gelir
    /// (<see cref="MemoryLine.Megabytes"/>): yarım MB sıfırdan uzağa yuvarlanır (2,5 MB → 3; tam sayı bölmesi ya da bankacı
    /// yuvarlaması 2 verirdi) ve büyük değer binlik ayırıcı almaz — App kendi yuvarlamasını yazmaz. [Karar K7] Satırlar artık
    /// yalnız-kopya gruptadır; kural aynı.</summary>
    [Fact]
    public void The_memory_rows_round_like_the_engine_memory_line()
    {
        var s = DiagnosticsReport.Compose(Full() with
        {
            Memory = new MemoryReading(PrivateBytes: 5L * 1024 * Mb, CommittedBytes: 5 * Mb / 2, HeapBytes: Mb / 2 - 1),
        });

        Assert.Equal(["5120 MB", "3 MB", "0 MB"], s.CopyOnly.Select(l => l.Value));
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1]</b> PATHS grubu: MSBuild · Repository root · State file · Logs.
    /// <para><b>Eski iddia</b> (<c>The_paths_group_is_msbuild_repository_state_logs_and_worktree_pool</c>):
    /// grup beşinci satır olarak <c>Worktree pool</c>'u taşırdı. <b>Değişme gerekçesi:</b> worktree modu kalktı;
    /// araç havuzu artık ne kurar ne kullanır — kullanılmayan bir yolu tanı raporunda göstermek, destek
    /// talebinde yanıltıcı bir ipucu olurdu.</para>
    /// </summary>
    [Fact]
    public void The_paths_group_is_msbuild_repository_state_and_logs()
    {
        var s = DiagnosticsReport.Compose(Full());

        Assert.Equal(["MSBuild", "Repository root", "State file", "Logs"], s.Paths.Select(l => l.Label));
        Assert.Equal(
            [@"C:\VS\MSBuild.exe (v17.9.8)", @"D:\repo", @"C:\state\ui-state.json", @"C:\state\logs"],
            s.Paths.Select(l => l.Value));
    }

    /// <summary>Tekrar YOK: bir etiket tüm grupların (kimlik, Runtime, yalnız-kopya, Paths) toplamında bir kez geçer — eski
    /// <c>App version</c>/<c>Engine version</c> satırları Environment'a geri sızarsa burada yakalanır; taşınan bellek satırları da
    /// iki grupta birden kalamaz.</summary>
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
        Assert.Equal(DiagnosticsReport.Unknown, PidValue(s));
    }

    /// <summary>[idle-memory · dalga 3 · karar 39] Motor boşta uyurken (VM durumu <see cref="EngineState.Asleep"/>) PID satırı <c>asleep</c>
    /// okur — Environment sekmesinde ve panoya kopyalanan metinde: süreç yok, ama "bilinmiyor" (<c>—</c>) da değil; sonraki iş motoru yeniden
    /// başlatır. VM uyuyan motorun PID'ini tutmaz (null). Sürüm satırı değişmez (uyanan motor aynı ikilidir). Sabitin metni bir kez pinlenir.
    /// <para>KIRMIZI: PID satırı uykuda "—" okuyordu (durum rapora hiç girmiyordu).</para></summary>
    [Fact]
    public void An_asleep_engine_reads_asleep_on_the_pid_row_and_in_the_copied_text()
    {
        Assert.Equal("asleep", DiagnosticsReport.Asleep); // metnin TEK literal pini

        var s = DiagnosticsReport.Compose(Full() with { EnginePid = null, EngineState = EngineState.Asleep });

        Assert.Equal(DiagnosticsReport.Asleep, PidValue(s));
        Assert.Equal(Full().EngineVersion, s.Engine.Value);
        Assert.Contains(DiagnosticsReport.ToText(s).Split(Environment.NewLine),
            row => row.StartsWith(DiagnosticsReport.EnginePidLabel, StringComparison.Ordinal) && row.EndsWith(DiagnosticsReport.Asleep, StringComparison.Ordinal));
    }

    /// <summary>Uykudan başka her motor durumu — sözleşmeden okunur: yarın eklenen bir durum da kendiliğinden sınanır.</summary>
    public static TheoryData<EngineState> StatesOtherThanAsleep()
    {
        var states = new TheoryData<EngineState>();
        foreach (var state in Enum.GetValues<EngineState>())
            if (state != EngineState.Asleep) states.Add(state);
        return states;
    }

    /// <summary>[karar 39 · PİN] Uykudan başka her durumda PID satırı bugünkü gibidir: motorun PID'i varsa o, yoksa <c>—</c> — uyku sürerken
    /// (süreç henüz duruyor), uyanırken (yeni süreç henüz hazır değil), ölü ya da erişilemez motorda da. PİN: durumu okumayan bugünkü rapor da
    /// yeşildir; bu test "asleep"in başka bir duruma sızmasını yakalar.</summary>
    [Theory]
    [MemberData(nameof(StatesOtherThanAsleep))]
    public void Every_other_engine_state_shows_the_pid_as_today(EngineState state)
    {
        Assert.Equal(Full().EnginePid!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PidValue(DiagnosticsReport.Compose(Full() with { EngineState = state })));
        Assert.Equal(DiagnosticsReport.Unknown, PidValue(DiagnosticsReport.Compose(Full() with { EngineState = state, EnginePid = null })));
    }

    /// <summary>
    /// [idle-memory · final review (Tasks 9-10)] Bellek okunamazsa (<c>MemoryLine.TryRead</c> <c>null</c> döner) tanı raporu yine
    /// kurulur ve üç bellek satırı <see cref="DiagnosticsReport.Unknown"/> yazar, satırlar KAYBOLMAZ — motor doğmamışken PID
    /// satırının <c>—</c> olması gibi. Bir tanı, tanı ekranını bozamaz: eskiden okuma korumasızdı ve süreç sorgusu patlarsa About
    /// hiç açılmazdı (motorun bellek satırı için <c>MemoryLine.Report</c> aynı güvenceyi baştan veriyordu). [Karar K7] Satırlar
    /// artık yalnız-kopya gruptadır: <c>—</c> kopyalanan metinde okunur.
    /// </summary>
    [Fact]
    public void An_unreadable_memory_reads_as_unknown_on_its_three_rows_instead_of_breaking_the_report()
    {
        var s = DiagnosticsReport.Compose(Full() with { Memory = null });

        Assert.Equal(3, s.CopyOnly.Count); // satırlar KAYBOLMAZ
        Assert.All(s.CopyOnly, line => Assert.Equal(DiagnosticsReport.Unknown, line.Value));
        Assert.Equal("4242", PidValue(s)); // öbür satırlar etkilenmez
        var rows = DiagnosticsReport.ToText(s).Split(Environment.NewLine);
        Assert.All(s.CopyOnly, line => Assert.Contains(rows,
            row => row.StartsWith(line.Label, StringComparison.Ordinal) && row.EndsWith(DiagnosticsReport.Unknown, StringComparison.Ordinal)));
    }

    [Fact]
    public void An_empty_repository_root_reads_as_no_repository()
        => Assert.Contains(DiagnosticsReport.Compose(Full() with { RepositoryRoot = "" }).Paths,
            l => l.Value == DiagnosticsReport.NoRepository);

    /// <summary>[v1.19.0 §2.10] Pano metni: ilk satır <c>{ürün} {sürüm}</c>, ardından hizalı <c>Engine</c> +
    /// Runtime + Paths satırları — satır başına bir satır. Telif panoya GİTMEZ (destek talebinde bilgi taşımaz).
    /// <para><b>[DEĞİŞEN KURAL — kaynak şeridi, karar K7]</b> Eski iddia: metin Engine + Runtime + Paths'ti (üç bellek satırı Runtime'ın
    /// içindeydi). Bellek satırları yalnız-kopya gruba taşındı ve metin onları hâlâ taşır: Engine + Runtime + <b>yalnız-kopya</b> + Paths.
    /// Yalnız-kopya grup boşsa "kopyada var" iddiası boşa geçerdi; dolu olduğu önce sınanır.</para></summary>
    [Fact]
    public void The_clipboard_text_is_a_title_line_then_engine_runtime_the_copy_only_rows_and_paths()
    {
        var s = DiagnosticsReport.Compose(Full());
        var rows = DiagnosticsReport.ToText(s).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(s.CopyOnly);
        Assert.Equal("Build Orchestrator 1.0.0+it5", rows[0]);
        var body = rows.Skip(1).ToList();
        var expected = new[] { s.Engine }.Concat(s.Runtime).Concat(s.CopyOnly).Concat(s.Paths).ToList();
        Assert.Equal(expected.Count, body.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.StartsWith(expected[i].Label, body[i], StringComparison.Ordinal);
            Assert.EndsWith(expected[i].Value, body[i], StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Copyright", DiagnosticsReport.ToText(s), StringComparison.Ordinal);
    }

    /// <summary>
    /// [kaynak şeridi, karar K7 · PİN] Üç bellek satırı ekrandan kalktı ama kopyalanan metin DEĞİŞMEDİ: bugüne kadarki sırayla —
    /// Engine, Runtime satırları, üç bellek satırı, Paths — ve bugünkü hizayla. Metin harfi harfine yazılır (modelin gruplarından
    /// türetilmez): gruplar yeniden dizilse ya da bir satır başka gruba taşınsa bile bir destek talebine yapıştırılan metin, eskisiyle
    /// karşılaştırılabilir kalır.
    /// <para>PİN: satırları taşımadan ÖNCEKİ rapor da yeşildir; bu test taşımanın metni oynatmadığını yakalar.</para>
    /// </summary>
    [Fact]
    public void The_clipboard_text_keeps_the_memory_rows_between_the_runtime_rows_and_the_paths()
    {
        string[] expected =
        [
            "Build Orchestrator 1.0.0+it5",
            "Engine           1.0.0+engine",
            "Engine PID       4242",
            ".NET runtime     .NET 10.0.0",
            "OS               Microsoft Windows 10.0.26200",
            "App private      309 MB",
            "GC committed     250 MB",
            "Managed heap     8 MB",
            @"MSBuild          C:\VS\MSBuild.exe (v17.9.8)",
            @"Repository root  D:\repo",
            @"State file       C:\state\ui-state.json",
            @"Logs             C:\state\logs",
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected) + Environment.NewLine,
            DiagnosticsReport.ToText(DiagnosticsReport.Compose(Full())));
    }

    /// <summary>Değerler AYNI kolonda başlar: bir destek talebine yapıştırıldığında okunabilir olmalı.</summary>
    [Fact]
    public void The_clipboard_text_aligns_the_values_in_one_column()
    {
        var s = DiagnosticsReport.Compose(Full());
        var rows = DiagnosticsReport.ToText(s).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(1);
        var lines = new[] { s.Engine }.Concat(s.Runtime).Concat(s.CopyOnly).Concat(s.Paths);

        var columns = rows.Zip(lines, (row, line) => row.LastIndexOf(line.Value, StringComparison.Ordinal))
                          .Distinct()
                          .ToList();

        Assert.All(columns, c => Assert.True(c > 0, "değer satırın başında — etiket kaybolmuş"));
        Assert.Single(columns);
    }
}
