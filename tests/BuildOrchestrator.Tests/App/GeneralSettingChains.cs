using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B3 · karar 47a] Settings → General'ın boolean ayarlarının zincir tablosu — testlerin TEK yeri (kopya YASAK): her satır bir ayarın
/// zincirdeki yüzlerini taşır (etiket, onaylanmış varsayılan, ayar dosyasındaki anahtar, <see cref="UiState"/> alanı, konsol notları) ve
/// zinciri koşan teoriler (<see cref="GeneralSettingChainTests"/>) satırı ayarın adıyla buradan alır. Yeni bir ayar = kataloğa bir satır +
/// buraya bir satır; tablonun kataloğu eksiksiz kapsadığı ayrıca pinlidir
/// (<see cref="GeneralSettingChainTests.The_table_has_one_row_per_catalog_row_and_the_shell_rows_are_the_shell_switches"/>).
/// <para>İki tür satır vardır: VM'de yaşayan beş iş akışı tercihi (<see cref="WorkflowFaces"/> — Pull before build, Resolve cycles at
/// full priority, Stash and switch branches, Compact console, Stop the engine when idle) ve <see cref="ShellSwitches"/> tablosunun dört
/// kabuk anahtarı (<see cref="ShellFaces"/>).</para>
/// </summary>
internal static class GeneralSettingChains
{
    /// <summary>Zincir testlerinin repo kökü — taslak, Save ve uygulama aynı kökte koşar (kök değişimi zincirin konusu değildir).</summary>
    public const string Root = @"D:\repo";

    /// <summary>Pull before build'in notu yalnız tanımlı harici proje varken yazılır: satırın uygulaması bir kartla gelir.</summary>
    private static readonly ExternalProject Mail = new(@"C:\src\shared\Delta.Common");

