using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>[design v1.19.0 §2.9] Settings → General sayfasının anahtarları.
/// <para><b>Henüz davranışa bağlı DEĞİL (kullanıcı kararı 1):</b> <see cref="StartWithWindows"/>,
/// <see cref="StartMinimizedToTray"/>, <see cref="CloseToTray"/> ve <see cref="ShowNotifications"/> yalnız diyalog
/// taslağında yaşar — kaydedilmez, ayar dosyasına yazılmaz/okunmaz, konsola not düşmez ve hiçbir davranışı
/// (autostart, tray, bildirim) sürmez; her açılışta varsayılana döner. Yalnız <see cref="PullBeforeBuild"/>
/// (<see cref="SettingsDraftViewModel.PullExternalsBeforeBuild"/>) ve <see cref="StashOnBranchSwitch"/>
/// (<see cref="SettingsDraftViewModel.StashOnBranchSwitch"/>) gerçektir.</para></summary>
public enum GeneralSetting
{
    /// <summary>Henüz bağlı değil — yalnız taslak.</summary>
    StartWithWindows,
    /// <summary>Henüz bağlı değil — yalnız taslak. <see cref="StartWithWindows"/> kapalıyken etkisizdir.</summary>
    StartMinimizedToTray,
    /// <summary>Henüz bağlı değil — yalnız taslak.</summary>
    CloseToTray,
    /// <summary>Gerçek bayrak: <see cref="SettingsDraftViewModel.PullExternalsBeforeBuild"/>.</summary>
    PullBeforeBuild,
    /// <summary>Henüz bağlı değil — yalnız taslak.</summary>
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
    public string Description => Definition.Description;
    public string SwitchName => Definition.SwitchName;

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
