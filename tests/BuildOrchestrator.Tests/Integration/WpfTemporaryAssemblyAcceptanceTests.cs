using System.IO;
using System.Security.Cryptography;
using System.Text;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [RESOLVE Faz 2 · KABUL] WPF'in yerel tipli XAML için derlediği geçici assembly (<c>*_wpftmp</c>) gövdesiz
/// (metadata-only) derlendiğinde (<see cref="WpfTemporaryAssemblyTargets"/>) NİHAİ çıktının DEĞİŞMEDİĞİNİ gerçek
/// <c>MSBuild.exe</c> ile kanıtlar. Her mini proje geçici bir klasöre kopyalanır ve iki kez derlenir — targets'sız ve
/// targets'lı; ikisi de motorun kendi argüman planıyla (<see cref="MsBuildArguments.PlanFor"/>). Karşılaştırılan:
/// <c>obj</c> altındaki BAML dosyalarının yolu + baytları ve çıktı DLL'inin boyutu; ayrıca çıktı DLL'inde reference
/// assembly işareti ya da targets'ın friend assembly adı OLMAMALI (targets yalnız geçici projede etki eder).
///
/// <para>Üç dal, targets'ın üç öğesini birlikte pinler: SDK-style proje (<c>ProduceReferenceAssembly=false</c>; yoksa
/// <c>/refout</c> ile <c>/refonly</c> çakışır, CS8308), internal özelliğe bağlanan XAML'li eski-stil proje (geçici
/// assembly'deki <c>InternalsVisibleTo</c>; yoksa MC3072) ve yalnız public üyelere bağlanan eski-stil proje. Süre
/// kazancı burada ÖLÇÜLMEZ (o perf ölçümünün işi); bu test yalnız "çıktı aynı" der.</para>
///
/// <para><b>Bu suite normal koşudan HARİÇTİR</b> (<c>[Trait("Category","Acceptance")]</c>): <c>dotnet test … --filter
/// "Category=Acceptance"</c>. Gerçek bir Visual Studio / Build Tools MSBuild'i ister (eski-stil mini proje ayrıca .NET
/// Framework 4.6 targeting pack'ini); MSBuild çözülemezse <see cref="SkippableFactAttribute"/> ile ATLANIR (fail
/// değil). Repo ya da OSYS gerekmez: girdi, test çıktısına kopyalanan <c>Fixtures\WpfMini</c> mini projeleridir.</para>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class WpfTemporaryAssemblyAcceptanceTests
{
    /// <summary>Test çıktısına kopyalanan mini projeler (BuildOrchestrator.Tests.csproj: <c>Fixtures\WpfMini</c>).</summary>
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WpfMini");

    [SkippableFact]
    public async Task Sdk_style_project_builds_the_same_output_with_the_targets()
        => await AssertSameOutputAsync(@"Sdk\MiniSdk.csproj", needsRestore: true);

    [SkippableFact]
    public async Task Old_style_xaml_binding_an_internal_member_builds_the_same_output_with_the_targets()
        => await AssertSameOutputAsync(@"Old\Mini.csproj", needsRestore: false, "-p:MiniView=ViewInternal.xaml");

    [SkippableFact]
    public async Task Old_style_project_builds_the_same_output_with_the_targets()
        => await AssertSameOutputAsync(@"Old\Mini.csproj", needsRestore: false, "-p:MiniView=ViewPublic.xaml");

    // ---------------------------------------------------------------- karşılaştırma

    /// <summary>Bir koşunun karşılaştırılan çıktıları.</summary>
    private sealed record Output(string BamlDigest, long DllLength, bool IsReferenceAssembly, bool HasFriendMarker);

    private static async Task AssertSameOutputAsync(string project, bool needsRestore, params string[] extraBuildArguments)
    {
        string msbuild = await ResolveMsBuildOrSkipAsync();
        using var scratch = new TempDir();

        // Targets, projelerin KENDİ klasörlerinin dışında durur: SDK-style varsayılan glob'u friend .cs dosyasını
        // gerçek derlemeye alırdı ve "friend işareti çıktıda yok" kanıtı boşa çıkardı.
        string targets = WpfTemporaryAssemblyTargets.EnsureWritten(Path.Combine(scratch.Path, "cache"));

        Output plain = await BuildAsync(msbuild, Path.Combine(scratch.Path, "plain"), project, needsRestore, null, extraBuildArguments);
        Output lean = await BuildAsync(msbuild, Path.Combine(scratch.Path, "targets"), project, needsRestore, targets, extraBuildArguments);

        // İşaretler önce: targets gerçek derlemeye sızarsa kırılma nedeni boyut farkı değil, işaretin kendisidir.
        foreach (var (label, output) in new[] { ("without the targets", plain), ("with the targets", lean) })
        {
            Assert.False(output.IsReferenceAssembly, $"{label}: the output assembly is a reference assembly (metadata only)");
            Assert.False(output.HasFriendMarker, $"{label}: the output assembly carries the temporary assembly's friend name");
        }
        Assert.Equal(plain.BamlDigest, lean.BamlDigest);
        Assert.Equal(plain.DllLength, lean.DllLength);
    }

    private static async Task<string> ResolveMsBuildOrSkipAsync()
    {
        string? path = null;
        string reason = string.Empty;
        try { path = (await new MsBuildResolver(new ProcessRunner()).ResolveAsync()).MsBuildExePath; }
        catch (MsBuildResolveException ex) { reason = ex.Message; }
        Skip.If(path is null, "MSBuild.exe could not be resolved — acceptance run skipped: " + reason);
        return path!;
    }

    /// <summary>Mini projeyi taze bir kopyada derler (gerekirse önce restore) ve çıktısını ölçer.</summary>
    private static async Task<Output> BuildAsync(string msbuild, string copyRoot, string project, bool needsRestore,
        string? customBeforeTargets, string[] extraBuildArguments)
    {
        CopyDirectory(FixtureRoot, copyRoot);
        string projectPath = Path.Combine(copyRoot, project);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string label = customBeforeTargets is null ? "without the targets" : "with the targets";

        // Motorun kendi argüman planı: SDK-style proje önce restore ister; targets yalnız build listesine girer.
        var (restore, build) = MsBuildArguments.PlanFor(
            new MsBuildInvokeRequest(projectPath, "Debug", projectDirectory, needsRestore, CustomBeforeTargets: customBeforeTargets));
        if (restore is not null)
            await RunAsync(msbuild, restore, projectDirectory, $"restore {label}");
        await RunAsync(msbuild, [.. build, .. extraBuildArguments], projectDirectory, $"build {label}");

        return Measure(projectDirectory, Path.GetFileNameWithoutExtension(project));
    }

    private static async Task RunAsync(string msbuild, IReadOnlyList<string> arguments, string workingDirectory, string what)
    {
        ProcessResult result = await new ProcessRunner().RunAsync(
            new ProcessSpec(msbuild, arguments, workingDirectory, TestPaths.WideRunTimeout));
        Assert.True(result.Success,
            $"MSBuild {what} failed (exit {result.ExitCode}{(result.TimedOut ? ", timed out" : string.Empty)}):"
            + Environment.NewLine + ErrorLines(result));
    }

    /// <summary>MSBuild çıktısındaki hata satırları (yoksa çıktının sonu): başarısızlık iletisi nedeni gösterir.</summary>
    private static string ErrorLines(ProcessResult result)
    {
        string[] lines = (result.StandardOutput + Environment.NewLine + result.StandardError)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] errors = [.. lines.Where(l => l.Contains(": error ", StringComparison.Ordinal)).Distinct()];
        return string.Join(Environment.NewLine, errors.Length > 0 ? errors : lines.TakeLast(15));
    }

    private static Output Measure(string projectDirectory, string assemblyName)
    {
        // BAML: yol + bayt. Yol, iki koşunun aynı göreli yerleşimde BAML ürettiğini de pinler (obj düzeni değişmez).
        string[] bamls = [.. Directory.EnumerateFiles(Path.Combine(projectDirectory, "obj"), "*.baml", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(projectDirectory, file))
            .Order(StringComparer.Ordinal)];
        Assert.NotEmpty(bamls); // BAML yoksa eşitlik boş bir kanıt olurdu

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string baml in bamls)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(baml));
            sha.AppendData(File.ReadAllBytes(Path.Combine(projectDirectory, baml)));
        }

        string dllPath = Directory.GetFiles(Path.Combine(projectDirectory, "bin"), assemblyName + ".dll", SearchOption.AllDirectories).Single();
        byte[] dll = File.ReadAllBytes(dllPath);
        return new Output(Convert.ToHexString(sha.GetHashAndReset()), dll.Length,
            IsReferenceAssembly: Contains(dll, nameof(System.Runtime.CompilerServices.ReferenceAssemblyAttribute)),
            HasFriendMarker: Contains(dll, WpfTemporaryAssemblyTargets.FriendAssemblyName));
    }

    /// <summary>Metadata'daki adlar UTF-8/ASCII bayt dizisidir: işaret aramak için bayt araması yeter.</summary>
    private static bool Contains(byte[] haystack, string ascii) => haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
