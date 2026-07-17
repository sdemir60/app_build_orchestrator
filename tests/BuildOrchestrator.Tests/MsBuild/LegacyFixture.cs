using System;
using System.IO;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// Test fixture: minimal, gerçek, derlenebilir v4.6 class library üretir (spike'ın kanıtladığı gibi
/// MSBuild.exe bu makinede legacy v4.6 projelerini derliyor). packages.config YOK, post-build copy YOK
/// (Task 13 gerekirse genişletir). Üretilen kaynak dosyasının adı sabit ("Class1.cs") — çağıran testler
/// (ör. derleme-hatası senaryosu) bu dosyanın üzerine bozuk kod yazarak fixture'ı genişletebilir.
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
}
