using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

/// <summary>
/// [idle-memory · dalga 2 · R3] Bir projenin yoldan türeyen görünen adının TEK tanımı (<see cref="ProjectDisplayName.FromPath"/>):
/// proje dosyasının uzantısız adı — klasördeki noktalar ve boşluklar adı bölmez, uzantı (csproj/vbproj) ne olursa olsun. Planın,
/// motorun ve uygulamanın adı bilinmeyen kimlik için yazdığı ad bu tanımdan gelir.
/// <para>[DEĞİŞEN KAPSAM — dalga 2 son inceleme · defter 10] Eski metin keşfin <c>AssemblyName</c> varsayılanının da bu tanımdan
/// geldiğini söylüyordu; o varsayılan derlemenin kimliğidir ve değerlendiricinin kendi MSBuild kuralındadır
/// (<c>$(MSBuildProjectName)</c> — pin: <c>CsprojEvaluatorTests</c>, "default = dosya adı").</para>
/// </summary>
public class ProjectDisplayNameTests
{
    [Theory]
    [InlineData(@"C:\p\A.csproj", "A")]
    [InlineData(@"D:\Projects\Delta\OSYS\UsedCars\OSYS.UsedCar\OSYS.Types.Customer\OSYS.Types.Customer.csproj", "OSYS.Types.Customer")]
    [InlineData(@"D:\Projects\Delta\OSYS\NewSales\NewSales Stock\NewSales Stock\OSYS.UI.NewSales.Stock.vbproj", "OSYS.UI.NewSales.Stock")]
    public void The_display_name_is_the_project_files_name_without_its_extension(string projectPath, string name) =>
        Assert.Equal(name, ProjectDisplayName.FromPath(projectPath));
}
