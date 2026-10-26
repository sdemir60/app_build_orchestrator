using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [v1.18.0 review R1 finding 2] Konsol başlığı SEÇİLİ satırı canlı izler, yalnız SEÇİM anını değil.
/// <c>ShowProjectLog</c> yalnız <c>MainWindow.OnSelectedProjectChangedAsync</c>'ten (kart tıklaması) çağrılır;
/// 200ms'lik run tick'i yalnız <c>SetLineCount</c>'u sürer (<c>MainWindow.xaml.cs</c> ~290-301). Bu yüzden
/// dependency-issue/cycle üyeliği SONRADAN gelirse başlık eski (bayat) durumda donuyordu. <c>MainWindow.TrackHeaderRow</c>
/// bunu satırın kendi <c>PropertyChanged</c>'ine abone olarak kapatır.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-25]</b> Abonelik eskiden statü glyph'ini ve statü sözcüğünü de
/// sürerdi (seçili proje derlerken bitirince spinner durur, sözcük "Succeeded" olurdu). Başlık koşu statüsünü artık
/// göstermez (gerekçe: <see cref="ConsoleHeaderDesignTests"/>'in sınıf özeti); abonelik yalnız iki rozeti tazeler.</para>
///
/// <para><b>Neden <c>SelectProject</c> ÜZERİNDEN değil:</b> <c>MainWindowHost</c>'un ürettiği
/// <c>EngineHost</c> hiçbir zaman <c>Start()</c> edilmez (gerçek bir Supervisor süreci doğurmak bu testleri
/// Acceptance kategorisine iterdi); <c>EngineHost.SendAsync</c> böyle bir motorda SENKRON fırlar
/// ("Engine is not running") ve <c>OnSelectedProjectChangedAsync</c> proje moduna HİÇ girmeden erken döner —
/// bu, test edilen şeyle (satır değişince başlığın tazelenmesi) İLGİSİZ, engine round-trip'ine ait ayrı bir
/// sorundur (Acceptance süiti onu zaten kapsar). Bu yüzden testler <c>OnSelectedProjectChangedAsync</c>'in
/// proje-log moduna girdiğinde çalıştırdığı AYNI iki satırı (<c>ShowProjectLog</c> + <c>TrackHeaderRow</c>,
/// bkz. <c>MainWindow.xaml.cs</c>) doğrudan çağırır — <c>TrackHeaderRow</c> tam da bunun için
/// <c>internal</c>'dır (test yüzeyi). Kanıtlanan, bu iki satırın GERÇEK GÖVDESİdir; taklit bir kopyası değil.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleHeaderLiveRefreshTests
{
    private static (ProjectRowViewModel row, ConsoleHeader header) SelectViaHeaderWiring(
        BuildOrchestrator.App.MainWindow window, BuildOrchestrator.App.ViewModels.RunViewModel vm, string projectId)
    {
        var row = vm.Projects.Single(p => p.Id == projectId);
        var header = window.Shell.ConsoleHeaderControl;
        // [MainWindow.OnSelectedProjectChangedAsync'in proje-log dalıyla AYNI sıra]
        header.ShowProjectLog(row, 0);
        window.TrackHeaderRow(row);
        return (row, header);
    }

    /// <summary>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-25] Seçili satırın koşu statüsü seçim değişmeden ilerlerken
    /// (başlar, döngü grubunda sırasını bekler, sıra ona geçer, biter) başlık hiçbir aşamada statü çizmez: glyph/spinner
    /// yoktur ve hiçbir metin bir statü sözcüğü değildir. Eski iddialar (iki test): başlığın glyph'i ve statü sözcüğü
    /// satırın kendi <see cref="ProjectRowViewModel.Status"/>'unu canlı izlerdi — proje bitince spinner durur, sözcük
    /// "Succeeded" olurdu (<c>A_selected_project_finishing_its_build_updates_the_header_without_a_reselect</c>); sırasını
    /// bekleyen Started döngü üyesi Queued, sıra ona geçince Building yazardı
    /// (<c>Header_glyph_and_status_word_follow_the_rows_own_Status_including_a_live_IsCompiling_flip</c>).</summary>
    [StaFact]
    public void A_selected_project_moving_through_a_run_never_draws_a_status_in_the_header()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        var (row, header) = SelectViaHeaderWiring(window, vm, idA);
        AssertNoRunStatus(header); // koşuya girmemiş (eski başlıkta: kesikli halka + "Discovered")

        vm.OnEvent(new BuildOrchestrator.Contracts.Ipc.ProjectStartedEvent("r1", idA, "A"));
        AssertNoRunStatus(header); // Building
        row.CycleWaiting = true;
        Assert.Equal(GraphStatus.Queued, row.Status); // ön-koşul: satırın kendi statüsü değişti
        AssertNoRunStatus(header);
        row.CycleWaiting = false;
        row.State = ProjectRowState.Succeeded; // satırdaki gerçek geçiş yolu — RunViewModel.OnProjectDone
        AssertNoRunStatus(header);
        Assert.Equal("A", header.ProjectNameText.Text);
        GC.KeepAlive(window);
    }

    private static readonly string[] RunStatusWords = ["Discovered", "Queued", "Building", "Succeeded", "Failed", "Skipped"];

    private static void AssertNoRunStatus(ConsoleHeader header)
    {
        var tree = DsResources.Descendants(header).ToList();
        Assert.DoesNotContain(tree, e => e is StatusGlyph or BuildingSpinner);
        Assert.DoesNotContain(tree.OfType<TextBlock>(), t => RunStatusWords.Contains(t.Text));
    }

    [StaFact]
    public void A_dependency_issue_arriving_or_clearing_on_the_selected_row_toggles_the_badge_live()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");

        var (row, header) = SelectViaHeaderWiring(window, vm, idA);
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility); // ön-koşul: henüz uyarı yok

        // [R1 bulgu 2] Canlı VARIŞ: satırın kendi DepIssues'u değişir — seçim/ShowProjectLog TEKRAR ÇAĞRILMAZ.
        row.DepIssues = ["B"];
        Assert.Equal(Visibility.Visible, header.DepIssueBadge.Visibility);
        Assert.Equal("Dependency issue: B — last successful output referenced", header.DepIssueTooltip.Content);

        // Canlı KAYBOLMA: liste aynı satırı temizler (ör. bir sonraki Sync) — rozet de birlikte iner.
        row.DepIssues = null;
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Döngü üyeliği de seçili satıra sonradan gelir (topoloji uzlaştırması) ve gider: rozet seçim değişmeden
    /// belirir ve iner, tooltip'i satırın döngü cümlesidir. Abonelik yalnız iki rozetin alanlarını dinler
    /// (<c>MainWindow.OnHeaderTrackedRowChanged</c>); bu test döngü alanını pinler.</summary>
    [StaFact]
    public void A_cycle_membership_arriving_or_clearing_on_the_selected_row_toggles_the_badge_live()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");

        var (row, header) = SelectViaHeaderWiring(window, vm, idA);
        Assert.Equal(Visibility.Collapsed, header.CycleBadge.Visibility); // ön-koşul: döngü üyesi değil

        row.InCycle = true;
        Assert.Equal(Visibility.Visible, header.CycleBadge.Visibility);
        Assert.Equal(RowWarning.InCycle, header.CycleTooltip.Content);

        row.InCycle = false;
        Assert.Equal(Visibility.Collapsed, header.CycleBadge.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Seçim BAŞKA bir projeye geçince eski satırın aboneliği bırakılır — aksi halde eski satırdaki
    /// bir değişiklik, artık ekranda olmayan bir başlığı (ya da daha kötüsü, YANLIŞ projenin başlığını)
    /// tazelerdi.</summary>
    [StaFact]
    public void Switching_the_selection_stops_listening_to_the_previously_selected_row()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        string idB = MainWindowHost.IdOf("B");

        var (rowA, header) = SelectViaHeaderWiring(window, vm, idA);
        SelectViaHeaderWiring(window, vm, idB); // ikinci seçim: A'nın aboneliği TrackHeaderRow içinde bırakılır
        Assert.Equal("B", header.ProjectNameText.Text); // ön-koşul

        rowA.DepIssues = ["X"]; // artık seçili olmayan A'da bir değişiklik

        Assert.Equal("B", header.ProjectNameText.Text); // başlık B'de kalır
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility); // A'nın rozeti B'ye SIZMADI
        GC.KeepAlive(window);
    }
}
