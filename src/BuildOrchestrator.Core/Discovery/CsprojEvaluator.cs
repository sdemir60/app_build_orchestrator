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
/// [Ruling · A2] Değerlendirmenin baktığı, csproj DIŞINDAKİ bir dosyanın o anki durumu: yok (<c>Length</c> <c>null</c>) ya da
/// boyut + yazma zamanı. <see cref="EvaluationCache"/> isabette yeniden okur; tutmazsa csproj değişmemiş olsa da proje yeniden
/// değerlendirilir.
/// </summary>
public sealed record FileStamp(string Path, long? Length, long MtimeTicks)
{
    public static FileStamp Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileStamp(path, info.Length, info.LastWriteTimeUtc.Ticks) : new FileStamp(path, null, 0);
    }

    /// <summary>Dosya hâlâ kaydedildiği gibi mi (yoksa hâlâ yok mu).</summary>
    public bool IsCurrent() => Of(Path) == this;
}

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
    /// Eski JSON kayıtlarında alan yoksa <c>null</c>. <see cref="OutputFileFor"/> onu projenin türüne göre okur: legacy'de
    /// yok ya da tanınmayan değer ⇒ kanıtsız; SDK-style'da yok ⇒ <c>Library</c> (SDK'nın varsayılanı).
    /// </summary>
    public string? OutputType { get; init; }

    /// <summary>
    /// [A1-A2] SDK-style projenin çıktı düzeni SDK'nın varsayılanı mı (<c>bin\&lt;Configuration&gt;\&lt;TargetFramework&gt;\</c>):
    /// proje .NET SDK'sında (<c>Microsoft.NET.Sdk</c> ailesi) ve ne csproj'da ne de proje klasöründen yukarı en yakın
    /// <c>Directory.Build.props</c> / <c>Directory.Build.targets</c>'ta düzeni oynatan bir ayar, <c>Import</c> ya da okunamayan
    /// dosya var. Kararı <see cref="CsprojEvaluator"/> verir; <see cref="OutputFileFor"/> yalnız okur. Positional değil
    /// init-property: alansız ESKİ JSON kaydı <c>false</c> (yol yok) çözülür — şema da bu yüzden artar.
    /// </summary>
    public bool SdkOutputLayoutIsDefault { get; init; }

    /// <summary>
    /// [Ruling · A2] <see cref="SdkOutputLayoutIsDefault"/> kararının csproj DIŞINDAKİ girdileri: yukarı yürürken bakılan her
    /// Directory.Build.props/targets adayının durumu — yok olanlar dahil, çünkü sonradan belirmeleri de kararı değiştirir.
    /// Legacy projede ve kararı csproj'un kendisi verdiğinde (başka SDK, düzeni oynatan ayar) boştur. <see cref="EvaluationCache"/>
    /// isabette bunları yeniden okur.
    /// </summary>
    public IReadOnlyList<FileStamp> LayoutInputs { get; init; } = [];

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
    /// <see cref="OutputPathUndecidable"/> ise <c>null</c> (kanıtsız). SDK-style proje SDK'nın varsayılan yolunu alır
    /// (<see cref="SdkDefaultOutputFileFor"/>). Legacy projede <see cref="OutputType"/> yok/tanınmayan, çözülen
    /// <c>AssemblyName</c> veya seçilen <c>OutputPath</c> hâlâ <c>$(</c> içeriyorsa <c>null</c>.
    /// </summary>
    public string? OutputFileFor(string configuration)
    {
        if (OutputPathUndecidable) return null;
        if (IsSdkStyle) return SdkDefaultOutputFileFor(configuration);
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
    /// [A1-A2] SDK-style projenin SDK varsayılan çıktı dosyası: <c>&lt;proje&gt;\bin\&lt;Configuration&gt;\&lt;TargetFramework&gt;\&lt;AssemblyName&gt;.dll</c>.
    /// Yalnız düzen varsayılanken (<see cref="SdkOutputLayoutIsDefault"/>), TargetFramework bilinip çözülmüşken, proje bir
    /// kitaplıkken (<see cref="OutputType"/> yok ya da <c>Library</c> — Exe/WinExe'nin çıktısı hedefe göre değişir) ve
    /// <c>AssemblyName</c> çözülmüşken; aksi <c>null</c> (§7.6: güvenle türetilemiyorsa yaklaşık değil, hiç). Klasör adı SDK'nın
    /// yazdığı gibi küçük harflidir (<c>$(TargetFramework.ToLowerInvariant())</c>).
    /// </summary>
    private string? SdkDefaultOutputFileFor(string configuration)
    {
        if (!SdkOutputLayoutIsDefault) return null;
        if (string.IsNullOrWhiteSpace(TargetFrameworkMoniker) || TargetFrameworkMoniker.Contains("$(")) return null;
        if (!string.IsNullOrWhiteSpace(OutputType) && !OutputType.Trim().Equals("Library", StringComparison.OrdinalIgnoreCase))
            return null;
        if (AssemblyName.Contains("$(")) return null;
        string projectDir = System.IO.Path.GetDirectoryName(Path)!;
        return System.IO.Path.Combine(projectDir, "bin", configuration, TargetFrameworkMoniker.ToLowerInvariant(), AssemblyName + ".dll");
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

    /// <summary>MSBuild'in proje klasöründen yukarı aradığı ve her ad için İLK bulunanı içe aldığı dosyalar — TEK kaynak
    /// (içerik girdilerinin listesi de buradan okur: <c>ProjectInputs.DirectoryLevelFileNames</c>).</summary>
    public static readonly IReadOnlyList<string> DirectoryBuildFileNames = ["Directory.Build.props", "Directory.Build.targets"];

    /// <summary>[A2] SDK'nın varsayılan çıktı düzenini (<c>bin\&lt;Configuration&gt;\&lt;TargetFramework&gt;\</c>) ya da çıktı dosyasının
    /// adını değiştiren ayarlar: csproj'da ya da en yakın Directory.Build.props/targets'ta biri varsa yol türetilmez.
    /// <c>TargetFrameworks</c> çoklu hedeftir. [Ruling] <c>Platform</c>/<c>PlatformName</c>/<c>AppendPlatformToOutputPath</c>
    /// (platform klasörü), <c>TargetName</c>/<c>TargetExt</c> (dosyanın adı/uzantısı), <c>TargetFrameworkVersion</c> (moniker'ı
    /// TargetFramework'ten değil ondan türetir — klasör adı bilinemez) ve <c>DirectoryBuildPropsPath</c>/<c>DirectoryBuildTargetsPath</c>
    /// (içe alınan Directory.Build dosyası en yakını olmaz) da sayılır. MSBuild özellik adları büyük/küçük harf duyarsızdır.</summary>
    private static readonly HashSet<string> LayoutOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputPath", "OutDir", "BaseOutputPath", "AppendTargetFrameworkToOutputPath", "AppendRuntimeIdentifierToOutputPath",
        "RuntimeIdentifier", "RuntimeIdentifiers", "UseArtifactsOutput", "ArtifactsPath", "TargetFrameworks",
        "Platform", "PlatformName", "AppendPlatformToOutputPath", "TargetName", "TargetExt", "TargetFrameworkVersion",
        "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath",
    };

    /// <summary>Yolun csproj'dan okunan girdileri (<see cref="Evaluate"/>'in AssemblyName, OutputType ve TargetFramework okuması).</summary>
    private static readonly string[] LayoutInputNames = ["AssemblyName", "OutputType", "TargetFramework"];

    /// <summary>[Ruling] Directory.Build.props/targets'ta görülünce düzeni oynatan ek ayarlar: yolun csproj'dan okunan girdileri.
    /// Props csproj'da olmayanın yerine geçer (AssemblyName yoksa dosya adı varsayılırdı), targets csproj'unkini ezer.</summary>
    private static readonly HashSet<string> DirectoryLayoutOverrides =
        new(LayoutOverrides.Concat(LayoutInputNames), StringComparer.OrdinalIgnoreCase);

    /// <summary>[Ruling] İçine bakılamayan içe alımlar: <c>Import</c> (ImportGroup içindekiler dahil) ve ek bir SDK getiren
    /// <c>Sdk</c> elemanı — getirdikleri dosya düzeni oynatabilir.</summary>
    private static readonly HashSet<string> ImportElements = new(StringComparer.OrdinalIgnoreCase) { "Import", "Sdk" };

    /// <summary>[Ruling] Varsayılan düzen .NET SDK'sınındır: <c>Microsoft.NET.Sdk</c> ve <c>Microsoft.NET.Sdk.*</c>. Başka bir SDK
    /// (hiç derlemeyen <c>Microsoft.Build.NoTargets</c> gibi), sürüm sabitli ya da <c>;</c> ile birden çok SDK eşleşmez.</summary>
    private static readonly Regex DotNetSdk = new(@"^Microsoft\.NET\.Sdk(\.[A-Za-z0-9]+)*$", RegexOptions.IgnoreCase);

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

        (bool IsDefault, IReadOnlyList<FileStamp> Inputs) layout = sdk ? SdkOutputLayout(root, dir) : (false, []);
        return new EvaluatedProject(csprojPath, asmName, compile.ToList(), hints, projRefs, sdk, tfMoniker)
        {
            ResourceFiles = resources.ToList(),
            OutputType = outputType,
            DefaultPlatform = defaultPlatform,
            OutputPaths = outputPaths,
            OutputPathUndecidable = outputPathUndecidable,
            SdkOutputLayoutIsDefault = layout.IsDefault,
            LayoutInputs = layout.Inputs,
        };
    }

    /// <summary>
    /// [A1-A2] SDK-style projenin çıktı düzeni SDK'nın varsayılanı mı. Değerlendirici ham XML okur, MSBuild koşturmaz: yalnız
    /// GÖREBİLDİĞİ girdilerde düzeni oynatan hiçbir şey yoksa "varsayılan" der — proje .NET SDK'sında, csproj'un hiçbir
    /// PropertyGroup'unda (koşullu ya da Choose/When içinde olsa da) <see cref="LayoutOverrides"/>'tan biri ve içe alım yok, en
    /// yakın Directory.Build.props ile en yakın Directory.Build.targets'ta da (<see cref="DirectoryLayoutOverrides"/>) yok.
    /// Okunamayan (kilitli, bozuk) bir Directory.Build dosyası düzeni bilinmez kılar. Arama MSBuild'inki gibi proje klasöründen
    /// sürücü köküne kadar yürür ve her ad için ilk bulunanda durur; bakılan her aday (yok olanlar dahil) kararın csproj dışı
    /// girdisi olarak döner (<see cref="EvaluatedProject.LayoutInputs"/>).
    /// </summary>
    private static (bool IsDefault, IReadOnlyList<FileStamp> Inputs) SdkOutputLayout(XElement project, string projectDir)
    {
        if (!DotNetSdk.IsMatch(project.Attribute("Sdk")!.Value.Trim())) return (false, []);
        if (MovesLayout(project, LayoutOverrides) || !InputsAreUnambiguous(project)) return (false, []);
        var probed = new List<FileStamp>();
        foreach (string name in DirectoryBuildFileNames)
            if (NearestAbove(projectDir, name, probed) is { } file && DirectoryFileMovesLayout(file))
                return (false, probed);
        return (true, probed);
    }

    /// <summary>[Final review I1] Yolun csproj'dan okunan girdilerini değerlendirici ve MSBuild aynı okuyor mu: değerlendirici her
    /// birinin İLK dolu değerini koşula bakmadan alır, MSBuild koşulları tartar, büyük/küçük harfe bakmaz ve son yazanı alır. İkisi
    /// yalnız girdi hiç yazılmamışsa ya da tam bir kez, koşulsuz (ne elemanın ne grubun <c>Condition</c>'ı), kökün doğrudan
    /// altındaki bir PropertyGroup'ta ve tam bu yazımla yazılmışsa aynı değeri görür; aksi hâlde türetilen dosya hiç oluşmayabilir
    /// ve proje kalıcı olarak "output missing" okurdu.</summary>
    private static bool InputsAreUnambiguous(XElement project) =>
        LayoutInputNames.All(name =>
        {
            var definitions = project.Descendants()
                .Where(e => e.Parent?.Name.LocalName == "PropertyGroup"
                    && string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return definitions.Count == 0
                || (definitions.Count == 1 && definitions[0] is var only
                    && only.Name.LocalName == name
                    && only.Attribute("Condition") is null
                    && only.Parent!.Attribute("Condition") is null
                    && only.Parent.Parent == project);
        });

    /// <summary>Elemanın altında içe alım ya da (herhangi bir derinlikteki) bir PropertyGroup'ta <paramref name="overrides"/>'tan
    /// bir ayar var mı.</summary>
    private static bool MovesLayout(XElement root, HashSet<string> overrides) =>
        root.Descendants().Any(e => ImportElements.Contains(e.Name.LocalName)
            || (e.Parent?.Name.LocalName == "PropertyGroup" && overrides.Contains(e.Name.LocalName)));

    /// <summary>Okunamayan dosya (kilit, erişim, bozuk XML) düzeni bilinmez kılar — "oynatıyor" sayılır.</summary>
    private static bool DirectoryFileMovesLayout(string file)
    {
        try { return MovesLayout(XDocument.Load(file).Root!, DirectoryLayoutOverrides); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return true; }
    }

    /// <summary>Proje klasöründen sürücü köküne doğru <paramref name="fileName"/>'in ilk bulunduğu yer; yoksa <c>null</c>. Bakılan
    /// her adayın durumu <paramref name="probed"/>'a eklenir — dosya içeriği OKUNMADAN önce: arada değişen dosya bir sonraki
    /// isabet kontrolünde tutmaz (güvenli yön).</summary>
    private static string? NearestAbove(string startDir, string fileName, List<FileStamp> probed)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var stamp = FileStamp.Of(System.IO.Path.Combine(dir.FullName, fileName));
            probed.Add(stamp);
            if (stamp.Length is not null) return stamp.Path;
        }
        return null;
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
