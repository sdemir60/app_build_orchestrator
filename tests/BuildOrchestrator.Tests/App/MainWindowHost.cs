using System.IO;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2] <see cref="MainWindow"/> kuran testlerin TEK kurulum yeri.
///
/// <para>T1 öncesinde bu blok iki dosyada (<c>MainWindowRealizeTests</c>, <c>MainWindowInputTests</c>) AYRI AYRI
/// duruyordu ve T2 üçüncü/dördüncüsünü yazacaktı — tek yere toplandı (kopya YASAK, CLAUDE.md).</para>
///
/// <para><b>İki değişmez burada zorlanır:</b>
/// (a) motor ASLA doğmaz — var olmayan bir supervisor yolu verilir ve pencere hiç <c>Show()</c> edilmez
/// (<c>Loaded</c>/<c>OnSourceInitialized</c> tetiklenmez; bkz. <see cref="MainWindowRealizeTests"/> sınıf özeti);
/// (b) <b>kalıcı durum store'u ZORUNLU olarak temp'e yönlendirilir</b> — parametre opsiyonel DEĞİLDİR, çünkü
/// unutulduğu anda test kullanıcının GERÇEK <c>%LOCALAPPDATA%\BuildOrchestrator\ui-state.json</c> dosyasını
/// yeniden yazar (T1/C1'de ölçülen yan etki: persist zinciri <c>Show()</c>'a bağlı değildir, abonelik ctor'da
/// kurulur). Parmak-izi guard'ı <see cref="MainWindowInputTests"/>'tedir.</para>
/// </summary>
internal static class MainWindowHost
{
    /// <summary>Konsol pompası test boyunca hiç tick etmesin — batcher sonsuza dek bekler.</summary>
    public static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>Üretim kablajının TAMAMIYLA kurulu bir <see cref="MainWindow"/>'u + onun VM'i.</summary>
    public static (MainWindow window, RunViewModel vm) New(TempDir uiStateDir)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = new JsonUiStateStore(Path.Combine(uiStateDir.Path, "ui-state.json"));
        return (new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope(), store), vm);
    }

    /// <summary>
    /// [fix round 1 · A1] Pencerenin İÇERİĞİNİ realize eder — <b>ölçüldü:</b> <c>Window.Measure/Arrange</c>
    /// gerçek bir <c>PresentationSource</c> (HWND) olmadan içeriğe HİÇ İNMEZ; caption butonlarının şablonları
    /// bile genişlemez (<c>MinButton.ApplyTemplate()</c> sonradan hâlâ <c>true</c> döner). İçerik kökü doğrudan
    /// ölçülüp yerleştirildiğinde ise şablonlar genişler ve <c>OnRender</c> koşar — yani <c>Background</c> gibi
    /// RENDER-ONLY özellikler de gerçekten okunur ve yanlış tipli token orada patlar.
    /// </summary>
    public static FrameworkElement Realize(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ApplyTemplate();
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1400, 800));
        content.Arrange(new Rect(0, 0, 1400, 800));
        content.UpdateLayout();
        return content;
    }
}
