using BuildOrchestrator.Core.MsBuild;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [okunan dosya kanıtı] <see cref="CompilerReferences"/>: C# derleyicisinin MSBuild çıktısına yazdığı komut
/// satırından <c>/reference:</c> yollarını okur. Döngü turları bir üyenin kardeşten GERÇEKTE hangi dosyayı
/// okuduğunu buradan öğrenir. Saf metin eşleşmesi — gerçek MSBuild gerekmez.
/// </summary>
public class CompilerReferencesTests
{
    // Gerçek OSYS logundan kısaltılmış satır: MSBuild çıktısı Türkçe olsa da derleyici satırı çevrilmez;
    // boşluk içeren yol tırnaklı, içermeyen tırnaksız yazılır.
    private const string OsysCompilerLine =
        @"  C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe /noconfig "
        + @"/nowarn:1701,1702 /fullpaths /nostdlib+ /define:DEBUG;TRACE "
        + @"/reference:C:\OSYS\Client\Bin\OSYS.UI.General.Common.dll "
        + @"/reference:""C:\Program Files (x86)\Infragistics\2020.1\WPF\CLR4.0\Bin\InfragisticsWPF4.v20.1.dll"" "
        + @"/reference:""D:\Projects\Delta\OSYS\General Projects\OSYS.Types.General.Common\bin\Debug\OSYS.Types.General.Common.dll"" "
        + @"/debug+ /out:obj\Debug\OSYS.UI.DMS.dll /target:library Properties\AssemblyInfo.cs";

    [Fact]
    public void a_compiler_line_yields_every_reference_quoted_or_not()
    {
        Assert.Equal(
            [
                @"C:\OSYS\Client\Bin\OSYS.UI.General.Common.dll",
                @"C:\Program Files (x86)\Infragistics\2020.1\WPF\CLR4.0\Bin\InfragisticsWPF4.v20.1.dll",
                @"D:\Projects\Delta\OSYS\General Projects\OSYS.Types.General.Common\bin\Debug\OSYS.Types.General.Common.dll",
            ],
            CompilerReferences.Parse(OsysCompilerLine));
    }

    [Fact] // SDK'nın kendi derleyicisi dotnet üzerinden koşar; satırın biçimi aynıdır.
    public void the_dotnet_hosted_compiler_is_read_the_same_way()
    {
        const string line = @"  ""C:\Program Files\dotnet\dotnet.exe"" exec ""C:\Program Files\dotnet\sdk\10.0.100\Roslyn\bincore\csc.dll"" "
            + @"/noconfig /reference:C:\lib\A.dll /out:obj\Debug\B.dll";

        Assert.Equal([@"C:\lib\A.dll"], CompilerReferences.Parse(line));
    }

    [Theory] // Derleyici çağrısı olmayan satır hiçbir şey söylemez: boş liste değil null (bilgi yok).
    [InlineData("Oluşturma başlatıldı: 27.09.2026 12:32:46.")]
    [InlineData(@"  Dosya ""C:\OSYS\Client\Bin\OSYS.UI.General.dll"" konumundan ""D:\x\bin\Debug\OSYS.UI.General.dll"" konumuna kopyalanıyor.")]
    [InlineData(@"C:\VS\Roslyn\Microsoft.CSharp.Core.targets(84,5): error MSB6006: ""csc.exe"" exited with code 1.")]
    public void a_line_that_is_not_a_compiler_invocation_yields_nothing(string line)
    {
        Assert.Null(CompilerReferences.Parse(line));
    }
}
