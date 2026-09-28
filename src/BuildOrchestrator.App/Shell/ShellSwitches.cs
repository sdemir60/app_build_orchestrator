using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [P3 · Task 1] Bir General→KABUK anahtarının satırı: <see cref="UiState"/> ve <see cref="SettingsFile"/> alanına
/// nasıl okunup yazılacağı, ve konsol notunun iki yarısı — tek yerde (kopya YASAK, CLAUDE.md).
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
    /// <summary>Konsol notu — etiket KATALOGDAN gelir (kopya YASAK): "Close to tray"/"Show notifications" burada
    /// literal olarak yazılmaz.</summary>
    public string Note(bool on) =>
        $"{GeneralSettingsCatalog.Definition(Setting).Label} {(on ? "on" : "off")} — {(on ? OnClause : OffClause)}";
}

/// <summary>
/// [P3 · Task 1] Settings → General'ın KABUK anahtarlarının TEK tablosu — bugün <see cref="GeneralSetting.CloseToTray"/>
/// ve <see cref="GeneralSetting.ShowNotifications"/>; bir sonraki görev <c>Start with Windows</c>/<c>Start minimized
/// to tray</c>'i buraya ekleyecektir. <see cref="SettingsDraftViewModel"/> bu tabloyu <see cref="All"/> üzerinden
/// gezerek <see cref="SettingsDraftViewModel.ToFile"/>/<see cref="SettingsDraftViewModel.LoadFrom"/>/
/// <see cref="SettingsDraftViewModel.CommitAsync"/>'i sürer — yeni bir kabuk anahtarı = buraya bir satır.
///
/// <para><b>Yalnız KALICILIK.</b> Bu tip anahtarların DEĞERİNİ taşır; pencere kapanışını ya da tray balloon'u
/// BAĞLAMAZ — sonraki görevler <see cref="CloseToTray"/>/<see cref="ShowNotifications"/>'ı okuyacaktır.</para>
/// </summary>
internal static class ShellSwitches
{
    /// <summary>Katalog sırası: STARTUP grubundaki Close to tray, NOTIFICATIONS grubundaki Show notifications.</summary>
    public static IReadOnlyList<ShellSwitch> All { get; } =
    [
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

    /// <summary>Anahtarın GEÇERLİ değeri: kayıtlı ?? katalog varsayılanı. Sonraki görevlerin okuyacağı TEK kapı
    /// (<see cref="CloseToTray"/>, <see cref="ShowNotifications"/> bunun birer kısaltmasıdır).</summary>
    public static bool IsOn(UiState state, GeneralSetting setting) =>
        Find(setting).Read(state) ?? GeneralSettingsCatalog.Definition(setting).Default;

    /// <summary>[sonraki görev] Pencere kapanış yolunun okuyacağı kapı — burada davranışa BAĞLANMAZ, yalnız
    /// kalıcı değeri verir.</summary>
    public static bool CloseToTray(UiState state) => IsOn(state, GeneralSetting.CloseToTray);

    /// <summary>[sonraki görev] Üç tray-balloon yolunun okuyacağı kapı — burada davranışa BAĞLANMAZ, yalnız kalıcı
    /// değeri verir.</summary>
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
