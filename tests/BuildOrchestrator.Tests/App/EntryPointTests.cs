using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 2] Velopack, kurulum/güncelleme kancalarını ana exe'yi <c>--veloapp-*</c> argümanıyla
/// çalıştırarak işletir ve <c>VelopackApp.Build().Run()</c>'ın WPF ayağa kalkmadan İLK ifade olmasını ister (kanca
/// modunda process orada biter; mutex, tepsi, DI hiç kurulmaz). WPF'in ürettiği Main bunu veremez → elle yazılmış
/// Program.Main + StartupObject. Kaynak guard'ı (SourceGuard deseni): headless bir testte Main koşturulamaz.</summary>
public class EntryPointTests
{
    private static string Csproj => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj"));
    private static string Program => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Program.cs"));

    [Fact]
    public void The_app_starts_from_the_hand_written_Program_class()
    {
        Assert.Contains("<StartupObject>BuildOrchestrator.App.Program</StartupObject>", Csproj, StringComparison.Ordinal);
        Assert.Contains("[STAThread]", Program, StringComparison.Ordinal);
        Assert.Contains("static void Main(string[] args)", Program, StringComparison.Ordinal);
    }

    [Fact]
    public void Velopack_runs_before_anything_else_in_Main()
    {
        // Main gövdesinde ilk ifade VelopackApp zinciri, WPF App ondan SONRA kurulur.
        int velopack = Program.IndexOf("VelopackApp.Build()", StringComparison.Ordinal);
        int run = Program.IndexOf(".Run();", velopack, StringComparison.Ordinal);
        int app = Program.IndexOf("new App()", StringComparison.Ordinal);
        Assert.True(velopack > 0 && run > velopack && app > run, "Main: VelopackApp.Build()…Run() önce, new App() sonra olmalı.");
        // Kanca kaydı: kaldırmada Windows başlangıç kaydı silinir (RemoveForUninstall) — dangling Run değeri kalmaz.
        Assert.Contains(".OnBeforeUninstallFastCallback(", Program, StringComparison.Ordinal);
        Assert.Contains("AutostartService.RemoveForUninstall(", Program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Velopack_package_and_the_vpk_tool_share_one_version()
    {
        var csproj = XDocument.Parse(Csproj);
        string package = csproj.Descendants("PackageReference").Single(p => (string?)p.Attribute("Include") == "Velopack")
            .Attribute("Version")!.Value;
        string manifest = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".config", "dotnet-tools.json"));
        string tool = Regex.Match(manifest, "\"vpk\"\\s*:\\s*\\{\\s*\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(tool, package); // vpk, kitaplıkla aynı sürümü bekler; ikisi ayrı yerde durur, bu test eşitler
    }
}
