using System.IO;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici proje eklenirken derlenecek hedefin (.sln) önerilmesi: dizinde tek bir solution varsa onu seç,
/// aksi halde null dön — kullanıcı seçsin. Öneri asla tahmin yürütmez.
/// </summary>
public class ExternalTargetResolverTests
{
    [Fact]
    public void AutoTarget_picks_the_single_sln()
    {
        using var temp = new TempDir();
        string sln = Path.Combine(temp.Path, "Mail.sln");
        File.WriteAllText(sln, "");
        File.WriteAllText(Path.Combine(temp.Path, "readme.md"), "");

        Assert.Equal(sln, ExternalTargetResolver.AutoTarget(temp.Path));
    }

    [Fact]
    public void AutoTarget_returns_null_when_ambiguous()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Mail.sln"), "");
        File.WriteAllText(Path.Combine(temp.Path, "Mail.Tools.sln"), "");

        Assert.Null(ExternalTargetResolver.AutoTarget(temp.Path));
    }

    [Fact]
    public void AutoTarget_returns_null_when_absent()
    {
        using var temp = new TempDir();

        Assert.Null(ExternalTargetResolver.AutoTarget(temp.Path));
    }

    [Fact]
    public void AutoTarget_does_not_reach_into_subdirectories()
    {
        using var temp = new TempDir();
        string nested = Path.Combine(temp.Path, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Mail.sln"), "");

        Assert.Null(ExternalTargetResolver.AutoTarget(temp.Path));
    }

    [Fact]
    public void AutoTarget_returns_null_for_a_directory_that_does_not_exist()
    {
        Assert.Null(ExternalTargetResolver.AutoTarget(Path.Combine(Path.GetTempPath(), "no-such-dir-9f3a")));
    }
}
