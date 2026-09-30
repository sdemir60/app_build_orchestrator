using System.IO;
using System.Linq;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [design v1.14.0 §9] Ayarlar'daki bir harici kart TARANABİLİR bir çalışma alanı köküdür: yolun altındaki
/// projeler bulunur ve ana taramayla BİRLEŞİR. Yol üç biçimden biri olabilir (klasör / <c>.sln</c> /
/// <c>.csproj</c>) ve hiçbiri persist edilmez — her koşuda yeniden çözülür.
/// </summary>
public class ExternalWorkspaceResolverTests
{
    private static readonly ScanResult EmptyMain = new([], []);

    private static ExternalWorkspace Resolve(ScanResult main, params ExternalProject[] externals) =>
        ExternalWorkspaceResolver.Resolve(main, externals, new WorkspaceScanner(), ExternalTestRoots.UnrelatedMainRoot);

    private static ExternalWorkspace Resolve(ScanResult main, string mainRootPath, params ExternalProject[] externals) =>
        ExternalWorkspaceResolver.Resolve(main, externals, new WorkspaceScanner(), mainRootPath);

    /// <summary>Bir klasörde proje (ve istenirse solution) dosyaları üretir; dönen değer klasörün yoludur.</summary>
    private static string WriteProject(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string csproj = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>" + name
            + "</AssemblyName><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return csproj;
    }

    /// <summary>[design v1.24.0] Çözümü keşif sayacıyla koşar; sayacın her raporu sırasıyla döner.</summary>
    private static (ExternalWorkspace Workspace, List<(int Repository, int External)> Reports) ResolveCounting(
        ScanResult main, string mainRootPath, params ExternalProject[] externals)
    {
        var reports = new List<(int Repository, int External)>();
        var workspace = ExternalWorkspaceResolver.Resolve(main, externals, new WorkspaceScanner(), mainRootPath,
            (repository, external) => reports.Add((repository, external)));
        return (workspace, reports);
    }

    /// <summary>Son rapor, çözümün kendi gruplamasıyla (grafın <c>IsExternal</c> ayrımı) AYNI olmalı:
    /// harici = rozetli kimlikler, repository = birleşik taramanın geri kalanı.</summary>
    private static void AssertLastReportMatchesTheWorkspace(
        ExternalWorkspace workspace, List<(int Repository, int External)> reports)
    {
        int external = workspace.ExternalProjectIds.Count;
        Assert.Equal((workspace.Scan.CsprojPaths.Count - external, external), reports[^1]);
    }

    // ---------------------------------------------------------------- yolun üç biçimi

    [Fact]
    public void A_folder_contributes_every_project_under_it()
    {
        using var temp = new TempDir();
        string mail = WriteProject(Path.Combine(temp.Path, "src", "Mail"), "Mail");
        string ocr = WriteProject(Path.Combine(temp.Path, "src", "Ocr"), "Ocr");

        var workspace = Resolve(EmptyMain, new ExternalProject(temp.Path));

        Assert.Equal([mail, ocr], workspace.Scan.CsprojPaths.Order(System.StringComparer.OrdinalIgnoreCase));
        Assert.Empty(workspace.Problems);
    }

    [Fact]
    public void A_solution_contributes_only_the_projects_it_lists()
    {
        // Klasörü taramak solution'a dahil OLMAYAN kardeş projeyi de içeri alırdı.
        using var temp = new TempDir();
        string inside = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");
        WriteProject(Path.Combine(temp.Path, "Sandbox"), "Sandbox");
        string sln = ExternalFixtureFiles.WriteSolution(temp.Path, "Mail", inside);

        var workspace = Resolve(EmptyMain, new ExternalProject(sln));

        Assert.Equal([inside], workspace.Scan.CsprojPaths);
        Assert.Equal([sln], workspace.Scan.SlnPaths);
    }

    [Fact]
    public void A_project_file_contributes_exactly_itself()
    {
        using var temp = new TempDir();
        string csproj = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");
        WriteProject(Path.Combine(temp.Path, "Other"), "Other");

        var workspace = Resolve(EmptyMain, new ExternalProject(csproj));

        Assert.Equal([csproj], workspace.Scan.CsprojPaths);
        Assert.Empty(workspace.Scan.SlnPaths);
    }

    // ---------------------------------------------------------------- birleşme ve rozet

    [Fact]
    public void The_main_scan_and_the_external_scan_become_one_workspace()
    {
        using var main = new TempDir();
        using var external = new TempDir();
        string mainProject = WriteProject(Path.Combine(main.Path, "A"), "A");
        string externalProject = WriteProject(Path.Combine(external.Path, "Mail"), "Mail");

        var workspace = Resolve(new ScanResult([mainProject], []), new ExternalProject(external.Path));

        Assert.Contains(mainProject, workspace.Scan.CsprojPaths);
        Assert.Contains(externalProject, workspace.Scan.CsprojPaths);
    }

