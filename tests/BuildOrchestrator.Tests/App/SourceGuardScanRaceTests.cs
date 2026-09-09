using System;
using System.IO;
using System.Linq;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kaynak guard'ları repo ağacını LİSTELEYİP sonra OKUR. Arada bir dosya silinirse okuma patlar ve guard,
/// denetlediği kuralla hiç ilgisi olmayan bir sebeple kırmızı verir — üstelik yalnız bazen, çünkü yarışı
/// açan şey az önce koşmuş bir build'dir.
///
/// <para>Bugünkü tek örnek WPF'in <c>MarkupCompilePass</c>'inin proje klasöründe üretip saniyeler içinde
/// sildiği <c>&lt;Ad&gt;_&lt;8hex&gt;_wpftmp.csproj</c>'tur. Üretim taraması (<see cref="WorkspaceScanner"/>)
/// onu baştan beri atlıyordu; guard taraması atlamıyordu ve tam süitte ara sıra kırmızı veriyordu (bir kez
/// <c>NoTurkishUserTextTests</c>'te görüldü, dosya-okuma anında). Karar artık TEK yerde
/// (<see cref="WorkspaceScanner.IsTransientBuildArtifact"/>) ve iki tarama da onu kullanır.</para>
/// </summary>
public class SourceGuardScanRaceTests
{
    [Theory]
    [InlineData(@"D:\r\src\App\BuildOrchestrator.App_a1b2c3d4_wpftmp.csproj", true)]
    [InlineData(@"D:\r\src\App\BuildOrchestrator.App_A1B2C3D4_WPFTMP.CSPROJ", true)]
    [InlineData(@"D:\r\src\App\BuildOrchestrator.App.csproj", false)]
    [InlineData(@"D:\r\src\App\App.xaml", false)]
    public void A_live_build_artifact_is_recognised_by_its_suffix(string path, bool expected)
        => Assert.Equal(expected, WorkspaceScanner.IsTransientBuildArtifact(path));

    [Fact]
    public void The_repo_scan_skips_a_wpf_temporary_project_that_a_live_build_may_delete()
    {
        // Gerçek ağaçta gerçek bir artefakt: guard'ın onu listelemediğini kanıtlar. Testin kendisi dosyayı
        // silmez-bırakmaz (finally) — repo ağacına iz bırakan bir test, bir sonraki guard koşusunu kirletirdi.
        string artifact = Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App_deadbeef_wpftmp.csproj");
        File.WriteAllText(artifact, "<Project />");
        try
        {
            Assert.DoesNotContain(RepoPaths.AppSourceFiles("*.csproj"), f =>
                f.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(RepoPaths.SrcSourceFiles("*.csproj"), f =>
                f.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(RepoPaths.RepoSourceFiles("*.csproj"), f =>
                f.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));

            // Gerçek proje dosyaları elenmemiş olmalı — filtre fazla geniş olsaydı guard sessizce boşalırdı.
            Assert.Contains(RepoPaths.AppSourceFiles("*.csproj"), f =>
                Path.GetFileName(f) == "BuildOrchestrator.App.csproj");
        }
        finally
        {
            File.Delete(artifact);
        }
    }
}
