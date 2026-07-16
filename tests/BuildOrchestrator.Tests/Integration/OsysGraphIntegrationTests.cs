using System.IO;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Planning;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [T71/It-1 acceptance] Gerçek OSYS reposunda (D:\Projects\Delta\OSYS) tam Sync/graph
/// pipeline'ının spike S3 3-sınıf modeline uyduğunu ve cache-hit'in hızlı olduğunu kanıtlar.
/// OSYS bu makinede yoksa SkippableFact ile atlanır.
/// </summary>
[Trait("Category", "Integration")]
public class OsysGraphIntegrationTests
{
    private const string OsysRoot = @"D:\Projects\Delta\OSYS";

    [SkippableFact]
    public void osys_scan_classifies_per_spike_3class_model_and_cache_hits()
    {
        Skip.IfNot(Directory.Exists(OsysRoot), "OSYS yok — entegrasyon atlandı.");
        string cachePath = Path.Combine(Path.GetTempPath(), "osys-it1-cache-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var scanner = new WorkspaceScanner();
            var scan = scanner.Scan(OsysRoot);
            Assert.InRange(scan.CsprojPaths.Count, 150, 260); // spike yer-gerçeği ~177

            var evaluator = new CsprojEvaluator();
            var evaluated = scan.CsprojPaths.Select(evaluator.Evaluate).ToList();
            var producers = ProducerMapBuilder.Build(evaluated);
            Assert.Empty(producers.AmbiguousDlls);              // spike: 0 ambiguity

            var report = HintPathClassifier.Classify(evaluated, producers);
            // repo-içi çözülme = Edge/(Edge+Unclassified) — spike 3-sınıf modeli: sınıflandırılamayan artık küçük olmalı
            Assert.True(report.RepoResolveRatio >= 0.95,
                $"repo-resolve {report.RepoResolveRatio:P1} < %95 — sınıflandırılamayan: {report.UnclassifiedCount}");

            // cache-hit: ikinci koşu yeniden değerlendirmemeli
            var cache = new EvaluationCache(cachePath);
            int calls = 0;
            EvaluatedProject Counting(string p) { calls++; return evaluator.Evaluate(p); }
            foreach (var p in scan.CsprojPaths) cache.GetOrEvaluate(p, Counting);
            int afterCold = calls;
            foreach (var p in scan.CsprojPaths) cache.GetOrEvaluate(p, Counting);
            Assert.Equal(afterCold, calls);                     // warm koşu: 0 yeni eval [cache-hit hızlı]

            // build-order + cycle rozeti verisi mevcut
            var plan = new BuildPlanBuilder(scanner, evaluator, new EvaluationCache(cachePath + ".2"))
                .Build(OsysRoot, "Debug");
            Assert.Equal(evaluated.Count, plan.Nodes.Count);
            Assert.True(plan.Nodes.Select(n => n.BuildOrder).SequenceEqual(Enumerable.Range(0, plan.Nodes.Count)));
        }
        finally { if (File.Exists(cachePath)) File.Delete(cachePath); }
    }
}