    /// <summary>
    /// <b>Eski iddia:</b> rozet bir <c>id → VcsKind</c> haritasıydı ve "kullanıcının SEÇTİĞİ kaynak" oraya
    /// yazılıyordu. TFVC kolu kaldırıldı: taşınacak bir değer kalmadı, soru "harici mi"ye indi ve harita bir
    /// Id KÜMESİ oldu. Kuralın özü aynı: yalnız harici köklerden gelen projeler rozet taşır.
    /// </summary>
    [Fact]
    public void Only_external_projects_are_marked_as_external()
    {
        using var main = new TempDir();
        using var external = new TempDir();
        string mainProject = WriteProject(Path.Combine(main.Path, "A"), "A");
        string externalProject = WriteProject(Path.Combine(external.Path, "Mail"), "Mail");

        var workspace = Resolve(new ScanResult([mainProject], []), new ExternalProject(external.Path));

        Assert.Contains(externalProject, workspace.ExternalProjectIds);
        Assert.DoesNotContain(mainProject, workspace.ExternalProjectIds);
    }

    [Fact]
    public void The_scan_stays_canonical_when_two_cards_overlap()
    {
        // Aynı proje iki kartta görünebilir (iç içe yollar). Liste tekil ve sıralı kalmalı, rozet tek kez.
        using var temp = new TempDir();
        string mail = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");

        var workspace = Resolve(EmptyMain,
            new ExternalProject(temp.Path), new ExternalProject(mail));

        Assert.Equal([mail], workspace.Scan.CsprojPaths);
        Assert.Equal([mail], workspace.ExternalProjectIds);
    }

    [Fact]
    public void An_empty_card_list_returns_the_main_scan_untouched()
    {
        var main = new ScanResult([@"D:\repo\A.csproj"], [@"D:\repo\Osys.sln"]);

        var workspace = ExternalWorkspaceResolver.Resolve(main, null, new WorkspaceScanner(), ExternalTestRoots.UnrelatedMainRoot);

        Assert.Same(main, workspace.Scan);
        Assert.Empty(workspace.Roots);
        Assert.Empty(workspace.ExternalProjectIds);
        Assert.Empty(workspace.Problems);
    }

    // ---------------------------------------------------------------- [design v1.24.0] keşif sayacı

    /// <summary>Kart yoksa sayaç TEK rapor verir: ana taramanın projeleri, harici 0. Sync'in keşif satırı
    /// harici tanım yokken de bu raporla <c>N found</c>'a ulaşır.</summary>
    [Fact]
    public void Without_cards_the_counter_reports_the_main_scan_once()
    {
        var main = new ScanResult([@"D:\repo\A\A.csproj", @"D:\repo\B\B.csproj"], [@"D:\repo\Osys.sln"]);

        var (workspace, reports) = ResolveCounting(main, ExternalTestRoots.UnrelatedMainRoot);

        Assert.Equal([(2, 0)], reports);
        AssertLastReportMatchesTheWorkspace(workspace, reports);
    }

    /// <summary>Sayaç kaynak başına ilerler: önce ana tarama (harici henüz 0), sonra projeye çözülen HER kart
    /// için bir rapor — değerler KÜMÜLATİFTİR (o ana kadar bulunanlar). Çözülemeyen kart rapor VERMEZ ve hiçbir
    /// sayıyı artırmaz: uyarısı ayrı yoldan (<see cref="ExternalWorkspace.Problems"/>) gelir.</summary>
    [Fact]
    public void The_counter_reports_the_main_scan_first_then_each_resolved_card_cumulatively()
    {
        using var main = new TempDir();
        using var external = new TempDir();
        string a = WriteProject(Path.Combine(main.Path, "A"), "A");
        WriteProject(Path.Combine(external.Path, "Tools", "Mail"), "Mail");
        WriteProject(Path.Combine(external.Path, "Tools", "Ocr"), "Ocr");
        string pay = WriteProject(Path.Combine(external.Path, "Pay"), "Pay");

        var (workspace, reports) = ResolveCounting(new ScanResult([a], []), main.Path,
            new ExternalProject(Path.Combine(external.Path, "Tools")),
            new ExternalProject(Path.Combine(Path.GetTempPath(), "gone-4c21")),
            new ExternalProject(pay));

        Assert.Equal([(1, 0), (1, 2), (1, 3)], reports);
        Assert.Single(workspace.Problems);
        AssertLastReportMatchesTheWorkspace(workspace, reports);
    }

