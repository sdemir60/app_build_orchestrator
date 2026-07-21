namespace BuildOrchestrator.App.Spikes;

/// <summary>
/// [It-4a Foundation] 36-proje örnek OSYS dependency grafı — design-v1
/// <c>prototype/app/build-data.js</c>'teki <c>LAYERS</c>/<c>P</c> (proje) dizilerinden BİREBİR PORT edilmiş
/// STATİK veri. Bu, simülasyon motoru DEĞİL (SimEngine/zamanlama/log üretimi kasıtlı olarak taşınmadı) —
/// yalnız topoloji (katman, bağımlılık, süre) + kaynak JS'teki iki statik işaret (dirty/fails). Sonraki
/// task'lar (T58 sticky header gruplama, T63 graf render) bu düğüm/kenar kümesini tüketir.
/// </summary>
public static class SampleGraphData
{
    /// <summary>build-data.js LAYERS[i].name'deki "OSYS." önekini atar (kısa ad — graf düğüm etiketi).
    /// [T63] Tek tanım <see cref="Graph.GraphNode.ShortLabel"/>'dedir (kopya YASAK) — bu yalnız devralınan addır.</summary>
    public static string ShortName(string fullName) => Graph.GraphNode.ShortLabel(fullName);

    public sealed record Layer(int Id, string Name);

    /// <summary>Bir proje düğümü. Name = kimlik (bu örnek veri kümesinde tekil; gerçek discovery'deki
    /// csproj-yolu Id kavramına karşılık gelmez — yalnız lab/örnek amaçlı).</summary>
    public sealed record Node(string Name, string Solution, int Layer, IReadOnlyList<string> Dependencies, int DurationMs, bool Dirty, bool Fails);

    /// <summary>Bir bağımlılık kenarı: From (bağımlılık) → To (bağımlı). build-data.js GRAPH.edges ile aynı yön
    /// (<c>p.deps.forEach(d => edges.push([d, p.name]))</c>).</summary>
    public sealed record Edge(string From, string To);

    public static readonly IReadOnlyList<Layer> Layers =
    [
        new(0, "Layer 0 — Core"),
        new(1, "Layer 1 — Infrastructure"),
        new(2, "Layer 2 — Domain"),
        new(3, "Layer 3 — Services"),
        new(4, "Layer 4 — API"),
        new(5, "Layer 5 — Client"),
    ];

