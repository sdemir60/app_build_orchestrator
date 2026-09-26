using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// Bir Reference item'ının ham HintPath değeri ve karşılaştırma için normalize edilmiş basename'i.
/// </summary>
public sealed record RawHintPath(string Raw, string BaseName);

/// <summary>
/// [Faz 3/Task 1] Tek bir <c>&lt;OutputPath&gt;</c> kaydı, koşulunun ayrıştırılmış (Configuration, Platform)
/// çiftiyle birlikte. Koşulsuz bir grup/eleman için <c>Configuration</c> ve <c>Platform</c> <c>null</c>'dır
/// (her configuration/platform'la eşleşir); yalnız <c>'$(Configuration)' == 'C'</c> biçimi için <c>Platform</c>
/// <c>null</c>'dır (her platform'la eşleşir).
/// </summary>
public sealed record ConditionalOutputPath(string? Configuration, string? Platform, string Path);

/// <summary>
/// Tek bir .csproj'un ham-XML değerlendirme sonucu: AssemblyName, Compile dosyaları,
/// HintPath referansları ve ProjectReference'lar. MSBuild.exe çalıştırılmaz — [Global Constraints raw-XML].
/// </summary>
/// <param name="TargetFrameworkMoniker">
/// [T72/Task 14] StaleObjDetector.Inspect'in beklediği TAM moniker (bkz. TargetFrameworkMonikerDeriver) — csproj'ta
/// ne TargetFrameworkVersion ne TargetFramework varsa (nadiren, ör. bozuk/eksik proje dosyası) null.
/// </param>
public sealed record EvaluatedProject(
    string Path, string AssemblyName, IReadOnlyList<string> CompileFiles,
    IReadOnlyList<RawHintPath> HintPaths, IReadOnlyList<string> ProjectReferences, bool IsSdkStyle,
    string? TargetFrameworkMoniker = null)
{
    /// <summary>
    /// [D2] Derlemeye giren ama <c>Compile</c> OLMAYAN bildirilmiş öğeler: <c>Page</c>,
    /// <c>ApplicationDefinition</c>, <c>EmbeddedResource</c>, <c>Resource</c> (mutlak yollar, sıralı, tekil).
    ///
    /// <para><b>Ne için var.</b> İçerik kararı bir projenin klasörünü zaten süpürür (bkz. <see
    /// cref="BuildOrchestrator.Core.Incremental.ProjectInputs"/>); bu liste o süpürmenin göremediği TEK şeyi
    /// yakalar: klasörün DIŞINA link verilmiş bir <c>.xaml</c> / <c>.resx</c>. Bu yüzden burada implicit
    /// SDK glob'ları AÇILMAZ — klasör içi zaten görülür.</para>
    ///
    /// <para>Positional değil init-property olmasının nedeni geriye dönük uyumdur: evaluation-cache'teki
    /// ESKİ JSON kayıtlarında bu alan yoktur ve alansız çözülen kayıt boş listeyle gelir (null değil).</para>
    /// </summary>
    public IReadOnlyList<string> ResourceFiles { get; init; } = [];

    /// <summary>
    /// [Faz 3/Task 1] <c>&lt;OutputType&gt;</c> (Library/Exe/WinExe), belge sırasındaki ilk PropertyGroup'tan.
    /// Eski JSON kayıtlarında alan yoksa <c>null</c> (SDK-style ya da tanınmayan değer için de <c>null</c>
    /// <see cref="OutputFileFor"/> içinde ele alınır).
    /// </summary>
    public string? OutputType { get; init; }

    /// <summary>
    /// [Faz 3/Task 1] <c>&lt;Platform Condition=" '$(Platform)' == '' "&gt;X&lt;/Platform&gt;</c> ile bildirilen
    /// varsayılan platform; yoksa <c>null</c> (o zaman <see cref="OutputFileFor"/> "AnyCPU" varsayar).
    /// </summary>
    public string? DefaultPlatform { get; init; }

    /// <summary>
    /// [Faz 3/Task 1] Belge sırasındaki koşullu <c>&lt;OutputPath&gt;</c> kayıtları (koşulsuz grup/eleman ⇒ ikisi
    /// <c>null</c>). MSBuild son-yazan-kazanır kuralınca <see cref="OutputFileFor"/> içinde belge sırasıyla
    /// UYAN SON kayıt seçilir.
    /// </summary>
    public IReadOnlyList<ConditionalOutputPath> OutputPaths { get; init; } = [];

    /// <summary>
    /// [Faz 3/Task 1] Bir <c>&lt;OutputPath&gt;</c> tanınmayan bir koşulun (grup ya da elemanın kendi
    /// <c>Condition</c>'ı) altındaysa <c>true</c> — bu durumda <see cref="OutputFileFor"/> hangi configuration
    /// sorulursa sorulsun (tutarsız kanıt riskine karşı) <c>null</c> döner.
    /// </summary>
    public bool OutputPathUndecidable { get; init; }

    /// <summary>
    /// [Faz 3/Task 1] Bu projenin derleme çıktı dosyasının TAM yolu (build kanıtı — bkz. Faz 3 kararları).
    /// SDK-style, <see cref="OutputType"/> yok/tanınmayan, <see cref="OutputPathUndecidable"/>, çözülen
    /// <c>AssemblyName</c> veya seçilen <c>OutputPath</c> hâlâ <c>$(</c> içeriyorsa <c>null</c> (kanıtsız).
    /// </summary>
    public string? OutputFileFor(string configuration)
    {
        if (IsSdkStyle || OutputPathUndecidable) return null;
        if (string.IsNullOrWhiteSpace(OutputType) || AssemblyName.Contains("$(")) return null;

        string? extension = OutputType.Trim().ToLowerInvariant() switch
        {
            "library" => ".dll",
            "exe" or "winexe" => ".exe",
            _ => null,
        };
        if (extension is null) return null;

        string platform = DefaultPlatform ?? "AnyCPU";
        string? chosen = null;
        // Belge sırasıyla uyan SON kayıt kazanır [MSBuild son-yazan-kazanır].
        foreach (var candidate in OutputPaths)
        {
            bool configurationMatches = candidate.Configuration is null
                || string.Equals(candidate.Configuration, configuration, StringComparison.OrdinalIgnoreCase);
            bool platformMatches = candidate.Platform is null
                || string.Equals(candidate.Platform, platform, StringComparison.OrdinalIgnoreCase);
            if (configurationMatches && platformMatches) chosen = candidate.Path;
        }
        string outputPath = chosen ?? System.IO.Path.Combine("bin", configuration) + System.IO.Path.DirectorySeparatorChar;
        if (outputPath.Contains("$(")) return null;

        string projectDir = System.IO.Path.GetDirectoryName(Path)!;
        string outputDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, outputPath));
        return System.IO.Path.Combine(outputDir, AssemblyName + extension);
    }

    /// <summary>
    /// [Faz 3/Task 1] Her <see cref="RawHintPath.Raw"/> için mutlak yol (göreliyse proje klasörüne göre);
    /// <c>$(</c> içerenler atlanır. Tekil, sıralı.
    /// </summary>
    public IReadOnlyList<string> HintPathTargets()
    {
        string projectDir = System.IO.Path.GetDirectoryName(Path)!;
        var targets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in HintPaths)
        {
            if (hint.Raw.Contains("$(")) continue;
            string full = System.IO.Path.IsPathRooted(hint.Raw)
                ? System.IO.Path.GetFullPath(hint.Raw)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, hint.Raw));
            targets.Add(full);
        }
        return targets.ToList();
    }
}