    /// <summary>Tablo — katalog sırasıyla.</summary>
    public static IReadOnlyList<GeneralSettingChain> All { get; } =
    [
        new(GeneralSetting.StartWithWindows, "Start with Windows", ApprovedDefault: false,
            "startWithWindows", f => f.StartWithWindows, s => s.Autostart, (s, v) => s.Autostart = v,
            Shell: new(ShellSwitches.StartWithWindows, nameof(UiState.Autostart),
                OnNote: "Start with Windows on — the app starts when you sign in to Windows",
                OffNote: "Start with Windows off — signing in to Windows no longer starts the app")),
        new(GeneralSetting.StartMinimizedToTray, "Start minimized to tray", ApprovedDefault: false,
            "startMinimizedToTray", f => f.StartMinimizedToTray, s => s.StartMinimizedToTray, (s, v) => s.StartMinimizedToTray = v,
            Shell: new(ShellSwitches.StartMinimizedToTray, nameof(UiState.StartMinimizedToTray),
                OnNote: "Start minimized to tray on — signing in to Windows starts the app in the tray, without a window",
                OffNote: "Start minimized to tray off — signing in to Windows opens the window")),
        new(GeneralSetting.CloseToTray, "Close to tray", ApprovedDefault: true,
            "closeToTray", f => f.CloseToTray, s => s.CloseToTray, (s, v) => s.CloseToTray = v,
            Shell: new(ShellSwitches.CloseToTray, nameof(UiState.CloseToTray),
                OnNote: "Close to tray on — closing the window keeps the app running in the tray",
                OffNote: "Close to tray off — closing the window quits the app")),
        // Varsayılan AÇIK: bayrak öncesi kaydedilmiş bir kurulum bugünkü davranışı (güncelle) sürdürür.
        new(GeneralSetting.PullBeforeBuild, "Pull before build", ApprovedDefault: true,
            "pullExternalBeforeBuild", f => f.PullExternalBeforeBuild, s => s.UpdateExternals, (s, v) => s.UpdateExternals = v,
            Workflow: new(nameof(SettingsDraftViewModel.PullExternalsBeforeBuild),
                d => d.PullExternalsBeforeBuild, (d, v) => d.PullExternalsBeforeBuild = v,
                v => new(null, Root, pullExternalsBeforeBuild: v),
                r => r.UpdateExternals, (r, v) => r.UpdateExternals = v,
                (r, v) => r.ApplySettingsAsync([], Root, [Mail], pullExternalsBeforeBuild: v),
                OnNote: "Pull before build on — external working copies update first",
                OffNote: "Pull before build off — external working copies are used as they are")),
        // Varsayılan AÇIK (kullanıcı onayı). [DEĞİŞEN KURAL — fix 1A · M5] Notların soneki eskiden "Resolve cycles follows…" / "Resolve
        // cycles runs…" idi; Save koşu sürerken de yazılır ve not uçuştaki koşuyu değiştiriyormuş gibi okunuyordu (motor koşu başındaki
        // anahtarı korur) — sonek artık bir SONRAKİ Resolve koşusunu söyler. Etiket önekini kod katalogdan türetir; tablo literal metni pinler.
        new(GeneralSetting.ResolveAtFullPriority, "Resolve cycles at full priority", ApprovedDefault: true,
            "resolveAtFullPriority", f => f.ResolveAtFullPriority, s => s.ResolveAtFullPriority, (s, v) => s.ResolveAtFullPriority = v,
            Workflow: new(nameof(SettingsDraftViewModel.ResolveAtFullPriority),
                d => d.ResolveAtFullPriority, (d, v) => d.ResolveAtFullPriority = v,
                v => new(null, Root, resolveAtFullPriority: v),
                r => r.ResolveAtFullPriority, (r, v) => r.ResolveAtFullPriority = v,
                (r, v) => r.ApplySettingsAsync([], Root, [], resolveAtFullPriority: v),
                OnNote: "Resolve cycles at full priority on — the next Resolve cycles run runs at normal priority with no CPU cap",
                OffNote: "Resolve cycles at full priority off — the next Resolve cycles run follows the performance mode")),
        // Varsayılan KAPALI: araç kullanıcının commit'lenmemiş işini kendiliğinden kenara koymaz.
        new(GeneralSetting.StashOnBranchSwitch, "Stash and switch branches", ApprovedDefault: false,
            "stashOnBranchSwitch", f => f.StashOnBranchSwitch, s => s.StashOnBranchSwitch, (s, v) => s.StashOnBranchSwitch = v,
            Workflow: new(nameof(SettingsDraftViewModel.StashOnBranchSwitch),
                d => d.StashOnBranchSwitch, (d, v) => d.StashOnBranchSwitch = v,
                v => new(null, Root, stashOnBranchSwitch: v),
                r => r.StashOnBranchSwitch, (r, v) => r.StashOnBranchSwitch = v,
                (r, v) => r.ApplySettingsAsync([], Root, [], stashOnBranchSwitch: v),
                OnNote: "Stash and switch branches on — uncommitted changes are stashed before a branch switch",
                OffNote: "Stash and switch branches off — a branch switch stops while there are uncommitted changes")),
        // Varsayılan AÇIK. Notlar "from the next run" der: değişiklik mevcut anlatıyı yeniden yazmaz, bir sonraki işlemde geçerli olur.
        // Ayarın VM'de ikinci bir okuması vardır: etkin kip (CompactConsoleInEffect) — işlem yokken ayarın kendisidir.
        new(GeneralSetting.CompactConsole, "Compact console", ApprovedDefault: true,
            "compactConsole", f => f.CompactConsole, s => s.CompactConsole, (s, v) => s.CompactConsole = v,
            Workflow: new(nameof(SettingsDraftViewModel.CompactConsole),
                d => d.CompactConsole, (d, v) => d.CompactConsole = v,
                v => new(null, Root, compactConsole: v),
                r => r.CompactConsole, (r, v) => r.CompactConsole = v,
                (r, v) => r.ApplySettingsAsync([], Root, [], compactConsole: v),
                OnNote: "Compact console on — errors, the first warnings of each project and a warning count from the next run",
                OffNote: "Compact console off — every MSBuild line is shown from the next run",
                InEffect: r => r.CompactConsoleInEffect)),
        // Varsayılan AÇIK. Kapatma notu "no longer stopped" der, "stays running" DEMEZ (kontrolcü kararı 10): ayarı kapatmak uykudaki
        // motoru uyandırmaz. "Beş" sözcüğünün sabitle uyuşması StopEngineWhenIdleSettingsTests'tedir.
        new(GeneralSetting.StopEngineWhenIdle, "Stop the engine when idle", ApprovedDefault: true,
            "stopEngineWhenIdle", f => f.StopEngineWhenIdle, s => s.StopEngineWhenIdle, (s, v) => s.StopEngineWhenIdle = v,
            Workflow: new(nameof(SettingsDraftViewModel.StopEngineWhenIdle),
                d => d.StopEngineWhenIdle, (d, v) => d.StopEngineWhenIdle = v,
                v => new(null, Root, stopEngineWhenIdle: v),
                r => r.StopEngineWhenIdle, (r, v) => r.StopEngineWhenIdle = v,
                (r, v) => r.ApplySettingsAsync([], Root, [], stopEngineWhenIdle: v),
                OnNote: "Stop the engine when idle on — the engine exits after five idle minutes",
                OffNote: "Stop the engine when idle off — the engine is no longer stopped when idle")),
        new(GeneralSetting.ShowNotifications, "Show notifications", ApprovedDefault: true,
            "showNotifications", f => f.ShowNotifications, s => s.ShowNotifications, (s, v) => s.ShowNotifications = v,
            Shell: new(ShellSwitches.ShowNotifications, nameof(UiState.ShowNotifications),
                OnNote: "Show notifications on — tray notifications are shown",
                OffNote: "Show notifications off — no tray notifications are shown")),
    ];

