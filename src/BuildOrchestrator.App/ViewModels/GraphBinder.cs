using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D5/T50] VM'in topolojisini + satır VM'lerini <see cref="GraphView"/>'in beslemesine (<see cref="GraphNode"/>/
/// <see cref="GraphEdge"/>) çeviren SAF çekirdek — WPF/process bağımsız, tek başına test edilir. <c>GraphView</c>
/// yalnız TÜKETİCİDİR (yeniden yazılmaz); D5'in işi besleme.
///
/// <para><b>Anahtarlar (kritik):</b> <see cref="GraphView"/> düğümleri <see cref="GraphNode.Id"/> ile,
/// <see cref="QuietGraphLayout"/> konumları yine <c>Id</c> ile anahtarlar — dolayısıyla
/// <see cref="GraphEdge.From"/>/<see cref="GraphEdge.To"/> de proje Id'sidir (tam csproj yolu) ve
/// <see cref="ProjectNode.Dependencies"/> HİÇ ÇEVRİLMEDEN geçer. Ad benzersiz olmadığı için (iki kök aynı
/// <c>AssemblyName</c>'i üretebilir) kimlik olamaz — bkz. <see cref="GraphNode"/>.</para>
///
/// <para><b>Statü otoritesi:</b> <see cref="StatusOf"/> eşlemeyi YENİDEN yazmaz — satır varsa
/// <see cref="ProjectRowViewModel.Status"/>'a delege eder (State/InCycle/<see cref="ProjectRowViewModel.InRunQueue"/>/
/// IsRunActive'in TEK eşleme yeri). İkinci bir otorite (çift switch) bir review kusuru olurdu.</para>
/// </summary>
public static class GraphBinder
{
    /// <summary>Topolojiyi graf düğümlerine çevirir (build-order KORUNUR — bant-içi sıra da build-order'dır).
    /// Bant = <see cref="Bands"/> (listenin grubunun eşleniği), statü = <see cref="StatusOf"/> (satır Id ile eşlenir).
    ///
    /// <para>[quiet] Kısa-ad öneki ve dep-hata bayrağı ARTIK TAŞINMAZ: v1.3.0 §2.3'te düğümün üstünde ad
    /// etiketi ve graf içi dep-issue rozeti yoktur, dolayısıyla grafın ikisini de okuyan bir yeri kalmadı.
    /// (Önek otoritesi <see cref="GraphNode.CommonDotPrefix"/> olarak duruyor — onu liste kartı ve şerit
    /// okuyor.)</para></summary>
    public static IReadOnlyList<GraphNode> Nodes(
        IReadOnlyList<ProjectNode> topology, IReadOnlyDictionary<string, ProjectRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(rows);

        var bands = Bands(topology);

        var result = new List<GraphNode>(topology.Count);
        foreach (var node in topology)
        {
            rows.TryGetValue(node.Id, out var row);
            // Elde topoloji varsa Sync yapılmıştır (bu metot yalnız o zaman çağrılır) → synced: true.
            var status = StatusOf(row, synced: true);
            // [design v1.20.0 §2.3] TEK renk kanalı: satırın görsel durumu grafa AYNEN taşınır — iki yüzey
            // (liste + graf) tek durumdan beslenir ve ikinci bir eşleme YAZILMAZ. Satır henüz yoksa
            // (topoloji düğümünün satırı kurulmamış — savunmacı) durum bilinmez: karar satırla gelir.
            // Döngü üyeliği ayrı taşınır — yalnız küpü boyar, rengi ezmez.
            var visual = row?.VisualStatus ?? VisualStatus.Unknown;
            bool inCycle = row?.InCycle ?? node.InCycle;
            result.Add(new GraphNode(node.Id, node.Name, bands[node.Id], status, visual, inCycle));
        }
        return result;
    }

