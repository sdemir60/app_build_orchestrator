using System.Linq;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// Harici köklerden gelen projeler AYRILMIŞ bir katmanda toplanır: adı <c>External</c>, indeksi −1. Amaç
/// listede ve grafta en üstte durmaları ve derleme sırasında ana repo projelerinden ÖNCE gelmeleridir — ana
/// projeler zaten onların çıktısına bağlıdır.
///
/// <para>Katman ataması <b>rozetten</b> okunur (<see cref="ProjectNode.ExternalVcs"/>), ayrı bir liste
/// taşınmaz. Kullanıcının pattern'leri haricilere UYGULANMAZ: eşleşseler bile <c>External</c>'da kalırlar ve
/// eşleşmeseler bile <c>Other</c>'a DÜŞMEZLER — <c>Other</c> ana reponun sınıflanmamış projeleri içindir.</para>
/// </summary>
public class ExternalLayerTests
{
    private static ProjectNode Main(string name, string[]? deps = null) =>
        new(Id: name, Name: name, ProjectPath: name, SolutionNames: [], Dependencies: deps ?? [],
            BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    private static ProjectNode External(string name, string[]? deps = null) =>
        Main(name, deps) with { ExternalVcs = VcsKind.Git };

    [Fact]
    public void An_external_project_lands_in_the_reserved_layer_even_with_no_patterns_configured()
    {
        // Katmansız kurulumda bile hariciler ayrılır: "en üstte" sözü pattern'lere bağlı olamaz.
        var result = LayerEngine.AssignLayers([Main("A"), External("Mail")], []);

        var mail = result.Nodes.Single(n => n.Name == "Mail");
        Assert.Equal(ExternalProjectsConventions.LayerName, mail.LayerName);
        Assert.Equal(ExternalProjectsConventions.LayerIndex, mail.LayerIndex);
        Assert.Null(result.Nodes.Single(n => n.Name == "A").LayerName); // ana proje isimsiz kalır
    }

    [Fact]
    public void Externals_come_first_in_build_order()
    {
        var result = LayerEngine.AssignLayers([Main("A"), Main("B"), External("Mail")], []);

        Assert.Equal(["Mail", "A", "B"], result.Nodes.Select(n => n.Name));
        Assert.Equal([0, 1, 2], result.Nodes.Select(n => n.BuildOrder)); // Nodes[i].BuildOrder == i korunur
    }

    [Fact]
    public void A_pattern_that_matches_an_external_does_not_move_it_out_of_the_reserved_layer()
    {
        LayerPattern[] patterns = [new(Order: 0, Regex: "Mail", Name: "Messaging")];

        var result = LayerEngine.AssignLayers([Main("A"), External("Mail")], patterns);

        var mail = result.Nodes.Single(n => n.Name == "Mail");
        Assert.Equal(ExternalProjectsConventions.LayerName, mail.LayerName);
        Assert.Equal(ExternalProjectsConventions.LayerIndex, mail.LayerIndex);
    }

    [Fact]
    public void An_external_that_no_pattern_matches_does_not_fall_into_other()
    {
        // Other, ana reponun sınıflanmamış projeleri içindir; harici oraya karışırsa listede en ALTA düşerdi.
        LayerPattern[] patterns = [new(Order: 0, Regex: "^A$", Name: "Alpha")];

        var result = LayerEngine.AssignLayers([Main("A"), Main("Zzz"), External("Mail")], patterns);

        Assert.Equal(ExternalProjectsConventions.LayerName, result.Nodes.Single(n => n.Name == "Mail").LayerName);
        Assert.Equal(LayerEngine.OtherLayerName, result.Nodes.Single(n => n.Name == "Zzz").LayerName);
        Assert.Equal(["Mail", "A", "Zzz"], result.Nodes.Select(n => n.Name));
    }

    [Fact]
    public void Externals_sit_above_every_configured_layer()
    {
        LayerPattern[] patterns = [new(Order: 0, Regex: "^A$", Name: "Alpha")];

        var result = LayerEngine.AssignLayers([Main("A"), External("Mail")], patterns);

        Assert.True(result.Nodes.Single(n => n.Name == "Mail").LayerIndex
                    < result.Nodes.Single(n => n.Name == "A").LayerIndex);
    }

    [Fact]
    public void A_workspace_without_externals_and_without_patterns_is_returned_untouched()
    {
        // Mevcut davranış bayt-bayt korunur: harici yoksa ve pattern yoksa liste HİÇ dokunulmadan döner.
        ProjectNode[] nodes = [Main("A"), Main("B")];

        var result = LayerEngine.AssignLayers(nodes, []);

        Assert.Same(nodes, result.Nodes);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_repository_project_depending_on_an_external_raises_no_reverse_layer_warning()
    {
        // Beklenen yön budur: ana proje harici projenin çıktısına bağlıdır, yani harici ÜSTTE (küçük indeks).
        var result = LayerEngine.AssignLayers([Main("A", ["Mail"]), External("Mail")], []);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void An_external_depending_on_a_repository_project_is_reported_as_a_reverse_layer_dependency()
    {
        // Ters yön SESSİZ geçmemeli: harici −1'de olduğu için kendi üreticisinden önce çıkar (warn-only,
        // scheduler bağımlılığa yine uyar). Kullanıcının pattern'lerini/kartlarını gözden geçirmesi gerekir.
        LayerPattern[] patterns = [new(Order: 0, Regex: "^A$", Name: "Alpha")];

        var result = LayerEngine.AssignLayers([Main("A"), External("Mail", ["A"])], patterns);

        Assert.Contains(result.Warnings, w => w.Contains("reverse layer dependency"));
    }
}
