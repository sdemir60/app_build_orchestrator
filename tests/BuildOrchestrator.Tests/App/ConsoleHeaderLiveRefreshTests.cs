using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [v1.18.0 review R1 finding 2] Konsol başlığı SEÇİLİ satırı canlı izler, yalnız SEÇİM anını değil.
/// <c>ShowProjectLog</c> yalnız <c>MainWindow.OnSelectedProjectChangedAsync</c>'ten (kart tıklaması) çağrılır;
/// 200ms'lik run tick'i yalnız <c>SetLineCount</c>'u sürer (<c>MainWindow.xaml.cs</c> ~290-301). Bu yüzden
/// seçili bir proje derlenirken bitirirse (Started→Succeeded) ya da dependency-issue/cycle üyeliği SONRADAN
/// gelirse, başlık eski (bayat) durumda donuyordu — spinner sonsuza dek dönerdi. <c>MainWindow.TrackHeaderRow</c>
/// bunu satırın kendi <c>PropertyChanged</c>'ine abone olarak kapatır.
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

    [StaFact]
    public void A_selected_project_finishing_its_build_updates_the_header_without_a_reselect()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        vm.OnEvent(new BuildOrchestrator.Contracts.Ipc.ProjectStartedEvent("r1", idA, "A"));

        var (row, header) = SelectViaHeaderWiring(window, vm, idA);

        // ön-koşul: statü "Building" (spinner dönüyor).
        Assert.Equal(VisualStatus.Building, header.StatusGlyphIcon.Status);
        var spinner = Assert.Single(DsResources.Descendants(header.StatusGlyphIcon).OfType<BuildingSpinner>());
        Assert.Equal(Visibility.Visible, spinner.Visibility);

        // Proje biter — SEÇİM DEĞİŞMEZ (ShowProjectLog/TrackHeaderRow TEKRAR ÇAĞRILMAZ), yalnız satırın kendi
        // State'i değişir (satırdaki gerçek geçiş yolu — RunViewModel.OnProjectDone).
        row.State = ProjectRowState.Succeeded;

        Assert.Equal(VisualStatus.Succeeded, header.StatusGlyphIcon.Status);
        Assert.Equal("Succeeded", header.StatusNameText.Text);
        Assert.Equal(Visibility.Collapsed, spinner.Visibility); // aynı kontrol örneği — artık dönmüyor
        GC.KeepAlive(window);
    }

    /// <summary>[Final review I-2 — tek doğruluk kaynağı] Başlığın glyph'i satırın KENDİ
    /// <see cref="ProjectRowViewModel.Status"/>'unu izler (satır ve graf da onu okur): bir döngü grubunda sırası
    /// kendisinde olmayan Started üye (<see cref="ProjectRowViewModel.IsCompiling"/> false) satırda Queued'dır,
    /// başlıkta da Queued olmalıdır — ikinci bir Started→Building eşlemesi başlıkta spinner döndürüyordu. Sıra
    /// üyeye geçtiğinde (IsCompiling true) başlık seçim değişmeden spinner'a geçer.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Statü YAZISI eskiden <c>State</c>'ten (motor sözlüğü, <c>ConsoleStatus.Name</c>)
    /// geliyordu, yani bu senaryoda glyph Queued gösterirken yazı hâlâ "Building" yazıyordu (State hâlâ Started) —
    /// ikon ile yazı ayrışıyordu. Kullanıcı kararıyla yazı da <see cref="ProjectRowViewModel.Status"/>'u izler;
    /// ikon, yazı ve renk artık AYNI kaynaktan (<c>StatusGlyph.LabelFor</c>/<c>BrushKeyFor</c>) okunur.</para></summary>
    [StaFact]
    public void Header_glyph_and_status_word_follow_the_rows_own_Status_including_a_live_IsCompiling_flip()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        vm.OnEvent(new BuildOrchestrator.Contracts.Ipc.ProjectStartedEvent("r1", idA, "A"));
        var rowA = vm.Projects.Single(p => p.Id == idA);
        rowA.CycleWaiting = true; // Started ama derlenmiyor — satır Queued gösterir
        Assert.Equal(GraphStatus.Queued, rowA.Status); // ön-koşul: satırın kendi statüsü

        var (row, header) = SelectViaHeaderWiring(window, vm, idA);

        Assert.Equal(VisualStatus.Queued, header.StatusGlyphIcon.Status); // satırla AYNI
        Assert.Equal("Queued", header.StatusNameText.Text); // yazı da satırla AYNI — State hâlâ Started'dır

        row.CycleWaiting = false; // sıra bu üyeye geçti — seçim DEĞİŞMEZ
        Assert.True(row.IsCompiling);

        Assert.Equal(VisualStatus.Building, header.StatusGlyphIcon.Status);
        Assert.Equal("Building", header.StatusNameText.Text);
        var spinner = Assert.Single(DsResources.Descendants(header.StatusGlyphIcon).OfType<BuildingSpinner>());
        Assert.Equal(Visibility.Visible, spinner.Visibility);
        GC.KeepAlive(window);
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
