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
}
