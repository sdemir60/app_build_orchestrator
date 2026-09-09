using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T3 fix-1 · B6] Realize edilmiş + açılmış bir <see cref="SettingsDialog"/> kuran TEK yer
/// (<see cref="MainWindowHost"/> deseninin eşi).
///
/// <para><b>Neden:</b> T3a (<c>SettingsDialogTests</c>) ve T3b (<c>SettingsDialogFocusTests</c>) aynı beş satırlık
/// kurulumu iki ayrı dosyaya kopyalamıştı ve kopya <b>daha kopyalanırken ayrışmıştı</b>: biri
/// <see cref="RunViewModel.RootPath"/>'i hiç set etmiyordu (diyalog "no repository" durumunda realize oluyordu —
/// <c>SettingsDialog.xaml.cs</c> <c>RepoPathText</c>), diğeri <c>D:\repo</c> veriyordu. Hangisinin doğru olduğu
/// hiçbir yerde yazmıyordu. Bu, T2'de yanan senaryonun birebir tekrarıdır (bkz. <see cref="MainWindowHost"/>
/// sınıf özeti) — tek yer + TEK karar.</para>
///
/// <para><b>Karar (ölçüldü):</b> fixture <b>repo SEÇİLMİŞ</b> durumu kurar. Gerekçe: (a) diyalog üretimde yalnız
/// gear butonundan açılır ve kullanıcı oraya bir kök seçtikten sonra ulaşır, (b) <see cref="MainWindowHost"/> de
/// kabuğu repo seçilmiş kurar (aynı sözleşme), (c) ölçüldü ki LAYERS kopya metinleri/boş-durum kutusu ve footer
/// etiketleri <c>RootPath</c>'ten ETKİLENMEZ (boş-durum <see cref="RunViewModel.LayerPatterns"/>'e bakar), yani
/// T3a'nın dört assertion'ı aynen geçer — ayrışan kopya yalnız gereksiz bir ikinci durumu realize ediyordu.</para>
/// </summary>
internal static class SettingsDialogHost
{
    /// <summary>Bellek-içi UiStateStore — persistence round-trip'ini WPF/dosya olmadan gözlemler.
    /// [fix-1 · C13] İki test dosyasında ikiz duruyordu.</summary>
    internal sealed class FakeStore : IUiStateStore
    {
        public UiState State { get; private set; } = new();
        public UiState Load() => State;
        public void Save(UiState state) => State = state;
    }

    /// <summary>Realize edilmiş + <see cref="SettingsDialog.Open"/> edilmiş diyalog. Dönen <see cref="IDisposable"/>
    /// hem ekran dışı pencereyi canlı tutar hem de <see cref="EngineHost"/>'u kapatır — o ctor inert DEĞİLDİR:
    /// <c>JobObject.CreateKillOnClose()</c> ile bir Win32 handle açar (lens2/lens3 · C9).</summary>
    /// <param name="windowWidth">Host penceresinin genişliği.</param>
    /// <param name="windowHeight">Host penceresinin yüksekliği — AboutDialogHost/NotesDialogHost'un AYNI kararı
    /// (800×700): varsayılan 400×200'de gövde <c>MinHeight</c>'ı (task-D6, 300px) pencereden BÜYÜK kalır ve
    /// dialog dikeyde kırpılırdı (DsResources.Realize gerekçesiyle AYNI risk). Pencere yüksekliğine bağlı gövde
    /// sınırını (<see cref="SettingsBodyHeight"/>) AYRICA sınayan çağıranlar bu iki parametreyi ELLE verir.</param>
    public static (SettingsDialog dialog, RunViewModel run, FakeStore store, IDisposable scope) OpenRealized(
        Action<RunViewModel>? configure = null, Func<string?>? pickFolder = null,
        double windowWidth = 800, double windowHeight = 700)
    {
        var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        configure?.Invoke(run);

        var host = DsResources.NewHost();
        var dialog = new SettingsDialog();
        var window = DsResources.Realize(host, dialog, windowWidth, windowHeight);

        var store = new FakeStore();
        dialog.Open(run, store, pickFolder ?? (() => null));
        dialog.UpdateLayout(); // Visibility Collapsed→Visible sonrası GERÇEK arrange

        return (dialog, run, store, new Scope(engine, window));
    }

    private sealed class Scope(EngineHost engine, System.Windows.Window window) : IDisposable
    {
        public void Dispose()
        {
            // EngineHost yalnız IAsyncDisposable'dır ve [StaFact] senkrondur. Motor HİÇ başlatılmadığı için
            // (var olmayan supervisor yolu) ShutdownGracefullyAsync yazacak bir writer bulamaz ve senkron
            // tamamlanır — sync-over-async burada bloklamaz.
            engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.KeepAlive(window);
        }
    }
}
