using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [design v1.7.0 §3.1] Stop sonrası bir sonraki <b>Build</b> ne derler?
///
/// <para><b>DEĞİŞEN KURAL.</b> Eski iddia ayrı bir <c>RunMode.Continue</c> yüzeyine aitti
/// (<c>Scheduling/ContinueRunTests</c>): "sürdürme, tamamlanmış projeleri YENİDEN dispatch etmez ve kalan
/// build-order'ı takip eder". O mod kaldırıldı — Build zaten aynı sonucu veriyor ve bu sınıf bunu pinler:</para>
/// <list type="number">
///   <item>Tamamlanıp yeşil biten proje imzasını persist etmiştir → sonraki Build onu <c>up to date</c>
///     sayıp atlar; yeniden dispatch edilmez.</item>
///   <item>Öldürülen proje <c>LastResult=Failed</c> (reason=stopped) ile geçersizleşir → kirli kalır.</item>
///   <item>Hiç başlamamış proje zaten kirliydi (bu yüzden kuyruktaydı) → kirli kalır.</item>
/// </list>
///
/// <para><b>Bilinçli FARK:</b> elapsed devralınmaz — Stop'tan sonrası YENİ bir koşudur ve süre sıfırdan
/// sayar (§3.1). Eski Continue segmenti elapsed'i taşıyordu.</para>
/// </summary>
public class BuildAfterStopTests
{
    private static ProjectNode Node(string id, int buildOrder) =>
        new(id, id, id, [], [], buildOrder, null, null, InCycle: false, WillBuild: null);

    private static readonly Func<string, string> NoRead = _ => throw new InvalidOperationException("okunmamalıydı");

    private static Func<string, string> ContentMap(params (string Path, string Content)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Content, StringComparer.Ordinal);
        return path => map.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException(path);
    }

    /// <summary>
    /// Kesilen koşunun üç sınıfı: DONE yeşil bitti, KILLED derlenirken öldürüldü, QUEUED hiç başlamadı.
    /// Üçü de aynı Sync'te kirliydi (hepsinin kaynağı değişmişti). Sonraki Build: DONE atlanır, KILLED ve
    /// QUEUED derlenir — yani tam olarak eski Continue kümesi.
    /// </summary>
    [Fact]
    public void The_next_build_skips_what_finished_and_takes_what_was_killed_or_never_started()
    {
        var done = Node("DONE", 0);
        var killed = Node("KILLED", 1);
        var queued = Node("QUEUED", 2);
        var plan = new BuildPlan([done, killed, queued], [], "Debug");

        var fingerprints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DONE"] = "fpDone", ["KILLED"] = "fpKilled", ["QUEUED"] = "fpQueued",
        };
        Func<ProjectNode, string?> fp = node => fingerprints[node.Id];

        // Üçünün de kaynağı bu koşuda değişmişti (v2). DONE derlendi ve v2'nin imzasını persist etti.
        var read = ContentMap(("DONE.cs", "v2"), ("KILLED.cs", "v2"), ("QUEUED.cs", "v2"));
        Func<ProjectNode, IReadOnlyList<string>> dirty = node => [node.Id + ".cs"];

        string sigDone = BuildSignature.Compute(done, "Debug", "fpDone", ["DONE.cs"], read, _ => null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // Yeşil bitti → güncel imza persist edildi.
            ["DONE"] = new BuildState("DONE", sigDone, LastResult: BuildResult.Succeeded),
            // Öldürüldü → RunCoordinator LastResult=Failed yazar (torn/eksik çıktı "bilinen iyi" değildir).
            // İmza alanına dokunulmaz ama sonuç geçersiz olduğu için proje kirli kalır.
            ["KILLED"] = new BuildState("KILLED", sigDone, LastResult: BuildResult.Failed),
            // Hiç başlamadı → state'te kayıt yok (ya da eski); her hâlde kirli.
        };

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", dirty, read, fp, state,
            inPlace: true, buildCycles: false, mode: DependentMode.Safe);

        var willBuild = result.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);
        Assert.False(willBuild["DONE"]);
        Assert.True(willBuild["KILLED"]);
        Assert.True(willBuild["QUEUED"]);
    }
}