    /// <summary>Ayarın satırı. Kataloğun tabloda olmayan bir satırı adıyla raporlanır.</summary>
    public static GeneralSettingChain Of(GeneralSetting setting) =>
        All.SingleOrDefault(c => c.Setting == setting)
        ?? throw new Xunit.Sdk.XunitException($"{setting}: the catalog has a row the chain table does not — add one to GeneralSettingChains");

    /// <summary>VM'de yaşayan beş iş akışı tercihi.</summary>
    public static IEnumerable<GeneralSettingChain> Workflow => All.Where(c => c.Workflow is not null);

    /// <summary><see cref="ShellSwitches"/>'in dört kabuk anahtarı.</summary>
    public static IEnumerable<GeneralSettingChain> Shell => All.Where(c => c.Shell is not null);
}

/// <summary>Tablonun bir satırı: bir General ayarının zincirdeki yüzleri.</summary>
/// <param name="Label">Kataloğun etiketi — konsol notlarının öneki ve "bu ayar konsola bir şey yazdı mı" sorusunun anahtarı.</param>
/// <param name="ApprovedDefault">Onaylanmış varsayılan (spec/kullanıcı kararı) — kataloğun satırı bunu taşır; tek literal pini burasıdır.</param>
/// <param name="FileKey">Dışa aktarılan ayar dosyasındaki JSON anahtarı (<see cref="SettingsFile"/>).</param>
/// <param name="File">Dosyanın tipli alanı.</param>
/// <param name="State">Kalıcı durumun (<see cref="UiState"/>) alanı — anahtar hiç yazılmamışsa <c>null</c>.</param>
/// <param name="SetState">Kalıcı durumun alanını yazar.</param>
internal sealed record GeneralSettingChain(
    GeneralSetting Setting, string Label, bool ApprovedDefault,
    string FileKey, Func<SettingsFile, bool?> File,
    Func<UiState, bool?> State, Action<UiState, bool?> SetState,
    WorkflowFaces? Workflow = null, ShellFaces? Shell = null);

/// <summary>VM'de yaşayan bir iş akışı tercihinin yüzleri.</summary>
/// <param name="DraftFlag">Taslağın özelliğinin adı — satır değişince taslak bunu bildirir.</param>
/// <param name="NewDraft">Diyaloğun taslağı, ctor'a yalnız bu ayarın canlı değeri verilerek.</param>
/// <param name="Apply">Settings Save'in uygulaması (<see cref="RunViewModel.ApplySettingsAsync"/>), yalnız bu ayarı taşıyarak.</param>
/// <param name="InEffect">Ayarın VM'deki ikinci okuması, varsa (Compact console'un etkin kipi).</param>
internal sealed record WorkflowFaces(
    string DraftFlag,
    Func<SettingsDraftViewModel, bool> Draft, Action<SettingsDraftViewModel, bool> SetDraft,
    Func<bool, SettingsDraftViewModel> NewDraft,
    Func<RunViewModel, bool> Vm, Action<RunViewModel, bool> SetVm,
    Func<RunViewModel, bool, Task> Apply,
    string OnNote, string OffNote,
    Func<RunViewModel, bool>? InEffect = null)
{
    /// <summary>Değer değişince konsola düşen not.</summary>
    public string Note(bool on) => on ? OnNote : OffNote;
}

/// <summary>Bir kabuk anahtarının yüzleri.</summary>
/// <param name="Reads">Davranış okuyucularının kısaltması (<see cref="ShellSwitches.StartWithWindows"/> vb.) — kayıtlı ?? varsayılan.</param>
/// <param name="StateToken">Alanın <c>ui-state.json</c>'daki adı.</param>
internal sealed record ShellFaces(Func<UiState, bool> Reads, string StateToken, string OnNote, string OffNote)
{
    /// <summary>Save'de değer değişince konsola düşen not.</summary>
    public string Note(bool on) => on ? OnNote : OffNote;
}
