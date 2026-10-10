using System.IO;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// Gerçek <c>MSBuild.exe</c> koşturan acceptance testlerinin ortak parçası (kopya YASAK): test çıktısına kopyalanan
/// <c>Fixtures\WpfMini</c> mini projeleri — kökündeki boş <c>Directory.Build.props</c> yukarı aramayı durdurur —, MSBuild'in
/// çözümü (çözülemezse test ATLANIR), bir MSBuild koşusu ve başarısız koşunun hata satırları.
/// </summary>
internal static class WpfMiniFixture
{
    /// <summary>Test çıktısına kopyalanan mini projeler (BuildOrchestrator.Tests.csproj: <c>Fixtures\WpfMini</c>).</summary>
    public static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WpfMini");

    /// <summary>Makinedeki <c>MSBuild.exe</c>; çözülemezse test atlanır — repo kuralı (<c>OsysRebuildAcceptanceTests</c>):
    /// ortam derleyemiyorsa ATLA, yalnız aracın kırdığı derlemede kırmızı.</summary>
    public static async Task<string> ResolveMsBuildOrSkipAsync()
    {
        string? path = null;
        string reason = string.Empty;
        try { path = (await new MsBuildResolver(new ProcessRunner()).ResolveAsync()).MsBuildExePath; }
        catch (MsBuildResolveException ex) { reason = ex.Message; }
        Skip.If(path is null, "MSBuild.exe could not be resolved — acceptance run skipped: " + reason);
        return path!;
    }

    /// <summary>Mini projelerin taze bir kopyası: <paramref name="to"/> altında fixture kökünün aynısı.</summary>
    public static void CopyTo(string to)
    {
        Directory.CreateDirectory(to);
        foreach (string directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(Root, directory)));
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(Root, file)));
    }

    public static Task<ProcessResult> RunAsync(string msbuild, IReadOnlyList<string> arguments, string workingDirectory)
        => new ProcessRunner().RunAsync(new ProcessSpec(msbuild, arguments, workingDirectory, TestPaths.WideRunTimeout));

    /// <summary>MSBuild çıktısındaki hata satırları (yoksa çıktının sonu): başarısızlık iletisi nedeni gösterir.</summary>
    public static string ErrorLines(ProcessResult result)
    {
        string[] lines = (result.StandardOutput + Environment.NewLine + result.StandardError)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] errors = [.. lines.Where(l => l.Contains(": error ", StringComparison.Ordinal)).Distinct()];
        return string.Join(Environment.NewLine, errors.Length > 0 ? errors : lines.TakeLast(15));
    }
}
