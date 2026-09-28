using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [P4] Bir General→KABUK anahtarının satırı: <see cref="UiState"/> ve <see cref="SettingsFile"/> alanına nasıl okunup
/// yazılacağı, ve konsol notunun iki yarısı — tek yerde (kopya YASAK, CLAUDE.md).
/// </summary>
/// <param name="Setting">Kataloğun hangi satırı — <see cref="Note"/>'un etiketi <see cref="GeneralSettingsCatalog.Definition"/>
/// üzerinden buradan gelir.</param>
/// <param name="Read">Kayıtlı değeri <see cref="UiState"/>'ten okur — anahtar hiç kaydedilmediyse <c>null</c>.</param>
/// <param name="Write">Değeri <see cref="UiState"/>'e yazar.</param>
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
    /// <summary>Konsol notu — etiket KATALOGDAN gelir (kopya YASAK): "Start with Windows" burada literal olarak
    /// yazılmaz.</summary>
    public string Note(bool on) =>
        $"{GeneralSettingsCatalog.Definition(Setting).Label} {(on ? "on" : "off")} — {(on ? OnClause : OffClause)}";
}

/// <summary>
/// [P4] Settings → General'ın KABUK anahtarlarının TEK tablosu — pencere/tepsi/başlangıç davranışını süren anahtarlar:
/// bugün <see cref="GeneralSetting.StartWithWindows"/> ve <see cref="GeneralSetting.StartMinimizedToTray"/>.
/// <see cref="SettingsDraftViewModel"/> bu tabloyu <see cref="All"/> üzerinden gezerek
/// <see cref="SettingsDraftViewModel.ToFile"/>/<see cref="SettingsDraftViewModel.LoadFrom"/>/
/// <see cref="SettingsDraftViewModel.CommitAsync"/>'i sürer — yeni bir kabuk anahtarı = buraya bir satır. Çalışma
/// anında değer her soruda kalıcı durumdan TAZE okunur (<see cref="IsOn"/>) — arada kopya yok.
///
/// <para>Pull before build ve Stash and switch branches bu tabloda DEĞİLDİR: onlar motora giden iş akışı
/// tercihleridir ve <see cref="RunViewModel"/>'de yaşar.</para>
///
/// <para>TODO(close-to-tray merge): close-to-tray branch'i (P3) bu tabloyu AYNI sözleşmeyle Close to tray ve Show
/// notifications satırlarıyla kurdu. Merge'de iki tablo tek tabloda birleşir (satırlar katalog sırasıyla:
/// StartWithWindows, StartMinimizedToTray, CloseToTray, ShowNotifications) ve bu not kalkar.</para>
/// </summary>
internal static class ShellSwitches
{
    /// <summary>Katalog sırası: STARTUP grubundaki Start with Windows ve Start minimized to tray.</summary>
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
    ];

    /// <summary>Anahtarın GEÇERLİ değeri: kayıtlı ?? katalog varsayılanı. Davranışın okuduğu TEK kapı
    /// (<see cref="StartWithWindows"/>, <see cref="StartMinimizedToTray"/> bunun birer kısaltmasıdır).</summary>
    public static bool IsOn(UiState state, GeneralSetting setting) =>
        Find(setting).Read(state) ?? GeneralSettingsCatalog.Definition(setting).Default;

    /// <summary>Uygulamanın tercihi: Windows oturumu açılınca başlasın mı. Açılış bunu Windows kaydıyla hizalar
    /// (<see cref="Services.AutostartService.Apply"/>).</summary>
    public static bool StartWithWindows(UiState state) => IsOn(state, GeneralSetting.StartWithWindows);

    /// <summary>Windows ile açılışta pencere gösterilmeden tepside mi başlansın — açılış yolunun kararı
    /// (<see cref="StartupArgs.Decide"/>) bunu okur.</summary>
    public static bool StartMinimizedToTray(UiState state) => IsOn(state, GeneralSetting.StartMinimizedToTray);

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
