using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak · Exit · inceleme Ö2] <c>RunViewModel.ExitWork</c>'ün <see cref="BuildOrchestrator.App.ViewModels.ExitWork.None"/>'u "uçuşta iş
/// yok"un tek tanımından gelir (<c>WorkspaceIdle</c>): kabuk kartsız kapanma kararını ona dayandırır. Meşguliyet listesi
/// (<c>WorkspaceBusy</c> ve <c>NonSyncWorkspaceBusy</c>) bir yerde yazılır; <c>ExitWork</c> işi ADLANDIRMAK için bayrakları ayrıca okur ve bu
/// guard listenin her üyesinin orada adlandırıldığını pinler — listeye eklenen bir iş adlandırılmadan kalırsa kırmızı. Şeridin okuduğu
/// <c>WorkInFlight</c> zinciri de aynı listeyi sayar; ikinci bir guard onu pinler. Kural kaynakta pinlenir çünkü "gelecekte eklenecek
/// bir iş" davranışla kurulamaz.
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

    /// <summary>Meşguliyet listesinin üyeleri — <c>WorkspaceBusy</c> ve <c>NonSyncWorkspaceBusy</c> gövdelerindeki her <c>*Busy</c>
    /// bayrağı (<c>NonSyncWorkspaceBusy</c>'nin kendisi bir üye değil, alt listedir). İki guard da üyeleri BURADAN okur: üye çıkarımı
    /// tek yerde durur (kopya YASAK).</summary>
    private static List<string> BusyListMembers()
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
        return members;
    }

    [Fact]
    public void Every_member_of_the_busy_list_is_named_by_exit_work()
    {
        var unnamed = BusyListMembers().Where(m => !Regex.IsMatch(ExitWorkNamingBody(), @"\b" + m + @"\b")).ToList();
        Assert.True(unnamed.Count == 0, "ExitWork'ün adlandırmadığı meşguliyet: " + string.Join(", ", unnamed));
    }

    /// <summary>[şerit işlemi · son inceleme O3] Şeridin okuduğu <c>WorkInFlight</c> zinciri de aynı listeyi sayar: listeye eklenen bir iş
    /// orada geçmezse şerit (pill, satır, çubuk) o işi hiç anlatmaz ve Esc "durdurulamaz" satırını yazmaz — sessiz bir kayma. Guard
    /// <c>WorkInFlight</c> gövdesinde listenin HER üyesinin (<c>SyncBusy</c> dahil) geçtiğini pinler. Kural kaynakta pinlenir çünkü
    /// "gelecekte eklenecek bir iş" davranışla kurulamaz.
    /// <para>KIRMIZI (geçici mutasyonla; kod doğruydu): <c>WorktreeBusy</c> zincirden çıkarılınca
    /// "WorkInFlight'ın saymadığı meşguliyet: WorktreeBusy". Kaymanın gerçek olduğunu bu dalda EscStopTests'in worktree testi
    /// gösterdi (worktree kurulumu zincirde yoktu) — o zamana dek yalnız <c>ExitWork</c> zinciri guard'lıydı.</para></summary>
    [Fact]
    public void Every_member_of_the_busy_list_is_counted_by_the_ribbons_work_in_flight()
    {
        var match = Regex.Match(Source("RunViewModel.Workspace.cs"), @"public RibbonWork WorkInFlight\s*=>(?<body>[^;]*);");
        Assert.True(match.Success, "WorkInFlight özelliği bulunamadı");
        string body = match.Groups["body"].Value;

        var uncounted = BusyListMembers().Where(m => !Regex.IsMatch(body, @"\b" + m + @"\b")).ToList();
        Assert.True(uncounted.Count == 0, "WorkInFlight'ın saymadığı meşguliyet: " + string.Join(", ", uncounted));
    }

    /// <summary>İşi adlandıran zincir — <c>ExitWork</c>'ün kendisi ya da onun çağırdığı <c>ExitWorkInFlight</c>.
    /// <para>[şerit işlemi] Zincirin adı eskiden <c>WorkInFlight</c>'tı; o ad şeridin okuduğu işe (<c>RunViewModel.WorkInFlight</c>,
    /// <c>RibbonWork</c>) geçti. Guard aynı gövdeyi okur — yalnız aradığı ad değişti.</para></summary>
    private static string ExitWorkNamingBody()
    {
        var match = Regex.Match(Source("RunViewModel.Exit.cs"), @"private ExitWork ExitWorkInFlight\s*=>(?<body>[^;]*);");
        return match.Success ? match.Groups["body"].Value : ExitWorkBody();
    }
}
