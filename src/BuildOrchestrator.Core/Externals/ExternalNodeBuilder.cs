using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// [D11] Bir harici proje incelemesini tel üzerindeki düğüme çevirir.
///
/// <para>Ayrı bir düğüm tipi AÇILMAZ: harici projeler sıradan <see cref="ProjectNode"/> olarak akar, yalnız
/// katmanları ve VCS rozetleri farklıdır. Bunun bedeli sıfır, kazancı büyüktür — liste gruplaması, graf
/// bandı, filtreler ve satır şablonları onları hiçbir değişiklik olmadan taşır.</para>
/// </summary>
public static class ExternalNodeBuilder
{
    /// <param name="inspection">Harici hakkında bilinenler (Sync'in okuması ya da koşu planlamasının kararı).</param>
    /// <param name="order">Düğümün build sırasındaki yeri — hariciler topolojinin BAŞINDA durur.</param>
    public static ProjectNode ToNode(ExternalInspection inspection, int order)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return new ProjectNode(
            Id: inspection.Target.TargetPath,                // kimlik = derlenecek hedef (build-state anahtarı)
            Name: inspection.Target.Name,
            ProjectPath: inspection.Target.TargetPath,
            SolutionNames: [Path.GetFileName(inspection.Target.TargetPath)],
            Dependencies: [],                                // haricilerin grafta kenarı yoktur
            BuildOrder: order,
            LayerIndex: ExternalProjectsConventions.LayerIndex,
            LayerName: ExternalProjectsConventions.LayerName,
            InCycle: false,
            WillBuild: inspection.WillBuild,
            WillBuildReason: inspection.Reason,
            ExternalVcs: inspection.Vcs);
    }
}
