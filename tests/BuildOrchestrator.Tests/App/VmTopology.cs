using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [topoloji kapısı] Run komutlarının (Build/Rebuild/RetryFailed) ön-koşulu: elde bir topoloji olmalı
/// (<see cref="RunViewModel.HasTopology"/>). Sync'siz bir Build motoru gerçekten derletir ama liste/graf/sayaç
/// BOŞ kalır, bu yüzden kapı VM'de kapatıldı — kapı KONUSU OLMAYAN testler (run-state bırakma, engine ölümü,
/// Sync guard'ı…) o ön-koşulu buradan kurar.
///
/// <para><b>Neden tek yer:</b> ön-koşul dört ayrı test dosyasında gerekiyor; her birinin kendi
/// <c>WorkspaceTopologyEvent</c>'ini kurması, aynı kurulumun dört kopyası olurdu (CLAUDE.md — kopya YASAK).</para>
///
/// <para><b>Varsayılan düğüm bilerek <c>C:\p\a.csproj</c> / "A"dır:</b> bu dosyalardaki testler satırlarını
/// zaten o Id ile kuruyor. <c>OnWorkspaceTopology</c> topolojide OLMAYAN satırları budadığı ve
/// <c>EnsureRow</c> aynı Id'yi bulup GÜNCELLEDİĞİ için, seed satır sayısını değiştirmez.</para>
/// </summary>
internal static class VmTopology
{
    /// <summary>Varsayılan tek düğümlü topoloji Id'si — testlerin satır kurarken kullandığı Id ile AYNI.</summary>
    public const string DefaultProjectId = @"C:\p\a.csproj";

    /// <summary>Verilen (ya da varsayılan) projelerden bir <c>workspaceTopology</c> akıtır: kapı açılır.
    /// Testin KENDİ event'lerinden ÖNCE çağrılmalıdır (topoloji, kendinde olmayan satırları budar).</summary>
    public static void Seed(RunViewModel vm, params string[] projectIds)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var ids = projectIds.Length > 0 ? projectIds : [DefaultProjectId];
        var nodes = ids.Select((id, i) => new ProjectNode(
            id, System.IO.Path.GetFileNameWithoutExtension(id), id, ["Osys"], [], i, null, null, false, null)).ToList();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
    }
}
