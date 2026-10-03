namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// WPF'in yerel tipli XAML için derlediği geçici assembly (<c>&lt;proje&gt;_&lt;rastgele&gt;_wpftmp</c>) yalnız
/// yansımayla okunur; gövdesiz (metadata-only) derlenmesi çıktıyı değiştirmez, süreyi kısaltır. Üç öğe BİRLİKTE
/// gerekir (bkz. ARCHITECTURE.md §9.2):
/// <list type="number">
/// <item><c>ProduceOnlyReferenceAssembly=true</c>: derleyici metot gövdelerini atlar.</item>
/// <item><c>ProduceReferenceAssembly=false</c>: SDK-style projede <c>/refout</c> ile <c>/refonly</c> birlikte
/// verilirse derleme CS8308 ile düşer.</item>
/// <item>Geçici assembly'ye <c>InternalsVisibleTo</c> taşıyan tek kaynak dosyası: gövdesiz derleme internal
/// üyeleri atar, internal üyeye bağlanan XAML ise MC3072 ile düşer.</item>
/// </list>
///
/// <para>Targets dosyası <c>-p:CustomBeforeMicrosoftCommonTargets=</c> ile verilir
/// (<see cref="MsBuildArguments.Build"/>), yalnız proje adı <c>_wpftmp</c> ile biten geçici projede etki eder ve
/// hiçbir çıktı yolunu (OutDir, obj) DEĞİŞTİRMEZ. Global property MSBuild'in varsayılan Custom.Before import'unun
/// yerine geçtiği için o dosya (<c>Custom.Before.Microsoft.Common.targets</c>) burada yeniden import edilir —
/// zincir kopmaz. Projenin kendi <c>CustomBeforeMicrosoftCommonTargets</c> tanımını ezer: bilinen sınır.</para>
///
/// <para>İçerik ve dosya adları TEK yerdedir (kopya YASAK). Dosyalar motorun önbellek kökü altındaki
/// <c>msbuild\</c> klasörüne yazılır — kullanıcının çalışma ağacına DEĞİL.</para>
/// </summary>
public static class WpfTemporaryAssemblyTargets
{
    public const string TargetsFileName = "wpf-temporary-assembly.targets";
    public const string FriendFileName = "wpf-temporary-assembly-friend.cs";
    public const string FriendAssemblyName = "BuildOrchestrator.WpfTemporaryAssemblyFriend";

    /// <summary>İki dosyanın önbellek kökü altındaki klasörü.</summary>
    private const string FolderName = "msbuild";

    /// <summary>
    /// Targets dosyasının içeriği (XML). Friend dosyasının yolu <c>$(MSBuildThisFileDirectory)</c> ile göreli
    /// verilir: iki dosya hep yan yana durur. Satır sonları platformunkine sabitlenir — içerik, kaynak dosyanın
    /// checkout satır sonundan bağımsız, her makinede aynı baytlardır.
    /// </summary>
    public static string TargetsContent { get; } = $"""
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <!-- Build Orchestrator: WPF's temporary local-type assembly is only reflected over; compile it as metadata only. -->
          <Import Project="$(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets"
                  Condition="Exists('$(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets')" />
          <PropertyGroup Condition="$(MSBuildProjectName.EndsWith('_wpftmp'))">
            <ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>
            <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
          </PropertyGroup>
          <ItemGroup Condition="$(MSBuildProjectName.EndsWith('_wpftmp'))">
            <Compile Include="$(MSBuildThisFileDirectory){FriendFileName}" />
          </ItemGroup>
        </Project>
        """.ReplaceLineEndings();

    /// <summary>Yalnız geçici assembly'ye derlenen kaynak: internal üyelerin gövdesiz derlemede de kalmasını sağlar.</summary>
    public static string FriendContent { get; } = $"""
        // Compiled only into WPF's temporary assembly so that a metadata-only build keeps internal members.
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("{FriendAssemblyName}")]
        """.ReplaceLineEndings();

    /// <summary>
    /// <c>&lt;cacheRoot&gt;\msbuild\</c> altına iki dosyayı yazar ve targets dosyasının TAM yolunu döner. İçerik
    /// aynıysa dosyaya dokunmaz (mtime korunur); farklıysa ya da yoksa yazar, yani bozulan ya da eski bir dosya her
    /// açılışta onarılır. Atomik yazım yoktur: motor tek yazıcıdır. G/Ç hatasında FIRLATIR — çağıran bunun bir
    /// optimizasyon olduğunu bilir ve derlemeyi targets'sız sürdürür.
    /// </summary>
    public static string EnsureWritten(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        string directory = Path.Combine(cacheRoot, FolderName);
        Directory.CreateDirectory(directory);

        string targetsPath = Path.Combine(directory, TargetsFileName);
        WriteIfDifferent(targetsPath, TargetsContent);
        WriteIfDifferent(Path.Combine(directory, FriendFileName), FriendContent);
        return targetsPath;
    }

    private static void WriteIfDifferent(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            return;
        File.WriteAllText(path, content); // UTF-8 (BOM'suz); içerik ASCII
    }
}
