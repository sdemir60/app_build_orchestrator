using System.IO;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

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
    /// <param name="beforeVm">[A13/T6 · t1] VM kurulduktan SONRA, pencere ctor'u onu SEED etmeden ÖNCE koşar.
    /// Pencerenin ctor'unda olan biteni (kalıcı durumdan repo/branch/perf seed'i — <c>MainWindow.xaml.cs:126</c>)
    /// gözlemek isteyen tek yol budur: <c>New</c> döndüğünde seed ÇOKTAN akmıştır, sonradan takılan bir prob onu
    /// göremez. Verilmezse davranış birebir eskisi gibidir.</param>
    public static (MainWindow window, RunViewModel vm) New(TempDir uiStateDir, Action<RunViewModel>? beforeVm = null)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        beforeVm?.Invoke(vm);
        var store = new JsonUiStateStore(Path.Combine(uiStateDir.Path, "ui-state.json"));
        return (new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope(), store), vm);
    }

    /// <summary>
    /// [T2 fix-1 · I-F] Realize edilmiş bir kabuk + topolojisi akmış bir VM — <b>üretim sırasıyla</b> (kabuk
    /// ÖNCE realize, veri SONRA) ve <c>Idle</c> fazında.
    ///
    /// <para>Üç test sınıfı bu fixture'ı ayrı ayrı kopyalamıştı ve ŞİMDİDEN ayrışmıştı: biri <c>RootPath</c>'i
    /// hiç set etmiyordu, yani o kabuk <c>HasWorkspace == false</c> ile ve "Pick a repository" overlay'i
    /// AÇIKKEN koşuyordu. Kopyalanmış fixture'ın sessiz ayrışması, kuralın (kopya YASAK) önlemeye çalıştığı
    /// şeyin ta kendisidir → tek yer.</para>
    /// </summary>
    /// <param name="nodes">Proje adı + (varsa) katman adı, build-order sırasında.</param>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjects(
        TempDir uiStateDir, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var (window, vm) = New(uiStateDir);
        Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var projectNodes = nodes.Select((n, i) => Node(n.Name, i, n.Layer)).ToList();
        vm.OnEvent(new WorkspaceTopologyEvent(projectNodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, projectNodes.Count, 0)); // → Idle
        return (window, vm, window.Shell.ProjectsList);
    }

    /// <summary>Test topolojisi düğümü — <c>Id</c> = kanonik csproj yolu (üretimdeki gibi tam yol).</summary>
    public static ProjectNode Node(string name, int order, string? layer = null) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order,
            layer is null ? null : order, layer, false, null);

    /// <summary>Bir test projesinin <c>Id</c>'si (<see cref="Node"/> ile BİREBİR aynı kural).</summary>
    public static string IdOf(string name) => $@"C:\p\{name}.csproj";

    /// <summary>
    /// [fix round 1 · A1] Pencerenin İÇERİĞİNİ realize eder — <b>ölçüldü:</b> <c>Window.Measure/Arrange</c>
    /// gerçek bir <c>PresentationSource</c> (HWND) olmadan içeriğe HİÇ İNMEZ; caption butonlarının şablonları
    /// bile genişlemez (<c>MinButton.ApplyTemplate()</c> sonradan hâlâ <c>true</c> döner). İçerik kökü doğrudan
    /// ölçülüp yerleştirildiğinde ise şablonlar genişler ve <c>OnRender</c> koşar — yani <c>Background</c> gibi
    /// RENDER-ONLY özellikler de gerçekten okunur ve yanlış tipli token orada patlar.
    ///
    /// <para>[A13/T3 fix-1 · B4] Boyut ARTIK parametre: <c>TitleBarContextTests</c> "en dar desteklenen pencere"
    /// (<c>Size.WindowMinWidth</c>=1240) senaryosunu ölçmek için bu bloğu inline yeniden yazmıştı — realize
    /// etmenin TEK yeri kuralı delinmişti. Varsayılan üretimin açılış boyutudur (MainWindow.xaml 1400×800).</para>
    /// </summary>
    public static FrameworkElement Realize(MainWindow window, double width = 1400, double height = 800)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ApplyTemplate();
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        return content;
    }
}
