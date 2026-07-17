using System;
using System.IO;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// Test fixture: minimal, gerçek, derlenebilir v4.6 class library üretir (spike'ın kanıtladığı gibi
/// MSBuild.exe bu makinede legacy v4.6 projelerini derliyor). packages.config YOK, post-build copy YOK
/// (Task 13 gerekirse genişletir). Üretilen kaynak dosyasının adı sabit ("Class1.cs") — çağıran testler
/// (ör. derleme-hatası senaryosu) bu dosyanın üzerine bozuk kod yazarak fixture'ı genişletebilir.
/// Fix wave 1 / Finding 1: <see cref="CreateClassLibWithLingeringPostBuild"/> istisna — post-build event
/// İÇEREN tek fixture, kasıtlı olarak MsBuildInvoker'ın başarı-yolu drain'ini test etmek için eklendi.
/// </summary>
public static class LegacyFixture
{
    public static string CreateClassLib(string dir, string assemblyName)
    {
        Directory.CreateDirectory(dir);

        string csPath = Path.Combine(dir, "Class1.cs");
        File.WriteAllText(csPath,
            $$"""
            namespace {{assemblyName}}
            {
                public class Class1
                {
                    public int Answer() => 42;
                }
            }
            """);

        string csprojPath = Path.Combine(dir, assemblyName + ".csproj");
        File.WriteAllText(csprojPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists('$(MSBuildToolsPath)\Microsoft.Common.props')" />
              <PropertyGroup>
                <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                <ProjectGuid>{{Guid.NewGuid():B}}</ProjectGuid>
                <OutputType>Library</OutputType>
                <RootNamespace>{{assemblyName}}</RootNamespace>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
              <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                <DebugSymbols>true</DebugSymbols>
                <DebugType>full</DebugType>
                <Optimize>false</Optimize>
                <OutputPath>bin\Debug\</OutputPath>
                <DefineConstants>DEBUG;TRACE</DefineConstants>
                <ErrorReport>prompt</ErrorReport>
                <WarningLevel>4</WarningLevel>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="System.Core" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Class1.cs" />
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);

        return csprojPath;
    }

