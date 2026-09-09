using System.IO;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [design v1.14.0 §9] Ayarlar'daki yol "bir klasör, bir solution ya da bir proje dosyası"dır; derlenecek hedef
/// her koşuda o yoldan çözülür. Çözüm asla tahmin yürütmez: tek aday yoksa bir SORUN döner ve kullanıcı yolu
/// hedefe işaret ettirir.
/// </summary>
public class ExternalTargetResolverTests
{
    [Fact]
    public void A_solution_file_is_its_own_target()
    {
        using var temp = new TempDir();
        string sln = Path.Combine(temp.Path, "Mail.sln");
        File.WriteAllText(sln, "");

        var resolution = ExternalTargetResolver.Resolve(sln);

        Assert.Null(resolution.Problem);
        Assert.Equal(new ExternalTarget("Mail", temp.Path, sln), resolution.Target);
    }

    [Fact]
    public void A_project_file_is_its_own_target()
    {
        using var temp = new TempDir();
        string csproj = Path.Combine(temp.Path, "Delta.Common.csproj");
        File.WriteAllText(csproj, "");

        var target = ExternalTargetResolver.Resolve(csproj).Target;

        Assert.Equal("Delta.Common", target!.Name);
        Assert.Equal(csproj, target.TargetPath);
    }

    [Fact]
    public void A_folder_with_a_single_solution_resolves_to_it()
    {
        using var temp = new TempDir();
        string sln = Path.Combine(temp.Path, "Mail.sln");
        File.WriteAllText(sln, "");
        File.WriteAllText(Path.Combine(temp.Path, "readme.md"), "");

        Assert.Equal(sln, ExternalTargetResolver.Resolve(temp.Path).Target!.TargetPath);
    }

    [Fact]
    public void A_folder_without_a_solution_but_with_a_single_project_resolves_to_the_project()
    {
        using var temp = new TempDir();
        string csproj = Path.Combine(temp.Path, "Mail.csproj");
        File.WriteAllText(csproj, "");

        Assert.Equal(csproj, ExternalTargetResolver.Resolve(temp.Path).Target!.TargetPath);
    }

    [Fact]
    public void A_folder_with_two_solutions_is_a_problem_not_a_guess()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Mail.sln"), "");
        File.WriteAllText(Path.Combine(temp.Path, "Mail.Tools.sln"), "");

        var resolution = ExternalTargetResolver.Resolve(temp.Path);

        Assert.Null(resolution.Target);
        Assert.Contains("more than one solution", resolution.Problem);
    }

    [Fact]
    public void A_folder_with_nothing_to_build_is_a_problem()
    {
        using var temp = new TempDir();

        var resolution = ExternalTargetResolver.Resolve(temp.Path);

        Assert.Null(resolution.Target);
        Assert.Contains("no solution or project file", resolution.Problem);
    }

    [Fact]
    public void The_folder_search_does_not_reach_into_subdirectories()
    {
        using var temp = new TempDir();
        string nested = Path.Combine(temp.Path, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Mail.sln"), "");

        Assert.Null(ExternalTargetResolver.Resolve(temp.Path).Target);
    }

    [Fact]
    public void A_file_that_is_neither_solution_nor_project_is_a_problem()
    {
        using var temp = new TempDir();
        string txt = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(txt, "");

        Assert.Contains("not a solution or project file", ExternalTargetResolver.Resolve(txt).Problem);
    }

    [Fact]
    public void A_path_that_does_not_exist_is_a_problem()
    {
        var resolution = ExternalTargetResolver.Resolve(Path.Combine(Path.GetTempPath(), "no-such-dir-9f3a"));

        Assert.Null(resolution.Target);
        Assert.Contains("was not found", resolution.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_path_is_a_problem(string path)
        => Assert.Contains("empty", ExternalTargetResolver.Resolve(path).Problem);

    [Theory]
    [InlineData(@"D:\ext\mail\Mail.sln", "Mail")]
    [InlineData(@"D:\ext\mail\Delta.Common.csproj", "Delta.Common")]
    [InlineData(@"D:\ext\mail", "mail")]
    [InlineData(@"D:\ext\mail\", "mail")]
    public void The_display_name_is_the_last_path_segment_without_a_target_extension(string path, string expected)
        => Assert.Equal(expected, ExternalTargetResolver.DisplayName(path));
}
