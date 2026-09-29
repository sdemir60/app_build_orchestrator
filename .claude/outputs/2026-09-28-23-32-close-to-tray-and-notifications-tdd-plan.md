# Close to tray · güvenli tam çıkış · Show notifications — TDD dökümü

- **Tarih:** 2026-09-28 · **Branch:** `feat/close-to-tray-and-notifications` (worktree `app_build_orchestrator-ai`, taban `main` `6224eeb`)
- **Kaynak (spec):** kullanıcının P3 promptu — `.claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md` §4 P3 ve
  §2 varsayılanları; bağlam `.claude/outputs/2026-09-28-12-33-ui-function-inventory.md` (§10 #64/#67, §13, §14-3).

## Tasarım (tek paragraf başına tek karar)

1. **Kabuk anahtarları tek tabloda** (`App/Shell/ShellSwitches.cs`): General'ın pencere/tepsi/başlangıç davranışını
   süren anahtarları. Her satır: ui-state.json alanı, ayar dosyası anahtarı, konsol notu. Bugün `CloseToTray` ve
   `ShowNotifications`; P4'te `StartWithWindows`/`StartMinimizedToTray` aynı tabloya satır ekler. Pull before build
   ve Stash and switch branches bu tabloda DEĞİL (motora giden iş akışı tercihleri, RunViewModel'de yaşar).
   Çalışma anında değer her soruda `IUiStateStore`'dan TAZE okunur — arada kopya yok.
2. **Çıkış bekleyişi VM'de** (`RunViewModel.Exit.cs`): `RequestExit()` / `ExitPending` / `ExitReady`. "Uçuşta iş"
   sorusu mevcut `WorkspaceIdle` (koşu kilidi yok + Sync/Clean/Optimize/checkout/pull yok) — yeni tanım yazılmaz.
   Değerlendirme mevcut tek meşguliyet noktasına (`NotifyAutoSyncGate`) ve sessizlik bekçisine bağlanır.
3. **Pencere kararı saf** (`App/Shell/WindowCloseRule.cs`): gerçek çıkış → kapan; çıkış bekliyor → kal; Close to tray
   açık → tepsiye; kapalı → çıkış iste. MainWindow yalnız uygular.
4. **Bildirim kapısı yol başına, cevap tek yerden:** üç balon yolu da `ShellSwitches.ShowNotifications(state)`'e sorar
   (ilk-× kapısı, koşu-bitti controller'ı, ikinci instance kararı).

## Global Constraints (her task için bağlayıcı)

- Çalışma yeri: `D:\Projects\Other\Apps\app_build_orchestrator-ai` (branch `feat/close-to-tray-and-notifications`).
  Ana projeye (`app_build_orchestrator`) DOKUNMA.
- **Kırmızı test kuralı:** her davranış için test önce yazılır ve KIRMIZI gösterilir (komut + başarısız çıktı rapora).
  Kırmızı gösterilemiyorsa test yanlıştır.
- **Davranış değişince testi değişir:** eski kuralı pinleyen test sessizce silinmez/gevşetilmez; yeni kuralı pinleyecek
  şekilde yeniden yazılır, doc'una `[DEĞİŞEN KURAL]` + eski iddia + gerekçe yazılır. Eşik/bütçe gevşetmek YASAK.
- **Kopya YASAK:** aynı metin/değer/primitif iki yerde tanımlanmaz (kod ve testler — ortak fixture tek yerde:
  `SettingsDialogHost.FakeStore`, `MainWindowHost`, `DispatcherPump`, `TestPaths`, `SupervisorSandbox`).
- Kod, UI metni, log İngilizce; kod yorumları Türkçe (çevredeki üslup: köşeli etiket `[P3]`, kısa gerekçe).
- Headless süit pencere GÖSTERMEZ, balon çıkarmaz: `Window.Show()`/gerçek `AppTrayIcon` testte kurulmaz. Gerekirse seam.
  D8: testte gerçek bekleme/sleep yok (enjekte saat, `DispatcherPump.PumpUntil`, olay-güdümlü bekleme).
- WPF STA testleri `[StaFact]` + `[Collection("Console UI (serial)")]`.
- Dosyaları Edit/Write ile düzenle; `sed -i` (CRLF'i bozar) ve PowerShell `Set-Content` (UTF-8'i bozar) YASAK.
- `ReleaseNotes.cs`, `ARCHITECTURE.md`, `README.md` bu task'larda DEĞİŞMEZ (Task 6 hariç).
- Commit: task başına, Türkçe konu (`feat(shell): ...` deseni), mesaj dosyaya yazılıp `git commit -F` ile.
  **Claude attribution satırı (Co-Authored-By / Generated with) EKLENMEZ.**
- Test komutları: odaklı `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~<Sınıf>"`;
  commit öncesi TAM süit `--filter "Category!=Acceptance"` yeşil ve build uyarısız.

## Task 1: Kabuk anahtarlarının kalıcılığı (Close to tray, Show notifications)

**Amaç:** iki anahtar Save'de ui-state.json'a yazılır; diyalog her açılışta kayıtlı değeri gösterir; Export/Import
taşır (dosyada anahtar yoksa formdaki değer korunur); değer değişince konsola tek satır düşer, değişmezse sessiz;
Clear varsayılana döndürür (mevcut kural, mevcut test kapsıyor).

**Dosyalar:**
- Yeni `src/BuildOrchestrator.App/Shell/ShellSwitches.cs` (internal).
- `Shell/UiStateStore.cs` — `UiState`: `StashOnBranchSwitch`'in ARDINA `public bool? CloseToTray { get; set; }` ve
  `public bool? ShowNotifications { get; set; }` (doc: `UpdateExternals` ile AYNI gerekçeyle nullable; yok ⇒ katalog
  varsayılanı; okuma yalnız `ShellSwitches` üzerinden).
- `ViewModels/GeneralSettings.cs` — `GeneralSettingsCatalog.Definition(GeneralSetting)` (katalogdaki tek satırı döner);
  enum ve sınıf doc'ları: Close to tray / Show notifications artık GERÇEK (kabuk anahtarı); yalnız Start with Windows
  ve Start minimized to tray taslakta kalır.
- `ViewModels/SettingsFile.cs` — `StashOnBranchSwitch`'in ARDINA (Layers'tan önce)
  `[JsonPropertyName("closeToTray")] public bool? CloseToTray` ve `[JsonPropertyName("showNotifications")] public bool? ShowNotifications`;
  sınıf doc'undaki biçim satırı:
  `{ app, version, repositoryRoot, externalProjects[{ path }], pullExternalBeforeBuild, stashOnBranchSwitch, closeToTray, showNotifications, layers[{ name, pattern }] }`.
- `ViewModels/SettingsDraftViewModel.cs` — ctor'a son opsiyonel parametre `UiState? saved = null` (null ⇒ katalog
  varsayılanları); `ToFile`/`LoadFrom`/`CommitAsync` tablo üzerinden; `GeneralGroups` doc'u güncellenir.
- `ViewModels/RunViewModel.ActionBar.cs` — `ApplySettingsAsync`'e son opsiyonel parametre
  `IReadOnlyList<string>? settingNotes = null`; notlar `ApplyStashOnBranchSwitch`'ten HEMEN SONRA, idle kapısından
  ÖNCE `AppendRunLine` ile sırayla yazılır (doc'a param açıklaması).
- `Views/SettingsDialog.xaml.cs` — `Open`: taslak `store.Load()` ile kurulur.
- `Views/SettingsDialog.xaml` — General yorumu (satır ~156-161): Pull before build, Stash and switch branches, Close to
  tray ve Show notifications bağlı; Start with Windows ve Start minimized to tray henüz taslak.

**`ShellSwitches` sözleşmesi (birebir):**
```csharp
internal sealed record ShellSwitch(
    GeneralSetting Setting,
    Func<UiState, bool?> Read, Action<UiState, bool> Write,
    Func<SettingsFile, bool?> ReadFile, Action<SettingsFile, bool> WriteFile,
    string OnClause, string OffClause)
{
    // Etiket katalogdan (kopya YASAK): "<Label> on — <OnClause>" / "<Label> off — <OffClause>"
    public string Note(bool on);
}

internal static class ShellSwitches
{
    public static IReadOnlyList<ShellSwitch> All { get; }            // [CloseToTray, ShowNotifications] (katalog sırası)
    public static bool IsOn(UiState state, GeneralSetting setting);   // kayıtlı ?? katalog Default
    public static bool CloseToTray(UiState state);                    // IsOn(state, GeneralSetting.CloseToTray)
    public static bool ShowNotifications(UiState state);              // IsOn(state, GeneralSetting.ShowNotifications)
    // Save: her anahtarı state'e yazar; kayıtlı (IsOn) değerden FARKLI olanların notunu All sırasıyla döner.
    public static IReadOnlyList<string> Commit(UiState state, Func<GeneralSetting, bool> draftValue);
}
```
Konsol notları (birebir; etiket kısmı katalogdan gelir):
- `Close to tray on — closing the window keeps the app running in the tray`
- `Close to tray off — closing the window quits the app`
- `Show notifications on — tray notifications are shown`
- `Show notifications off — no tray notifications are shown`

Taslak kablosu: ctor `saved` verildiyse her `ShellSwitches.All` satırı `IsOn(saved, s)` ile tohumlanır; `ToFile()` =
`SettingsFile.From(...)` + her anahtar için `WriteFile(file, GeneralRow(s).IsOn)`; `LoadFrom` her anahtar için
`ReadFile(file) is { } v` ise satırı yazar; `CommitAsync`: `var notes = ShellSwitches.Commit(state, s => GeneralRow(s).IsOn);`
`store.Save(state)` ÖNCE, sonra `ApplySettingsAsync(..., settingNotes: notes)`.

**Testler (önce kırmızı):**
- Yeni `tests/BuildOrchestrator.Tests/App/ShellSwitchesTests.cs`:
  - `An_unsaved_switch_reads_its_catalog_default` — `new UiState()` → iki anahtar açık.
  - `A_saved_switch_reads_its_saved_value` — `CloseToTray=false, ShowNotifications=false` → kapalı.
  - `The_switches_survive_the_json_store_and_a_null_token_reads_as_the_default` — `JsonUiStateStore` (TempDir):
    false yaz → yeniden oku false; dosyada `"CloseToTray": null` → varsayılan açık ve dosyadaki diğer alanlar
    (ör. `LayoutMode`) KORUNUR.
  - `The_draft_opens_on_the_saved_values` — `new SettingsDraftViewModel(null, @"D:\repo", saved: new UiState { CloseToTray = false, ShowNotifications = false })` → iki satır kapalı.
  - `Save_writes_both_switches_and_notes_only_the_changed_ones` — `CommitAsync(run, FakeStore)`; store'da değerler;
    `run.GetRunDocumentText()` değişenin notunu BİR kez içerir, değişmeyeninkini içermez (desen:
    `StashOnBranchSwitchTests.A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet`).
  - `The_switches_round_trip_through_the_settings_file` — export JSON `"closeToTray": false` ve
    `"showNotifications": false` içerir; yeni taslağa `LoadFrom` ikisini de kapatır.
  - `A_file_without_the_switches_leaves_the_form_untouched` — `LoadFrom(SettingsFile.From(@"D:\repo", []))` formdaki
    (kapalı) değerleri korur.
- `SettingsDialogHost.OpenRealized`'a opsiyonel `UiState? saved = null` (FakeStore'u tohumlar) ve bir realize testi:
  `The_general_page_opens_on_the_saved_switch_values` (SettingsGeneralPageTests içinde; kayıtlı kapalı → switch'ler kapalı).
- **Yeniden yazılacak iki test** (`SettingsGeneralPageTests`), `[DEĞİŞEN KURAL — P3, kullanıcı kararı 2026-09-28]` doc'u ile:
  - `The_four_new_switches_are_not_written_to_the_settings_file` → `The_startup_switches_are_not_written_to_the_settings_file`
    (yalnız Start with Windows + Start minimized to tray çevrilir → JSON aynı). Eski iddia: dört anahtar dosyaya girmez.
  - `The_four_new_switches_are_not_saved_and_reopen_on_their_defaults` → `The_startup_switches_are_not_saved_and_reopen_on_their_defaults`
    (yalnız iki başlangıç anahtarı) + ayrı `Close_to_tray_and_show_notifications_are_saved_and_reopen_on_the_saved_value`.
  - Sınıf doc'undaki "Kullanıcı kararı 1" paragrafı güncellenir.

## Task 2: Güvenli çıkış bekleyişi (VM) ve şerit

**Amaç:** kullanıcının tam çıkış isteği uçuşta iş yoksa HEMEN, varsa iş bitince çıkışı bildirir. Derleme graceful
durdurulur (yeni proje başlamaz, uçuştakiler post-build copy dahil biter); Sync/Clean/Optimize/checkout/pull
beklenir. İkinci istek ikinci durdurma/satır üretmez. Motor yanıt vermezse (mevcut sessizlik bekçisi) ya da ölürse
beklenmez. Bekleyişte kendiliğinden Sync başlamaz. Şerit bekleyiş boyunca "Stopping" der.

**Dosyalar:**
- Yeni `src/BuildOrchestrator.App/ViewModels/RunViewModel.Exit.cs` (partial).
- `ViewModels/RunViewModel.AutoSync.cs` — `NotifyAutoSyncGate()` çıkış koşulunu da değerlendirir (doc: iki tüketici).
- `ViewModels/RunViewModel.cs` — `RibbonLine` → `exitPending: ExitPending`.
- `ViewModels/RibbonText.cs` — `Compose`'a son opsiyonel `bool exitPending = false`; Stopping satırı tek yardımcıya
  çıkarılır (faz dalı ve çıkış dalı AYNI yardımcıyı çağırır).
- `Views/StickyRibbon.xaml.cs` — `nameof(RunViewModel.ExitPending)` metni tazeleyen gruba eklenir.

**Sözleşme (birebir):**
```csharp
public sealed partial class RunViewModel
{
    [ObservableProperty] private bool _exitPending;                 // bekleyiş sürüyor (bir kez true, geri dönmez)
    public event EventHandler? ExitReady;                           // TEK atım
    public void RequestExit();                                      // idempotent
    internal static string ExitPendingLine => "exit requested — the app closes when the work in flight finishes";
}
```
`RequestExit` sırası: (1) ikinci istekse hiçbir şey yapma; (2) `DisableAutoSync()`; (3) `WorkspaceIdle` ise
`ExitReady` (ExitPending false kalır, satır yazılmaz, komut gitmez); (4) değilse `ExitPending = true`,
`AppendRunLine(ExitPendingLine)`, `StopCommand.CanExecute(null)` ise `StopCommand.Execute(null)` (marking fazında
bekleyen koşuyu geri alır, aksi hâlde graceful `StopRunCommand`); (5) koşulu hemen yeniden değerlendir.
Koşul: `ExitPending && (WorkspaceIdle || EngineOverdueMessage is not null)` → `ExitReady` bir kez. Değerlendirme
noktaları: `NotifyAutoSyncGate()` (Sync/Clean/Optimize/checkout/pull ve koşu kilidinin her geçişi zaten buraya iner) ve
`partial void OnEngineOverdueMessageChanged`.

Şerit (birebir): `exitPending` doğruysa (motor-öldü ve motor-sustu dallarından SONRA, diğer her daldan ÖNCE) satır
Stopping satırıdır: faz `Stopping` ve `c.Building > 0` ise mevcut `"▸ Stopping — {0}/{1} · finishing {2} in flight"`,
aksi hâlde mevcut `"▸ Stopping — wrapping up"` (renk `Brush.TextSecondary`, glyph yok). Metinler tek yerde kalır.

**Testler (önce kırmızı)** — yeni `tests/BuildOrchestrator.Tests/App/SafeExitTests.cs` (VM düzeyi; motor başlatılmaz,
`vm.DebugSendOverride = _ => Task.CompletedTask` ile gönderim kabul edilir, `vm.DebugOnCommandSent` ile izlenir, cevap
`vm.OnEvent(...)`; saat enjekte — desen `EngineSilenceWatchdogTests`, `TrayMenuTests`):
- `An_exit_with_nothing_in_flight_is_ready_at_once` — boşta: `ExitReady` bir kez, hiç komut yok, `ExitPending` false, satır yok.
- `An_exit_during_a_build_stops_it_gracefully_and_waits_for_the_drain` — `RunStartedEvent` ile koşan koşu →
  tek `StopRunCommand(runId, StopKind.Graceful)`, faz `Stopping`, `ExitPending`, konsolda `ExitPendingLine` bir kez,
  `ExitReady` YOK; `RunStoppedEvent` → `ExitReady` bir kez.
- `A_second_exit_request_sends_no_second_stop_and_no_second_line`.
- `An_exit_waits_for_a_workspace_job_to_finish` — Sync, Clean (bitişte zincirlenen Sync dahil), Optimize, checkout,
  pull için ayrı ayrı (Theory ya da ayrı Fact): `StopRunCommand` YOK, iş sürerken `ExitReady` yok, iş (ve zinciri)
  bitince `ExitReady` bir kez. Mevcut kurulum desenleri: `CleanCommandTests`, `BranchCheckoutTests`,
  `RunViewModelStateTests` (Sync guard), `PullBeforeBuildTests`/`BranchInventoryTests` (pull).
- `A_silent_engine_does_not_hold_the_exit` — Stopping'de bekleyiş; saat `EngineSilenceThresholdMs` ilerler,
  `TickElapsed()` → `ExitReady`.
- `An_engine_that_dies_during_the_wait_releases_the_exit` — `OnEngineExited(1)` → `ExitReady`.
- `An_exit_during_the_marking_phase_cancels_the_pending_run` — `OperationChoreography` bitmeyen bir Task döndürür,
  Build basılır (komut henüz gitmedi) → `RequestExit` → `StartRunCommand`/`StopRunCommand` gitmez, `ExitReady`; sonradan
  koreografi tamamlansa da koşu gönderilmez.
- `No_automatic_sync_starts_while_the_exit_waits` — `EnableAutoSync` (sahte izleyici; `FakeHeadWatcher` deseni) +
  koşan koşu → `RequestExit` → `OnWindowActivated()` ve HEAD tetiği `SyncWorkspaceCommand` GÖNDERMEZ.
- `RibbonTextTests`: `An_exit_in_flight_reads_stopping_in_every_phase` (Syncing/Idle/Boot → `"▸ Stopping — wrapping up"`;
  Stopping + Building 2 → `"▸ Stopping — 3/10 · finishing 2 in flight"`); motor-öldü ve motor-sustu yine önce gelir.
- `StickyRibbonTests`: `The_ribbon_redraws_when_the_exit_starts_waiting` (ExitPending değişince metin yenilenir).

## Task 3: Pencere kararı ve MainWindow kablosu (×, Alt+F4, sistem menüsü, tepsi Exit)

**Amaç:** Close to tray açık → × tepsiye (bugünkü); kapalı → güvenli çıkış. Çıkış beklerken × pencereyi gizlemez ve
ikinci durdurma üretmez. Tepsi → Exit her zaman güvenli çıkıştır; beklerken pencere öne gelir. Oturum kapanışı
(SessionEnding) bugünkü gibi anında kapanır.

**Dosyalar:** yeni `src/BuildOrchestrator.App/Shell/WindowCloseRule.cs` (internal, saf); `MainWindow.xaml.cs`.

**Sözleşme (birebir):**
```csharp
internal enum CloseAction { Close, Stay, HideToTray, RequestExit }
internal static class WindowCloseRule
{
    // exiting (Shutdown başladı ya da oturum kapanıyor) → Close; exitPending → Stay; closeToTray → HideToTray; değilse RequestExit
    public static CloseAction Decide(bool exiting, bool exitPending, bool closeToTray);
}
```
MainWindow:
- `OnClosing`: `WindowCloseRule.Decide(_exiting, _vm.ExitPending, ShellSwitches.CloseToTray(_uiState.Load()))`;
  `Close` → `base.OnClosing(e)`; diğerleri `e.Cancel = true` + `HideToTray` → mevcut `MinimizeToTray()`,
  `RequestExit` → `_vm.RequestExit()`, `Stay` → hiçbir şey.
- ctor: `_vm.ExitReady += (_, _) => ExitNow();` — `ExitNow()`: `_exiting = true; ShutdownApplication();`.
- Test seam'leri (internal, ctor'da üretim değeriyle kurulur): `internal Action ShutdownApplication` — üretimde
  `Dispatcher.BeginInvoke(() => Application.Current?.Shutdown())` (Closing içinden yeniden girişi önler);
  `internal Action BringForward` — üretimde `ShowFromTray`.
- Tepsi: `OnSourceInitialized`'da `_tray.ExitRequested += ExitFromTray;`; `internal void ExitFromTray()`:
  `_vm.RequestExit(); if (_vm.ExitPending) BringForward();`. Eski `ExitApplication()` kalkar (tek çıkış yolu).
- `_exiting` alan doc'u ve `OnClose` yorumu yeni karara göre güncellenir; SessionEnding kablosu DEĞİŞMEZ.

**Testler (önce kırmızı):**
- Yeni `tests/BuildOrchestrator.Tests/App/WindowCloseRuleTests.cs`: dört dal (gerçek çıkış her iki anahtar değerinde
  Close; bekleyiş her iki değerde Stay; açık → HideToTray; kapalı → RequestExit).
- Yeni `tests/BuildOrchestrator.Tests/App/CloseToTrayTests.cs` (`MainWindowHost.New(temp)`, pencere GÖSTERİLMEZ;
  `window.Close()` Closing'i tetikler; `ShutdownApplication`/`BringForward` sayaçlı sahtelerle değiştirilir; gönderim
  `MainWindowHost.AcceptSends`):
  - `By_default_the_close_button_hides_to_the_tray` — kapatma iptal, shutdown yok, `ExitPending` false.
  - `With_close_to_tray_off_the_close_button_quits_when_nothing_is_in_flight` — ui-state'te `CloseToTray=false` →
    `Close()` → pompala → shutdown bir kez.
  - `With_close_to_tray_off_the_close_button_waits_for_a_running_build` — koşan koşu → `Close()` → tek graceful stop,
    shutdown yok; ikinci `Close()` → hâlâ tek stop; `RunStoppedEvent` → shutdown bir kez.
  - `The_tray_exit_brings_the_window_forward_while_it_waits` — (anahtar açıkken de) koşan koşu → `ExitFromTray()` →
    `BringForward` bir kez, shutdown yok; drain bitince shutdown.
  - `The_tray_exit_quits_without_showing_the_window_when_idle` — `BringForward` hiç, shutdown bir kez.
- Kaynak guard'ı (`TrayMenuTests` deseni): `MainWindow_wires_the_tray_exit_item_to_the_safe_exit` —
  `_tray.ExitRequested += ExitFromTray;` tam bir kez ve MainWindow'da `.Shutdown(` çağrısı tam bir yerde.

## Task 4: Show notifications — üç balon yolunun bastırılması

**Amaç:** anahtar kapalıyken HİÇBİR OS balonu gösterilmez; ilk-× balonu bastırılınca `TrayBalloonShown` HARCANMAZ;
tepsi göstergesi (overlay) etkilenmez; ikinci instance ayarı ui-state.json'dan okur ve ayrışan çıkış kodu (3) korunur.

**Dosyalar:** `Shell/UiStateStore.cs` (`FirstCloseBalloonGate`), `Services/TrayBuildIndicatorController.cs`,
`MainWindow.xaml.cs`, `Shell/SecondInstanceGate.cs`, `App.xaml.cs`; `Shell/AppTrayIcon.cs` yalnız doc (balon
metotlarının kapıları).

**Sözleşme (birebir):**
- `FirstCloseBalloonGate.ClaimShow()`: `TrayBalloonShown` ise false; `!ShellSwitches.ShowNotifications(state)` ise
  **kaydetmeden** false; değilse bayrağı yaz, true.
- `TrayBuildIndicatorController(ITrayBuildIndicatorView view, ITrayRunNotifier notifier, Func<bool> notificationsOn)`
  (ZORUNLU parametre — kablo unutulamasın); balon anında `notificationsOn()` TAZE okunur, false ise
  `ShowRunFinished` çağrılmaz; görünür/çıkış/gizleme fiilleri aynen sürer. MainWindow:
  `() => ShellSwitches.ShowNotifications(_uiState.Load())`.
- `SecondInstanceGate.Decide(bool activated, IUiStateStore store)`: öne getirildiyse `(false, 0)` (store okunmaz);
  getirilemediyse `(ShowBalloon: ShellSwitches.ShowNotifications(store.Load()), ExitCode: App.SecondInstanceActivationFailedExitCode)`.
  `App.OnStartup`: `new JsonUiStateStore(JsonUiStateStore.DefaultPath)` verir; balon yoksa mevcut dal gibi hemen
  `Shutdown(outcome.ExitCode)`.

**Testler (önce kırmızı):**
- `TrayBalloonOnceTests`: `With_notifications_off_the_first_close_shows_no_balloon_and_keeps_the_flag` (kapalı → false,
  `TrayBalloonShown` false, kayıt yok; açılınca ilk çağrı true).
- `TrayBuildIndicatorControllerTests`: `With_notifications_off_a_finished_run_shows_no_balloon_but_the_indicator_still_runs`
  ve `The_setting_is_read_when_the_run_finishes` (koşu sırasında kapatılırsa balon yok). Mevcut fixture ve
  `TrayIndicatorBinderTests` kurulumları yeni ctor'a `() => true` ile uyarlanır.
- `SecondInstanceGateTests`: mevcut iki test yeni imzaya; + `With_notifications_off_a_failed_activation_exits_quietly_with_its_distinct_code`
  ve `The_second_instance_reads_the_setting_from_the_state_file` (TempDir'de `JsonUiStateStore`: `ShowNotifications=false`
  → balon yok; dosya yok → balon var).
- Kaynak guard'ları: MainWindow controller'ı `ShellSwitches.ShowNotifications(_uiState.Load())` ile kurar;
  `App.OnStartup` `SecondInstanceGate.Decide`'e `new JsonUiStateStore(JsonUiStateStore.DefaultPath)` verir.

## Task 5: Çıkıştan sonra process kalmaz — gerçek motorla ölçüm testi

**Amaç:** güvenli çıkış sırasının (RequestExit → graceful Stop → drain → `ExitReady` → App.OnExit'in yaptığı
`AppShutdown.WaitForAsyncDisposal(engine, AppShutdown.DisposalTimeout)`) ardından hiçbir Supervisor ya da MSBuild
process'i kalmadığını ve uçuştaki projelerin öldürülmeden BİTTİĞİNİ gerçek Supervisor + gerçek MSBuild ile göstermek.

**Dosyalar:** `src/BuildOrchestrator.App/Services/EngineHost.cs` — test yüzeyi `internal JobObject OuterJob => _outerJob;`
(doc: testler ağacı IOCP ile izler); yeni `tests/BuildOrchestrator.Tests/App/SafeExitProcessTests.cs`.

**Test:** `[SkippableFact]`, `[Trait("Category", "MsBuild")]` (`msbuildNotFound` gelirse Skip — `KillMidBuildTests`
deseni). `LegacyFixture.CreateClassLibWithSharedBinCopy` ile 2-3 gecikmeli classlib; `SupervisorSandbox` +
`IsolatedEngineHost(TestPaths.WideStartupTimeout)`; `engine.OuterJob.AttachCompletionPort()` Build'den ÖNCE; motor
olayları bir kuyruğa, VM tek test thread'inde `OnEvent` ile beslenir. Sync → Build → ≥1 canlı gerçek `MSBuild.exe`
(IOCP NEW_PROCESS + isim süzgeci) görülünce Supervisor + canlı MSBuild handle'ları açılır → `vm.RequestExit()` →
tek graceful `StopRunCommand`, `ExitReady` henüz yok → olaylar işlenir, `ExitReady` gelir → uçuştaki projeler
`ProjectSucceededEvent` ile bitmiş olmalı → `AppShutdown.WaitForAsyncDisposal` → her handle en çok 2 sn içinde çıkmış
(0 orphan). Adı: `After_a_safe_exit_no_supervisor_or_msbuild_process_is_left`.

## Task 6: Dokümanlar

ARCHITECTURE §12.3 (tepsi, balon, × davranışı, güvenli çıkış), §13.3 (General: iki anahtarın kalıcılığı, Export
biçimi), §20 ("Four General switches are not wired yet" → yalnız Start with Windows ve Start minimized to tray), §22
kod haritası (yeni dosyalar); README General paragrafı ve tepsi/kapanış bölümü. Anlatı üslubu korunur (changelog yok),
her iddia kodda doğrulanır, bayatlayacak sayı yok. `ReleaseNotes.cs`'e dokunulmaz.
