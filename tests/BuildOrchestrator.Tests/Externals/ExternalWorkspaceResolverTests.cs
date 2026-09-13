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
        ExternalWorkspaceResolver.Resolve(main, externals, new WorkspaceScanner());

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

    private static string WriteSolution(string directory, string name, params string[] csprojPaths)
    {
        string sln = Path.Combine(directory, name + ".sln");
        File.WriteAllText(sln, string.Concat(csprojPaths.Select(p =>
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + Path.GetFileNameWithoutExtension(p)
            + "\", \"" + Path.GetRelativePath(directory, p) + "\", \"{1}\"\nEndProject\n")));
        return sln;
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
        string sln = WriteSolution(temp.Path, "Mail", inside);

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

        var workspace = ExternalWorkspaceResolver.Resolve(main, null, new WorkspaceScanner());

        Assert.Same(main, workspace.Scan);
        Assert.Empty(workspace.Roots);
        Assert.Empty(workspace.ExternalProjectIds);
        Assert.Empty(workspace.Problems);
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
