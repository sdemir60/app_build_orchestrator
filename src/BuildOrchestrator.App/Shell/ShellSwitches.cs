using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [P3 · P4] Bir General→KABUK anahtarının satırı: <see cref="UiState"/> ve <see cref="SettingsFile"/> alanına nasıl
/// okunup yazılacağı, ve konsol notunun iki yarısı — tek yerde (kopya YASAK, CLAUDE.md).
/// </summary>
/// <param name="Setting">Kataloğun hangi satırı — <see cref="Note"/>'un etiketi <see cref="GeneralSettingsCatalog.Definition"/>
/// üzerinden buradan gelir.</param>
/// <param name="Read">Kayıtlı değeri <see cref="UiState"/>'ten okur — anahtar hiç kaydedilmediyse <c>null</c>.</param>
/// <param name="Write">Değeri <see cref="UiState"/>'e yazar — Save HER ZAMAN yazar (değişip değişmediğine bakmaz).</param>
/// <param name="ReadFile">Kayıtlı değeri dışa aktarılan <see cref="SettingsFile"/>'tan okur — anahtar dosyada yoksa
/// <c>null</c> (taslaktaki değer <see cref="SettingsDraftViewModel.LoadFrom"/>'da KORUNUR).</param>
/// <param name="WriteFile">Değeri <see cref="SettingsFile"/>'a yazar — Export HER ZAMAN yazar.</param>
/// <param name="OnClause">Konsol notunun AÇIK yarısı: <c>"{Label} on — {OnClause}"</c>.</param>
/// <param name="OffClause">Konsol notunun KAPALI yarısı: <c>"{Label} off — {OffClause}"</c>.</param>
internal sealed record ShellSwitch(
    GeneralSetting Setting,
    Func<UiState, bool?> Read, Action<UiState, bool> Write,
    Func<SettingsFile, bool?> ReadFile, Action<SettingsFile, bool> WriteFile,
    string OnClause, string OffClause)
{
    /// <summary>Konsol notu — etiket KATALOGDAN gelir (kopya YASAK): anahtarın adı burada literal olarak
    /// yazılmaz.</summary>
    public string Note(bool on) =>
        $"{GeneralSettingsCatalog.Definition(Setting).Label} {(on ? "on" : "off")} — {(on ? OnClause : OffClause)}";
}

/// <summary>
/// [P3 · P4] Settings → General'ın KABUK anahtarlarının TEK tablosu — pencere/tepsi/başlangıç davranışını süren
/// anahtarlar: <see cref="GeneralSetting.StartWithWindows"/>, <see cref="GeneralSetting.StartMinimizedToTray"/>,
/// <see cref="GeneralSetting.CloseToTray"/> ve <see cref="GeneralSetting.ShowNotifications"/>.
/// <see cref="SettingsDraftViewModel"/> bu tabloyu <see cref="All"/> üzerinden gezerek
/// <see cref="SettingsDraftViewModel.ToFile"/>/<see cref="SettingsDraftViewModel.LoadFrom"/>/
/// <see cref="SettingsDraftViewModel.CommitAsync"/>'i sürer — yeni bir kabuk anahtarı = buraya bir satır (artı
/// okuduğu/yazdığı <see cref="UiState"/> ve <see cref="SettingsFile"/> özellikleri).
///
/// <para>Pull before build ve Stash and switch branches bu tabloda DEĞİLDİR: onlar motora giden iş akışı
/// tercihleridir ve <see cref="RunViewModel"/>'de yaşar.</para>
///
/// <para><b>Tablo yalnız DEĞERİ taşır</b>, davranışı kendisi BAĞLAMAZ — okuyucular değeri her soruda kalıcı durumdan
/// TAZE okur (<see cref="IsOn"/>), arada kopya yok: <see cref="StartWithWindows"/>'u açılışın Windows kaydı hizalaması,
/// <see cref="StartMinimizedToTray"/>'ı açılış yolu kararı, <see cref="CloseToTray"/>'ı pencere kapanışı
/// (<c>MainWindow.OnClosing</c> → <see cref="WindowCloseRule"/>), <see cref="ShowNotifications"/>'ı dört tray-balloon
/// yolu (<c>FirstCloseBalloonGate</c>, <c>TrayBuildIndicatorController</c>, <c>SecondInstanceGate</c>,
/// <c>MainWindow.OnGlobalHotkey</c>) okur.</para>
/// </summary>
internal static class ShellSwitches
{
    /// <summary>Katalog sırası: STARTUP grubundaki Start with Windows, Start minimized to tray ve Close to tray;
    /// NOTIFICATIONS grubundaki Show notifications.</summary>
    public static IReadOnlyList<ShellSwitch> All { get; } =
    [
        new ShellSwitch(GeneralSetting.StartWithWindows,
            state => state.Autostart, (state, on) => state.Autostart = on,
            file => file.StartWithWindows, (file, on) => file.StartWithWindows = on,
            OnClause: "the app starts when you sign in to Windows",
            OffClause: "signing in to Windows no longer starts the app"),
        new ShellSwitch(GeneralSetting.StartMinimizedToTray,
            state => state.StartMinimizedToTray, (state, on) => state.StartMinimizedToTray = on,
            file => file.StartMinimizedToTray, (file, on) => file.StartMinimizedToTray = on,
            OnClause: "signing in to Windows starts the app in the tray, without a window",
            OffClause: "signing in to Windows opens the window"),
        new ShellSwitch(GeneralSetting.CloseToTray,
            state => state.CloseToTray, (state, on) => state.CloseToTray = on,
            file => file.CloseToTray, (file, on) => file.CloseToTray = on,
            OnClause: "closing the window keeps the app running in the tray",
            OffClause: "closing the window quits the app"),
        new ShellSwitch(GeneralSetting.ShowNotifications,
            state => state.ShowNotifications, (state, on) => state.ShowNotifications = on,
            file => file.ShowNotifications, (file, on) => file.ShowNotifications = on,
            OnClause: "tray notifications are shown",
            OffClause: "no tray notifications are shown"),
    ];