    public static readonly IReadOnlyList<Node> Nodes =
    [
        // L0
        new("OSYS.Base", "Osys.sln", 0, [], 1600, Dirty: false, Fails: false),
        new("OSYS.Common.Contracts", "Osys.sln", 0, ["OSYS.Base"], 1300, Dirty: false, Fails: false),
        new("OSYS.Common.Utils", "Osys.sln", 0, ["OSYS.Base"], 1500, Dirty: false, Fails: false),
        // L1
        new("OSYS.Data.Core", "Osys.sln", 1, ["OSYS.Base", "OSYS.Common.Contracts"], 2600, Dirty: false, Fails: false),
        new("OSYS.Data.Migrations", "Osys.sln", 1, ["OSYS.Data.Core"], 1400, Dirty: false, Fails: false),
        new("OSYS.Security", "Osys.sln", 1, ["OSYS.Base", "OSYS.Common.Utils"], 1700, Dirty: false, Fails: false),
        new("OSYS.Shared.UI", "Osys.sln", 1, ["OSYS.Common.Utils"], 2100, Dirty: false, Fails: false),
        new("OSYS.Integration.Core", "Osys.sln", 1, ["OSYS.Common.Contracts"], 1800, Dirty: false, Fails: false),
        // L2
        new("OSYS.Domain.Vehicle", "Osys.sln", 2, ["OSYS.Data.Core"], 2200, Dirty: false, Fails: false),
        new("OSYS.Domain.Customer", "Osys.sln", 2, ["OSYS.Data.Core"], 2000, Dirty: false, Fails: false),
        new("OSYS.Domain.Inventory", "Osys.sln", 2, ["OSYS.Data.Core"], 1900, Dirty: false, Fails: false),
        new("OSYS.Domain.Service", "Osys.sln", 2, ["OSYS.Data.Core", "OSYS.Domain.Vehicle"], 2400, Dirty: true, Fails: false),
        new("OSYS.Domain.Parts", "Osys.sln", 2, ["OSYS.Data.Core", "OSYS.Domain.Inventory"], 2100, Dirty: true, Fails: false),
        new("OSYS.Domain.Finance", "Osys.sln", 2, ["OSYS.Data.Core"], 2300, Dirty: false, Fails: false),
        // L3
        new("OSYS.Sales.Core", "Osys.Sales.sln", 3, ["OSYS.Domain.Vehicle", "OSYS.Domain.Customer"], 2800, Dirty: true, Fails: true),
        new("OSYS.UsedCars.Core", "Osys.Sales.sln", 3, ["OSYS.Domain.Vehicle", "OSYS.Sales.Core"], 2200, Dirty: false, Fails: false),
        new("OSYS.Service.Scheduling", "Osys.Service.sln", 3, ["OSYS.Domain.Service", "OSYS.Domain.Customer"], 1900, Dirty: false, Fails: false),
        new("OSYS.Service.Workshop", "Osys.Service.sln", 3, ["OSYS.Domain.Service", "OSYS.Domain.Parts"], 2600, Dirty: false, Fails: false),
        new("OSYS.Parts.Inventory", "Osys.Parts.sln", 3, ["OSYS.Domain.Parts"], 1700, Dirty: false, Fails: false),
        new("OSYS.Parts.Catalog", "Osys.Parts.sln", 3, ["OSYS.Domain.Parts"], 2000, Dirty: false, Fails: false),
        new("OSYS.Finance.Invoicing", "Osys.sln", 3, ["OSYS.Domain.Finance"], 2400, Dirty: false, Fails: false),
        new("OSYS.Finance.Accounting", "Osys.sln", 3, ["OSYS.Domain.Finance", "OSYS.Finance.Invoicing"], 2600, Dirty: false, Fails: false),
        new("OSYS.Reporting.Core", "Osys.sln", 3, ["OSYS.Data.Core", "OSYS.Domain.Finance"], 2200, Dirty: true, Fails: false),
        // L4
        new("OSYS.Server.Api", "Osys.sln", 4, ["OSYS.Sales.Core", "OSYS.Service.Scheduling", "OSYS.Parts.Inventory", "OSYS.Security"], 3400, Dirty: true, Fails: false),
        new("OSYS.Sales.Api", "Osys.Sales.sln", 4, ["OSYS.Sales.Core", "OSYS.Security"], 2600, Dirty: false, Fails: false),
        new("OSYS.Service.Api", "Osys.Service.sln", 4, ["OSYS.Service.Scheduling", "OSYS.Service.Workshop", "OSYS.Security"], 2900, Dirty: false, Fails: false),
        new("OSYS.Parts.Api", "Osys.Parts.sln", 4, ["OSYS.Parts.Catalog", "OSYS.Parts.Inventory", "OSYS.Security"], 3100, Dirty: false, Fails: false),
        new("OSYS.Reporting.Api", "Osys.sln", 4, ["OSYS.Reporting.Core", "OSYS.Security"], 2300, Dirty: false, Fails: false),
        new("OSYS.Notifications.Api", "Osys.sln", 4, ["OSYS.Common.Utils", "OSYS.Security"], 1600, Dirty: false, Fails: false),
        new("OSYS.Integration.Api", "Osys.sln", 4, ["OSYS.Integration.Core", "OSYS.Security"], 1900, Dirty: false, Fails: false),
        // L5
        new("OSYS.Web.Portal", "Osys.Web.sln", 5, ["OSYS.Sales.Api", "OSYS.Reporting.Api", "OSYS.Shared.UI"], 4200, Dirty: true, Fails: true),
        new("OSYS.Web.DealerPortal", "Osys.Web.sln", 5, ["OSYS.Sales.Api", "OSYS.Shared.UI"], 3600, Dirty: false, Fails: false),
        new("OSYS.Client.Core", "Osys.Client.sln", 5, ["OSYS.Shared.UI", "OSYS.Common.Contracts"], 2400, Dirty: false, Fails: false),
        new("OSYS.Client.Sales", "Osys.Client.sln", 5, ["OSYS.Client.Core", "OSYS.Sales.Api"], 3200, Dirty: true, Fails: false),
        new("OSYS.Client.Service", "Osys.Client.sln", 5, ["OSYS.Client.Core", "OSYS.Service.Api"], 2800, Dirty: false, Fails: false),
        new("OSYS.Mobile.Api", "Osys.Mobile.sln", 5, ["OSYS.Service.Api", "OSYS.Parts.Api", "OSYS.Security"], 2600, Dirty: false, Fails: false),
    ];

    /// <summary>build-data.js GRAPH.edges portu: her düğümün her bağımlılığı için bir kenar (From=bağımlılık,
    /// To=bağımlı proje). Türetilmiş — Nodes tek kaynak.</summary>
    public static readonly IReadOnlyList<Edge> Edges =
        Nodes.SelectMany(n => n.Dependencies.Select(d => new Edge(d, n.Name))).ToList();
}
