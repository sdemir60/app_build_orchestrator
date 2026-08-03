using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Settings'in VARSAYILAN katman tanımları (<see cref="LayerDefaults"/>). OSYS çözümünde proje adları
/// <c>OSYS.&lt;Katman&gt;.&lt;Proje…&gt;</c> biçimindedir; varsayılanlar bu dört sabit öneki katman sırasıyla
/// tanımlar. Liste TEK yerdedir — taslak kurulumu ve "Restore default layers" aynı kaynaktan okur.
/// </summary>
public class LayerDefaultsTests
{
    private static ProjectNode N(string name) =>
        new(Id: $@"D:\repo\{name}\{name}.csproj", Name: name, ProjectPath: $@"D:\repo\{name}\{name}.csproj",
            SolutionNames: [], Dependencies: [], BuildOrder: 0, LayerIndex: null, LayerName: null,
            InCycle: false, WillBuild: null);

    [Fact] // Ad, regex ve SIRA birebir pinlenir — bu liste kullanıcıya varsayılan olarak sunulan sözleşmedir.
    public void Default_layers_are_the_four_OSYS_prefixes_in_order()
    {
        Assert.Equal(
            ["OSYS.Types", "OSYS.Business", "OSYS.Orchestration", "OSYS.UI"],
            LayerDefaults.Layers.Select(l => l.Name));
        Assert.Equal(
            [@"^OSYS\.Types\.", @"^OSYS\.Business\.", @"^OSYS\.Orchestration\.", @"^OSYS\.UI\."],
            LayerDefaults.Layers.Select(l => l.Regex));
    }

    [Fact] // Varsayılanlar GERÇEK proje adlarına karşı Core'da çalışır: önek eşleşir, geri kalanı Other'a düşer.
    public void Default_layers_group_real_OSYS_project_names_and_drop_the_rest_into_Other()
    {
        // Order = liste indeksi. Üretimde bu eşleme SettingsDraftViewModel.BuildPatterns()'tedir (taslak satır
        // indeksinden) ve orada ayrıca test edilir; burada yalnız varsayılanların Core'da ne ürettiği ölçülür.
        var patterns = LayerDefaults.Layers.Select((l, i) => new LayerPattern(i, l.Regex, l.Name)).ToList();

        ProjectNode[] nodes =
        [
            N("OSYS.Types.Service.WorkOrder"),
            N("OSYS.Business.Service.WorkOrder"),
            N("OSYS.Orchestration.Service.WorkOrder"),
            N("OSYS.UI.Service.WorkOrder"),
            N("Contoso.Tools.Cli"),   // hiçbir önekle eşleşmez
            N("OSYS.Types"),          // çıplak önek: nokta YOK → bilinçli olarak eşleşmez
        ];

        var result = LayerEngine.AssignLayers(nodes, patterns);
        var byName = result.Nodes.ToDictionary(n => n.Name, n => n.LayerName);

        Assert.Equal("OSYS.Types", byName["OSYS.Types.Service.WorkOrder"]);
        Assert.Equal("OSYS.Business", byName["OSYS.Business.Service.WorkOrder"]);
        Assert.Equal("OSYS.Orchestration", byName["OSYS.Orchestration.Service.WorkOrder"]);
        Assert.Equal("OSYS.UI", byName["OSYS.UI.Service.WorkOrder"]);
        Assert.Equal(LayerEngine.OtherLayerName, byName["Contoso.Tools.Cli"]);
        Assert.Equal(LayerEngine.OtherLayerName, byName["OSYS.Types"]);
    }
}