/// <summary>
/// Ham-XML csproj evaluator. Legacy (.NET Framework, xmlns'li) ve SDK-style projeleri
/// MSBuild çalıştırmadan ayrıştırır: legacy projelerde item'lar explicit olduğu için
/// XML değeri MSBuild-evaluated değere eşittir (bkz. plan §Discovery).
/// </summary>
public sealed class CsprojEvaluator
{
    private static readonly EnumerationOptions Recurse = new() { RecurseSubdirectories = true };

    /// <summary>[T1/T22 · kopya YASAK] Atlanan derleme-çıktısı klasörleri — tek kaynak
    /// <see cref="WorkspaceScanner.BuildOutputFolderNames"/> (bağımsız bir "obj"/"bin" literali burada
    /// YAZILMAZ). <see cref="WorkspaceScanner.ExternalToolingFolderNames"/> (<c>.git</c>/<c>.vs</c>/
    /// <c>node_modules</c>) BİLEREK dışarıda kalır: bu evaluator csproj item glob'larını değerlendirir, workspace
    /// taraması değildir — davranış eskisiyle AYNI kalır (yalnız bin/obj atlanır).</summary>
    private static readonly HashSet<string> SkipDirs =
        new(WorkspaceScanner.BuildOutputFolderNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>[D2] <see cref="EvaluatedProject.ResourceFiles"/>'a giren item adları.</summary>
    private static readonly string[] ResourceItemNames =
        ["Page", "ApplicationDefinition", "EmbeddedResource", "Resource"];

    public EvaluatedProject Evaluate(string csprojPath)
    {
        csprojPath = System.IO.Path.GetFullPath(csprojPath);
        string dir = System.IO.Path.GetDirectoryName(csprojPath)!;
        var doc = XDocument.Load(csprojPath);
        var root = doc.Root!;
        bool sdk = root.Attribute("Sdk") is not null;

        string asmName = Elements(root, "PropertyGroup").SelectMany(pg => Elements(pg, "AssemblyName"))
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0)
            ?? System.IO.Path.GetFileNameWithoutExtension(csprojPath);

        // [T72/Task 14] StaleObjDetector.Inspect'in expectedTfm'i için: legacy TargetFrameworkVersion ya da
        // SDK-style TargetFramework — bkz. TargetFrameworkMonikerDeriver.
        string? tfv = Elements(root, "PropertyGroup").SelectMany(pg => Elements(pg, "TargetFrameworkVersion"))
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        string? tf = Elements(root, "PropertyGroup").SelectMany(pg => Elements(pg, "TargetFramework"))
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        string? tfMoniker = TargetFrameworkMonikerDeriver.FromRaw(tfv, tf);

        // [Faz 3/Task 1] OutputType (Library/Exe/WinExe) — AssemblyName ile ayni desen: belge sirasindaki ilk
        // dolu deger.
        string? outputType = Elements(root, "PropertyGroup").SelectMany(pg => Elements(pg, "OutputType"))
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);

