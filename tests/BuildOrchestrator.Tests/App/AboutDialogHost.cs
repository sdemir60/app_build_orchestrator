using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Realize edilmiş + açılmış bir <see cref="AboutDialog"/> kuran TEK yer (<see cref="SettingsDialogHost"/>
/// deseninin eşi — kopya YASAK, CLAUDE.md). Fixture repo SEÇİLMİŞ durumu kurar: diyalog üretimde de
/// kullanıcının bir kök seçtikten sonra ulaştığı bir yüzeydir.
///
/// <para><b>MSBuild çözümü ENJEKTE EDİLİR</b> — test hiçbir koşulda <c>vswhere</c> başlatmaz (D8).</para>
/// </summary>
internal static class AboutDialogHost
{
    /// <summary>Testlerin gördüğü sahte MSBuild satırı (üretimde <c>MsBuildResolver</c> üretir).</summary>
    public const string FakeMsBuild = @"C:\VS\MSBuild.exe (v17.9.8)";

    /// <param name="backgroundSibling">Verilirse diyalog, bu kontrolle AYNI kökün altında realize edilir —
    /// odak tuzağı testi "Tab arka plandaki bir kontrole kaçıyor mu" sorusunu ancak böyle sorabilir.</param>
    public static (AboutDialog dialog, RunViewModel run, IDisposable scope) OpenRealized(
        Action<RunViewModel>? configure = null,
        bool hotkeyRegistered = true,
        Func<Task<string>>? resolveMsBuild = null,
        FrameworkElement? backgroundSibling = null)
    {
        var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        configure?.Invoke(run);

        var host = DsResources.NewHost();
        var dialog = new AboutDialog();

        FrameworkElement content = dialog;
        if (backgroundSibling is not null)
        {
            var root = new Grid();
            root.Children.Add(backgroundSibling);
            root.Children.Add(dialog);
            content = root;
        }
        // Pencere diyaloğu SIĞDIRACAK kadar büyük olmalı: varsayılan 400×200'de 620px'lik modal kırpılır ve
        // ActualHeight içerik ne olursa olsun aynı doymuş değeri döner (bkz. DsResources.Realize gerekçesi).
        var window = DsResources.Realize(host, content, width: 800, height: 700);

        dialog.Open(run, hotkeyRegistered, resolveMsBuild ?? (() => Task.FromResult(FakeMsBuild)));
        content.UpdateLayout(); // Visibility Collapsed→Visible sonrası GERÇEK arrange

        return (dialog, run, new Scope(engine, window));
    }

    private sealed class Scope(EngineHost engine, Window window) : IDisposable
    {
        public void Dispose()
        {
            // SettingsDialogHost.Scope ile AYNI gerekçe: motor hiç başlatılmadığı için (var olmayan supervisor
            // yolu) ShutdownGracefullyAsync yazacak bir writer bulamaz ve senkron tamamlanır.
            engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.KeepAlive(window);
        }
    }
}
