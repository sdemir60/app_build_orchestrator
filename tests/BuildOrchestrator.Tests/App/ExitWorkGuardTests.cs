using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak · Exit · inceleme Ö2] <c>RunViewModel.ExitWork</c>'ün <see cref="BuildOrchestrator.App.ViewModels.ExitWork.None"/>'u "uçuşta iş
/// yok"un tek tanımından gelir (<c>WorkspaceIdle</c>): kabuk kartsız kapanma kararını ona dayandırır. Meşguliyet listesi
/// (<c>WorkspaceBusy</c> ve <c>NonSyncWorkspaceBusy</c>) bir yerde yazılır; <c>ExitWork</c> işi ADLANDIRMAK için bayrakları ayrıca okur ve bu
/// guard listenin her üyesinin orada adlandırıldığını pinler — listeye eklenen bir iş adlandırılmadan kalırsa kırmızı. Kural kaynakta
/// pinlenir çünkü "gelecekte eklenecek bir iş" davranışla kurulamaz.
/// <para>KIRMIZI (düzeltmeden önce): <c>ExitWork</c> <c>None</c>'a kendi bayrak zinciriyle düşüyordu — listeye eklenen bir iş <c>None</c>
/// okunur, kabuk "iş yok" deyip onaysız kapanırdı.</para>
/// </summary>
public sealed class ExitWorkGuardTests
{
    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "ViewModels", file));

    /// <summary><c>ExitWork</c> özelliğinin gövdesi — <c>=&gt;</c>'dan ilk <c>;</c>'e.</summary>
    private static string ExitWorkBody()
    {
        var match = Regex.Match(Source("RunViewModel.Exit.cs"), @"public ExitWork ExitWork\s*=>(?<body>[^;]*);");
        Assert.True(match.Success, "ExitWork özelliği bulunamadı");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void No_work_is_the_workspaces_own_idle_answer()
    {
        Assert.Matches(@"^\s*WorkspaceIdle\s*\?\s*ExitWork\.None\s*:", ExitWorkBody());
    }

    [Fact]
    public void Every_member_of_the_busy_list_is_named_by_exit_work()
    {
        string workspace = Source("RunViewModel.Workspace.cs");
        var members = new List<string>();
        foreach (string property in new[] { "WorkspaceBusy", "NonSyncWorkspaceBusy" })
        {
            var definition = Regex.Match(workspace, @"private bool " + property + @"\s*=>(?<body>[^;]*);");
            Assert.True(definition.Success, property + " bulunamadı");
            members.AddRange(Regex.Matches(definition.Groups["body"].Value, @"\b[A-Z]\w*Busy\b").Select(m => m.Value));
        }
        members.Remove("NonSyncWorkspaceBusy");
        Assert.NotEmpty(members);

        var unnamed = members.Where(m => !Regex.IsMatch(ExitWorkNamingBody(), @"\b" + m + @"\b")).ToList();
        Assert.True(unnamed.Count == 0, "ExitWork'ün adlandırmadığı meşguliyet: " + string.Join(", ", unnamed));
    }

    /// <summary>İşi adlandıran zincir — <c>ExitWork</c>'ün kendisi ya da onun çağırdığı <c>WorkInFlight</c>.</summary>
    private static string ExitWorkNamingBody()
    {
        var match = Regex.Match(Source("RunViewModel.Exit.cs"), @"private ExitWork WorkInFlight\s*=>(?<body>[^;]*);");
        return match.Success ? match.Groups["body"].Value : ExitWorkBody();
    }
}