        // [Faz 3/Task 1] <Platform Condition=" '$(Platform)' == '' ">X</Platform> — yalnizca bu ozel kosulu
        // tasiyan Platform elemani varsayilan platformu bildirir.
        string? defaultPlatform = Elements(root, "PropertyGroup").SelectMany(pg => Elements(pg, "Platform"))
            .Where(e => IsDefaultPlatformCondition(e.Attribute("Condition")?.Value))
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);

        // [Faz 3/Task 1] Belge sirasindaki her <OutputPath>: kendi Condition'i varsa onu, yoksa grubunkini
        // ayristir. Ne kendi ne grup kosulu varsa kosulsuzdur (her configuration/platform'la esleşir).
        // Taninmayan bir kosul formu projeyi TUMUYLE OutputPathUndecidable yapar [tutarsiz kanit riski].
        var outputPaths = new List<ConditionalOutputPath>();
        bool outputPathUndecidable = false;
        foreach (var pg in Elements(root, "PropertyGroup"))
        {
            string? groupCondition = pg.Attribute("Condition")?.Value;
            foreach (var outputPathElement in Elements(pg, "OutputPath"))
            {
                string value = outputPathElement.Value.Trim();
                if (value.Length == 0) continue;
                string? condition = outputPathElement.Attribute("Condition")?.Value ?? groupCondition;
                if (condition is null)
                {
                    outputPaths.Add(new ConditionalOutputPath(null, null, value));
                }
                else if (TryParseConfigurationPlatformCondition(condition, out string? configuration, out string? platform))
                {
                    outputPaths.Add(new ConditionalOutputPath(configuration, platform, value));
                }
                else
                {
                    outputPathUndecidable = true;
                }
            }
        }

        // Compile: legacy'de yalnız explicit <Compile Include> (wildcard varsa diskle genişlet);
        // SDK-style'da implicit glob **/*.cs (obj/bin hariç) + explicit Include birleşimi. Determinizm [D8]: SortedSet.
        var compile = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inc in Items(root, "Compile").Select(i => i.Attribute("Include")?.Value).Where(v => !string.IsNullOrWhiteSpace(v)))
            foreach (var f in ResolveInclude(dir, inc!)) compile.Add(f);
        if (sdk)
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", Recurse))
                if (!IsUnderSkipped(dir, f)) compile.Add(System.IO.Path.GetFullPath(f));

        // [D2] Compile OLMAYAN bildirilmiş öğeler — yalnız EXPLICIT Include'lar (klasör içi zaten süpürülür,
        // bkz. ProjectInputs): asıl kazanç klasör dışına link verilmiş xaml/resx'tir.
        var resources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string itemName in ResourceItemNames)
            foreach (var inc in Items(root, itemName).Select(i => i.Attribute("Include")?.Value).Where(v => !string.IsNullOrWhiteSpace(v)))
                foreach (var f in ResolveInclude(dir, inc!)) resources.Add(f);

        // HintPath yalnız <Reference><HintPath> olanlar; GAC ref'leri (HintPath yok) kenar değil.
        var hints = Items(root, "Reference")
            .Select(r => Elements(r, "HintPath").Select(h => h.Value.Trim()).FirstOrDefault())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => new RawHintPath(h!, System.IO.Path.GetFileName(h!).ToLowerInvariant()))
            .OrderBy(h => h.BaseName, StringComparer.OrdinalIgnoreCase).ToList();

        // MSBuild HER item'ın Include'unu ';' ile böler — ProjectReference de istisna değil [D11].
        var projRefs = Items(root, "ProjectReference")
            .Select(p => p.Attribute("Include")?.Value).Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => v!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, v)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

        return new EvaluatedProject(csprojPath, asmName, compile.ToList(), hints, projRefs, sdk, tfMoniker)
        {
            ResourceFiles = resources.ToList(),
            OutputType = outputType,
            DefaultPlatform = defaultPlatform,
            OutputPaths = outputPaths,
            OutputPathUndecidable = outputPathUndecidable,
        };
    }

    // MSBuild namespace toleransı: legacy'de xmlns var, SDK'da yok → LocalName ile eşle [D5].
    private static IEnumerable<XElement> Elements(XElement parent, string local) =>
        parent.Elements().Where(e => e.Name.LocalName == local);
    private static IEnumerable<XElement> Items(XElement root, string local) =>
        Elements(root, "ItemGroup").SelectMany(ig => Elements(ig, local));

    // [Faz 3/Task 1] Tanınan koşul biçimleri (boşluk/tırnak toleranslı, büyük-küçük harf duyarsız):
    // '$(Configuration)|$(Platform)' == 'C|P'  ve  '$(Configuration)' == 'C'. Başka her biçim tanınmaz.
    private static readonly Regex ConfigurationAndPlatformCondition =
        new(@"^\s*'\$\(Configuration\)\|\$\(Platform\)'\s*==\s*'([^|']*)\|([^']*)'\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex ConfigurationOnlyCondition =
        new(@"^\s*'\$\(Configuration\)'\s*==\s*'([^']*)'\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex DefaultPlatformConditionPattern =
        new(@"^\s*'\$\(Platform\)'\s*==\s*''\s*$", RegexOptions.IgnoreCase);

    private static bool TryParseConfigurationPlatformCondition(string condition, out string? configuration, out string? platform)
    {
        var both = ConfigurationAndPlatformCondition.Match(condition);
        if (both.Success)
        {
            configuration = both.Groups[1].Value;
            platform = both.Groups[2].Value;
            return true;
        }
        var configOnly = ConfigurationOnlyCondition.Match(condition);
        if (configOnly.Success)
        {
            configuration = configOnly.Groups[1].Value;
            platform = null;
            return true;
        }
        configuration = null;
        platform = null;
        return false;
    }

    private static bool IsDefaultPlatformCondition(string? condition) =>
        condition is not null && DefaultPlatformConditionPattern.IsMatch(condition);

    private static IEnumerable<string> ResolveInclude(string dir, string include)
    {
        // MSBuild ';' ile çoklu Include ayırır
        foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('*') || part.Contains('?'))
            {
                string pat = System.IO.Path.GetFileName(part);
                string sub = System.IO.Path.GetDirectoryName(part) ?? "";
                // "**" bir dizin adı DEĞİL, recursive işaretidir: taban dizinden RECURSE et [D11].
                // Eskiden GetDirectoryName("**\*.cs") == "**" döndüğü ve o ad diskte hiç var olmadığı
                // için Directory.Exists daima false dönüyor, dosyalar sessizce kayboluyordu.
                bool recursive = sub.Contains("**");
                if (recursive) sub = sub.Replace("**", "").TrimEnd('\\', '/');
                string baseDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, sub));
                if (Directory.Exists(baseDir))
                    foreach (var f in Directory.EnumerateFiles(baseDir, pat, recursive ? Recurse : new EnumerationOptions()))
                        yield return System.IO.Path.GetFullPath(f);
            }
            else yield return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, part));
        }
    }

    private static bool IsUnderSkipped(string projDir, string file)
    {
        string rel = System.IO.Path.GetRelativePath(projDir, file);
        return rel.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                  .Any(seg => SkipDirs.Contains(seg));
    }
}
