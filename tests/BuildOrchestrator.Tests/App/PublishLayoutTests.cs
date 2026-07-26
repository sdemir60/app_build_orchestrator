using System.IO;
using System.Reflection;
using System.Xml.Linq;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D1] Dağıtım yerleşiminin (build + <c>dotnet publish</c>) statik guard'ları. <c>dotnet publish</c> testte
/// KOŞULMAZ (suite için ağır); onun yerine csproj/props'un publish sözleşmesi denetlenir — gerçek publish
/// doğrulaması It-5 kabulünde bir kez elle koşulur ve kayda geçer (task-D1-report.md).
/// <para>Korunan kırılma: <c>CopySupervisorOutput</c> çıplak bir <c>&lt;Copy&gt;</c> olduğu için publish
/// çıktısında <c>supervisor\</c> klasörü HİÇ oluşmuyordu → engine hiç başlamıyor, kullanıcıya hata da
/// görünmüyordu.</para>
/// </summary>
public sealed class PublishLayoutTests
{
    private static readonly XNamespace None = "";

    private static string AppCsprojPath => Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj");
    private static string DirectoryBuildPropsPath => Path.Combine(RepoPaths.RepoRoot, "Directory.Build.props");

    private static XDocument Load(string path) => XDocument.Load(path);

    /// <summary>App projesinin build çıktı dizini — test çıktı dizininden (bin\&lt;Config&gt;\&lt;TFM&gt;)
    /// Configuration/TFM türetilir, böylece Debug/Release ayrımı elle yazılmaz.</summary>
    private static string AppOutputDir()
    {
        var testOut = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string tfm = testOut.Name;
        string configuration = testOut.Parent!.Name;
        return Path.Combine(RepoPaths.AppSrcRoot, "bin", configuration, tfm);
    }

    // ------------------------------------------------------------------ "supervisor" adının TEK kaynağı

