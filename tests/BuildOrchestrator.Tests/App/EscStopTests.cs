using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Esc zincirinin koşu katmanı, pencerenin GERÇEK Esc bağlamasından sürülür
/// (<see cref="MainWindowInputTests"/> deseni): hiçbir katman açık değilken Esc çalışan Build/Rebuild/Clean'i durdurur
/// (graceful); durdurma zaten sürerken tekrar Esc hard stop gönderir ("Stop now"), üçüncü Esc hiçbir şey göndermez;
/// durdurulamayan bir iş (Sync, Deep Clean, Optimize, checkout, pull) sürerken konsola tek satır düşer — aynı işte
/// yalnız ilk basışta. Görünmeyen sessiz Sync'te Esc bir şey söylemez. [kullanıcı kararı 2026-10-20] Esc'in koşuda yapacak işi
/// kalmayınca (hard gitti, satır yazıldı, görünen iş yok) pencereyi × gibi kapatır — ×'in iki dalı (tepsi / onay kartı)
/// <see cref="CloseToTrayTests"/>'te.
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
    /// kimliğiyle) ve konsola tek satır düşer — pencere yerinde kalır; üçüncü Esc hiçbir şey göndermez, satır EKLEMEZ ve pencereyi × gibi
    /// kapatır (Close to tray varsayılanı açık: tepsiye). Drain arkada sürer.
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-20]</b> ESKİ İDDİA
    /// (<c>A_second_escape_while_stopping_sends_a_hard_stop_and_a_third_sends_nothing</c>): üçüncü Esc hiçbir şey yapmazdı — Stop komutunun
    /// kapısında yutulurdu. GEREKÇE: kullanıcı durdurma sürerken ve bittikten sonra Esc'e bastı, hiçbir şey olmadı; Esc'in yapacak işi
    /// kalmayınca × gibi kapatmasını istedi. İkinci Esc'in hard stop'u kullanıcının seçimiyle (A) korundu.</para></summary>
    [StaFact]
    public void A_second_escape_while_stopping_sends_a_hard_stop_and_a_third_closes_the_window()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);
        MainWindowHost.PressEscape(window);
        Assert.Equal(AppPhase.Stopping, vm.Phase); // ön-koşul: ilk Esc graceful gitti

        MainWindowHost.PressEscape(window); // ikinci Esc: hard
        string afterHard = vm.GetRunDocumentText();
        Assert.Equal(Visibility.Collapsed, window.Visibility); // hard'ı gönderen Esc pencereyi kapatmadı (hiç gösterilmedi: Collapsed)
        MainWindowHost.PressEscape(window); // üçüncü Esc

        StopRunCommand[] expected = [new("r1", StopKind.Graceful), new("r1", StopKind.Hard)];
        Assert.Equal(expected, sent.OfType<StopRunCommand>().ToArray());
        Assert.Equal(1, MainWindowHost.Occurrences(afterHard, RunViewModel.StopNowRequestedLine));
        Assert.Equal(afterHard, vm.GetRunDocumentText()); // üçüncü Esc konsola hiçbir satır eklemedi — konsolun TAMAMI aynı
        Assert.Equal(Visibility.Hidden, window.Visibility); // × gibi: tepsiye
        Assert.True(vm.IsRunning); // drain arkada sürüyor — Esc onu durdurmadı ya da bitirmedi
        GC.KeepAlive(window);
    }

    /// <summary>[Stop now · M6] Branch değişiminin kesmesi de durdurmayı Stopping'e çeker (<c>RequestInterruptAsync</c> →
    /// <see cref="StopKind.Interrupt"/>): kullanıcı O drain sürerken İLK kez Esc'e bastığında bu hard stop'tur — "ikinci basış"
    /// anlatısı yalnız kullanıcının kendi Stop'u içindir — ve aşama baştan "Stop now"dur (tüm yüzler bunu okur).</summary>
    [StaFact]
    public async Task Escape_during_a_branch_interrupt_drain_sends_the_hard_stop_at_once()
    {
        using var temp = new TempDir();
        var (window, vm, sent) = MainWindowHost.NewWithSends(temp);
        MainWindowHost.StartBuild(vm);

        await vm.RequestInterruptAsync(); // branch değişti: kesme gitti, drain başladı
        Assert.Equal(AppPhase.Stopping, vm.Phase); // ön-koşul
        Assert.Equal(StopStage.StopNow, vm.StopStage);

        MainWindowHost.PressEscape(window);

        StopRunCommand[] expected = [new("r1", StopKind.Interrupt), new("r1", StopKind.Hard)];
        Assert.Equal(expected, sent.OfType<StopRunCommand>().ToArray());
        Assert.Equal(StopStage.Terminating, vm.StopStage);
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

        Assert.Equal(1, MainWindowHost.Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.Sync)));
        GC.KeepAlive(window);
    }

    /// <summary>[kullanıcı kararı 2026-10-20] Satır yazıldıktan sonra Esc'in anlatacak bir şeyi kalmaz: ilk Esc satırı yazar ve pencere
    /// yerinde kalır, ikincisi pencereyi × gibi kapatır (Close to tray varsayılanı açık: tepsiye) — Sync arkada sürer. ESKİ: ikinci Esc
    /// hiçbir şey yapmazdı.</summary>
    [StaFact]
    public async Task A_second_escape_during_a_sync_closes_the_window_after_the_line()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);
        Assert.True(vm.SyncBusy); // ön-koşul: motorun cevabı bekleniyor

        MainWindowHost.PressEscape(window);
        Assert.Equal(1, MainWindowHost.Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.Sync)));
        Assert.Equal(Visibility.Collapsed, window.Visibility); // satırı yazan Esc pencereyi kapatmadı (hiç gösterilmedi: Collapsed)

        MainWindowHost.PressEscape(window);
        Assert.Equal(Visibility.Hidden, window.Visibility); // × gibi: tepsiye
        Assert.True(vm.SyncBusy); // Sync arkada sürüyor
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

        Assert.Equal(1, MainWindowHost.Occurrences(vm.GetRunDocumentText(), RunViewModel.EscCannotStopLine(OperationLabel.DeepClean)));
        GC.KeepAlive(window);
    }

    /// <summary>Sessiz Sync kullanıcıya görünmez (pill yazılmaz, transkript akmaz) — Esc onun hakkında konuşmaz;
    /// konuşsaydı kullanıcının başlatmadığı bir işi anlatırdı. Ortada görünen bir iş yoktur, yani Esc pencereyi × gibi kapatır
    /// (Close to tray varsayılanı açık: tepsiye).
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-20]</b> ESKİ İDDİA (<c>Escape_during_a_silent_sync_says_nothing</c>): Esc hiçbir
    /// şey yapmazdı. GEREKÇE: kullanıcı "çalışan bir şey yokken Esc × gibi kapatsın" dedi; sessiz Sync'i görmez.</para></summary>
    [StaFact]
    public async Task Escape_during_a_silent_sync_says_nothing_and_closes_the_window()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithSends(temp);
        await MainWindowHost.StartSync(vm, SyncMode.Silent);
        Assert.True(vm.SyncBusy); // ön-koşul
        string console = vm.GetRunDocumentText();

        MainWindowHost.PressEscape(window);

        Assert.Equal(console, vm.GetRunDocumentText());
        Assert.Equal(Visibility.Hidden, window.Visibility);
        GC.KeepAlive(window);
    }
}
