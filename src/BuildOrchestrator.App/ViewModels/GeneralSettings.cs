using BuildOrchestrator.App.Shell;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>[design v1.19.0 §2.9] Settings → General sayfasının anahtarları.
/// <para>Altı anahtarın hepsi gerçektir. <see cref="PullBeforeBuild"/>
/// (<see cref="SettingsDraftViewModel.PullExternalsBeforeBuild"/>) ve <see cref="StashOnBranchSwitch"/>
/// (<see cref="SettingsDraftViewModel.StashOnBranchSwitch"/>) motora giden iş akışı tercihleridir; kalan dördü kabuk
/// anahtarıdır ve <see cref="ShellSwitches"/> tablosu üzerinden kalıcıdır: Save'de yazılır, diyalog kayıtlı değeri
/// gösterir, Export/Import taşır, değişince konsola not düşer.</para>
/// <para><b>[DEĞİŞEN KURAL — P3 2026-09-28 · P4 2026-09-29, kullanıcı kararları]</b> ESKİ: dört kabuk anahtarı
/// (<see cref="StartWithWindows"/>, <see cref="StartMinimizedToTray"/>, <see cref="CloseToTray"/>,
/// <see cref="ShowNotifications"/>) yalnız diyalog taslağında yaşardı — kaydedilmez, dosyaya yazılmaz, hiçbir davranışı
/// sürmezdi. Artık Windows ile başlama, Windows ile açılışta tepside başlama, pencere kapanışı
/// (<c>MainWindow.OnClosing</c>) ve üç tray-balloon yolu bu değerleri okur.</para></summary>
public enum GeneralSetting
{
    /// <summary>[P4] Kalıcı kabuk anahtarı (<see cref="ShellSwitches.StartWithWindows"/>) — Windows'un başlangıç
    /// kaydını Save anında yazar/siler.</summary>
    StartWithWindows,
    /// <summary>[P4] Kalıcı kabuk anahtarı (<see cref="ShellSwitches.StartMinimizedToTray"/>) — Windows ile açılışta
    /// pencere mi tepside mi. <see cref="StartWithWindows"/> kapalıyken etkisizdir.</summary>
    StartMinimizedToTray,
    /// <summary>[P3] Kalıcı kabuk anahtarı (<see cref="ShellSwitches.CloseToTray"/>) — pencere kapanışını sürer:
    /// açık ⇒ × tepsiye gizler, kapalı ⇒ güvenli tam çıkış.</summary>
    CloseToTray,
    /// <summary>Gerçek bayrak: <see cref="SettingsDraftViewModel.PullExternalsBeforeBuild"/>.</summary>
    PullBeforeBuild,
    /// <summary>[P3] Kalıcı kabuk anahtarı (<see cref="ShellSwitches.ShowNotifications"/>) — kapalıyken tepsi hiçbir
    /// OS balonu göstermez.</summary>
    ShowNotifications,
    /// <summary>Gerçek bayrak: <see cref="SettingsDraftViewModel.StashOnBranchSwitch"/> — branch chip'inden
    /// checkout'ta kirli ağaç stash'lenip geçilsin mi (spec 2026-09-18 §6.3).</summary>
    StashOnBranchSwitch,
}

/// <summary>General sayfasındaki tek bir ayar satırının tanımı (prototip <c>GENERAL_GROUPS</c> satırı).</summary>
/// <param name="Default">Taslağın açılış değeri ve Clear'ın döndüğü değer.</param>
/// <param name="DependsOn">Bu satır yalnız o anahtar açıkken etkindir (prototip <c>dep</c>).</param>
/// <param name="AutomationName">Switch'in UIA adı; <c>null</c> ise etiket.</param>
public sealed record GeneralSettingDefinition(
    GeneralSetting Setting, string Label, string Description, bool Default,
    GeneralSetting? DependsOn = null, string? AutomationName = null)
{
    public string SwitchName => AutomationName ?? Label;
}

/// <summary>Bir grup: caps başlık + satırları.</summary>
public sealed record GeneralSettingGroupDefinition(string Title, IReadOnlyList<GeneralSettingDefinition> Rows);

