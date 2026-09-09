using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.13.2 §2.5 · §9] <b>Konsol ve event stream her işlemde EKRANDA temizlenir.</b> VM tamponunun
/// silindiğini <c>RunViewModelStateTests</c> pinler; bu dosya kabuk kablajını pinler: VM'in temizliği
/// (<c>RunViewModel.ConsoleCleared</c>) AvalonEdit belgesine (<c>ConsoleView.ClearRunDocument</c>) ve
/// <c>StreamEvents</c>'in boşalması event stream satırlarına ulaşıyor mu.
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
        vm.OnEvent(new RunStartedEvent("r0", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectSucceededEvent("r0", MainWindowHost.IdOf("Alpha"), 100));
        vm.OnEvent(new RunCompletedEvent("r0", RunOutcome.Completed, 1, 0, 0, 0, 100));
        window.Shell.ConsoleViewControl.ShowRunDocument(PreviousLine);
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
}
