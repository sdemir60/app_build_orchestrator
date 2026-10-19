using System.IO;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [temizlik madde 4] Kaynak guard'larının ortak iskeletinin (<see cref="SourceGuard"/>) KENDİ kanıtları. Tüm-<c>src</c> taramalı guard'lar
/// "her üretim projesine gerçekten baktım" iddiasını <see cref="SourceGuard.AssertScansEveryProductionProject"/> ile kurar; o iddianın
/// başarısızlık yolu gerçek ağaçta hiç koşmaz (ağaç hep tamdır), bu yüzden saf çekirdeği sahte listelerle sınanır.
/// </summary>
public class SourceGuardTests
{
    private static string In(string project, string file) => Path.Combine(project, file);

    /// <summary>Dosyası taranmamış üretim projesi adıyla raporlanır; ad öneki aynı olan başka bir klasör o projeyi saymaz; her projeden bir
    /// dosya varsa rapor boştur ve gerçek tarama (guard'ların deseniyle) boştur.</summary>
    [Fact]
    public void A_production_project_with_no_scanned_file_is_reported_by_name()
    {
        string[] allButContracts =
        [
            In("BuildOrchestrator.App", "App.xaml.cs"),
            In("BuildOrchestrator.Core", "Graph.cs"),
            In("BuildOrchestrator.Supervisor", "Program.cs"),
            In("BuildOrchestrator.ContractsTools", "Fake.cs"), // ad öneki Contracts'la aynı, klasörü değil
        ];

        Assert.Equal(["BuildOrchestrator.Contracts"], SourceGuard.UnseenProductionProjects(allButContracts));
        Assert.Equal(SourceGuard.ProductionProjects, SourceGuard.UnseenProductionProjects([]));
        Assert.Empty(SourceGuard.UnseenProductionProjects([.. allButContracts, In("BuildOrchestrator.Contracts", "Ipc.cs")]));
        Assert.Empty(SourceGuard.UnseenProductionProjects(SourceGuard.ScannedSrcFiles("*.cs")));
    }
}
