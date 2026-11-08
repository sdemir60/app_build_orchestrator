using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] App'in iki kökü vardır: Settings'teki repo kökü (<c>RootPath</c> — kalıcı durumun <c>RepositoryRoot</c>'u, Settings'in
/// gösterdiği, worktree'nin eklendiği kök) ve aktif kök (<c>ActiveRoot</c> — Repo ya da Worktree, hangisi seçiliyse). Motora giden her
/// komut, HEAD izleyicisi ve git işlem kapısı AKTİF kökte çalışır; tek istisna worktree'nin oluşturulmasıdır (ana repodan eklenir).
///
/// <para><b>Neden guard:</b> repo kopyasındayken iki kök aynıdır — yanlış kökü alan yeni bir komut her testte yeşil kalır ve yalnız
/// worktree aktifken, kullanıcının makinesinde, ana kopyayı derleyerek ya da temizleyerek ortaya çıkar.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> kökü bir değişkene alıp komuta onu vermek. Guard çağrı biçimine bakar; niyetin
/// denetimi review'ın işidir.</para>
/// </summary>
public sealed class WorktreeRootGuardTests
{
    /// <summary>Repo kökünden kurulan bir motor komutu — worktree'nin oluşturulması hariç. Kök argüman listesinin herhangi bir
    /// yerinde olabilir (koşu komutunda üçüncüdür); arama deyimin sonuna (<c>;</c>) kadar sürer, başka bir nesnenin üyesi
    /// (<c>e.RootPath</c>) sayılmaz.</summary>
    private static readonly Regex CommandFromRepositoryRoot =
        new(@"new\s+(?!CreateWorktreeCommand\b)\w+Command\s*\([^;]*?(?<![.\w])RootPath\b", RegexOptions.Compiled);

    /// <summary>HEAD izleyicisinin bağlanması ya da git işlem kapısının yoklaması repo kökünden.</summary>
    private static readonly Regex GitReadFromRepositoryRoot =
        new(@"\b(?:Attach|InspectGitOperation)\s*\(\s*RootPath\b", RegexOptions.Compiled);

    [Fact]
    public void No_engine_command_is_built_from_the_repository_root()
    {
        var offenders = SourceGuard.ScanApp("*.cs", CommandFromRepositoryRoot, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "Motor komutu repo kökünden kuruluyor (aktif kök ActiveRoot olmalı):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_head_watcher_and_the_git_gate_follow_the_active_root()
    {
        var offenders = SourceGuard.ScanApp("*.cs", GitReadFromRepositoryRoot, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "Git okuması repo kökünde (aktif kök ActiveRoot olmalı):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_rules_recognise_the_repository_root_sneaking_back()
    {
        Assert.Single(SourceGuard.ScanText("ViewModels/RunViewModel.cs",
            "var cmd = new StartRunCommand(runId, mode, RootPath, Configuration);", CommandFromRepositoryRoot));
        Assert.Single(SourceGuard.ScanText("ViewModels/RunViewModel.cs",
            "await TrySendAsync(new CleanWorkspaceCommand(RootPath, ExternalProjectsForWire), \"clean\");", CommandFromRepositoryRoot));
        Assert.Empty(SourceGuard.ScanText("ViewModels/RunViewModel.Worktree.cs",
            "await TrySendAsync(new CreateWorktreeCommand(RootPath, folder), \"createWorktree\");", CommandFromRepositoryRoot));
        Assert.Single(SourceGuard.ScanText("ViewModels/RunViewModel.AutoSync.cs",
            "_autoSync.Attach(RootPath);", GitReadFromRepositoryRoot));
    }
}