    /// <summary>Üst üste binen kartlar (bir klasör ve içindeki <c>.sln</c>) aynı projeyi iki kez SAYMAZ: harici
    /// sayı tekil kimliklerdir — birleşik taramanın tekilleştirmesiyle aynı kural. İkinci kart yine de rapor verir
    /// (kaynak okundu), yalnız sayı yerinde kalır.</summary>
    [Fact]
    public void Overlapping_cards_do_not_count_a_project_twice()
    {
        using var temp = new TempDir();
        string mail = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");
        string sln = ExternalFixtureFiles.WriteSolution(temp.Path, "Mail", mail);

        var (workspace, reports) = ResolveCounting(EmptyMain, ExternalTestRoots.UnrelatedMainRoot,
            new ExternalProject(temp.Path), new ExternalProject(sln));

        Assert.Equal([(0, 0), (0, 1), (0, 1)], reports);
        AssertLastReportMatchesTheWorkspace(workspace, reports);
    }

    /// <summary>
    /// AYIRT EDİCİ — <b>repository, ana taramadaki projelerden harici OLMAYANLARdır</b>, ana taramanın ham sayısı
    /// değil. Ana kökün ÜST klasörünü gösteren bir kart reddedilmez (yalnız ana kökün içi reddedilir) ve ana
    /// repo projelerini de harici rozetiyle getirir; o projeler satır gruplamasında <c>EXTERNAL PROJECTS</c>
    /// altına düşer, sayaç da onları oraya taşır. Repository bu yüzden azalabilir; TOPLAM (bulunan tekil
    /// projeler) asla azalmaz.
    /// </summary>
    [Fact]
    public void A_card_above_the_main_root_moves_its_projects_to_external_and_the_total_never_drops()
    {
        using var temp = new TempDir();
        string mainRoot = Path.Combine(temp.Path, "main");
        string a = WriteProject(Path.Combine(mainRoot, "A"), "A");
        WriteProject(Path.Combine(temp.Path, "Shared", "B"), "B");

        var (workspace, reports) = ResolveCounting(new ScanResult([a], []), mainRoot,
            new ExternalProject(temp.Path));

        Assert.Equal([(1, 0), (0, 2)], reports);
        Assert.Empty(workspace.Problems);
        var totals = reports.Select(r => r.Repository + r.External).ToList();
        Assert.Equal(totals.Order(), totals);
        AssertLastReportMatchesTheWorkspace(workspace, reports);
    }