    /// <summary>
    /// Fix wave 1 / Finding 1 regresyon fixture'ı: build sonrasında MSBuild.exe'nin KENDİSİ ÇIKTIKTAN SONRA da
    /// yaşayan, stdout/stderr pipe'ının bir kopyasını elinde tutan bir grandchild (ping.exe) bırakan v4.6
    /// classlib.
    /// <para>
    /// DENENDİ VE ELENDİ: &lt;Exec&gt; üzerinden "cmd /c start ... /b ping" — redirected/konsolsuz altında cmd'nin
    /// "start" iç komutu ASENKRON DAVRANMIYOR (ping bitene kadar senkron bekliyor). "powershell Start-Process
    /// -NoNewWindow" de denendi — powershell script'in kendisi ~40ms'de dönüyor (ölçüldü: BEFORE/AFTER marker
    /// farkı), FAKAT &lt;Exec&gt;'in TAMAMI yine de ping'in TÜM süresi kadar (~5.3s ping -n 6 için) sürüyor —
    /// yani MSBuild'in KENDİ &lt;Exec&gt; capture mekanizması, invoke ettiğimiz koddaki AYNI SINIF hatayı
    /// (pipe EOF'una kadar sınırsız bekleme) BİR KATMAN İÇERİDE de taşıyor; bu yüzden &lt;Exec&gt; üzerinden
    /// hangi teknik kullanılırsa kullanılsın MSBuild.exe kendisi grandchild ölmeden ASLA çıkmıyor — bizim
    /// düzeltmemizin sınamak istediği senaryo (MSBuild ÇIKAR, grandchild YAŞAMAYA DEVAM EDER) &lt;Exec&gt;
    /// üzerinden kurulamıyor.
    /// </para>
    /// <para>
    /// ÇALIŞAN teknik: RoslynCodeTaskFactory ile satır-içi (inline) bir MSBuild task'ı — Build sonrası
    /// doğrudan (Exec/cmd ARA KATMANI OLMADAN) <c>Process.Start</c> çağırır. .NET Framework'te
    /// UseShellExecute=false + yönlendirme YOK olan bu çağrı CreateProcess'i bInheritHandles=TRUE ile yapar
    /// (finding metnindeki tam mekanizma) ve WaitForExit ÇAĞRILMAZ — inline task hemen döner, MSBuild.exe
    /// normal şekilde Build'i bitirip ÇIKAR, ping.exe ise MSBuild.exe'nin miras verdiği stdout/stderr pipe
    /// uçlarının bir kopyasını <paramref name="sleepSeconds"/> kadar (MSBuild.exe çıktıktan SONRA da) açık
    /// tutmaya devam eder. Bu, &lt;Exec&gt;'in kendi (ayrı) yakalama borusunu DEVREYE SOKMADIĞI için yukarıdaki
    /// tuzağa düşmez.
    /// </para>
    /// </summary>
    public static string CreateClassLibWithLingeringPostBuild(string dir, string assemblyName, int sleepSeconds)
    {
        Directory.CreateDirectory(dir);

        string csPath = Path.Combine(dir, "Class1.cs");
        File.WriteAllText(csPath,
            $$"""
            namespace {{assemblyName}}
            {
                public class Class1
                {
                    public int Answer() => 42;
                }
            }
            """);

        int pingCount = sleepSeconds + 1; // ping -n (N+1) ≈ N saniye (ilk paket anında gider)

        string csprojPath = Path.Combine(dir, assemblyName + ".csproj");
        File.WriteAllText(csprojPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists('$(MSBuildToolsPath)\Microsoft.Common.props')" />
              <PropertyGroup>
                <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                <ProjectGuid>{{Guid.NewGuid():B}}</ProjectGuid>
                <OutputType>Library</OutputType>
                <RootNamespace>{{assemblyName}}</RootNamespace>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
              <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                <DebugSymbols>true</DebugSymbols>
                <DebugType>full</DebugType>
                <Optimize>false</Optimize>
                <OutputPath>bin\Debug\</OutputPath>
                <DefineConstants>DEBUG;TRACE</DefineConstants>
                <ErrorReport>prompt</ErrorReport>
                <WarningLevel>4</WarningLevel>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="System.Core" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Class1.cs" />
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <UsingTask TaskName="SpawnLingeringGrandchild" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
                <ParameterGroup>
                  <Seconds ParameterType="System.Int32" Required="true" />
                </ParameterGroup>
                <Task>
                  <Using Namespace="System.Diagnostics" />
                  <Code Type="Fragment" Language="cs"><![CDATA[
                    var psi = new ProcessStartInfo("ping.exe", "-n " + (Seconds + 1) + " 127.0.0.1")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    Process.Start(psi);
                  ]]></Code>
                </Task>
              </UsingTask>
              <Target Name="LingeringPostBuildCopy" AfterTargets="Build">
                <SpawnLingeringGrandchild Seconds="{{sleepSeconds}}" />
              </Target>
            </Project>
            """);

        return csprojPath;
    }

    /// <summary>
    /// Fix wave 2 / Finding 1 regresyon fixture'ı: <see cref="CreateClassLibWithLingeringPostBuild"/> ile AYNI
    /// "MSBuild.exe çıkar, grandchild yaşamaya devam eder" senaryosu, FAKAT grandchild'in ÇIKTISI GÜVENİLİR
    /// şekilde bizim inherited pipe'ımıza yazılır. Deneysel olarak doğrulandı: ping.exe VE düz
    /// (redirect'siz) powershell.exe HER İKİSİ DE bu ortamda MSBuild.exe'nin miras verdiği pipe UCUNU açık
    /// tutuyor (pump'ın EOF'u grandchild çıkana kadar gerçekten gecikiyor) FAKAT KENDİ çıktılarını o pipe'a HİÇ
    /// YAZMIYORLAR — muhtemelen konsolsuz/headless bir ata (CreateNoWindow+STARTF_USESTDHANDLES ile başlatılan
    /// MSBuild.exe) altında, STARTF_USESTDHANDLES OLMADAN başlatılan konsol alt-sistemi child'ları için Windows
    /// KENDİ (görünmez) yeni bir konsol ayırıyor; miras alınan pipe uçları yalnız "kazara" (bInheritHandles=TRUE
    /// blanket inheritance) açık kalıyor, hiç KULLANILMIYOR. Bu da "abandoned pump geç bir SATIR yakalar"
    /// senaryosunu (Finding 1'in asıl iddiası) ping/powershell'in KENDİ çıktısıyla KANITLANAMAZ hale getirir.
    /// <para>
    /// Bu yüzden grandchild, kendi stdout'unu DEĞİL, MSBuild.exe'nin (yani BİZİM pipe'ımızın) tam handle
    /// DEĞERİNİ (<c>GetStdHandle(STD_OUTPUT_HANDLE)</c>, RoslynCodeTaskFactory'nin <c>Type="Class"</c> +
    /// <c>DllImport</c> ile MSBuild.exe İÇİNDEN okunur) argüman olarak alıp doğrudan <c>WriteFile</c> ile o
    /// handle'a yazan bir PowerShell script'i başlatır — Windows'ta inherited handle DEĞERLERİ child'ta AYNI
    /// sayı olarak kalır, dolayısıyla bu, MSBuild.exe'nin exit ettiği pipe ucuna kesin/garantili teslimat sağlar.
    /// </para>
    /// </summary>
    public static string CreateClassLibWithLingeringPostBuildTextWriter(string dir, string assemblyName, int seconds)
    {
        Directory.CreateDirectory(dir);

        string csPath = Path.Combine(dir, "Class1.cs");
        File.WriteAllText(csPath,
            $$"""
            namespace {{assemblyName}}
            {
                public class Class1
                {
                    public int Answer() => 42;
                }
            }
            """);

        string csprojPath = Path.Combine(dir, assemblyName + ".csproj");
        File.WriteAllText(csprojPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists('$(MSBuildToolsPath)\Microsoft.Common.props')" />
              <PropertyGroup>
                <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                <ProjectGuid>{{Guid.NewGuid():B}}</ProjectGuid>
                <OutputType>Library</OutputType>
                <RootNamespace>{{assemblyName}}</RootNamespace>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
              <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                <DebugSymbols>true</DebugSymbols>
                <DebugType>full</DebugType>
                <Optimize>false</Optimize>
                <OutputPath>bin\Debug\</OutputPath>
                <DefineConstants>DEBUG;TRACE</DefineConstants>
                <ErrorReport>prompt</ErrorReport>
                <WarningLevel>4</WarningLevel>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="System.Core" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Class1.cs" />
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <UsingTask TaskName="SpawnLingeringGrandchildWriter" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
                <ParameterGroup>
                  <Seconds ParameterType="System.Int32" Required="true" />
                </ParameterGroup>
                <Task>
                  <Reference Include="$(MSBuildToolsPath)\Microsoft.Build.Framework.dll" />
                  <Reference Include="$(MSBuildToolsPath)\Microsoft.Build.Utilities.Core.dll" />
                  <Code Type="Class" Language="cs"><![CDATA[
                    using System;
                    using System.Diagnostics;
                    using System.IO;
                    using System.Runtime.InteropServices;
                    using Microsoft.Build.Framework;
                    using Microsoft.Build.Utilities;

                    public class SpawnLingeringGrandchildWriter : Task
                    {
                        public int Seconds { get; set; }

                        [DllImport("kernel32.dll", SetLastError = true)]
                        private static extern IntPtr GetStdHandle(int nStdHandle);

                        private const int STD_OUTPUT_HANDLE = -11;

                        public override bool Execute()
                        {
                            long handleValue = GetStdHandle(STD_OUTPUT_HANDLE).ToInt64();
                            string scriptPath = Path.Combine(Path.GetTempPath(), "boi-grandchild-writer-" + Guid.NewGuid().ToString("N") + ".ps1");
                            string script =
                                "Add-Type -Name W -Namespace N -MemberDefinition '[System.Runtime.InteropServices.DllImport(\"kernel32.dll\")] public static extern bool WriteFile(IntPtr h, byte[] b, int n, out int w, IntPtr o);'\r\n" +
                                "$h = [IntPtr]" + handleValue + "\r\n" +
                                "for ($i = 1; $i -le " + Seconds + "; $i++) {\r\n" +
                                "  $b = [System.Text.Encoding]::ASCII.GetBytes('GRANDCHILD-LINE-' + $i + \"`r`n\")\r\n" +
                                "  $w = 0\r\n" +
                                "  [N.W]::WriteFile($h, $b, $b.Length, [ref] $w, [IntPtr]::Zero) | Out-Null\r\n" +
                                "  Start-Sleep -Seconds 1\r\n" +
                                "}\r\n";
                            File.WriteAllText(scriptPath, script);

                            var psi = new ProcessStartInfo("powershell.exe",
                                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            };
                            Process.Start(psi);
                            return true;
                        }
                    }
                  ]]></Code>
                </Task>
              </UsingTask>
              <Target Name="LingeringPostBuildWriter" AfterTargets="Build">
                <SpawnLingeringGrandchildWriter Seconds="{{seconds}}" />
              </Target>
            </Project>
            """);

        return csprojPath;
    }

