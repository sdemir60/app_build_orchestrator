using BuildOrchestrator.App.Services;
using Velopack;

namespace BuildOrchestrator.App;

/// <summary>
/// [yayın hattı] Uygulamanın giriş noktası. <c>VelopackApp.Build().Run()</c> Main'in İLK ifadesidir: Velopack
/// kurulum, güncelleme ve kaldırma kancalarını ana exe'yi <c>--veloapp-install|obsolete|updated|uninstall &lt;sürüm&gt;</c>
/// ile çalıştırarak işletir; <c>Run()</c> bu argümanları görürse kancayı koşturur ve process'i orada bitirir — WPF,
/// tek-örnek mutex'i, tepsi ve DI hiç kurulmaz. Normal açılışta <c>Run()</c> hiçbir şey yapmaz ve WPF her zamanki gibi
/// başlar (<see cref="App.OnStartup"/>). İndirilmiş ama kurulmamış bir güncelleme varsa Velopack onu açılışta kurar
/// (<c>SetAutoApplyOnStartup</c> varsayılanı açık — tasarım §2.12: "bir sonraki açılışta kendiliğinden kurulur").
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // Kaldırmada Windows başlangıç kaydı silinir — aksi hâlde HKCU\...\Run'da var olmayan bir exe'ye işaret kalırdı.
            .OnBeforeUninstallFastCallback(_ => AutostartService.RemoveForUninstall(new RegistryAutostartRegistry()))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