    /// <summary>Bağımlılık kenarları: her düğüm N ve her <c>depId</c> için, <c>depId</c> topolojide bir düğüme
    /// çözülüyorsa <c>GraphEdge(From: depId, To: N.Id)</c> (bağımlılık→bağımlı). Topolojide olmayan dep atlanır.
    /// Build-order KORUNUR (topoloji sırasında gezilir). Uçlar Id'dir: Id→Ad çevirisi çakışan adlarda kenarı
    /// YANLIŞ düğüme bağlardı, ve çeviri kalktığı için ara harita da gerekmez.</summary>
    public static IReadOnlyList<GraphEdge> Edges(IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in topology) known.Add(node.Id);

        var edges = new List<GraphEdge>();
        foreach (var node in topology)
            foreach (var depId in node.Dependencies)
                if (known.Contains(depId))
                    edges.Add(new GraphEdge(depId, node.Id));
        return edges;
    }

    /// <summary>Her düğümün bandı (Id → bant): listenin grubunun eşleniği — <see cref="LayerGrouping.BandOf"/>. Proje
    /// listede hangi gruptaysa grafta o grubun bandındadır; katman tanımı yokken liste tek düz liste, graf tek
    /// banttır (harici projeler varsa kendi <c>External</c> grupları/bantları üstte).
    /// <para>Bant topolojik derinlikten TÜRETİLMEZ: derinlik bantları gerçek OSYS'te 28 bant verdi (177 düğüm), panele
    /// sığdırma da düğümü 8 px tabanına indirdi — ve liste aynı anda tek düz liste gösteriyordu.</para></summary>
    public static IReadOnlyDictionary<string, int> Bands(IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return BandMemo.GetValue(topology, static t => { _bandComputations++; return LayerGrouping.BandOf(t); });
    }

    /// <summary>[perf Faz B · B5] Topoloji REFERANSI başına bir kez hesaplanan bantlar (<see cref="Bands"/> ve
    /// <see cref="Nodes"/> aynı önbellekten geçer). Topoloji yalnız Sync'te değişir — VM yeni bir liste atar, eskisini
    /// değiştirmez — ama graf her statü değişiminde <see cref="Nodes"/>'u çağırır. Anahtar zayıf tutulur: eski topoloji
    /// gidince girdisi de gider. Döndürülen sözlük PAYLAŞIMLIDIR — salt okunurdur.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<ProjectNode>, IReadOnlyDictionary<string, int>> BandMemo = new();

    /// <summary>[perf Faz B · B5 test yüzeyi] Bantların gerçekten HESAPLANDIĞI (önbellekten gelmediği) çağrı sayısı.
    /// İş parçacığına özeldir: paralel koşan başka sınıfların graf çağrıları bir testin sayısını bozmaz.</summary>
    [ThreadStatic] private static int _bandComputations;
    internal static int BandComputations => _bandComputations;

    /// <summary>Graf statüsü — eşleme TEK otoriteden (<see cref="ProjectRowViewModel.Status"/>) gelir:
    /// <list type="bullet">
    /// <item>Sync yapılmamışsa (<paramref name="synced"/> false) her şey <see cref="GraphStatus.Discovered"/>.</item>
    /// <item>Satır yoksa (topoloji düğümünün henüz satırı yok — savunmacı) <see cref="GraphStatus.Discovered"/>.</item>
    /// <item>Satır varsa doğrudan <see cref="ProjectRowViewModel.Status"/> (State+InRunQueue+IsRunActive'in tek
    /// eşleme yeri). Burada eşleme KOPYALANMAZ. Döngü üyeliği bu kanalda DEĞİLDİR (design v1.7.0 §5): düğümün
    /// kendi <c>InCycle</c> alanında taşınır ve çekirdek rengini belirler.</item>
    /// </list></summary>
    public static GraphStatus StatusOf(ProjectRowViewModel? row, bool synced)
    {
        if (!synced) return GraphStatus.Discovered;
        return row?.Status ?? GraphStatus.Discovered;
    }
}