    // ---------------------------------------------------------------- çözülemeyen yollar

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_reported_and_contributes_nothing(string path)
    {
        var workspace = Resolve(EmptyMain, new ExternalProject(path));

        Assert.Empty(workspace.Scan.CsprojPaths);
        Assert.Equal("the path is empty", Assert.Single(workspace.Problems).Problem);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_reported_with_its_name()
    {
        var missing = Path.Combine(Path.GetTempPath(), "DoganTrend-9f31");

        var workspace = Resolve(EmptyMain, new ExternalProject(missing));

        var problem = Assert.Single(workspace.Problems);
        Assert.Equal("DoganTrend-9f31", problem.Name);   // uyarıyı/iptali kuran metin adı buradan alır
        Assert.Equal("the path was not found", problem.Problem);
        Assert.Equal(missing, problem.Project.Path);
    }

    [Fact]
    public void A_folder_without_project_files_is_reported()
    {
        using var temp = new TempDir();

        var workspace = Resolve(EmptyMain, new ExternalProject(temp.Path));

        Assert.Equal("no project file was found in the folder", Assert.Single(workspace.Problems).Problem);
    }

    [Fact]
    public void A_file_that_is_neither_solution_nor_project_is_reported()
    {
        using var temp = new TempDir();
        string txt = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(txt, "");

        var workspace = Resolve(EmptyMain, new ExternalProject(txt));

        Assert.Contains("not a solution or project file", Assert.Single(workspace.Problems).Problem);
    }

    [Fact]
    public void A_broken_card_does_not_stop_the_healthy_ones()
    {
        using var temp = new TempDir();
        string mail = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");

        var workspace = Resolve(EmptyMain,
            new ExternalProject(Path.Combine(Path.GetTempPath(), "gone-7b12")),
            new ExternalProject(temp.Path));

        Assert.Equal([mail], workspace.Scan.CsprojPaths);
        Assert.Single(workspace.Problems);
    }

    // ---------------------------------------------------------------- [kullanıcı kararı] ana kökün içi reddedilir

    /// <summary>Kart TAM OLARAK ana kökü gösteriyor: ana ağaç zaten taranıyor, ikinci kez "harici" rozetiyle
    /// taranması aynı projeye iki kimlik (sıradan + harici) verirdi. Sync uyarır, hiçbir harici proje eklenmez —
    /// diğer çözülemeyen yollarla AYNI yüzey (<see cref="ExternalScanProblem"/>).</summary>
    [Fact]
    public void A_card_pointing_at_the_main_root_itself_is_rejected()
    {
        using var main = new TempDir();
        WriteProject(Path.Combine(main.Path, "A"), "A");

        var workspace = Resolve(new ScanResult([], []), main.Path, new ExternalProject(main.Path));

        Assert.Equal("the path is inside the main workspace root", Assert.Single(workspace.Problems).Problem);
        Assert.Empty(workspace.Roots);
        Assert.Empty(workspace.ExternalProjectIds);
    }

    /// <summary>Kart ana kökün BİR ALT KLASÖRÜNÜ gösteriyor — "ekleyemesin" kararı yalnız tam eşleşmeyi değil,
    /// ana ağacın İÇİNİ de kapsar.</summary>
    [Fact]
    public void A_card_pointing_at_a_subfolder_of_the_main_root_is_rejected()
    {
        using var main = new TempDir();
        string nested = Path.Combine(main.Path, "src", "Mail");
        WriteProject(nested, "Mail");

        var workspace = Resolve(new ScanResult([], []), main.Path, new ExternalProject(nested));

        Assert.Equal("the path is inside the main workspace root", Assert.Single(workspace.Problems).Problem);
        Assert.Empty(workspace.Scan.CsprojPaths);
    }

    /// <summary>Karşılaştırma normalizasyon-güvenlidir: sondaki ayraç ve büyük/küçük harf farkı Windows'ta
    /// AYNI kökü gösterir, kontrolü atlatmaz.</summary>
    [Fact]
    public void The_comparison_is_normalization_safe_for_trailing_separators_and_case()
    {
        using var main = new TempDir();
        string upper = main.Path.ToUpperInvariant() + Path.DirectorySeparatorChar;

        var workspace = Resolve(new ScanResult([], []), main.Path, new ExternalProject(upper));

        Assert.Equal("the path is inside the main workspace root", Assert.Single(workspace.Problems).Problem);
    }

    /// <summary>Ana kökün YANINDAKİ (aynı önekle başlayan ama AYRI) bir kök yanlışlıkla reddedilmemeli — <see
    /// cref="RootScope"/>'un tuzak notu: <c>C:\repo</c> öneki <c>C:\repo2\...</c>'yi KAPSAMAZ.</summary>
    [Fact]
    public void A_sibling_root_with_an_overlapping_prefix_is_not_rejected()
    {
        using var main = new TempDir();
        string sibling = main.Path + "-sibling";
        string mail = WriteProject(Path.Combine(sibling, "Mail"), "Mail");

        try
        {
            var workspace = Resolve(new ScanResult([], []), main.Path, new ExternalProject(sibling));

            Assert.Equal([mail], workspace.Scan.CsprojPaths);
            Assert.Empty(workspace.Problems);
        }
        finally
        {
            if (Directory.Exists(sibling)) Directory.Delete(sibling, recursive: true);
        }
    }

    // ---------------------------------------------------------------- yardımcılar

    [Theory]
    [InlineData(@"D:\ext\mail\Mail.sln", "Mail")]
    [InlineData(@"D:\ext\mail\Delta.Common.csproj", "Delta.Common")]
    [InlineData(@"D:\Projects\Delta\CustomerProject\DoganTrend", "DoganTrend")]
    [InlineData(@"D:\Projects\Delta\CustomerProject\DoganTrend\", "DoganTrend")]
    public void The_display_name_is_the_last_path_segment_without_a_target_extension(string path, string expected)
        => Assert.Equal(expected, ExternalWorkspaceResolver.DisplayName(path));

    [Fact]
    public void The_search_root_of_a_file_is_its_folder_and_of_a_folder_is_itself()
    {
        using var temp = new TempDir();
        string csproj = WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");

        Assert.Equal(Path.GetDirectoryName(csproj), ExternalWorkspaceResolver.SearchRootOf(csproj));
        Assert.Equal(Path.GetFullPath(temp.Path), ExternalWorkspaceResolver.SearchRootOf(temp.Path));
    }

    [Fact]
    public void The_search_root_of_a_path_that_does_not_exist_is_the_path_itself()
    {
        // Güncelleme adımı taramadan ÖNCE koşar ve HAM yolu verir — var olmayan bir yol da bir cevap almalı.
        string missing = Path.Combine(Path.GetTempPath(), "gone-4a19");

        Assert.Equal(missing, ExternalWorkspaceResolver.SearchRootOf(missing));
    }
}
