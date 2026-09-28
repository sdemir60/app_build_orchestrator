# UI eksikleri temizlik turu (P1) — TDD dökümü

- **Tarih:** 2026-09-28 · **Taban:** `main` (`35ddc41`) · **Branch:** `chore/ui-gap-cleanup`
- **Bağlam:** `.claude/outputs/2026-09-28-12-33-ui-function-inventory.md` (§14-§16),
  `.claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md` (P1)
- Her iddia `35ddc41`'de kodda doğrulandı (satır numaraları o commit'e göredir).

## Özet

| # | Madde | Tür | Test |
|---|---|---|---|
| 1 | Proje listesi `⌄ latest` pill'i + `StickyLayerList.ResumeFollow` kaldırılır | Kaldırma | AccessibilityTests iki pill'e göre yeniden yazılır |
| 2 | `RunViewModel.ChangeRepositoryAsync` kaldırılır | Kaldırma | 5 testten 4'ü `ApplySettingsAsync` yoluna taşınır (her biri mutasyonla ayırt edici olduğu gösterilir), 1'i zaten kapsandığı için silinir |
| 3 | Tepsi menüsündeki Stop, `StopCommand.CanExecute` false iken pasif | **Davranış** | Önce KIRMIZI: `TrayMenuTests` |
| 4 | Bayat kod yorumları | Yorum | — (build uyarısız kalır) |
| 5 | README General paragrafı | Doküman | — (ARCHITECTURE §13.3 doğru, dokunulmaz) |
| 6 | Test projesindeki 11 derleme uyarısı | Test kodu | Build uyarı sayısı 0 |

**Madde 2 — test kararları**

| Test | Pinlediği | ApplySettingsAsync yolunda kapsanıyor mu | Karar |
|---|---|---|---|
| `SettingsDialogTests.Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root` | kök değişir, satırlar hollow, yeni kökte tek Sync + envanter | Evet: `Applying_settings_with_a_new_root_resets_rows_and_syncs_at_the_new_root` + `Saving_the_first_repository_root_applies_it_and_syncs_at_that_root` + taşınan envanter testi | **Sil** |
| `SettingsDialogTests.Repicking_the_current_repository_root_is_a_no_op` | aynı kök (harf farkı) → kök ve satırlar değişmez | Hayır | **Taşı** |
| `SettingsDialogTests.Choosing_a_folder_while_a_sync_is_in_flight_changes_nothing` | Sync uçuşta → kök değişmez, komut gitmez | Hayır (pull ve koşu var, Sync yok) | **Taşı** |
| `BranchInventoryTests.Changing_the_repository_re_asks_for_the_inventory_with_the_new_root` | yeni kökün envanteri istenir | Hayır | **Taşı** |
| `RunViewModelTests.A_root_change_forgets_the_last_sync_head` | kök değişince son Sync HEAD'i unutulur | Hayır | **Taşı** |

**Kapsam kararları (kullanıcıya raporlanır)**

