using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.13.2 §2.5 · §9] <b>Konsol ve event stream her işlemde EKRANDA temizlenir.</b> VM tamponunun
/// silindiğini <c>RunViewModelStateTests</c> pinler; bu dosya kabuk kablajını pinler: VM'in temizliği
/// (<c>RunViewModel.ConsoleCleared</c>) AvalonEdit belgesine (boş anlatıya yeniden bağlanan belge —
/// <c>ConsoleView.ReplaceRunDocument</c>) ve <c>StreamEvents</c>'in boşalması event stream satırlarına ulaşıyor mu.
///
/// <para><b>Ölçülen kusur (kullanıcı testi):</b> Build ve Sync'te konsol "hiç temizlenmiyordu". VM tamponu
/// siliniyor ama ekrandaki belge yalnız mod geçişinde (<c>ShowRunConsole</c>) yeniden kuruluyordu — yeni işlemin
/// satırları bir öncekinin ALTINA ekleniyordu. Testler o yüzden VM'de değil KABUKTA sorar: pencere üretim
/// kablajıyla kurulur (<see cref="MainWindowHost"/>), önceki işlemin izi belgeye ve stream'e bırakılır, işlem
/// tetiklenir ve EKRAN okunur.</para>
///
/// <para>Konsol pompası test boyunca tick etmez (<see cref="MainWindowHost.NeverTickingBatcher"/>); önceki
/// işlemin satırı bu yüzden belgeye doğrudan kurulur (<c>ShowRunDocument</c>) — sorulan soru "belge boşaldı mı",
/// "pompa nasıl akıtır" değil.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class OperationConsoleClearTests
{
    private const string PreviousLine = "Build succeeded";

    private static string ConsoleText(MainWindow window) => window.Shell.ConsoleViewControl.Editor.Document.Text;

    /// <summary>Önceki bir koşunun izi: stream'de satırlar (gerçek olay yolu), belgede bir satır.</summary>
    private static void LeavePreviousOperationOnScreen(MainWindow window, RunViewModel vm)
    {
        vm.OnEvent(new RunStartedEvent("r0", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new ProjectSucceededEvent("r0", MainWindowHost.IdOf("Alpha"), 100));
        vm.OnEvent(new RunCompletedEvent("r0", RunOutcome.Completed, 1, 0, 0, 0, 100));
        window.Shell.ConsoleViewControl.ShowRunDocument(NarrativeSnapshots.FromText(PreviousLine));
        window.Shell.EventStreamControl.UpdateLayout();

        Assert.Contains(PreviousLine, ConsoleText(window));
        Assert.True(window.Shell.EventStreamControl.Rows.Count > 0, "ön-koşul: stream'de önceki işlemden satır yok (vakum)");
    }

    [StaFact]
    public void Build_clears_the_console_document_and_the_stream_rows_on_screen()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        LeavePreviousOperationOnScreen(window, vm);

        _ = vm.BuildCommand.ExecuteAsync(null); // temizlik ilk await'ten ÖNCE, senkron — koreografi kapısı sonra

        Assert.DoesNotContain(PreviousLine, ConsoleText(window));
        Assert.Empty(window.Shell.EventStreamControl.Rows);
        GC.KeepAlive(window);
    }

    [StaFact]
    public async Task Sync_clears_the_console_document_and_the_stream_rows_on_screen()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        LeavePreviousOperationOnScreen(window, vm);

        await vm.SyncCommand.ExecuteAsync(null); // motor doğmadı → gönderim düşer; temizlik ondan ÖNCE

        Assert.DoesNotContain(PreviousLine, ConsoleText(window));
        Assert.Empty(window.Shell.EventStreamControl.Rows);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · dalga 1] <b>Temizlikle aynı anda gelen satır belgeye BİR kez iner.</b> VM anlatıyı kilit altında siler
    /// ve olayı kilit DIŞINDA yayınlar; arada IPC thread'inden bir satır gelebilir — store'a yazılır ve temizliğin sentinel'inden
    /// SONRA pompaya gider (yeni nesil), yani batch olarak belgeye inecektir. Kabuk bu yüzden belgeyi anlatıya BOŞ bağlar
    /// (satır sayısı 0). Store'u ikinci kez okuyup o anki sayıyla bağlasaydı o satır hem tohumda hem batch'te olurdu: çift satır.
    /// Test o aralığı kabuğun işleyicisinden ÖNCE koşan bir aboneyle kurar (olay aboneleri abonelik sırasıyla çağrılır; VM'e
    /// pencereden önce abone olmanın tek yolu <c>beforeVm</c>'dir).
    /// </summary>
    [StaFact]
    public async Task A_line_that_arrives_while_the_console_is_cleared_lands_once()
    {
        const string lateLine = "late line from the engine";
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp, beforeVm: model =>
            model.ConsoleCleared += (_, _) => MainWindowHost.LogLine(model, "Alpha", 1, lateLine));
        MainWindowHost.Realize(window);

        await vm.SyncCommand.ExecuteAsync(null); // temizlik: anlatı silinir, olay yayınlanır — arada satır gelir

        window.AppendConsoleBatch(lateLine + "\n", window.ConsoleReseedGen); // pompanın o satır için batch'i (pompa tick etmez)
        Assert.Equal(1, MainWindowHost.Occurrences(ConsoleText(window), lateLine)); // KIRMIZI ikinci okumayla: 2
        GC.KeepAlive(window);
    }
}
