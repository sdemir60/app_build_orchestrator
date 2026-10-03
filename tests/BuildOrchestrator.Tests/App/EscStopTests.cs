using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Esc zincirinin koşu katmanı, pencerenin GERÇEK Esc bağlamasından sürülür
/// (<see cref="MainWindowInputTests"/> deseni): hiçbir katman açık değilken Esc çalışan Build/Rebuild/Clean'i durdurur
/// (graceful); durdurma zaten sürerken tekrar Esc hard stop gönderir ("Stop now"), üçüncü Esc hiçbir şey göndermez;
/// durdurulamayan bir iş (Sync, Deep Clean, Optimize, checkout, pull) sürerken konsola tek satır düşer — aynı işte
/// yalnız ilk basışta. Görünmeyen sessiz Sync'te Esc bir şey söylemez.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-03 · perf Faz B · B3]</b> ESKİ İDDİA (kullanıcı kararı 2026-09-29):
/// durdurma sürerken tekrar Esc ikinci bir stop GÖNDERMEZ; şerit satırı kısa bir vurguyla (opaklık 1 → 0,4 → 1) "duyuldu,
/// zaten duruyor" der ve konsola satır yazılmaz. GEREKÇE: drain, uçuştaki en yavaş projenin kalan süresi kadar sürebilir ve
/// ikinci Esc kullanıcının beklemek istemediğini söyler — ona "duyuldu" demek yerine isteğini yerine getirmek gerekir.
/// "Duyuldu" vurgusunun yerini iki kalıcı iz aldı: konsol satırı (<see cref="RunViewModel.StopNowRequestedLine"/>) ve
/// düğmenin "Terminating…" hâli. Vurguyu pinleyen iki şerit testi (motion açıkken saat kurulur / reduced-motion'da kurulmaz)
/// bu yüzden SİLİNDİ: pinledikleri davranış (<c>StopRequestAcknowledged</c> olayı, şerit pulse'ı) artık yok.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EscStopTests
{
    private static int Occurrences(string text, string line) =>
        text.Split('\n').Count(l => l.Contains(line, StringComparison.Ordinal));

    // ---------------------------------------------------------------- durdur

    [StaFact]
    public void Escape_with_nothing_open_stops_a_running_build()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);

        MainWindowHost.PressEscape(window);

        Assert.Equal(AppPhase.Stopping, vm.Phase);
        Assert.Single(sent.OfType<StopRunCommand>(), s => s.Kind == StopKind.Graceful);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_clears_a_selection_before_it_stops_the_build()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        vm.SelectProject(@"C:\p\a.csproj");

        MainWindowHost.PressEscape(window);
        Assert.Null(vm.SelectedProjectId);
        Assert.Empty(sent.OfType<StopRunCommand>()); // ilk Esc yalnız seçimi bıraktı

        MainWindowHost.PressEscape(window);
        Assert.Single(sent.OfType<StopRunCommand>());
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now] Durdurma sürerken ikinci Esc hard stop gönderir (<see cref="StopKind.Hard"/>, koşan run'ın
    /// kimliğiyle) ve konsola tek satır düşer; üçüncü Esc hiçbir şey göndermez ve satır EKLEMEZ — hard zaten gitti.</summary>
    [StaFact]
    public void A_second_escape_while_stopping_sends_a_hard_stop_and_a_third_sends_nothing()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        MainWindowHost.PressEscape(window);
        Assert.Equal(AppPhase.Stopping, vm.Phase); // ön-koşul: ilk Esc graceful gitti

        MainWindowHost.PressEscape(window);
        MainWindowHost.PressEscape(window);

        StopRunCommand[] expected = [new("r1", StopKind.Graceful), new("r1", StopKind.Hard)];
        Assert.Equal(expected, sent.OfType<StopRunCommand>().ToArray());
        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), RunViewModel.StopNowRequestedLine));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- durdurulamaz işler

    [StaFact]
    public async Task Escape_during_a_sync_says_once_that_it_cannot_be_stopped()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);
        Assert.True(vm.SyncBusy); // ön-koşul: motorun cevabı bekleniyor

        MainWindowHost.PressEscape(window);
        MainWindowHost.PressEscape(window);

        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.Sync)));
        GC.KeepAlive(window);
    }

    /// <summary>"Aynı işte yalnız ilk basışta" kuralının öbür yüzü: iş bitip YENİ bir iş başlayınca satır yeniden
    /// düşer (bu kez o işin adıyla).</summary>
    [StaFact]
    public async Task A_new_operation_gets_its_own_cannot_be_stopped_line()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);
        MainWindowHost.PressEscape(window);
        MainWindowHost.ReplySync(vm, ("A", null)); // Sync bitti — meşguliyet kalktı

        await vm.CleanCommand.ExecuteAsync(null); // derin Clean: motorun cevabı bekleniyor
        Assert.True(vm.CleanBusy); // ön-koşul
        MainWindowHost.PressEscape(window);

        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.DeepClean)));
        GC.KeepAlive(window);
    }

    /// <summary>Sessiz Sync kullanıcıya görünmez (pill yazılmaz, transkript akmaz) — Esc onun hakkında konuşmaz;
    /// konuşsaydı kullanıcının başlatmadığı bir işi anlatırdı.</summary>
    [StaFact]
    public async Task Escape_during_a_silent_sync_says_nothing()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Silent);
        Assert.True(vm.SyncBusy); // ön-koşul
        string console = vm.GetRunDocumentText();

        MainWindowHost.PressEscape(window);

        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }
}
