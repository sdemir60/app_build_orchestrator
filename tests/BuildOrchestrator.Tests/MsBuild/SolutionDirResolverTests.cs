using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

public class SolutionDirResolverTests
{
    [Fact] // [SPIKE S2-a] restore sln bağlamı ister: tek sln → onun dizini
    public void Single_solution_resolves_to_its_directory()
    {
        var refs = new[] { new SolutionRef("Osys", @"C:\repo\Osys.sln") };
        Assert.Equal(@"C:\repo", SolutionDirResolver.Resolve(@"C:\repo\sub\A.csproj", refs));
    }

    [Fact] // >1 sln → deterministik: Name'e göre ordinal-ignore-case ilk (T32 çoklu-değer; UI seçtirmesi It-4)
    public void Multiple_solutions_pick_first_by_name_deterministically()
    {
        var refs = new[]
        {
            new SolutionRef("Zeta", @"C:\repo\z\Zeta.sln"),
            new SolutionRef("Alpha", @"C:\repo\a\Alpha.sln"),
        };
        Assert.Equal(@"C:\repo\a", SolutionDirResolver.Resolve(@"C:\repo\sub\A.csproj", refs));
        Assert.Equal(SolutionDirResolver.Resolve(@"C:\repo\sub\A.csproj", refs),
                     SolutionDirResolver.Resolve(@"C:\repo\sub\A.csproj", refs.Reverse().ToArray())); // giriş sırası etkilemez
    }

    [Fact] // 0 sln → projenin kendi dizini (restore yine de sln bağlamı görür; kayda değer sapma)
    public void No_solution_falls_back_to_project_directory()
    {
        Assert.Equal(@"C:\repo\sub", SolutionDirResolver.Resolve(@"C:\repo\sub\A.csproj", []));
    }
}