- `IsFollowSuppressedByUser` testler için prob olarak kalır (kod ve doc'u değişmez).
- `Continue` taraması App tarafıyla sınırlı: bugünkü davranışı yanlış anlatan App yorumları düzeltilir. Motor/Core
  tarafındaki Continue/segment/snapshot anlatıları (RunCoordinator, ReadySetScheduler, RunSnapshot, RunClock,
  Supervisor/Program.cs) ve `RunViewModel.OnBuildPreview`'daki "segment 1/2" gerekçesi **dokunulmadı**: doğru
  yazılmaları o mekanizmanın bugün canlı mı ölü mü olduğunun ayrıca incelenmesini ister.
- Listede olmayan ama aynı türden iki bayat yorum da düzeltilir: `RebuildProjectAsync` özeti ("tam Rebuild ile
  aynı anlam") ve `MainWindow.ExitApplication` özeti (`dotnet build` → `MSBuild.exe`).
- Tepsi Stop'u için doküman güncellemesi yok: ARCHITECTURE/README tepsi menüsünün maddelerini anlatmıyor.
- `OsysIncrementalAcceptanceTests` yalnız derlenerek doğrulanır: koşusu gerçek OSYS çalışma ağacını derliyor.

---

## Global Constraints

- Repo `D:\Projects\Other\Apps\app_build_orchestrator`, branch `chore/ui-gap-cleanup`, yerinde çalışılır (worktree yok).
- Derleme ve test **Release** ile: `dotnet build BuildOrchestrator.slnx -c Release` ·
  `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "FullyQualifiedName~<Sınıf>"`.
  Tam süiti koşma (controller koşar).
- Kod, UI metinleri, loglar İngilizce; kod yorumları ve test doc'ları Türkçe.
- Dosya düzenleme yalnız Edit/Write ile. **YASAK:** `sed -i` (CRLF'i LF'e çevirir), PowerShell
  `Get-Content | Set-Content` (UTF-8 bozar), ters bölülü kodu heredoc'la yazmak. CRLF ve dosyanın kodlaması korunur.
- Uyarı susturma YASAK (`#pragma`, `NoWarn`, `SuppressMessage`). Build uyarısız kalmalı; yeni doc metninde
  `<see cref>` yalnız o dosyada zaten çözülen semboller için, emin değilsen `<c>` kullan.
- `App/Services/ReleaseNotes.cs`'e dokunma. `[DEĞİŞEN KURAL]` tarihçe notlarına dokunma.
- Kopya YASAK / tek doğruluk kaynağı (CLAUDE.md). Test ortak fixture/host'ları tekrar yazılmaz.
- Task başına commit; yalnız task'ın dosyaları `git add` ile eklenir. Commit mesajı ASCII Türkçe
  (`chore(list): ...` biçimi), mesaj dosyaya yazılıp `git commit -F <dosya>` ile atılır. Mesaja
  `Co-Authored-By` / "Generated with" satırı EKLENMEZ.
- Subagent dispatch etmezsin.

---

### Task 1: Proje listesindeki `⌄ latest` pill'ini kaldır

Karar (kullanıcı): listede gerek yok. Pill hiç görünür olmuyor, tıklaması bağlı değil. Konsol ve event stream
pill'lerine dokunulmaz.

Değişiklikler:

1. `src/BuildOrchestrator.App/ShellRoot.xaml:187` — `<controls:LatestPill x:Name="PART_ProjectsPill" Visibility="Collapsed" />` satırını sil.
2. `src/BuildOrchestrator.App/ShellRoot.xaml.cs:48-50` — iki satırlık `// [A13/T5] \`⌄ latest\` pill'i AYNI ilkeyle adlanır ...` yorumunu ve `PART_ProjectsPill.AccessibleName = AccessibilityNames.LatestProjects;` satırını sil.
3. `src/BuildOrchestrator.App/AccessibilityNames.cs:182-186` — `LatestProjects` sabitini sil; üstteki yorumda
   `bu yüzden üç ayrı metin vardır` → `bu yüzden iki ayrı metin vardır (konsol, event stream)`.
4. `src/BuildOrchestrator.App/Controls/StickyLayerList.xaml.cs:227-229` — `ResumeFollow` (summary + metot) sil.
   `IsFollowSuppressedByUser` (kod + doc) AYNEN kalır.
5. `src/BuildOrchestrator.App/Controls/LatestPill.xaml.cs:35` — `gidildiğini (projeler / konsol / event stream) yalnız host bilir` → `gidildiğini (konsol / event stream) yalnız host bilir`.
6. `tests/BuildOrchestrator.Tests/App/AccessibilityTests.cs:169-186` — `Each_latest_pill_names_the_stream_it_jumps_to`
   iki pill için yeniden yazılır: `projects` değişkeni ve assert'i kalkar; `LatestConsole` ve `LatestEvents`
   assert'leri kalır; son assert `Assert.Equal(2, new[] { console, events }.Distinct(StringComparer.Ordinal).Count());`.
   Doc: "üç panelde ORTAK" → "iki panelde (konsol, event stream) ORTAK"; sonuna
   `<para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-28]</b> Eski iddia: üç pill (proje listesi dahil) üç farklı ad taşır. Proje listesinin pill'i kaldırıldı — hiç görünür olmuyordu ve tıklaması bağlı değildi; kural kalan iki pill için aynıdır.</para>` eklenir.
7. `AccessibilityTests.cs:467` — beklenen adlar listesinden `AccessibilityNames.LatestProjects,  // n3` satırı çıkar;
   `// n3` işareti `AccessibilityNames.LatestConsole,` satırına taşınır.

Doğrulama:

- `grep -rn "PART_ProjectsPill\|LatestProjects\|ResumeFollow" src tests --include=*.cs --include=*.xaml` (obj/bin hariç) → sıfır sonuç.
- Tarama testi `MinimumScannedSurfaces` (40) eşiğini hâlâ geçmeli. Geçmezse eşiği DEĞİŞTİRME, NEEDS_CONTEXT ile dön.
- Koş: `--filter "FullyQualifiedName~AccessibilityTests|FullyQualifiedName~StickyLayer|FullyQualifiedName~FrontierFollow|FullyQualifiedName~ShellRoot"` → yeşil.

Commit: `chore(list): proje listesindeki gorunmeyen latest pill ve cagrilmayan ResumeFollow kaldirildi`

---

### Task 2: `RunViewModel.ChangeRepositoryAsync`'i kaldır

Üretimde çağıranı yok (kaldırılmış "Choose Folder" yolundan kalma). Kök yalnız Settings → Save →
`ApplySettingsAsync` ile uygulanır. Kapsam kaybı olmaz: taşınan her test mutasyonla ayırt edici gösterilir.

**A. Üretim kodu**

1. `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs:242-253` — `ChangeRepositoryAsync` (summary + metot) sil.
2. Aynı dosya `ApplySettingsAsync` summary (a) paragrafı (~195-196): ` (<see cref="ChangeRepositoryAsync"/>` +
   `de aynı kapıda no-op'tur)` parantezini sil; cümle `... ikinci bir Sync de çift Sync olurdu. Bekleyen GERÇEK ...` olarak akar.
   (Aynı summary'deki `Retry/Continue` Task 4'ün işidir, dokunma.)
3. `SyncAfterRootChangeAsync` summary'si (~255-263) şununla değişir:
   ```
   /// <summary>[spec 2026-09-18 §6.2] Settings Save'in Sync'i (<see cref="SyncMode.Appended"/>). Kök GERÇEKTEN
   /// değiştiyse plan yüzeyi önce boşaltılır (<see cref="ClearPlanSurface"/>): Sync artık listeyi kendisi
   /// boşaltmaz ve eski reponun (kararsız) satırları yeni reponun topolojisi gelene dek ekranda kalırdı.
   /// <para>Tek çağıran <see cref="ApplySettingsAsync"/>'tir: bayrağı <see cref="ApplyRepositoryRoot"/>'un
   /// sonucundan geçer (katman-only bir Save'de <c>false</c> — yüzey boşalmaz) ve motor erişilemezken buraya
   /// hiç gelmez: o yolda satırlar yalnız kararları düşmüş hâlde kalır (<see cref="ResetRowsToHollow"/>), çünkü
   /// onları geri getirecek bir topoloji gelmeyecektir.</para></summary>
   ```
4. `ApplyRepositoryRoot` summary (~272-273): `Sync GÖNDERMEZ — o kararı çağıran verir (Choose Folder hemen, Settings Save'de tek Sync içinde). İki yolun ortak adımı burada TEK yerdedir (kopya yasağı).` →
   `Sync GÖNDERMEZ — Sync'i çağıran (<see cref="ApplySettingsAsync"/>) Save'in TEK Sync'i içinde gönderir.`
5. `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` `SyncCoreAsync` summary (~1186-1187):
   `ve Settings Save / kök değişimi` → `ve Settings Save (kök değişimi dahil)`.
6. Aynı metodun gövdesindeki yorum (~1261-1263): `ve repo'yu değiştiren HER yol (ilk klasör seçimi / Choose Folder → ChangeRepositoryAsync, Settings→Save → ApplySettingsAsync) zaten buraya iner;` →
   `ve repo'yu değiştiren tek yol (Settings → Save → ApplySettingsAsync; ilk kurulum dahil) zaten buraya iner;`
7. `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs:540`: `Settings Save'in ve Choose Folder'ın kapısı;` → `Settings Save'in kapısı;`
8. `src/BuildOrchestrator.App/MainWindow.xaml.cs:156-157`: `komut göndermez (ChangeRepositoryAsync DEĞİL).` → `komut göndermez (Settings Save'in kök değişimi gibi Sync başlatmaz).`
9. `MainWindow.xaml.cs:1033-1038` notunun son iki cümlesi şöyle olur (ilk iki cümle aynen kalır):
   ```
   // düğmesine geçti. Kalıcı durumdan gelen kök DOĞRUDAN RootPath set'iyle seed edilir (yukarıda, D7 M3 —
   // seed'in kendisi komut göndermez; açılışın Sync'i motor hazır olunca RunViewModel.OnEngineReady'den gider).
   ```
   (`RunViewModel.ChangeRepositoryAsync'in üretimde çağıranı YOKTUR, yalnız testlerden sürülür.` cümlesi gider.)
10. `MainWindow.xaml.cs:1077`: `RootPath değişimi (ilk klasör seçimi, Settings→Change, Choose Folder — hepsi RootPath'i set eder)` →
    `RootPath değişimi (Settings → Save ile uygulanan kök, ilk kurulum dahil)`.

**B. Testler** (hepsi `new EngineHost(TestPaths.SupervisorExe)` başlatılmadan kurulan VM'le, mevcut desende)

1. `BranchInventoryTests.Changing_the_repository_re_asks_for_the_inventory_with_the_new_root` → **taşı**, ad:
   `Saving_a_new_repository_root_re_asks_for_the_inventory_with_the_new_root`. Gövde:
   `await vm.ApplySettingsAsync([], @"D:\other-repo", []);` → `Assert.Single(sent.OfType<ListBranchesCommand>())`,
   `RootPath == @"D:\other-repo"`. Summary: "Repo değişince liste BAYATLAR — Settings Save'in kök değişimi yeni kökün envanterini ister (yeni kökün yoluyla)."
   Aynı dosya `:45-46` summary'si: `ilk repo seçimi (<c>ChangeRepositoryAsync</c> → <c>SyncAsync</c>), Settings→Save ve elle Sync hepsi buradan akar.` →
   `ilk repo seçimi ve kök değişimi (Settings → Save → <c>ApplySettingsAsync</c>), açılış ve elle Sync hepsi buradan akar.`
2. `RunViewModelTests.A_root_change_forgets_the_last_sync_head` → **taşı** (ad aynı kalabilir):
   `await vm.ApplySettingsAsync([], @"D:\other-repo", []);` — üç `Assert.Null` aynen. Son assert yorumu:
   `// motor başlamadı: Save'in Sync'i gönderilemedi`. Summary'e "(Settings Save'in kök değişimi)" ekle.
3. `SettingsDialogTests.Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root` → **sil** (gerekçe üstteki tabloda).
4. `SettingsDialogTests.Repicking_the_current_repository_root_is_a_no_op` → **taşı**, ad:
   `Saving_the_current_repository_root_in_another_case_keeps_the_root_and_the_rows`. `{ RootPath = @"D:\repo" }` +
   `ProjectStartedEvent` ile Started satır (mevcut kurulum), `sent` liste olarak toplanır,
   `await run.ApplySettingsAsync([], @"d:\REPO", []);` → `RootPath == @"D:\repo"` (harf durumu korunur), satır hâlâ
   `Started` (hollow yok), konsolda `Repository root →` YOK, ve Save kuralı gereği TEK `SyncWorkspaceCommand` ESKİ
   yazımla (`@"D:\repo"`) gider. Summary: aynı kökü farklı harf durumuyla kaydetmek bir kök değişimi DEĞİLDİR
   (`IsRepositoryChange`, Windows yolu); Save yine TEK Sync gönderir (`Applying_settings_sends_one_sync_that_carries_the_new_layer_patterns`), ama kök ve satırlar yerinde kalır.
5. `SettingsDialogTests.Choosing_a_folder_while_a_sync_is_in_flight_changes_nothing` → **taşı**, ad:
   `Applying_settings_while_a_sync_is_in_flight_defers_the_repository_change_and_sends_no_sync`. Kurulum aynı
   (`SyncStartedEvent` → `SyncBusy` ön-koşulu), `patterns` ile
   `await run.ApplySettingsAsync(patterns, @"D:\new\repo", []);` → `Assert.Same(patterns, run.LayerPatterns)`,
   `RootPath == @"D:\repo"`, `Assert.Empty(sent)`,
   `Assert.Contains(RunViewModel.RepositoryChangeDeferredLine(runInFlight: false), run.GetRunDocumentText())`.
   Summary: pull ve koşu testlerinin eşi — Sync uçuştayken ikinci Sync çift Sync olurdu.
6. `RunViewModelStateTests.cs:77-80` yorum bloğu şununla değişir:
   ```
   [Fact] // [E6/D7 M3] Açılış seed'i = DOĞRUDAN RootPath set (Empty→Boot) — Settings Save'in kök değişimi DEĞİL: kayıtlı
   // repo seed edilir, repo BİLİNİR ama seed'in kendisi hiçbir komut GÖNDERMEZ. Açılışın Sync'i motor hazır olunca gider
   // (RunViewModel.OnEngineReady — RunViewModelTests.The_first_engine_ready_syncs_with_the_transcript); Save'in kök
   // değişimi ise TEK Sync gönderir (SettingsDialogTests.Applying_settings_with_a_new_root_resets_rows_and_syncs_at_the_new_root).
   ```
   `:88` yorumu `(doğrudan set — ChangeRepositoryAsync DEĞİL)` → `(doğrudan set — Settings Save DEĞİL)`;
   `:91` yorumu `(seed-but-idle)` → `(seed komut göndermez)`.
7. `StartupPathTests.cs:90` başlığı `t1: autostart SESSİZ başlar (Sync YOK)` → `t1: açılış seed'i motora komut göndermez`.
   `:95-98` yorumu (`[t1 · asıl değer] Autostart yolunun tek anlamlı riski budur ... DEĞİLDİR.`) şununla değişir:
   ```
   // [t1] Açılış repo'yu HATIRLAR (MainWindow.xaml.cs `_vm.RootPath = repo`) ve seed'in KENDİSİ motora hiçbir
   // komut göndermez — doğrudan RootPath set'i yalnız Empty→Boot sürer; Sync tetikleyen yol (Settings Save'in
   // kök değişimi) DEĞİLDİR. Açılışın Sync'i motor hazır olunca RunViewModel.OnEngineReady'den gider (bu testte
   // motor başlatılmaz; o kural RunViewModelTests.The_first_engine_ready_syncs_with_the_transcript'te pinli).
   ```
   Sonraki `// Yol ÜRETİMDEKİ yoldur: ...` satırları aynen kalır.

**C. Mutasyon kanıtı** (taşınan her test için; her mutasyonu geri al, report'a çıktıyı yaz)

| Taşınan test | Geçici mutasyon | Beklenen |
|---|---|---|
| B1 envanter | `SyncCoreAsync`'teki `ListBranchesCommand` gönderimini yoruma al | KIRMIZI |
| B2 HEAD unutma | `ApplyRepositoryRoot`'taki `ForgetLastSync();` satırını yoruma al | KIRMIZI |
| B4 aynı kök | `IsRepositoryChange`'te `OrdinalIgnoreCase` → `Ordinal` | KIRMIZI |
| B5 Sync uçuşta | `ApplySettingsAsync`'te `if (!WorkspaceIdle)` → `if (IsMidRunLocked)` | KIRMIZI |

Doğrulama: `grep -rn "ChangeRepositoryAsync" src tests` (obj/bin hariç) → sıfır sonuç. Koş:
`--filter "FullyQualifiedName~BranchInventoryTests|FullyQualifiedName~SettingsDialogTests|FullyQualifiedName~RunViewModelTests|FullyQualifiedName~RunViewModelStateTests|FullyQualifiedName~StartupPathTests"` → yeşil.

Commit: `chore(settings): kullanilmayan ChangeRepositoryAsync kaldirildi, testleri Settings Save yoluna tasindi`

---

### Task 3: Tepsi menüsündeki Stop, durdurulacak derleme yokken pasif

Bugün `AppTrayIcon` Stop maddesini her zaman etkin kurar; `MainWindow.OnSourceInitialized:1122` tıklamada
`StopCommand.CanExecute`'u sorar, derleme yokken hiçbir şey olmaz. Hedef: madde `RunViewModel.StopCommand`'a
bağlı olsun — etkinliği `CanExecute`'tan gelir (`CanStop = (IsRunning || IsStarting) && Phase != Stopping`),
ActionBar'daki Stop düğmesiyle AYNI kaynak. Uygulamada özel MenuItem stili yok; WPF varsayılan şablonu pasif
maddeyi gri çizer.

Sıra (kırmızı test kuralı):

1. **Davranışı koruyan seam (refactor).** `AppTrayIcon`:
   - `internal static ContextMenu CreateMenu(ICommand stop, Action exit)` eklenir; ctor menüyü onunla kurar.
   - Bu adımda Stop maddesi BUGÜNKÜ gibi davranır: her zaman etkin, `Click` → `if (stop.CanExecute(null)) stop.Execute(null);`
     (MainWindow'daki kapı buraya taşınır). Exit aynen.
   - ctor `AppTrayIcon(ICommand stopCommand)` olur; `StopRequested` olayı ve doc'u silinir.
   - `MainWindow.xaml.cs:1120-1122`: `_tray = new AppTrayIcon(_vm.StopCommand);`, `StopRequested` satırı silinir.
   - `tests/.../TrayBalloonProbeTests.cs:45` ctor çağrısı derlenecek şekilde güncellenir (ör. `new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }, () => false)`).
   - Build yeşil, `--filter "FullyQualifiedName~Tray"` yeşil.
2. **KIRMIZI.** Yeni `tests/BuildOrchestrator.Tests/App/TrayMenuTests.cs` (`[StaFact]`; VM:
   `new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" }`,
   motor başlatılmaz; menü `AppTrayIcon.CreateMenu(vm.StopCommand, () => { })`, Stop maddesi `Header == "Stop"` ile bulunur):
   - `Tray_stop_is_disabled_while_no_build_can_be_stopped` — ön-koşul `Assert.False(vm.StopCommand.CanExecute(null))`, sonra `Assert.False(stop.IsEnabled)`.
   - `Tray_stop_follows_the_stop_command_through_a_run` — `vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0))`
     (Phase Running) → `Assert.True(stop.IsEnabled)`; `vm.Phase = AppPhase.Stopping` → `Assert.False(stop.IsEnabled)`.
   - Seam üzerinde ikisi de KIRMIZI olmalı; çıktıyı report'a yaz. Kırmızı değilse test yanlıştır — düzelt.
3. **Fix.** Stop maddesi `new MenuItem { Header = "Stop", Command = stop }` olur, `Click` işleyicisi kalkar. İki test YEŞİL.
4. **Tıklama pini** (fix'ten sonra yeşil): `Invoking_the_tray_stop_item_stops_the_run` — koşu
   `vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0))` ile kurulur (yalnız `IsStarting = true`
   yetmez: `StopAsync` `_currentRunId` yokken hiçbir şey göndermez), `vm.DebugOnCommandSent` ile komutlar toplanır,
   madde UIA ile çağrılır (`((IInvokeProvider)new MenuItemAutomationPeer(stop)).Invoke()`),
   `Assert.Single(sent.OfType<StopRunCommand>())` → `RunId == "r1"`, `Kind == StopKind.Graceful`.
   UIA çağrısı headless çalışmıyorsa `Assert.Same(vm.StopCommand, stop.Command)`'a düş ve bunu report'ta söyle.
5. **Kablo pini** (kaynak): `MainWindow.xaml.cs`'te `new AppTrayIcon(_vm.StopCommand)` tam bir kez geçer —
   `TrayIndicatorBinderTests.Clicking_a_balloon_takes_the_same_restore_path_as_the_tray_icon` deseni
   (`RepoPaths.AppSrcRoot`, regex, `Assert.Single`), doc'unda neden kaynağa bakıldığı (TaskbarIcon headless kurulamaz).
6. `AppTrayIcon` sınıf özeti: "olayları dışarı verir (RestoreRequested/StopRequested/ExitRequested)" →
   RestoreRequested/ExitRequested olaydır; Stop maddesi verilen komuta bağlıdır ve etkinliği CanExecute'tan gelir
   (ActionBar'daki Stop düğmesiyle aynı kaynak — ikinci bir kapı yazılmaz).

Doğrulama: `--filter "FullyQualifiedName~Tray"` yeşil; build uyarısız.

Commit(ler): seam `refactor(tray): tepsi menusu tek fabrikadan kuruluyor, Stop komutu disaridan veriliyor`;
fix + testler `fix(tray): tepsi menusundeki Stop durdurulacak derleme yokken pasif`.

---

### Task 4: Bayat kod yorumlarını koda göre düzelt (davranış değişmez)

1. **Tek proje Build** — `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1151-1155` `BuildProjectAsync` summary'si:
   ```
   /// <summary>[tek proje · design v1.11.0 §3.8] Satırın play düğmesi ve ⋯ menüsünün <i>Build</i> maddesi:
   /// YALNIZ o projeyi derler — bağımlılıklar derlenmez, kapsam dışına dokunulmaz. Hedef güncel olsa da
   /// derlenir: tek projelik kapsamda incremental karar sorulmaz (<c>Core/Planning/ProjectRunScope</c>,
   /// <c>WillBuild = true</c>; ARCHITECTURE §8.1). <see cref="RebuildProjectCommand"/>'dan farkı MSBuild
   /// hedefidir (<c>-t:Build</c> / <c>-t:Rebuild</c>). Parametre satırın kimliğidir; kapı tam koşununkiyle AYNI
   /// (<see cref="CanRebuildOrRetry"/>) + bir hedef: uçuşta bir koşu varken hiçbir satırdan ikinci bir koşu
   /// başlatılamaz.</summary>
   ```
   `:1159-1160` `RebuildProjectAsync` summary'si:
   ```
   /// <summary>[tek proje] ⋯ menüsünün <i>Rebuild</i> maddesi: aynı kapsam, MSBuild'in kendi Rebuild'i
   /// (<c>-t:Rebuild</c> — önce temizler, sonra derler). Alt bardaki Rebuild'den farklıdır: orada "cache'i yok
   /// say" demektir ve proje başına <c>-t:Build</c> koşar (ARCHITECTURE §8.1).</summary>
   ```
   `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs:123-124` `ScopeProjectId` doc'unda
   `Hedef tam koşuyla AYNI motor yolundan geçer — Build modunda incremental kural, Rebuild'de koşulsuz.` →
   `Hedef tam koşuyla AYNI motor yolundan geçer ama incremental karar sorulmaz: Build'de de Rebuild'de de güncel olsa da derlenir (<c>WillBuild = true</c>, ARCHITECTURE §8.1); ikisini MSBuild hedefi ayırır (<c>-t:Build</c> / <c>-t:Rebuild</c>). Clean'de hedefte <c>-t:Clean</c> koşar.`
2. **Settings General** — `src/BuildOrchestrator.App/Views/SettingsDialog.xaml:158-159`:
   `Yalnız Pull before build davranışa bağlıdır; diğer dört anahtar henüz yalnız taslaktır (SettingsDraftViewModel.GeneralGroups).` →
   `Pull before build ve Stash and switch branches davranışa bağlıdır; diğer dört anahtar (Start with Windows, Start minimized to tray, Close to tray, Show notifications) henüz yalnız taslaktır (SettingsDraftViewModel.GeneralGroups).`
3. **UiStateStore** — `src/BuildOrchestrator.App/Shell/UiStateStore.cs:32` başlığı →
   `// ---- İş akışı tercihleri — Settings ve action bar yazar (SeenVersion'ı What's new yazar; Autostart'ı yazan UI henüz yok) ----`.
   `:44-47` `SeenVersion` summary'si:
   ```
   /// <summary>[design v1.13.0] Kullanıcının What's new diyaloğunda (<c>NotesDialog</c>) EN SON gördüğü sürüm.
   /// Çalışan sürümden farklıysa başlık çubuğundaki ✨ What's new düğmesinde 5px amber bir nokta durur; diyalog
   /// açıldığı anda bu değer yazılır ve nokta söner. Prototipteki <c>localStorage delta-bo-seen-version-v1</c>'in
   /// karşılığıdır — kalıcı durumun tek yeri burasıdır.</summary>
   ```
4. **"ileride event stream"** — `src/BuildOrchestrator.App/Controls/LatestPill.xaml.cs:10`:
   `konsol + (ileride) event stream ORTAK kullanır` → `konsol ve event stream ORTAK kullanır`.
   `src/BuildOrchestrator.App/Controls/BottomAnchorBehavior.cs:6-7`: `(ConsoleView'ın AvalonEdit TextEditor'ı, ileride Event Stream'in ScrollViewer'ı)` →
   `(ConsoleView'ın AvalonEdit TextEditor'ı ve EventStreamView'ın ScrollViewer'ı)`.
   `:10-12`: `bu sınıf ikisine de (ve ileride Event Stream'in host'una da) duck-typing'siz, WPF'e dokunmadan hizmet eder — bu yüzden testleri <c>[Fact]</c> (STA gerekmez), yalnız gerçek kablaj (ConsoleView) <c>[StaFact]</c>'tir.` →
   `bu sınıf ikisine de duck-typing'siz, WPF'e dokunmadan hizmet eder — bu yüzden testleri <c>[Fact]</c> (STA gerekmez), yalnız gerçek kablajlar (ConsoleView, EventStreamView) <c>[StaFact]</c>'tir.`
5. **"Açılışta otomatik Sync yok"** —
   `src/BuildOrchestrator.App/App.xaml.cs:125-126`:
   ```
   // [E2/T16] Autostart argümanıyla açıldıysa pencere GÖSTERİLMEDEN tepside başlar; aksi halde bugünkü davranış
   // (normal göster). İki yolda da açılışın Sync'i motor hazır olunca koşar (RunViewModel.OnEngineReady).
   // Karar yukarıdaki TEK dikişten gelir.
   ```
   `src/BuildOrchestrator.App/MainWindow.xaml.cs:1226-1228` (`StartInTray` summary'sinin son kısmı):
   `Oto-Sync YOKtur (normal açılışta da yok — [D7 M3] RepositoryRoot açılışta SEED edilir/hatırlanır ama SEED-BUT-IDLE: doğrudan RootPath set'i yalnız Empty→Boot sürer, Sync tetiklemez; autostart yolu bugünkü "temiz" başlangıcı tepside korur).` →
   `Açılışın Sync'i normal açılıştaki gibi motor hazır olunca koşar (<c>RunViewModel.OnEngineReady</c>); RepositoryRoot'un seed'i ([D7 M3]) kendisi komut göndermez.`
   `src/BuildOrchestrator.App/Shell/StartupArgs.cs:12`: `Oto-Sync YOKtur — normal açılışta da yok.` →
   `Açılışın Sync'i normal açılıştaki gibi motor hazır olunca koşar (<c>RunViewModel.OnEngineReady</c>).`
6. **Kaldırılmış Continue** (App):
   - `MainWindow.xaml.cs:307` `(IsStarting/IsRunning/CanContinue)` → `(IsStarting/IsRunning)`.
   - `:397` `// çıplak F5 → Stop/Continue/Build (duruma göre)` → `// çıplak F5 → Stop/Build (duruma göre)`.
   - `:407` `Çıplak F5: koşarken → Stop, stopped'ta → Continue, aksi → Build (v7 K6).` → `Çıplak F5: koşarken → Stop, koşmayan her durumda → Build (v7 K6).`
   - `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs:568` `Aksi halde split-button (Build/Continue).` → `Aksi halde split-button (Build).`
   - `RunViewModel.cs:634` `Stop/Continue butonları` → `Stop/Build butonları`.
   - `RunViewModel.cs:720` `Sync/Build/Rebuild/Retry/Continue bu durumda` → `Sync/Build/Rebuild bu durumda`.
   - `RunViewModel.ActionBar.cs:210` `Sync/Build/Rebuild/Retry/Continue düğmelerinin` → `Sync/Build/Rebuild düğmelerinin`.
   - `RunViewModel.cs:894` ` <b>Continue temizlemez</b> (önceki segmentin log/proje sonuçlarını korur).` cümlesini sil (her çağıran `clearBuffers: true` geçer).
   - `RunViewModel.cs:1851` `RebuildAsync/ContinueAsync'de ERKEN temizlemek YANLIŞ` → `gönderim anında (BeginRunAsync) ERKEN temizlemek YANLIŞ`.
   - `RunViewModel.cs:2254` `run genelinde (Continue segmentleri dahil) kümülatif özet` → `run genelinde kümülatif özet`.
   - `src/BuildOrchestrator.App/Controls/SplitButton.cs:32` `ikon + "Build"/"Continue")` → `ikon + "Build")`.
   - `src/BuildOrchestrator.App/ViewModels/AppPhase.cs:42` `(henüz durmadı, Continue erişilebilir değil)` → `(henüz durmadı)`.
   - `src/BuildOrchestrator.App/Views/BuildMenu.xaml:7-8`: `Maddeler koşulludur (Continue yalnız stopped; Retry yalnız failed>0) ve kod-tarafı (BuildMenu.xaml.cs) VM durumundan kurulur; F5 rozeti stopped'ta Build'den Continue'ya taşınır.` →
     `Maddeler koşulsuzdur — her fazda aynı üç madde (Build, Rebuild, Clean) — ve kod-tarafında (BuildMenu.xaml.cs) kurulur; F5 rozeti her fazda Build'dedir.`
   - `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs:20` `"Build started"/"Continue" anlatı satırı` → `koşunun başlangıç anlatı satırı ("Build started" ailesi)`;
     `:22` `(Continue re-emit'te çift satır olmaz)` → `(satır koşu başına bir kez yazılır)`;
     `:125` `"Build started"/"Continue" satırını` → `başlangıç satırını ("Build started" ailesi)`;
     `:231` `BuildStarted/Continue satırlarıyla AYNI` → `BuildStarted satırıyla AYNI`.
   - DOKUNULMAZ (doğru tarihçe ya da başka kavram): `RunViewModel.cs:1367`, `KeyboardShortcuts.cs:112`,
     `ActionBar.xaml.cs:582`, `RibbonText.cs:195`, `IpcMessages.cs:486` (CycleRoundDecision), `StickyLayerList.xaml:86`
     (WPF enum), `RunViewModel.OnBuildPreview` segment gerekçesi, Supervisor/Core'daki tüm Continue anlatıları.
7. **Ek:** `MainWindow.xaml.cs:1240` `Supervisor ve tüm <c>dotnet build</c> child'ları.` → `Supervisor ve tüm <c>MSBuild.exe</c> child'ları.`

Doğrulama: `dotnet build BuildOrchestrator.slnx -c Release` → 0 hata, src'de 0 uyarı. Davranış değişmediği için test
koşusu gerekmez; yine de `--filter "FullyQualifiedName~RunViewModelTests"` yeşil kalmalı.

Commit: `docs(comments): bayat yorumlar koda gore duzeltildi (tek proje Build, Continue, acilis Sync, General, SeenVersion)`

---

### Task 5: README → Settings adımındaki General paragrafı

`README.md:142-144` şununla değişir:

```
   **General** holds switches in four groups — Startup, Build, Branches and Notifications. *Pull before build*
   (see step 4) and *Stash and switch branches* (see step 3) are the ones that work today; *Start with Windows*,
   *Start minimized to tray*, *Close to tray* and *Show notifications* are shown but not wired yet, are not saved,
   and reset whenever the dialog opens.
```

ARCHITECTURE §13.3 (`:2956-2966`) ve §20 (`:4817-4819`) zaten doğru: dokunulmaz.

Commit: `docs(readme): General paragrafi dort grup ve iki bagli anahtarla guncellendi`

---

### Task 6: Test projesindeki 11 derleme uyarısını temizle (susturma YASAK)

| Uyarı | Yer | Düzeltme |
|---|---|---|
| CS0028 | `Planning/ExternalLayerTests.cs:19` | `Main(...)` yardımcısı `MainRepo(...)` olur; dosyadaki tüm çağrılar (`External` içindeki dahil) güncellenir |
| CS8600 + CS8602 | `App/ActionBarTests.cs:929` | `((StackPanel)bar.Split.PrimaryContent)` → `Assert.IsType<StackPanel>(bar.Split.PrimaryContent)` |
| CS0219 | `App/RunViewModelStateTests.cs:1065` | kullanılmayan `retryChanged` değişkeni kalkar (`bool buildChanged = false, rebuildChanged = false;`) |
| CS0067 | `App/DsControlTemplateTests.cs:199` | `public event EventHandler? CanExecuteChanged { add { } remove { } }` (hiç değişmeyen komut) |
| xUnit2013 | `App/AboutDialogTests.cs:286` | `Assert.Equal(1, Regex.Matches(...).Count)` → `Assert.Single(Regex.Matches(...))` |
| xUnit2029 | `App/NotesDialogTests.cs:288` | `Assert.Empty(X.OfType<TextBlock>().Where(t => ...))` → `Assert.DoesNotContain(X.OfType<TextBlock>(), t => ...)` |
| xUnit2029 ×2 | `Integration/OsysIncrementalAcceptanceTests.cs:287-288` | `Assert.Empty(builtOutside.Where(p))` → `Assert.DoesNotContain(builtOutside, p)` (yorumlar `// (b)`, `// (c)` kalır) |
| xUnit2031 | `Supervisor/OptimizeDispatchTests.cs:169` | `Assert.Single(all.OfType<ErrorEvent>().Where(e => e.Code == "optimizeRejected"))` → `Assert.Single(all.OfType<ErrorEvent>(), e => e.Code == "optimizeRejected")` |
| xUnit1031 | `App/EngineHostTests.cs:48` | `readyEvent.Task.Result.Pid` → `(await readyEvent.Task).Pid` |

Doğrulama: `dotnet build BuildOrchestrator.slnx -c Release --no-incremental` → **0 uyarı, 0 hata**. Koş:
`--filter "FullyQualifiedName~ExternalLayerTests|FullyQualifiedName~ActionBarTests|FullyQualifiedName~RunViewModelStateTests|FullyQualifiedName~DsControlTemplateTests|FullyQualifiedName~AboutDialogTests|FullyQualifiedName~NotesDialogTests|FullyQualifiedName~OptimizeDispatchTests|FullyQualifiedName~EngineHostTests"` → yeşil.
`OsysIncrementalAcceptanceTests` (Category=Acceptance) KOŞULMAZ — gerçek OSYS ağacını derler; yalnız derlenmesi yeter.

Commit: `test: test projesindeki 11 derleme uyarisi kod duzeltilerek temizlendi`
