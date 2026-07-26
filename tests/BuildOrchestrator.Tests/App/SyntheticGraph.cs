using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [G1/It-5] Ölçek testleri için DETERMİNİSTİK sentetik bağımlılık grafı üreteci. Bugüne kadarki graf fixture'ları
/// test-lokal ve küçüktü (3-4 düğüm); 500-1000 düğümlük ölçüm zemini için tek ortak kaynak burasıdır (kopya YASAK,
/// CLAUDE.md).
///
/// <para><b>Determinizm:</b> her çağrı KENDİ <see cref="Random"/>'ını <paramref name="seed"/>'den kurar — paylaşılan
/// bir generator YOKTUR, dolayısıyla aynı argümanlar her koşuda birebir aynı grafı verir (perf medyanı ancak böyle
/// karşılaştırılabilir).</para>
///
/// <para><b>Profil:</b> gerçek OSYS workspace'i referans alınır (~177 proje / ~6 katman —
/// <c>OsysGraphIntegrationTests.cs:30</c>): katman boyutları uçlarda ince, ortada şişkin; kenarlar ağırlıklı olarak
/// bir üst katmandan gelir, azınlığı daha yukarıdan atlar. Statü karışımı da koşu-ortası bir grafı taklit eder
/// (çoğu succeeded, birkaç building/queued, seyrek failed) — böylece kenar stili dallarının ve rozet yolunun
/// maliyeti de ölçüme girer.</para>
/// </summary>
internal static class SyntheticGraph
{
    /// <summary>Üretilen adların ortak öneki — <see cref="GraphNode.ShortName"/> gerçek workspace'teki gibi kırpılır.</summary>
    public const string NamePrefix = "OSYS.";

    /// <summary>Varsayılan tohum — sabit tutulur ki 500/1000 ölçümleri koşular arasında karşılaştırılabilsin.</summary>
    public const int DefaultSeed = 20260725;

    /// <param name="nodeCount">Toplam düğüm sayısı (≥ <paramref name="layerCount"/>).</param>
    /// <param name="layerCount">Katman sayısı (topolojik derinlik).</param>
    /// <param name="avgFanIn">Katman-0 dışındaki bir düğümün ortalama bağımlılık (fan-in) sayısı.</param>
    /// <param name="seed">Tohum — aynı tohum aynı grafı verir.</param>
    public static (IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges) Build(
        int nodeCount, int layerCount, double avgFanIn, int seed = DefaultSeed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(layerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, layerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(avgFanIn);

        var rng = new Random(seed); // ÇAĞRIYA ÖZEL — paylaşılan generator determinizmi bozardı
        int[] sizes = LayerSizes(nodeCount, layerCount);

        var nodes = new List<GraphNode>(nodeCount);
        var layerNames = new List<string>[layerCount];
        for (int layer = 0; layer < layerCount; layer++)
        {
            layerNames[layer] = new List<string>(sizes[layer]);
            for (int i = 0; i < sizes[layer]; i++)
            {
                string name = $"{NamePrefix}Synth.L{layer}.P{layerNames[layer].Count:D4}";
                layerNames[layer].Add(name);
                var (status, depIssue) = NextStatus(rng);
                nodes.Add(new GraphNode(name, layer, status, depIssue, NamePrefix));
            }
        }

        var edges = new List<GraphEdge>(nodeCount * 2);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        for (int layer = 1; layer < layerCount; layer++)
        {
            foreach (string name in layerNames[layer])
            {
                chosen.Clear();
                int fanIn = Math.Max(1, (int)Math.Round(avgFanIn, MidpointRounding.AwayFromZero) + rng.Next(-1, 2));
                for (int e = 0; e < fanIn; e++)
                {
                    // %70 doğrudan bir üst katman, kalanı daha yukarıdan atlayan bağımlılık (gerçek profil).
                    int sourceLayer = layer == 1 || rng.NextDouble() < 0.7 ? layer - 1 : rng.Next(0, layer);
                    var pool = layerNames[sourceLayer];
                    string from = pool[rng.Next(pool.Count)];
                    if (chosen.Add(from)) edges.Add(new GraphEdge(from, name));
                }
            }
        }

        return (nodes, edges);
    }

    /// <summary>Katman boyutları: uçlarda ince, ortada şişkin (üçgen ağırlık). Kümülatif yuvarlama kullanılır —
    /// toplam TAM OLARAK <paramref name="nodeCount"/> eder ve her katmanda en az 1 düğüm kalır.</summary>
    private static int[] LayerSizes(int nodeCount, int layerCount)
    {
        var weights = new double[layerCount];
        double total = 0;
        for (int layer = 0; layer < layerCount; layer++)
        {
            weights[layer] = 1 + Math.Min(layer, layerCount - 1 - layer);
            total += weights[layer];
        }

        var sizes = new int[layerCount];
        double cumulative = 0;
        int assigned = 0;
        for (int layer = 0; layer < layerCount; layer++)
        {
            cumulative += weights[layer];
            int target = (int)Math.Round(nodeCount * cumulative / total, MidpointRounding.AwayFromZero);
            sizes[layer] = target - assigned;
            assigned = target;
        }

        // Savunmacı: boş katman kalırsa en kalabalık katmandan bir düğüm ödünç alınır (topoloji delinmesin).
        for (int layer = 0; layer < layerCount; layer++)
        {
            if (sizes[layer] > 0) continue;
            int fattest = 0;
            for (int j = 1; j < layerCount; j++)
                if (sizes[j] > sizes[fattest]) fattest = j;
            sizes[fattest]--;
            sizes[layer] = 1;
        }

        return sizes;
    }

    /// <summary>Koşu-ortası bir grafın statü karışımı (yüzdeler tohumdan deterministik olarak çekilir).</summary>
    private static (GraphStatus Status, bool DepIssue) NextStatus(Random rng)
    {
        double roll = rng.NextDouble();
        var status = roll switch
        {
            < 0.55 => GraphStatus.Succeeded,
            < 0.70 => GraphStatus.Discovered,
            < 0.85 => GraphStatus.Queued,
            < 0.93 => GraphStatus.Building,
            < 0.97 => GraphStatus.Failed,
            _ => GraphStatus.Skipped,
        };
        return (status, rng.NextDouble() < 0.03); // ~%3 dep-hata rozeti
    }
}