    [Fact]
    public void The_supervisor_folder_name_is_declared_once_in_msbuild_and_read_back_by_the_app()
    {
        // TEK KAYNAK: csproj $(SupervisorFolderName) → AssemblyMetadata → SupervisorLayout.FolderName.
        // İki taraf ayrışırsa (eskiden iki bağımsız string'di) derleme hatası OLMAZ, runtime'da sessizce kırılırdı.
        string? declared = Load(AppCsprojPath).Descendants(None + "SupervisorFolderName")
            .Select(e => e.Value).FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(declared), "csproj'da $(SupervisorFolderName) property'si yok");
        Assert.Equal(declared, SupervisorLayout.FolderName);
        Assert.Equal(declared, SupervisorLayout.ReadFolderName(typeof(SupervisorLayout).Assembly));
    }

    [Fact]
    public void No_app_source_file_repeats_the_supervisor_folder_name_as_a_literal()
    {
        string literal = "\"" + SupervisorLayout.FolderName + "\"";
        var offenders = RepoPaths.AppSourceFiles("*.cs")
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.AppSrcRoot, f))
            .ToList();

        Assert.Empty(offenders); // ad yalnız csproj'da yazılır; C# onu metadata'dan okur
    }

    // ------------------------------------------------------------------ build çıktısı

    [Fact]
    public void The_supervisor_executable_sits_next_to_the_app_in_the_build_output()
    {
        string appOut = AppOutputDir();
        Assert.True(Directory.Exists(appOut), $"App çıktı dizini yok: {appOut}");

        // SupervisorLayout.ResolveExePath ÜRETİMDEKİ yolun ta kendisi (App.xaml.cs aynı çağrıyı yapar) —
        // klasör adını da exe adını da bu test pinler.
        string exe = SupervisorLayout.ResolveExePath(appOut);
        Assert.True(File.Exists(exe), $"CopySupervisorOutput çıktısı eksik: {exe}");
    }

    // ------------------------------------------------------------------ publish sözleşmesi

    [Fact]
    public void The_supervisor_output_is_contributed_to_the_publish_layout()
    {
        var target = Load(AppCsprojPath).Descendants(None + "Target")
            .FirstOrDefault(t => t.Descendants(None + "ResolvedFileToPublish").Any());
        Assert.NotNull(target); // publish listesine hiçbir şey eklenmiyorsa supervisor\ publish'te oluşmaz

        // Target publish akışında koşmalı (ResolvedFileToPublish'i hesaplayan target'a bağlı).
        string hooks = (string?)target!.Attribute("AfterTargets") + ";" + (string?)target.Attribute("BeforeTargets");
        Assert.Contains("ComputeResolvedFilesToPublishList", hooks, StringComparison.Ordinal);

        var item = target.Descendants(None + "ResolvedFileToPublish").Single();
        string relativePath = item.Elements(None + "RelativePath").Single().Value;
        Assert.StartsWith("$(SupervisorFolderName)\\", relativePath, StringComparison.Ordinal); // TEK kaynak burada da
        Assert.False(string.IsNullOrWhiteSpace(item.Elements(None + "CopyToPublishDirectory").Single().Value));
    }

    [Fact]
    public void The_font_licence_is_declared_for_the_publish_output_too()
    {
        // OFL "included in all copies" şartı asıl DAĞITILAN artifact için geçerli — SDK varsayılanına bırakılmaz.
        var licence = Load(AppCsprojPath).Descendants(None + "Content")
            .Single(e => ((string?)e.Attribute("Include"))?.EndsWith("GEIST-LICENSE.txt", StringComparison.Ordinal) == true);

        Assert.False(string.IsNullOrWhiteSpace((string?)licence.Attribute("CopyToPublishDirectory")));
        Assert.False(string.IsNullOrWhiteSpace((string?)licence.Attribute("CopyToOutputDirectory")));
    }

    [Fact]
    public void Publish_stays_folder_based_no_single_file_bundle()
    {
        // [v1 KARARI] PublishSingleFile KULLANILMAZ: AppContext.BaseDirectory extraction dizinini gösterir ve
        // supervisor\ alt klasörü bundle'a giremez → engine spawn'ı kırılırdı.
        var buildFiles = Directory.EnumerateFiles(RepoPaths.RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Append(DirectoryBuildPropsPath);

        // Hiçbir proje bunu AÇMAZ (property olarak set etmez).
        var offenders = buildFiles
            .Where(f => Load(f).Descendants(None + "PublishSingleFile").Any())
            .Select(f => Path.GetRelativePath(RepoPaths.RepoRoot, f))
            .ToList();
        Assert.Empty(offenders);

        // [D1 review · C2] …ve komut satırından (-p:PublishSingleFile=true) SESSİZCE açılamaz: App csproj'unda
        // bunu açık hatayla reddeden bir target var (kaynak taraması niyeti korur, target komut satırını kapatır).
        var gate = Load(AppCsprojPath).Descendants(None + "Target")
            .SelectMany(t => t.Elements(None + "Error"))
            .FirstOrDefault(e => ((string?)e.Attribute("Condition"))?.Contains("PublishSingleFile", StringComparison.Ordinal) == true);
        Assert.NotNull(gate);
        Assert.Contains("'true'", (string?)gate!.Attribute("Condition"), StringComparison.Ordinal);
    }

    /// <summary>[D1 review · A1] Publish, içinde engine OLMAYAN bir paketi SESSİZCE üretemez: dosya kümesi
    /// boşsa build/publish açık bir hatayla durur (D1'in kapattığı bug'ın bir seviye yukarısı).</summary>
    [Fact]
    public void An_empty_supervisor_file_set_fails_the_build_instead_of_shipping_an_engineless_package()
    {
        var resolve = Load(AppCsprojPath).Descendants(None + "Target")
            .Single(t => (string?)t.Attribute("Name") == "_ResolveSupervisorFiles");

        var conditions = resolve.Elements(None + "Error")
            .Select(e => (string?)e.Attribute("Condition") ?? "")
            .ToList();

        Assert.Contains(conditions, c => c.Contains("@(_SupervisorFiles)", StringComparison.Ordinal));  // A1: boş küme
        Assert.Contains(conditions, c => c.Contains("$(_SupervisorOutDir)", StringComparison.Ordinal)); // C1: çözülemeyen dizin
    }

    /// <summary>[D1 review · C3] Kopyalama incremental: hedefin <c>Inputs</c>/<c>Outputs</c>'u var (aksi halde
    /// her build'de tüm supervisor ağacı silinip yeniden kopyalanırdı ve <c>SkipUnchangedFiles</c> ölü kalırdı).</summary>
    [Fact]
    public void The_supervisor_copy_is_incremental()
    {
        var copy = Load(AppCsprojPath).Descendants(None + "Target")
            .Single(t => (string?)t.Attribute("Name") == "CopySupervisorOutput");

        Assert.Equal("@(_SupervisorFiles)", (string?)copy.Attribute("Inputs"));
        Assert.False(string.IsNullOrWhiteSpace((string?)copy.Attribute("Outputs")));
    }

    // ------------------------------------------------------------------ sürüm / ürün kimliği

    [Fact]
    public void Version_and_product_are_declared_once_for_the_whole_solution()
    {
        var props = Load(DirectoryBuildPropsPath);
        string version = props.Descendants(None + "Version").Single().Value;
        string product = props.Descendants(None + "Product").Single().Value;

        // Tek kaynak: hiçbir csproj Version'ı yeniden tanımlamaz (Supervisor motor sürümünü assembly'sinden okur).
        var redeclared = Directory.EnumerateFiles(Path.Combine(RepoPaths.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => Load(f).Descendants(None + "Version").Any())
            .Select(f => Path.GetRelativePath(RepoPaths.RepoRoot, f))
            .ToList();
        Assert.Empty(redeclared);

        var app = typeof(SupervisorLayout).Assembly;
        Assert.Equal(version, app.GetName().Version?.ToString(3));
        Assert.Equal(product, app.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
    }

    /// <summary>
    /// [D1 review · B2] Sürüm kablosunun GERÇEKTEN kurulu olduğunu AYIRT EDEN test. Yalın <c>1.0.0</c>,
    /// <c>Directory.Build.props</c> hiç devreye girmese bile .NET SDK'nın varsayılanıdır — onu doğrulamak
    /// "yeşil ama boş" bir testtir. Bu yüzden props ayırt edici bir <c>InformationalVersion</c> (teslim etiketi)
    /// tanımlar ve hem App hem Supervisor assembly'sinde AYNEN gözlenir. Supervisor bu değeri
    /// <c>engineReady.engineVersion</c> ile bildirir; App onu konsolun boot satırında gösterir.
    /// </summary>
    [Fact]
    public void The_informational_version_proves_directory_build_props_is_really_applied()
    {
        var props = Load(DirectoryBuildPropsPath);
        string version = props.Descendants(None + "Version").Single().Value;
        string declared = props.Descendants(None + "InformationalVersion").Single().Value
            .Replace("$(Version)", version, StringComparison.Ordinal);

        Assert.NotEqual(version, declared); // SDK varsayılanından AYIRT EDİLEBİLİR olmalı

        foreach (var assembly in new[] { typeof(SupervisorLayout).Assembly, typeof(BuildOrchestrator.Supervisor.SupervisorHost).Assembly })
            Assert.Equal(declared,
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    }
}
