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
    /// [PERF Faz C/C6] İki dosyanın SABİT, ESKİ değişiklik tarihi (UTC). MSBuild <c>CustomBeforeMicrosoftCommonTargets</c>
    /// import'unu her projenin <c>$(MSBuildAllProjects)</c> listesine katar ve <c>CoreCompile</c>'ın girdi denetimi o
    /// listenin EN YENİ dosyasına bakar: targets dosyası yeni bir tarihle yazılınca (yeni bir sürümün içeriği
    /// değiştirmesi ya da dosyanın ilk kez yazılması) motorun çağırdığı HER proje, çıktısı güncel olsa bile bir kez
    /// baştan derlenirdi (ölçüldü: Rebuild 46 → 78 sn). Import nihai çıktıyı değiştirmez (acceptance testi: aynı
    /// BAML baytları, aynı DLL boyutu), yani geçersizleştirmeye katılmamalıdır; bu yüzden iki dosya HER durumda bu
    /// eski tarihle durur — yazımdan sonra da, içerik zaten aynıyken de.
    /// <para><b>Sınır:</b> bu import bir gün nihai çıktıyı değiştirirse bu çekme KALDIRILMALIDIR — aksi halde MSBuild
    /// değişikliği görmez ve eski çıktıyı güncel sayar.</para>
    /// <para>Dosyalar aracın kendi önbellek kökündedir (<c>%LOCALAPPDATA%\BuildOrchestrator\msbuild\</c>), kullanıcının
    /// OutDir'inde ya da obj'unda DEĞİL: "OutDir'e dokunulmaz" değişmezi bozulmaz.</para>
    /// </summary>
    public static readonly DateTime FixedTimestampUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <c>&lt;cacheRoot&gt;\msbuild\</c> altına iki dosyayı yazar ve targets dosyasının TAM yolunu döner. İçerik
    /// aynıysa içeriğe dokunmaz, tarihini ise her durumda <see cref="FixedTimestampUtc"/>'ye çeker; farklıysa ya da yoksa yazar, yani bozulan ya da eski bir dosya,
    /// bir motorun MSBuild'i ilk çözdüğü anda (ilk koşu ya da ilk Optimize) onarılır; çözüm motor başına bir kez
    /// yapıldığından sonraki onarım yeni bir motoru bekler. Atomik yazım yoktur: motor tek yazıcıdır. G/Ç
    /// hatasında FIRLATIR — çağıran bunun bir optimizasyon olduğunu bilir ve derlemeyi targets'sız sürdürür.
    /// </summary>
    public static string EnsureWritten(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        // Tam yol: göreli bir kök (ör. göreli --logs) MSBuild'in çalışma dizinine göre çözülür ve targets'ın Exists
        // koşulu sessizce düşer — optimizasyon hiçbir uyarı vermeden kapanırdı.
        string directory = Path.Combine(Path.GetFullPath(cacheRoot), FolderName);
        Directory.CreateDirectory(directory);

        // Friend ÖNCE: "targets var ⇒ friend var". Yazım targets'tan önce yarıda kalsa bile targets'ı gören bir
        // MSBuild olmayan bir kaynak dosyasına bağlanmaz.
        EnsureFile(Path.Combine(directory, FriendFileName), FriendContent);
        string targetsPath = Path.Combine(directory, TargetsFileName);
        EnsureFile(targetsPath, TargetsContent);
        return targetsPath;
    }

    /// <summary>İçerik farklıysa ya da dosya yoksa yazar; tarihi HER durumda <see cref="FixedTimestampUtc"/>'ye çeker.</summary>
    private static void EnsureFile(string path, string content)
    {
        bool identical = File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal);
        if (!identical)
            File.WriteAllText(path, content); // UTF-8 (BOM'suz); içerik ASCII
        // Tarih yazımdan sonra da, içerik zaten aynıyken de çekilir: önceki bir sürümün yeni tarihle yazdığı dosya da
        // düzelir. Zaten sabitse dosyaya hiç dokunulmaz.
        if (File.GetLastWriteTimeUtc(path) != FixedTimestampUtc)
            File.SetLastWriteTimeUtc(path, FixedTimestampUtc);
    }
}
