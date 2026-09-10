using System.Xml.Linq;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// Bir Reference item'ının ham HintPath değeri ve karşılaştırma için normalize edilmiş basename'i.
/// </summary>
public sealed record RawHintPath(string Raw, string BaseName);

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
}

/// <summary>
/// Ham-XML csproj evaluator. Legacy (.NET Framework, xmlns'li) ve SDK-style projeleri
/// MSBuild çalıştırmadan ayrıştırır: legacy projelerde item'lar explicit olduğu için
/// XML değeri MSBuild-evaluated değere eşittir (bkz. plan §Discovery).
/// </summary>
public sealed class CsprojEvaluator
{
    private static readonly EnumerationOptions Recurse = new() { RecurseSubdirectories = true };
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase) { "obj", "bin" };

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
        };
    }

    // MSBuild namespace toleransı: legacy'de xmlns var, SDK'da yok → LocalName ile eşle [D5].
    private static IEnumerable<XElement> Elements(XElement parent, string local) =>
        parent.Elements().Where(e => e.Name.LocalName == local);
    private static IEnumerable<XElement> Items(XElement root, string local) =>
        Elements(root, "ItemGroup").SelectMany(ig => Elements(ig, local));

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
