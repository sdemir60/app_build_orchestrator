using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Workspace;
using Xunit;

namespace BuildOrchestrator.Tests.Workspace;

/// <summary>
/// [Task 3] <see cref="LocalEdits.ProjectsWithLocalEdits"/>: SAF hesap — girdi ve dirty-yol kümeleri elle
/// verilir, disk/git'e HİÇ DOKUNULMAZ. Sync'in kendisiyle olan entegrasyonu (sahte git üzerinden)
/// <c>SyncWorkspaceServiceTests</c>'tedir; burası yalnız kesişim mantığını pinler.
/// </summary>
public class LocalEditsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "bo-local-edits-fixture");
    private const string ProjectId = "proj-A";

    private static IReadOnlyDictionary<string, IReadOnlyList<ProjectInput>> InputsFor(params string[] logicalPaths) =>
        new Dictionary<string, IReadOnlyList<ProjectInput>>
        {
            [ProjectId] = [.. logicalPaths.Select(p => new ProjectInput(p, p))],
        };

    /// <summary>Proje klasörünün dışındaki bir dirty dosya projeyi işaretlemez — kesişim GERÇEKTEN boş kalmalı.</summary>
    [Fact]
    public void A_dirty_file_outside_the_project_folder_marks_nothing()
    {
        var inputs = InputsFor(Path.Combine(Root, "A", "A.csproj"), Path.Combine(Root, "A", "A.cs"));

        var result = LocalEdits.ProjectsWithLocalEdits(["B/B.cs"], inputs, Root);

        Assert.Empty(result);
    }

    /// <summary>Csproj'un KENDİSİ girdi kümesindedir (ProjectInputs.Collect kuralı) — dirty ise proje işaretlenir.</summary>
    [Fact]
    public void The_csproj_file_itself_being_dirty_marks_the_project()
    {
        var inputs = InputsFor(Path.Combine(Root, "A", "A.csproj"), Path.Combine(Root, "A", "A.cs"));

        var result = LocalEdits.ProjectsWithLocalEdits(["A/A.csproj"], inputs, Root);

        Assert.Contains(ProjectId, result);
    }

    /// <summary>Ruling: `git status --porcelain` (-uall OLMADAN) yeni bir untracked klasörü TEK bir `dir/`
    /// satırıyla bildirir — `/` ile biten bir dirty yol bu yüzden bir DİZİN ÖNEKİDİR, altındaki her girdi
    /// dirty sayılır.</summary>
    [Fact]
    public void A_dirty_directory_prefix_marks_every_input_under_it()
    {
        var inputs = InputsFor(Path.Combine(Root, "A", "Sub", "New.cs"));

        var result = LocalEdits.ProjectsWithLocalEdits(["A/Sub/"], inputs, Root);

        Assert.Contains(ProjectId, result);
    }

    /// <summary>Boş dirty-yol kümesi (temiz repo YA DA başarısız sorgunun çağıran tarafından düşürülmüş hâli)
    /// hiçbir projeyi işaretlemez.</summary>
    [Fact]
    public void An_empty_dirty_path_list_marks_nothing()
    {
        var inputs = InputsFor(Path.Combine(Root, "A", "A.csproj"));

        var result = LocalEdits.ProjectsWithLocalEdits([], inputs, Root);

        Assert.Empty(result);
    }
}