    /// <summary>Anahtarın GEÇERLİ değeri: kayıtlı ?? katalog varsayılanı. Davranış okuyucularının TEK kapısı
    /// (aşağıdaki dört kısaltma bunun birer kısaltmasıdır).</summary>
    public static bool IsOn(UiState state, GeneralSetting setting) =>
        Find(setting).Read(state) ?? GeneralSettingsCatalog.Definition(setting).Default;

    /// <summary>[P4] Uygulamanın tercihi: Windows oturumu açılınca başlasın mı. Açılış bunu Windows kaydıyla hizalar
    /// (<see cref="Services.AutostartService.Apply"/>).</summary>
    public static bool StartWithWindows(UiState state) => IsOn(state, GeneralSetting.StartWithWindows);

    /// <summary>[P4] Windows ile açılışta pencere gösterilmeden tepside mi başlansın — açılış yolunun kararı
    /// (<see cref="StartupArgs.Decide"/>) bunu okur.</summary>
    public static bool StartMinimizedToTray(UiState state) => IsOn(state, GeneralSetting.StartMinimizedToTray);

    /// <summary>[P3] Pencere kapanış yolunun kapısı: <c>MainWindow.OnClosing</c> her × / Alt+F4 / sistem menüsü
    /// Kapat'ta TAZE okur ve <see cref="WindowCloseRule"/>'a verir — açık ⇒ pencere tepsiye gizlenir, kapalı ⇒ güvenli
    /// tam çıkış.</summary>
    public static bool CloseToTray(UiState state) => IsOn(state, GeneralSetting.CloseToTray);

    /// <summary>[P3] Dört tray-balloon yolunun (ilk-× bilgilendirmesi, koşu bitişi, ikinci-instance uyarısı, yok sayılan
    /// Build kısayolunun notu) TEK kapısı; dördü de bunu balonun TAM gösterileceği anda TAZE okur.</summary>
    public static bool ShowNotifications(UiState state) => IsOn(state, GeneralSetting.ShowNotifications);

    /// <summary>Save: <see cref="All"/>'daki HER anahtarı <paramref name="state"/>'e yazar (değişmemiş olsa bile) ve
    /// yazımdan ÖNCEKİ kayıtlı değerden (<see cref="IsOn"/>) FARKLI olanların konsol notunu <see cref="All"/>
    /// sırasıyla döner. "Değişti mi" sorusu HER ZAMAN önceki KAYITLI duruma göre sorulur — taslağın açılış tohumuna
    /// göre değil; bu yüzden ilk kayıtta (henüz hiç kaydedilmemiş bir kurulum) taslak katalog varsayılanını
    /// korursa not YOK.</summary>
    public static IReadOnlyList<string> Commit(UiState state, Func<GeneralSetting, bool> draftValue)
    {
        List<string> notes = [];
        foreach (var s in All)
        {
            bool before = IsOn(state, s.Setting);
            bool after = draftValue(s.Setting);
            s.Write(state, after);
            if (after != before) notes.Add(s.Note(after));
        }
        return notes;
    }

    private static ShellSwitch Find(GeneralSetting setting) => All.Single(s => s.Setting == setting);
}