/// <summary>[design v1.19.0 §2.9] General sayfasının TEK kaynağı (prototip <c>GENERAL_GROUPS</c> +
/// <c>DEFAULT_GENERAL</c>): yeni bir ayar = buraya bir satır; düzen işi yoktur (satırlar tek <c>ToggleRow</c>
/// şablonuyla çizilir). Pull açıklaması git-only uyarlamadır (TFVC kaldırıldı).</summary>
public static class GeneralSettingsCatalog
{
    public static IReadOnlyList<GeneralSettingGroupDefinition> Groups { get; } =
    [
        new("STARTUP",
        [
            new(GeneralSetting.StartWithWindows, "Start with Windows",
                "Launch when you sign in to Windows.", Default: false),
            new(GeneralSetting.StartMinimizedToTray, "Start minimized to tray",
                "No window on start — the tray icon brings it back.", Default: false,
                DependsOn: GeneralSetting.StartWithWindows),
            new(GeneralSetting.CloseToTray, "Close to tray",
                "Closing the window leaves the engine running in the tray.", Default: true),
        ]),
        new("BUILD",
        [
            new(GeneralSetting.PullBeforeBuild, "Pull before build",
                "Update every external working copy first — a fast-forward-only git pull, one per copy.", Default: true,
                AutomationName: AccessibilityNames.PullExternalsBeforeBuild),
        ]),
        new("BRANCHES",
        [
            new(GeneralSetting.StashOnBranchSwitch, "Stash and switch branches",
                "When the working tree has uncommitted changes, stash them (including untracked files) and switch. "
                + "Off: switching stops and asks you to commit or stash first.", Default: false),
        ]),
        new("NOTIFICATIONS",
        [
            new(GeneralSetting.ShowNotifications, "Show notifications",
                "A tray notification when a build finishes — succeeded or failed.", Default: true),
        ]),
    ];

    /// <summary>Kataloğun TEK satırı — <see cref="ShellSwitch.Note"/>'un etiketi (kopya YASAK) buradan okur, bir
    /// literal olarak tekrarlamaz.</summary>
    public static GeneralSettingDefinition Definition(GeneralSetting setting) =>
        Groups.SelectMany(g => g.Rows).Single(r => r.Setting == setting);
}

/// <summary>General sayfasındaki bir satırın taslak durumu — <c>Ds.Settings.ToggleRow</c> şablonu buna bağlanır.</summary>
public sealed partial class GeneralSettingRowViewModel : ObservableObject
{
    public GeneralSettingRowViewModel(GeneralSettingDefinition definition, bool isFirstInGroup)
    {
        Definition = definition;
        IsFirstInGroup = isFirstInGroup;
        _isOn = definition.Default;
    }

    public GeneralSettingDefinition Definition { get; }
    public string Label => Definition.Label;

    /// <summary>Satırın açıklaması: kataloğun metni; [P4] bir <see cref="Note"/> varsa onun yerine not.</summary>
    public string Description => Note ?? Definition.Description;

    public string SwitchName => Definition.SwitchName;

    /// <summary>[P4] Satırın o anki durumunu anlatan, açıklamanın YERİNE geçen not — ör. Görev Yöneticisi'nde devre
    /// dışı bırakılmış Start with Windows. <c>null</c> ⇒ kataloğun açıklaması.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string? _note;

    /// <summary>Grubun ilk satırı üstte hairline taşımaz.</summary>
    public bool IsFirstInGroup { get; }

    [ObservableProperty] private bool _isOn;

    /// <summary>Bağımlı satırda üst anahtarın değeri; diğerlerinde hep açık.</summary>
    [ObservableProperty] private bool _isEnabled = true;
}

/// <summary>General sayfasındaki bir grubun taslak görünümü.</summary>
public sealed class GeneralSettingGroupViewModel(string title, bool isFirst, IReadOnlyList<GeneralSettingRowViewModel> rows)
{
    public string Title { get; } = title;

    /// <summary>İlk grup üstte 22px aralık almaz.</summary>
    public bool IsFirst { get; } = isFirst;

    public IReadOnlyList<GeneralSettingRowViewModel> Rows { get; } = rows;
}
