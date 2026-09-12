using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici bir projenin satırındaki sha, ana repo satırlarıyla AYNI şeyi anlatır: en son hangi sürümden
/// derlendi. O sürüm ana reponun HEAD'i DEĞİL, projenin KENDİ çalışma kopyasının revizyonudur — bu okuyucu
/// onu bulup kökten doğan her projeye dağıtır.
/// </summary>
public class ExternalRevisionReaderTests
{
    private static ExternalWorkspace Resolve(params ExternalProject[] cards) =>
        ExternalWorkspaceResolver.Resolve(new ScanResult([], []), cards, new WorkspaceScanner());

    private static string WriteProject(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string csproj = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>" + name
            + "</AssemblyName><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return csproj;
    }

    private static Task<System.Collections.Generic.IReadOnlyDictionary<string, string>> ReadAsync(
        ExternalWorkspace workspace) =>
        new ExternalRevisionReader(new ProcessRunner()).ReadAsync(workspace.Roots);

    /// <summary>
    /// TFVC kökünün revizyonu YALNIZ güncelleme adımından gelebilir: changeset sorgusu sunucuya gider ve
    /// planlama ağa bağlanmamalıdır.
    ///
    /// <para><b>Eski iddia:</b> bu test <c>$tf</c> işaretli bir TFVC kökünün yerelden OKUNAMADIĞINI ve
    /// revizyonunun YALNIZ güncelleme adımından (<c>C</c> önekli changeset) gelebildiğini pinliyordu. TFVC kolu
    /// kaldırıldı; kuralın hayatta kalan yarısı şudur: çalışma kopyası HİÇ yoksa yerel okuma da yoktur, ama
    /// güncelleme adımı bir revizyon okuduysa o yine dağıtılır.</para>
    /// </summary>
    [Fact]
    public async Task A_root_without_a_working_copy_has_no_revision_unless_the_update_step_read_one()
    {
        using var temp = new TempDir();
        string csproj = WriteProject(Path.Combine(temp.Path, "Ocr"), "Ocr");
        var workspace = Resolve(new ExternalProject(temp.Path));

        Assert.Empty(await ReadAsync(workspace));
    }

    /// <summary>Aynı koşuda güncelleme yapılmışsa o okuma yeğlenir — güncellemeden SONRAKİ hâli anlatır.</summary>
    [Fact]
    public async Task A_revision_read_during_the_update_wins_over_the_local_read()
    {
        using var repo = new GitTestRepo();
        string csproj = WriteProject(Path.Combine(repo.RootPath, "Mail"), "Mail");
        repo.CommitAll("first");
        var workspace = Resolve(new ExternalProject(repo.RootPath));

        var fromUpdate = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            [repo.RootPath] = "0123456789012345678901234567890123456789",
        };
        var merged = await new ExternalRevisionReader(new ProcessRunner()).ReadAsync(workspace.Roots, fromUpdate);

        Assert.Equal("0123456789012345678901234567890123456789", merged[csproj]);
    }

    [Fact]
    public async Task Every_project_under_a_git_root_carries_that_working_copys_head()
    {
        using var repo = new GitTestRepo();
        string mail = WriteProject(Path.Combine(repo.RootPath, "Mail"), "Mail");
        string ocr = WriteProject(Path.Combine(repo.RootPath, "Ocr"), "Ocr");
        string head = repo.CommitAll("first");

        var revisions = await ReadAsync(Resolve(new ExternalProject(repo.RootPath)));

        Assert.Equal(head, revisions[mail]);
        Assert.Equal(head, revisions[ocr]);   // tek çalışma kopyası → tek revizyon
    }

    [Fact]
    public async Task The_working_copy_root_is_found_from_a_nested_card_path()
    {
        using var repo = new GitTestRepo();
        string csproj = WriteProject(Path.Combine(repo.RootPath, "src", "Mail"), "Mail");
        string head = repo.CommitAll("first");

        // Kart doğrudan .csproj'u gösteriyor; kök yukarı yürünerek bulunur.
        var revisions = await ReadAsync(Resolve(new ExternalProject(csproj)));

        Assert.Equal(head, revisions[csproj]);
    }

    [Fact]
    public async Task A_tfvc_root_is_left_without_a_revision()
    {
        // TFVC karşılığı (`tf vc history`) SUNUCUYA gider; planlamayı ağa bağlamamak için okunmaz — o
        // satırların sha yuvası boş kalır. Yanlış bir değer göstermektense hiçbir şey göstermek doğrudur.
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "$tf"));
        WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");

        var revisions = await ReadAsync(Resolve(new ExternalProject(temp.Path)));

        Assert.Empty(revisions);
    }

    [Fact]
    public async Task A_path_with_no_git_working_copy_above_it_is_left_without_a_revision()
    {
        using var temp = new TempDir();
        WriteProject(Path.Combine(temp.Path, "Mail"), "Mail");

        var revisions = await ReadAsync(Resolve(new ExternalProject(temp.Path)));

        Assert.Empty(revisions);
    }

    [Fact]
    public async Task Two_roots_keep_their_own_revisions()
    {
        using var first = new GitTestRepo();
        string mail = WriteProject(Path.Combine(first.RootPath, "Mail"), "Mail");
        string firstHead = first.CommitAll("first");
        using var second = new GitTestRepo();
        string ocr = WriteProject(Path.Combine(second.RootPath, "Ocr"), "Ocr");
        string secondHead = second.CommitAll("second");

        var revisions = await ReadAsync(Resolve(
            new ExternalProject(first.RootPath), new ExternalProject(second.RootPath)));

        Assert.Equal(firstHead, revisions[mail]);
        Assert.Equal(secondHead, revisions[ocr]);
        Assert.NotEqual(revisions[mail], revisions[ocr]);
    }
}
