namespace BuildOrchestrator.App.Shell;

/// <summary>[A13/T6 · t1+t2] Komut satırının SEÇTİĞİ açılış yolu. Üç yol vardır ve üçü BİRBİRİNİ DIŞLAR —
/// bu yüzden karar tek bir değerdir (iki ayrı bayrak değil): ikisi aynı anda "doğru" olamaz.</summary>
internal enum StartupRoute
{
    /// <summary>Argümansız (ya da tanınmayan argümanlı) normal açılış ve [P4] Start minimized to tray KAPALIYKEN
    /// Windows ile açılış (<c>--autostart</c>): DI + single-instance kurulur ve pencere <c>Show()</c> edilir.</summary>
    ShowWindow,

    /// <summary>[E2/T16 · P4] Windows ile açılış (<c>--autostart</c>) VE Start minimized to tray açık: pencere
    /// GÖSTERİLMEDEN tepside (gizli) başlar (<see cref="MainWindow.StartInTray"/>). Açılışın Sync'i normal açılıştaki
    /// gibi motor hazır olunca koşar (<c>RunViewModel.OnEngineReady</c>).</summary>
    StartInTray,

    /// <summary>[T65/K9] <c>--font-ab</c>: font A/B karar penceresi. DI/EngineHost kurulmaz, Supervisor spawn
    /// edilmez ve single-instance kapısının DIŞINDADIR (çalışan bir ana pencere varken de açılabilmelidir).</summary>
    FontAbSpike,
}

/// <summary>
/// [A13/T6 · t1+t2] <c>App.OnStartup</c>'ın argüman AYRIŞTIRMASININ tek yeri. Saftır (WPF/DI/process İÇERMEZ)
/// ki <see cref="App.OnStartup"/> dışında test edilebilsin — <see cref="SecondInstanceGate"/> ile AYNI desen
/// ve AYNI gerekçe: <see cref="Application"/> headless kurulamaz, bu yüzden KARAR kabuktan ayrılır.
///
/// <para><b>Önceliği bu tip sahiplenir:</b> <c>--font-ab</c> dev kabuğu diğer her şeyi EZER — üretimde o dal
/// DI'dan ve single-instance kapısından ÖNCE dönüyor (<c>App.xaml.cs</c>), dolayısıyla <c>--autostart</c> ile
/// birlikte verilse bile tepsi yolu hiç çalışmaz. Karar buraya taşınmasaydı bu sıra yalnız
/// <c>OnStartup</c>'ın satır sırasında yaşardı ve testsiz kalırdı.</para>
///
/// <para><b>[P4] Pencere mi tepsi mi:</b> registry'deki Run komutu yalnız "Windows ile açıldım" işaretini
/// (<see cref="App.AutostartArg"/>) taşır; kararı ui-state.json'daki Start minimized to tray verir (tek doğruluk
/// kaynağı — ayar değişince Run komutu yeniden yazılmaz). Elle açılış (kısayol, exe) o ayardan bağımsız olarak
/// her zaman pencereyi gösterir.</para>
///
/// <para><b>Tanınmayan argüman YUTULUR</b> (ör. T35'te kaldırılan <c>--it4a-lab</c> lab kabuğu, Explorer'dan
/// gelen dosya yolu, hata ayıklayıcı bayrağı): uygulama normal açılır, çökmez ve davranışı değişmez.</para>
/// </summary>
internal static class StartupArgs
{
    /// <summary>[T65/K9] Font A/B karar penceresini açan dev argümanı. (<see cref="App.AutostartArg"/>'ın
    /// simetriği; o, registry autostart komutuna YAZILDIĞI için <see cref="App"/>'te yaşamaya devam eder.)</summary>
    public const string FontAbArg = "--font-ab";

    /// <param name="args">Komut satırı.</param>
    /// <param name="startMinimizedToTray">[P4] Settings → General → Start minimized to tray
    /// (<see cref="ShellSwitches.StartMinimizedToTray"/>) — yalnız Windows ile açılışta okunur.</param>
    public static StartupRoute Decide(IReadOnlyList<string> args, bool startMinimizedToTray)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains(FontAbArg)) return StartupRoute.FontAbSpike;      // dev kabuğu her şeyi EZER
        if (args.Contains(App.AutostartArg) && startMinimizedToTray) return StartupRoute.StartInTray;
        return StartupRoute.ShowWindow; // elle açılış, ayar kapalıyken Windows ile açılış, tanınmayan argüman (yutulur)
    }
}
