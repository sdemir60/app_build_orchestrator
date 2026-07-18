using System.Xml;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Supervisor;

/// <summary>
/// [T72/Task 14] SPIKE S2 (OSYS.Types.NewSales.Print vakası) — run başında IN-PLACE (worktree-izole OLMAYAN)
/// projeler için <see cref="StaleObjDetector.Inspect"/>'i tetikler: bayat obj (yabancı-TFM restore artığı)
/// bulunan her proje için TEK bir warn satırı üretir. <see cref="StaleObjDetector"/> gibi hiçbir dosyaya
/// DOKUNMAZ/silmez ve ASLA fırlatmaz — csproj okunamazsa (yok/bozuk/erişilemez, ör. bu sınıfın testlerindeki
/// fake node'lar ya da RunCoordinatorTests'in gerçek dosyası olmayan Node() fixture'ları) ya da TFM
/// türetilemezse (ne TargetFrameworkVersion ne TargetFramework var) o proje SESSİZCE atlanır — warn-only,
/// correctness dependency değil.
/// </summary>
public static class StaleObjRunStartWarner
{
    /// <param name="nodes">Taranacak projeler (tipik: run'ın BuildPlan.Nodes'u).</param>
    /// <param name="emit">Bayat bulunan HER proje için tek satır çağrılır (çağıran taraf console/decision.log'a yazar).</param>
    /// <param name="evaluator">Enjekte edilebilir (testlerde tekrar kullanım için); verilmezse yeni bir <see cref="CsprojEvaluator"/>.</param>
    public static void WarnStaleObj(IReadOnlyList<ProjectNode> nodes, Action<string> emit, CsprojEvaluator? evaluator = null)
    {
        var eval = evaluator ?? new CsprojEvaluator();
        foreach (var node in nodes)
        {
            string? tfm;
            try { tfm = eval.Evaluate(node.Id).TargetFrameworkMoniker; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException
                or ArgumentException or PathTooLongException or NotSupportedException)
            { continue; } // csproj yok/bozuk/erişilemez — teşhis edilemez, warn yok [never-throw]
            if (tfm is null) continue; // TargetFrameworkVersion/TargetFramework yok — teşhis edilemez

            var diagnosis = StaleObjDetector.Inspect(node.Id, tfm);
            if (diagnosis.IsStale)
                emit($"warning: {node.Name}: {diagnosis.Reason}");
        }
    }
}
