using System.IO;
using System.Security.Cryptography;
using System.Text;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [RESOLVE Faz 2 · KABUL] WPF'in yerel tipli XAML için derlediği geçici assembly (<c>*_wpftmp</c>) targets'la gövdesiz
/// (metadata-only) derlendiğinde (<see cref="WpfTemporaryAssemblyTargets"/>) gerçek <c>MSBuild.exe</c> ile iki şeyi
/// gösterir: targets ETKİ EDER ve NİHAİ çıktı eşdeğer kalır. Her mini proje geçici bir klasöre kopyalanır ve iki kez
/// derlenir — targets'sız (taban) ve targets'lı; ikisi de motorun kendi argüman planıyla
/// (<see cref="MsBuildArguments.PlanFor"/>).
///
/// <para><b>Etki:</b> MSBuild'in çıktısındaki derleyici komut satırları (varsayılan ayrıntı düzeyi onları yazar —
/// ARCHITECTURE §9.2) targets'lı koşuda <c>/refonly</c> taşıyan bir derleme içerir, tabanda hiçbiri taşımaz. Bu
/// olmasa eşdeğerlik kanıtı etkisiz bir targets dosyasıyla da (koşuldaki yazım hatası, düşen bir öğe, kaybolan
/// argüman) yeşil kalırdı. O <c>/refonly</c> derlemesinin GEÇİCİ assembly'ye ait olduğu, işaret iddiasıyla birlikte
/// kanıtlanır: gerçek projenin derlemesi <c>/refonly</c> olsaydı çıktı DLL'i reference assembly olurdu.</para>
///
/// <para><b>Eşdeğerlik:</b> <c>obj</c> altındaki BAML dosyalarının yolu + baytları ve çıktı DLL'inin boyutu iki koşuda
/// eşit; çıktı DLL'inde reference assembly işareti ya da targets'ın friend assembly adı yok. Bit düzeyinde eşitlik
/// iddia EDİLMEZ (MVID ve gömülü PDB yolu koşuya göre değişir); süre kazancı ÖLÇÜLMEZ (o perf ölçümünün işi).</para>
///
/// <para><b>Duyarlılık</b> (her biri kırmızı koşuyla gösterildi): SDK-style proje, <c>ProduceReferenceAssembly=false</c>
/// silinirse CS8308 ile düşer (<c>/refout</c> ile <c>/refonly</c> çakışır); internal özelliğe bağlanan XAML'li eski-stil
/// proje, friend kaynağı silinirse MC3072 ile düşer; etki iddiası, <c>ProduceOnlyReferenceAssembly=true</c> silinirse,
/// koşuldaki <c>_wpftmp</c> yazımı bozulursa ya da argüman kaybolursa düşer. Yalnız public üyelere bağlanan eski-stil
/// proje sıradan durumun referansıdır.</para>
///
/// <para><b>Bu suite normal koşudan HARİÇTİR</b> (<c>[Trait("Category","Acceptance")]</c>): <c>dotnet test … --filter
/// "Category=Acceptance"</c>. Repo kuralı (<c>OsysRebuildAcceptanceTests</c>): ortam derleyemiyorsa ATLANIR, yalnız
/// aracın kırdığı derlemede kırmızı olur. Bu yüzden MSBuild çözülemezse ya da TABAN (targets'sız) restore/derleme
/// başarısız olursa — eski-stil mini proje için .NET Framework 4.6 targeting pack'i yok; SDK-style mini proje için
/// .NET 10 SDK'sını taşıyabilen bir MSBuild (VS 18 / Build Tools 18; VS 2022 taşıyamaz) yok — ilgili test atlanır;
/// kırmızı yalnız targets'lı derlemede ve karşılaştırmada olur. Repo ya da OSYS gerekmez: girdi, test çıktısına
/// kopyalanan <c>Fixtures\WpfMini</c> mini projeleridir.</para>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class WpfTemporaryAssemblyAcceptanceTests
{
    /// <summary>Derleyici komut satırını MSBuild çıktısında tanıtan anahtar: Csc'nin her çağrısında bulunur.</summary>
    private const string CompilerSwitch = "/noconfig";

    /// <summary>Gövdesiz derleme anahtarı: <c>ProduceOnlyReferenceAssembly=true</c> Csc'ye <c>/refonly</c> olarak gider.</summary>
    private const string MetadataOnlySwitch = "/refonly";

    /// <summary>
    /// Bir koşunun kimliği. Klasör adları EŞİT uzunlukta: çıktı DLL'ine gömülen PDB yolu iki koşuda aynı uzunlukta
    /// olur, boyut karşılaştırması yol uzunluğundan etkilenmez.
    /// </summary>
    private sealed record Variant(string Folder, string Label, bool UsesTargets);

    private static readonly Variant Plain = new("base", "without the targets", UsesTargets: false);
    private static readonly Variant Lean = new("lean", "with the targets", UsesTargets: true);

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
    private sealed record Output(string BamlDigest, long DllLength, bool IsReferenceAssembly, bool HasFriendMarker,
        int CompilerCount, int MetadataOnlyCount);

    private static async Task AssertSameOutputAsync(string project, bool needsRestore, params string[] extraBuildArguments)
    {
        string msbuild = await WpfMiniFixture.ResolveMsBuildOrSkipAsync();
        using var scratch = new TempDir();

        // Targets, projelerin KENDİ klasörlerinin dışında durur: SDK-style varsayılan glob'u friend .cs dosyasını
        // gerçek derlemeye alırdı ve "friend işareti çıktıda yok" kanıtı boşa çıkardı.
        string targets = WpfTemporaryAssemblyTargets.EnsureWritten(Path.Combine(scratch.Path, "cache"));

        // Taban önce: başarısızlığı ortam eksiğidir ve testi atlatır; targets'lı koşu ancak taban derlenebiliyorsa anlamlıdır.
        Output plain = await BuildAsync(msbuild, scratch.Path, Plain, project, needsRestore, targets, extraBuildArguments);
        Output lean = await BuildAsync(msbuild, scratch.Path, Lean, project, needsRestore, targets, extraBuildArguments);

        // Etki: derleyici komut satırları görünür olmalı (yoksa "tabanda /refonly yok" boş bir iddia olurdu); targets'lı
        // koşuda gövdesiz bir derleme var, tabanda yok.
        Assert.True(plain.CompilerCount > 0 && lean.CompilerCount > 0,
            "MSBuild's output carries no compiler command line, so a metadata-only compile cannot be seen");
        Assert.True(lean.MetadataOnlyCount > 0,
            $"{Lean.Label}: no compiler command line carries {MetadataOnlySwitch} — the targets had no effect on the temporary assembly");
        Assert.True(plain.MetadataOnlyCount == 0,
            $"{Plain.Label}: a compiler command line already carries {MetadataOnlySwitch} — there is nothing for the targets to change");

        // Zarar vermeme. İşaretler önce: targets gerçek derlemeye sızarsa kırılma nedeni boyut farkı değil, işaretin kendisidir.
        foreach (var (variant, output) in new[] { (Plain, plain), (Lean, lean) })
        {
            Assert.False(output.IsReferenceAssembly, $"{variant.Label}: the output assembly is a reference assembly (metadata only)");
            Assert.False(output.HasFriendMarker, $"{variant.Label}: the output assembly carries the temporary assembly's friend name");
        }
        Assert.Equal(plain.BamlDigest, lean.BamlDigest);
        Assert.Equal(plain.DllLength, lean.DllLength);
    }

    /// <summary>Mini projeyi taze bir kopyada derler (gerekirse önce restore) ve çıktısını ölçer.</summary>
    private static async Task<Output> BuildAsync(string msbuild, string scratch, Variant variant, string project,
        bool needsRestore, string targets, string[] extraBuildArguments)
    {
        string copyRoot = Path.Combine(scratch, variant.Folder);
        WpfMiniFixture.CopyTo(copyRoot);
        string projectPath = Path.Combine(copyRoot, project);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;

        // Motorun kendi argüman planı: SDK-style proje önce restore ister; targets yalnız build listesine girer.
        var (restore, build) = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(projectPath, "Debug", projectDirectory,
            needsRestore, CustomBeforeTargets: variant.UsesTargets ? targets : null));
        if (restore is not null)
            Require(await WpfMiniFixture.RunAsync(msbuild, restore, projectDirectory), variant, "restore");
        ProcessResult built = await WpfMiniFixture.RunAsync(msbuild, [.. build, .. extraBuildArguments], projectDirectory);
        Require(built, variant, "build");

        return Measure(projectDirectory, Path.GetFileNameWithoutExtension(project), built);
    }

    /// <summary>
    /// Taban (targets'sız) koşunun başarısızlığı ORTAM eksiğidir — fixture bu makinede derlenemiyor (eski-stil proje için
    /// 4.6 targeting pack yok, SDK-style proje için .NET 10 SDK'sını taşıyamayan bir MSBuild vb.): ATLA. Targets'lı
    /// koşunun başarısızlığı ise aracın kırdığı bir derlemedir: KIRMIZI.
    /// </summary>
    private static void Require(ProcessResult result, Variant variant, string step)
    {
        if (result.Success)
            return;
        string failure = $"MSBuild {step} {variant.Label} failed (exit {result.ExitCode}{(result.TimedOut ? ", timed out" : string.Empty)}):"
            + Environment.NewLine + WpfMiniFixture.ErrorLines(result);
        Skip.If(!variant.UsesTargets, "baseline build failed without the targets; fixture not buildable here: " + failure);
        Assert.Fail(failure);
    }

    private static Output Measure(string projectDirectory, string assemblyName, ProcessResult built)
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

        string[] compilerLines = [.. built.StandardOutput.Split('\n').Where(line => line.Contains(CompilerSwitch, StringComparison.Ordinal))];
        return new Output(Convert.ToHexString(sha.GetHashAndReset()), dll.Length,
            IsReferenceAssembly: Contains(dll, nameof(System.Runtime.CompilerServices.ReferenceAssemblyAttribute)),
            HasFriendMarker: Contains(dll, WpfTemporaryAssemblyTargets.FriendAssemblyName),
            CompilerCount: compilerLines.Length,
            MetadataOnlyCount: compilerLines.Count(line => line.Contains(MetadataOnlySwitch, StringComparison.Ordinal)));
    }

    /// <summary>Metadata'daki adlar UTF-8/ASCII bayt dizisidir: işaret aramak için bayt araması yeter.</summary>
    private static bool Contains(byte[] haystack, string ascii) => haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;
}