    /// <summary>
    /// Task 13 (T9) fixture'ı: <see cref="CreateClassLib"/> ile AYNI derlenebilir v4.6 classlib, FAKAT gerçek bir
    /// VS-parity post-build copy EKLER — derlenen DLL, projenin kendi <c>bin\Debug</c>'ı DIŞINDA,
    /// <paramref name="sharedBinDir"/> altındaki ORTAK bir dizine de kopyalanır (Copy task DestinationFolder'ı
    /// kendisi oluşturur). Kill-mid-build testi bu ortak dizini okuyarak "torn DLL yok" iddiasını (kill'den önce
    /// başarıyla biten her projenin DLL'i geçerli bir PE'dir) doğrular — paralel derlenen ≥2 proje AYNI çıktı
    /// dizinine yazdığı için bir yarım kalmış kopya gözlemlenebilir olurdu.
    /// <para>
    /// Fix wave 1 / Finding 1: gecikme artık <c>CoreCompile</c>'DAN ÖNCE değil, <c>Build</c> hedefinden SONRA
    /// (yani <c>csc.exe</c> GERÇEKTEN çalışıp DLL projenin kendi <c>bin\Debug</c>'ına yazıldıktan SONRA) ama ortak
    /// bin'e kopyadan HEMEN ÖNCE devreye giriyor (<c>CopyToSharedBin</c> hedefinin başında). Eski konumda
    /// (compile'dan önce) kill her zaman derleyici hiç doğmadan gelirdi — torn-DLL senaryosu yapısal olarak asla
    /// tetiklenemezdi. Yeni konumda derleyici GERÇEKTEN çalışmış olur; <paramref name="delaySeconds"/> &gt; 0 olan
    /// projeler için MSBuild.exe, ortak bine kopyayı henüz YAPMADAN bloke kalır — kill o anda gelirse gerçek,
    /// tamamlanmamış bir "writer" öldürülmüş olur (Exec'in KENDİ senkron bekleme mekanizması kullanılıyor,
    /// LingeringPostBuild varyantlarının aksine grandchild bırakmaz).
    /// </para>
    /// <para>
    /// <paramref name="delaySeconds"/> = 0 → gecikme YOK ("hızlı/kontrol" proje: compile + ortak-bin kopyası
    /// erken tamamlanır, kill'den önce GERÇEK, geçerli bir DLL üretilmiş olur). Kill-mid-build testi, bir kısım
    /// projeyi 0 (hızlı) bir kısmını &gt;0 (yavaş) vererek aynı anda hem "kill öncesi gerçekten bitmiş bir DLL"
    /// hem de "kill anında hâlâ uçuşta, kopyası henüz yapılmamış bir MSBuild.exe" gözlemler.
    /// </para>
    /// </summary>
    public static string CreateClassLibWithSharedBinCopy(string dir, string assemblyName, string sharedBinDir, int delaySeconds = 3)
    {
        Directory.CreateDirectory(dir);

        string csPath = Path.Combine(dir, "Class1.cs");
        File.WriteAllText(csPath,
            $$"""
            namespace {{assemblyName}}
            {
                public class Class1
                {
                    public int Answer() => 42;
                }
            }
            """);

        // delaySeconds = 0 → Exec hedefi hiç eklenmiyor (gecikmesiz "hızlı/kontrol" proje).
        string delayExec = delaySeconds > 0
            ? $"""<Exec Command="ping -n {delaySeconds + 1} 127.0.0.1 &gt;NUL" />"""
            : "";

        string csprojPath = Path.Combine(dir, assemblyName + ".csproj");
        File.WriteAllText(csprojPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists('$(MSBuildToolsPath)\Microsoft.Common.props')" />
              <PropertyGroup>
                <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
                <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
                <ProjectGuid>{{Guid.NewGuid():B}}</ProjectGuid>
                <OutputType>Library</OutputType>
                <RootNamespace>{{assemblyName}}</RootNamespace>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
              <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                <DebugSymbols>true</DebugSymbols>
                <DebugType>full</DebugType>
                <Optimize>false</Optimize>
                <OutputPath>bin\Debug\</OutputPath>
                <DefineConstants>DEBUG;TRACE</DefineConstants>
                <ErrorReport>prompt</ErrorReport>
                <WarningLevel>4</WarningLevel>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="System.Core" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Class1.cs" />
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <Target Name="CopyToSharedBin" AfterTargets="Build">
                {{delayExec}}
                <ItemGroup>
                  <_SharedBinCopy Include="$(OutDir)$(AssemblyName).dll" />
                </ItemGroup>
                <Copy SourceFiles="@(_SharedBinCopy)" DestinationFolder="{{sharedBinDir}}" />
              </Target>
            </Project>
            """);

        return csprojPath;
    }
}
